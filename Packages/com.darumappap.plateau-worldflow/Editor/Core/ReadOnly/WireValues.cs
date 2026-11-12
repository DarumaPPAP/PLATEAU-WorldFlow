using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// Read-only JSONで扱う識別子、文字列、URLの形式を検証し、公開に適さない入力を拒否する。
    /// </summary>
    internal static class WireValues
    {
        internal const int MaxDatasets = 20;
        internal const int MaxGridCodes = 500;
        private const string Secret = @"[Gg][Hh][PpOoUuSsRr]_|[Gg][Ii][Tt][Hh][Uu][Bb]_[Pp][Aa][Tt]_|[Ss][Kk]-|[Aa][Kk][Ii][Aa][A-Z0-9]{16}|eyJ[A-Za-z0-9_-]{8}|[Bb][Ee][Aa][Rr][Ee][Rr][ _-]";

        /// <summary>
        /// Datasetや取得元の識別子に許可した文字種と長さを検証し、既知の認証情報形式を拒否する。
        /// </summary>
        /// <param name="value">公開用の識別子。</param>
        /// <returns>識別子として受理できる場合はtrue。</returns>
        internal static bool Id(string value) => Safe(value, @"^(?:[A-Za-z0-9][A-Za-z0-9_-]{0,63})(?![\s\S])");
        internal static bool Uuid(string value) => Matches(value, @"^(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?![\s\S])");
        internal static bool Hash(string value) => Matches(value, @"^(?:[a-f0-9]{64})(?![\s\S])");
        internal static bool Grid(string value) => Matches(value, @"^(?:[0-9]{4}(?:[0-7]{2}(?:[0-9]{2}[1-4]{0,3})?)?)(?![\s\S])");
        internal static bool Crs(string value) => value == null || Matches(value, @"^(?:EPSG:[1-9][0-9]{0,5})(?![\s\S])");
        /// <summary>
        /// 日本語の表示情報に許可した文字種を検証し、パスに使われる区切りや既知の認証情報形式を拒否する。
        /// </summary>
        /// <param name="value">表示に使う公開メタデータ。</param>
        /// <returns>受理できる文字列か未取得を示すnullの場合はtrue。</returns>
        internal static bool Text(string value) => value == null || Safe(value, @"^(?:[A-Za-z0-9\u3000-\u30ff\u3400-\u9fff\uFF10-\uFF19\uFF08][A-Za-z0-9\u3000-\u30ff\u3400-\u9fff\uFF10-\uFF19\uFF08\uFF09 ._()・-]{0,127})(?![\s\S])") && !value.Contains("..");
        /// <summary>
        /// 出典やライセンスのURLを検証し、公式ホストのHTTPSと公開可能なパスに限定する。
        /// </summary>
        /// <param name="value">出典またはライセンスのURL。</param>
        /// <returns>受理できるURLか未取得を示すnullの場合はtrue。</returns>
        internal static bool Url(string value) => value == null || value.Length <= 256 && Safe(value, @"^(?:https://(?:www\.mlit\.go\.jp|www\.geospatial\.jp|www\.gsi\.go\.jp|www\.creativecommons\.org|creativecommons\.org|www\.digital\.go\.jp)(?:/[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*)*/?)(?![\s\S])");
        internal static bool Source(string value) => value == "LOCAL" || value == "REMOTE";

        /// <summary>
        /// 確認済みの地物種別を検証し、重複を除いて契約の順序にそろえる。
        /// </summary>
        /// <param name="values">地物種別の観測。未確認の場合はnull。</param>
        /// <returns>確認済みの種別を格納した一覧。未確認の場合はnull。</returns>
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
