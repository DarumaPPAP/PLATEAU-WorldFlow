"""Validate the Phase 0 and Phase 1 wire shapes and run deterministic contract fixtures."""

import argparse
from datetime import datetime
from hashlib import sha256
import json
from pathlib import Path
import sys

from jsonschema import Draft202012Validator, FormatChecker, validators as schema_validators
from jsonschema.exceptions import SchemaError

from readonly_contract import response_issues
from static_common import ROOT, load_json

SETUP_CONTRACTS = ("dependency-inventory", "dependency-install-plan", "dependency-install-result")
CONTRACTS = (*SETUP_CONTRACTS, "readonly-request", "readonly-response")
DRAFT = "https://json-schema.org/draft/2020-12/schema"
WireValidator = schema_validators.extend(
    Draft202012Validator,
    type_checker=Draft202012Validator.TYPE_CHECKER.redefine(
        "integer", lambda _checker, value: type(value) is int,
    ),
)


def utc_second(value):
    if not isinstance(value, str):
        return True
    try:
        return datetime.fromisoformat(value).utcoffset() is not None
    except ValueError:
        return False


def validators(root):
    checker = FormatChecker(formats=())
    checker.checks("date-time")(utc_second)
    result = {}
    schema_ids = set()
    for name in CONTRACTS:
        schema = load_json(root / "docs/contracts" / f"{name}.schema.json")
        required_keys = {"$schema", "$id", "title", "type", "required"}
        if not required_keys <= schema.keys() or schema["$schema"] != DRAFT:
            raise ValueError(f"{name}: expected a complete Draft 2020-12 schema")
        if schema["$id"] in schema_ids or not schema["$id"].endswith(f"/v1/{name}.schema.json"):
            raise ValueError(f"{name}: invalid schema ID")
        schema_ids.add(schema["$id"])
        Draft202012Validator.check_schema(schema)
        result[name] = WireValidator(schema, format_checker=checker)
    return result


def canonical_plan_bytes(plan):
    fields = {key: value for key, value in plan.items() if key != "planHash"}
    return json.dumps(
        fields, sort_keys=True, ensure_ascii=False, separators=(",", ":"), allow_nan=False,
    ).encode("utf-8")


def plan_hash(plan):
    return sha256(canonical_plan_bytes(plan)).hexdigest()


def contract_issues(validator, contract, payload):
    errors = {
        "schema:" + error.validator for error in validator.iter_errors(payload)
    }
    if not errors and contract == "readonly-response":
        return response_issues(payload)
    if errors or contract != "dependency-install-plan":
        return errors
    try:
        if plan_hash(payload) != payload["planHash"]:
            return {"plan:hash"}
    except UnicodeEncodeError:
        return {"plan:unicode"}
    return set()


def fixture_context_issues(plan, context):
    """Model only the fixture inputs for C# integration vectors."""
    errors = set()
    if plan["projectFingerprint"] != context["projectFingerprint"]:
        errors.add("context:project")
    if not plan["createdAt"] <= context["now"] < plan["expiresAt"]:
        errors.add("context:expiry")
    fields = ("packageId", "sourceUri", "versionOrCommit", "expectedVersion", "licenseUrl")
    for change in plan["changes"]:
        identity = {key: change[key] for key in fields}
        if identity not in context["allowedChanges"]:
            errors.add("context:source")
    return errors


def validate_fixtures(root):
    schemas = validators(root)
    fixtures = root / "tests/fixtures"
    index = load_json(fixtures / "cases.json")
    seen = set()
    for case in index["cases"]:
        file = Path(case["file"])
        if len(file.parts) != 2 or file.parent.name not in schemas or file in seen:
            raise ValueError(f"invalid or duplicate fixture entry: {file}")
        seen.add(file)
        contract = file.parent.name
        payload = load_json(fixtures / file)
        errors = contract_issues(schemas[contract], contract, payload)
        if not errors and contract == "dependency-install-plan":
            errors = fixture_context_issues(payload, index["context"])
        expected = set() if case["expect"] == "valid" else {case["expect"]}
        if errors != expected:
            raise ValueError(f"{file}: expected {sorted(expected)}, got {sorted(errors)}")

    checked_in = {path.relative_to(fixtures) for name in SETUP_CONTRACTS for path in (fixtures / name).glob("*.json")}
    if seen != checked_in or any(Path(name, "valid.json") not in seen for name in SETUP_CONTRACTS):
        raise ValueError("fixture index must cover all contract fixtures and each valid case")

    plan_fixtures = fixtures / "dependency-install-plan"
    valid_plan = load_json(plan_fixtures / "valid.json")
    valid_result = load_json(fixtures / "dependency-install-result/valid.json")
    for field in ("projectFingerprint", "planId", "planHash"):
        if valid_result[field] != valid_plan[field]:
            raise ValueError(f"valid result fixture must link to the golden plan by {field}")
    if canonical_plan_bytes(valid_plan) != (plan_fixtures / "golden-canonical.txt").read_bytes():
        raise ValueError("canonical plan bytes differ from the cross-language golden fixture")
    return len(seen)


def validate_readonly_fixtures(root):
    schemas = validators(root)
    golden = load_json(root / "tests/fixtures/phase1/readonly-golden.json")
    cases = [*golden["cases"], golden["png"]]
    for case in cases:
        request = case["request"]
        response = case["response"]
        for field in ("requestId", "projectFingerprint", "operation"):
            if request[field] != response[field]:
                raise ValueError(f"Phase 1 golden does not correlate by {field}")
        issues = contract_issues(schemas["readonly-request"], "readonly-request", request)
        if bool(issues) != case.get("invalidRequest", False):
            raise ValueError(f"Phase 1 request golden mismatch: {request['operation']}, {sorted(issues)}")
        issues = contract_issues(schemas["readonly-response"], "readonly-response", response)
        if issues:
            raise ValueError(f"Phase 1 response golden mismatch: {response['operation']}, {sorted(issues)}")
    return len(cases)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", choices=CONTRACTS)
    parser.add_argument("document", type=Path, nargs="?")
    args = parser.parse_args()
    if bool(args.contract) != bool(args.document):
        parser.error("--contract and document must be supplied together")
    try:
        if args.document:
            validator = validators(ROOT)[args.contract]
            issues = contract_issues(validator, args.contract, load_json(args.document))
            if issues:
                raise ValueError(", ".join(sorted(issues)))
            print(f"PASS: {args.contract} shape and applicable hash")
        else:
            print(f"PASS: {len(CONTRACTS)} schemas, {validate_fixtures(ROOT)} Setup fixtures, canonical Plan bytes, {validate_readonly_fixtures(ROOT)} Read-only golden pairs")
    except SchemaError as error:
        print(f"FAIL: schema keyword {error.validator!r} at {list(error.path)}", file=sys.stderr)
        return 1
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
