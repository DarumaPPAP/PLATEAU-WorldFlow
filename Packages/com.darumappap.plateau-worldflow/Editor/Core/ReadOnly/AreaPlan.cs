using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    internal sealed class AreaPlan
    {
        internal string Id { get; }
        internal int Revision { get; }
        internal string SnapshotId { get; }
        internal Extent Extent { get; }
        internal IReadOnlyList<string> Selected { get; }
        internal IReadOnlyList<string> Covered { get; }
        internal IReadOnlyList<string> Missing { get; }
        internal IReadOnlyList<string> Unverified { get; }
        internal bool HasUnverifiedCrs { get; }

        internal AreaPlan(string id, int revision, CoverageSnapshot snapshot, IEnumerable<string> selected, string crs)
        {
            Id = id;
            Revision = revision;
            SnapshotId = snapshot.Id;
            Selected = Array.AsReadOnly(selected.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray());
            var covered = new List<CoverageCell>();
            var missing = new List<string>();
            var unverified = new List<string>();
            foreach (var code in Selected)
            {
                if (snapshot.TryGetCell(code, out var cell))
                {
                    covered.Add(cell);
                }
                else if (snapshot.IsMissing(code))
                {
                    missing.Add(code);
                }
                else
                {
                    unverified.Add(code);
                }
            }

            Covered = covered.Select(cell => cell.Code.Value).ToList().AsReadOnly();
            Missing = missing.AsReadOnly();
            Unverified = unverified.AsReadOnly();
            // PR-Aで緯度経度として同一参照系を確認できるのはWGS84のみ。
            HasUnverifiedCrs = covered.Count != 0 && (crs != "EPSG:4326" || covered.Any(cell => cell.Extent.Crs != crs));
            if (covered.Count != 0 && !HasUnverifiedCrs)
            {
                double west = covered.Min(cell => cell.Extent.West);
                double east = covered.Max(cell => cell.Extent.East);
                if (east - west <= 180)
                {
                    Extent = new Extent(covered.Min(cell => cell.Extent.South), west, covered.Max(cell => cell.Extent.North), east, crs);
                }
                else
                {
                    CrossesLongitudeBoundary = true;
                }
            }
        }

        internal bool CrossesLongitudeBoundary { get; }

        internal Dictionary<string, object> ToJson() => Json.Object(("planId", Id), ("planRevision", Revision), ("coverageSnapshotId", SnapshotId), ("selectedGridCodes", Selected), ("coveredGridCodes", Covered), ("missingGridCodes", Missing), ("unverifiedGridCodes", Unverified), ("extent", Extent?.ToJson()), ("overlap", null), ("estimatedAreaSquareMeters", null), ("areaMethod", "NOT_COMPUTED"));
    }
}
