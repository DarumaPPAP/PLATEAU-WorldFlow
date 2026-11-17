using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DarumaPPAP.PlateauWorldFlow.Import;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.SceneManagement;
#if WORLDFLOW_PLATEAU_4_3_0
using DarumaPPAP.PlateauWorldFlow.Plateau;
#endif
using Fields = System.Collections.Generic.Dictionary<string, object>;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DarumaPPAP.PlateauWorldFlow.Entry
{
    /// <summary>
    /// 両Adapterから受け取るImport要求を実Projectに束縛し、承認前の事前照合に委譲する。
    /// </summary>
    internal sealed class ImportSession
    {
        internal static ImportSession Current { get; } = new ImportSession();
        private readonly AdoptionValidator _adoption = new AdoptionValidator();
        private readonly PlanIdentity _identity = new PlanIdentity();
        private readonly SemaphoreSlim _queue = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, (string Wire, string Manifest, Fields Attribution, DateTimeOffset Expires)> _prepared = new Dictionary<string, (string, string, Fields, DateTimeOffset)>(StringComparer.Ordinal);
        private string _pendingApproval;
        private string _approvedPlan;
        private string _activeJob;
        private CancellationTokenSource _activeCancellation;
        private int _expectedFiles;
        private int _notifiedFiles;
        private int _sdkPercent;
        private bool _recovered;

        [MenuItem("Tools/PLATEAU WorldFlow/中断したImportを照合")]
        private static void ReconcileFromMenu()
        {
            Current.ReconcileInEditor();
        }

        private void ReconcileInEditor()
        {
            if (!CanRequestApproval() || _activeJob != null || !_queue.Wait(0))
            {
                EditorUtility.DisplayDialog("Importの手動照合", "Import処理やEditorの状態移行が終了してから照合してください。", "閉じる");
                return;
            }

            try
            {
                string project = Session.Current.ProjectFingerprint();
                var journal = ProjectJournal(project);
                journal.RecoverInterrupted(DateTimeOffset.UtcNow);
                var job = journal.FindReconciliation();
                if (job == null)
                {
                    EditorUtility.DisplayDialog("Importの手動照合", "照合待ちのJobはありません。", "閉じる");
                    return;
                }

                string id = Json.Field<string>(job, "jobId");
                string hash = Json.Field<string>(job, "planHash");
                var staging = InspectStaging(job);
                var review = journal.InspectReconciliation(id, hash, staging.Identity, staging.Roots);
                string proposed = Json.Field<string>(review, "nextState");
                string message = $"Project {Application.dataPath}\nFingerprint {project}\nJob {id}\nPlan Hash {hash}\n\n出力先 {review["output"]}\nフォルダ {Presence(review, "folder")} / .meta {Presence(review, "folderMeta")}\nCity.unity {Presence(review, "scene")} / .meta {Presence(review, "sceneMeta")}\n生成Asset {review["assetCount"]}件 / .meta {review["assetMetaCount"]}件\n作業Sceneのルート {staging.Roots}件\n\nこのJobの残存物と照合できた場合のみ、{proposed} に確定してください。Scene・Asset・メタファイルは保持します。";
                // ダイアログをキャンセルした場合は確認記録もJob状態も更新しない。
                if (!EditorUtility.DisplayDialog("Importの手動照合", message, $"{proposed} に確定", "キャンセル")) return;

                if (!CanRequestApproval() || _activeJob != null || Session.Current.ProjectFingerprint() != project)
                {
                    throw new InvalidOperationException("Import reconciliation Editor context changed.");
                }
                staging = InspectStaging(job);
                string state = journal.ConfirmReconciliation(id, hash, review, staging.Identity, staging.Roots, editorConfirmed: true, now: DateTimeOffset.UtcNow);
                string result = $"Job {id} を {state} に確定しました。";
                if (state == "FAILED")
                {
                    ConfirmRetryInEditor(journal, job, project, result);
                }
                else
                {
                    EditorUtility.DisplayDialog("Importの手動照合", result, "閉じる");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is FormatException || exception is InvalidOperationException || exception is ArgumentException)
            {
                Debug.LogWarning($"WorldFlow Importの手動照合を停止しました。{exception.GetType().Name}: {exception.Message}");
                EditorUtility.DisplayDialog("Importの手動照合", "Job、出力先、または作業Sceneを確定できませんでした。Consoleを確認してください。", "閉じる");
            }
            finally
            {
                _queue.Release();
            }
        }

        private void ConfirmRetryInEditor(Journal journal, Fields job, string project, string result)
        {
            string id = Json.Field<string>(job, "jobId");
            string hash = Json.Field<string>(job, "planHash");
            // 失敗確定とは別に、利用者が同一Scopeの新Plan作成を許可した場合だけ予約を解放する。
            if (!EditorUtility.DisplayDialog("同じImport範囲の再試行を許可", $"{result}\n\nJob {id}\nPlan Hash {hash}\n\n同じ範囲で新しいPlanを作成できるようにします。新PlanのImportには別途Editor承認が必要です。", "再試行を許可", "閉じる")) return;

            try
            {
                if (!CanRequestApproval() || _activeJob != null || Session.Current.ProjectFingerprint() != project || InspectStaging(job).Roots != 0)
                {
                    throw new InvalidOperationException("Import retry could not verify the Editor context or remaining Scene.");
                }
                journal.AllowRetryAfterManualReview(id, hash, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is FormatException)
            {
                Debug.LogWarning($"WorldFlow Importの再試行を許可できません。{exception.GetType().Name}: {exception.Message}");
                EditorUtility.DisplayDialog("同じImport範囲の再試行を許可", "再試行を許可できませんでした。Consoleを確認してください。", "閉じる");
            }
        }

        private string Presence(Fields review, string name) => Json.Field<bool>(review, name) ? "あり" : "なし";

        private (string Identity, int Roots) InspectStaging(Fields job)
        {
            string id = Json.Field<string>(job, "jobId");
            string path = $"{PlanIdentity.OutputRoot}/{job["importPlanId"]}/City.unity";
            int handle = SessionState.GetInt("PLATEAUWorldFlow.Import.Staging." + id, 0);
            (string, int) staging = (null, 0);
            bool hasUnownedUntitled = false;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (handle != 0 && scene.handle == handle)
                {
                    if (!scene.isLoaded || staging.Item1 != null || !string.IsNullOrEmpty(scene.path) && scene.path != path)
                    {
                        throw new InvalidOperationException("Import staging scene ownership is uncertain.");
                    }
                    staging = ($"{handle}:{scene.path}:{scene.isDirty}", scene.rootCount);
                }
                else if (scene.path == path && scene.isLoaded)
                {
                    if (scene.isDirty || staging.Item1 != null)
                    {
                        throw new InvalidOperationException("Import reconciliation found unverified Scene changes.");
                    }
                    staging = ($"{scene.handle}:{scene.path}:{scene.isDirty}", scene.rootCount);
                }
                else if (string.IsNullOrEmpty(scene.path) && (scene.isDirty || scene.rootCount > 0))
                {
                    hasUnownedUntitled = true;
                }
            }
            if (staging.Item1 == null && hasUnownedUntitled)
            {
                throw new InvalidOperationException("Import reconciliation found an unverified unsaved Scene.");
            }
            return staging;
        }

        [InitializeOnLoadMethod]
        private static void RegisterRecovery()
        {
            void RecoverOnUpdate()
            {
                EditorApplication.update -= RecoverOnUpdate;
                try
                {
                    Current.Recover(Session.Current.ProjectFingerprint());
                }
                catch (Exception exception) when (exception is IOException || exception is FormatException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    // Setup前や復旧に失敗した環境では、最初のJob操作で再び照合する。
                }
            }
            EditorApplication.update += RecoverOnUpdate;
        }

        /// <summary>
        /// v1 Import要求を検証し、Project相対の計画または構造化された停止理由を返す。
        /// </summary>
        internal async Task<string> ExecuteAsync(string document, CancellationToken token)
        {
            var request = Json.Parse(document ?? "");
            string requestId = Json.Field<string>(request, "requestId");
            string operation = Json.Field<string>(request, "operation");
            string project = Json.Field<string>(request, "projectFingerprint");
            if (!WireValues.Uuid(requestId) || !WireValues.Hash(project) || request.Count != 5 || Json.Field<int>(request, "schemaVersion") != 1 || string.IsNullOrEmpty(operation))
            {
                throw new FormatException("Invalid WorldFlow Import envelope.");
            }

            await _queue.WaitAsync(token);
            try
            {
                if (Session.Current.ProjectFingerprint() != project)
                {
                    return Response(requestId, "BLOCKED", "PROJECT_MISMATCH");
                }

                var input = Json.Field<Fields>(request, "input");
                if (operation == "import.prepare")
                {
                    var prepared = await PrepareAsync(input, project, token);
                    return Response(requestId, prepared.Plan == null ? "BLOCKED" : "READY", prepared.Error, prepared.Plan, prepared.FileCount, prepared.Plan == null ? null : "PREPARED");
                }

                if (operation == "import.inspect" || operation == "import.request_approval")
                {
                    return await ReviewAsync(requestId, operation, input, project, token);
                }

                if (operation == "import.start")
                {
                    Recover(project);
                    return Start(requestId, input, project, token);
                }

                if (operation == "import.status" || operation == "import.cancel" || operation == "import.verify" || operation == "import.artifacts")
                {
                    Recover(project);
                    return JobCommand(requestId, operation, input, project);
                }

                return Response(requestId, "BLOCKED", "OPERATION_UNAVAILABLE");
            }
            finally
            {
                _queue.Release();
            }
        }

        private async Task<(Fields Plan, string Error, int FileCount)> PrepareAsync(Fields input, string project, CancellationToken token)
        {
            string session = Json.Field<string>(input, "planningSessionId");
            int revision = Json.Field<int>(input, "planRevision");
            string planningJson = Json.Field<string>(input, "planningResult");
            var features = Json.Field<object[]>(input, "featureSettings");
            int zone = Json.Field<int>(input, "coordinateZoneId");
            if (input?.Count != 5 || features == null || zone < 1 || zone > 19 || !_adoption.Accepts(planningJson, project, session, revision))
            {
                return (null, "ADOPTED_PLAN_UNVERIFIED", 0);
            }

#if WORLDFLOW_PLATEAU_4_3_0
            if (!HasPinnedSdk())
            {
                return (null, "SDK_NOT_READY", 0);
            }

            var planning = Json.Parse(planningJson);
            var coverage = Json.Field<Fields>(planning, "coverageReference");
            string kind = Json.Field<string>(coverage, "source");
            bool remote = kind == "REMOTE";
            if (remote && Json.Field<string>(coverage, "sourceReference") != RemoteSource.Reference)
            {
                return (null, "SOURCE_MAPPING_UNVERIFIED", 0);
            }

            string datasetId = Json.Field<string>(coverage, "datasetId");
            string sourceRoot = remote ? null : Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ROOT");
            string sourceVersion = remote ? Json.Field<string>(CatalogDataset(planning, datasetId), "version") : Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_VERSION");
            if (!remote && (kind != "LOCAL" || !Path.IsPathFullyQualified(sourceRoot ?? "") || datasetId != Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ID")))
            {
                return (null, "SOURCE_NOT_ALLOWED", 0);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var selectedPlan = Json.Field<Fields>(Json.Field<Fields>(planning, "areaChoice"), "plan");
            var grids = Json.Field<object[]>(selectedPlan, "selectedGridCodes").Cast<string>().ToArray();
            var selectedQuality = Json.Field<Fields>(planning, "qualityChoice");
            string planId = Guid.NewGuid().ToString("D");
            string plannedPath = $"{PlanIdentity.OutputRoot}/{planId}/City.unity";
            string emptyDigest = new string('0', 64);
            var source = Json.Object(("kind", "LOCAL"), ("worldFlowDatasetId", datasetId), ("sdkSourceId", null), ("datasetVersion", sourceVersion), ("sourceFingerprint", emptyDigest), ("mappingStatus", "VERIFIED"));
            var draft = Json.Object(
                ("schemaVersion", 1), ("importPlanId", planId), ("revision", 1), ("projectFingerprint", project),
                ("phase2", Json.Object(("planningSessionId", session), ("planRevision", revision), ("planDigest", planning["planDigest"]))),
                ("source", source), ("selection", Json.Object(("gridCodes", grids), ("coordinateZoneId", zone), ("featureSettings", features), ("qualityRequirementsStatus", "VERIFIED"))),
                ("output", Json.Object(("mode", "NEW_SCENE"), ("scenePath", plannedPath), ("assetExport", "NONE"), ("overwriteExisting", false))),
                ("expectedGmlManifestHash", emptyDigest), ("sdkVersion", DependencyCatalog.Version), ("expiresAt", Digests.Timestamp(DateTimeOffset.UtcNow.AddMinutes(5))));

            // 不正な設定でSDKへ進まない。Sealはスコープ投影の前提がそろっている場合だけ呼ぶ。
            if (!TryGetRequirements(features, selectedQuality, out var lods) || !CandidateAccepts(draft, project))
            {
                return (null, "QUALITY_MISMATCH", 0);
            }

            string manifest;
            try
            {
                if (remote)
                {
                    var resolved = await new RemoteManifest().InspectAsync(datasetId, sourceVersion, grids, lods, token);
                    sourceVersion = resolved.Version;
                    source["kind"] = kind;
                    source["sdkSourceId"] = resolved.SdkId;
                    source["datasetVersion"] = sourceVersion;
                    manifest = resolved.Manifest;
                }
                else
                {
                    manifest = await Task.Run(() => new LocalManifest().Inspect(sourceRoot, grids, lods, token), token);
                }
            }
            catch (InvalidDataException exception)
            {
                return (null, SourceError(exception.Message), 0);
            }
            catch (Exception exception) when (exception is IOException || exception is XmlException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is HttpRequestException || exception is FormatException || exception is OperationCanceledException && !token.IsCancellationRequested)
            {
                return (null, remote ? "SOURCE_MAPPING_UNVERIFIED" : "SOURCE_UNVERIFIED", 0);
            }

            var files = Json.Field<object[]>(Json.Parse(manifest), "files");
            var locations = Json.Field<object[]>(planning, "candidatePlaces")?.OfType<Fields>().Where(candidate => Json.Field<string>(candidate, "id") == Json.Field<string>(planning, "selectedLocationId")).Take(2).ToArray();
            if (locations?.Length != 1 || Json.Field<string>(locations[0], "physicalPlaceStatus") != "VERIFIED")
            {
                return (null, "ADOPTED_PLAN_UNVERIFIED", 0);
            }
            var zoneResolver = new CoordinateZone();
            if (files.Any(file => !MatchesZone(file as Fields, zone, zoneResolver)))
            {
                return (null, "COORDINATE_ZONE_UNVERIFIED", 0);
            }

            string requestedCrs = Json.Field<string>(Json.Field<Fields>(selectedPlan, "extent"), "crs");
            if (requestedCrs != null && files.OfType<Fields>().Any(file => requestedCrs != Json.Field<string>(file, "crs") && !(requestedCrs == "EPSG:6668" && Json.Field<string>(file, "crs") == "EPSG:6697")))
            {
                return (null, "SOURCE_CRS_MISMATCH", 0);
            }

            if (Session.Current.ProjectFingerprint() != project || !OutputAvailable(projectRoot, plannedPath))
            {
                return (null, "DESTINATION_UNAVAILABLE", 0);
            }

            string manifestHash = Digests.Sha256(Json.Utf8.GetBytes(manifest));
            source["sourceFingerprint"] = SourceFingerprint(sourceRoot, datasetId, sourceVersion, manifestHash);
            draft["expectedGmlManifestHash"] = manifestHash;
            var now = DateTimeOffset.UtcNow;
            var expires = now.AddMinutes(5);
            draft["expiresAt"] = Digests.Timestamp(expires);
            string wire = _identity.Seal(draft);
            foreach (string expired in _prepared.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
            {
                _prepared.Remove(expired);
            }
            _prepared.Add(planId, (wire, manifest, Attribution(planning, datasetId), expires));
            return (Json.Parse(wire), null, files.Length);
#else
            await Task.Yield();
            return (null, "SDK_NOT_READY", 0);
#endif
        }

        private async Task<string> ReviewAsync(string requestId, string operation, Fields input, string project, CancellationToken token)
        {
            // UI操作が成立しない環境で承認要求を受理しない。
            if (operation == "import.request_approval" && !CanRequestApproval()) return Response(requestId, "BLOCKED", "HUMAN_APPROVAL_REQUIRED");

            string id = Json.Field<string>(input, "importPlanId");
            string hash = Json.Field<string>(input, "planHash");
            if (input?.Count != 2 || !WireValues.Uuid(id) || !WireValues.Hash(hash) || !_prepared.TryGetValue(id, out var prepared))
            {
                return Response(requestId, "BLOCKED", "PLAN_NOT_PREPARED");
            }

            var plan = Json.Parse(prepared.Wire);
            if (hash != Json.Field<string>(plan, "planHash") || !_identity.Accepts(prepared.Wire, project, DateTimeOffset.UtcNow))
            {
                return Response(requestId, "BLOCKED", "PLAN_STALE");
            }

            var verification = await RecheckAsync(plan, token);
            if (verification.Error != null)
            {
                // 計画内容と実体がずれた場合は、同じ承認を再利用できない。
                if (_approvedPlan == id)
                {
                    _approvedPlan = null;
                }
                return Response(requestId, "BLOCKED", verification.Error);
            }

            if (operation == "import.request_approval" && _approvedPlan != id)
            {
                if (!CanRequestApproval())
                {
                    return Response(requestId, "BLOCKED", "HUMAN_APPROVAL_REQUIRED");
                }

                if (_pendingApproval != null && _pendingApproval != id)
                {
                    return Response(requestId, "BLOCKED", "APPROVAL_IN_PROGRESS");
                }

                if (_pendingApproval == null)
                {
                    _pendingApproval = id;
                    try
                    {
                        var scene = SceneManager.GetActiveScene();
                        ApprovalWindow.Open(plan, verification.Count, Application.productName, string.IsNullOrEmpty(scene.path) ? scene.name : scene.path, prepared.Attribution, () => ConfirmApprovalAsync(id, project), () => CloseApproval(id));
                    }
                    catch
                    {
                        _pendingApproval = null;
                        throw;
                    }
                }
            }

            var state = _approvedPlan == id ? "APPROVED" : _pendingApproval == id ? "AWAITING_APPROVAL" : "PREPARED";
            return Response(requestId, "READY", null, plan, verification.Count, state);
        }

        private bool CanRequestApproval()
        {
            return !Application.isBatchMode && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private async Task<string> ConfirmApprovalAsync(string id, string project)
        {
            await _queue.WaitAsync();
            try
            {
                // この処理はEditorウィンドウの操作に結び付く。失効・閉鎖・移行中は承認を発行しない。
                if (_pendingApproval != id || !CanRequestApproval() || !_prepared.TryGetValue(id, out var prepared) || !_identity.Accepts(prepared.Wire, project, DateTimeOffset.UtcNow)) return "PLAN_STALE";

                var verification = await RecheckAsync(Json.Parse(prepared.Wire), CancellationToken.None);
                if (verification.Error != null || _pendingApproval != id || !CanRequestApproval())
                {
                    return verification.Error ?? "PLAN_STALE";
                }

                _approvedPlan = id;
                return null;
            }
            finally
            {
                _queue.Release();
            }
        }

        private void CloseApproval(string id)
        {
            if (_pendingApproval == id)
            {
                _pendingApproval = null;
            }
        }

        private string Start(string requestId, Fields input, string project, CancellationToken token)
        {
            string id = Json.Field<string>(input, "importPlanId");
            string hash = Json.Field<string>(input, "planHash");
            // Startは現在のEditorが発行した計画参照だけを受け取る。
            if (input?.Count != 2 || !WireValues.Uuid(id) || !WireValues.Hash(hash) || !_prepared.TryGetValue(id, out var cached)) return Response(requestId, "BLOCKED", "PLAN_NOT_PREPARED");

            var plan = Json.Parse(cached.Wire);
            if (hash != Json.Field<string>(plan, "planHash") || !_identity.Accepts(cached.Wire, project, DateTimeOffset.UtcNow))
            {
                return Response(requestId, "BLOCKED", "PLAN_STALE");
            }

            string scope = Json.Field<string>(plan, "operationKey");
            var journal = ProjectJournal(project);
            var existing = journal.Find(scope, id);
            // 同じScopeの再送は既存Jobを照会するだけで、再実行権を発行しない。
            if (existing != null) return Response(requestId, "READY", null, job: existing);

            // Editor内の利用者操作なしに新規Jobや出力を作らない。
            if (_approvedPlan != id || !CanRequestApproval()) return Response(requestId, "BLOCKED", "HUMAN_APPROVAL_REQUIRED");

            token.ThrowIfCancellationRequested();
            (Fields Snapshot, bool Created) reservation;
            try
            {
                reservation = journal.Reserve(id, hash, scope, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException)
            {
                return Response(requestId, "BLOCKED", "JOB_RESERVATION_UNAVAILABLE");
            }

            _approvedPlan = null;
            // 同時要求で先に予約されていれば既存の記録だけを返す。
            if (!reservation.Created) return Response(requestId, "READY", null, job: reservation.Snapshot);

            string jobId = Json.Field<string>(reservation.Snapshot, "jobId");
            try
            {
                journal.WriteArtifact(jobId, hash, "plan.json", cached.Wire);
                journal.WriteArtifact(jobId, hash, "expected-gml.json", cached.Manifest);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                journal.Advance(jobId, hash, "RESERVED", "NEEDS_RECONCILIATION", DateTimeOffset.UtcNow);
                return Response(requestId, "BLOCKED", "JOB_EVIDENCE_UNAVAILABLE", job: journal.Inspect(jobId, hash));
            }

            _activeJob = jobId;
            _expectedFiles = Json.Field<object[]>(Json.Parse(cached.Manifest), "files").Length;
            Volatile.Write(ref _notifiedFiles, 0);
            Volatile.Write(ref _sdkPercent, 0);
            _activeCancellation = new CancellationTokenSource();
            var cancellation = _activeCancellation;
            void DispatchOnUpdate()
            {
                EditorApplication.update -= DispatchOnUpdate;
                RunReserved(journal, reservation.Snapshot, plan, cached.Attribution, cancellation);
            }
            EditorApplication.update += DispatchOnUpdate;
            return Response(requestId, "READY", null, job: reservation.Snapshot);
        }

        private string JobCommand(string requestId, string operation, Fields input, string project)
        {
            string id = Json.Field<string>(input, "jobId");
            string hash = Json.Field<string>(input, "planHash");
            // Job参照はPlanに束縛し、未登録の呼び出し元に別Planの状態を返さない。
            if (input?.Count != 2 || !WireValues.Uuid(id) || !WireValues.Hash(hash)) return Response(requestId, "BLOCKED", "JOB_UNAVAILABLE");

            var journal = ProjectJournal(project);
            try
            {
                var snapshot = journal.Inspect(id, hash);
                if (operation == "import.verify" || operation == "import.artifacts")
                {
                    return EvidenceCommand(journal, requestId, operation, snapshot);
                }
                if (operation == "import.cancel")
                {
                    string state = Json.Field<string>(snapshot, "state");
                    if (_activeJob != id || _activeCancellation == null || (state != "RESERVED" && state != "RUNNING" && state != "CANCELLING"))
                    {
                        return Response(requestId, "BLOCKED", "JOB_NOT_CANCELLABLE");
                    }

                    if (state == "RUNNING")
                    {
                        journal.Advance(id, hash, state, "CANCELLING", DateTimeOffset.UtcNow);
                    }
                    _activeCancellation.Cancel();
                    snapshot = journal.Inspect(id, hash);
                }
                var progress = _activeJob == id ? Json.Object(("expectedGmlCount", _expectedFiles), ("notifiedGmlCount", Volatile.Read(ref _notifiedFiles)), ("lastSdkPercent", Volatile.Read(ref _sdkPercent))) : null;
                return Response(requestId, "READY", null, job: snapshot, progress: progress);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException || exception is FormatException || exception is UnauthorizedAccessException)
            {
                return Response(requestId, "BLOCKED", "JOB_UNAVAILABLE");
            }
        }

        private async void RunReserved(Journal journal, Fields reservation, Fields plan, Fields attribution, CancellationTokenSource cancellation)
        {
            string id = Json.Field<string>(reservation, "jobId");
            string hash = Json.Field<string>(reservation, "planHash");
            try
            {
                if (cancellation.IsCancellationRequested)
                {
                    journal.Advance(id, hash, "RESERVED", "CANCELLED", DateTimeOffset.UtcNow);
                    return;
                }

                var check = await RecheckAsync(plan, cancellation.Token);
                if (check.Error != null || !CanRequestApproval())
                {
                    Debug.LogWarning($"WorldFlow Import Job {id} を実行前の再照合で停止しました（{check.Error ?? "EDITOR_UNAVAILABLE"}）。");
                    journal.Advance(id, hash, "RESERVED", "NEEDS_RECONCILIATION", DateTimeOffset.UtcNow);
                    return;
                }

#if WORLDFLOW_PLATEAU_4_3_0
                journal.Advance(id, hash, "RESERVED", "RUNNING", DateTimeOffset.UtcNow);
                EditorApplication.LockReloadAssemblies();
                try
                {
                    string root = check.Root;
                    string sdkManifest = check.Manifest;
                    if (root == null)
                    {
                        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        root = Path.Combine(projectRoot, "Library", "PLATEAUWorldFlow", "Import", "jobs", id, "source");
                        var selection = Json.Field<Fields>(plan, "selection");
                        var grids = Json.Field<object[]>(selection, "gridCodes").Cast<string>().ToArray();
                        var lods = FeatureLods(selection);
                        sdkManifest = await Task.Run(() => new RemoteStaging().AcquireAsync(root, check.Manifest, grids, lods, cancellation.Token), cancellation.Token);
                        var files = Json.Field<object[]>(Json.Parse(sdkManifest), "files");
                        int zone = Json.Field<int>(selection, "coordinateZoneId");
                        var resolver = new CoordinateZone();
                        string scene = Json.Field<string>(Json.Field<Fields>(plan, "output"), "scenePath");
                        if (!CanRequestApproval() || Session.Current.ProjectFingerprint() != Json.Field<string>(plan, "projectFingerprint") || !HasPinnedSdk() || !OutputAvailable(projectRoot, scene) || files.Any(file => !MatchesZone(file as Fields, zone, resolver)))
                        {
                            throw new InvalidDataException("SOURCE_UNVERIFIED");
                        }
                        journal.WriteArtifact(id, hash, "resolved-gml.json", sdkManifest);
                    }

                    var outcome = await new Runner().RunAsync(plan, root, sdkManifest, cancellation.Token, scene =>
                    {
                        SessionState.SetInt("PLATEAUWorldFlow.Import.Staging." + id, scene.handle);
                    }, (notified, percent) =>
                    {
                        Volatile.Write(ref _notifiedFiles, notified);
                        Volatile.Write(ref _sdkPercent, percent);
                    }, () =>
                    {
                        string before = Json.Field<string>(journal.Inspect(id, hash), "state");
                        journal.Advance(id, hash, before, "VERIFYING", DateTimeOffset.UtcNow);
                    });
                    var row = Json.Object(("schemaVersion", 1), ("jobId", id), ("importPlanId", reservation["importPlanId"]), ("projectFingerprint", reservation["projectFingerprint"]), ("planHash", hash), ("operationKey", reservation["operationKey"]), ("checkedAt", Digests.Timestamp(DateTimeOffset.UtcNow)), ("state", outcome.State));
                    int successful = outcome.Gml.Count(item => Json.Field<string>(item, "state") == "SUCCEEDED");
                    row["gml"] = Json.Object(("expected", outcome.Gml.Length), ("succeeded", successful), ("unverified", outcome.Gml.Length - successful));
                    row["sceneVerification"] = outcome.SceneVerified ? "VERIFIED" : "UNVERIFIED";
                    string gmlResult = Json.Write(Json.Object(("files", outcome.Gml)));
                    journal.WriteArtifact(id, hash, "gml-result.json", gmlResult);
                    journal.WriteArtifact(id, hash, "result.json", Json.Write(row));
                    var evidence = new Evidence().Record(reservation, plan, row, gmlResult, attribution);
                    var savedScene = Json.Field<Fields>(evidence, "scene");
                    if (outcome.SceneVerified && savedScene != null)
                    {
                        var captures = new SceneCapture().Record(journal, id, hash, Json.Field<string>(savedScene, "relativePath"), Json.Field<string>(savedScene, "guid"));
                        evidence["captureStatus"] = captures.Status;
                        evidence["captures"] = captures.Manifest;
                    }
                    if (check.Root == null)
                    {
                        evidence["resolvedGmlSha256"] = Digests.Sha256(Json.Utf8.GetBytes(sdkManifest));
                    }
                    journal.WriteArtifact(id, hash, "evidence.json", Json.Write(evidence));
                    journal.Advance(id, hash, "VERIFYING", outcome.State, DateTimeOffset.UtcNow);
                }
                finally
                {
                    EditorApplication.UnlockReloadAssemblies();
                }
#endif
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                RecordInterrupted(journal, id, hash, cancelled: true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"WorldFlow Import Job {id} が中断しました。Editor Console で例外と Job 記録を確認してください。");
                Debug.LogException(exception);
                bool ownershipUnverified = exception.Message == "SCENE_OWNERSHIP_UNVERIFIED" || exception.Message == "ASSET_DESTINATION_CONFLICT" || exception.Message == "ASSET_FOLDER_CREATION_FAILED";
                RecordInterrupted(journal, id, hash, cancelled: false, ownershipUnverified: ownershipUnverified);
            }
            finally
            {
                if (_activeJob == id)
                {
                    _activeJob = null;
                    _activeCancellation = null;
                }
                cancellation.Dispose();
            }
        }

        private void RecordInterrupted(Journal journal, string id, string hash, bool cancelled, bool ownershipUnverified = false)
        {
            try
            {
                string state = Json.Field<string>(journal.Inspect(id, hash), "state");
                if (state == "RESERVED" || state == "RUNNING" || state == "CANCELLING" || state == "VERIFYING")
                {
                    journal.Advance(id, hash, state, cancelled && state == "RESERVED" ? "CANCELLED" : "NEEDS_RECONCILIATION", DateTimeOffset.UtcNow, ownershipUnverified);
                }
            }
            catch (Exception)
            {
                Debug.LogError("WorldFlow Import Jobの中断を記録できません。Journalと生成物を確認してください。");
            }
        }

        private Journal ProjectJournal(string project)
        {
            return new Journal(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), project);
        }

        private void Recover(string project)
        {
            // 起動直後の一度だけ、現在のプロセスで所有していない途中Jobを回収する。
            if (_recovered) return;

            ProjectJournal(project).RecoverInterrupted(DateTimeOffset.UtcNow, _activeJob);
            _recovered = true;
        }

        private string EvidenceCommand(Journal journal, string requestId, string operation, Fields job)
        {
            string id = Json.Field<string>(job, "jobId");
            string hash = Json.Field<string>(job, "planHash");
            try
            {
                string planWire = journal.ReadArtifact(id, hash, "plan.json");
                if (!DateTimeOffset.TryParseExact(Json.Field<string>(job, "createdAt"), Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reservedAt) || !_identity.Accepts(planWire, Json.Field<string>(job, "projectFingerprint"), reservedAt))
                {
                    return Response(requestId, "BLOCKED", "JOB_PLAN_UNVERIFIED", job: job);
                }

                var plan = Json.Parse(planWire);
                if (Json.Field<string>(plan, "importPlanId") != Json.Field<string>(job, "importPlanId") || Json.Field<string>(plan, "operationKey") != Json.Field<string>(job, "operationKey"))
                {
                    return Response(requestId, "BLOCKED", "JOB_PLAN_UNVERIFIED", job: job);
                }

                var result = Json.Parse(journal.ReadArtifact(id, hash, "result.json"));
                var evidence = Json.Parse(journal.ReadArtifact(id, hash, "evidence.json"));
                string expected = journal.ReadArtifact(id, hash, "expected-gml.json");
                bool remote = Json.Field<string>(Json.Field<Fields>(plan, "source"), "kind") == "REMOTE";
                if (remote && Json.Field<string>(evidence, "resolvedGmlSha256") != Digests.Sha256(Json.Utf8.GetBytes(journal.ReadArtifact(id, hash, "resolved-gml.json"))))
                {
                    return Response(requestId, "BLOCKED", "EVIDENCE_MISMATCH", job: job);
                }
                var verifier = new Evidence();
                string gmlResult = journal.ReadArtifact(id, hash, "gml-result.json");
                if (!verifier.Verify(job, result, evidence, plan, expected, gmlResult))
                {
                    return Response(requestId, "BLOCKED", "EVIDENCE_MISMATCH", job: job);
                }

                string sceneGuid = Json.Field<string>(Json.Field<Fields>(evidence, "scene"), "guid");
                var listed = Json.Field<object[]>(evidence, "captures") ?? Array.Empty<object>();
                var valid = listed.OfType<Fields>().Where(item => journal.MatchesCapture(id, hash, item, sceneGuid)).GroupBy(item => Json.Field<string>(item, "viewId"), StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
                string captureStatus = Json.Field<string>(evidence, "captureStatus");
                string checkedCaptureStatus = valid.Length == 2 ? "READY" : valid.Length == 1 ? "PARTIAL" : captureStatus == "NOT_RUN" && listed.Length == 0 ? "NOT_RUN" : "BLOCKED";
                evidence["captureStatus"] = checkedCaptureStatus;
                evidence["captures"] = valid;
                string captureDiagnostic = valid.Length != listed.Length || captureStatus != checkedCaptureStatus ? "CAPTURE_INTEGRITY_MISMATCH" : null;
                string prefix = $"Library/PLATEAUWorldFlow/Import/jobs/{id}";
                var artifacts = new List<string> { $"{prefix}/result.json", $"{prefix}/evidence.json", $"{prefix}/gml-result.json" };
                artifacts.AddRange(valid.Select(item => Json.Field<string>(item, "relativePath")));
                if (remote)
                {
                    artifacts.Add($"{prefix}/resolved-gml.json");
                }
                return Response(requestId, "READY", captureDiagnostic, job: job, result: result, evidence: evidence, artifacts: operation == "import.artifacts" ? artifacts.ToArray() : null);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is FormatException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return Response(requestId, "BLOCKED", "EVIDENCE_UNAVAILABLE", job: job);
            }
        }

        private async Task<(string Error, int Count, string Manifest, string Root)> RecheckAsync(Fields plan, CancellationToken token)
        {
#if WORLDFLOW_PLATEAU_4_3_0
            if (!HasPinnedSdk())
            {
                return ("SDK_NOT_READY", 0, null, null);
            }

            var source = Json.Field<Fields>(plan, "source");
            bool remote = Json.Field<string>(source, "kind") == "REMOTE";
            string root = remote ? null : Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ROOT");
            string id = Json.Field<string>(source, "worldFlowDatasetId");
            string version = Json.Field<string>(source, "datasetVersion");
            if (!remote && (!Path.IsPathFullyQualified(root ?? "") || !SourceConfigurationMatches(root, id, version)))
            {
                return ("SOURCE_NOT_ALLOWED", 0, null, null);
            }

            var selection = Json.Field<Fields>(plan, "selection");
            var grids = Json.Field<object[]>(selection, "gridCodes").Cast<string>().ToArray();
            var lods = FeatureLods(selection);
            string manifest;
            try
            {
                if (remote)
                {
                    var resolved = await new RemoteManifest().InspectAsync(id, version, grids, lods, token);
                    if (resolved.SdkId != Json.Field<string>(source, "sdkSourceId") || resolved.Version != version)
                    {
                        return ("SOURCE_MAPPING_UNVERIFIED", 0, null, null);
                    }
                    manifest = resolved.Manifest;
                }
                else
                {
                    manifest = await Task.Run(() => new LocalManifest().Inspect(root, grids, lods, token), token);
                }
            }
            catch (InvalidDataException exception)
            {
                return (SourceError(exception.Message), 0, null, null);
            }
            catch (Exception exception) when (exception is IOException || exception is XmlException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is HttpRequestException || exception is FormatException || exception is OperationCanceledException && !token.IsCancellationRequested)
            {
                return (remote ? "SOURCE_MAPPING_UNVERIFIED" : "SOURCE_UNVERIFIED", 0, null, null);
            }

            token.ThrowIfCancellationRequested();
            string manifestHash = Digests.Sha256(Json.Utf8.GetBytes(manifest));
            if (Json.Field<string>(plan, "expectedGmlManifestHash") != manifestHash || Json.Field<string>(source, "sourceFingerprint") != SourceFingerprint(root, id, version, manifestHash) || !remote && !SourceConfigurationMatches(root, id, version))
            {
                return ("SOURCE_CHANGED", 0, null, null);
            }

            if (!_identity.Accepts(Json.Write(plan), Session.Current.ProjectFingerprint(), DateTimeOffset.UtcNow) || !HasPinnedSdk())
            {
                return ("PLAN_STALE", 0, null, null);
            }

            string scene = Json.Field<string>(Json.Field<Fields>(plan, "output"), "scenePath");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!OutputAvailable(projectRoot, scene))
            {
                return ("DESTINATION_UNAVAILABLE", 0, null, null);
            }

            return (null, Json.Field<object[]>(Json.Parse(manifest), "files").Length, manifest, root);
#else
            await Task.Yield();
            return ("SDK_NOT_READY", 0, null, null);
#endif
        }

#if WORLDFLOW_PLATEAU_4_3_0
        private bool HasPinnedSdk()
        {
            var sdk = PackageInfo.GetAllRegisteredPackages().SingleOrDefault(package => package.name == DependencyCatalog.PackageId);
            return sdk != null && sdk.version == DependencyCatalog.Version && sdk.isDirectDependency && sdk.source == PackageSource.Git && sdk.git?.hash == DependencyCatalog.Commit;
        }

        private bool SourceConfigurationMatches(string root, string id, string version)
        {
            return root == Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ROOT") && id == Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_ID") && version == Environment.GetEnvironmentVariable("WORLDFLOW_LOCAL_DATASET_VERSION");
        }

        private Dictionary<string, int[]> FeatureLods(Fields selection)
        {
            return Json.Field<object[]>(selection, "featureSettings").Cast<Fields>().ToDictionary(feature => Json.Field<string>(feature, "featureType"), feature => Json.Field<object[]>(feature, "lods").Cast<int>().ToArray(), StringComparer.Ordinal);
        }

        private string SourceFingerprint(string root, string id, string version, string manifest)
        {
            string path = root == null ? null : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (path != null && Application.platform == RuntimePlatform.WindowsEditor)
            {
                path = path.ToUpperInvariant();
            }
            return Digests.Sha256(Json.Utf8.GetBytes(Json.Write(Json.Object(("path", path), ("dataset", id), ("version", version), ("manifest", manifest)))));
        }

        private Fields Attribution(Fields planning, string datasetId)
        {
            var dataset = CatalogDataset(planning, datasetId);
            string origin = Json.Field<string>(dataset, "sourceUrl");
            string license = Json.Field<string>(dataset, "licenseId");
            string terms = Json.Field<string>(dataset, "licenseUrl");
            return Json.Object(("sourceUrl", WireValues.Url(origin) ? origin : null), ("licenseId", license != null && WireValues.Text(license) ? license : null), ("licenseUrl", WireValues.Url(terms) ? terms : null));
        }

        private Fields CatalogDataset(Fields planning, string datasetId)
        {
            var catalog = Json.Field<Fields>(Json.Field<Fields>(planning, "catalog"), "result");
            return Json.Field<object[]>(catalog, "datasets")?.OfType<Fields>().FirstOrDefault(item => Json.Field<string>(item, "datasetId") == datasetId);
        }

        private bool CandidateAccepts(Fields draft, string project)
        {
            try
            {
                return _identity.Accepts(_identity.Seal(draft), project, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is ArgumentException)
            {
                return false;
            }
        }

        private bool TryGetRequirements(object[] features, Fields selectedQuality, out Dictionary<string, int[]> requirements)
        {
            requirements = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var chosenFeatures = Json.Field<object[]>(selectedQuality, "requestedFeatureTypes");
            var observedFeatures = Json.Field<object[]>(selectedQuality, "verifiedFeatureTypes");
            var chosenLods = Json.Field<object[]>(selectedQuality, "requestedLods");
            // Phase 2で未指定のLODはSDKで確認する。明示済みの範囲と地物はここで固定する。
            if (chosenFeatures == null || observedFeatures == null || chosenLods == null || features.Length == 0 || features.Length > 64) return false;

            foreach (var item in features)
            {
                var fields = item as Fields;
                string feature = Json.Field<string>(fields, "featureType");
                var values = Json.Field<object[]>(fields, "lods");
                bool matches = WireValues.Id(feature) && observedFeatures.Contains(feature) && values != null && values.All(value => value is int) && (chosenLods.Length == 0 || values.Length == chosenLods.Length && !values.Except(chosenLods).Any());
                // Phase 2の選択と矛盾した設定や重複をSDK設定として扱わない。
                if (!matches || !requirements.TryAdd(feature, values.Cast<int>().ToArray())) return false;
            }

            var available = requirements;
            return chosenFeatures.Length == 0 || chosenFeatures.Length == available.Count && chosenFeatures.All(item => item is string feature && available.ContainsKey(feature));
        }

        private bool MatchesZone(Fields file, int zone, CoordinateZone resolver)
        {
            var municipalities = Json.Field<object[]>(file, "municipalityCodes");
            return municipalities != null && municipalities.Length > 0 && municipalities.All(code => code is string municipality && resolver.FromMunicipality(municipality) == zone);
        }

        private bool OutputAvailable(string project, string scene)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(project, scene)));
            for (var directory = new DirectoryInfo(folder); directory != null; directory = directory.Parent)
            {
                try
                {
                    var attributes = File.GetAttributes(directory.FullName);
                    // 保存先は新規ディレクトリに限定する。経路上のファイルとリンクも拒否する。
                    if (directory.FullName == folder || (attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) return false;
                }
                catch (IOException exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException)
                {
                    // 未作成は許容し、残る上位ディレクトリの実体を検査する。
                }
            }

            return true;
        }

        private string SourceError(string message)
        {
            switch (message)
            {
                case "EMPTY_SOURCE":
                case "SOURCE_MAPPING_UNVERIFIED":
                case "SOURCE_PATH_UNVERIFIED":
                case "SOURCE_FEATURE_UNVERIFIED":
                case "SOURCE_FEATURE_MISMATCH":
                case "SOURCE_GRID_MISMATCH":
                case "SOURCE_CRS_MISMATCH":
                case "SOURCE_CRS_UNVERIFIED":
                case "SOURCE_LOD_UNVERIFIED":
                case "SOURCE_REFERENCE_UNVERIFIED":
                case "SOURCE_MUNICIPALITY_UNVERIFIED":
                case "SDK_LOD_RANGE_MISMATCH": return message;
                default: return "SOURCE_UNVERIFIED";
            }
        }
#endif

        private string Response(string requestId, string status, string code, Fields plan = null, int count = 0, string approvalState = null, Fields job = null, Fields progress = null, Fields result = null, Fields evidence = null, string[] artifacts = null)
        {
            return Json.Write(Json.Object(("schemaVersion", 1), ("requestId", requestId), ("status", status), ("code", code), ("plan", plan), ("expectedGmlCount", count), ("approvalState", approvalState), ("job", job), ("progress", progress), ("result", result), ("evidence", evidence), ("artifacts", artifacts)));
        }
    }
}
