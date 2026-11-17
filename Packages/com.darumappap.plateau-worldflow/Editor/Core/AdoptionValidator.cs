using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using Fields = System.Collections.Generic.Dictionary<string, object>;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// 読み取り専用の採用済み計画を受け取り、計画の整合性と呼び出し元が指定した対象を照合する。
    /// </summary>
    internal sealed class AdoptionValidator
    {
        private readonly string[] _decisionOrder = { "LOCATION", "DATASET", "AREA", "OVERLAP_POLICY", "QUALITY", "PLAN" };

        /// <summary>
        /// 採用済み記録のDigest、参照先、Decision履歴を照合して、現行データの再確認に進めるかを判定する。
        /// </summary>
        /// <param name="document">AI計画で作成した採用記録のJSON。</param>
        /// <param name="projectFingerprint">接続先Projectから確認したFingerprint。</param>
        /// <param name="sessionId">要求が参照する計画セッションID。</param>
        /// <param name="revision">要求が参照する確定Revision。</param>
        /// <returns>計画が照合に合格した場合はtrue。</returns>
        internal bool Accepts(string document, string projectFingerprint, string sessionId, int revision)
        {
            // 参照するProjectとRevisionを確定できる場合だけ、採用記録を処理する。
            if (string.IsNullOrEmpty(document) || !WireValues.Hash(projectFingerprint) || !WireValues.Uuid(sessionId) || revision <= 0) return false;

            Fields plan;
            try
            {
                plan = Json.Parse(document);
            }
            catch (FormatException)
            {
                return false;
            }

            bool matchesRequest = Json.Field<int>(plan, "schemaVersion") == 1 && Json.Field<string>(plan, "stage") == "PLAN_READY" && Json.Field<string>(plan, "status") == "READY" && Json.Field<string>(plan, "projectFingerprint") == projectFingerprint && Json.Field<string>(plan, "planningSessionId") == sessionId && Json.Field<int>(plan, "planRevision") == revision;
            // 保留中の計画や、別Project・別Revisionの記録を実行準備へ渡さない。
            if (!matchesRequest || !plan.TryGetValue("pending", out var pending) || pending != null || !plan.TryGetValue("nextQuestion", out var nextQuestion) || nextQuestion != null) return false;

            string digest = Json.Field<string>(plan, "planDigest");
            // 整合性を検証できるハッシュが欠落していれば、Decisionの内容を採用しない。
            if (!WireValues.Hash(digest)) return false;

            plan.Remove("planDigest");
            string wire = Json.Write(plan).Normalize(NormalizationForm.FormC);
            return Digests.Sha256(Json.Utf8.GetBytes(wire)) == digest && MatchesAdoption(plan, projectFingerprint) && HasDecisionHistory(plan, revision);
        }

        private bool MatchesAdoption(Fields plan, string projectFingerprint)
        {
            var coverage = Json.Field<Fields>(plan, "coverageReference");
            var catalog = Json.Field<Fields>(plan, "catalog");
            var area = Json.Field<Fields>(plan, "areaChoice");
            var areaPlan = Json.Field<Fields>(area, "plan");
            var quality = Json.Field<Fields>(plan, "qualityChoice");
            var check = Json.Field<Fields>(plan, "adoptionCheck");

            string source = Json.Field<string>(coverage, "source");
            string sourceReference = Json.Field<string>(coverage, "sourceReference");
            bool matchesSource = WireValues.Source(source) && source == Json.Field<string>(catalog, "source") && sourceReference == (source == "LOCAL" ? "approved-local" : "plateau-public") && sourceReference == Json.Field<string>(catalog, "sourceReference");
            string coverageId = Json.Field<string>(coverage, "coverageSnapshotId");
            string areaId = Json.Field<string>(area, "id");
            int areaRevision = Json.Field<int>(areaPlan, "planRevision");
            bool matchesReferences = WireValues.Uuid(coverageId) && coverageId == Json.Field<string>(areaPlan, "coverageSnapshotId") && coverageId == Json.Field<string>(check, "coverageSnapshotId") && WireValues.Uuid(areaId) && areaId == Json.Field<string>(areaPlan, "planId") && areaId == Json.Field<string>(check, "phase1AreaPlanId") && areaRevision > 0 && areaRevision <= Json.Field<int>(plan, "planRevision") && areaRevision == Json.Field<int>(check, "phase1PlanRevision");
            var selected = Json.Field<object[]>(areaPlan, "selectedGridCodes");
            var covered = Json.Field<object[]>(areaPlan, "coveredGridCodes");
            bool coversSelection = selected != null && selected.Length > 0 && selected.Length <= WireValues.MaxGridCodes && selected.All(code => code is string grid && WireValues.Grid(grid)) && selected.Distinct().Count() == selected.Length && covered?.Length == selected.Length && !selected.Except(covered).Any() && Json.Field<object[]>(areaPlan, "missingGridCodes")?.Length == 0 && Json.Field<object[]>(areaPlan, "unverifiedGridCodes")?.Length == 0;
            bool confirmsPlan = Json.Field<string>(check, "projectFingerprint") == projectFingerprint && Json.Field<string>(check, "status") == "READY" && Json.Field<string>(area, "previewStatus") == "READY" && Json.Field<object[]>(quality, "unverifiedRequirements")?.Length == 0;

            return matchesSource && matchesReferences && coversSelection && confirmsPlan && MatchesObservedQuality(plan, coverage, selected, quality) && IsIndependentObservation(plan, check, coverage, catalog);
        }

        /// <summary>
        /// 選択メッシュのCoverageと品質候補を照合し、全選択セルで裏付けのある要求だけを引き継ぐ。
        /// </summary>
        private bool MatchesObservedQuality(Fields plan, Fields coverage, object[] selected, Fields quality)
        {
            var result = Json.Field<Fields>(coverage, "result");
            string datasetId = Json.Field<string>(coverage, "datasetId");
            var cells = Json.Field<object[]>(result, "availableGridCodes");
            var requestedGrids = Json.Field<object[]>(coverage, "requestedGrids");
            // 同じDatasetの観測で選択メッシュが照会されていなければ、品質を確認できない。
            if (!WireValues.Id(datasetId) || datasetId != Json.Field<string>(result, "datasetId") || cells == null || requestedGrids == null || selected.Except(requestedGrids).Any()) return false;

            var unavailable = Json.Field<object[]>(result, "unavailableGridCodes") ?? Array.Empty<object>();
            var unverified = Json.Field<object[]>(result, "unverifiedGridCodes");
            // 利用可能とされたメッシュに不在または未確認の記録が重複していれば、Coverageを採用できない。
            if (unverified == null || selected.Intersect(unavailable).Any() || selected.Intersect(unverified).Any()) return false;

            var remaining = new HashSet<object>(selected);
            var selectedCells = new List<Fields>(selected.Length);
            foreach (var item in cells)
            {
                var cell = item as Fields;
                string code = Json.Field<string>(cell, "code");
                if (selected.Contains(code))
                {
                    // 一つのメッシュに複数の観測があると、採用した観測を一意に特定できない。
                    if (!remaining.Remove(code)) return false;

                    selectedCells.Add(cell);
                }
            }

            // 全選択セルの実測と、選択された品質候補の両方が必要。
            if (remaining.Count != 0 || !MatchesQualityOption(plan, quality)) return false;

            var requestedLods = Json.Field<object[]>(quality, "requestedLods");
            var requestedFeatures = Json.Field<object[]>(quality, "requestedFeatureTypes");
            // 読み取り専用計画の集約観測は、同時に指定された地物とLODの組み合わせを証明できない。
            if (requestedLods == null || requestedFeatures == null || requestedLods.Length > 0 && requestedFeatures.Length > 0) return false;

            return MatchesCommonValues(selectedCells, "availableLods", Json.Field<object[]>(quality, "verifiedLods"), requestedLods)
                && MatchesCommonValues(selectedCells, "featureTypes", Json.Field<object[]>(quality, "verifiedFeatureTypes"), requestedFeatures);
        }

        private bool MatchesQualityOption(Fields plan, Fields quality)
        {
            var options = Json.Field<object[]>(plan, "qualityOptions");
            string selectedId = Json.Field<string>(quality, "id");
            // 選択した品質を候補と照合できなければ、承認時の内容を特定できない。
            if (options == null || string.IsNullOrEmpty(selectedId)) return false;

            var matches = options.OfType<Fields>().Where(option => Json.Field<string>(option, "id") == selectedId).Take(2).ToArray();
            return matches.Length == 1 && Json.Write(matches[0]) == Json.Write(quality);
        }

        private bool MatchesCommonValues(IReadOnlyList<Fields> cells, string key, object[] declared, object[] requested)
        {
            // 実測済みの値を宣言していなければ、品質候補の根拠を照合できない。
            if (declared == null) return false;

            var common = new HashSet<object>(Json.Field<object[]>(cells[0], key) ?? Array.Empty<object>());
            for (int index = 1; index < cells.Count; index++)
            {
                common.IntersectWith(Json.Field<object[]>(cells[index], key) ?? Array.Empty<object>());
            }

            return common.Count == declared.Length && common.SetEquals(declared) && requested.All(common.Contains);
        }

        /// <summary>
        /// 採用時の再照会IDと観測期間を確認し、以前の照会の使い回しを拒否する。
        /// </summary>
        private bool IsIndependentObservation(Fields plan, Fields check, Fields coverage, Fields catalog)
        {
            string requestId = Json.Field<string>(check, "requestId");
            var areas = Json.Field<object[]>(plan, "areaOptions");
            // 採用時の照会IDを過去の照会と比較できなければ、独立した再照会と確認できない。
            if (!WireValues.Uuid(requestId) || areas == null || requestId == Json.Field<string>(coverage, "coverageSnapshotId") || requestId == Json.Field<string>(catalog, "requestId")) return false;

            foreach (var option in areas)
            {
                var area = option as Fields;
                var preview = Json.Field<Fields>(area, "preview");
                var png = Json.Field<Fields>(area, "png");
                // 過去の範囲案または証跡と同じIDなら、採用後の確認として受理できない。
                if (area == null || requestId == Json.Field<string>(area, "id") || requestId == Json.Field<string>(preview, "previewId") || requestId == Json.Field<string>(png, "artifactId")) return false;
            }

            var decisions = Json.Field<object[]>(plan, "decisions");
            var finalDecision = decisions?.LastOrDefault() as Fields;
            return TryReadTime(coverage, "checkedAt", out var coverageTime) && TryReadTime(coverage, "expiresAt", out var expires) && TryReadTime(finalDecision, "decidedAt", out var adoptedAt) && TryReadTime(check, "checkedAt", out var recheckedAt) && coverageTime <= adoptedAt && adoptedAt <= recheckedAt && recheckedAt < expires;
        }

        private bool TryReadTime(Fields fields, string key, out DateTimeOffset timestamp)
        {
            return DateTimeOffset.TryParseExact(Json.Field<string>(fields, key), Digests.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
        }

        private bool HasDecisionHistory(Fields plan, int revision)
        {
            var decisions = Json.Field<object[]>(plan, "decisions");
            // 採用に必要な全Decisionがそろっている場合だけ順序と参照を確認する。
            if (decisions?.Length != _decisionOrder.Length) return false;

            var coverage = Json.Field<Fields>(plan, "coverageReference");
            var area = Json.Field<Fields>(plan, "areaChoice");
            var quality = Json.Field<Fields>(plan, "qualityChoice");
            string areaId = Json.Field<string>(area, "id");
            string locationId = Json.Field<string>(plan, "selectedLocationId");
            string spatialFit = Json.Field<string>(area, "spatialFit");
            string targetKind = Json.Field<string>(decisions[0] as Fields, "targetKind");
            bool meetsSpatialCondition = targetKind == "POINT" ? spatialFit == "VERIFIED" : (targetKind == "MUNICIPALITY" || targetKind == "GRID") && spatialFit == "NOT_APPLICABLE";
            // 同じ地点のCoverageがあり、対象の種類に応じた包含確認が成立する場合だけ採用を照合する。
            if (locationId != Json.Field<string>(coverage, "locationId") || !meetsSpatialCondition) return false;

            string[] selections = { locationId, Json.Field<string>(coverage, "datasetId"), areaId, Json.Field<string>(area, "overlapPolicy"), Json.Field<string>(quality, "id"), areaId };
            int previousRevision = 0;
            DateTimeOffset previousDecision = DateTimeOffset.MinValue;
            for (int index = 0; index < _decisionOrder.Length; index++)
            {
                var decision = decisions[index] as Fields;
                int decisionRevision = Json.Field<int>(decision, "revision");
                bool matchesSelection = !string.IsNullOrEmpty(selections[index]) && Json.Field<string>(decision, "selectedId") == selections[index];
                bool validTime = TryReadTime(decision, "decidedAt", out var decidedAt);
                bool matchesOrder = decisionRevision > previousRevision && decisionRevision <= revision && validTime && decidedAt >= previousDecision;

                // 順序、時刻、対象、明示選択のいずれかが崩れた履歴は採用記録として使えない。
                if (!matchesSelection || !matchesOrder || Json.Field<string>(decision, "kind") != _decisionOrder[index] || Json.Field<string>(decision, "decisionSource") != "USER_EXPLICIT") return false;

                previousRevision = decisionRevision;
                previousDecision = decidedAt;
            }

            return true;
        }
    }
}
