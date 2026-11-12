using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// リモート索引の部分取得とDataset固有の公開出典の扱いを検証する。
    /// </summary>
    internal sealed class RemoteSourceTests
    {
        /// <summary>
        /// 打切り時は確認済みのメッシュを返し、一部しか読めていない属性一覧を伏せる。
        /// </summary>
        [TestCase("complete")]
        [TestCase("count")]
        [TestCase("time")]
        public void IndexCompletenessControlsAttributes(string limit)
        {
            var files = Json.Object(("bldg", new[] { Json.Object(("code", "53394525"), ("lod2", 1)) }),
                ("tran", new[] { Json.Object(("code", "53394525"), ("lod3", 1)), Json.Object(("code", "53394526"), ("lod1", 1)) }));
            var city = Json.Object(("cityCode", "13104"), ("year", 2024), ("files", files));
            var response = Json.Object(("cities", new object[] { city }));
            int checks = 0;
            var observation = new RemoteSource().InspectIndex(response, "13104", new[] { "53394525", "53394526" }, CancellationToken.None, limit == "count" ? 1 : 10, () => limit == "time" && ++checks > 1).Observations.Single();
            Assert.That(observation.EnumerationComplete, Is.EqualTo(limit == "complete"));
            Assert.That(observation.TryGetCell("53394525", out var cell), Is.True);
            if (limit == "complete")
            {
                Assert.That(cell.AvailableLods, Is.EqualTo(new[] { 2, 3 }));
                Assert.That(cell.FeatureTypes, Is.EqualTo(new[] { "bldg", "tran" }));
            }
            else
            {
                Assert.That(cell.AvailableLods, Is.Null);
                Assert.That(cell.FeatureTypes, Is.Null);
                Assert.That(observation.IsMissing("53394526"), Is.False);
            }
        }

        /// <summary>
        /// 公式フィールドに含まれる安全な個別ページだけを出典として扱う。
        /// </summary>
        [TestCase("https://www.geospatial.jp/ckan/dataset/plateau-13104-shinjuku-ku-2025", true)]
        [TestCase("https://www.mlit.go.jp/plateau/", false)]
        [TestCase("https://www.geospatial.jp/ckan/dataset/plateau-13104?token=test", false)]
        [TestCase("https://www.geospatial.jp/ckan/dataset/", false)]
        [TestCase(null, false)]
        public void OnlyOfficialDatasetPageIsExposed(string url, bool accepted)
        {
            var fields = Json.Object(("gspatialjp_dataset_url", url));
            Assert.That(new RemoteSource().DatasetSourceUrl(fields), Is.EqualTo(accepted ? url : null));
        }

        /// <summary>
        /// 最新一覧に出典がない場合、同一自治体の公式公開レコードを一意に照合できたときだけ採用する。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LatestDatasetDoesNotGuessBetweenPublishedRecords(bool duplicate)
        {
            const string url = "https://www.geospatial.jp/ckan/dataset/plateau-13104-shinjuku-ku-2025";
            var latest = Json.Object(("id", "13104"), ("city_code", "13104"), ("year", "latest"));
            var city = Json.Object(("id", "13104"), ("city_code", "13104"), ("year", 2025), ("gspatialjp_dataset_url", url));
            var response = Json.Object(("latest_citygml", new object[] { latest }), ("citygml", duplicate ? new object[] { city, city } : new object[] { city }));
            var dataset = new RemoteSource().ListCatalog(response, 20, CancellationToken.None).Observations.Single().Dataset;
            Assert.That(dataset.SourceUrl, Is.EqualTo(duplicate ? null : url));
        }
    }
}
