"""Normalize only provenance and assertions selected by the AI client from research."""

from datetime import datetime
import re
import unicodedata
from urllib.parse import urlsplit

ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", re.ASCII)
SECRET = re.compile(r"(?:gh[pousr]_|github_pat_|sk-(?:proj-)?|AKIA|Bearer\s|api[_ -]?key\s*[:=]|token\s*[:=])", re.I)
PRIVATE = re.compile(r"(?:[a-z]:[\\/]|\\\\|(?:^|\s)/(?:Users|home|private|tmp|mnt|var)/)", re.I)
PUBLIC_PATH = re.compile(r"/[A-Za-z0-9_./~-]*", re.ASCII)
PRIMARY = {"OFFICIAL", "PUBLISHER", "MUNICIPAL"}


def require(condition, code):
    if not condition:
        raise PlanningError(code)


class PlanningError(ValueError):
    """An event was refused without changing the input snapshot."""


def public_text(value):
    require(isinstance(value, str), "INVALID_PUBLIC_TEXT")
    value = unicodedata.normalize("NFC", value).strip()
    require(0 < len(value) <= 280 and not SECRET.search(value) and not PRIVATE.search(value) and "://" not in value
            and not any(unicodedata.category(c) in {"Cc", "Cf", "Cs"} for c in value), "INVALID_PUBLIC_TEXT")
    return value


def source_ref(value, hosts):
    if ID.fullmatch(value) and not SECRET.search(value):
        return value
    parsed = urlsplit(value)
    require(parsed.scheme == "https" and parsed.netloc in hosts and
            (not parsed.path or PUBLIC_PATH.fullmatch(parsed.path)) and
            not SECRET.search(parsed.path) and ".." not in parsed.path.split("/"), "UNSAFE_SOURCE_REF")
    # Query and fragment can carry access tokens and are not part of the public citation.
    return f"https://{parsed.netloc}{parsed.path}"


def normalize_research(event, now):
    """Accept assertions only, with no field for an external body or tool call."""
    require(event["researchStatus"] in {"COMPLETE", "PARTIAL", "UNKNOWN"}, "INVALID_RESEARCH_STATUS")
    require(event["researchStatus"] != "UNKNOWN" or not event["candidates"], "INVALID_RESEARCH_STATUS")
    sources, places = [], []
    for source in event["evidence"]:
        require(set(source) == {"id", "subjectId", "claimType", "sourceKind", "providerId", "sourceRef", "observedAt", "factStatus"},
                "INVALID_EVIDENCE_FIELDS")
        require(source["observedAt"] <= now and datetime.fromisoformat(source["observedAt"]).utcoffset() is not None,
                "EVIDENCE_IN_FUTURE")
        sources.append(dict(source, sourceRef=source_ref(source["sourceRef"], event["publicHosts"])))
    for candidate in event["candidates"]:
        require(set(candidate) == {"id", "name", "locality", "administrativeCode", "coordinates",
                                   "physicalPlaceStatus", "workAssociationStatus", "evidenceIds", "reason", "datasetAvailability"},
                "INVALID_CANDIDATE_FIELDS")
        require(candidate["datasetAvailability"] in {"UNKNOWN", "CANDIDATE_ONLY"}, "COVERAGE_MUST_USE_PHASE1")
        place = candidate.copy()
        for field in ("name", "locality", "reason"):
            if place[field] is not None:
                place[field] = public_text(place[field])
        if place["coordinates"] is not None:
            place["coordinates"] = dict(place["coordinates"], precision=public_text(place["coordinates"]["precision"]))
        places.append(place)
    research_consistency(places, sources)
    return places, sources


def research_consistency(places, sources):
    by_id = {item["id"]: item for item in sources}
    place_ids = {item["id"] for item in places}
    require(len(by_id) == len(sources) and len(place_ids) == len(places), "DUPLICATE_RESEARCH_ID")
    require(all(item["subjectId"] in place_ids for item in sources), "EVIDENCE_SUBJECT_MISMATCH")
    for place in places:
        for field in ("name", "locality", "reason"):
            if place[field] is not None:
                require(place[field] == public_text(place[field]), "INVALID_PUBLIC_TEXT")
        attached = {item["id"]: item for item in sources if item["subjectId"] == place["id"]}
        require(set(place["evidenceIds"]) == set(attached), "EVIDENCE_SUBJECT_MISMATCH")
        for field, claim in (("physicalPlaceStatus", "PLACE_EXISTENCE"), ("workAssociationStatus", "WORK_ASSOCIATION")):
            items = [item for item in attached.values() if item["claimType"] == claim]
            if place[field] == "VERIFIED":
                require(any(item["factStatus"] == "VERIFIED" and item["sourceKind"] in PRIMARY for item in items)
                        and not any(item["factStatus"] == "DISPUTED" for item in items), "UNSUPPORTED_VERIFIED_CLAIM")
            if place[field] == "DISPUTED":
                require(any(item["factStatus"] == "DISPUTED" for item in items), "UNSUPPORTED_DISPUTE")
        coordinates = place["coordinates"]
        if coordinates is not None:
            require(coordinates["precision"] == public_text(coordinates["precision"]), "INVALID_PUBLIC_TEXT")
            require(place["physicalPlaceStatus"] == "VERIFIED", "UNSUPPORTED_COORDINATES")
            require(not any(item["claimType"] == "COORDINATES" and item["factStatus"] == "DISPUTED"
                            for item in attached.values()), "UNSUPPORTED_COORDINATES")
            require(all(identifier in attached and attached[identifier]["claimType"] == "COORDINATES"
                        and attached[identifier]["factStatus"] == "VERIFIED"
                        and attached[identifier]["sourceKind"] in PRIMARY for identifier in coordinates["sourceEvidenceIds"]),
                    "UNSUPPORTED_COORDINATES")
    for item in sources:
        ref = item["sourceRef"]
        require(source_ref(ref, [urlsplit(ref).netloc]) == ref, "UNSAFE_SOURCE_REF")
