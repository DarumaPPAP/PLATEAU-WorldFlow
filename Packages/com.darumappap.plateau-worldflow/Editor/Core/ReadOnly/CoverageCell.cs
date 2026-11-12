using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
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

        internal Dictionary<string, object> ToJson() => Json.Object(("code", Code.Value), ("extent", Extent.ToJson()), ("featureTypes", FeatureTypes), ("availableLods", AvailableLods));
    }
}
