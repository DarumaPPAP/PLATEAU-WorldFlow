using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using PLATEAU.Dataset;
using CoreGridCode = DarumaPPAP.PlateauWorldFlow.ReadOnly.GridCode;

namespace DarumaPPAP.PlateauWorldFlow.Plateau
{
    /// <summary>
    /// ローカルで許可されたUDXを有限件数だけ調べ、SDKでファイル名由来の範囲と地物種別を観測する。
    /// </summary>
    internal sealed class LocalSource
    {
        private const int EntryLimit = 4096;
        private readonly string _reference;
        private readonly string _datasetId;
        private readonly string _root;

        internal LocalSource(string reference, string datasetId, string root)
        {
            if (!WireValues.Id(reference) || !WireValues.Id(datasetId) || !Path.IsPathRooted(root))
            {
                throw new ArgumentException("Invalid local dataset configuration.");
            }

            _reference = reference;
            _datasetId = datasetId;
            _root = Path.GetFullPath(root);
        }

        /// <summary>
        /// 既存UDXのファイル名を観測し、本文を読まずに確認できたメッシュを返す。
        /// </summary>
        /// <param name="requested">明示照会するコード。nullの場合は上限内で観測したメッシュを列挙する。</param>
        /// <param name="cancellationToken">ディレクトリとファイルの処理単位で確認するキャンセル通知。</param>
        /// <returns>許可済み取得元に対応するDataset観測。</returns>
        internal Catalog Inspect(IReadOnlyCollection<string> requested, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            return Inspect(requested, cancellationToken, EntryLimit, () => timer.Elapsed >= TimeSpan.FromSeconds(3));
        }

        /// <summary>
        /// ファイルシステムの走査上限を適用し、打ち切り時に不在の判定を抑止する。
        /// </summary>
        internal Catalog Inspect(IReadOnlyCollection<string> requested, CancellationToken cancellationToken, int entryLimit, Func<bool> timeExceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var directory = new DirectoryInfo(_root); directory != null; directory = directory.Parent)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Local dataset directory is unavailable.");
                }
            }

            var udx = new DirectoryInfo(Path.Combine(_root, "udx"));
            if (!udx.Exists || (udx.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Local UDX directory is unavailable.");
            }

            var filter = requested == null ? null : new HashSet<string>(requested, StringComparer.Ordinal);
            var cells = new Dictionary<string, (Extent Extent, HashSet<string> Features)>(StringComparer.Ordinal);
            var pending = new Stack<DirectoryInfo>();
            pending.Push(udx);
            bool complete = true;
            bool stopped = false;
            int entries = 0;
            while (pending.Count > 0 && !stopped)
            {
                foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > entryLimit || timeExceeded())
                    {
                        complete = false;
                        stopped = true;
                        break;
                    }

                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        complete = false;
                        continue;
                    }

                    if (entry is DirectoryInfo child)
                    {
                        pending.Push(child);
                        continue;
                    }

                    if (!string.Equals(entry.Extension, ".gml", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // SDK 4.3.0のGmlFile.GridCodeは無効コードのコピー中にStringCodeを呼んで再帰する。
                    // UDXのファイル名の先頭要素を検証してから、SDKでメッシュ境界を計算する。
                    int separator = entry.Name.IndexOf('_');
                    string value = separator > 0 ? entry.Name.Substring(0, separator) : null;
                    if (!WireValues.Grid(value))
                    {
                        complete = false;
                        continue;
                    }

                    using var code = PLATEAU.Dataset.GridCode.Create(value);
                    if (!code.IsValid)
                    {
                        complete = false;
                        continue;
                    }

                    if (filter != null && !filter.Contains(value))
                    {
                        continue;
                    }

                    var file = GmlFile.Create(entry.FullName);
                    try
                    {
                        string feature = file.FeatureType;
                        if (!WireValues.Id(feature))
                        {
                            complete = false;
                            continue;
                        }

                        if (!cells.TryGetValue(value, out var cell))
                        {
                            var bounds = code.Extent;
                            // ファイル名由来のメッシュ境界には測地系の証拠がない。
                            cell = (new Extent(bounds.Min.Latitude, bounds.Min.Longitude, bounds.Max.Latitude, bounds.Max.Longitude, null), new HashSet<string>(StringComparer.Ordinal));
                            cells.Add(value, cell);
                        }

                        cell.Features.Add(feature);
                    }
                    finally
                    {
                        file.Dispose();
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var allFeatures = complete && filter == null ? cells.Values.SelectMany(cell => cell.Features).Distinct(StringComparer.Ordinal).Take(65).ToArray() : null;
            var dataset = new Dataset(_datasetId, null, allFeatures?.Length > 64 ? null : allFeatures, null, null, null, null, null);
            var available = cells.Select(pair => new CoverageCell(new CoreGridCode(pair.Key), pair.Value.Extent, pair.Value.Features.Count > 64 ? null : pair.Value.Features, null));
            var missing = complete && filter != null ? filter.Where(value => !cells.ContainsKey(value)).Where(IsValidCode).Select(value => new CoreGridCode(value)) : Enumerable.Empty<CoreGridCode>();
            var observed = new Observation(dataset, available, missing, complete);
            var checkedAt = DateTimeOffset.UtcNow;
            return new Catalog("LOCAL", _reference, checkedAt, checkedAt.AddMinutes(5), new[] { observed });
        }

        private bool IsValidCode(string value)
        {
            using var grid = PLATEAU.Dataset.GridCode.Create(value);
            return grid.IsValid;
        }
    }
}
