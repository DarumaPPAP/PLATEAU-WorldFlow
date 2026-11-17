using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using PLATEAU.CityGML;
using PLATEAU.Dataset;
using PLATEAU.Native;
using Fields = System.Collections.Generic.Dictionary<string, object>;
using SdkGridCode = PLATEAU.Dataset.GridCode;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 許可済みローカルデータを固定SDKの選択規則で照会し、実ファイルとLODを含むImport前の証跡を作る。
    /// </summary>
    internal sealed class LocalManifest
    {
        /// <summary>
        /// 選択メッシュ・地物に対するSDKの予定GMLを読み取り、実効LODと参照ファイルを照合する。
        /// </summary>
        /// <param name="root">呼び出し側で利用を許可したDatasetのルート。</param>
        /// <param name="gridCodes">採用済み計画と照合したメッシュ。</param>
        /// <param name="requiredLods">適用予定の地物とLOD。</param>
        /// <param name="token">ファイルおよび要素の境界で確認するキャンセル通知。</param>
        /// <returns>相対パス順の予定GMLと依存ファイルを含む正規化JSON。</returns>
        internal string Inspect(string root, IReadOnlyCollection<string> gridCodes, IReadOnlyDictionary<string, int[]> requiredLods, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root) || gridCodes == null || gridCodes.Count == 0 || requiredLods == null || requiredLods.Count == 0)
            {
                throw new ArgumentException("Invalid local import selection.");
            }

            root = Path.GetFullPath(root);
            string udx = Path.Combine(root, "udx");
            CheckParents(udx);
            CheckParents(Path.Combine(root, "codelists"));
            InspectTree(udx, token);

            var gridSet = new HashSet<string>(gridCodes, StringComparer.Ordinal);
            if (gridSet.Count != gridCodes.Count || gridSet.Count > WireValues.MaxGridCodes)
            {
                throw new ArgumentException("Invalid local import meshes.");
            }

            using var nativeGrids = NativeVectorGridCode.Create();
            foreach (string value in gridSet)
            {
                if (!WireValues.Grid(value))
                {
                    throw new ArgumentException("Invalid local import mesh.");
                }

                using var sdkCode = SdkGridCode.Create(value);
                if (!sdkCode.IsValid)
                {
                    throw new ArgumentException("Invalid SDK grid code.");
                }

                nativeGrids.AddCopyOf(sdkCode);
            }

            var packages = PredefinedCityModelPackage.None;
            foreach (var request in requiredLods)
            {
                if (!WireValues.Id(request.Key) || request.Value == null || request.Value.Length == 0 || request.Value.Any(lod => lod < 0) || request.Value.Distinct().Count() != request.Value.Length)
                {
                    throw new ArgumentException("Invalid local import feature selection.");
                }

                var package = DatasetAccessor.FeatureTypeToPackage(request.Key);
                if (package == PredefinedCityModelPackage.None || package == PredefinedCityModelPackage.Unknown || (packages & package) != 0)
                {
                    throw new InvalidDataException("SOURCE_FEATURE_UNVERIFIED");
                }

                packages |= package;
            }

            using var dataset = DatasetSource.Create(new DatasetSourceConfigLocal(root));
            using var accessor = dataset.Accessor;
            using var filtered = accessor.FilterByGridCodes(nativeGrids);
            using var gmlFiles = filtered.GetGmlFiles(packages);
            if (gmlFiles.Length == 0)
            {
                throw new InvalidDataException("EMPTY_SOURCE");
            }

            var files = new SortedDictionary<string, Fields>(StringComparer.Ordinal);
            var references = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var observed = new Dictionary<(string Grid, string Feature), HashSet<int>>();
            var coordinateSystems = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < gmlFiles.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var gml = gmlFiles.At(index);
                string path = gml.Path;
                string relative = CheckedRelative(root, path);
                string feature = gml.FeatureType;
                string[] parts = relative.Split('/');
                if (parts.Length < 3 || parts[0] != "udx" || parts[1] != feature || !requiredLods.ContainsKey(feature) || !string.Equals(Path.GetExtension(path), ".gml", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SOURCE_FEATURE_MISMATCH");
                }

                string fileName = parts[parts.Length - 1];
                int separator = fileName.IndexOf('_');
                string grid = separator > 0 ? fileName.Substring(0, separator) : null;
                if (grid == null || !gridSet.Contains(grid) || files.ContainsKey(relative))
                {
                    throw new InvalidDataException("SOURCE_GRID_MISMATCH");
                }

                using var stream = File.OpenRead(path);
                string fileHash = Hash(stream);
                stream.Position = 0;
                var metadata = ReadXml(path, stream, target => RecordReference(root, target, references), token);
                coordinateSystems.Add(metadata.Crs);
                var lods = ReadLods(path, token);
                files.Add(relative, Json.Object(("relativePath", relative), ("sha256", fileHash), ("gridCode", grid), ("featureType", feature), ("crs", metadata.Crs), ("municipalityCodes", metadata.Municipalities), ("lods", lods)));

                var key = (grid, feature);
                if (!observed.TryGetValue(key, out var combined))
                {
                    combined = new HashSet<int>();
                    observed.Add(key, combined);
                }

                combined.UnionWith(lods);
            }

            if (coordinateSystems.Count != 1)
            {
                throw new InvalidDataException("SOURCE_CRS_MISMATCH");
            }

            ValidateQuality(gridSet, requiredLods, observed);
            var dependencies = references.Select(reference => Json.Object(("relativePath", reference.Key), ("sha256", reference.Value))).ToArray();
            return Json.Write(Json.Object(("files", files.Values.ToArray()), ("dependencies", dependencies)));
        }

        private void ValidateQuality(HashSet<string> grids, IReadOnlyDictionary<string, int[]> required, Dictionary<(string Grid, string Feature), HashSet<int>> observed)
        {
            foreach (string grid in grids)
            {
                foreach (var feature in required)
                {
                    if (!observed.TryGetValue((grid, feature.Key), out var lods) || !lods.IsSupersetOf(feature.Value))
                    {
                        throw new InvalidDataException("SOURCE_LOD_UNVERIFIED");
                    }

                    int low = feature.Value.Min();
                    int high = feature.Value.Max();
                    if (lods.Any(lod => low <= lod && lod <= high && !feature.Value.Contains(lod)))
                    {
                        throw new InvalidDataException("SDK_LOD_RANGE_MISMATCH");
                    }
                }
            }
        }

        private int[] ReadLods(string path, CancellationToken token)
        {
            using var model = CityGml.Load(path, new CitygmlParserParams(true, false, true, false));
            var lods = new SortedSet<int>();
            foreach (var root in model.RootCityObjects)
            {
                foreach (var cityObject in root.CityObjectDescendantsDFS)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var geometry in cityObject.Geometries)
                    {
                        foreach (var part in geometry.GeometryDescendantsDFS)
                        {
                            lods.Add(part.LOD);
                        }
                    }
                }
            }

            return lods.ToArray();
        }

        /// <summary>
        /// 取得済みGMLに明示された依存先を返し、未取得のファイルを開かずに範囲内のパスだけを許可する。
        /// </summary>
        internal string[] ReferencedPaths(string root, string path, CancellationToken token)
        {
            var references = new SortedSet<string>(StringComparer.Ordinal);
            using var stream = File.OpenRead(path);
            ReadXml(path, stream, target => { references.Add(RelativeWithinRoot(root, target)); }, token);
            return references.ToArray();
        }

        private (string Crs, string[] Municipalities) ReadXml(string path, Stream stream, Action<string> reference, CancellationToken token)
        {
            const string xmlNamespace = "http://www.w3.org/XML/1998/namespace";
            const string linkNamespace = "http://www.w3.org/1999/xlink";
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            var coordinateSystems = new HashSet<string>(StringComparer.Ordinal);
            var municipalities = new SortedSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.GetAttribute("base", xmlNamespace) != null)
                    {
                        throw new InvalidDataException("SOURCE_REFERENCE_UNVERIFIED");
                    }

                    string srs = reader.GetAttribute("srsName");
                    if (srs != null)
                    {
                        coordinateSystems.Add(NormalizeCrs(srs));
                    }

                    string codeSpace = reader.GetAttribute("codeSpace");
                    ReadReference(path, codeSpace, reference);
                    ReadReference(path, reader.GetAttribute("href", linkNamespace), reference);
                    if (reader.LocalName == "imageURI")
                    {
                        ReadReference(path, reader.ReadString(), reference);
                    }
                    else if (reader.LocalName == "city" && IsMunicipalityAttribute(reader.NamespaceURI, codeSpace))
                    {
                        string code = reader.ReadString().Trim();
                        if (code.Length != 5 || code.Any(character => character < '0' || character > '9'))
                        {
                            throw new InvalidDataException("SOURCE_MUNICIPALITY_UNVERIFIED");
                        }
                        municipalities.Add(code);
                    }
                }
            }

            if (coordinateSystems.Count != 1)
            {
                throw new InvalidDataException("SOURCE_CRS_UNVERIFIED");
            }

            return (coordinateSystems.Single(), municipalities.ToArray());
        }

        private bool IsMunicipalityAttribute(string schema, string codeSpace)
        {
            bool urbanSchema = schema.StartsWith("https://www.geospatial.jp/iur/uro/", StringComparison.Ordinal) || schema.StartsWith("http://www.kantei.go.jp/jp/singi/tiiki/toshisaisei/itoshisaisei/iur/uro/", StringComparison.Ordinal);
            return urbanSchema && codeSpace != null && Path.GetFileName(codeSpace.Replace('\\', '/')) == "Common_localPublicAuthorities.xml";
        }

        private string NormalizeCrs(string value)
        {
            foreach (string code in new[] { "6668", "6697" })
            {
                if (value == "EPSG:" + code || value == "http://www.opengis.net/def/crs/EPSG/0/" + code || value == "https://www.opengis.net/def/crs/EPSG/0/" + code || value == "urn:ogc:def:crs:EPSG::" + code)
                {
                    return "EPSG:" + code;
                }
            }

            throw new InvalidDataException("SOURCE_CRS_UNVERIFIED");
        }

        private void ReadReference(string gml, string value, Action<string> reference)
        {
            // 未指定と文書内フラグメントでは別のファイルを開かない。
            if (value == null || value.StartsWith("#", StringComparison.Ordinal)) return;

            string resource = value.Split('#')[0];
            if (string.IsNullOrWhiteSpace(resource) || resource.IndexOfAny(new[] { ':', '?', '%' }) >= 0 || resource.StartsWith("/", StringComparison.Ordinal) || resource.StartsWith("\\", StringComparison.Ordinal) || Path.IsPathRooted(resource))
            {
                throw new InvalidDataException("SOURCE_REFERENCE_UNVERIFIED");
            }

            string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gml), resource.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            reference(target);
        }

        private void RecordReference(string root, string target, SortedDictionary<string, string> references)
        {
            string relative = CheckedRelative(root, target);
            if (!references.ContainsKey(relative))
            {
                using var file = File.OpenRead(target);
                references.Add(relative, Hash(file));
            }
        }

        private string RelativeWithinRoot(string root, string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            {
                throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
            }

            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative == "." || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
            }

            return relative;
        }

        private string CheckedRelative(string root, string path)
        {
            string relative = RelativeWithinRoot(root, path);
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
            }

            CheckParents(file.DirectoryName);
            return relative;
        }

        private void CheckParents(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
                }
            }
        }

        private void InspectTree(string path, CancellationToken token)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(path));
            while (pending.Count > 0)
            {
                foreach (var item in pending.Pop().EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
                    }

                    if (item is DirectoryInfo directory)
                    {
                        pending.Push(directory);
                    }
                }
            }
        }

        private string Hash(Stream stream)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
