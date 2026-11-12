using System;
using System.Collections.Generic;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 公開可能なDataset識別子、表示情報、出典を検証して保持する。
    /// </summary>
    internal sealed class Dataset
    {
        internal string Id { get; }
        internal string Title { get; }
        internal IReadOnlyList<string> FeatureTypes { get; }
        internal string Version { get; }
        internal string Publisher { get; }
        internal string LicenseId { get; }
        internal string LicenseUrl { get; }
        internal string SourceUrl { get; }

        internal Dataset(string id, string title, IEnumerable<string> featureTypes, string version, string publisher, string licenseId, string licenseUrl, string sourceUrl)
        {
            if (!WireValues.Id(id) || !WireValues.Text(title) || !WireValues.Text(version) || !WireValues.Text(publisher) || !WireValues.Text(licenseId) || !WireValues.Url(licenseUrl) || !WireValues.Url(sourceUrl))
            {
                throw new ArgumentException("Invalid dataset metadata.");
            }

            Id = id;
            Title = title;
            FeatureTypes = WireValues.FeatureTypes(featureTypes);
            Version = version;
            Publisher = publisher;
            LicenseId = licenseId;
            LicenseUrl = licenseUrl;
            SourceUrl = sourceUrl;
        }

        /// <summary>
        /// Datasetの公開用メタデータを契約の結果形式に変換する。
        /// </summary>
        /// <returns>未取得の項目をnullで保持したDatasetのJSONオブジェクト。</returns>
        internal Dictionary<string, object> ToJson() => Json.Object(("datasetId", Id), ("title", Title), ("featureTypes", FeatureTypes), ("version", Version), ("publisher", Publisher), ("licenseId", LicenseId), ("licenseUrl", LicenseUrl), ("sourceUrl", SourceUrl));
    }
}
