from copy import deepcopy
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from validate_contracts import canonical_plan_bytes, contract_issues, plan_hash, validate_fixtures, validators


class ContractTests(unittest.TestCase):
    def test_all_checked_in_cases(self):
        self.assertGreaterEqual(validate_fixtures(ROOT), 12)

    def test_cli_rejects_missing_fields(self):
        result = subprocess.run(
            [
                sys.executable,
                ROOT / "tools/validate_contracts.py",
                "--contract", "dependency-inventory",
                ROOT / "tests/fixtures/dependency-inventory/missing-field.json",
            ],
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("required", result.stderr)

    def test_canonical_bytes_and_hash(self):
        directory = ROOT / "tests/fixtures/dependency-install-plan"
        plan = json.loads((directory / "valid.json").read_text(encoding="utf-8"))
        expected = (directory / "golden-canonical.txt").read_bytes()
        self.assertEqual(canonical_plan_bytes(plan), expected)
        reordered = json.loads(json.dumps(plan, sort_keys=True, indent=2))
        self.assertEqual(plan_hash(reordered), plan["planHash"])

        altered = deepcopy(plan)
        altered["changes"][0]["reason"] += "変更"
        self.assertNotEqual(plan_hash(altered), plan["planHash"])
        altered = deepcopy(plan)
        altered["warnings"].reverse()
        self.assertNotEqual(plan_hash(altered), plan["planHash"])

    def test_import_job_accepts_lifecycle_states(self):
        validator = validators(ROOT)["import-job"]
        job = self.import_job_snapshot()
        states = (
            "PREPARED", "AWAITING_APPROVAL", "APPROVED", "RESERVED", "RUNNING", "VERIFYING",
            "COMPLETED", "PARTIAL", "FAILED", "CANCELLING", "CANCELLED", "NEEDS_RECONCILIATION",
        )
        for state in states:
            with self.subTest(state=state):
                self.assertEqual(contract_issues(validator, "import-job", dict(job, state=state)), set())

    def test_import_job_rejects_invalid_identity_and_approval_fields(self):
        validator = validators(ROOT)["import-job"]
        job = self.import_job_snapshot()
        rejected = (
            (dict(job, schemaVersion=2), "schema:const"),
            (dict(job, jobId="job"), "schema:pattern"),
            (dict(job, importPlanId="plan"), "schema:pattern"),
            (dict(job, projectFingerprint="F" * 64), "schema:pattern"),
            (dict(job, planHash=None), "schema:type"),
            (dict(job, operationKey="x" * 64), "schema:pattern"),
            (dict(job, state="QUEUED"), "schema:enum"),
            (dict(job, updatedAt="2026-01-01T00:00:00+09:00"), "schema:pattern"),
            (dict(job, approved=True), "schema:additionalProperties"),
            ({key: value for key, value in job.items() if key != "planHash"}, "schema:required"),
        )
        for record, expected in rejected:
            with self.subTest(record=record):
                self.assertEqual(contract_issues(validator, "import-job", record), {expected})

    def import_job_snapshot(self):
        return {
            "schemaVersion": 1,
            "jobId": "00000000-0000-4000-8000-000000000001",
            "importPlanId": "00000000-0000-4000-8000-000000000002",
            "projectFingerprint": "1" * 64,
            "planHash": "2" * 64,
            "operationKey": "3" * 64,
            "state": "RESERVED",
            "createdAt": "2026-01-01T00:00:00Z",
            "updatedAt": "2026-01-01T00:00:01Z",
        }

    def test_import_evidence_requires_a_capture_path_in_its_own_job(self):
        validator = validators(ROOT)["import-evidence"]
        job = self.import_job_snapshot()
        evidence = {
            key: job[key] for key in ("schemaVersion", "jobId", "importPlanId", "projectFingerprint", "planHash", "operationKey")
        }
        directory = f"Library/PLATEAUWorldFlow/Import/jobs/{job['jobId']}/captures"
        capture = {
            "viewId": "overview",
            "relativePath": f"{directory}/overview.png",
            "mimeType": "image/png",
            "sha256": "4" * 64,
            "byteLength": 2345,
            "createdAt": job["updatedAt"],
        }
        evidence.update(checkedAt=job["updatedAt"], captureStatus="PARTIAL", captures=[capture])
        self.assertEqual(contract_issues(validator, "import-evidence", evidence), set())
        rejected = (
            (dict(capture, relativePath="Assets/Screenshots/overview.png"), "schema:pattern"),
            (dict(capture, relativePath=f"{directory}/../overview.png"), "schema:pattern"),
            (dict(capture, relativePath="D:/captures/overview.png"), "schema:pattern"),
            (dict(capture, relativePath=capture["relativePath"].replace(job["jobId"], job["importPlanId"])), "capture:job"),
            (dict(capture, byteLength=0), "schema:minimum"),
            (dict(capture, sha256="PENDING"), "schema:pattern"),
        )
        for invalid, expected in rejected:
            with self.subTest(capture=invalid):
                self.assertEqual(contract_issues(validator, "import-evidence", dict(evidence, captures=[invalid])), {expected})

        incomplete = dict(evidence, captureStatus="BLOCKED", captures=[])
        self.assertEqual(contract_issues(validator, "import-evidence", incomplete), set())
        self.assertEqual(contract_issues(validator, "import-evidence", dict(incomplete, captureStatus="READY")), {"schema:minItems"})

    def test_import_result_requires_verified_scene_and_all_expected_gml(self):
        validator = validators(ROOT)["import-result"]
        job = self.import_job_snapshot()
        result = {
            key: job[key] for key in ("schemaVersion", "jobId", "importPlanId", "projectFingerprint", "planHash", "operationKey")
        }
        result.update(
            checkedAt=job["updatedAt"], state="COMPLETED",
            gml={"expected": 3, "succeeded": 3, "unverified": 0},
            sceneVerification="VERIFIED",
        )
        self.assertEqual(contract_issues(validator, "import-result", result), set())
        partial_gml = {"expected": 3, "succeeded": 2, "unverified": 1}
        rejected = (
            (dict(result, gml=partial_gml), "schema:const"),
            (dict(result, gml={"expected": 3, "succeeded": 2, "unverified": 0}), "gml:counts"),
            (dict(result, gml={"expected": 3, "succeeded": 4, "unverified": 0}), "gml:counts"),
            (dict(result, gml={"expected": 0, "succeeded": 0, "unverified": 0}), "schema:minimum"),
            (dict(result, sceneVerification="UNVERIFIED"), "schema:const"),
            (dict(result, state="RUNNING"), "schema:enum"),
        )
        for invalid, expected in rejected:
            with self.subTest(result=invalid):
                self.assertEqual(contract_issues(validator, "import-result", invalid), {expected})

        self.assertEqual(contract_issues(validator, "import-result", dict(result, state="PARTIAL", gml=partial_gml)), set())
        interrupted = dict(result, state="NEEDS_RECONCILIATION", gml={"expected": 3, "succeeded": 0, "unverified": 3}, sceneVerification="UNVERIFIED")
        self.assertEqual(contract_issues(validator, "import-result", interrupted), set())

    def test_import_plan_requires_phase2_reference_and_coordinate_zone(self):
        validator = validators(ROOT)["import-plan"]
        plan = self.import_plan()
        self.assertEqual(contract_issues(validator, "import-plan", plan), set())
        rejected = (
            (dict(plan, phase2=dict(plan["phase2"], planRevision=0)), "schema:minimum"),
            (dict(plan, phase2=dict(plan["phase2"], planDigest=None)), "schema:type"),
            (dict(plan, selection=dict(plan["selection"], coordinateZoneId=6668)), "schema:maximum"),
            (dict(plan, selection=dict(plan["selection"], gridCodes=[])), "schema:minItems"),
            (dict(plan, selection=dict(plan["selection"], gridCodes=["533945", "533945"])), "schema:uniqueItems"),
            (dict(plan, sdkVersion="4.2.0"), "schema:const"),
            (dict(plan, approved=True), "schema:additionalProperties"),
            (dict(plan, output=dict(plan["output"], overwriteExisting=True)), "schema:const"),
            (dict(plan, output=dict(plan["output"], scenePath="Assets/Scenes/SampleScene.unity")), "schema:pattern"),
            (dict(plan, source=dict(plan["source"], kind="REMOTE")), "schema:type"),
        )
        for invalid, expected in rejected:
            with self.subTest(plan=invalid):
                self.assertEqual(contract_issues(validator, "import-plan", invalid), {expected})

    def test_import_plan_matches_hashes_and_persists_scope_across_reissues(self):
        validator = validators(ROOT)["import-plan"]
        plan = self.import_plan()
        reissued = deepcopy(plan)
        reissued["importPlanId"] = "00000000-0000-4000-8000-000000000003"
        reissued["output"]["scenePath"] = reissued["output"]["scenePath"].replace(plan["importPlanId"], reissued["importPlanId"])
        reissued["selection"]["gridCodes"].reverse()
        reissued["selection"]["featureSettings"][0]["lods"].reverse()
        reissued["expiresAt"] = "2026-11-16T00:06:00Z"
        reissued["planHash"] = plan_hash(reissued)
        self.assertNotEqual(reissued["planHash"], plan["planHash"])
        self.assertEqual(contract_issues(validator, "import-plan", reissued), set())

        changed = deepcopy(plan)
        changed["selection"]["featureSettings"][0]["doSetMeshCollider"] = False
        self.assertIn("plan:hash", contract_issues(validator, "import-plan", changed))
        changed["planHash"] = plan_hash(changed)
        self.assertEqual(contract_issues(validator, "import-plan", changed), {"plan:scope"})

        wrong_path = deepcopy(plan)
        wrong_path["output"]["scenePath"] = reissued["output"]["scenePath"]
        wrong_path["planHash"] = plan_hash(wrong_path)
        self.assertEqual(contract_issues(validator, "import-plan", wrong_path), {"plan:output"})

    def import_plan(self):
        return json.loads((ROOT / "tests/fixtures/import/import-plan.json").read_text(encoding="utf-8"))
