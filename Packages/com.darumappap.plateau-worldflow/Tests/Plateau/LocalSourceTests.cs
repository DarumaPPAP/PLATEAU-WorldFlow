using System;
using System.IO;
using System.Linq;
using System.Threading;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 実SDKを使って、ローカルのメッシュ境界と未確認LODの扱いを検証する。
    /// </summary>
    internal sealed class LocalSourceTests
    {
        [Test]
        public void InspectReadsGridAndFeatureWithoutParsingGml()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string building = Directory.CreateDirectory(Path.Combine(root, "udx", "bldg")).FullName;
            try
            {
                File.WriteAllText(Path.Combine(building, "53394525_bldg_6697_op.gml"), "filename metadata fixture");
                var catalog = new LocalSource("fixture", "test-city", root).Inspect(null, CancellationToken.None);
                var observation = catalog.Observations.Single();
                Assert.That(observation.TryGetCell("53394525", out var cell), Is.True);
                Assert.That(cell.Extent.West, Is.EqualTo(139.6875).Within(0.000001));
                Assert.That(cell.Extent.South, Is.EqualTo(35.6833333333).Within(0.000001));
                Assert.That(cell.Extent.Crs, Is.Null);
                Assert.That(cell.FeatureTypes, Is.EqualTo(new[] { "bldg" }));
                Assert.That(cell.AvailableLods, Is.Null);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void InspectChecksCancellationBeforeAccessingFiles()
        {
            var source = new LocalSource("fixture", "test-city", Path.GetTempPath());
            Assert.Throws<OperationCanceledException>(() => source.Inspect(null, new CancellationToken(true)));
        }
    }
}
