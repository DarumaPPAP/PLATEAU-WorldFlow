using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal static class WireValues
    {
        internal const int MaxDatasets = 20;
        internal const int MaxGridCodes = 500;
        private const string Secret = @"[Gg][Hh][PpOoUuSsRr]_|[Gg][Ii][Tt][Hh][Uu][Bb]_[Pp][Aa][Tt]_|[Ss][Kk]-|[Aa][Kk][Ii][Aa][A-Z0-9]{16}|eyJ[A-Za-z0-9_-]{8}|[Bb][Ee][Aa][Rr][Ee][Rr][ _-]|[A-Za-z0-9_-]{32,}";

        internal static bool Id(string value) => Safe(value, @"^(?:[A-Za-z0-9][A-Za-z0-9_-]{0,63})(?![\s\S])");
        internal static bool Uuid(string value) => Matches(value, @"^(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?![\s\S])");
        internal static bool Hash(string value) => Matches(value, @"^(?:[a-f0-9]{64})(?![\s\S])");
        internal static bool Grid(string value) => Matches(value, @"^(?:[0-9]{4}(?:[0-7]{2}(?:[0-9]{2}[1-4]{0,3})?)?)(?![\s\S])");
        internal static bool Crs(string value) => value == null || Matches(value, @"^(?:EPSG:[1-9][0-9]{0,5})(?![\s\S])");
        internal static bool Text(string value) => value == null || Safe(value, @"^(?:[A-Za-z0-9\u3000-\u30ff\u3400-\u9fff][A-Za-z0-9\u3000-\u30ff\u3400-\u9fff ._()・-]{0,127})(?![\s\S])") && !value.Contains("..");
        internal static bool Url(string value) => value == null || value.Length <= 256 && Safe(value, @"^(?:https://(?:www\.mlit\.go\.jp|www\.geospatial\.jp|www\.gsi\.go\.jp|www\.creativecommons\.org|creativecommons\.org|www\.digital\.go\.jp)(?:/[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*)*/?)(?![\s\S])");
        internal static bool Source(string value) => value == "LOCAL" || value == "REMOTE";

        internal static IReadOnlyList<string> FeatureTypes(IEnumerable<string> values)
        {
            // 未確認の一覧は、確認済みゼロ件と区別する。
            if (values == null) return null;

            var items = values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Take(65).ToArray();
            if (items.Length > 64 || items.Any(value => !Id(value)))
            {
                throw new ArgumentException("Invalid feature types.");
            }

            return Array.AsReadOnly(items);
        }

        private static bool Safe(string value, string pattern) => Matches(value, pattern) && !Regex.IsMatch(value, Secret, RegexOptions.CultureInvariant);
        private static bool Matches(string value, string pattern) => value != null && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
    }
}
