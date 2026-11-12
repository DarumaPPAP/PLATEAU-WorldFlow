using System;
using System.Collections.Generic;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 有限の緯度経度と座標参照系を保持し、経度境界をまたぐ反転した矩形を拒否する。
    /// </summary>
    internal sealed class Extent
    {
        internal double South { get; }
        internal double West { get; }
        internal double North { get; }
        internal double East { get; }
        internal string Crs { get; }

        internal Extent(double south, double west, double north, double east, string crs)
        {
            if (!(south >= -90 && south < north && north <= 90 && west >= -180 && west < east && east <= 180) || !WireValues.Crs(crs))
            {
                throw new ArgumentException("Invalid extent.");
            }

            South = south;
            West = west;
            North = north;
            East = east;
            Crs = crs;
        }

        /// <summary>
        /// 緯度経度をJSON数値に変換し、座標参照系と共に返す。
        /// </summary>
        internal Dictionary<string, object> ToJson() => Json.Object(("south", Json.Real(South)), ("west", Json.Real(West)), ("north", Json.Real(North)), ("east", Json.Real(East)), ("crs", Crs));
    }
}
