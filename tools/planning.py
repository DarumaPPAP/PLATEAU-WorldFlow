"""Reduce one Phase 2 chat event and emit at most one Phase 1 read-only request."""

import argparse
from copy import deepcopy
from datetime import datetime, timedelta, timezone
from fractions import Fraction
from hashlib import sha256
import json
from pathlib import Path
import sys
from unicodedata import normalize
from uuid import uuid4

from planning_research import PlanningError, normalize_research, public_text, require, research_consistency
from readonly_contract import response_issues
from static_common import ROOT, load_json, parse_json
from validate_contracts import contract_issues, validators

DECISIONS = ("LOCATION", "DATASET", "AREA", "OVERLAP_POLICY", "QUALITY", "PLAN")
SOURCES = {"LOCAL": "approved-local", "REMOTE": "plateau-public"}
OPERATIONS = {"datasets.list", "coverage.inspect", "areas.plan", "areas.preview", "areas.preview_png"}
MAX_WINDOW = timedelta(minutes=5)
AREA_FACTS = {"CRS", "LOD", "FEATURE_TYPES", "FEATURE_LOD_ASSOCIATION", "AREA_SQUARE_METERS", "LOCATION_CONTAINMENT"}
TARGETS = {"POINT", "MUNICIPALITY", "GRID"}
EVENT_FIELDS = {
    "start": set(), "research_start": set(), "cancel": set(), "reset_area": set(),
    "research": {"candidates", "evidence", "publicHosts", "researchStatus"},
    "choose": {"kind", "selectedId", "decisionSource", "gridCodes", "targetKind"},
    "catalog": {"source", "projectFingerprint"},
    "observe": {"response", "manifest"},
    "request_failed": {"requestId", "reason"},
    "area": {"gridCodes", "coordinateReference"},
    "preview": {"areaId"},
    "png": {"areaId", "width", "height"},
    "quality": {"options"},
}
QUESTIONS = {
    "research": "どの場所や作品を調べますか？",
    "location": "どの地点候補を使いますか？",
    "dataset": "どの Dataset を使いますか？",
    "coverage": "照会するメッシュを確認できますか？",
    "area": "どの範囲案を採用しますか？",
    "overlap": "どの分割・重複方針を採用しますか？",
    "quality": "どの品質案を採用しますか？",
    "plan": "確認した内容で読み取り専用計画を採用しますか？",
    "spatial": "地点の根拠を追加するか、確認済みの範囲を選び直しますか？",
    "preview_retry": "採用の再確認に必要な範囲プレビューを再照会しますか？",
    "retry": "観測が失効しました。同じ取得元で再照会しますか？",
}
SCHEMAS = validators(ROOT)


def timestamp(value):
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def canonical_bytes(state):
    """Serialize the approved document with stable keys and without the digest itself."""
    wire = json.dumps({key: value for key, value in state.items() if key != "planDigest"},
                      sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    return normalize("NFC", wire).encode("utf-8")


def digest(state):
    return sha256(canonical_bytes(state)).hexdigest()


def start():
    return dict(schemaVersion=1, planningSessionId=str(uuid4()), planRevision=1, projectFingerprint=None,
                stage="REQUESTED", status="NEEDS_CLARIFICATION", researchStatus=None, candidatePlaces=[],
                evidence=[], decisions=[], selectedLocationId=None, catalog=None, coverageReference=None,
                areaOptions=[], areaChoice=None, qualityOptions=[], qualityChoice=None, pending=None,
                warnings=[], missingFacts=[], nextQuestion=QUESTIONS["research"], planDigest=None, adoptionCheck=None)


def decision(state, kind):
    return next((item for item in state["decisions"] if item["kind"] == kind), None)


def state_at(state, stage, question=None, status="NEEDS_CLARIFICATION"):
    state.update(stage=stage, status=status, nextQuestion=QUESTIONS[question] if question else None)


def invalidate(state, from_kind):
    index = DECISIONS.index(from_kind)
    state["decisions"] = [item for item in state["decisions"] if DECISIONS.index(item["kind"]) < index]
    state.update(planDigest=None, pending=None, adoptionCheck=None)
    if index <= 1:
        state["warnings"] = [item for item in state["warnings"] if item == "RESEARCH_INCOMPLETE"]
    elif index <= 3:
        state["warnings"] = [item for item in state["warnings"]
                             if item not in {"LOCATION_UNVERIFIED", "LOCATION_OUTSIDE",
                                             "PLAN_FACTS_UNCONFIRMED"} and not item.startswith("ADOPTION_PREVIEW_")]
    if index == 0:
        state.update(selectedLocationId=None, projectFingerprint=None, catalog=None)
    if index <= 1:
        state["coverageReference"] = None
        for place in state["candidatePlaces"]:
            if place["datasetAvailability"].startswith("VERIFIED"):
                place["datasetAvailability"] = "CANDIDATE_ONLY"
    if index <= 2:
        state.update(areaOptions=[], areaChoice=None)
        state["missingFacts"] = [item for item in state["missingFacts"] if item not in AREA_FACTS]
    if index <= 3:
        state.update(qualityOptions=[], qualityChoice=None)
        if state["areaChoice"]:
            state["areaChoice"]["overlapPolicy"] = None
    if index == 4:
        state["qualityChoice"] = None
    if index <= 4 and "FEATURE_LOD_ASSOCIATION" in state["missingFacts"]:
        state["missingFacts"].remove("FEATURE_LOD_ASSOCIATION")


def stale(state):
    invalidate(state, "DATASET")
    warn(state, "PLAN_STALE")
    warn(state, "PREVIEW_STALE")
    state_at(state, "LOCATION_SELECTED", "retry", "BLOCKED")


def warn(state, code):
    if code not in state["warnings"]:
        state["warnings"].append(code)


def issue(state, operation, inputs, now, purpose=None):
    require(operation in OPERATIONS and state["pending"] is None, "INVALID_READONLY_REQUEST")
    request = dict(schemaVersion=1, requestId=str(uuid4()), operation=operation,
                   projectFingerprint=state["projectFingerprint"], input=inputs)
    require(not contract_issues(SCHEMAS["readonly-request"], "readonly-request", request), "INVALID_READONLY_REQUEST")
    state["pending"] = dict(request=request, revision=state["planRevision"], issuedAt=now, purpose=purpose)


def research(state, event, now):
    require(state["stage"] == "RESEARCHING", "RESEARCH_NOT_STARTED")
    places, evidence = normalize_research(event, now)
    invalidate(state, "LOCATION")
    state.update(candidatePlaces=places, evidence=evidence, researchStatus=event["researchStatus"], warnings=[])
    if event["researchStatus"] != "COMPLETE":
        warn(state, "RESEARCH_INCOMPLETE")
    state["missingFacts"] = unresolved_location_facts(places, evidence)
    state_at(state, "LOCATION_PROPOSED", "location" if places else "research",
             "UNKNOWN" if not places else ("PARTIAL" if event["researchStatus"] == "PARTIAL" else "NEEDS_CLARIFICATION"))


def unresolved_location_facts(places, evidence):
    return sorted({field for place in places for field, missing in (
        ("COORDINATES", place["coordinates"] is None),
        ("PLACE_EXISTENCE", place["physicalPlaceStatus"] != "VERIFIED"),
        ("WORK_ASSOCIATION", place["workAssociationStatus"] in {"UNVERIFIED", "DISPUTED"} and
         any(item["claimType"] == "WORK_ASSOCIATION" and item["subjectId"] == place["id"] for item in evidence)),
    ) if missing})


def find(items, identifier, key="id"):
    value = next((item for item in items if item[key] == identifier), None)
    require(value is not None, "UNKNOWN_OPTION")
    return value


def location(state):
    return find(state["candidatePlaces"], state["selectedLocationId"])


def check_municipality_identity(state):
    place = location(state)
    require(place["administrativeCode"] is not None and
            any(item["subjectId"] == place["id"] and item["claimType"] == "ADMINISTRATIVE_CODE"
                and item["factStatus"] == "VERIFIED" and item["sourceKind"] in {"OFFICIAL", "MUNICIPAL"}
                for item in state["evidence"]), "MUNICIPALITY_UNCONFIRMED")


def select_dataset(state, identifier, event, now):
    catalog = state["catalog"]
    require(catalog is not None and catalog["result"] is not None, "CATALOG_REQUIRED")
    place = location(state)
    if decision(state, "LOCATION")["targetKind"] == "MUNICIPALITY":
        require(catalog["source"] == "REMOTE", "MUNICIPALITY_DATASET_UNCONFIRMED")
    require(catalog["source"] != "REMOTE" or (len(identifier) == 5 and identifier.isascii() and identifier.isdecimal()),
            "DATASET_ID_UNCONFIRMED")
    require(catalog["source"] != "REMOTE" or place["administrativeCode"] is None or
            place["administrativeCode"] == identifier, "LOCATION_DATASET_MISMATCH")
    datasets = catalog["result"]["datasets"] or []
    if not any(item["datasetId"] == identifier for item in datasets):
        # Phase 1 REMOTE uses the five-digit municipality code. External provider IDs are never mapped here.
        sources = [item for item in state["evidence"] if item["subjectId"] == place["id"]]
        require(catalog["source"] == "REMOTE" and catalog["result"]["truncated"]
                and identifier == place["administrativeCode"] and any(item["claimType"] == "ADMINISTRATIVE_CODE"
                and item["factStatus"] == "VERIFIED" and item["sourceKind"] in {"OFFICIAL", "MUNICIPAL"}
                for item in sources), "DATASET_ID_UNCONFIRMED")
    invalidate(state, "DATASET")
    grids = event["gridCodes"]
    require(isinstance(grids, list) and bool(grids) and len(grids) <= 500, "GRID_CODES_REQUIRED")
    issue(state, "coverage.inspect", dict(source=catalog["source"], sourceReference=catalog["sourceReference"],
                                        datasetId=identifier, gridCodes=sorted(set(grids)), maxResults=500), now)
    state_at(state, "COVERAGE_CHECKING")


def choose(state, event, now):
    require(event["decisionSource"] == "USER_EXPLICIT", "USER_DECISION_REQUIRED")
    kind, identifier = event["kind"], event["selectedId"]
    require(kind == "LOCATION" or "targetKind" not in event, "INVALID_TARGET_KIND")
    attributes = {}
    if kind == "LOCATION":
        place = find(state["candidatePlaces"], identifier)
        require(place["physicalPlaceStatus"] == "VERIFIED", "LOCATION_UNCONFIRMED")
        invalidate(state, "LOCATION")
        state["selectedLocationId"] = identifier
        target = event.get("targetKind", "POINT")
        require(target in TARGETS, "INVALID_TARGET_KIND")
        if target == "MUNICIPALITY":
            check_municipality_identity(state)
        attributes["targetKind"] = target
        state["missingFacts"] = unresolved_location_facts([place], state["evidence"]) if target == "POINT" else []
        state_at(state, "LOCATION_SELECTED", "dataset")
    elif kind == "DATASET":
        select_dataset(state, identifier, event, now)
    elif kind == "AREA":
        option = find(state["areaOptions"], identifier)
        require(option["preview"] and option["plan"]["coveredGridCodes"], "AREA_UNCONFIRMED")
        invalidate(state, "OVERLAP_POLICY")
        if "CRS_UNVERIFIED" in state["warnings"]:
            state["warnings"].remove("CRS_UNVERIFIED")
        state["areaChoice"] = dict(option, overlapPolicy=None)
        state["missingFacts"] = [item for item in state["missingFacts"] if item not in AREA_FACTS]
        missing = []
        if option["spatialFit"] in {"UNVERIFIED", "OUTSIDE"}:
            missing.append("LOCATION_CONTAINMENT")
        if option["preview"]["coordinateReference"] is None:
            warn(state, "CRS_UNVERIFIED")
            missing.append("CRS")
        if option["plan"]["estimatedAreaSquareMeters"] is None:
            missing.append("AREA_SQUARE_METERS")
        lods, features = quality_facts(state)
        if not lods:
            missing.append("LOD")
        if not features:
            missing.append("FEATURE_TYPES")
        state["missingFacts"] = sorted(set(state["missingFacts"] + missing))
        state_at(state, "AREA_SELECTED", "overlap")
    elif kind == "OVERLAP_POLICY":
        require(state["areaChoice"] is not None and identifier in {"SINGLE", "SHARED_DATASET", "SEPARATE"}, "AREA_REQUIRED")
        invalidate(state, "OVERLAP_POLICY")
        state["areaChoice"]["overlapPolicy"] = identifier
        state_at(state, "AREA_SELECTED", "quality")
    elif kind == "QUALITY":
        option = find(state["qualityOptions"], identifier)
        invalidate(state, "QUALITY")
        state["qualityChoice"] = option
        state["missingFacts"] = [item for item in state["missingFacts"] if item != "FEATURE_LOD_ASSOCIATION"]
        if "FEATURE_LOD_ASSOCIATION" in option["unverifiedRequirements"]:
            state["missingFacts"].append("FEATURE_LOD_ASSOCIATION")
            state["missingFacts"].sort()
        question = adoption_question(state)
        state_at(state, "QUALITY_PROPOSED", question, "NEEDS_CLARIFICATION" if question == "plan" else "PARTIAL")
    elif kind == "PLAN":
        require(state["qualityChoice"] is not None, "PLAN_CHANGED")
        require(not state["qualityChoice"]["unverifiedRequirements"], "PLAN_FACTS_UNCONFIRMED")
        choice = state["areaChoice"]
        question = adoption_question(state)
        if question == "spatial":
            warn(state, "LOCATION_" + choice["spatialFit"])
            state_at(state, "QUALITY_PROPOSED", "spatial", "PARTIAL")
            return
        area = choice["plan"]
        if question == "area":
            warn(state, "PLAN_FACTS_UNCONFIRMED")
            state_at(state, "QUALITY_PROPOSED", "area", "PARTIAL")
            return
        state["warnings"] = [item for item in state["warnings"] if not item.startswith("ADOPTION_PREVIEW_")]
        issue(state, "areas.preview", dict(planId=area["planId"], planRevision=area["planRevision"]), now, "ADOPTION")
        # Record adoption only when the Editor has confirmed the selected plan is still live.
        return
    else:
        raise PlanningError("INVALID_DECISION")
    index = DECISIONS.index(kind)
    state["decisions"] = [item for item in state["decisions"] if DECISIONS.index(item["kind"]) < index]
    state["decisions"].append(dict(kind=kind, selectedId=identifier, revision=state["planRevision"],
                                   decisionSource="USER_EXPLICIT", decidedAt=now, **attributes))


def adoption_question(state):
    area = state["areaChoice"]
    if state["qualityChoice"]["unverifiedRequirements"]:
        return "quality"
    if area["spatialFit"] not in {"VERIFIED", "NOT_APPLICABLE"}:
        return "spatial"
    if area["plan"]["missingGridCodes"] or area["plan"]["unverifiedGridCodes"] or area["previewStatus"] != "READY":
        return "area"
    return "plan"


def compare_area(state, event, now):
    coverage = state["coverageReference"]
    require(coverage is not None and coverage["result"] is not None, "COVERAGE_REQUIRED")
    require(state["areaChoice"] is None, "RESET_AREA_BEFORE_COMPARISON")
    grids = sorted(set(event["gridCodes"]))
    require(bool(grids) and set(grids) <= set(coverage["requestedGrids"]), "GRID_OUTSIDE_COVERAGE_REQUEST")
    require(len(state["areaOptions"]) < 12, "AREA_OPTION_LIMIT")
    issue(state, "areas.plan", dict(datasetId=coverage["datasetId"], selectedGridCodes=grids,
                                   coverageSnapshotId=coverage["coverageSnapshotId"],
                                   coordinateReference=event["coordinateReference"],
                                   planRevision=state["planRevision"]), now)


def quality_facts(state):
    area = state["areaChoice"]["plan"]
    covered = set(area["coveredGridCodes"])
    cells = [item for item in state["coverageReference"]["result"]["availableGridCodes"] if item["code"] in covered]
    require(len(cells) == len(covered) and bool(cells), "COVERAGE_MISMATCH")
    verified_lods = sorted(set.intersection(*(set(item["availableLods"] or []) for item in cells)))
    verified_features = sorted(set.intersection(*(set(item["featureTypes"] or []) for item in cells)))
    return verified_lods, verified_features


def unverified_requirements(option, lods, features):
    return ((["LOD"] if not set(option["requestedLods"]) <= set(lods) else [])
            + (["FEATURE_TYPE"] if not set(option["requestedFeatureTypes"]) <= set(features) else [])
            + (["FEATURE_LOD_ASSOCIATION"] if option["requestedLods"] and option["requestedFeatureTypes"] else []))


def observed_presets(lods):
    if not lods:
        # Without a verified LOD there is no observed value to assign to a preset.
        return []
    ranks = [("LIGHT", lods[0])]
    if len(lods) > 2:
        ranks.append(("BALANCED", lods[len(lods) // 2]))
    if len(lods) > 1:
        ranks.append(("HIGH", lods[-1]))
    return [dict(id="preset-" + preset.lower(), preset=preset, requestedFeatureTypes=[], requestedLods=[lod],
                 reason=f"選択メッシュで確認できた LOD {lod} を指定") for preset, lod in ranks]


def quality_options(state, event):
    require(decision(state, "OVERLAP_POLICY") is not None, "AREA_REQUIRED")
    verified_lods, verified_features = quality_facts(state)
    invalidate(state, "QUALITY")
    options = []
    supplied = event.get("options", [])
    require(all(item["preset"] == "CUSTOM" for item in supplied), "PRESET_REQUIRES_OBSERVATION")
    for item in [*supplied, *observed_presets(verified_lods)]:
        require(set(item) == {"id", "preset", "requestedFeatureTypes", "requestedLods", "reason"}, "INVALID_QUALITY_FIELDS")
        missing = unverified_requirements(item, verified_lods, verified_features)
        options.append(dict(item, reason=public_text(item["reason"]), verifiedLods=verified_lods,
                            verifiedFeatureTypes=verified_features, unverifiedRequirements=missing,
                            estimates=dict(diskBytes=None, memoryBytes=None, durationSeconds=None)))
    require(bool(options) and len({item["id"] for item in options}) == len(options), "INVALID_QUALITY_OPTIONS")
    state["qualityOptions"] = options
    state_at(state, "QUALITY_PROPOSED", "quality")


def check_preview(area, preview, coverage):
    plan = area["plan"]
    expected = {code: availability for key, availability in (("coveredGridCodes", "AVAILABLE"),
                ("missingGridCodes", "UNAVAILABLE"), ("unverifiedGridCodes", "UNVERIFIED")) for code in plan[key]}
    observed = {cell["code"]: cell["availability"] for cell in preview["gridCells"] if cell["selected"]}
    cells = {cell["code"]: cell["extent"] for cell in coverage["result"]["availableGridCodes"] or []}
    require(expected == observed and
            all(cell["extent"] == cells.get(cell["code"]) and
                (cell["selected"] or (cell["code"] in cells and cell["availability"] == "AVAILABLE"))
                for cell in preview["gridCells"]),
            "PREVIEW_MISMATCH")
    extent = preview["extent"]
    if extent is not None:
        require(preview["coordinateReference"] == extent["crs"] and
                all(extent["south"] <= bound["south"] < bound["north"] <= extent["north"] and
                    extent["west"] <= bound["west"] < bound["east"] <= extent["east"] and extent["crs"] == bound["crs"]
                    for cell in preview["gridCells"] if (bound := cell["extent"]) is not None), "PREVIEW_MISMATCH")
    else:
        require(preview["coordinateReference"] is None, "PREVIEW_MISMATCH")


def check_png(area, result, manifest, coverage):
    require(not contract_issues(SCHEMAS["preview-manifest"], "preview-manifest", manifest), "INVALID_MANIFEST")
    require(all(manifest.get(key) == value for key, value in result.items()) and
            result["sourceDatasetId"] == coverage["datasetId"] and result["planId"] == area["plan"]["planId"]
            and result["planRevision"] == area["plan"]["planRevision"] and manifest["source"] == coverage["source"] + "_DATASET"
            and manifest["sourceReference"] == coverage["sourceReference"] and manifest["extent"] == area["preview"]["extent"],
            "PNG_MANIFEST_MISMATCH")
    require(result["relativePath"] == f"Library/PLATEAUWorldFlow/Preview/{result['artifactId']}.png"
            and result["crs"] == area["preview"]["coordinateReference"], "PNG_MANIFEST_MISMATCH")


def observe(state, event, now):
    pending = state["pending"]
    require(pending is not None, "NO_PENDING_REQUEST")
    request = pending["request"]
    response = event["response"]
    require(not contract_issues(SCHEMAS["readonly-response"], "readonly-response", response), "INVALID_PHASE1_RESPONSE")
    require(all(response[key] == request[key] for key in ("requestId", "operation")), "RESPONSE_MISMATCH")
    require(response["projectFingerprint"] == request["projectFingerprint"], "PROJECT_MISMATCH")
    require(pending["issuedAt"] <= response["checkedAt"] <= now, "STALE_RESPONSE")
    operation, inputs, result = request["operation"], request["input"], response["result"]
    state["pending"] = None
    diagnostics = {item["code"] for item in response["diagnostics"]}
    if "PLAN_STALE" in diagnostics or "PROJECT_MISMATCH" in diagnostics:
        stale(state)
        for code in sorted(diagnostics):
            warn(state, code)
        return
    status = response["status"]
    if pending["purpose"] == "ADOPTION" and status != "READY":
        warn(state, "ADOPTION_PREVIEW_" + status)
        for code in sorted(diagnostics):
            warn(state, "ADOPTION_PREVIEW_" + code)
        state_at(state, "QUALITY_PROPOSED", "preview_retry", readonly_status(status))
        return
    for code in sorted(diagnostics):
        warn(state, code)
    if operation == "datasets.list":
        require(result is None or result["source"] == inputs["source"], "SOURCE_MISMATCH")
        state["catalog"] = dict(source=inputs["source"], sourceReference=inputs["sourceReference"],
                                requestId=request["requestId"], checkedAt=response["checkedAt"], status=status, result=result)
        state_at(state, "LOCATION_SELECTED", "dataset", readonly_status(status))
    elif operation == "coverage.inspect":
        observe_coverage(state, request, response, pending["issuedAt"], now)
    elif status not in {"READY", "PARTIAL"} or result is None:
        state_at(state, state["stage"], "area" if not state["areaChoice"] else "plan", readonly_status(status))
    elif operation == "areas.plan":
        observe_area(state, request, response)
    elif operation == "areas.preview":
        area = find(state["areaOptions"], inputs["planId"])
        check_preview(area, result, state["coverageReference"])
        require(result["previewId"] == request["requestId"], "PREVIEW_MISMATCH")
        if pending["purpose"] == "ADOPTION":
            finish_adoption(state, pending, response)
        else:
            area["preview"] = result
            area["previewStatus"] = status
            area["png"] = None
            state_at(state, "AREA_PROPOSED", "area", readonly_status(status))
    elif operation == "areas.preview_png":
        area = find(state["areaOptions"], pending["purpose"])
        require(result["artifactId"] == request["requestId"] and
                result["width"] == inputs["width"] and result["height"] == inputs["height"], "PNG_MANIFEST_MISMATCH")
        check_png(area, result, event["manifest"], state["coverageReference"])
        area["png"] = event["manifest"]
        state_at(state, "AREA_PROPOSED", "area")


def readonly_status(status):
    return {"READY": "NEEDS_CLARIFICATION", "PARTIAL": "PARTIAL",
            "BLOCKED": "BLOCKED", "FAILED": "BLOCKED"}.get(status, "UNKNOWN")


def covers_latitudes(cells, south, north):
    """Cover a closed latitude interval with south-inclusive, north-exclusive cells."""
    covered_until = south
    for low, high in sorted((cell["south"], cell["north"]) for cell in cells):
        if low > covered_until:
            # Later intervals start even farther north, so the gap cannot be covered.
            return False
        covered_until = max(covered_until, high)
        if covered_until > north:
            return True
    return False


def covers_uncertainty_box(cells, south, north, west, east):
    """Check each longitude strip and the eastern endpoint without bridging gaps."""
    edges = sorted({west, east} | {cell[key] for cell in cells for key in ("west", "east") if west < cell[key] < east})
    for left, right in zip(edges, edges[1:]):
        spanning = (cell for cell in cells if cell["west"] <= left and right <= cell["east"])
        if not covers_latitudes(spanning, south, north):
            return False
    return covers_latitudes((cell for cell in cells if cell["west"] <= east < cell["east"]), south, north)


def place_in_confirmed_cells(place, extents):
    """Compare the sourced uncertainty box against the union of cells in the same CRS."""
    coordinates = place["coordinates"]
    if coordinates is None or coordinates.get("accuracyDegrees") is None:
        # The digit count and the public description alone do not bound positional error.
        return "UNVERIFIED"
    # Keep a positive uncertainty from disappearing when rounded against a cell edge.
    lat, lon, error = (Fraction(coordinates[key]) for key in ("latitude", "longitude", "accuracyDegrees"))
    south, north, west, east = lat - error, lat + error, lon - error, lon + error
    if not (-90 < south <= north < 90 and -180 < west <= east < 180):
        # The verification does not handle touching or crossing a pole or the antimeridian.
        return "UNVERIFIED"
    comparable = [bound for bound in extents if bound and bound["crs"] == coordinates["crs"]
                  and bound["south"] < bound["north"] and bound["west"] < bound["east"]]
    if covers_uncertainty_box(comparable, south, north, west, east):
        return "VERIFIED"
    if comparable and len(comparable) == len(extents) and all(
            north < bound["south"] or south >= bound["north"] or east < bound["west"] or west >= bound["east"]
            for bound in comparable):
        return "OUTSIDE"
    return "UNVERIFIED"


def area_spatial_fit(state, plan):
    if decision(state, "LOCATION")["targetKind"] != "POINT":
        # The explicitly selected subject is an administrative area or a set of grid identifiers.
        return "NOT_APPLICABLE"
    cells = {item["code"]: item["extent"] for item in state["coverageReference"]["result"]["availableGridCodes"] or []}
    return place_in_confirmed_cells(location(state), [cells.get(code) for code in plan["selectedGridCodes"]])


def observe_coverage(state, request, response, issued_at, now):
    inputs, result, checked = request["input"], response["result"], response["checkedAt"]
    require(result is None or result["datasetId"] == inputs["datasetId"], "DATASET_MISMATCH")
    # An observation may predate response serialization. Conservatively start the window at request issuance.
    expires = timestamp(datetime.fromisoformat(issued_at) + MAX_WINDOW)
    state["coverageReference"] = dict(**{key: inputs[key] for key in ("source", "sourceReference", "datasetId")},
                                      requestedGrids=inputs["gridCodes"], coverageSnapshotId=request["requestId"],
                                      locationId=state["selectedLocationId"], status=response["status"],
                                      issuedAt=issued_at, checkedAt=checked, expiresAt=expires,
                                      diagnostics=response["diagnostics"], result=result)
    check_coverage(state["coverageReference"])
    place = location(state)
    verified = verified_location_availability(place, state["coverageReference"])
    if verified:
        place["datasetAvailability"] = verified
    can_plan = bool(result and (result["availableGridCodes"] or result["unavailableGridCodes"]))
    state_at(state, "AREA_PROPOSED" if can_plan else "LOCATION_SELECTED",
             "area" if can_plan else "coverage", readonly_status(response["status"]))
    if now >= expires:
        stale(state)


def coverage_groups(result):
    return (
        {cell["code"] for cell in result["availableGridCodes"] or []},
        set(result["unavailableGridCodes"] or []),
        set(result["unverifiedGridCodes"]),
    )


def verified_location_availability(place, reference):
    cells = (reference["result"]["availableGridCodes"] or []) if reference and reference["result"] else []
    if place_in_confirmed_cells(place, [cell["extent"] for cell in cells]) == "VERIFIED":
        complete = reference["status"] == "READY" and {cell["code"] for cell in cells} == set(reference["requestedGrids"])
        return "VERIFIED_AVAILABLE" if complete else "VERIFIED_PARTIAL"
    return None


def check_coverage(reference):
    result = reference["result"]
    maximum = timestamp(datetime.fromisoformat(reference["issuedAt"]) + MAX_WINDOW)
    require(reference["issuedAt"] <= reference["checkedAt"] and reference["expiresAt"] == maximum, "COVERAGE_EXPIRY_MISMATCH")
    require(reference["status"] not in {"READY", "PARTIAL", "UNAVAILABLE"} or result is not None, "COVERAGE_STATUS_MISMATCH")
    if result is not None:
        require(reference["status"] not in {"FAILED", "BLOCKED"}, "COVERAGE_STATUS_MISMATCH")
        require(result["datasetId"] == reference["datasetId"], "DATASET_MISMATCH")
        require(not response_issues(dict(operation="coverage.inspect", result=result, status=reference["status"],
                                         checkedAt=reference["checkedAt"], diagnostics=reference["diagnostics"], evidence=[])),
                "COVERAGE_STATUS_MISMATCH")
        groups = coverage_groups(result)
        classified = set.union(*groups)
        require(len(classified) == sum(map(len, groups)) and classified <= set(reference["requestedGrids"]),
                "COVERAGE_GRID_MISMATCH")


def check_area(plan, reference):
    require(reference is not None and reference["result"] is not None, "COVERAGE_REQUIRED")
    available, missing, _ = coverage_groups(reference["result"])
    selected = set(plan["selectedGridCodes"])
    require(plan["coverageSnapshotId"] == reference["coverageSnapshotId"] and
            selected <= set(reference["requestedGrids"]) and
            selected & available == set(plan["coveredGridCodes"]) and
            selected & missing == set(plan["missingGridCodes"]) and
            selected - available - missing == set(plan["unverifiedGridCodes"]), "AREA_PLAN_MISMATCH")


def observe_area(state, request, response):
    inputs, plan = request["input"], response["result"]
    check_area(plan, state["coverageReference"])
    require(plan["planId"] == request["requestId"] and plan["selectedGridCodes"] == inputs["selectedGridCodes"]
            and all(plan[key] == inputs[key] for key in ("planRevision", "coverageSnapshotId")), "AREA_PLAN_MISMATCH")
    state["areaOptions"].append(dict(id=plan["planId"], plan=plan, coordinateReference=inputs["coordinateReference"],
                                     status=response["status"], preview=None, previewStatus=None, png=None,
                                     spatialFit=area_spatial_fit(state, plan)))
    state_at(state, "AREA_PROPOSED", "area", readonly_status(response["status"]))


def finish_adoption(state, pending, response):
    area, quality = state["areaChoice"], state["qualityChoice"]
    plan, preview = area["plan"], response["result"]
    request, checked_at, decided_at = pending["request"], response["checkedAt"], pending["issuedAt"]
    require(area["preview"] is not None and quality is not None, "PLAN_CHANGED")
    require(response["status"] == "READY" and not response["diagnostics"] and request["operation"] == "areas.preview"
            and request["input"] == dict(planId=plan["planId"], planRevision=plan["planRevision"])
            and response["projectFingerprint"] == state["projectFingerprint"]
            and state["coverageReference"]["checkedAt"] <= decided_at <= checked_at < state["coverageReference"]["expiresAt"],
            "ADOPTION_PREVIEW_UNCONFIRMED")
    require(area["previewStatus"] == "READY" and area["spatialFit"] in {"VERIFIED", "NOT_APPLICABLE"}
            and not plan["unverifiedGridCodes"] and not plan["missingGridCodes"]
            and not quality["unverifiedRequirements"], "PLAN_FACTS_UNCONFIRMED")
    require(all(preview[key] == value for key, value in area["preview"].items() if key != "previewId"), "PREVIEW_MISMATCH")
    state["adoptionCheck"] = dict(checkedAt=checked_at, requestId=preview["previewId"], phase1AreaPlanId=area["id"],
                                  phase1PlanRevision=plan["planRevision"], projectFingerprint=state["projectFingerprint"],
                                  coverageSnapshotId=plan["coverageSnapshotId"], status=response["status"])
    # The probe result is write-free and the artifact reference still describes the preview selected by the user.
    state["decisions"].append(dict(kind="PLAN", selectedId=area["id"],
                                   revision=state["planRevision"] - 1, decisionSource="USER_EXPLICIT", decidedAt=decided_at))
    state_at(state, "PLAN_READY", status="READY")


def request_failed(state, event):
    pending = state["pending"]
    require(pending is not None and event["requestId"] == pending["request"]["requestId"], "RESPONSE_MISMATCH")
    require(event["reason"] in {"PHASE1_TIMEOUT", "PHASE1_TRANSPORT_UNAVAILABLE"}, "INVALID_TRANSPORT_FAILURE")
    operation = pending["request"]["operation"]
    state["pending"] = None
    warn(state, event["reason"])
    if operation in {"datasets.list", "coverage.inspect"}:
        state_at(state, "LOCATION_SELECTED", "dataset" if operation == "datasets.list" else "coverage", "UNKNOWN")
    else:
        state_at(state, state["stage"], "plan" if pending["purpose"] == "ADOPTION" else "area", "UNKNOWN")


def advance(previous, event, clock=None):
    """Process a revision-bound event from the chat client and validate the resulting record."""
    now = timestamp(clock or datetime.now(timezone.utc))
    action = event["action"]
    require(action in EVENT_FIELDS, "INVALID_ACTION")
    require(set(event) <= EVENT_FIELDS[action] | {"action", "revision"}, "INVALID_EVENT_FIELDS")
    if previous is None:
        require(action == "start", "SESSION_REQUIRED")
        return start()
    validate_state(previous)
    require(type(event["revision"]) is int and event["revision"] == previous["planRevision"], "STALE_REVISION")
    state = deepcopy(previous)
    state["planRevision"] += 1
    coverage = state["coverageReference"]
    replacing_location = action == "choose" and event["kind"] == "LOCATION"
    if coverage is not None and now >= coverage["expiresAt"] and action not in {"cancel", "catalog", "research_start"} and not replacing_location:
        stale(state)
        return state
    if action == "cancel":
        invalidate(state, "LOCATION")
        state_at(state, "REQUESTED", status="CANCELLED")
    elif action == "research_start":
        invalidate(state, "LOCATION")
        state.update(candidatePlaces=[], evidence=[], researchStatus=None, missingFacts=[], warnings=[])
        state_at(state, "RESEARCHING")
    elif action == "research":
        research(state, event, now)
    elif action == "observe":
        observe(state, event, now)
    elif action == "request_failed":
        request_failed(state, event)
    else:
        require(state["pending"] is None or replacing_location, "PENDING_PHASE1_RESPONSE")
        require(state["status"] not in {"READY", "CANCELLED"} or replacing_location or action == "catalog",
                "SESSION_CLOSED")
        if action == "choose":
            # The proposal digest refers to the version displayed before this event.
            if event["kind"] == "PLAN":
                require(event["selectedId"] == digest(previous), "PLAN_CHANGED")
            choose(state, event, now)
        elif action == "catalog":
            require(state["selectedLocationId"] is not None, "LOCATION_REQUIRED")
            invalidate(state, "DATASET")
            state.update(catalog=None, projectFingerprint=event["projectFingerprint"])
            source = event["source"]
            require(source in SOURCES, "SOURCE_NOT_ALLOWED")
            issue(state, "datasets.list", dict(source=source, sourceReference=SOURCES[source], maxResults=20), now)
            state_at(state, "COVERAGE_CHECKING")
        elif action == "area":
            compare_area(state, event, now)
        elif action == "preview":
            require(state["areaChoice"] is None, "AREA_ALREADY_SELECTED")
            area = find(state["areaOptions"], event["areaId"])
            issue(state, "areas.preview", dict(planId=area["id"], planRevision=area["plan"]["planRevision"]), now)
        elif action == "png":
            require(state["areaChoice"] is None, "AREA_ALREADY_SELECTED")
            area = find(state["areaOptions"], event["areaId"])
            require(area["preview"] is not None, "PREVIEW_REQUIRED")
            issue(state, "areas.preview_png", dict(previewId=area["preview"]["previewId"],
                                                 width=event["width"], height=event["height"]), now, area["id"])
        elif action == "quality":
            quality_options(state, event)
        elif action == "reset_area":
            require(coverage is not None and state["areaChoice"] is not None, "AREA_REQUIRED")
            invalidate(state, "OVERLAP_POLICY")
            state["areaChoice"] = None
            if "CRS_UNVERIFIED" in state["warnings"]:
                state["warnings"].remove("CRS_UNVERIFIED")
            state["missingFacts"] = [item for item in state["missingFacts"] if item not in AREA_FACTS]
            state["decisions"] = [item for item in state["decisions"] if item["kind"] != "AREA"]
            state_at(state, "AREA_PROPOSED", "area")
        else:
            raise PlanningError("INVALID_ACTION")
    if state["status"] == "READY":
        state["planDigest"] = digest(state)
    validate_state(state)
    return state


def validate_state(state):
    """Verify the exchange document and the cross-field references used by the reducer."""
    require(not contract_issues(SCHEMAS["planning-result"], "planning-result", state), "INVALID_PLANNING_RESULT")
    research_consistency(state["candidatePlaces"], state["evidence"])
    decisions = state["decisions"]
    kinds = [item["kind"] for item in decisions]
    revisions = [item["revision"] for item in decisions]
    require(kinds == list(DECISIONS[:len(kinds)]) and revisions == sorted(set(revisions))
            and all(value <= state["planRevision"] for value in revisions), "INVALID_DECISION_HISTORY")
    require([item["decidedAt"] for item in decisions] == sorted(item["decidedAt"] for item in decisions),
            "INVALID_DECISION_HISTORY")
    require(state["selectedLocationId"] == (decisions[0]["selectedId"] if decisions else None), "LOCATION_MISMATCH")
    if decisions:
        require(location(state)["physicalPlaceStatus"] == "VERIFIED", "LOCATION_UNCONFIRMED")
        if decisions[0]["targetKind"] == "MUNICIPALITY":
            check_municipality_identity(state)
    catalog = state["catalog"]
    if catalog:
        require(bool(decisions) and state["projectFingerprint"] is not None
                and catalog["sourceReference"] == SOURCES[catalog["source"]]
                and (catalog["result"] is None or catalog["source"] == catalog["result"]["source"]),
                "SOURCE_MISMATCH")
    coverage = state["coverageReference"]
    if coverage:
        dataset = decision(state, "DATASET")
        check_coverage(coverage)
        require(dataset is not None and coverage["datasetId"] == dataset["selectedId"]
                and coverage["locationId"] == state["selectedLocationId"]
                and state["catalog"] is not None
                and all(coverage[key] == state["catalog"][key] for key in ("source", "sourceReference"))
                and state["catalog"]["checkedAt"] <= coverage["checkedAt"] < coverage["expiresAt"],
                "COVERAGE_MISMATCH")
        if decisions[0]["targetKind"] == "MUNICIPALITY":
            require(coverage["source"] == "REMOTE" and coverage["datasetId"] == location(state)["administrativeCode"],
                    "MUNICIPALITY_DATASET_UNCONFIRMED")
    for place in state["candidatePlaces"]:
        expected = verified_location_availability(place, coverage) if place["id"] == state["selectedLocationId"] else None
        observed = place["datasetAvailability"]
        require(observed == expected if expected else observed in {"UNKNOWN", "CANDIDATE_ONLY"},
                "LOCATION_AVAILABILITY_MISMATCH")
    if state["pending"]:
        pending = state["pending"]
        require(pending["revision"] == state["planRevision"]
                and pending["request"]["projectFingerprint"] == state["projectFingerprint"]
                and pending["request"]["operation"] in OPERATIONS and bool(state["selectedLocationId"]), "RESPONSE_MISMATCH")
        if pending["purpose"] == "ADOPTION":
            choice = state["areaChoice"]
            require(choice is not None and state["qualityChoice"] is not None
                    and pending["request"]["operation"] == "areas.preview"
                    and pending["request"]["input"] == dict(planId=choice["id"], planRevision=choice["plan"]["planRevision"]),
                    "RESPONSE_MISMATCH")
    area = state["areaChoice"]
    area_decision = decision(state, "AREA")
    require(bool(area) == bool(area_decision), "AREA_PLAN_MISMATCH")
    if area:
        option = find(state["areaOptions"], area_decision["selectedId"])
        require(all(area[key] == value for key, value in option.items()), "AREA_PLAN_MISMATCH")
        require(("LOCATION_CONTAINMENT" in state["missingFacts"]) == (area["spatialFit"] in {"UNVERIFIED", "OUTSIDE"}),
                "SPATIAL_FIT_MISMATCH")
        overlap = decision(state, "OVERLAP_POLICY")
        require(area["overlapPolicy"] == (overlap["selectedId"] if overlap else None), "AREA_PLAN_MISMATCH")
    for option in state["areaOptions"]:
        check_area(option["plan"], coverage)
        require(option["id"] == option["plan"]["planId"] and
                option["plan"]["planRevision"] <= state["planRevision"], "AREA_PLAN_MISMATCH")
        require(option["spatialFit"] == area_spatial_fit(state, option["plan"]), "SPATIAL_FIT_MISMATCH")
        require(bool(option["preview"]) == bool(option["previewStatus"]), "PREVIEW_MISMATCH")
        if option["preview"]:
            check_preview(option, option["preview"], coverage)
        if option["png"]:
            require(option["preview"] is not None, "PREVIEW_REQUIRED")
            check_png(option, option["png"], option["png"], coverage)
    if state["qualityOptions"]:
        require(decision(state, "OVERLAP_POLICY") is not None, "AREA_REQUIRED")
        lods, features = quality_facts(state)
        presets = {item["id"]: item for item in observed_presets(lods)}
        for option in state["qualityOptions"]:
            require(option["verifiedLods"] == lods and option["verifiedFeatureTypes"] == features
                    and option["unverifiedRequirements"] == unverified_requirements(option, lods, features)
                    and option["reason"] == public_text(option["reason"]), "QUALITY_MISMATCH")
            if option["preset"] != "CUSTOM":
                require(option["id"] in presets and all(option[key] == value for key, value in presets[option["id"]].items()),
                        "QUALITY_MISMATCH")
    quality = decision(state, "QUALITY")
    require(state["qualityChoice"] == (find(state["qualityOptions"], quality["selectedId"]) if quality else None),
            "QUALITY_MISMATCH")
    require(("FEATURE_LOD_ASSOCIATION" in state["missingFacts"]) ==
            bool(quality and "FEATURE_LOD_ASSOCIATION" in state["qualityChoice"]["unverifiedRequirements"]), "QUALITY_MISMATCH")
    if state["status"] == "READY":
        check = state["adoptionCheck"]
        used_ids = {catalog["requestId"], coverage["coverageSnapshotId"]} if catalog and coverage else set()
        for option in state["areaOptions"]:
            used_ids.add(option["id"])
            if option["preview"]:
                used_ids.add(option["preview"]["previewId"])
            if option["png"]:
                used_ids.add(option["png"]["artifactId"])
        require(state["stage"] == "PLAN_READY" and kinds == list(DECISIONS) and state["pending"] is None
                and decisions[-1]["selectedId"] == area["id"] and state["planDigest"] == digest(state)
                and not area["plan"]["missingGridCodes"] and not area["plan"]["unverifiedGridCodes"]
                and area["previewStatus"] == "READY" and area["spatialFit"] in {"VERIFIED", "NOT_APPLICABLE"}
                and not state["qualityChoice"]["unverifiedRequirements"]
                and check is not None and check["phase1AreaPlanId"] == area["id"]
                and check["phase1PlanRevision"] == area["plan"]["planRevision"]
                and check["coverageSnapshotId"] == coverage["coverageSnapshotId"]
                and check["projectFingerprint"] == state["projectFingerprint"] and check["status"] == "READY"
                and check["requestId"] not in used_ids
                and coverage["checkedAt"] <= decisions[-1]["decidedAt"] <= check["checkedAt"] < coverage["expiresAt"]
                and state["nextQuestion"] is None, "PLAN_DIGEST_MISMATCH")
    else:
        require(state["planDigest"] is None and state["adoptionCheck"] is None and
                "PLAN" not in kinds and state["stage"] != "PLAN_READY", "PLAN_DIGEST_MISMATCH")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path)
    parser.add_argument("--proposal-digest", action="store_true")
    parser.add_argument("--validate", action="store_true")
    args = parser.parse_args()
    try:
        state = load_json(args.state) if args.state else None
        if args.proposal_digest or args.validate:
            validate_state(state)
            print(digest(state) if args.proposal_digest else "PASS")
        else:
            raw = sys.stdin.buffer.read(2_097_153)
            require(len(raw) <= 2_097_152, "EVENT_TOO_LARGE")
            result = advance(state, parse_json(raw.decode("utf-8")))
            sys.stdout.buffer.write((json.dumps(result, ensure_ascii=False, allow_nan=False, indent=2) + "\n").encode("utf-8"))
    except PlanningError as error:
        print(str(error), file=sys.stderr)
        return 1
    except (OSError, ValueError, TypeError, KeyError, AttributeError, RecursionError):
        print("INVALID_PLANNING_INPUT", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
