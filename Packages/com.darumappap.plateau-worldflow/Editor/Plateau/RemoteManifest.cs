using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using PLATEAU.Dataset;
using PLATEAU.Native;
using PLATEAU.Network;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 公開カタログの版とファイル一覧を、固定SDKが公式サーバーから取得したDatasetと照合する。
    /// </summary>
    internal sealed class RemoteManifest
    {
        private const string Server = "https://api.plateau.reearth.io";
        private const string AssetHost = "assets.cms.plateau.reearth.io";
        private const int MaxFiles = 4096;

        /// <summary>
        /// 公式の索引とSDKのDataset情報だけで予定Manifestを作る。ファイルの取得は承認後に行う。
        /// </summary>
        internal async Task<(string SdkId, string Version, string Manifest)> InspectAsync(string datasetId, string adoptedVersion, string[] grids, IReadOnlyDictionary<string, int[]> features, CancellationToken token)
        {
            var index = await new RemoteSource().ReadIndexAsync(datasetId, token).ConfigureAwait(false);
            var published = ReadPublished(index, datasetId, adoptedVersion, grids, features, token);
            var resolved = await Task.Run(() =>
            {
                try
                {
                    string sdkId = ResolveSdkId(published.Title, token);
                    var sdkUrls = ReadSdkUrls(sdkId, grids, features, token);
                    return (SdkId: sdkId, Version: published.Version, Manifest: MatchFiles(published.Files, sdkUrls, published.Archives));
                }
                catch (Exception exception) when (exception.GetType() == typeof(Exception) && exception.Message.StartsWith("Error in Lib Plateau DLL.", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED", exception);
                }
            }, token).ConfigureAwait(false);
            string manifest = await new RemoteStaging().BindDeliveryAsync(resolved.Manifest, token).ConfigureAwait(false);
            return (resolved.SdkId, resolved.Version, manifest);
        }

        /// <summary>
        /// 公開索引の版、地域メッシュ・地物ごとのLOD、配布先を検証する。
        /// </summary>
        internal (string Title, string Version, Fields[] Files, Fields Archives) ReadPublished(Fields index, string datasetId, string adoptedVersion, string[] grids, IReadOnlyDictionary<string, int[]> features, CancellationToken token)
        {
            var cities = Json.Field<object[]>(index, "cities");
            var city = cities?.Length == 1 ? cities[0] as Fields : null;
            string title = Json.Field<string>(city, "cityName");
            int year = Json.Field<int>(city, "year");
            string version = year.ToString(CultureInfo.InvariantCulture);
            if (Json.Field<string>(city, "cityCode") != datasetId || title == null || !WireValues.Text(title) || year <= 0 || adoptedVersion != null && version != adoptedVersion || !TryAssetUri(Json.Field<string>(city, "url"), out var archive) || !archive.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }

            string directory = archive.AbsolutePath.Substring(0, archive.AbsolutePath.Length - 4);
            string rootName = directory.Substring(directory.LastIndexOf('/') + 1);
            if (!rootName.StartsWith(datasetId + "_", StringComparison.Ordinal) || !rootName.Contains("_" + version + "_"))
            {
                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }

            var indexed = Json.Field<Fields>(city, "files");
            var requested = new HashSet<string>(grids, StringComparer.Ordinal);
            var observed = new Dictionary<(string Grid, string Feature), HashSet<int>>();
            var files = new SortedDictionary<string, Fields>(StringComparer.Ordinal);
            foreach (var feature in features)
            {
                var entries = Json.Field<object[]>(indexed, feature.Key);
                if (entries == null || entries.Length > MaxFiles)
                {
                    throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                }

                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    var file = entry as Fields;
                    string grid = Json.Field<string>(file, "code");
                    // 破損した索引行は対象メッシュの完全性を証明できないため停止する。
                    if (!WireValues.Grid(grid)) throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                    // 要求範囲の索引行だけをManifestに含める。
                    if (!requested.Contains(grid)) continue;

                    string url = Json.Field<string>(file, "url");
                    string expectedDirectory = directory + "/udx/" + feature.Key + "/";
                    int size = Json.Field<int>(file, "fileSize");
                    if (!TryAssetUri(url, out var uri) || !uri.AbsolutePath.StartsWith(expectedDirectory, StringComparison.Ordinal) || size <= 0 || files.Count >= MaxFiles)
                    {
                        throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                    }

                    string relative = uri.AbsolutePath.Substring(directory.Length + 1);
                    string name = relative.Substring(relative.LastIndexOf('/') + 1);
                    var match = Regex.Match(name, "^" + Regex.Escape(grid + "_" + feature.Key) + "_(6697|6668)(?:_[A-Za-z0-9-]+)*_op\\.gml$", RegexOptions.CultureInvariant);
                    var lods = Enumerable.Range(0, 5).Where(lod => Json.Field<int>(file, "lod" + lod) > 0).ToArray();
                    if (!match.Success || lods.Length == 0 || lods.Max() != Json.Field<int>(file, "maxLod") || !files.TryAdd(relative, Json.Object(("relativePath", relative), ("publishedUrl", url), ("byteLength", size), ("gridCode", grid), ("featureType", feature.Key), ("crs", "EPSG:" + match.Groups[1].Value), ("municipalityCodes", new[] { datasetId }), ("lods", lods))))
                    {
                        throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                    }

                    var key = (grid, feature.Key);
                    if (!observed.TryGetValue(key, out var combined))
                    {
                        combined = new HashSet<int>();
                        observed.Add(key, combined);
                    }
                    combined.UnionWith(lods);
                }
            }

            foreach (string grid in grids)
            {
                foreach (var feature in features)
                {
                    if (!observed.TryGetValue((grid, feature.Key), out var lods) || !lods.IsSupersetOf(feature.Value))
                    {
                        throw new InvalidDataException("SOURCE_LOD_UNVERIFIED");
                    }
                    if (lods.Any(lod => lod >= feature.Value.Min() && lod <= feature.Value.Max() && !feature.Value.Contains(lod)))
                    {
                        throw new InvalidDataException("SDK_LOD_RANGE_MISMATCH");
                    }
                }
            }

            var archives = new Fields(StringComparer.Ordinal);
            foreach (var suffix in new[] { "codelists", "schemas" })
            {
                var matches = Json.Field<object[]>(city, "metadataZipUrls")?.OfType<string>().Where(url => TryAssetUri(url, out var uri) && Path.GetFileName(uri.AbsolutePath) == rootName + "_" + suffix + ".zip").Take(2).ToArray();
                if (matches?.Length != 1)
                {
                    throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                }
                archives.Add(suffix, matches[0]);
            }

            return (title, version, files.Values.ToArray(), archives);
        }

        /// <summary>
        /// 公式索引とSDKの選択結果が、重複なしで同じ公開URLの集合を指すことを確認する。
        /// </summary>
        internal string MatchFiles(Fields[] published, IEnumerable<string> sdkUrls, Fields archives)
        {
            var candidates = sdkUrls.ToArray();
            var urls = new HashSet<string>(candidates, StringComparer.Ordinal);
            var expected = new HashSet<string>(published.Select(file => Json.Field<string>(file, "publishedUrl")), StringComparer.Ordinal);
            if (published.Length == 0 || expected.Count != published.Length || urls.Count != candidates.Length || !urls.SetEquals(expected))
            {
                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }

            return Json.Write(Json.Object(("files", published), ("archives", archives)));
        }

        private string ResolveSdkId(string title, CancellationToken token)
        {
            var client = Client.Create(Server, "");
            try
            {
                using var groups = client.GetDatasetMetadataGroup();
                string sdkId = null;
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var datasets = groups.At(groupIndex).Datasets;
                    for (int entry = 0; entry < datasets.Length; entry++)
                    {
                        token.ThrowIfCancellationRequested();
                        var dataset = datasets.At(entry);
                        if (dataset.Title == title)
                        {
                            if (sdkId != null || !WireValues.Id(dataset.ID))
                            {
                                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                            }
                            sdkId = dataset.ID;
                        }
                    }
                }

                return sdkId ?? throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }
            finally
            {
                client.Dispose();
            }
        }

        private string[] ReadSdkUrls(string sdkId, string[] grids, IReadOnlyDictionary<string, int[]> features, CancellationToken token)
        {
            using var codes = NativeVectorGridCode.Create();
            foreach (string value in grids)
            {
                using var code = PLATEAU.Dataset.GridCode.Create(value);
                codes.AddCopyOf(code);
            }

            var packages = PredefinedCityModelPackage.None;
            foreach (string feature in features.Keys)
            {
                var package = DatasetAccessor.FeatureTypeToPackage(feature);
                if (package == PredefinedCityModelPackage.None || package == PredefinedCityModelPackage.Unknown || (packages & package) != 0)
                {
                    throw new InvalidDataException("SOURCE_FEATURE_UNVERIFIED");
                }
                packages |= package;
            }

            using var source = DatasetSource.Create(new DatasetSourceConfigRemote(sdkId, Server, ""));
            using var accessor = source.Accessor;
            using var selection = accessor.FilterByGridCodes(codes);
            using var files = selection.GetGmlFiles(packages);
            if (files.Length == 0 || files.Length > MaxFiles)
            {
                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }

            var urls = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < files.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                string url = files.At(index).Path;
                if (!TryAssetUri(url, out _) || !urls.Add(url))
                {
                    throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
                }
            }
            return urls.ToArray();
        }

        private bool TryAssetUri(string url, out Uri uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == AssetHost && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsoluteUri == url && Regex.IsMatch(uri.AbsolutePath, @"^/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.[a-z]+$", RegexOptions.CultureInvariant);
        }
    }
}
