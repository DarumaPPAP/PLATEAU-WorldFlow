using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// 承認済みの公開ファイルだけをJob専用領域に取得し、SDKへ渡す前にローカル実体を照合する。
    /// </summary>
    internal sealed class RemoteStaging
    {
        private const string Host = "assets.cms.plateau.reearth.io";
        private const long MaxArchiveBytes = 64L * 1024 * 1024;
        private const long MaxAcquisitionBytes = 4L * 1024 * 1024 * 1024;
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None }) { Timeout = TimeSpan.FromSeconds(20) };
        private long _remaining = MaxAcquisitionBytes;

        /// <summary>
        /// CityGML本体を取得せず、同じ配信表現の強いETagと転送サイズを承認対象のManifestに固定する。
        /// </summary>
        internal async Task<string> BindDeliveryAsync(string manifest, CancellationToken token)
        {
            var plan = Json.Parse(manifest);
            foreach (var file in Json.Field<object[]>(plan, "files").Cast<Fields>())
            {
                file["delivery"] = await ReadDeliveryAsync(Json.Field<string>(file, "publishedUrl"), token).ConfigureAwait(false);
            }
            var archiveDelivery = new Fields(StringComparer.Ordinal);
            foreach (var archive in Json.Field<Fields>(plan, "archives"))
            {
                archiveDelivery.Add(archive.Key, await ReadDeliveryAsync((string)archive.Value, token).ConfigureAwait(false));
            }
            plan["archiveDelivery"] = archiveDelivery;
            return Json.Write(plan);
        }

        private async Task<Fields> ReadDeliveryAsync(string url, CancellationToken token)
        {
            var uri = new Uri(url, UriKind.Absolute);
            RequireOfficialUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            request.Headers.AcceptEncoding.ParseAdd("gzip");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            string encoding = ContentEncoding(response);
            var tag = response.Headers.ETag;
            if (tag == null || tag.IsWeak || tag.Tag == "*" || !length.HasValue || length <= 0 || length > int.MaxValue || encoding == null)
            {
                throw new InvalidDataException("SOURCE_MAPPING_UNVERIFIED");
            }
            return Json.Object(("etag", tag.Tag), ("wireLength", (int)length.Value), ("encoding", encoding));
        }

        /// <summary>
        /// 照合済みの版と地域メッシュに必要なGML、コードリスト、スキーマ、参照ファイルを取得・検査する。
        /// </summary>
        internal async Task<string> AcquireAsync(string root, string remoteManifest, string[] grids, IReadOnlyDictionary<string, int[]> features, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(root) || File.Exists(root))
            {
                throw new IOException("SOURCE_STAGE_EXISTS");
            }
            CheckDirectory(Path.GetDirectoryName(root));
            Directory.CreateDirectory(root);

            var planned = Json.Parse(remoteManifest);
            var files = Json.Field<object[]>(planned, "files").Cast<Fields>().ToArray();
            var archives = Json.Field<Fields>(planned, "archives");
            var archiveDelivery = Json.Field<Fields>(planned, "archiveDelivery");
            foreach (var archive in archives)
            {
                var uri = new Uri((string)archive.Value);
                string file = Destination(root, archive.Key + ".zip");
                await DownloadAsync(uri, file, MaxArchiveBytes, Json.Field<Fields>(archiveDelivery, archive.Key), token).ConfigureAwait(false);
                string name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                Extract(root, file, archive.Key, name.Substring(0, name.Length - archive.Key.Length - 1), token);
            }

            foreach (var gml in files)
            {
                string relative = Json.Field<string>(gml, "relativePath");
                var uri = new Uri(Json.Field<string>(gml, "publishedUrl"));
                await DownloadAsync(uri, Destination(root, relative), Math.Min(int.MaxValue, _remaining), Json.Field<Fields>(gml, "delivery"), token).ConfigureAwait(false);
            }

            var local = new LocalManifest();
            foreach (var gml in files)
            {
                string relative = Json.Field<string>(gml, "relativePath");
                string assetUrl = Json.Field<string>(gml, "publishedUrl");
                string baseUrl = assetUrl.Substring(0, assetUrl.Length - relative.Length);
                foreach (var reference in local.ReferencedPaths(root, Destination(root, relative), token))
                {
                    string destination = Destination(root, reference);
                    // 同版のZIPから取り出せない参照は、同じ配布ルートのudx内に限定する。
                    if (File.Exists(destination)) continue;
                    if (!reference.StartsWith("udx/", StringComparison.Ordinal) || reference.EndsWith(".gml", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("SOURCE_REFERENCE_UNVERIFIED");
                    }
                    string encoded = string.Join("/", reference.Split('/').Select(Uri.EscapeDataString));
                    await DownloadAsync(new Uri(baseUrl + encoded), destination, MaxArchiveBytes, null, token).ConfigureAwait(false);
                }
            }

            string manifest = local.Inspect(root, grids, features, token);
            var actual = Json.Field<object[]>(Json.Parse(manifest), "files").Cast<Fields>().ToArray();
            if (actual.Length != files.Length || files.Zip(actual, (expected, found) => Json.Field<string>(expected, "relativePath") == Json.Field<string>(found, "relativePath") && Json.Field<string>(expected, "crs") == Json.Field<string>(found, "crs")).Any(matches => !matches))
            {
                throw new InvalidDataException("SOURCE_CHANGED");
            }
            return manifest;
        }

        private async Task DownloadAsync(Uri uri, string file, long limit, Fields delivery, CancellationToken token)
        {
            RequireOfficialUri(uri);
            if (limit <= 0 || limit > _remaining)
            {
                throw new InvalidDataException("SOURCE_DOWNLOAD_UNVERIFIED");
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (delivery != null)
            {
                request.Headers.AcceptEncoding.ParseAdd("gzip");
                request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(Json.Field<string>(delivery, "etag")));
            }
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? reported = response.Content.Headers.ContentLength;
            string encoding = ContentEncoding(response);
            bool representationMatches = delivery == null ? encoding == "identity" : response.Headers.ETag?.IsWeak == false && response.Headers.ETag.Tag == Json.Field<string>(delivery, "etag") && reported == Json.Field<int>(delivery, "wireLength") && encoding == Json.Field<string>(delivery, "encoding");
            if (!representationMatches || reported > limit)
            {
                throw new InvalidDataException("SOURCE_DOWNLOAD_UNVERIFIED");
            }

            string folder = Path.GetDirectoryName(file);
            CheckDirectory(folder);
            Directory.CreateDirectory(folder);
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var gzip = encoding == "gzip" ? new GZipStream(input, CompressionMode.Decompress, leaveOpen: true) : null;
            Stream content = gzip ?? input;
            using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            var buffer = new byte[64 * 1024];
            long received = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) != 0)
            {
                received += read;
                if (received > limit)
                {
                    throw new InvalidDataException("SOURCE_DOWNLOAD_UNVERIFIED");
                }
                await output.WriteAsync(buffer, 0, read, deadline.Token).ConfigureAwait(false);
            }

            if (received == 0 || encoding == "identity" && reported.HasValue && reported.Value != received)
            {
                throw new InvalidDataException("SOURCE_DOWNLOAD_UNVERIFIED");
            }
            _remaining -= received;
        }

        private void RequireOfficialUri(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != Host || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new InvalidDataException("SOURCE_DOWNLOAD_UNVERIFIED");
            }
        }

        private string ContentEncoding(HttpResponseMessage response)
        {
            var encodings = response.Content.Headers.ContentEncoding;
            // CMS の非圧縮応答には Content-Encoding が存在しない。
            if (encodings.Count == 0) return "identity";

            return encodings.Count == 1 && encodings.Contains("gzip", StringComparer.OrdinalIgnoreCase) ? "gzip" : null;
        }

        /// <summary>
        /// 配布ZIPの内容を指定された依存種別の新規ファイルとして展開する。
        /// </summary>
        internal void Extract(string root, string archive, string role, string dataset, CancellationToken token)
        {
            if (role != "codelists" && role != "schemas")
            {
                throw new InvalidDataException("SOURCE_ARCHIVE_UNVERIFIED");
            }

            using var stream = File.OpenRead(archive);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > 8192)
            {
                throw new InvalidDataException("SOURCE_ARCHIVE_UNVERIFIED");
            }

            long remaining = MaxArchiveBytes;
            var buffer = new byte[64 * 1024];
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string name = entry.FullName.Replace('\\', '/');
                string normalized = name.TrimEnd('/');
                Destination(root, normalized);
                // 空ディレクトリはSDKへの入力に含まれない。
                if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                string prefix = dataset + "_" + role + "/";
                if (normalized.StartsWith(prefix + role + "/", StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(prefix.Length);
                }
                else if (normalized.StartsWith(dataset + "/" + role + "/", StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(dataset.Length + 1);
                }
                string relative = normalized.StartsWith(role + "/", StringComparison.Ordinal) ? normalized : role + "/" + normalized;
                string path = Destination(root, relative);
                if (entry.Length <= 0 || entry.Length > remaining || entry.Length > _remaining)
                {
                    throw new InvalidDataException("SOURCE_ARCHIVE_UNVERIFIED");
                }

                string folder = Path.GetDirectoryName(path);
                CheckDirectory(folder);
                Directory.CreateDirectory(folder);
                using var input = entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                long expanded = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    expanded += read;
                    if (expanded > entry.Length)
                    {
                        throw new InvalidDataException("SOURCE_ARCHIVE_UNVERIFIED");
                    }
                    output.Write(buffer, 0, read);
                }
                if (expanded != entry.Length)
                {
                    throw new InvalidDataException("SOURCE_ARCHIVE_UNVERIFIED");
                }
                remaining -= expanded;
                _remaining -= expanded;
            }
        }

        private string Destination(string root, string relative)
        {
            var invalid = Path.GetInvalidFileNameChars();
            if (relative.IndexOfAny(new[] { '\\', ':' }) >= 0 || relative.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".." || part.IndexOfAny(invalid) >= 0 || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || IsDeviceName(part)))
            {
                throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
            }
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private bool IsDeviceName(string name)
        {
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            return stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]);
        }

        private void CheckDirectory(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0 || File.Exists(directory.FullName))
                {
                    throw new InvalidDataException("SOURCE_PATH_UNVERIFIED");
                }
            }
        }
    }
}
