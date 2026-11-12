using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DarumaPPAP.PlateauWorldFlow
{
    [InitializeOnLoad]
    internal sealed class Bootstrap
    {
        private const string Menu = "Tools/PLATEAU WorldFlow/";
        private const string SessionKey = "DarumaPPAP.PlateauWorldFlow.InitialDiagnostic.";
        private static readonly Bootstrap Instance = new Bootstrap();
        private readonly string _project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private readonly InstallGate _gate = new InstallGate();
        private JobStore _store;
        private ProjectSnapshot _beforeList;
        private InstallPlan _plan;
        private ListRequest _list;
        private AddRequest _add;
        private Task<string> _tools;
        private string _upmFailure;
        private bool _inspectQueued = true;
        private double _nextInspection;

        static Bootstrap()
        {
            EditorApplication.update += Instance.Tick;
            Events.registeredPackages += Instance.OnPackagesRegistered;
        }

        [MenuItem(Menu + "Check Dependencies")]
        private static void CheckDependencies() => Instance.QueueInspection();

        [MenuItem(Menu + "Review and Install SDK")]
        private static void ReviewInstall() => Instance.Install();

        [MenuItem(Menu + "Review and Install SDK", true)]
        private static bool CanReviewInstall() => !Application.isBatchMode && Instance._plan != null && Instance._add == null && Instance._list == null && !Instance._inspectQueued;

        private void OnPackagesRegistered(PackageRegistrationEventArgs _) => QueueInspection();

        private void QueueInspection()
        {
            _plan = null;
            _inspectQueued = true;
            _nextInspection = 0;
        }

        private void Tick()
        {
            // Package state can change during editor startup, compilation, and play mode transitions.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;

            try
            {
                if (_add != null)
                {
                    // Observe the sole mutation to completion before issuing a new UPM request.
                    if (!_add.IsCompleted) return;
                    _upmFailure = _add.Status == StatusCode.Success ? null : ClassifyUpmFailure(_add.Error?.message);
                    _add = null;
                    _store.ObserveRequest(_upmFailure);
                    QueueInspection();
                }

                if (_list != null)
                {
                    // Both read-only probes must be available for one consistent inventory.
                    if (!_list.IsCompleted || !_tools.IsCompleted) return;
                    FinishInspection();
                }

                if (_inspectQueued && EditorApplication.timeSinceStartup >= _nextInspection)
                {
                    StartInspection();
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is InvalidOperationException)
            {
                _list = null;
                _inspectQueued = false;
                _plan = null;
                Debug.LogWarning("[WorldFlow] 診断を中断しました。ProjectのパッケージファイルとSetup記録を確認し、Check Dependenciesを再実行してください。 " + error.GetType().Name);
            }
        }

        private void StartInspection()
        {
            _store ??= new JobStore(_project);
            _beforeList = new ProjectSnapshot(_project);
            _inspectQueued = false;
            _list = Client.List(true, true);
            _tools = ToolProbe.Start();
        }

        private void FinishInspection()
        {
            var snapshot = new ProjectSnapshot(_project);
            var request = _list;
            _list = null;
            // If UPM rewrote either file during List, the result cannot justify a plan against the new snapshot.
            if (!_beforeList.SameFiles(snapshot))
            {
                QueueInspection();
                return;
            }

            bool listed = request.Status == StatusCode.Success;
            var sdk = listed ? request.Result.FirstOrDefault(package => package.name == DependencyCatalog.PackageId) : null;
            string toolStatus = _tools.IsFaulted ? "GIT_PROBE_FAILED" : _tools.Result;
            string failure = _upmFailure ?? toolStatus;
            var diagnostic = new SdkDiagnostic(_store.Fingerprint, snapshot, sdk, listed, SupportedEditor(), failure, _upmFailure == null ? "ENVIRONMENT" : "UPM");
            _store.WriteInventory(diagnostic);
            _store.RemovePlan();
            _plan = null;
            var job = _store.ReadPending();
            if (job != null)
            {
                Reconcile(job, snapshot, diagnostic, listed);
            }
            else if (diagnostic.Status == "MISSING" && toolStatus == null && !_inspectQueued)
            {
                _plan = InstallPlan.Create(Context(snapshot, true));
                _store.WritePlan(_plan);
            }

            string key = SessionKey + _store.Fingerprint + "." + PackageInfo.FindForAssembly(typeof(Bootstrap).Assembly)?.version;
            bool previouslyNotified = SessionState.GetBool(key, false);
            SessionState.SetBool(key, true);
            if (!previouslyNotified || _nextInspection == 0)
            {
                string proposal = _plan == null ? "" : $" 導入候補 {_plan.Source} (Package {DependencyCatalog.Version})。";
                string manual = failure == null ? "" : $" {failure}。公式Releaseの .tgz を使った手動導入を確認してください。 {DependencyCatalog.Release}";
                Debug.Log($"[WorldFlow] SDK診断 {diagnostic.Status}。{proposal}{manual}");
            }
        }

        private void Reconcile(Dictionary<string, object> job, ProjectSnapshot snapshot, SdkDiagnostic diagnostic, bool listed)
        {
            var plan = InstallPlan.Parse(Json.Write(job["plan"]));
            if (plan.Project != _store.Fingerprint)
            {
                Debug.LogWarning("[WorldFlow] 保存済みJobは別Projectに属します。追加を停止しています。");
                return;
            }

            bool compileSettled = diagnostic.Status == "READY" || EditorUtility.scriptCompilationFailed;
            var stage = Json.Field<string>(job, "stage");
            bool requestFinished = stage == "ADD_SUCCEEDED" || stage == "ADD_FAILED";
            if (listed && ((diagnostic.ResolvedPinnedSdk && compileSettled) || stage == "ADD_FAILED" || (requestFinished && !diagnostic.ResolvedPinnedSdk)))
            {
                string failure = JobStore.GetObservedFailure(job);
                if (failure == null && diagnostic.Status != "READY")
                {
                    failure = diagnostic.ResolvedPinnedSdk ? "COMPILE_OR_CAPABILITY_FAILED" : "PACKAGE_RESOLUTION_MISMATCH";
                }
                _store.Complete(job, plan, snapshot, diagnostic, failure);
                _upmFailure = null;
                return;
            }

            string recoveryKey = SessionKey + "recovery." + plan.Id;
            if (!SessionState.GetBool(recoveryKey, false))
            {
                SessionState.SetBool(recoveryKey, true);
                Debug.LogWarning($"[WorldFlow] 保存済みJobを照合しています。結果が確定するまでSDK追加を再発行しません。 plan={plan.Id}");
            }
            _inspectQueued = true;
            _nextInspection = EditorApplication.timeSinceStartup + 10;
        }

        private void Install()
        {
            // The menu can also be invoked programmatically. These checks are part of the execution path.
            if (!CanReviewInstall()) return;
            var plan = _plan;
            try
            {
                var outcome = _gate.Run(plan, () => CurrentContext(plan), Application.isBatchMode,
                    () => EditorUtility.DisplayDialog("PLATEAU SDKの追加を承認", $"対象Project\n{_project}\n\n導入元\n{plan.Source}\nTag {DependencyCatalog.Tag} / Package {DependencyCatalog.Version}\n期待するCommit {DependencyCatalog.Commit}\n\nPlan {plan.Id} (Revision {plan.Revision})\nHash {plan.Hash}\n有効期限 {Digests.Timestamp(plan.Expires)}\n\nPackages/manifest.json に直接依存を追加し、packages-lock.json を更新します。推移的依存の変更、再コンパイル、Domain Reloadが発生する場合があります。", "SDKを追加", "保留"),
                    () => _store.Begin(plan, new ProjectSnapshot(_project)),
                    source => Dispatch(plan, source));
                Debug.Log($"[WorldFlow] SDK追加要求 {outcome}。");
                QueueInspection();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is InvalidOperationException)
            {
                Debug.LogWarning("[WorldFlow] SDK追加要求を停止しました。Check Dependenciesで状態を確認してください。 " + error.GetType().Name);
                QueueInspection();
            }
        }

        private void Dispatch(InstallPlan plan, string source)
        {
            var snapshot = new ProjectSnapshot(_project);
            if (!plan.CanExecute(Context(snapshot, CanAdd(plan, snapshot))))
            {
                _store.CancelBeforeDispatch(plan, snapshot);
                throw new InvalidOperationException("The project changed after reserving the job.");
            }

            _add = Client.Add(source);
        }

        private InstallContext CurrentContext(InstallPlan plan)
        {
            var snapshot = new ProjectSnapshot(_project);
            return Context(snapshot, CanAdd(plan, snapshot) && !_store.HasPending());
        }

        private bool CanAdd(InstallPlan plan, ProjectSnapshot snapshot)
        {
            bool editorIdle = !Application.isBatchMode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode;
            bool requestIdle = _plan == plan && _add == null && _list == null && !_inspectQueued;
            return editorIdle && requestIdle && !snapshot.HasSdk && SupportedEditor() && !PackageInfo.GetAllRegisteredPackages().Any(package => package.name == DependencyCatalog.PackageId);
        }

        private InstallContext Context(ProjectSnapshot snapshot, bool canAdd)
        {
            return new InstallContext(_store.Fingerprint, snapshot.ManifestHash, snapshot.LockHash, DateTimeOffset.UtcNow, canAdd);
        }

        private bool SupportedEditor()
        {
            var match = Regex.Match(Application.unityVersion, @"\A(\d+\.\d+\.\d+)f(\d+)\z", RegexOptions.CultureInvariant);
            return match.Success && Version.TryParse(match.Groups[1].Value, out var editor) && (editor > new Version(6000, 3, 10) || (editor == new Version(6000, 3, 10) && int.Parse(match.Groups[2].Value) >= 1));
        }

        internal static string ClassifyUpmFailure(string message)
        {
            string error = message?.ToLowerInvariant() ?? "";
            // UPM may include private paths and URLs in its message. Persist only a controlled code.
            if (error.Contains("git lfs") || error.Contains("git-lfs"))
            {
                return "GIT_LFS_FAILED";
            }
            if (error.Contains("could not resolve host") || error.Contains("failed to connect") || error.Contains("network") || error.Contains("timed out"))
            {
                return "NETWORK_UNAVAILABLE";
            }
            if (error.Contains("no git executable") || error.Contains("git was not found"))
            {
                return "GIT_UNAVAILABLE";
            }
            return "UPM_ADD_FAILED";
        }
    }
}
