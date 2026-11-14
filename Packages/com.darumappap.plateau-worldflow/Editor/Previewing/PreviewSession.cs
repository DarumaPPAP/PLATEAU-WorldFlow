using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;

namespace DarumaPPAP.PlateauWorldFlow.Previewing
{
    /// <summary>
    /// 現在有効な範囲案を概略図へ変換し、明示要求された場合にだけPNGとManifestを保存する。
    /// </summary>
    internal sealed class PreviewSession
    {
        private const string RelativeRoot = "Library/PLATEAUWorldFlow/Preview/";
        private readonly string _project;
        private readonly Dictionary<string, (AreaPlan Plan, CoverageSnapshot Snapshot)> _plans = new Dictionary<string, (AreaPlan, CoverageSnapshot)>(StringComparer.Ordinal);
        private readonly Dictionary<string, Preview> _previews = new Dictionary<string, Preview>(StringComparer.Ordinal);

        internal PreviewSession(string project) => _project = Path.GetFullPath(project);

        /// <summary>
        /// 検証を完了した範囲案と元の観測を保持する。
        /// </summary>
        internal void Remember(AreaPlan plan, CoverageSnapshot snapshot)
        {
            foreach (var id in _plans.Where(pair => pair.Value.Snapshot.DatasetId == snapshot.DatasetId && pair.Value.Snapshot.Catalog.Reference == snapshot.Catalog.Reference && pair.Value.Snapshot.Catalog.Source == snapshot.Catalog.Source).Select(pair => pair.Key).ToArray())
            {
                _plans.Remove(id);
            }

            Trim(_plans);
            _plans.Add(plan.Id, (plan, snapshot));
        }

        /// <summary>
        /// 指定版のPlanを概略図に変換するか、既に発行した概略図からPNGを保存する。
        /// </summary>
        internal (string Status, object Result, object Evidence, string Code, string Location) Execute(string requestId, string operation, Dictionary<string, object> input, DateTimeOffset now)
        {
            if (operation == "areas.preview")
            {
                string planId = Json.Field<string>(input, "planId");
                int revision = Json.Field<int>(input, "planRevision");
                // 不正なPlan参照は保存済み計画と照合する前に拒否する。
                if (input.Count != 2 || !WireValues.Uuid(planId) || revision < 1) return ("FAILED", null, null, "INVALID_INPUT", "input");

                // 別の版や期限外のPlanから概略図を生成しない。
                if (!_plans.TryGetValue(planId, out var bound) || bound.Plan.Revision != revision || !Fresh(bound.Snapshot.Catalog, now)) return ("BLOCKED", null, null, "PLAN_STALE", "planId");

                // 同一IDで既存の概略図を書き換えない。
                if (_previews.ContainsKey(requestId)) return ("FAILED", null, null, "INVALID_INPUT", "input");

                var preview = new Preview(requestId, bound.Plan, bound.Snapshot);
                Trim(_previews);
                _previews.Add(requestId, preview);
                return ("READY", preview.ToJson(), Evidence("AREA_PREVIEW", bound.Snapshot.Catalog, now), null, null);
            }

            string previewId = Json.Field<string>(input, "previewId");
            int width = Json.Field<int>(input, "width");
            int height = Json.Field<int>(input, "height");
            // 既知の画像寸法とPreview参照だけを受け付ける。
            if (input.Count != 3 || !WireValues.Uuid(previewId)) return ("FAILED", null, null, "INVALID_INPUT", "input");

            // 上限外の画像にメモリを割り当てない。
            if (width < 1 || width > 1024 || height < 1 || height > 1024) return ("FAILED", null, null, "LIMIT_EXCEEDED", width < 1 || width > 1024 ? "width" : "height");

            // 元Planの更新や期限切れにより、Previewも保存不可になる。
            if (!_previews.TryGetValue(previewId, out var item) || !_plans.ContainsKey(item.Plan.Id) || !Fresh(item.Snapshot.Catalog, now)) return ("BLOCKED", null, null, "PLAN_STALE", "previewId");

            try
            {
                return ("READY", Save(requestId, item, width, height, now), Evidence("PNG_ARTIFACT", item.Snapshot.Catalog, now), null, null);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is UnityEngine.UnityException)
            {
                return ("FAILED", null, null, "ARTIFACT_WRITE_FAILED", "relativePath");
            }
        }

        private bool Fresh(Catalog catalog, DateTimeOffset now) => now >= catalog.CheckedAt && now < catalog.ValidUntil;

        private object Evidence(string kind, Catalog catalog, DateTimeOffset now) =>
            Json.Object(("kind", kind), ("source", catalog.Source == "LOCAL" ? "LOCAL_DATASET" : "REMOTE_DATASET"), ("checkedAt", Digests.Timestamp(now)));

        private void Trim<T>(Dictionary<string, T> items)
        {
            if (items.Count >= 64)
            {
                items.Remove(items.Keys.First());
            }
        }

        private object Save(string id, Preview preview, int width, int height, DateTimeOffset now)
        {
            string relative = RelativeRoot + id + ".png";
            var png = new PngRenderer().Draw(preview, width, height);
            var result = Json.Object(("artifactId", id), ("relativePath", relative), ("mimeType", "image/png"), ("width", width),
                ("height", height), ("sha256", Digests.Sha256(png)), ("byteLength", png.Length), ("createdAt", Digests.Timestamp(now)),
                ("sourceDatasetId", preview.Snapshot.DatasetId), ("planId", preview.Plan.Id), ("planRevision", preview.Plan.Revision),
                ("crs", preview.Extent?.Crs), ("deliveryStatus", "ARTIFACT_ONLY"));
            var manifest = new Dictionary<string, object>(result, StringComparer.Ordinal)
            {
                ["source"] = preview.Snapshot.Catalog.Source == "LOCAL" ? "LOCAL_DATASET" : "REMOTE_DATASET",
                ["sourceReference"] = preview.Snapshot.Catalog.Reference,
                ["extent"] = preview.Extent?.ToJson()
            };

            string directory = _project;
            foreach (var segment in RelativeRoot.TrimEnd('/').Split('/'))
            {
                directory = Path.Combine(directory, segment);
                var info = Directory.CreateDirectory(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Invalid evidence directory.");
                }
            }

            string pngPath = Path.Combine(directory, id + ".png");
            string manifestPath = Path.Combine(directory, id + ".json");
            string tempSuffix = "." + Guid.NewGuid().ToString("N") + ".tmp";
            string temporaryPng = pngPath + tempSuffix;
            string temporaryManifest = manifestPath + tempSuffix;
            bool published = false;
            try
            {
                WriteNew(temporaryPng, png);
                WriteNew(temporaryManifest, Json.Utf8.GetBytes(Json.Write(manifest)));
                File.Move(temporaryPng, pngPath);
                published = true;
                File.Move(temporaryManifest, manifestPath);
            }
            catch
            {
                if (published)
                {
                    File.Delete(pngPath);
                }

                throw;
            }
            finally
            {
                File.Delete(temporaryPng);
                File.Delete(temporaryManifest);
            }

            return result;
        }

        private void WriteNew(string path, byte[] data)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(data, 0, data.Length);
        }
    }
}
