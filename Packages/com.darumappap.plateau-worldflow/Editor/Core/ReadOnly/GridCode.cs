using System;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// Read-only契約が扱う地域メッシュの形式を検証し、元のコードを保持する。
    /// </summary>
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
