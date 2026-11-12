using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class CoverageSnapshot
    {
        internal string Id { get; }
        internal string ProjectFingerprint { get; }
        internal string DatasetId { get; }
        internal Catalog Catalog { get; }
        internal bool Truncated { get; }
        internal bool HasUnverified => _unverified.Length > 0 || !_enumerationComplete;
        private readonly Dictionary<string, CoverageCell> _available;
        private readonly HashSet<string> _missing;
        private readonly string[] _unverified;
        private readonly bool _enumerationComplete;

        internal CoverageSnapshot(string id, string fingerprint, Catalog catalog, Observation observation, IEnumerable<string> requested, int maxResults)
        {
            Id = id;
            ProjectFingerprint = fingerprint;
            DatasetId = observation.Dataset.Id;
            Catalog = catalog;
            _enumerationComplete = requested != null || observation.EnumerationComplete;
            var codes = (requested ?? observation.AvailableCodes).Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).Take(maxResults + 1).ToArray();
            Truncated = codes.Length > maxResults;
            _available = new Dictionary<string, CoverageCell>(StringComparer.Ordinal);
            _missing = new HashSet<string>(StringComparer.Ordinal);
            var unverified = new List<string>();
            foreach (var code in codes.Take(maxResults))
            {
                if (observation.TryGetCell(code, out var cell))
                {
                    _available.Add(code, cell);
                }
                else if (observation.IsMissing(code))
                {
                    _missing.Add(code);
                }
                else
                {
                    unverified.Add(code);
                }
            }

            _unverified = unverified.ToArray();
        }

        internal int AvailableCount => _available.Count;
        internal int MissingCount => _missing.Count;
        internal bool TryGetCell(string code, out CoverageCell cell) => _available.TryGetValue(code, out cell);
        internal bool IsMissing(string code) => _missing.Contains(code);

        internal Dictionary<string, object> ToJson()
        {
            bool noVerifiedCoverage = HasUnverified && AvailableCount + MissingCount == 0;
            var available = noVerifiedCoverage ? null : _available.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.ToJson());
            var missing = noVerifiedCoverage ? null : _missing.OrderBy(code => code, StringComparer.Ordinal);
            return Json.Object(("datasetId", DatasetId), ("availableGridCodes", available), ("unavailableGridCodes", missing), ("unverifiedGridCodes", _unverified), ("truncated", Truncated));
        }
    }
}
