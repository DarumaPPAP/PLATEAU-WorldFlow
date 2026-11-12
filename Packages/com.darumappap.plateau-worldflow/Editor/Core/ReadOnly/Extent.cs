using System;
using System.Collections.Generic;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
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

        internal Dictionary<string, object> ToJson() => Json.Object(("south", Json.Real(South)), ("west", Json.Real(West)), ("north", Json.Real(North)), ("east", Json.Real(East)), ("crs", Crs));
    }
}
