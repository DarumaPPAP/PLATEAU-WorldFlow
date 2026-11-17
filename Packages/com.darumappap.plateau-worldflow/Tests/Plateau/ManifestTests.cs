using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// SDKのファイル選択とGeometryの観測を組み合わせ、ローカル計画の対象と品質を検証する。
    /// </summary>
    internal sealed class ManifestTests
    {
        [Test]
        public void InspectUsesSdkSelectedFilesAndActualLods()
        {
            string json = Inspect(Fixture(), new[] { "53392547", "53392546" }, new[] { 0, 1 });
            var files = (object[])Json.Parse(json)["files"];
            Assert.That(files, Has.Length.EqualTo(2));
            var first = (Dictionary<string, object>)files[0];
            Assert.That(first["relativePath"], Is.EqualTo("udx/bldg/53392546_bldg_6697_2_op.gml"));
            Assert.That(first["crs"], Is.EqualTo("EPSG:6697"));
            Assert.That(first["municipalityCodes"], Is.EqualTo(new[] { "13111" }));
            Assert.That(first["lods"], Is.EqualTo(new[] { 0, 1 }));
            Assert.That(first["sha256"], Does.Match("^[a-f0-9]{64}$"));
            var dependencies = (object[])Json.Parse(json)["dependencies"];
            Assert.That(dependencies.Cast<Dictionary<string, object>>().Select(item => item["relativePath"]), Does.Contain("codelists/Common_prefecture.xml"));
            Assert.That(json, Does.Not.Contain(Fixture()));
            Assert.That(Inspect(Fixture(), new[] { "53392546", "53392547" }, new[] { 1, 0 }), Is.EqualTo(json));
        }

        [Test]
        public void InspectRejectsUnavailableLodAndEmptySdkSelection()
        {
            Assert.That(Assert.Throws<InvalidDataException>(() => Inspect(Fixture(), new[] { "53392546" }, new[] { 2 })).Message, Is.EqualTo("SOURCE_LOD_UNVERIFIED"));
            Assert.That(Assert.Throws<InvalidDataException>(() => Inspect(Fixture(), new[] { "53392548" }, new[] { 0 })).Message, Is.EqualTo("EMPTY_SOURCE"));
            Assert.That(Assert.Throws<InvalidDataException>(() => Inspect(Fixture(), new[] { "53394525" }, new[] { 0, 2 })).Message, Is.EqualTo("SDK_LOD_RANGE_MISMATCH"));
        }

        [Test]
        public void InspectRejectsDocumentWithExternalDtdBeforeNativeParse()
        {
            InspectInvalidDocument("<!DOCTYPE CityModel SYSTEM 'outside.dtd'><CityModel/>", typeof(System.Xml.XmlException), null);
        }

        /// <summary>
        /// CRSの証拠が不足または矛盾する場合や、参照がDatasetを逸脱する場合を検証する。
        /// </summary>
        [TestCase("<CityModel/>", "SOURCE_CRS_UNVERIFIED")]
        [TestCase("<CityModel srsName='EPSG:6697'><item srsName='EPSG:6668'/></CityModel>", "SOURCE_CRS_UNVERIFIED")]
        [TestCase("<CityModel srsName='EPSG:6697'><item codeSpace='../../../outside.xml'/></CityModel>", "SOURCE_PATH_UNVERIFIED")]
        public void InspectRejectsUnverifiedCrsAndReference(string xml, string error)
        {
            InspectInvalidDocument(xml, typeof(InvalidDataException), error);
        }

        private void InspectInvalidDocument(string xml, Type exceptionType, string error)
        {
            string root = Path.Combine(Path.GetTempPath(), "WorldFlowManifestTests", Guid.NewGuid().ToString("N"));
            string udx = Directory.CreateDirectory(Path.Combine(root, "udx", "bldg")).FullName;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "codelists"));
                File.WriteAllText(Path.Combine(udx, "53392546_bldg_6697_op.gml"), xml);
                var thrown = Assert.Throws(exceptionType, () => Inspect(root, new[] { "53392546" }, new[] { 0 }));
                if (error != null)
                {
                    Assert.That(thrown.Message, Is.EqualTo(error));
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private string Inspect(string root, string[] grids, int[] lods)
        {
            return new LocalManifest().Inspect(root, grids, new Dictionary<string, int[]> { ["bldg"] = lods }, CancellationToken.None);
        }

        private string Fixture()
        {
            var sdk = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Single(package => package.name == "com.synesthesias.plateau-unity-sdk");
            return Path.Combine(sdk.resolvedPath, "Tests", "TestData", "日本語パステスト", "TestDataTokyoMini");
        }
    }
}
