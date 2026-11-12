using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 一度の照会で分類した利用可能・不在・未確認のメッシュを取得元とProjectに紐づけて保持する。
    /// </summary>
    internal sealed class CoverageSnapshot
    {
        internal string Id { get; }
        internal string ProjectFingerprint { get; }
        internal string DatasetId { get; }
        internal Catalog Catalog { get; }
        internal bool Truncated { get; }
        internal bool HasUnverified => _unverified.Length > 0 || !_enumerationComplete;
        private readonly Dictionary<string, CoverageCell> _available;
        private readonly HashSet<string> _missing;
        private readonly string[] _unverified;
        private readonly bool _enumerationComplete;

        /// <summary>
        /// Datasetの観測を指定範囲と件数上限で絞り、後続の範囲計画で参照するCoverageを作る。
        /// </summary>
        /// <param name="id">Coverage照会の要求識別子。</param>
        /// <param name="fingerprint">照会対象ProjectのFingerprint。</param>
        /// <param name="catalog">照会に使った取得元の観測。</param>
        /// <param name="observation">照会対象Datasetの観測。</param>
        /// <param name="requested">照会対象のコード。nullの場合は観測済みの利用可能コードを列挙する。</param>
        /// <param name="maxResults">分類して返すコードの件数上限。</param>
        internal CoverageSnapshot(string id, string fingerprint, Catalog catalog, Observation observation, IEnumerable<string> requested, int maxResults)
        {
            Id = id;
            ProjectFingerprint = fingerprint;
            DatasetId = observation.Dataset.Id;
            Catalog = catalog;
            _enumerationComplete = observation.EnumerationComplete;
            var codes = (requested ?? observation.AvailableCodes).Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).Take(maxResults + 1).ToArray();
            Truncated = codes.Length > maxResults;
            _available = new Dictionary<string, CoverageCell>(StringComparer.Ordinal);
            _missing = new HashSet<string>(StringComparer.Ordinal);
            var unverified = new List<string>();
            foreach (var code in codes.Take(maxResults))
            {
                if (observation.TryGetCell(code, out var cell))
                {
                    _available.Add(code, cell);
                }
                else if (observation.IsMissing(code))
                {
                    _missing.Add(code);
                }
                else
                {
                    unverified.Add(code);
                }
            }

            _unverified = unverified.ToArray();
        }

        internal int AvailableCount => _available.Count;
        internal int MissingCount => _missing.Count;
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

        /// <summary>
        /// 分類済みのメッシュを契約の結果形式に変換する。
        /// </summary>
        /// <returns>未確認と確認済みゼロ件を区別したCoverageのJSONオブジェクト。</returns>
        internal Dictionary<string, object> ToJson()
        {
            bool noVerifiedCoverage = HasUnverified && AvailableCount + MissingCount == 0;
            var available = noVerifiedCoverage ? null : _available.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.ToJson());
            var missing = noVerifiedCoverage ? null : _missing.OrderBy(code => code, StringComparer.Ordinal);
            return Json.Object(("datasetId", DatasetId), ("availableGridCodes", available), ("unavailableGridCodes", missing), ("unverifiedGridCodes", _unverified), ("truncated", Truncated));
        }
    }
}
