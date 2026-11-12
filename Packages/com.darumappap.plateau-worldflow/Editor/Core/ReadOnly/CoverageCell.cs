using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 利用可能と確認したメッシュの境界と、そのメッシュで確認できた地物種別およびLODを保持する。
    /// </summary>
    internal sealed class CoverageCell
    {
        internal GridCode Code { get; }
        internal Extent Extent { get; }
        internal IReadOnlyList<string> FeatureTypes { get; }
        internal IReadOnlyList<int> AvailableLods { get; }

        internal CoverageCell(GridCode code, Extent extent, IEnumerable<string> featureTypes, IEnumerable<int> availableLods)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Extent = extent ?? throw new ArgumentNullException(nameof(extent));
            FeatureTypes = WireValues.FeatureTypes(featureTypes);
            if (availableLods != null)
            {
                var lods = availableLods.Distinct().OrderBy(value => value).Take(65).ToArray();
                if (lods.Length > 64 || lods.Any(value => value < 0))
                {
                    throw new ArgumentException("Invalid LODs.");
                }

                AvailableLods = Array.AsReadOnly(lods);
            }
        }

        /// <summary>
        /// 利用可能なメッシュの境界と確認済みの属性を契約の結果形式に変換する。
        /// </summary>
        /// <returns>未確認の属性をnullで保持したメッシュのJSONオブジェクト。</returns>
        internal Dictionary<string, object> ToJson() => Json.Object(("code", Code.Value), ("extent", Extent.ToJson()), ("featureTypes", FeatureTypes), ("availableLods", AvailableLods));
    }
}
