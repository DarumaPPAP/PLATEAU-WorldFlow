using System;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class GridCode
    {
        internal string Value { get; }

        internal GridCode(string value)
        {
            if (!WireValues.Grid(value))
            {
                throw new ArgumentException("Invalid grid code.");
            }

            Value = value;
        }
    }
}
