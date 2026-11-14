using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 範囲案に固定されたメッシュ分類と概略図の表示境界を保持する。
    /// </summary>
    internal sealed class Preview
    {
        internal string Id { get; }
        internal AreaPlan Plan { get; }
        internal CoverageSnapshot Snapshot { get; }
        internal Extent Extent { get; }
        internal IReadOnlyList<(string Code, Extent Extent, string Availability, bool Selected)> Cells { get; }

        internal Preview(string id, AreaPlan plan, CoverageSnapshot snapshot)
        {
            Id = id;
            Plan = plan;
            Snapshot = snapshot;
            var selected = new HashSet<string>(plan.Selected, StringComparer.Ordinal);
            var codes = plan.Selected.Concat(snapshot.AvailableCodes.Where(code => !selected.Contains(code)).OrderBy(code => code, StringComparer.Ordinal)).Take(WireValues.MaxGridCodes);
            Cells = Array.AsReadOnly(codes.OrderBy(code => code, StringComparer.Ordinal).Select(code =>
            {
                if (snapshot.TryGetCell(code, out var cell))
                {
                    return (code, cell.Extent, "AVAILABLE", selected.Contains(code));
                }

                return (code, (Extent)null, snapshot.IsMissing(code) ? "UNAVAILABLE" : "UNVERIFIED", true);
            }).ToArray());

            var located = Cells.Where(cell => cell.Extent != null).Select(cell => cell.Extent).ToArray();
            if (located.Length > 0)
            {
                var crs = located[0].Crs;
                double west = located.Min(extent => extent.West);
                double east = located.Max(extent => extent.East);
                if (east - west <= 180 && located.All(extent => extent.Crs == crs))
                {
                    Extent = new Extent(located.Min(extent => extent.South), west, located.Max(extent => extent.North), east, crs);
                }
            }
        }

        /// <summary>
        /// メッシュと出典に紐づいた概略図を契約形式に変換する。
        /// </summary>
        internal object ToJson() => Json.Object(("previewId", Id), ("extent", Extent?.ToJson()), ("coordinateReference", Extent?.Crs),
            ("gridCells", Cells.Select(cell => Json.Object(("code", cell.Code), ("extent", cell.Extent?.ToJson()), ("availability", cell.Availability), ("selected", cell.Selected)))),
            ("legend", new[] { "AVAILABLE", "UNAVAILABLE", "UNVERIFIED" }), ("accuracyNote", Extent?.Crs == null ? "CRS_UNVERIFIED" : "SCHEMATIC"));
    }
}
