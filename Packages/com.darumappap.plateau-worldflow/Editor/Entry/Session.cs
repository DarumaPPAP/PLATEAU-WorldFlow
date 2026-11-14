using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DarumaPPAP.PlateauWorldFlow.Previewing;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
#if WORLDFLOW_PLATEAU_4_3_0
using DarumaPPAP.PlateauWorldFlow.Plateau;
#endif

namespace DarumaPPAP.PlateauWorldFlow.Entry
{
    /// <summary>
    /// 同じUnity Editorに接続する両Adapterの要求を直列化し、読取結果と後続操作の参照を共有する。
    /// </summary>
    internal sealed class Session
    {
        // 呼び出し単位でインスタンスを作るとAdapter間でPlanとPreviewを参照できない。
        internal static Session Current { get; } = new Session(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")));
        private const string LocalReference = "approved-local";
        private const string RemoteReference = "plateau-public";
        private readonly string _project;
        private readonly string _setup;
        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1, 1);
        private readonly PreviewSession _previews;
        private readonly Dictionary<string, CoverageSnapshot> _coverage = new Dictionary<string, CoverageSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTimeOffset> _usedIds = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        internal Session(string project)
        {
            _project = project;
            _setup = Path.Combine(project, "Library", "PLATEAUWorldFlow", "Setup");
            _previews = new PreviewSession(project);
        }

        /// <summary>
        /// Project識別を照合し、外部の情報取得またはCoreの読取操作を実行する。
        /// </summary>
        internal async Task<string> ExecuteAsync(string json, CancellationToken cancellationToken)
        {
            if (json == null || json.Length > 32768)
            {
                throw new FormatException("Invalid request size.");
            }

            await _queue.WaitAsync(cancellationToken);
            try
            {
                string identity = ReadSetup("project-id", 64);
                if (!Guid.TryParseExact(identity, "N", out _))
                {
                    throw new InvalidOperationException("Project identity unavailable.");
                }

                string fingerprint = Digests.Sha256(Json.Utf8.GetBytes(_project + "\n" + identity));
                var coordinator = new Coordinator(fingerprint);
                var request = Json.Parse(json);
                string operation = Json.Field<string>(request, "operation");
                string requestId = Json.Field<string>(request, "requestId");
                var input = Json.Field<Dictionary<string, object>>(request, "input");
                var now = DateTimeOffset.UtcNow;
                string snapshotId = Json.Field<string>(input, "coverageSnapshotId");
                _coverage.TryGetValue(snapshotId ?? "", out var snapshot);

                // 副作用のない最初の応答で、エンベロープとProjectの照合を完了する。
                string preflight = coordinator.Execute(json, null, now, snapshot, out _, cancellationToken);
                if (Json.Field<string>(request, "projectFingerprint") != fingerprint)
                {
                    return preflight;
                }

                foreach (var expired in _usedIds.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                {
                    _usedIds.Remove(expired);
                }

                if (_usedIds.ContainsKey(requestId))
                {
                    return Failure(preflight, "FAILED", "INVALID_INPUT", "input", false);
                }

                if (_usedIds.Count >= 512)
                {
                    return Failure(preflight, "BLOCKED", "LIMIT_EXCEEDED", "input", true);
                }

                _usedIds.Add(requestId, now.AddMinutes(6));
                bool isQuery = operation == "datasets.list" || operation == "coverage.inspect";
                if (isQuery && Json.Field<string>(Json.Parse(preflight), "status") == "UNKNOWN")
                {
                    return await QueryAsync(json, preflight, coordinator, operation, input, cancellationToken);
                }

                return coordinator.Execute(json, null, now, snapshot, out _, cancellationToken, _previews.Remember, ExecuteExtension);
            }
            finally
            {
                _queue.Release();
            }
        }

        private async Task<string> QueryAsync(string json, string preflight, Coordinator coordinator, string operation, Dictionary<string, object> input, CancellationToken cancellationToken)
        {
            string source = Json.Field<string>(input, "source");
            string reference = Json.Field<string>(input, "sourceReference");
            bool local = source == "LOCAL";
            // 任意URL、パス、Tokenを照会先として解釈しない。
            if (reference != (local ? LocalReference : RemoteReference)) return Failure(preflight, "BLOCKED", "SOURCE_NOT_ALLOWED", "sourceReference", false);

#if WORLDFLOW_PLATEAU_4_3_0
            var requested = Json.Field<object[]>(input, "gridCodes")?.Cast<string>().ToArray();
            string datasetId = Json.Field<string>(input, "datasetId");
            int max = Json.Field<int>(input, "maxResults");
            bool list = operation == "datasets.list";
            Catalog catalog;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if (local)
                {
                    string root = Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ROOT");
                    string id = Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ID");
                    // OS側の許可済み設定がなければ、ファイルシステムの照会を開始しない。
                    if (!WireValues.Id(id) || string.IsNullOrEmpty(root)) return Failure(preflight, "BLOCKED", "SOURCE_NOT_ALLOWED", "sourceReference", false);

                    if (!list && id != datasetId)
                    {
                        return Failure(preflight, "BLOCKED", "SOURCE_NOT_ALLOWED", "datasetId", false);
                    }

                    catalog = await Task.Run(() => new LocalSource(reference, id, root).Inspect(requested, deadline.Token), deadline.Token);
                }
                else
                {
                    if (!list && (datasetId.Length != 5 || datasetId.Any(ch => ch < '0' || ch > '9')))
                    {
                        return Failure(preflight, "FAILED", "INVALID_INPUT", "datasetId", false);
                    }

                    var remote = new RemoteSource();
                    catalog = list ? await remote.ListAsync(max, deadline.Token) : await remote.InspectAsync(datasetId, requested, deadline.Token);
                }

                deadline.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(preflight, "UNKNOWN", local ? "COVERAGE_UNCONFIRMED" : "NETWORK_UNAVAILABLE", "sourceReference", true);
            }
            catch (HttpRequestException)
            {
                return Failure(preflight, "UNKNOWN", "NETWORK_UNAVAILABLE", "sourceReference", true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            {
                return Failure(preflight, "UNKNOWN", "COVERAGE_UNCONFIRMED", "sourceReference", true);
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is InvalidOperationException || exception is ArgumentException)
            {
                return Failure(preflight, "FAILED", "NATIVE_FAILURE", "sourceReference", false);
            }

            string result = coordinator.Execute(json, catalog, DateTimeOffset.UtcNow, null, out var inspected, cancellationToken);
            if (inspected != null)
            {
                foreach (var old in _coverage.Values.Where(value => value.Catalog.ValidUntil <= DateTimeOffset.UtcNow || value.DatasetId == inspected.DatasetId && value.Catalog.Source == source && value.Catalog.Reference == reference).ToArray())
                {
                    _previews.Invalidate(old.Id);
                    _coverage.Remove(old.Id);
                }

                if (_coverage.Count >= 64)
                {
                    string oldest = _coverage.Keys.First();
                    _previews.Invalidate(oldest);
                    _coverage.Remove(oldest);
                }

                _coverage.Add(inspected.Id, inspected);
            }

            return result;
#else
            await Task.CompletedTask;
            return Failure(preflight, "BLOCKED", "SDK_NOT_READY", "operation", false);
#endif
        }

        private (string Status, object Result, object Evidence, string Code, string Location) ExecuteExtension(string requestId, string operation, Dictionary<string, object> input, DateTimeOffset now)
        {
            // プレビューとPNGの要求はそれぞれの入力検証に委譲する。
            if (operation != "dependencies.inspect") return _previews.Execute(requestId, operation, input, now);

            // 診断にパスや任意パラメーターを指定できない。
            if (input.Count != 0) return ("FAILED", null, null, "INVALID_INPUT", "input");

            try
            {
                var fields = Json.Parse(ReadSetup("inventory.json", 128 * 1024));
                var identity = ReadSetup("project-id", 64);
                var fingerprint = Digests.Sha256(Json.Utf8.GetBytes(_project + "\n" + identity));
                var inspectedAt = Json.Field<string>(fields, "inspectedAt");
                var packages = Json.Field<object[]>(fields, "packages");
                if (Json.Field<int>(fields, "schemaVersion") != 1 || Json.Field<string>(fields, "projectFingerprint") != fingerprint || packages == null ||
                    !DateTimeOffset.TryParseExact(inspectedAt, Digests.TimestampFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var checkedAt) || checkedAt > now)
                {
                    return ("UNKNOWN", null, null, "COVERAGE_UNCONFIRMED", "inventoryRef");
                }

                var sdk = packages.OfType<Dictionary<string, object>>().SingleOrDefault(item => Json.Field<string>(item, "packageId") == DependencyCatalog.PackageId);
                string dependencyStatus = Json.Field<string>(sdk, "status");
                var evidence = Json.Object(("kind", "DEPENDENCY_INVENTORY"), ("source", "LOCAL_PROJECT"), ("checkedAt", inspectedAt));
                var result = Json.Object(("inventoryRef", "phase0-inventory"), ("dependencyStatus", dependencyStatus), ("inspectedAt", inspectedAt));
                switch (dependencyStatus)
                {
                    case "READY": return ("READY", result, evidence, null, null);
                    case "MISSING": return ("UNAVAILABLE", result, evidence, "SDK_NOT_READY", "inventoryRef");
                    case "INCOMPATIBLE":
                    case "BLOCKED": return ("BLOCKED", null, evidence, "SDK_NOT_READY", "inventoryRef");
                    default: return ("UNKNOWN", null, evidence, "COVERAGE_UNCONFIRMED", "inventoryRef");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException || exception is InvalidOperationException)
            {
                return ("UNKNOWN", null, null, "COVERAGE_UNCONFIRMED", "inventoryRef");
            }
        }

        private string ReadSetup(string name, int limit)
        {
            for (var directory = new DirectoryInfo(_setup); directory != null; directory = directory.Parent)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Setup directory unavailable.");
                }
            }

            string path = Path.Combine(_setup, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Setup record unavailable.");
            }

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = file.Length;
            if (length > limit)
            {
                throw new IOException("Setup record exceeds the read limit.");
            }

            var bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = file.Read(bytes, offset, bytes.Length - offset);
                if (count == 0)
                {
                    throw new IOException("Setup record changed while reading.");
                }

                offset += count;
            }

            return Json.Utf8.GetString(bytes);
        }

        private string Failure(string preflight, string status, string code, string location, bool retryable)
        {
            var fields = Json.Parse(preflight);
            fields["status"] = status;
            fields["checkedAt"] = Digests.Timestamp(DateTimeOffset.UtcNow);
            fields["result"] = null;
            fields["evidence"] = Array.Empty<object>();
            fields["diagnostics"] = new[] { Json.Object(("code", code), ("location", location), ("retryable", retryable)) };
            return Json.Write(fields);
        }
    }
}
