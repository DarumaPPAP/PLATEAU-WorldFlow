using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PLATEAU.CityImport.AreaSelector;
using PLATEAU.CityImport.Config;
using PLATEAU.CityImport.Config.PackageImportConfigs;
using PLATEAU.CityImport.Import;
using PLATEAU.Dataset;
using PLATEAU.PolygonMesh;
using PLATEAU.Util;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 承認済みPlanでSDKを実行し、個別GML通知とSceneの実体から結果を組み立てる。
    /// </summary>
    internal sealed class Runner
    {
        /// <summary>
        /// UnityメインスレッドでAdditive Sceneを用意してImportし、保存後の参照を照合する。
        /// </summary>
        internal async Task<(string State, bool SceneVerified, Fields[] Gml)> RunAsync(Fields plan, string root, string manifest, CancellationToken token, Action<Scene> stagingCreated, Action<int, int> progress, Action verifying)
        {
            var files = Json.Field<object[]>(Json.Parse(manifest), "files").Cast<Fields>().ToArray();
            var selection = Json.Field<Fields>(plan, "selection");
            var grids = Json.Field<object[]>(selection, "gridCodes").Cast<string>();
            int zone = Json.Field<int>(selection, "coordinateZoneId");
            string scenePath = Json.Field<string>(Json.Field<Fields>(plan, "output"), "scenePath");
            using var nativeGrids = GridCodeList.CreateFromGridCodesStr(grids);
            var source = new DatasetSourceConfigLocal(root);
            var area = new AreaSelectResult(new ConfigBeforeAreaSelect(source, zone), nativeGrids, AreaSelectResult.ResultReason.Confirm);
            var config = CityImportConfig.CreateWithAreaSelectResult(area);
            ApplySettings(config, selection, files);
            token.ThrowIfCancellationRequested();
            var sceneSave = new SceneSave();

            var previous = SceneManager.GetActiveScene();
            var staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var notifications = new Notifications(root, progress);
            bool sceneChanged = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            void ActiveSceneChanged(Scene before, Scene after)
            {
                if (after != staging)
                {
                    sceneChanged = true;
                    cancellation.Cancel();
                }
            }

            try
            {
                stagingCreated(staging);
                if (SceneManager.GetActiveScene() != staging)
                {
                    SceneManager.SetActiveScene(staging);
                }

                if (SceneManager.GetActiveScene() != staging)
                {
                    throw new InvalidOperationException("STAGING_SCENE_UNAVAILABLE");
                }

                SceneManager.activeSceneChanged += ActiveSceneChanged;
                EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
                try
                {
                    await CityImporter.ImportAsync(config, notifications, cancellation.Token, new[] { notifications });
                }
                finally
                {
                    SceneManager.activeSceneChanged -= ActiveSceneChanged;
                    EditorSceneManager.activeSceneChangedInEditMode -= ActiveSceneChanged;
                }

                verifying();
                var expected = new HashSet<string>(files.Select(file => Json.Field<string>(file, "relativePath")), StringComparer.Ordinal);
                if (sceneChanged || !staging.isLoaded || SceneManager.GetActiveScene() != staging || notifications.HasUnexpected(expected, staging))
                {
                    throw new InvalidOperationException("SCENE_OWNERSHIP_UNVERIFIED");
                }

                var results = files.Select(file =>
                {
                    string path = Json.Field<string>(file, "relativePath");
                    bool success = notifications.HasObjects(path, staging);
                    return Json.Object(("relativePath", path), ("state", success ? "SUCCEEDED" : "FAILED_OR_UNOBSERVED"));
                }).ToArray();
                int succeeded = results.Count(result => Json.Field<string>(result, "state") == "SUCCEEDED");
                bool saved = staging.rootCount > 0;
                bool verified = saved && new SceneStage(scenePath).SaveAndVerify(staging, previous, zone, sceneSave);
                string state = succeeded == files.Length && verified ? "COMPLETED" : saved ? "PARTIAL" : token.IsCancellationRequested ? "CANCELLED" : "FAILED";
                return (state, verified, results);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        private void ApplySettings(CityImportConfig config, Fields selection, Fields[] manifest)
        {
            foreach (var package in config.PackageImportConfigDict.ForEachPackagePair)
            {
                package.Value.ImportPackage = false;
            }

            foreach (var feature in Json.Field<object[]>(selection, "featureSettings").Cast<Fields>())
            {
                string id = Json.Field<string>(feature, "featureType");
                var package = config.GetConfigForPackage(DatasetAccessor.FeatureTypeToPackage(id));
                if (package == null)
                {
                    throw new InvalidOperationException("SOURCE_FEATURE_MISMATCH");
                }

                var lods = Json.Field<object[]>(feature, "lods").Cast<int>().ToArray();
                int max = manifest.Where(file => Json.Field<string>(file, "featureType") == id).SelectMany(file => Json.Field<object[]>(file, "lods").Cast<int>()).Max();
                package.ImportPackage = true;
                package.IncludeTexture = Json.Field<bool>(feature, "includeTexture");
                package.EnableTexturePacking = Json.Field<bool>(feature, "enableTexturePacking");
                package.DoSetMeshCollider = Json.Field<bool>(feature, "doSetMeshCollider");
                package.DoSetAttrInfo = Json.Field<bool>(feature, "doSetAttrInfo");
                package.MeshGranularity = Enum.Parse<MeshGranularity>(Json.Field<string>(feature, "meshGranularity"));
                package.LODRange = new LODRange(lods.Min(), lods.Max(), max);
                if (package.MeshGranularity.ToString() != Json.Field<string>(feature, "meshGranularity") || package.IncludeTexture != Json.Field<bool>(feature, "includeTexture") || package.EnableTexturePacking != Json.Field<bool>(feature, "enableTexturePacking"))
                {
                    throw new InvalidOperationException("SDK_SETTINGS_MISMATCH");
                }
            }
        }

        /// <summary>
        /// SDK通知を対象ファイル別に記録し、後でメインスレッドからScene帰属を検証する。
        /// </summary>
        private sealed class Notifications : IPostGmlImportProcessor, IProgressDisplay
        {
            private readonly string _root;
            private readonly Dictionary<string, GameObject[]> _observed = new Dictionary<string, GameObject[]>(StringComparer.Ordinal);
            private bool _duplicate;
            private readonly Action<int, int> _progress;
            private int _lastPercent;

            internal Notifications(string root, Action<int, int> progress)
            {
                _root = root;
                _progress = progress;
            }

            public void OnGmlImported(GmlImportResult result)
            {
                string relative = Path.GetRelativePath(_root, result.GmlFile.Path).Replace('\\', '/');
                lock (_observed)
                {
                    if (!_observed.TryAdd(relative, result.GeneratedObjects.ToArray()))
                    {
                        _duplicate = true;
                    }
                    _progress(_observed.Count, _lastPercent);
                }
            }

            public void SetProgress(string progressName, float percentage, string message)
            {
                // SDKから別スレッドで通知される。ファイル名やメッセージは共有応答に載せない。
                if (float.IsNaN(percentage) || float.IsInfinity(percentage)) return;

                lock (_observed)
                {
                    _lastPercent = (int)Math.Clamp(percentage, 0f, 100f);
                    _progress(_observed.Count, _lastPercent);
                }
            }

            internal bool HasUnexpected(HashSet<string> expected, Scene target)
            {
                lock (_observed)
                {
                    return _duplicate || _observed.Any(pair => !expected.Contains(pair.Key) || pair.Value.Any(item => item != null && item.scene != target));
                }
            }

            internal bool HasObjects(string path, Scene target)
            {
                lock (_observed)
                {
                    return _observed.TryGetValue(path, out var generated) && generated.Length > 0 && generated.All(item => item != null && item.scene == target);
                }
            }
        }
    }
}
