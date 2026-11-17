using System;
using System.IO;
using System.Linq;
using DarumaPPAP.PlateauWorldFlow.Previewing;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using NUnit.Framework;
using UnityEngine;

namespace DarumaPPAP.PlateauWorldFlow
{
    /// <summary>
    /// 範囲案から生成したPNGを復号し、保存物とManifestの照合および失効したPreviewの拒否を検証する。
    /// </summary>
    internal sealed class PreviewTests
    {
        [Test]
        public void EvidenceMatchesPlanAndPreviewRevision()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string fingerprint = new string('1', 64);
            var now = DateTimeOffset.UtcNow;
            var dataset = new Dataset("test-city", null, null, null, null, null, null, null);
            var first = new GridCode("53394525");
            var second = new GridCode("53394526");
            var cell = new CoverageCell(first, new Extent(35.68, 139.68, 35.69, 139.69, "EPSG:4326"), null, null);
            var observation = new Observation(dataset, new[] { cell }, new[] { second });
            var catalog = new Catalog("LOCAL", "fixture", now, now.AddMinutes(1), new[] { observation });
            var coverage = new CoverageSnapshot(Guid.NewGuid().ToString("D"), fingerprint, catalog, observation, new[] { first.Value, second.Value }, 10);
            var plan = new AreaPlan(Guid.NewGuid().ToString("D"), 1, coverage, new[] { first.Value, second.Value }, "EPSG:4326");
            var session = new PreviewSession(root);
            session.Remember(plan, coverage);
            var previewId = Guid.NewGuid().ToString("D");
            var preview = session.Execute(previewId, "areas.preview", Json.Object(("planId", plan.Id), ("planRevision", 1)), now);
            Assert.That(preview.Status, Is.EqualTo("READY"));
            Assert.That(Directory.Exists(root), Is.False);

            try
            {
                var input = Json.Object(("previewId", previewId), ("width", 640), ("height", 448));
                var saved = session.Execute(Guid.NewGuid().ToString("D"), "areas.preview_png", input, now);
                Assert.That(saved.Status, Is.EqualTo("READY"));
                var result = Json.Parse(Json.Write(saved.Result));
                string pngPath = Path.Combine(root, Json.Field<string>(result, "relativePath"));
                byte[] bytes = File.ReadAllBytes(pngPath);
                var manifest = Json.Parse(File.ReadAllText(Path.ChangeExtension(pngPath, ".json")));
                Assert.That(Json.Field<string>(manifest, "sha256"), Is.EqualTo(Digests.Sha256(bytes)));
                Assert.That(Json.Field<int>(manifest, "byteLength"), Is.EqualTo(bytes.Length));
                Assert.That(Json.Field<string>(manifest, "planId"), Is.EqualTo(plan.Id));
                Assert.That(Json.Field<string>(manifest, "sourceReference"), Is.EqualTo("fixture"));
                Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(pngPath)).Count(), Is.EqualTo(2));

                var texture = new Texture2D(2, 2);
                try
                {
                    Assert.That(ImageConversion.LoadImage(texture, bytes), Is.True);
                    Assert.That(texture.width, Is.EqualTo(640));
                    Assert.That(texture.height, Is.EqualTo(448));
                    Assert.That((Color32)texture.GetPixel(24, 428), Is.EqualTo(new Color32(221, 233, 245, 255)));
                    var available = (Color32)texture.GetPixel(20, 53);
                    var selected = (Color32)texture.GetPixel(170, 53);
                    Assert.That(available, Is.EqualTo(new Color32(36, 145, 132, 255)));
                    Assert.That(selected, Is.EqualTo(new Color32(255, 215, 120, 255)));
                    Assert.That((Color32)texture.GetPixel(320, 223), Is.EqualTo(available));
                    Assert.That((Color32)texture.GetPixel(226, 223), Is.EqualTo(selected));

                    var unavailableOnly = new AreaPlan(Guid.NewGuid().ToString("D"), 1, coverage, new[] { second.Value }, "EPSG:4326");
                    var unselectedPreview = new Preview(Guid.NewGuid().ToString("D"), unavailableOnly, coverage);
                    Assert.That(ImageConversion.LoadImage(texture, new PngRenderer().Draw(unselectedPreview, 640, 448)), Is.True);
                    Assert.That((Color32)texture.GetPixel(320, 223), Is.EqualTo(available));
                    Assert.That((Color32)texture.GetPixel(226, 223), Is.EqualTo(new Color32(21, 94, 86, 255)));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                var revised = new AreaPlan(Guid.NewGuid().ToString("D"), 2, coverage, new[] { first.Value }, "EPSG:4326");
                session.Remember(revised, coverage);
                Assert.That(session.Execute(Guid.NewGuid().ToString("D"), "areas.preview_png", input, now).Code, Is.EqualTo("PLAN_STALE"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
