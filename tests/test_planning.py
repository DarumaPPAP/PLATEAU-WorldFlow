"""Phase 2 contract and fake research acceptance tests."""

from copy import deepcopy
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))

from planning import PlanningError, advance, digest, place_in_confirmed_cells, timestamp, validate_state
from plateau_research import provider_items, search
from static_common import ROOT, load_json

CLOCK = datetime(2026, 11, 12, 0, 0, tzinfo=timezone.utc)
NOW = timestamp(CLOCK)
PROJECT = "1" * 64
DATASET = "12345"
AREA = "533945"
FIXTURES = ROOT / "tests/fixtures"
READONLY_GOLDEN = load_json(FIXTURES / "readonly/readonly-golden.json")


class PlanningTests(unittest.TestCase):
    def test_s01_to_s08_mesh_union(self):
        def cell(west, east, south=34.0, north=36.0, crs="EPSG:6668"):
            return dict(west=west, east=east, south=south, north=north, crs=crs)

        west, east = cell(138, 139), cell(139, 140)
        cases = (
            ("S-01", [cell(138, 140)], 0.01, "VERIFIED"),
            ("S-02", [west, east], 0.01, "VERIFIED"),
            ("S-03", [west, cell(139.001, 140)], 0.01, "UNVERIFIED"),
            ("S-03-clear-slot", [west, cell(139, 140, north=34.999), cell(139, 140, south=35.001)], 0.01, "UNVERIFIED"),
            ("S-04", [cell(140, 141)], 0.01, "OUTSIDE"),
            ("S-05", [west, cell(139, 140, crs="EPSG:4326")], 0.01, "UNVERIFIED"),
            ("S-06", [cell(140, 141), None], 0.01, "UNVERIFIED"),
            ("S-07", [west, east], None, "UNVERIFIED"),
            ("S-08", [west, None], 0.01, "UNVERIFIED"),
            ("S-08-confirmed-subset", [west, east, None], 0.01, "VERIFIED"),
            ("S-02-latitude-seam", [cell(138, 140, north=35), cell(138, 140, south=35)], 0.01, "VERIFIED"),
            ("S-02-exclusive-outer-edge", [cell(138, 139.125)], 0.125, "UNVERIFIED"),
        )
        for label, extents, accuracy, expected in cases:
            with self.subTest(case=label):
                position = dict(latitude=35.0, longitude=139.0, crs="EPSG:6668", accuracyDegrees=accuracy)
                self.assertEqual(expected, place_in_confirmed_cells({"coordinates": position}, extents))

    def test_pole_and_antimeridian_are_unverified(self):
        for lat, lon in ((89.875, 139), (35, 179.875), (35, -179.875)):
            with self.subTest(lat=lat, lon=lon):
                position = dict(latitude=lat, longitude=lon, crs="EPSG:6668", accuracyDegrees=0.125)
                extent = dict(south=-90, west=-180, north=90, east=180, crs="EPSG:6668")
                self.assertEqual("UNVERIFIED", place_in_confirmed_cells({"coordinates": position}, [extent]))

    def test_positive_uncertainty_survives_rounding_at_a_mesh_edge(self):
        position = dict(latitude=35.0, longitude=139.0, crs="EPSG:6668", accuracyDegrees=1e-16)
        extent = dict(south=35, west=139, north=36, east=140, crs="EPSG:6668")
        self.assertEqual("UNVERIFIED", place_in_confirmed_cells({"coordinates": position}, [extent]))

    def setUp(self):
        self.state = advance(None, {"action": "start"}, CLOCK)
        self.research = load_json(FIXTURES / "planning/research.json")

    def step(self, action, **values):
        self.state = advance(self.state, dict(action=action, revision=self.state["planRevision"], **values), CLOCK)
        return self.state

    def researched(self):
        self.step("research_start")
        return self.step("research", **self.research)

    def choose(self, kind, value, **fields):
        return self.step("choose", kind=kind, selectedId=value, decisionSource="USER_EXPLICIT", **fields)

    def response(self, result, status="READY", diagnostic=None, evidence="DATASET_QUERY"):
        pending = self.state["pending"]["request"]
        return dict(**{key: pending[key] for key in ("schemaVersion", "requestId", "projectFingerprint", "operation")},
                    result=result, status=status, checkedAt=NOW,
                    evidence=[] if result is None else [dict(kind=evidence, source="REMOTE_DATASET", checkedAt=NOW)],
                    diagnostics=[] if not diagnostic else [dict(code=diagnostic, retryable=False, location="input")], warnings=[])

    def listed(self, entries=None, truncated=False, source="REMOTE", target=None):
        self.researched()
        self.choose("LOCATION", "place-a", **({"targetKind": target} if target else {}))
        self.step("catalog", projectFingerprint=PROJECT, source=source)
        datasets = [dict(READONLY_GOLDEN["dataset"], datasetId=DATASET)] if entries is None else entries
        diagnostic = "LIMIT_EXCEEDED" if truncated else None
        return self.step("observe", response=self.response(dict(source=source, datasets=datasets, truncated=truncated),
                                                          "PARTIAL" if truncated else "READY", diagnostic))

    def inspected(self, partial=False, target=None):
        self.listed(target=target)
        grids = [AREA, "533946", "533947"] if partial else [AREA]
        self.choose("DATASET", DATASET, gridCodes=grids)
        cell = dict(READONLY_GOLDEN["cells"][0], featureTypes=["bldg"], availableLods=[0])
        result = dict(datasetId=DATASET, availableGridCodes=[cell], unavailableGridCodes=["533946"] if partial else [],
                      unverifiedGridCodes=["533947"] if partial else [], truncated=False)
        return self.step("observe", response=self.response(result, "PARTIAL" if partial else "READY",
                                                          "COVERAGE_UNCONFIRMED" if partial else None))

    def area_option(self, partial=False, target=None):
        self.inspected(partial, target)
        grids = self.state["coverageReference"]["requestedGrids"]
        self.step("area", gridCodes=grids, coordinateReference="EPSG:4326")
        request = self.state["pending"]["request"]
        plan = dict(planId=request["requestId"], planRevision=request["input"]["planRevision"],
                    coverageSnapshotId=request["input"]["coverageSnapshotId"], selectedGridCodes=grids,
                    coveredGridCodes=[AREA], missingGridCodes=grids[1:2], unverifiedGridCodes=grids[2:3],
                    extent=READONLY_GOLDEN["cells"][0]["extent"], overlap=None, estimatedAreaSquareMeters=None, areaMethod="NOT_COMPUTED")
        self.step("observe", response=self.response(plan, "PARTIAL" if partial else "READY",
                                                    "COVERAGE_UNCONFIRMED" if partial else None))
        self.step("preview", areaId=plan["planId"])
        self.step("observe", response=self.response(self.preview(plan), evidence="AREA_PREVIEW"))
        return plan["planId"]

    def preview(self, plan):
        extent = plan["extent"]
        statuses = zip(plan["selectedGridCodes"], ("AVAILABLE", "UNAVAILABLE", "UNVERIFIED"))
        return dict(previewId=self.state["pending"]["request"]["requestId"], extent=extent, coordinateReference="EPSG:4326",
                    gridCells=[dict(code=code, availability=availability, selected=True, extent=extent if code == AREA else None)
                               for code, availability in statuses],
                    legend=["AVAILABLE", "UNAVAILABLE", "UNVERIFIED"], accuracyNote="SCHEMATIC")

    def quality(self, lods=(0,), features=("bldg",), target=None):
        area_id = self.area_option(target=target)
        self.choose("AREA", area_id)
        self.choose("OVERLAP_POLICY", "SINGLE")
        return self.step("quality", options=[dict(id="light", preset="CUSTOM", requestedFeatureTypes=list(features),
                                                 requestedLods=list(lods), reason="軽量な候補")])

    def add_coordinates(self, latitude, longitude, crs="EPSG:4326", accuracy=0.0001):
        candidate = self.research["candidates"][0]
        candidate["coordinates"] = dict(latitude=latitude, longitude=longitude, crs=crs, precision="合成測量値",
                                        accuracyDegrees=accuracy, sourceEvidenceIds=["coordinate-a"])
        candidate["evidenceIds"].append("coordinate-a")
        self.research["evidence"].append(dict(self.research["evidence"][0], id="coordinate-a", claimType="COORDINATES"))

    def request_adoption(self):
        return self.choose("PLAN", digest(self.state))

    def test_initial_state_requires_research(self):
        state = self.state
        self.assertEqual("REQUESTED", state["stage"])
        self.assertEqual([], state["decisions"])
        self.assertIsNone(state["planDigest"])

    def test_r01_point_without_verified_position_cannot_be_adopted(self):
        self.quality(lods=(), features=())
        self.choose("QUALITY", "light")
        self.assertEqual("PARTIAL", self.state["status"])
        self.choose("PLAN", digest(self.state))
        self.assertEqual("UNVERIFIED", self.state["areaChoice"]["spatialFit"])
        self.assertEqual("PARTIAL", self.state["status"])
        self.assertIsNone(self.state["pending"])

    def test_r02_point_outside_selected_mesh_is_detected(self):
        self.add_coordinates(36.5, 139.5)
        self.quality(lods=(), features=())
        self.assertEqual("OUTSIDE", self.state["areaChoice"]["spatialFit"])

    def test_r03_different_crs_is_not_compared(self):
        self.add_coordinates(35.5, 139.5, "EPSG:6668")
        self.quality(lods=(), features=())
        self.assertEqual("UNVERIFIED", self.state["areaChoice"]["spatialFit"])

    def test_uncertainty_crossing_cell_boundary_is_not_verified(self):
        self.add_coordinates(35.99995, 139.5)
        self.quality(lods=(), features=())
        self.assertEqual("UNVERIFIED", self.state["areaChoice"]["spatialFit"])

    def test_r04_non_point_targets_can_be_adopted_without_coordinates(self):
        for target in ("MUNICIPALITY", "GRID"):
            with self.subTest(target=target):
                self.setUp()
                self.quality(lods=(), features=(), target=target)
                self.choose("QUALITY", "light")
                self.request_adoption()
                self.step("observe", response=self.response(self.preview(self.state["areaChoice"]["plan"]), evidence="AREA_PREVIEW"))
                self.assertEqual("READY", self.state["status"])
                self.assertEqual("NOT_APPLICABLE", self.state["areaChoice"]["spatialFit"])

    def test_r05_partial_final_preview_cannot_adopt(self):
        self.add_coordinates(35.5, 139.5)
        self.quality(lods=(), features=())
        self.choose("QUALITY", "light")
        self.request_adoption()
        self.step("observe", response=self.response(self.preview(self.state["areaChoice"]["plan"]),
                                                    "PARTIAL", "COVERAGE_UNCONFIRMED", "AREA_PREVIEW"))
        self.assertEqual("PARTIAL", self.state["status"])
        self.assertIsNone(self.state["planDigest"])
        self.assertIsNone(self.state["adoptionCheck"])
        self.request_adoption()
        self.step("observe", response=self.response(self.preview(self.state["areaChoice"]["plan"]), evidence="AREA_PREVIEW"))
        self.assertEqual("READY", self.state["status"])
        self.assertNotIn("ADOPTION_PREVIEW_PARTIAL", self.state["warnings"])

    def test_r07_individual_evidence_is_not_pair_evidence(self):
        self.add_coordinates(35.5, 139.5)
        self.quality()
        option = self.state["qualityOptions"][0]
        self.assertEqual([0], option["verifiedLods"])
        self.assertEqual(["bldg"], option["verifiedFeatureTypes"])
        self.assertIn("FEATURE_LOD_ASSOCIATION", option["unverifiedRequirements"])

    def test_r08_unverified_pair_is_retained_when_user_requests_it(self):
        self.add_coordinates(35.5, 139.5)
        self.quality()
        self.choose("QUALITY", "light")
        with self.assertRaisesRegex(PlanningError, "PLAN_FACTS_UNCONFIRMED"):
            self.request_adoption()
        self.assertIn("FEATURE_LOD_ASSOCIATION", self.state["missingFacts"])

    def test_r09_rehash_does_not_mask_missing_spatial_proof(self):
        golden = load_json(FIXTURES / "planning/adopted-plan.json")
        golden["candidatePlaces"][0]["coordinates"]["latitude"] = 36.5
        golden["candidatePlaces"][0]["datasetAvailability"] = "CANDIDATE_ONLY"
        golden["planDigest"] = digest(golden)
        with self.assertRaisesRegex(PlanningError, "SPATIAL_FIT_MISMATCH"):
            validate_state(golden)

    def test_unverified_adoption_fixture_is_rejected_after_rehash(self):
        fixture = load_json(FIXTURES / "planning/unverified-adoption.json")
        golden = load_json(FIXTURES / "planning" / fixture["base"])
        candidate = next(item for item in golden["candidatePlaces"] if item["id"] == fixture["candidateId"])
        candidate["coordinates"] = fixture["coordinates"]
        candidate["datasetAvailability"] = fixture["datasetAvailability"]
        golden["planDigest"] = digest(golden)
        with self.assertRaisesRegex(PlanningError, fixture["expect"]):
            validate_state(golden)

    def test_r09_rehash_cannot_forge_coverage_state_expiry_or_adoption_identifier(self):
        for field, value, expected in (
            ("status", "UNKNOWN", "COVERAGE_STATUS_MISMATCH"),
            ("expiresAt", "2027-02-01T00:00:00Z", "COVERAGE_EXPIRY_MISMATCH"),
        ):
            with self.subTest(field=field):
                golden = load_json(FIXTURES / "planning/adopted-plan.json")
                golden["coverageReference"][field] = value
                golden["planDigest"] = digest(golden)
                with self.assertRaisesRegex(PlanningError, expected):
                    validate_state(golden)
        golden = load_json(FIXTURES / "planning/adopted-plan.json")
        golden["adoptionCheck"]["requestId"] = golden["areaChoice"]["id"]
        golden["planDigest"] = digest(golden)
        with self.assertRaisesRegex(PlanningError, "PLAN_DIGEST_MISMATCH"):
            validate_state(golden)
        golden = load_json(FIXTURES / "planning/adopted-plan.json")
        golden["candidatePlaces"][1]["datasetAvailability"] = "VERIFIED_AVAILABLE"
        golden["planDigest"] = digest(golden)
        with self.assertRaisesRegex(PlanningError, "LOCATION_AVAILABILITY_MISMATCH"):
            validate_state(golden)

    def test_only_user_can_choose(self):
        with self.assertRaisesRegex(PlanningError, "USER_DECISION_REQUIRED"):
            self.step("choose", kind="LOCATION", selectedId="place-a", decisionSource="RECOMMENDED")

    def test_p2_01_dataset_presence_does_not_verify_coverage(self):
        self.listed()
        self.assertEqual("CANDIDATE_ONLY", self.state["candidatePlaces"][0]["datasetAvailability"])
        self.choose("DATASET", DATASET, gridCodes=[AREA])
        self.assertEqual("coverage.inspect", self.state["pending"]["request"]["operation"])

    def test_p2_02_same_name_requires_distinct_choice(self):
        self.researched()
        first, second = self.state["candidatePlaces"]
        self.assertEqual(first["name"], second["name"])
        self.assertNotEqual(first["locality"], second["locality"])
        self.assertIsNone(self.state["selectedLocationId"])

    def test_p2_03_unverified_work_is_not_verified_place(self):
        self.research["evidence"][0]["factStatus"] = "UNVERIFIED"
        self.research["candidates"][0]["physicalPlaceStatus"] = "UNVERIFIED"
        self.researched()
        with self.assertRaisesRegex(PlanningError, "LOCATION_UNCONFIRMED"):
            self.choose("LOCATION", "place-a")

    def test_p2_04_disputed_work_is_separate_from_place(self):
        conflicting = dict(self.research["evidence"][0], id="work", claimType="WORK_ASSOCIATION",
                           sourceKind="COMMUNITY", factStatus="DISPUTED")
        self.research["evidence"].append(conflicting)
        self.research["candidates"][0]["evidenceIds"].append("work")
        self.research["candidates"][0]["workAssociationStatus"] = "DISPUTED"
        self.researched()
        self.choose("LOCATION", "place-a")
        self.assertIn("WORK_ASSOCIATION", self.state["missingFacts"])

    def test_p2_05_failed_coverage_does_not_prove_absence(self):
        self.listed()
        self.choose("DATASET", DATASET, gridCodes=[AREA])
        self.step("observe", response=self.response(None, "UNKNOWN", "NETWORK_UNAVAILABLE"))
        self.assertEqual("UNKNOWN", self.state["status"])
        self.assertEqual("CANDIDATE_ONLY", self.state["candidatePlaces"][0]["datasetAvailability"])

    def test_p2_06_three_grid_states_survive_preview(self):
        self.area_option(True)
        self.assertEqual(["AVAILABLE", "UNAVAILABLE", "UNVERIFIED"],
                         [item["availability"] for item in self.state["areaOptions"][0]["preview"]["gridCells"]])

    def test_p2_07_uncomputed_area_stays_null(self):
        self.area_option()
        self.assertIsNone(self.state["areaOptions"][0]["plan"]["estimatedAreaSquareMeters"])

    def test_p2_08_crs_cannot_be_assigned_by_chat(self):
        self.inspected()
        self.step("area", gridCodes=[AREA], coordinateReference="EPSG:6668")
        self.step("observe", response=self.response(None, "BLOCKED", "CRS_UNVERIFIED"))
        self.assertEqual([], self.state["areaOptions"])

    def test_p2_09_requested_lod_is_not_verified(self):
        self.quality((2,))
        option = self.state["qualityOptions"][0]
        self.assertEqual([0], option["verifiedLods"])
        self.assertEqual(["LOD", "FEATURE_LOD_ASSOCIATION"], option["unverifiedRequirements"])
        self.choose("QUALITY", "light")
        with self.assertRaisesRegex(PlanningError, "PLAN_FACTS_UNCONFIRMED"):
            self.choose("PLAN", digest(self.state))

    def test_p2_10_optional_mcp_timeout_and_truncation(self):
        with patch("plateau_research.rpc", side_effect=TimeoutError):
            self.assertEqual("UNKNOWN", search("datasets", DATASET)["status"])
        document = dict(datasets=[dict(id="remote-a", name="候補", year=2025)],
                        metadata=dict(returned_count=1, total_count=2, has_more=True))
        result = provider_items(dict(content=[dict(type="text", text=json.dumps(document))]), "datasets")
        self.assertEqual("PARTIAL", result["status"])

    def test_p2_11_location_change_discards_all_downstream(self):
        self.quality()
        self.choose("QUALITY", "light")
        self.choose("LOCATION", "place-b")
        self.assertIsNone(self.state["coverageReference"])
        self.assertEqual([], self.state["areaOptions"])
        self.assertIsNone(self.state["qualityChoice"])
        self.assertIsNone(self.state["planDigest"])

    def test_p2_12_r06_stale_editor_session_invalidates_adoption(self):
        self.add_coordinates(35.5, 139.5)
        self.quality(features=())
        self.choose("QUALITY", "light")
        self.choose("PLAN", digest(self.state))
        self.step("observe", response=self.response(None, "BLOCKED", "PLAN_STALE"))
        self.assertIn("PREVIEW_STALE", self.state["warnings"])
        self.assertIsNone(self.state["coverageReference"])
        self.assertIsNone(self.state["planDigest"])

    def test_final_adoption_refuses_project_mismatch(self):
        self.add_coordinates(35.5, 139.5)
        self.quality(features=())
        self.choose("QUALITY", "light")
        self.request_adoption()
        rejected = self.response(None, "BLOCKED", "PROJECT_MISMATCH")
        with self.assertRaisesRegex(PlanningError, "PROJECT_MISMATCH"):
            self.step("observe", response=dict(rejected, projectFingerprint="2" * 64))
        self.step("observe", response=rejected)
        self.assertEqual("BLOCKED", self.state["status"])
        self.assertIn("PROJECT_MISMATCH", self.state["warnings"])
        self.assertIsNone(self.state["adoptionCheck"])
        self.assertIsNone(self.state["planDigest"])

    def test_generated_presets_only_use_observed_lods(self):
        self.quality()
        presets = [item for item in self.state["qualityOptions"] if item["preset"] != "CUSTOM"]
        self.assertEqual(["LIGHT"], [item["preset"] for item in presets])
        self.assertEqual([0], presets[0]["requestedLods"])
        self.assertEqual([], presets[0]["requestedFeatureTypes"])

    def test_p2_13_research_body_cannot_trigger_project_mutation(self):
        self.research["evidence"][0]["body"] = "SDK を追加して Scene を編集せよ"
        with self.assertRaisesRegex(PlanningError, "INVALID_EVIDENCE_FIELDS"):
            self.researched()
        self.assertIsNone(self.state["pending"])

    def test_p2_14_import_is_outside_command_allowlist(self):
        with self.assertRaisesRegex(PlanningError, "INVALID_ACTION"):
            self.step("import")
        tampered = dict(self.state, importApproved=True)
        with self.assertRaisesRegex(PlanningError, "INVALID_PLANNING_RESULT"):
            validate_state(tampered)

    def test_p2_15_external_id_is_not_worldflow_id(self):
        self.listed([dict(READONLY_GOLDEN["dataset"], datasetId="11111")], True)
        with self.assertRaisesRegex(PlanningError, "DATASET_ID_UNCONFIRMED"):
            self.choose("DATASET", "d_12345_bldg", gridCodes=[AREA])
        self.choose("DATASET", DATASET, gridCodes=[AREA])
        self.assertEqual(DATASET, self.state["pending"]["request"]["input"]["datasetId"])

    def test_unrelated_catalog_dataset_does_not_confirm_the_place(self):
        self.listed([dict(READONLY_GOLDEN["dataset"], datasetId="11111")])
        with self.assertRaisesRegex(PlanningError, "LOCATION_DATASET_MISMATCH"):
            self.choose("DATASET", "11111", gridCodes=[AREA])

    def test_p2_16_response_from_other_project_is_refused(self):
        self.listed(source="LOCAL")
        self.choose("DATASET", DATASET, gridCodes=[AREA])
        self.assertEqual("approved-local", self.state["pending"]["request"]["input"]["sourceReference"])
        response = self.response(None, "BLOCKED", "PROJECT_MISMATCH")
        response["projectFingerprint"] = "2" * 64
        with self.assertRaisesRegex(PlanningError, "PROJECT_MISMATCH"):
            self.step("observe", response=response)
        self.assertIsNone(self.state["coverageReference"])

    def test_p2_17_selecting_one_location_moves_to_dataset(self):
        self.researched()
        question = self.state["nextQuestion"]
        self.choose("LOCATION", "place-a")
        self.assertNotEqual(question, self.state["nextQuestion"])
        self.assertEqual("place-a", self.state["selectedLocationId"])

    def test_p2_18_url_query_is_removed_and_local_path_is_refused(self):
        self.research["evidence"][0]["sourceRef"] += "?private=synthetic#fragment"
        self.researched()
        self.assertEqual("https://example.test/a", self.state["evidence"][0]["sourceRef"])
        self.research["evidence"][0]["sourceRef"] = r"D:\fixture\private"
        with self.assertRaisesRegex(PlanningError, "UNSAFE_SOURCE_REF"):
            self.researched()

    def test_p2_19_dataset_without_sdk_is_blocked(self):
        self.researched()
        self.choose("LOCATION", "place-a")
        self.step("catalog", projectFingerprint=PROJECT, source="REMOTE")
        self.step("observe", response=self.response(None, "BLOCKED", "SDK_NOT_READY"))
        self.assertEqual("BLOCKED", self.state["status"])
        self.assertEqual(2, len(self.state["candidatePlaces"]))

    def test_p2_20_unobserved_extent_is_not_a_plan(self):
        self.inspected()
        with self.assertRaisesRegex(PlanningError, "GRID_OUTSIDE_COVERAGE_REQUEST"):
            self.step("area", gridCodes=[AREA, "533946", "533947"], coordinateReference="EPSG:4326")
        self.assertEqual([], self.state["areaOptions"])

    def test_expiry_at_boundary_and_revision_reuse(self):
        self.inspected()
        event = dict(action="area", revision=self.state["planRevision"], gridCodes=[AREA], coordinateReference=None)
        expired = advance(self.state, event, CLOCK + timedelta(minutes=5))
        self.assertEqual("BLOCKED", expired["status"])
        with self.assertRaisesRegex(PlanningError, "STALE_REVISION"):
            advance(expired, event, CLOCK + timedelta(minutes=5))

    def test_full_plan_ready_and_digest_tampering(self):
        self.add_coordinates(35.5, 139.5)
        self.quality(features=())
        self.choose("QUALITY", "light")
        self.choose("PLAN", digest(self.state))
        self.assertIsNone(self.state["planDigest"])
        self.step("observe", response=self.response(self.preview(self.state["areaChoice"]["plan"]), evidence="AREA_PREVIEW"))
        self.assertEqual("PLAN_READY", self.state["stage"])
        self.assertEqual(digest(self.state), self.state["planDigest"])
        tampered = deepcopy(self.state)
        tampered["candidatePlaces"][0]["reason"] = "編集済み"
        with self.assertRaisesRegex(PlanningError, "PLAN_DIGEST_MISMATCH"):
            validate_state(tampered)

    def test_shared_adopted_plan_fixture(self):
        golden = load_json(FIXTURES / "planning/adopted-plan.json")
        validate_state(golden)
        self.assertEqual(golden["planDigest"], digest(golden))

    def test_rehash_cannot_mask_unrelated_coverage(self):
        golden = load_json(FIXTURES / "planning/adopted-plan.json")
        golden["coverageReference"]["result"]["datasetId"] = "year-independent-id"
        golden["planDigest"] = digest(golden)
        with self.assertRaisesRegex(PlanningError, "DATASET_MISMATCH"):
            validate_state(golden)

    def test_artifact_manifest_cannot_belong_to_another_area(self):
        area_id = self.area_option()
        area = self.state["areaOptions"][0]
        reference = self.state["coverageReference"]
        template = deepcopy(READONLY_GOLDEN["png"]["response"]["result"])
        template.update(planId=area_id, planRevision=area["plan"]["planRevision"],
                        sourceDatasetId=DATASET, createdAt=NOW, crs="EPSG:4326")
        self.step("png", areaId=area_id, width=template["width"], height=template["height"])
        artifact = self.state["pending"]["request"]["requestId"]
        template.update(artifactId=artifact, relativePath=f"Library/PLATEAUWorldFlow/Preview/{artifact}.png")
        response = self.response(template, evidence="PNG_ARTIFACT")
        manifest = dict(template, source=reference["source"] + "_DATASET", sourceReference=reference["sourceReference"],
                        extent=area["preview"]["extent"])
        with self.assertRaisesRegex(PlanningError, "PNG_MANIFEST_MISMATCH"):
            self.step("observe", response=response, manifest=dict(manifest, sourceReference="approved-local"))
        previous = deepcopy(response)
        old_id = READONLY_GOLDEN["png"]["response"]["result"]["artifactId"]
        previous["result"].update(artifactId=old_id, relativePath=f"Library/PLATEAUWorldFlow/Preview/{old_id}.png")
        with self.assertRaisesRegex(PlanningError, "PNG_MANIFEST_MISMATCH"):
            self.step("observe", response=previous, manifest=dict(manifest, **previous["result"]))
        self.step("observe", response=response, manifest=manifest)
        self.assertEqual("ARTIFACT_ONLY", self.state["areaOptions"][0]["png"]["deliveryStatus"])

    def test_transport_timeout_releases_pending_catalog_and_discards_late_response(self):
        self.researched()
        self.choose("LOCATION", "place-a")
        self.step("catalog", projectFingerprint=PROJECT, source="REMOTE")
        old = self.response(None, "UNKNOWN", "NETWORK_UNAVAILABLE")
        self.step("request_failed", requestId=old["requestId"], reason="PHASE1_TIMEOUT")
        self.assertIsNone(self.state["pending"])
        self.step("catalog", projectFingerprint=PROJECT, source="REMOTE")
        with self.assertRaisesRegex(PlanningError, "RESPONSE_MISMATCH"):
            self.step("observe", response=old)

    def test_no_external_instruction_is_accepted_as_an_operation(self):
        self.listed()
        self.choose("DATASET", DATASET, gridCodes=[AREA])
        malicious = deepcopy(self.state)
        malicious["pending"]["request"].update(operation="dependencies.inspect", input={})
        with self.assertRaisesRegex(PlanningError, "RESPONSE_MISMATCH"):
            validate_state(malicious)
