using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// Project単位の排他を取得し、同じImport Scopeの予約を一つの永続Jobに対応付ける。
    /// </summary>
    internal sealed class Journal
    {
        private readonly string _project;
        private readonly string _fingerprint;
        private readonly string _root;

        internal Journal(string project, string fingerprint)
        {
            if (!Path.IsPathFullyQualified(project) || !WireValues.Hash(fingerprint))
            {
                throw new ArgumentException("Invalid import journal location or project identity.");
            }

            _project = Path.GetFullPath(project);
            _fingerprint = fingerprint;
            _root = Path.Combine(_project, "Library", "PLATEAUWorldFlow", "Import");
        }

        /// <summary>
        /// Editorで承認を消費した呼び出し元のために、SDK実行前の予約を永続化するか既存Jobを返す。
        /// </summary>
        /// <param name="planId">承認済みImport Planの識別子。</param>
        /// <param name="planHash">承認されたPlanのハッシュ。</param>
        /// <param name="operationKey">同じImport Scopeを識別するハッシュ。</param>
        /// <param name="now">予約を記録する時刻。</param>
        /// <returns>永続化されたスナップショットと、この呼び出しで新規に予約したかどうか。</returns>
        internal (Fields Snapshot, bool Created) Reserve(string planId, string planHash, string operationKey, DateTimeOffset now)
        {
            if (!WireValues.Uuid(planId) || !WireValues.Hash(planHash) || !WireValues.Hash(operationKey))
            {
                throw new ArgumentException("Invalid import reservation identity.");
            }

            using var lease = OpenLease(create: true);

            string jobs = Path.Combine(_root, "jobs");
            CheckPath(jobs, true, true);
            Directory.CreateDirectory(jobs);
            var knownKeys = new HashSet<string>(StringComparer.Ordinal);
            Fields repeated = null;
            bool hasActive = false;
            foreach (string directory in Directory.EnumerateFileSystemEntries(jobs))
            {
                Fields snapshot = Read(directory);
                string savedKey = Json.Field<string>(snapshot, "operationKey");
                if (savedKey == operationKey && Json.Field<string>(snapshot, "importPlanId") == planId)
                {
                    return (snapshot, false);
                }
                // 手動照合で再試行が許された失敗履歴は、新しいPlanの予約を占有しない。
                if (IsRetryCleared(directory, snapshot)) continue;
                bool active = IsActive(Json.Field<string>(snapshot, "state"));
                if (!knownKeys.Add(savedKey) || active && hasActive)
                {
                    throw new InvalidOperationException("Import journal contains conflicting reservations.");
                }

                hasActive |= active;
                if (savedKey == operationKey)
                {
                    repeated = snapshot;
                }
            }

            // 既存Jobは状態の参照だけを返し、SDKの再ディスパッチ権を付与しない。
            if (repeated != null) return (repeated, false);

            if (hasActive)
            {
                throw new InvalidOperationException("Another import job is active or requires reconciliation.");
            }

            string jobId = Guid.NewGuid().ToString("D");
            string timestamp = Digests.Timestamp(now);
            var reservation = Json.Object(("schemaVersion", 1), ("jobId", jobId), ("importPlanId", planId), ("projectFingerprint", _fingerprint), ("planHash", planHash), ("operationKey", operationKey), ("state", "RESERVED"), ("createdAt", timestamp), ("updatedAt", timestamp));
            string destination = Path.Combine(jobs, jobId);
            Directory.CreateDirectory(destination);
            WriteAtomic(Path.Combine(destination, "journal.json"), Json.Write(reservation), replace: false);
            return (reservation, true);
        }

        /// <summary>
        /// 対象Planと直前の状態を排他下で照合し、許可された次の状態を永続化する。
        /// </summary>
        /// <param name="jobId">更新するJobの識別子。</param>
        /// <param name="planHash">Jobに結び付くPlanのハッシュ。</param>
        /// <param name="expectedState">呼び出し元が処理した直前の状態。</param>
        /// <param name="nextState">検査や処理結果に対応する次の状態。</param>
        /// <param name="now">更新を記録する時刻。</param>
        /// <param name="ownershipUnverified">出力の帰属が不明な中断を復旧で確定しないための記録。</param>
        internal void Advance(string jobId, string planHash, string expectedState, string nextState, DateTimeOffset now, bool ownershipUnverified = false)
        {
            if (!WireValues.Uuid(jobId) || !WireValues.Hash(planHash))
            {
                throw new ArgumentException("Invalid import job identity.");
            }

            using var lease = OpenLease(create: false);
            string jobs = Path.Combine(_root, "jobs");
            CheckPath(jobs, true, false);
            string directory = Path.Combine(jobs, jobId);
            var snapshot = Read(directory);
            bool matches = Json.Field<string>(snapshot, "planHash") == planHash && Json.Field<string>(snapshot, "state") == expectedState;
            if (!matches || !AllowsTransition(expectedState, nextState) || ownershipUnverified && nextState != "NEEDS_RECONCILIATION" || !TryReadTime(snapshot, "updatedAt", out var updated) || now < updated)
            {
                throw new InvalidOperationException("Import job update conflicts with the persisted state.");
            }

            snapshot["state"] = nextState;
            snapshot["updatedAt"] = Digests.Timestamp(now);
            if (ownershipUnverified)
            {
                snapshot["ownershipUnverified"] = true;
            }
            WriteAtomic(Path.Combine(directory, "journal.json"), Json.Write(snapshot), replace: true);
        }

        /// <summary>
        /// ProjectとPlanに属するJobの最後に永続化した状態を取得する。
        /// </summary>
        internal Fields Inspect(string jobId, string planHash)
        {
            return ReadJob(jobId, planHash, includeReconciliation: false);
        }

        private Fields ReadJob(string jobId, string planHash, bool includeReconciliation)
        {
            string directory = JobDirectory(jobId, planHash);
            var snapshot = Read(directory, includeReconciliation);
            if (Json.Field<string>(snapshot, "planHash") != planHash)
            {
                throw new InvalidOperationException("Import job belongs to another plan.");
            }

            return snapshot;
        }

        /// <summary>
        /// 排他を保持している照合待ちJobをEditorの手動復旧に提示する。
        /// </summary>
        internal Fields FindReconciliation()
        {
            string jobs = Path.Combine(_root, "jobs");
            CheckDirectoryChain(jobs);
            // Import履歴のないProjectには確認操作でJournalを作らない。
            if (!Directory.Exists(jobs)) return null;

            using var lease = OpenLease(create: false);
            return Directory.EnumerateFileSystemEntries(jobs).Select(directory => Read(directory)).SingleOrDefault(job => Json.Field<string>(job, "state") == "NEEDS_RECONCILIATION");
        }

        /// <summary>
        /// 予約時のPlanと専用出力先を読み取り、Editorに提示する復旧判断を返す。
        /// </summary>
        internal Fields InspectReconciliation(string jobId, string planHash, string stagingIdentity, int stagingRoots)
        {
            using var lease = OpenLease(create: false);
            return ReviewReconciliation(ReadJob(jobId, planHash, includeReconciliation: true), stagingIdentity, stagingRoots);
        }

        /// <summary>
        /// Editorで確認した検査結果を再照合し、終端状態と確認記録を一度の置換で永続化する。
        /// </summary>
        internal string ConfirmReconciliation(string jobId, string planHash, Fields review, string stagingIdentity, int stagingRoots, bool editorConfirmed, DateTimeOffset now)
        {
            if (!editorConfirmed || review == null)
            {
                throw new InvalidOperationException("Import reconciliation requires Editor confirmation.");
            }

            using var lease = OpenLease(create: false);
            var snapshot = ReadJob(jobId, planHash, includeReconciliation: true);
            string state = Json.Field<string>(snapshot, "state");
            string shown = Json.Write(review);
            var recorded = Json.Field<Fields>(snapshot, "reconciliation");
            // 確定済みの同じ確認は記録を返すだけで、再検査・再書き込みを行わない。
            if (recorded != null && shown == Json.Write(recorded)) return state;

            var checkedReview = ReviewReconciliation(snapshot, stagingIdentity, stagingRoots);
            if (shown != Json.Write(checkedReview) || !TryReadTime(snapshot, "updatedAt", out var updated) || now < updated)
            {
                throw new InvalidOperationException("Import reconciliation changed after inspection.");
            }

            state = Json.Field<string>(checkedReview, "nextState");
            snapshot["state"] = state;
            snapshot["updatedAt"] = Digests.Timestamp(now);
            snapshot["reconciliation"] = checkedReview;
            WriteAtomic(Path.Combine(JobDirectory(jobId, planHash), "journal.json"), Json.Write(snapshot), replace: true);
            return state;
        }

        private Fields ReviewReconciliation(Fields job, string stagingIdentity, int stagingRoots)
        {
            if (Json.Field<string>(job, "state") != "NEEDS_RECONCILIATION")
            {
                throw new InvalidOperationException("Import job does not require reconciliation.");
            }
            if (Json.Field<bool>(job, "ownershipUnverified"))
            {
                throw new InvalidDataException("Import output ownership was not verified before interruption.");
            }

            string id = Json.Field<string>(job, "jobId");
            string hash = Json.Field<string>(job, "planHash");
            string planId = Json.Field<string>(job, "importPlanId");
            string wire = ReadArtifact(id, hash, "plan.json");
            var plan = Json.Parse(wire);
            if (!TryReadTime(job, "createdAt", out var reservedAt) || !new PlanIdentity().Accepts(wire, _fingerprint, reservedAt) || !Equals(plan["planHash"], hash) || !Equals(plan["importPlanId"], planId) || !Equals(plan["operationKey"], job["operationKey"]))
            {
                throw new InvalidDataException("Import reconciliation cannot verify the reserved plan.");
            }

            string relative = $"{PlanIdentity.OutputRoot}/{planId}";
            string output = Path.Combine(_project, relative);
            CheckDirectoryChain(Path.GetDirectoryName(output));
            bool hasFolder = CheckPath(output, true, true);
            bool hasFolderMeta = CheckPath(output + ".meta", false, true);
            var inventory = new List<Fields>();
            if (hasFolderMeta)
            {
                inventory.Add(OutputFile(output + ".meta", planId + ".meta"));
            }
            if (hasFolder)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(output).OrderBy(path => path, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(path);
                    if (name != "City.unity" && name != "City.unity.meta" && !Regex.IsMatch(name, @"\AGenerated-[0-9]{6,}\.asset(?:\.meta)?\z", RegexOptions.CultureInvariant))
                    {
                        throw new InvalidDataException("Import reconciliation found an unrecognized output.");
                    }
                    inventory.Add(OutputFile(path, name));
                }
            }
            bool hasScene = inventory.Any(file => Equals(file["name"], "City.unity"));
            bool hasSceneMeta = inventory.Any(file => Equals(file["name"], "City.unity.meta"));
            int assets = inventory.Count(file => Json.Field<string>(file, "name").EndsWith(".asset", StringComparison.Ordinal));
            int assetMeta = inventory.Count(file => Json.Field<string>(file, "name").EndsWith(".asset.meta", StringComparison.Ordinal));
            string state = hasFolder || hasFolderMeta || stagingRoots > 0 ? "PARTIAL" : "FAILED";
            return Json.Object(("jobId", id), ("projectFingerprint", _fingerprint), ("planHash", hash), ("inspectedRevision", job["updatedAt"]), ("output", relative), ("folder", hasFolder), ("folderMeta", hasFolderMeta), ("scene", hasScene), ("sceneMeta", hasSceneMeta), ("assetCount", assets), ("assetMetaCount", assetMeta), ("inventoryHash", Digests.Sha256(Json.Utf8.GetBytes(Json.Write(inventory.ToArray())))), ("stagingIdentity", stagingIdentity), ("stagingRoots", stagingRoots), ("nextState", state));
        }

        private Fields OutputFile(string path, string name)
        {
            CheckPath(path, false, false);
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Json.Object(("name", name), ("length", file.Length.ToString(CultureInfo.InvariantCulture)), ("modified", File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// すでに予約された実効範囲の記録を、Jobを新規作成せず照会する。
        /// </summary>
        internal Fields Find(string operationKey, string planId = null)
        {
            if (!WireValues.Hash(operationKey))
            {
                throw new ArgumentException("Invalid Import scope.");
            }

            string jobs = Path.Combine(_root, "jobs");
            CheckDirectoryChain(jobs);
            // Import未実行のProjectにJournalを作らない。
            if (!Directory.Exists(jobs)) return null;

            using var lease = OpenLease(create: false);
            Fields found = null;
            foreach (string directory in Directory.EnumerateFileSystemEntries(jobs))
            {
                var snapshot = Read(directory);
                if (Json.Field<string>(snapshot, "operationKey") == operationKey)
                {
                    if (planId != null && Json.Field<string>(snapshot, "importPlanId") == planId)
                    {
                        return snapshot;
                    }
                    // 新規計画の照会では、手動確認済みの失敗履歴を実行候補から除く。
                    if (IsRetryCleared(directory, snapshot)) continue;
                    if (found != null)
                    {
                        throw new InvalidOperationException("Import journal contains conflicting reservations.");
                    }
                    found = snapshot;
                }
            }
            return found;
        }

        /// <summary>
        /// 出力がなかったと利用者が確認した失敗Jobを残したまま、新しいPlanの予約を許可する。
        /// </summary>
        internal void AllowRetryAfterManualReview(string jobId, string planHash, DateTimeOffset now)
        {
            using var lease = OpenLease(create: false);
            var snapshot = Inspect(jobId, planHash);
            string directory = JobDirectory(jobId, planHash);
            if (Json.Field<string>(snapshot, "state") != "FAILED" || HasOutput(snapshot))
            {
                throw new InvalidOperationException("Import output must be absent before an unsuccessful scope can be retried.");
            }

            string marker = Path.Combine(directory, "manual-retry.json");
            CheckPath(marker, false, true);
            WriteAtomic(marker, Json.Write(Json.Object(("jobId", jobId), ("planHash", planHash), ("reviewedAt", Digests.Timestamp(now)))), replace: false);
        }

        private bool IsRetryCleared(string directory, Fields snapshot)
        {
            // 実行中・成功・照合待ちの予約では再試行記録を適用しない。
            if (Json.Field<string>(snapshot, "state") != "FAILED") return false;

            string path = Path.Combine(directory, "manual-retry.json");
            CheckPath(path, false, true);
            // 再試行の指示がない失敗履歴は、従来通り予約を保持する。
            if (!File.Exists(path)) return false;

            var marker = Json.Parse(File.ReadAllText(path, Json.Utf8));
            if (marker.Count != 3 || !Equals(marker.GetValueOrDefault("jobId"), snapshot["jobId"]) || !Equals(marker.GetValueOrDefault("planHash"), snapshot["planHash"]) || !TryReadTime(marker, "reviewedAt", out _))
            {
                throw new FormatException("Import manual retry record does not match its job.");
            }
            if (HasOutput(snapshot))
            {
                throw new InvalidOperationException("An output appeared after manual Import review.");
            }
            return true;
        }

        private bool HasOutput(Fields snapshot)
        {
            string output = Path.Combine(_project, PlanIdentity.OutputRoot, Json.Field<string>(snapshot, "importPlanId"));
            CheckDirectoryChain(Path.GetDirectoryName(output));
            return CheckPath(output, true, true) || CheckPath(output + ".meta", false, true);
        }

        /// <summary>
        /// Editor起動・Domain Reload時に残った実行途中のJobを照合待ちへ移す。
        /// </summary>
        internal void RecoverInterrupted(DateTimeOffset now, string currentJob = null)
        {
            string jobs = Path.Combine(_root, "jobs");
            CheckDirectoryChain(jobs);
            // Import履歴のないProjectには復旧処理でディレクトリを作らない。
            if (!Directory.Exists(jobs)) return;

            using var lease = OpenLease(create: false);
            foreach (string directory in Directory.EnumerateFileSystemEntries(jobs))
            {
                var snapshot = Read(directory);
                string state = Json.Field<string>(snapshot, "state");
                if (Json.Field<string>(snapshot, "jobId") != currentJob && IsActive(state) && state != "NEEDS_RECONCILIATION")
                {
                    snapshot["state"] = "NEEDS_RECONCILIATION";
                    TryReadTime(snapshot, "updatedAt", out var updated);
                    snapshot["updatedAt"] = Digests.Timestamp(now > updated ? now : updated);
                    WriteAtomic(Path.Combine(directory, "journal.json"), Json.Write(snapshot), replace: true);
                }
            }
        }

        /// <summary>
        /// 予約済みJobに計画、予定一覧、検証結果を一度だけ保存する。
        /// </summary>
        internal void WriteArtifact(string jobId, string planHash, string name, string json)
        {
            if (!IsArtifact(name))
            {
                throw new ArgumentException("Unknown Import artifact.");
            }

            using var lease = OpenLease(create: false);
            Inspect(jobId, planHash);
            string path = Path.Combine(JobDirectory(jobId, planHash), name);
            CheckPath(path, false, true);
            WriteAtomic(path, json, replace: false);
        }

        /// <summary>
        /// JobのPlan照合後に、許可された成果物の内容を読み取る。
        /// </summary>
        internal string ReadArtifact(string jobId, string planHash, string name)
        {
            if (!IsArtifact(name))
            {
                throw new ArgumentException("Unknown Import artifact.");
            }

            Inspect(jobId, planHash);
            string path = Path.Combine(JobDirectory(jobId, planHash), name);
            CheckPath(path, false, false);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(file, Json.Utf8, false);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Jobにひも付くPNGを新規保存し、ファイルと撮影対象を識別するManifest項目を返す。
        /// </summary>
        internal Fields WriteCapture(string jobId, string planHash, string view, byte[] png, string sceneGuid, DateTimeOffset created)
        {
            using var lease = OpenLease(create: false);
            var job = Inspect(jobId, planHash);
            if (Json.Field<string>(job, "state") != "VERIFYING" || !LooksLikePng(png))
            {
                throw new InvalidDataException("CAPTURE_UNVERIFIED");
            }

            string path = CapturePath(jobId, planHash, view, allowMissing: true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteAtomic(path, png, replace: false);
            return Json.Object(("viewId", view), ("relativePath", CaptureReference(jobId, view)), ("mimeType", "image/png"), ("sha256", Digests.Sha256(png)), ("byteLength", png.Length), ("createdAt", Digests.Timestamp(created)), ("sceneGuid", sceneGuid));
        }

        /// <summary>
        /// 保存PNGの出力パス、サイズ、SHA-256と撮影時のScene GUIDを照合する。
        /// </summary>
        internal bool MatchesCapture(string jobId, string planHash, Fields record, string sceneGuid)
        {
            string view = Json.Field<string>(record, "viewId");
            // 撮影対象のSceneとJobを確認できない項目は、成果物として案内しない。
            if (sceneGuid == null || Json.Field<string>(record, "sceneGuid") != sceneGuid || Json.Field<string>(record, "relativePath") != CaptureReference(jobId, view) || Json.Field<string>(record, "mimeType") != "image/png") return false;

            try
            {
                Inspect(jobId, planHash);
                string path = CapturePath(jobId, planHash, view, allowMissing: false);
                using var file = File.OpenRead(path);
                int expectedSize = Json.Field<int>(record, "byteLength");
                // 証跡の生成解像度に対して過大なファイルをメモリに読まない。
                if (expectedSize <= 8 || expectedSize > 4 * 1024 * 1024 || file.Length != expectedSize) return false;

                using var bytes = new MemoryStream(expectedSize);
                file.CopyTo(bytes);
                var png = bytes.ToArray();
                return LooksLikePng(png) && Digests.Sha256(png) == Json.Field<string>(record, "sha256");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ArgumentException)
            {
                return false;
            }
        }

        private string CapturePath(string jobId, string planHash, string view, bool allowMissing)
        {
            if (view != "oblique" && view != "overhead")
            {
                throw new ArgumentException("Unknown capture viewpoint.");
            }

            string directory = Path.Combine(JobDirectory(jobId, planHash), "captures");
            CheckDirectoryChain(directory);
            string path = Path.Combine(directory, view + ".png");
            CheckPath(path, false, allowMissing);
            return path;
        }

        private string CaptureReference(string jobId, string view) => $"Library/PLATEAUWorldFlow/Import/jobs/{jobId}/captures/{view}.png";

        private bool LooksLikePng(byte[] bytes)
        {
            return bytes != null && bytes.Length > 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;
        }

        private bool IsArtifact(string name)
        {
            return name == "plan.json" || name == "expected-gml.json" || name == "resolved-gml.json" || name == "gml-result.json" || name == "result.json" || name == "evidence.json";
        }

        private string JobDirectory(string jobId, string planHash)
        {
            if (!WireValues.Uuid(jobId) || !WireValues.Hash(planHash))
            {
                throw new ArgumentException("Invalid import job identity.");
            }

            string jobs = Path.Combine(_root, "jobs");
            CheckDirectoryChain(jobs);
            return Path.Combine(jobs, jobId);
        }

        private FileStream OpenLease(bool create)
        {
            CheckDirectoryChain(_root);
            if (create)
            {
                Directory.CreateDirectory(_root);
            }

            string path = Path.Combine(_root, "reservation.lock");
            CheckPath(path, false, create);
            return new FileStream(path, create ? FileMode.OpenOrCreate : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private Fields Read(string directory, bool includeReconciliation = false)
        {
            CheckPath(directory, true, false);
            string path = Path.Combine(directory, "journal.json");
            CheckPath(path, false, false);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 64 * 1024)
            {
                throw new FormatException("Import journal exceeds the record limit.");
            }

            using var reader = new StreamReader(file, Json.Utf8, false);
            var snapshot = Json.Parse(reader.ReadToEnd());
            string jobId = Json.Field<string>(snapshot, "jobId");
            string state = Json.Field<string>(snapshot, "state");
            bool identifiesRecord = WireValues.Uuid(jobId) && jobId == Path.GetFileName(directory) && Json.Field<int>(snapshot, "schemaVersion") == 1 && WireValues.Uuid(Json.Field<string>(snapshot, "importPlanId")) && WireValues.Hash(Json.Field<string>(snapshot, "planHash")) && WireValues.Hash(Json.Field<string>(snapshot, "operationKey"));
            bool recordsTimes = TryReadTime(snapshot, "createdAt", out var created) && TryReadTime(snapshot, "updatedAt", out var updated) && created <= updated;
            bool hasReview = snapshot.TryGetValue("reconciliation", out var receipt);
            bool hasOwnershipFlag = snapshot.TryGetValue("ownershipUnverified", out var ownership);
            bool validReview = !hasReview || receipt is Fields review && (state == "PARTIAL" || state == "FAILED") && Equals(review.GetValueOrDefault("nextState"), state) && Equals(review.GetValueOrDefault("jobId"), jobId) && Equals(review.GetValueOrDefault("planHash"), snapshot.GetValueOrDefault("planHash")) && Equals(review.GetValueOrDefault("projectFingerprint"), _fingerprint);
            bool validOwnership = !hasOwnershipFlag || state == "NEEDS_RECONCILIATION" && Equals(ownership, true);
            if (snapshot.Count != 9 + (hasReview ? 1 : 0) + (hasOwnershipFlag ? 1 : 0) || !validReview || !validOwnership || !identifiesRecord || !recordsTimes || !IsActive(state) && !IsTerminal(state))
            {
                throw new FormatException("Invalid import journal record.");
            }

            if (Json.Field<string>(snapshot, "projectFingerprint") != _fingerprint)
            {
                throw new InvalidOperationException("Import journal belongs to another project.");
            }

            if (!includeReconciliation)
            {
                snapshot.Remove("reconciliation");
                snapshot.Remove("ownershipUnverified");
            }
            return snapshot;
        }

        private bool TryReadTime(Fields snapshot, string field, out DateTimeOffset timestamp)
        {
            return DateTimeOffset.TryParseExact(Json.Field<string>(snapshot, field), Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
        }

        private bool IsActive(string state)
        {
            return state == "RESERVED" || state == "RUNNING" || state == "VERIFYING" || state == "CANCELLING" || state == "NEEDS_RECONCILIATION";
        }

        private bool IsTerminal(string state)
        {
            return state == "COMPLETED" || state == "PARTIAL" || state == "FAILED" || state == "CANCELLED";
        }

        private bool AllowsTransition(string before, string after)
        {
            switch (before)
            {
                case "RESERVED": return after == "RUNNING" || after == "CANCELLED" || after == "NEEDS_RECONCILIATION";
                case "RUNNING": return after == "VERIFYING" || after == "CANCELLING" || after == "NEEDS_RECONCILIATION";
                case "CANCELLING": return after == "VERIFYING" || after == "NEEDS_RECONCILIATION";
                case "VERIFYING": return IsTerminal(after) || after == "NEEDS_RECONCILIATION";
                default: return false;
            }
        }

        private void CheckDirectoryChain(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            {
                CheckPath(directory.FullName, true, directory.FullName != _project);
            }
        }

        private bool CheckPath(string path, bool directory, bool allowMissing)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (IOException exception) when (allowMissing && (exception is FileNotFoundException || exception is DirectoryNotFoundException))
            {
                // 新規の書き込み先だけは未作成を許す。上位ディレクトリは呼び出し元で確認する。
                return false;
            }

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory != directory || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Invalid import journal filesystem entry.");
            }
            return true;
        }

        private void WriteAtomic(string path, string json, bool replace)
        {
            WriteAtomic(path, Json.Utf8.GetBytes(json), replace);
        }

        private void WriteAtomic(string path, byte[] bytes, bool replace)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool ownsTemporary = false;
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                if (replace)
                {
                    ReplaceRecord(temporary, path);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (ownsTemporary)
                {
                    File.Delete(temporary);
                }
            }
        }

        private void ReplaceRecord(string temporary, string path)
        {
            // Windowsの共有違反では置換元と置換先が残る。排他を保持したまま最大150msだけ待つ。
            for (int delay = 10; ; delay *= 2)
            {
                try
                {
                    File.Replace(temporary, path, null);
                    return;
                }
                catch (IOException exception) when (delay <= 80 && IsRetryableReplaceFailure(exception) && File.Exists(temporary) && File.Exists(path))
                {
                    Thread.Sleep(delay);
                }
            }
        }

        private bool IsRetryableReplaceFailure(IOException exception)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            const int unableToRemoveReplaced = unchecked((int)0x80070497);
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (exception.HResult == sharingViolation || exception.HResult == lockViolation || exception.HResult == unableToRemoveReplaced);
        }
    }
}
