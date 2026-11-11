from copy import deepcopy
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from validate_contracts import canonical_plan_bytes, plan_hash, validate_fixtures


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
