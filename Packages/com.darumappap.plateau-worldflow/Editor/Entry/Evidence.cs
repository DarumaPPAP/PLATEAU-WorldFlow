using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DarumaPPAP.PlateauWorldFlow.Import;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Entry
{
    /// <summary>
    /// Jobに属する保存Sceneと生成Assetを識別し、成果物の内容と後から照合できる証跡を組み立てる。
    /// </summary>
    internal sealed class Evidence
    {
        private readonly string _project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>
        /// Import直後の保存Scene、生成Asset、メッシュ等の件数を記録する。
        /// </summary>
        internal Fields Record(Fields job, Fields plan, Fields result, string gmlResult, Fields attribution)
        {
            Fields evidence = Identity(job);
            var source = Json.Field<Fields>(plan, "source");
            evidence["checkedAt"] = result["checkedAt"];
            evidence["phase2"] = plan["phase2"];
            evidence["sdkVersion"] = plan["sdkVersion"];
            evidence["datasetSource"] = Json.Field<string>(source, "kind");
            evidence["datasetId"] = Json.Field<string>(source, "worldFlowDatasetId");
            evidence["datasetVersion"] = Json.Field<string>(source, "datasetVersion");
            evidence["attribution"] = attribution;
            evidence["selection"] = plan["selection"];
            evidence["sceneVerification"] = result["sceneVerification"];
            evidence["gml"] = result["gml"];
            evidence["gmlResultSha256"] = TextHash(gmlResult);
            evidence["captureStatus"] = "NOT_RUN";
            evidence["captures"] = Array.Empty<object>();
            var saved = Snapshot(job);
            evidence["scene"] = saved.Scene;
            evidence["assets"] = saved.Assets;
            var scene = SceneManager.GetSceneByPath(ScenePath(job));
            evidence["measurements"] = scene.IsValid() && scene.isLoaded ? Measure(scene) : null;
            return evidence;
        }

        /// <summary>
        /// JobとPlanの結合、GML件数、および保存ファイルのハッシュを再照合する。
        /// </summary>
        internal bool Verify(Fields job, Fields result, Fields evidence, Fields plan, string manifest, string gmlResult)
        {
            if (!Matches(job, result) || !Matches(job, evidence) || Json.Field<string>(result, "state") != Json.Field<string>(job, "state") || Json.Field<string>(plan, "planHash") != Json.Field<string>(job, "planHash"))
            {
                return false;
            }

            string manifestHash = TextHash(manifest);
            var source = Json.Field<Fields>(plan, "source");
            if (!Equals(evidence.GetValueOrDefault("datasetId"), source["worldFlowDatasetId"]) || !Equals(evidence.GetValueOrDefault("datasetVersion"), source["datasetVersion"]) || !Equals(evidence.GetValueOrDefault("datasetSource"), source["kind"]) || !Equals(evidence.GetValueOrDefault("sceneVerification"), result.GetValueOrDefault("sceneVerification")) || Json.Write(evidence.GetValueOrDefault("selection")) != Json.Write(plan["selection"]))
            {
                return false;
            }

            var gml = Json.Field<Fields>(result, "gml");
            int expected = Json.Field<int>(gml, "expected");
            int succeeded = Json.Field<int>(gml, "succeeded");
            int unverified = Json.Field<int>(gml, "unverified");
            bool complete = Json.Field<string>(job, "state") == "COMPLETED";
            if (manifestHash != Json.Field<string>(plan, "expectedGmlManifestHash") || Json.Field<object[]>(Json.Parse(manifest), "files")?.Length != expected || succeeded < 0 || unverified < 0 || (long)succeeded + unverified != expected || complete && (expected == 0 || unverified != 0 || Json.Field<string>(result, "sceneVerification") != "VERIFIED"))
            {
                return false;
            }

            if (Json.Field<string>(evidence, "gmlResultSha256") != TextHash(gmlResult) || !evidence.TryGetValue("gml", out var totals) || Json.Write(totals) != Json.Write(gml) || !GmlResultsMatch(manifest, gmlResult, expected, succeeded))
            {
                return false;
            }

            var current = Snapshot(job);
            return (!complete || current.Scene != null) && evidence.TryGetValue("scene", out var scene) && Json.Write(scene) == Json.Write(current.Scene)
                && evidence.TryGetValue("assets", out var assets) && Json.Write(assets) == Json.Write(current.Assets);
        }

        /// <summary>
        /// 共有応答に含められる最小のJob／Planひも付けを生成する。
        /// </summary>
        internal Fields Identity(Fields job)
        {
            return Json.Object(("schemaVersion", 1), ("jobId", job["jobId"]), ("importPlanId", job["importPlanId"]), ("projectFingerprint", job["projectFingerprint"]), ("planHash", job["planHash"]), ("operationKey", job["operationKey"]));
        }

        private bool Matches(Fields job, Fields candidate)
        {
            return Identity(job).All(field => candidate.TryGetValue(field.Key, out var value) && Equals(field.Value, value));
        }

        private bool GmlResultsMatch(string expectedManifest, string outcomes, int total, int succeeded)
        {
            var expected = Json.Field<object[]>(Json.Parse(expectedManifest), "files").OfType<Fields>().Select(item => Json.Field<string>(item, "relativePath")).ToArray();
            var actual = Json.Field<object[]>(Json.Parse(outcomes), "files")?.OfType<Fields>().ToArray();
            // 対象のGMLと保存された各結果が一対一で一致しなければ件数を信用できない。
            if (actual == null || expected.Length != total || actual.Length != total || expected.Any(string.IsNullOrEmpty) || expected.Distinct(StringComparer.Ordinal).Count() != total) return false;

            var paths = actual.Select(item => Json.Field<string>(item, "relativePath"));
            var states = actual.Select(item => Json.Field<string>(item, "state")).ToArray();
            return expected.SequenceEqual(paths) && states.All(state => state == "SUCCEEDED" || state == "FAILED_OR_UNOBSERVED") && states.Count(state => state == "SUCCEEDED") == succeeded;
        }

        private string TextHash(string text)
        {
            return Digests.Sha256(Json.Utf8.GetBytes(text));
        }

        private (Fields Scene, Fields[] Assets) Snapshot(Fields job)
        {
            string relative = ScenePath(job);
            string folder = relative.Substring(0, relative.LastIndexOf('/'));
            string directory = Path.Combine(_project, folder);
            var ancestors = new DirectoryInfo(directory);
            for (var current = ancestors; current != null; current = current.Parent)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Evidence output traversal is unverified.");
                }
            }

            // 保存できなかったJobも記録する。存在しないSceneのハッシュやGUIDは作らない。
            if (!Directory.Exists(directory)) return (null, Array.Empty<Fields>());

            var files = Directory.EnumerateFileSystemEntries(directory).OrderBy(file => file, StringComparer.Ordinal).ToArray();
            var assets = new List<Fields>();
            Fields scene = null;
            foreach (string file in files)
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    throw new InvalidDataException("Evidence contains an unexpected output entry.");
                }

                string assetPath = folder + "/" + Path.GetFileName(file);
                var digest = Digest(assetPath, file);
                if (Path.GetFileName(file) == "City.unity")
                {
                    digest["guid"] = AssetDatabase.AssetPathToGUID(relative);
                    scene = digest;
                }
                else
                {
                    assets.Add(digest);
                }
            }

            return (scene, assets.ToArray());
        }

        private string ScenePath(Fields job)
        {
            return $"{PlanIdentity.OutputRoot}/{job["importPlanId"]}/City.unity";
        }

        private Fields Digest(string relative, string absolute)
        {
            using var stream = File.OpenRead(absolute);
            using var hash = SHA256.Create();
            return Json.Object(("relativePath", relative), ("byteLength", stream.Length), ("sha256", BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()));
        }

        private Fields Measure(Scene scene)
        {
            var transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            int renderers = transforms.Sum(transform => transform.GetComponents<Renderer>().Length);
            int meshFilters = transforms.Sum(transform => transform.GetComponents<MeshFilter>().Count(filter => filter.sharedMesh != null));
            return Json.Object(("gameObjects", transforms.Length), ("renderers", renderers), ("meshFilters", meshFilters));
        }
    }
}
