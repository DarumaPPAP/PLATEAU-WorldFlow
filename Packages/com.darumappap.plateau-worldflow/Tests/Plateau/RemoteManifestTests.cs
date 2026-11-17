using System.Collections.Generic;
using System.IO;
using System.Threading;
using DarumaPPAP.PlateauWorldFlow.Plateau;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Tests.Plateau
{
    /// <summary>
    /// 公開索引とSDKで観測したURLが同じ版と範囲を指す場合だけManifestを確定することを検証する。
    /// </summary>
    internal sealed class RemoteManifestTests
    {
        private const string Grid = "53392546";
        private const string Root = "https://assets.cms.plateau.reearth.io/assets/test/13111_ota-ku_2025_citygml_1_op";
        private const string Building = Root + "/udx/bldg/53392546_bldg_6697_op.gml";
        private const string Road = Root + "/udx/tran/53392546_tran_6697_op.gml";

        [Test]
        public void PublishedAndSdkFilesMustMatchOneToOne()
        {
            var resolver = new RemoteManifest();
            var published = resolver.ReadPublished(Index(), "13111", "2025", new[] { Grid }, new Dictionary<string, int[]> { ["bldg"] = new[] { 1 }, ["tran"] = new[] { 1 } }, CancellationToken.None);

            Assert.That(published.Title, Is.EqualTo("大田区"));
            Assert.That(published.Files.Length, Is.EqualTo(2));
            Assert.That(resolver.MatchFiles(published.Files, new[] { Road, Building }, published.Archives), Is.EqualTo(resolver.MatchFiles(published.Files, new[] { Building, Road }, published.Archives)));
            Assert.That(() => resolver.MatchFiles(published.Files, new[] { Road }, published.Archives), Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("SOURCE_MAPPING_UNVERIFIED"));
            Assert.That(() => resolver.MatchFiles(published.Files, new[] { Road, Building, Building }, published.Archives), Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("SOURCE_MAPPING_UNVERIFIED"));
        }

        [TestCase("2024", Building, 1, "SOURCE_MAPPING_UNVERIFIED")]
        [TestCase("2025", Building + "?extra=1", 1, "SOURCE_MAPPING_UNVERIFIED")]
        [TestCase("2025", Road, 1, "SOURCE_MAPPING_UNVERIFIED")]
        [TestCase("2025", Building, 2, "SOURCE_LOD_UNVERIFIED")]
        public void RejectsWrongReleaseSourceOrFeatureLod(string version, string url, int lod, string error)
        {
            var resolver = new RemoteManifest();
            Assert.That(() => resolver.ReadPublished(Index(url), "13111", version, new[] { Grid }, new Dictionary<string, int[]> { ["bldg"] = new[] { lod } }, CancellationToken.None), Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(error));
        }

        private Dictionary<string, object> Index(string buildingUrl = Building)
        {
            var building = Json.Object(("code", Grid), ("url", buildingUrl), ("fileSize", 15), ("maxLod", 1), ("lod0", 1), ("lod1", 1));
            var road = Json.Object(("code", Grid), ("url", Road), ("fileSize", 9), ("maxLod", 1), ("lod1", 1));
            var city = Json.Object(("cityCode", "13111"), ("cityName", "大田区"), ("year", 2025), ("url", Root + ".zip"), ("metadataZipUrls", new object[] { Root + "_codelists.zip", Root + "_schemas.zip" }), ("files", Json.Object(("bldg", new object[] { building }), ("tran", new object[] { road }))));
            return Json.Object(("cities", new object[] { city }));
        }
    }
}
