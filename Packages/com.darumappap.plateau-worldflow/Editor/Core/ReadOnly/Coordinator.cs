using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DarumaPPAP.PlateauWorldFlow.ReadOnly
{
    /// <summary>
    /// Read-only操作の入力とProjectの一致を検証し、Dataset照会と範囲計画のJSON応答を組み立てる。
    /// </summary>
    internal sealed class Coordinator
    {
        private readonly string _fingerprint;

        internal Coordinator(string projectFingerprint)
        {
            if (!WireValues.Hash(projectFingerprint))
            {
                throw new ArgumentException("Invalid project fingerprint.");
            }

            _fingerprint = projectFingerprint;
        }

        /// <summary>
        /// Projectと取得元を照合してRead-only要求を評価し、操作別の応答を生成する。
        /// </summary>
        /// <param name="json">Read-only契約の要求JSON。</param>
        /// <param name="catalog">現在の取得元から得た観測。未取得の場合はnull。</param>
        /// <param name="now">観測の有効期間と応答時刻の判定に用いる時刻。</param>
        /// <param name="snapshot">範囲計画の入力となる既存のCoverage。</param>
        /// <param name="inspected">今回発行したCoverage。発行しなかった場合はnull。</param>
        /// <param name="cancellationToken">処理開始前と応答生成前に確認するキャンセル通知。</param>
        /// <returns>操作結果、判定状態、診断、証跡を格納した応答JSON。</returns>
        internal string Execute(string json, Catalog catalog, DateTimeOffset now, CoverageSnapshot snapshot, out CoverageSnapshot inspected, CancellationToken cancellationToken = default)
        {
            inspected = null;
            cancellationToken.ThrowIfCancellationRequested();
            if (json == null || json.Length > 32768)
            {
                throw new FormatException("Invalid request size.");
            }

            var request = Json.Parse(json);
            var id = Json.Field<string>(request, "requestId");
            var fingerprint = Json.Field<string>(request, "projectFingerprint");
            var operation = Json.Field<string>(request, "operation");
            var input = Json.Field<Dictionary<string, object>>(request, "input");
            if (request.Count != 5 || Json.Field<int>(request, "schemaVersion") != 1 || !WireValues.Uuid(id) || !WireValues.Hash(fingerprint) || input == null || !KnownOperation(operation))
            {
                throw new FormatException("Invalid request envelope.");
            }

            string Respond(string status, object result, Catalog evidence = null, params object[] diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = evidence == null ? Array.Empty<object>() : new[] { evidence.Evidence() };
                return Json.Write(Json.Object(("schemaVersion", 1), ("requestId", id), ("projectFingerprint", fingerprint), ("operation", operation), ("status", status), ("checkedAt", Digests.Timestamp(now)), ("result", result), ("evidence", record), ("diagnostics", diagnostics), ("warnings", Array.Empty<string>())));
            }

            string Error(string status, string code, string location, Catalog evidence = null) => Respond(status, null, evidence, Diagnostic(code, location));

            // 別Projectの入力から照会結果や証跡を返さない。
            if (fingerprint != _fingerprint) return Error("BLOCKED", "PROJECT_MISMATCH", "projectFingerprint");

            switch (operation)
            {
                case "datasets.list":
                {
                    var source = Json.Field<string>(input, "source");
                    var reference = Json.Field<string>(input, "sourceReference");
                    var max = Json.Field<int>(input, "maxResults");
                    // 入力元が許可済み識別子と一致しない限り、一覧を開示しない。
                    if (input.Count != 3 || !WireValues.Source(source) || !WireValues.Id(reference)) return Error("BLOCKED", "SOURCE_NOT_ALLOWED", "sourceReference");

                    // 既定値で補完すると、利用者の指定した上限を超える可能性がある。
                    if (max < 1 || max > WireValues.MaxDatasets) return Error("FAILED", "LIMIT_EXCEEDED", "maxResults");

                    // 期限外の観測結果で一覧の存在や欠落を断定しない。
                    if (catalog == null || now < catalog.CheckedAt || now >= catalog.ValidUntil) return Error("UNKNOWN", "COVERAGE_UNCONFIRMED", "sourceReference");

                    // Sourceは区分と承認済み参照の両方で照合する。
                    if (source != catalog.Source || reference != catalog.Reference) return Error("BLOCKED", "SOURCE_NOT_ALLOWED", "sourceReference");

                    var entries = catalog.Observations.OrderBy(item => item.Dataset.Id, StringComparer.Ordinal).Take(max + 1).ToArray();
                    bool truncated = entries.Length > max;
                    // 照会を完了していない空の一覧は確認済みゼロ件ではない。
                    if (!catalog.Complete && entries.Length == 0) return Error("UNKNOWN", "COVERAGE_UNCONFIRMED", "sourceReference", catalog);

                    var result = Json.Object(("source", source), ("datasets", entries.Take(max).Select(item => item.Dataset.ToJson())), ("truncated", truncated));
                    var diagnostics = new List<object>();
                    if (truncated)
                    {
                        diagnostics.Add(Diagnostic("LIMIT_EXCEEDED", "maxResults"));
                    }

                    if (!catalog.Complete)
                    {
                        diagnostics.Add(Diagnostic("COVERAGE_UNCONFIRMED", "sourceReference"));
                    }

                    return Respond(diagnostics.Count == 0 ? "READY" : "PARTIAL", result, catalog, diagnostics.ToArray());
                }
                case "coverage.inspect":
                {
                    var source = Json.Field<string>(input, "source");
                    var reference = Json.Field<string>(input, "sourceReference");
                    var datasetId = Json.Field<string>(input, "datasetId");
                    var max = Json.Field<int>(input, "maxResults");
                    bool hasCodes = input.ContainsKey("gridCodes");
                    // 取得元を安全に特定できない要求には、観測結果を開示しない。
                    if (!WireValues.Id(reference)) return Error("BLOCKED", "SOURCE_NOT_ALLOWED", "sourceReference");

                    // 未知フィールドや不正な識別子を含む照会は処理しない。
                    if (input.Count != (hasCodes ? 5 : 4) || !WireValues.Source(source) || !WireValues.Id(datasetId)) return Error("FAILED", "INVALID_INPUT", "input");

                    // 一覧取得の負荷を要求の上限で制限する。
                    if (max < 1 || max > WireValues.MaxGridCodes) return Error("FAILED", "LIMIT_EXCEEDED", "maxResults");

                    string[] codes = null;
                    if (hasCodes)
                    {
                        var issue = ReadCodes(input, "gridCodes", false, out codes);
                        // 無効なコードを落として部分的に処理すると入力誤りを見落とす。
                        if (issue != null) return Error("FAILED", issue, "gridCodes");
                    }

                    // 同じDataset IDでも、取得元が異なる観測は流用できない。
                    if (catalog != null && (source != catalog.Source || reference != catalog.Reference)) return Error("BLOCKED", "SOURCE_NOT_ALLOWED", source != catalog.Source ? "source" : "sourceReference");

                    // 期限切れや未取得の観測結果を、存在なしとして扱わない。
                    if (catalog == null || now < catalog.CheckedAt || now >= catalog.ValidUntil) return Error("UNKNOWN", "COVERAGE_UNCONFIRMED", "datasetId");

                    if (!catalog.TryGet(datasetId, out var observation))
                    {
                        return catalog.Complete ? Error("UNAVAILABLE", "DATASET_NOT_FOUND", "datasetId", catalog) : Error("UNKNOWN", "COVERAGE_UNCONFIRMED", "datasetId", catalog);
                    }

                    var coverage = new CoverageSnapshot(id, _fingerprint, catalog, observation, codes, max);
                    var diagnostics = new List<object>();
                    if (coverage.HasUnverified)
                    {
                        diagnostics.Add(Diagnostic("COVERAGE_UNCONFIRMED", "gridCodes"));
                    }

                    if (coverage.Truncated)
                    {
                        diagnostics.Add(Diagnostic("LIMIT_EXCEEDED", "maxResults"));
                    }

                    if (coverage.MissingCount > 0 && coverage.AvailableCount == 0 && !coverage.HasUnverified && !coverage.Truncated)
                    {
                        diagnostics.Add(Diagnostic("COVERAGE_NOT_FOUND", "gridCodes"));
                    }

                    var status = "READY";
                    if (coverage.Truncated || coverage.HasUnverified)
                    {
                        status = coverage.AvailableCount + coverage.MissingCount == 0 ? "UNKNOWN" : "PARTIAL";
                    }
                    else if (coverage.MissingCount > 0 && coverage.AvailableCount == 0)
                    {
                        status = "UNAVAILABLE";
                    }

                    string response = Respond(status, coverage.ToJson(), catalog, diagnostics.ToArray());
                    inspected = coverage;
                    return response;
                }
                case "areas.plan":
                {
                    var datasetId = Json.Field<string>(input, "datasetId");
                    var snapshotId = Json.Field<string>(input, "coverageSnapshotId");
                    int revision = Json.Field<int>(input, "planRevision");
                    // 照会結果に結び付かない選択案の作成を防ぐ。
                    if (input.Count != 5 || !WireValues.Id(datasetId) || !WireValues.Uuid(snapshotId) || revision < 1) return Error("FAILED", "INVALID_INPUT", "input");

                    // 型違いを未指定CRSとして扱うと、入力誤りを利用者が識別できない。
                    if (!input.TryGetValue("coordinateReference", out var crs) || crs != null && (!(crs is string) || !WireValues.Crs((string)crs))) return Error("FAILED", "INVALID_INPUT", "coordinateReference");

                    var issue = ReadCodes(input, "selectedGridCodes", true, out var selected);
                    // 選択全体を表現できない場合はPlanを発行しない。
                    if (issue != null) return Error("FAILED", issue, "selectedGridCodes");

                    bool changedCatalog = snapshot != null && catalog != null && !ReferenceEquals(snapshot.Catalog, catalog);
                    // 対象が変わった場合や期限外では新しいCoverageが必要になる。
                    if (snapshot == null || snapshot.Id != snapshotId || snapshot.DatasetId != datasetId || snapshot.ProjectFingerprint != _fingerprint || changedCatalog || now < snapshot.Catalog.CheckedAt || now >= snapshot.Catalog.ValidUntil) return Error("BLOCKED", "PLAN_STALE", "coverageSnapshotId");

                    var plan = new AreaPlan(id, revision, snapshot, selected, (string)crs);
                    // 非対応の経度跨ぎを通常の矩形に補正すると別範囲を指してしまう。
                    if (plan.CrossesLongitudeBoundary) return Error("BLOCKED", "LONGITUDE_BOUNDARY", "extent", snapshot.Catalog);

                    var diagnostics = new List<object>();
                    if (plan.Unverified.Count > 0)
                    {
                        diagnostics.Add(Diagnostic("COVERAGE_UNCONFIRMED", "selectedGridCodes"));
                    }

                    if (plan.HasUnverifiedCrs)
                    {
                        diagnostics.Add(Diagnostic("CRS_UNVERIFIED", "coordinateReference"));
                    }

                    if (plan.Missing.Count > 0 && plan.Covered.Count == 0 && plan.Unverified.Count == 0)
                    {
                        diagnostics.Add(Diagnostic("COVERAGE_NOT_FOUND", "selectedGridCodes"));
                    }

                    string status = "READY";
                    if (plan.Unverified.Count > 0 || plan.HasUnverifiedCrs)
                    {
                        status = plan.Covered.Count + plan.Missing.Count == 0 ? "UNKNOWN" : "PARTIAL";
                    }
                    else if (plan.Covered.Count == 0)
                    {
                        status = "UNAVAILABLE";
                    }

                    return Respond(status, plan.ToJson(), snapshot.Catalog, diagnostics.ToArray());
                }
                default:
                    return Error("BLOCKED", "CAPABILITY_UNAVAILABLE", "operation");
            }
        }

        private bool KnownOperation(string operation) => operation == "datasets.list" || operation == "coverage.inspect" || operation == "areas.plan" || operation == "dependencies.inspect" || operation == "areas.preview" || operation == "areas.preview_png";

        private string ReadCodes(Dictionary<string, object> input, string key, bool requireSelection, out string[] result)
        {
            result = null;
            var values = Json.Field<object[]>(input, key);
            // 欠落や型不一致を全件照会として解釈しない。
            if (values == null || requireSelection && values.Length == 0) return "INVALID_INPUT";

            // 重複で上限を回避できないよう、正規化前の件数を検査する。
            if (values.Length > WireValues.MaxGridCodes) return "LIMIT_EXCEEDED";

            // 検査を通過したコードだけを照会や選択に使う。
            if (values.Any(value => !WireValues.Grid(value as string))) return "INVALID_GRID_CODE";

            result = values.Cast<string>().ToArray();
            return null;
        }

        private object Diagnostic(string code, string location) => Json.Object(("code", code), ("location", location), ("retryable", false));
    }
}
