using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
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
        private const string NoticeKeyPrefix = "DarumaPPAP.PlateauWorldFlow.InitialDiagnostic.";
        private const double RecoveryDelaySeconds = 10;
        private static readonly Bootstrap Instance = new Bootstrap();
        private readonly string _project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private readonly InstallGate _gate = new InstallGate();
        private JobStore _store;
        private ProjectSnapshot _beforeList;
        private InstallPlan _plan;
        private ListRequest _list;
        private AddRequest _add;
        private Task<(string Failure, Dictionary<string, object>[] Tools)> _tools;
        private string _upmFailure;
        private bool _inspectQueued = true;
        private bool _compileObservedWithSdk;
        private double _nextInspection;

        static Bootstrap()
        {
            EditorApplication.update += Instance.Tick;
            Events.registeredPackages += Instance.OnPackagesRegistered;
            CompilationPipeline.compilationFinished += Instance.OnCompilationFinished;
        }

        [MenuItem(Menu + "Check Dependencies")]
        private static void CheckDependencies() => Instance.QueueInspection();

        [MenuItem(Menu + "Review and Install SDK")]
        private static void ReviewInstall() => Instance.Install();

        [MenuItem(Menu + "Review and Install SDK", true)]
        private static bool CanReviewInstall() => !Application.isBatchMode && Instance._plan != null && Instance._add == null && Instance._list == null && !Instance._inspectQueued;

        private void OnPackagesRegistered(PackageRegistrationEventArgs _) => QueueInspection();

        private void OnCompilationFinished(object _)
        {
            _compileObservedWithSdk = IsPinnedSdkRegistered();
            if (_nextInspection > 0)
            {
                QueueInspection();
            }
        }

        private bool IsPinnedSdkRegistered()
        {
            return PackageInfo.GetAllRegisteredPackages().Any(package => package.name == DependencyCatalog.PackageId && package.version == DependencyCatalog.Version && package.git?.hash == DependencyCatalog.Commit);
        }

        private void QueueInspection()
        {
            _plan = null;
            _inspectQueued = true;
            _nextInspection = 0;
        }

        private void Tick()
        {
            // Packageの状態が変動する間は診断の確定と通知を保留する。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;

            try
            {
                if (_add != null)
                {
                    // 追加結果を永続化してから、次のUPM Requestを発行する。
                    if (!_add.IsCompleted) return;
                    _upmFailure = _add.Status == StatusCode.Success ? null : ClassifyUpmFailure(_add.Error?.message);
                    _add = null;
                    _store.ObserveRequest(_upmFailure);
                    QueueInspection();
                }

                if (_list != null)
                {
                    // 同じ診断に使うPackage一覧とツール検査の両方を待つ。
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
            if (_store == null)
            {
                _store = new JobStore(_project);
                // Reload後に既に登録済みのSDKを確認できる場合は、新しいDomainで照合する。
                _compileObservedWithSdk |= IsPinnedSdkRegistered();
            }
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
            // List中にPackage構成が変わった場合は、その結果から新しいPlanを作らない。
            if (!_beforeList.SameFiles(snapshot))
            {
                QueueInspection();
                return;
            }

            bool listed = request.Status == StatusCode.Success;
            var sdk = listed ? request.Result.FirstOrDefault(package => package.name == DependencyCatalog.PackageId) : null;
            var tools = _tools.IsCompletedSuccessfully ? _tools.Result : ("GIT_PROBE_FAILED", ToolProbe.UnknownTools());
            string failure = _upmFailure ?? tools.Item1;
            var diagnostic = new SdkDiagnostic(_store.Fingerprint, snapshot, sdk, listed, SupportedEditor(), failure, _upmFailure == null ? "ENVIRONMENT" : "UPM", tools.Item2);
            _store.WriteInventory(diagnostic);
            _store.RemovePlan();
            _plan = null;
            var job = _store.ReadPending();
            if (job != null)
            {
                Reconcile(job, snapshot, diagnostic, listed);
            }
            else if (diagnostic.Status == "MISSING" && tools.Item1 == null && !_inspectQueued)
            {
                _plan = InstallPlan.Create(Context(snapshot, true));
                _store.WritePlan(_plan);
            }

            if (_nextInspection == 0)
            {
                string proposal = _plan == null ? "" : $" 導入候補 {_plan.Source} (Package {DependencyCatalog.Version})。";
                string manual = failure == null ? "" : $" {failure}。公式Releaseの .tgz を使った手動導入を確認してください。 {DependencyCatalog.Release}";
                Debug.Log($"[WorldFlow] SDK診断 {diagnostic.Status}。{proposal}{manual}");
            }
            ShowInitialNotice(diagnostic, job != null);
        }

        private void ShowInitialNotice(SdkDiagnostic diagnostic, bool hasPendingJob)
        {
            // 診断・UPM処理の途中では初回通知を消費せず、安全な次の診断に委ねる。
            if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || hasPendingJob || _add != null || _list != null || _inspectQueued) return;
            // SDKが不足している場合だけ通知済みにする。その他の診断結果では記録しない。
            if (diagnostic.Status != "MISSING") return;

            var package = PackageInfo.FindForAssembly(typeof(Bootstrap).Assembly);
            string key = NoticeKeyPrefix + _store.Fingerprint + "." + package?.version + "." + package?.packageId;
            // Editor再起動後も通知履歴を保持し、ダイアログ表示前に記録して再入を防ぐ。
            if (EditorPrefs.GetBool(key, false)) return;
            EditorPrefs.SetBool(key, true);

            string instructions = _plan == null
                ? $"導入元: {DependencyCatalog.Source}\nPackage: {DependencyCatalog.Version}\n\nGitとGit LFSの状態を確認し、Tools > PLATEAU WorldFlow > Check Dependencies で再診断してください。"
                : $"導入元: {_plan.Source}\nPackage: {DependencyCatalog.Version}\n\n追加内容とProjectへの影響は、Tools > PLATEAU WorldFlow > Review and Install SDK で確認できます。";
            EditorUtility.DisplayDialog("PLATEAU SDKのセットアップ案内", "このProjectにはPLATEAU SDKが導入されていません。\n\n" + instructions, "閉じる");
        }

        private void Reconcile(Dictionary<string, object> job, ProjectSnapshot snapshot, SdkDiagnostic diagnostic, bool listed)
        {
            var plan = InstallPlan.Parse(Json.Write(job["plan"]));
            if (plan.Project != _store.Fingerprint)
            {
                Debug.LogWarning("[WorldFlow] 保存済みJobは別Projectに属します。追加を停止しています。");
                return;
            }

            bool settled = _nextInspection > 0;
            var observation = (diagnostic.ResolvedPinnedSdk, diagnostic.Status, diagnostic.Version, diagnostic.Inventory);
            bool compileFailed = EditorUtility.scriptCompilationFailed;
            bool compileSettled = settled && (_compileObservedWithSdk || compileFailed);
            if (_store.TryReconcile(job, plan, snapshot, observation, listed, compileFailed, settled, compileSettled))
            {
                _upmFailure = null;
                _nextInspection = 0;
                return;
            }

            if (!settled)
            {
                _inspectQueued = true;
                _nextInspection = EditorApplication.timeSinceStartup + RecoveryDelaySeconds;
            }
            else
            {
                Debug.LogWarning($"[WorldFlow] 保存済みJobの結果を確定できません。SDK追加を停止しています。Package Managerで状態を確認し、Check Dependenciesで再照合してください。 plan={plan.Id}");
            }
        }

        private void Install()
        {
            // メニューがコードから呼ばれる場合にも、実行可否をここで確認する。
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
            // UPMのメッセージには非公開パスやURLが含まれ得るため、定義済みコードだけを保存する。
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
