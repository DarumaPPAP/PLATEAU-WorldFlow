using System;
using System.Collections.Generic;
using System.Linq;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// 指定されたメッシュをCoverageの観測結果で分類し、座標参照系を確認できる範囲の外接矩形を求める。
    /// </summary>
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

        /// <summary>
        /// 選択メッシュをCoverageと照合し、同じWGS84を確認できる観測から外接矩形を求める。
        /// </summary>
        /// <param name="id">選択案の識別子。</param>
        /// <param name="revision">呼び出し元が指定した選択案の版。</param>
        /// <param name="snapshot">選択範囲を照合するCoverage。</param>
        /// <param name="selected">利用者が選んだ地域メッシュコード。</param>
        /// <param name="crs">利用者が指定した座標参照系。未指定の場合はnull。</param>
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

        /// <summary>
        /// 選択範囲の分類と外接矩形を契約の結果形式に変換する。
        /// </summary>
        /// <returns>投影面積を未計算として記録した範囲計画のJSONオブジェクト。</returns>
        internal Dictionary<string, object> ToJson() => Json.Object(("planId", Id), ("planRevision", Revision), ("coverageSnapshotId", SnapshotId), ("selectedGridCodes", Selected), ("coveredGridCodes", Covered), ("missingGridCodes", Missing), ("unverifiedGridCodes", Unverified), ("extent", Extent?.ToJson()), ("overlap", null), ("estimatedAreaSquareMeters", null), ("areaMethod", "NOT_COMPUTED"));
    }
}
