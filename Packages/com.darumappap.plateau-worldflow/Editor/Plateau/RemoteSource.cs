using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 国土交通省の公開カタログと索引を取得し、SDKで地域メッシュの境界を計算する。
    /// </summary>
    internal sealed class RemoteSource
    {
        internal const string Reference = "plateau-public";
        private const string Endpoint = "https://api.plateauview.mlit.go.jp/datacatalog/";
        private const int FileLimit = 4096;
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 16 * 1024 * 1024
        };

        /// <summary>
        /// 公式一覧で公開されている各自治体の最新CityGML Datasetを取得する。
        /// </summary>
        internal async Task<Catalog> ListAsync(int maxResults, CancellationToken cancellationToken)
        {
            var response = await GetAsync("plateau-datasets", cancellationToken).ConfigureAwait(false);
            var entries = RequiredArray(response, "latest_citygml");
            var datasets = new Dictionary<string, Observation>(StringComparer.Ordinal);
            bool complete = true;
            foreach (var fields in entries.Take(FileLimit).OfType<Dictionary<string, object>>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = Json.Field<string>(fields, "id");
                if (!IsCityCode(id) || id != Json.Field<string>(fields, "city_code"))
                {
                    complete = false;
                    continue;
                }

                string title = Json.Field<string>(fields, "city");
                var features = Json.Field<object[]>(fields, "feature_types");
                var validFeatures = features != null && features.Length <= 64 && features.All(value => WireValues.Id(value as string)) ? features.Cast<string>() : null;
                var dataset = new Dataset(id, WireValues.Text(title) ? title : null, validFeatures, null, null, null, null, DatasetSourceUrl(fields));
                datasets[id] = new Observation(dataset, Array.Empty<CoverageCell>(), Array.Empty<GridCode>(), false);
                // Coreが1件分の超過を検出し、契約のtruncatedを返す。
                if (datasets.Count > maxResults) return CatalogFrom(datasets.Values, false);
            }

            return CatalogFrom(datasets.Values, complete && entries.Length == datasets.Count);
        }

        /// <summary>
        /// 対象自治体のファイル索引を取得し、参照先を取得せずにメッシュと確認済みLODを観測する。
        /// </summary>
        internal async Task<Catalog> InspectAsync(string datasetId, IReadOnlyCollection<string> requested, CancellationToken cancellationToken)
        {
            if (!IsCityCode(datasetId))
            {
                throw new ArgumentException("Invalid PLATEAU dataset identifier.");
            }

            var response = await GetAsync("citygml/" + datasetId, cancellationToken).ConfigureAwait(false);
            var timer = Stopwatch.StartNew();
            return InspectIndex(response, datasetId, requested, cancellationToken, FileLimit, () => timer.Elapsed >= TimeSpan.FromSeconds(3));
        }

        /// <summary>
        /// 応答に含まれる索引を上限内で走査し、確認済みメッシュと観測の完全性を返す。
        /// </summary>
        internal Catalog InspectIndex(Dictionary<string, object> response, string datasetId, IReadOnlyCollection<string> requested, CancellationToken cancellationToken, int fileLimit, Func<bool> timeExceeded)
        {
            var cities = RequiredArray(response, "cities");
            var matching = cities.OfType<Dictionary<string, object>>().Where(city => Json.Field<string>(city, "cityCode") == datasetId).ToArray();
            if (matching.Length != 1 || cities.Length != 1 || !(matching[0].TryGetValue("year", out var yearValue) && yearValue is int year && year > 0) || Json.Field<Dictionary<string, object>>(matching[0], "files") == null)
            {
                throw new FormatException("Unexpected PLATEAU coverage response.");
            }

            var city = matching[0];
            var files = Json.Field<Dictionary<string, object>>(city, "files");
            var filter = requested == null ? null : new HashSet<string>(requested, StringComparer.Ordinal);
            var cells = new Dictionary<string, (HashSet<string> Features, HashSet<int> Lods)>(StringComparer.Ordinal);
            bool complete = true;
            bool stopped = false;
            int examined = 0;
            foreach (var feature in files)
            {
                if (!WireValues.Id(feature.Key) || !(feature.Value is object[] list))
                {
                    complete = false;
                    continue;
                }

                foreach (var entry in list)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++examined > fileLimit || timeExceeded())
                    {
                        complete = false;
                        stopped = true;
                        break;
                    }

                    var file = entry as Dictionary<string, object>;
                    var code = Json.Field<string>(file, "code");
                    if (!WireValues.Grid(code))
                    {
                        complete = false;
                        continue;
                    }

                    if (filter != null && !filter.Contains(code))
                    {
                        continue;
                    }

                    if (!cells.TryGetValue(code, out var cell))
                    {
                        cell = (new HashSet<string>(StringComparer.Ordinal), new HashSet<int>());
                        cells.Add(code, cell);
                    }

                    cell.Features.Add(feature.Key);
                    for (int lod = 0; lod <= 4; lod++)
                    {
                        if (Json.Field<int>(file, "lod" + lod) > 0)
                        {
                            cell.Lods.Add(lod);
                        }
                    }
                }

                if (stopped)
                {
                    break;
                }
            }

            var available = new List<CoverageCell>();
            foreach (var pair in cells)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var grid = PLATEAU.Dataset.GridCode.Create(pair.Key);
                if (!grid.IsValid)
                {
                    complete = false;
                    continue;
                }

                var bounds = grid.Extent;
                var extent = new Extent(bounds.Min.Latitude, bounds.Min.Longitude, bounds.Max.Latitude, bounds.Max.Longitude, null);
                var features = pair.Value.Features.Count <= 64 ? pair.Value.Features : null;
                available.Add(new CoverageCell(new GridCode(pair.Key), extent, features, pair.Value.Lods.Count == 0 ? null : pair.Value.Lods));
            }

            string title = Json.Field<string>(city, "cityName");
            var dataset = new Dataset(datasetId, WireValues.Text(title) ? title : null, null, year.ToString(CultureInfo.InvariantCulture), null, null, null, DatasetSourceUrl(city));
            return CatalogFrom(new[] { new Observation(dataset, available, Array.Empty<GridCode>(), complete) }, true);
        }

        /// <summary>
        /// 公式メタデータに明示されたG空間情報センターの個別Datasetページを検証する。
        /// </summary>
        internal string DatasetSourceUrl(Dictionary<string, object> fields)
        {
            const string prefix = "https://www.geospatial.jp/ckan/dataset/";
            string url = Json.Field<string>(fields, "gspatialjp_dataset_url");
            return url != null && url.Length > prefix.Length && url.StartsWith(prefix, StringComparison.Ordinal) && WireValues.Url(url) && WireValues.Id(url.Substring(prefix.Length).TrimEnd('/')) ? url : null;
        }

        private async Task<Dictionary<string, object>> GetAsync(string resource, CancellationToken cancellationToken)
        {
            using var response = await Http.GetAsync(Endpoint + resource, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Json.Parse(body);
        }

        private object[] RequiredArray(Dictionary<string, object> fields, string key) =>
            Json.Field<object[]>(fields, key) ?? throw new FormatException("Unexpected PLATEAU response.");

        private bool IsCityCode(string value) => value != null && value.Length == 5 && value.All(ch => ch >= '0' && ch <= '9');

        private Catalog CatalogFrom(IEnumerable<Observation> observations, bool complete)
        {
            var checkedAt = DateTimeOffset.UtcNow;
            return new Catalog("REMOTE", Reference, checkedAt, checkedAt.AddMinutes(5), observations, complete);
        }
    }
}
