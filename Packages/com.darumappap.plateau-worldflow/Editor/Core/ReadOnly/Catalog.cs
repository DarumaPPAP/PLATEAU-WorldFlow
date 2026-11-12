using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 取得元の区分と識別子に紐づけて、Datasetの観測結果とその有効期間を保持する。
    /// </summary>
    internal sealed class Catalog
    {
        internal string Source { get; }
        internal string Reference { get; }
        internal DateTimeOffset CheckedAt { get; }
        internal DateTimeOffset ValidUntil { get; }
        internal bool Complete { get; }
        private readonly Dictionary<string, Observation> _observations;

        /// <summary>
        /// 観測を取得元に紐づけ、その観測を利用できる期間を設定する。
        /// </summary>
        /// <param name="source">取得元の区分。</param>
        /// <param name="reference">取得元を特定する公開可能な識別子。</param>
        /// <param name="checkedAt">観測を取得した時刻。</param>
        /// <param name="validUntil">観測が失効する時刻。この時刻から観測を利用できない。</param>
        /// <param name="observations">取得元で確認したDataset別の観測。</param>
        /// <param name="complete">Datasetの一覧を全件確認した場合はtrue。</param>
        internal Catalog(string source, string reference, DateTimeOffset checkedAt, DateTimeOffset validUntil, IEnumerable<Observation> observations, bool complete = true)
        {
            if (!WireValues.Source(source) || !WireValues.Id(reference) || validUntil <= checkedAt)
            {
                throw new ArgumentException("Invalid catalog provenance.");
            }

            Source = source;
            Reference = reference;
            CheckedAt = checkedAt;
            ValidUntil = validUntil;
            Complete = complete;
            _observations = observations.ToDictionary(item => item.Dataset.Id, StringComparer.Ordinal);
        }

        internal IEnumerable<Observation> Observations => _observations.Values;
        /// <summary>
        /// 取得元の観測から指定したDatasetを探す。
        /// </summary>
        /// <param name="id">検索するDataset識別子。</param>
        /// <param name="observation">見つかったDatasetの観測。</param>
        /// <returns>観測に含まれる場合はtrue。</returns>
        internal bool TryGet(string id, out Observation observation) => _observations.TryGetValue(id, out observation);

        /// <summary>
        /// 取得元の区分と照会時刻を公開用の証跡に変換する。
        /// </summary>
        /// <returns>公開用の取得元種別と観測時刻を格納したJSONオブジェクト。</returns>
        internal object Evidence() => Json.Object(("kind", "DATASET_QUERY"), ("source", Source == "LOCAL" ? "LOCAL_DATASET" : "REMOTE_DATASET"), ("checkedAt", Digests.Timestamp(CheckedAt)));
    }
}
