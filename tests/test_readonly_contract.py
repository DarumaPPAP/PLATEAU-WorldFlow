from copy import deepcopy
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from static_common import load_json
from validate_contracts import contract_issues, validators


class ReadonlyContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.golden = load_json(ROOT / "tests/fixtures/phase1/readonly-golden.json")
        cls.schemas = validators(ROOT)

    def issues(self, direction, payload):
        name = "readonly-" + direction
        return contract_issues(self.schemas[name], name, payload)

    def test_shared_golden(self):
        for case in [*self.golden["cases"], self.golden["png"]]:
            with self.subTest(operation=case["request"]["operation"], status=case["response"]["status"]):
                self.assertEqual(bool(self.issues("request", case["request"])), case.get("invalidRequest", False))
                self.assertFalse(self.issues("response", case["response"]))

    def test_reject_inconsistent_or_unsafe_response(self):
        def change(document, keys, value):
            target = document
            for key in keys[:-1]:
                target = target[key]
            target[keys[-1]] = value

        cases = [
            (1, ["status"], "READY"),
            (1, ["evidence"], []),
            (0, ["result", "datasets", 0, "datasetId"], "ghp_exampletoken"),
            (0, ["result", "datasets", 0, "title"], "city\nprivate"),
            (1, ["result", "availableGridCodes", 0, "extent", "south"], 91),
            (1, ["result", "availableGridCodes", 0, "extent", "west"], 141),
            (1, ["result", "availableGridCodes", 0, "code"], "533946"),
            (1, ["result", "unavailableGridCodes"], ["533950"]),
            (1, ["result", "availableGridCodes", 0, "availableLods"], [1, 1]),
            (0, ["evidence", 0, "source"], "https://www.mlit.go.jp/?token=private"),
            (0, ["result", "datasets", 0, "title"], "ghp_exampletoken"),
            (0, ["result", "datasets", 0, "token"], "example"),
            (0, ["checkedAt"], "2026-02-30T00:00:00Z"),
            (2, ["result", "estimatedAreaSquareMeters"], 1),
            (2, ["result", "missingGridCodes"], []),
            (3, ["diagnostics"], []),
            ("png", ["result", "relativePath"], "../../outside.png"),
            ("png", ["result", "artifactId"], "00000000-0000-4000-8000-000000000001"),
        ]
        for index, keys, value in cases:
            with self.subTest(index=index, keys=keys, value=value):
                case = self.golden["png"] if index == "png" else self.golden["cases"][index]
                document = deepcopy(case["response"])
                change(document, keys, value)
                self.assertTrue(self.issues("response", document))

    def test_list_status_requires_matching_observations(self):
        response = self.golden["cases"][0]["response"]
        for status, datasets in [("PARTIAL", []), ("UNAVAILABLE", response["result"]["datasets"])]:
            with self.subTest(status=status):
                document = deepcopy(response)
                document["status"] = status
                document["diagnostics"] = [{"code": "DATASET_NOT_FOUND", "location": "datasetId", "retryable": False}]
                document["result"]["datasets"] = datasets
                self.assertIn("readonly:status", self.issues("response", document))

    def test_request_bounds_and_required_fields(self):
        for index, key, value in [(0, "maxResults", 21), (1, "gridCodes", ["533945"] * 501),
                                  (1, "sourceReference", "ghp_exampletoken"), (1, "sourceReference", "D:/private"),
                                  (1, "datasetId", "a" * 65), (2, "planRevision", 0), ("png", "width", 1025)]:
            with self.subTest(key=key):
                case = self.golden["png"] if index == "png" else self.golden["cases"][index]
                request = deepcopy(case["request"])
                request["input"][key] = value
                self.assertTrue(self.issues("request", request))
                del request["input"][key]
                if key != "gridCodes":
                    self.assertTrue(self.issues("request", request))

    def test_dataset_id_accepts_minimum_long_length(self):
        case = self.golden["cases"][1]
        request = deepcopy(case["request"])
        response = deepcopy(case["response"])
        request["input"]["datasetId"] = response["result"]["datasetId"] = "a" * 32
        self.assertFalse(self.issues("request", request))
        self.assertFalse(self.issues("response", response))
