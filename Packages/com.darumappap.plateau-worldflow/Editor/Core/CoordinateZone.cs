using System;
using System.Globalization;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// 国土地理院の平面直角座標系告示から、自治体コードだけで確定できる系番号を返す。
    /// </summary>
    internal sealed class CoordinateZone
    {
        // 都道府県番号の順。告示で複数の系に分かれる都道府県は0。
        private readonly int[] _prefectures = { 0, 0, 10, 10, 10, 10, 10, 9, 9, 9, 9, 9, 9, 0, 9, 8, 7, 7, 6, 8, 8, 7, 8, 7, 6, 6, 6, 6, 5, 6, 6, 5, 3, 5, 3, 3, 4, 4, 4, 4, 2, 2, 1, 2, 2, 2, 0, 0 };

        /// <summary>
        /// 自治体コードに対応する系番号を返す。詳細な地理照合が必要な場合は0を返す。
        /// </summary>
        internal int FromMunicipality(string code)
        {
            // 自治体コードの入力が確定していない場合は、系番号を推定しない。
            if (code == null || code.Length != 5 || !int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out int municipality)) return 0;

            int prefecture = municipality / 1000;
            // 東京の特別区と市は告示上の南方離島の条件に該当しない。
            if (municipality >= 13101 && municipality <= 13123 || municipality >= 13201 && municipality <= 13229) return 9;

            return prefecture > 0 && prefecture < _prefectures.Length && municipality % 1000 != 0 ? _prefectures[prefecture] : 0;
        }
    }
}
