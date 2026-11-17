using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// 採用済みの計画と実行側の照合条件を独立に変更し、Import前に不一致を拒否することを検証する。
    /// </summary>
    internal sealed class AdoptionTests
    {
        /// <summary>
        /// 有効な採用記録だけを受け入れ、要求・Digest・採用履歴の不一致を拒否する。
        /// </summary>
        [TestCase("adopted", true)]
        [TestCase("digest", false)]
        [TestCase("project", false)]
        [TestCase("session", false)]
        [TestCase("requested-revision", false)]
        [TestCase("changed-revision", false)]
        [TestCase("partial", false)]
        [TestCase("decision-order", false)]
        [TestCase("decision-source", false)]
        [TestCase("adoption-revision", false)]
        [TestCase("adoption-project", false)]
        [TestCase("adoption-at-expiry", false)]
        [TestCase("reused-coverage-request", false)]
        [TestCase("point-without-spatial-evidence", false)]
        [TestCase("missing-observed-cell", false)]
        [TestCase("coverage-dataset-mismatch", false)]
        [TestCase("coverage-grid-not-requested", false)]
        [TestCase("rehashed-unverified-lod", false)]
        [TestCase("unmatched-quality-choice", false)]
        public void ValidatesAdoptedPlanningResult(string change, bool expected)
        {
            var fields = ReadFixture();
            var project = Json.Field<string>(fields, "projectFingerprint");
            var session = Json.Field<string>(fields, "planningSessionId");
            var revision = Json.Field<int>(fields, "planRevision");
            var decisions = Json.Field<object[]>(fields, "decisions");
            var adoption = Json.Field<Dictionary<string, object>>(fields, "adoptionCheck");
            var coverage = Json.Field<Dictionary<string, object>>(fields, "coverageReference");
            var coverageResult = Json.Field<Dictionary<string, object>>(coverage, "result");
            var quality = Json.Field<Dictionary<string, object>>(fields, "qualityChoice");

            switch (change)
            {
                case "digest":
                    fields["planDigest"] = new string('0', 64);
                    break;
                case "project":
                    project = new string('0', 64);
                    break;
                case "session":
                    session = "00000000-0000-4000-8000-000000000001";
                    break;
                case "requested-revision":
                    revision++;
                    break;
                case "changed-revision":
                    fields["planRevision"] = ++revision;
                    break;
                case "partial":
                    fields["status"] = "PARTIAL";
                    Rehash(fields);
                    break;
                case "decision-order":
                    (decisions[0], decisions[1]) = (decisions[1], decisions[0]);
                    Rehash(fields);
                    break;
                case "decision-source":
                    ((Dictionary<string, object>)decisions[0])["decisionSource"] = "INFERRED";
                    Rehash(fields);
                    break;
                case "adoption-revision":
                    adoption["phase1PlanRevision"] = Json.Field<int>(adoption, "phase1PlanRevision") + 1;
                    Rehash(fields);
                    break;
                case "adoption-project":
                    adoption["projectFingerprint"] = new string('0', 64);
                    Rehash(fields);
                    break;
                case "adoption-at-expiry":
                    adoption["checkedAt"] = coverage["expiresAt"];
                    Rehash(fields);
                    break;
                case "reused-coverage-request":
                    adoption["requestId"] = adoption["coverageSnapshotId"];
                    Rehash(fields);
                    break;
                case "point-without-spatial-evidence":
                    Json.Field<Dictionary<string, object>>(fields, "areaChoice")["spatialFit"] = "NOT_APPLICABLE";
                    Rehash(fields);
                    break;
                case "missing-observed-cell":
                    coverageResult["availableGridCodes"] = Array.Empty<object>();
                    Rehash(fields);
                    break;
                case "coverage-dataset-mismatch":
                    coverageResult["datasetId"] = "99999";
                    Rehash(fields);
                    break;
                case "coverage-grid-not-requested":
                    coverage["requestedGrids"] = new[] { "533946" };
                    Rehash(fields);
                    break;
                case "rehashed-unverified-lod":
                    quality["requestedLods"] = new[] { 2 };
                    quality["verifiedLods"] = new[] { 2 };
                    Json.Field<object[]>(fields, "qualityOptions")[0] = quality;
                    Rehash(fields);
                    break;
                case "unmatched-quality-choice":
                    quality["reason"] = "別候補の記録";
                    Rehash(fields);
                    break;
            }

            var validator = new AdoptionValidator();
            Assert.That(validator.Accepts(Json.Write(fields), project, session, revision), Is.EqualTo(expected));
        }

        private Dictionary<string, object> ReadFixture()
        {
            string repository = Environment.GetEnvironmentVariable("WORLDFLOW_REPOSITORY");
            if (string.IsNullOrEmpty(repository))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AdoptionTests).Assembly);
                repository = Path.GetFullPath(Path.Combine(package.resolvedPath, "..", ".."));
            }

            string path = Path.Combine(repository, "tests", "fixtures", "planning", "adopted-plan.json");
            return Json.Parse(File.ReadAllText(path, Json.Utf8));
        }

        /// <summary>
        /// 内容を改変した入力に正しい形式のDigestを付け直し、Digest以外の照合が必要な状態を作る。
        /// </summary>
        private void Rehash(Dictionary<string, object> fields)
        {
            fields.Remove("planDigest");
            fields["planDigest"] = Digests.Sha256(Json.Utf8.GetBytes(Json.Write(fields).Normalize(NormalizationForm.FormC)));
        }

    }
}
