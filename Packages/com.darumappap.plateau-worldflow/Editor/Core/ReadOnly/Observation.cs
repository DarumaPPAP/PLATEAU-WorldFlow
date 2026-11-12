using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class Observation
    {
        internal Dataset Dataset { get; }
        internal bool EnumerationComplete { get; }
        private readonly Dictionary<string, CoverageCell> _available;
        private readonly HashSet<string> _missing;

        internal Observation(Dataset dataset, IEnumerable<CoverageCell> available, IEnumerable<GridCode> missing, bool enumerationComplete = true)
        {
            Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            EnumerationComplete = enumerationComplete;
            _available = available.ToDictionary(cell => cell.Code.Value, StringComparer.Ordinal);
            _missing = new HashSet<string>(missing.Select(code => code.Value), StringComparer.Ordinal);
            if (_missing.Overlaps(_available.Keys))
            {
                throw new ArgumentException("Conflicting coverage observations.");
            }
        }

        internal IEnumerable<string> AvailableCodes => _available.Keys;
        internal bool TryGetCell(string code, out CoverageCell cell) => _available.TryGetValue(code, out cell);
        internal bool IsMissing(string code) => _missing.Contains(code);
    }
}
