"""Cross-field invariants that Draft 2020-12 cannot express."""

import math


def response_issues(payload):
    result = payload["result"]
    status = payload["status"]
    operation = payload["operation"]
    errors = set()
    if any(item["checkedAt"] > payload["checkedAt"] for item in payload["evidence"]):
        errors.add("readonly:evidence-time")
    if result is None:
        return errors

    def ordered(values, key=None):
        if values is not None:
            keys = [key(item) for item in values] if key else values
            if keys != sorted(set(keys)):
                errors.add("readonly:order")

    def extent(bounds):
        if bounds is not None:
            south, west, north, east = (bounds[key] for key in ("south", "west", "north", "east"))
            if not (south < north and west < east):
                errors.add("readonly:extent")

    def partition(groups, selected=None):
        merged = []
        for group in groups:
            ordered(group)
            merged.extend(group or [])
        if len(merged) != len(set(merged)) or len(merged) > 500:
            errors.add("readonly:partition")
        if selected is not None and sorted(merged) != selected:
            errors.add("readonly:partition")

    if operation == "datasets.list":
        entries = result["datasets"]
        ordered(entries, lambda item: item["datasetId"])
        for item in entries or []:
            ordered(item["featureTypes"])
        valid = {
            "READY": entries is not None and not result["truncated"],
            "PARTIAL": bool(entries),
            "UNKNOWN": entries is None and not result["truncated"],
            "UNAVAILABLE": entries == [] and not result["truncated"],
        }
        if not valid.get(status, False):
            errors.add("readonly:status")
    elif operation == "coverage.inspect":
        available = result["availableGridCodes"]
        missing = result["unavailableGridCodes"]
        unknown = result["unverifiedGridCodes"]
        for cell in available or []:
            extent(cell["extent"])
            ordered(cell["featureTypes"])
            ordered(cell["availableLods"])
        partition([[cell["code"] for cell in available] if available is not None else None, missing, unknown])
        unconfirmed = any(item["code"] == "COVERAGE_UNCONFIRMED" for item in payload["diagnostics"])
        incomplete = unknown or result["truncated"] or available is None or missing is None or unconfirmed
        if status == "READY" and incomplete or status == "UNAVAILABLE" and (incomplete or available or not missing):
            errors.add("readonly:status")
        if status == "UNKNOWN" and (available is not None or missing is not None) or status == "PARTIAL" and (not incomplete or not (available or missing)):
            errors.add("readonly:status")
    elif operation == "areas.plan":
        covered, missing, unknown = (result[key] for key in ("coveredGridCodes", "missingGridCodes", "unverifiedGridCodes"))
        partition([covered, missing, unknown], result["selectedGridCodes"])
        extent(result["extent"])
        if result["extent"] is not None and (not covered or result["extent"]["crs"] is None):
            errors.add("readonly:extent")
        if status == "READY" and (unknown or not covered or result["extent"] is None):
            errors.add("readonly:status")
        if status == "UNAVAILABLE" and (covered or unknown or not missing) or status == "UNKNOWN" and (covered or missing):
            errors.add("readonly:status")
        if status == "PARTIAL" and (not (covered or missing) or not (unknown or result["extent"] is None)):
            errors.add("readonly:status")
        for overlap in result["overlap"] or []:
            ordered(overlap["areaIds"])
            ordered(overlap["gridCodes"])
        areas = [result["estimatedAreaSquareMeters"], *(item["estimatedAreaSquareMeters"] for item in result["overlap"] or [])]
        if any(value is not None and isinstance(value, float) and not math.isfinite(value) for value in areas):
            errors.add("readonly:area")
    elif operation == "areas.preview":
        extent(result["extent"])
        ordered(result["gridCells"], lambda item: item["code"])
        ordered(result["legend"])
        for cell in result["gridCells"]:
            extent(cell["extent"])
        if not {cell["availability"] for cell in result["gridCells"]} <= set(result["legend"]):
            errors.add("readonly:legend")
    elif operation == "areas.preview_png":
        if result["relativePath"] != f"Library/PLATEAUWorldFlow/Preview/{result['artifactId']}.png":
            errors.add("readonly:artifact-path")
        if result["createdAt"] > payload["checkedAt"] or not any(item["kind"] == "PNG_ARTIFACT" for item in payload["evidence"]):
            errors.add("readonly:artifact-evidence")
        if (status == "UNAVAILABLE") != (result["deliveryStatus"] == "UNAVAILABLE"):
            errors.add("readonly:status")
    return errors
