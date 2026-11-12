using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// Dataset単位の観測を保持し、明示的に不在を確認したメッシュを未照会のメッシュと区別する。
    /// </summary>
    internal sealed class Observation
    {
        internal Dataset Dataset { get; }
        internal bool EnumerationComplete { get; }
        private readonly Dictionary<string, CoverageCell> _available;
        private readonly HashSet<string> _missing;

        /// <summary>
        /// 一つのDatasetについて利用可能範囲と明示的に不在を確認した範囲を登録する。
        /// </summary>
        /// <param name="dataset">観測対象のDataset。</param>
        /// <param name="available">利用可能と確認したメッシュ。</param>
        /// <param name="missing">明示的に不在を確認したメッシュ。</param>
        /// <param name="enumerationComplete">利用可能なメッシュの列挙が完了した場合はtrue。</param>
        internal Observation(Dataset dataset, IEnumerable<CoverageCell> available, IEnumerable<GridCode> missing, bool enumerationComplete = true)
        {
            Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            EnumerationComplete = enumerationComplete;
            _available = available.ToDictionary(cell => cell.Code.Value, StringComparer.Ordinal);
            _missing = new HashSet<string>(missing.Select(code => code.Value), StringComparer.Ordinal);
            if (_missing.Overlaps(_available.Keys))
            {
                throw new ArgumentException("Conflicting coverage observations.");
            }
        }

        internal IEnumerable<string> AvailableCodes => _available.Keys;
        /// <summary>
        /// 指定されたメッシュの利用可能な観測を取得する。
        /// </summary>
        /// <param name="code">照会対象の地域メッシュコード。</param>
        /// <param name="cell">利用可能と確認したメッシュの観測。</param>
        /// <returns>利用可能と確認済みの場合はtrue。</returns>
        internal bool TryGetCell(string code, out CoverageCell cell) => _available.TryGetValue(code, out cell);
        /// <summary>
        /// 指定されたメッシュが明示的に不在と確認されたかを判定する。
        /// </summary>
        /// <param name="code">照会対象の地域メッシュコード。</param>
        /// <returns>不在と確認済みの場合のみtrue。</returns>
        internal bool IsMissing(string code) => _missing.Contains(code);
    }
}
