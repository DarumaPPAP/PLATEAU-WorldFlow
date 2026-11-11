"""Validate the UPM manifest, repository file policy, and an optional release tag."""

import argparse
from pathlib import Path
import re
import subprocess
import sys

from static_common import PACKAGE, PACKAGE_ID, ROOT, git_index, parse_json

VERSION_NUMBER = r"(?:0|[1-9][0-9]*)"
PRERELEASE_ID = rf"(?:{VERSION_NUMBER}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
VERSION = re.compile(
    rf"{VERSION_NUMBER}\.{VERSION_NUMBER}\.{VERSION_NUMBER}"
    rf"(?:-{PRERELEASE_ID}(?:\.{PRERELEASE_ID})*)?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
)
DEPENDENCY_ID = re.compile(r"[a-z0-9]+(?:\.[a-z0-9][a-z0-9_-]*){2,}")
FORBIDDEN_DIRECTORIES = {
    "artifacts", "citygml", "datasets", "evidence", "generated", "library",
    "logs", "memorycaptures", "recordings", "temp", "usersettings",
}
FORBIDDEN_SUFFIXES = {
    ".7z", ".citygml", ".gml", ".gpkg", ".jks", ".jpeg", ".jpg", ".key", ".keystore",
    ".las", ".laz", ".mov", ".mp4", ".p8", ".p12", ".pem", ".pfx", ".png", ".rar",
    ".shp", ".tar", ".tgz", ".tif", ".tiff", ".unitypackage", ".webm", ".zip",
}
SECRET = re.compile(
    rb"-----BEGIN [A-Z ]*PRIVATE KEY-----"
    rb"|(?<![A-Za-z0-9])(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}"
    rb"|(?:AKIA|ASIA)[A-Z0-9]{16}|xox[baprs]-[A-Za-z0-9-]{12,})"
    rb"|https?://[^\s/@<>\"']+:[^\s/@<>\"']+@[A-Za-z0-9.-]+"
)
MAX_FILE_BYTES = 2 * 1024 * 1024


def validate_manifest(files):
    manifest = parse_json(files[PACKAGE / "package.json"].decode("utf-8"))
    expected = {
        "name": PACKAGE_ID,
        "displayName": "PLATEAU WorldFlow",
        "unity": "6000.3",
        "unityRelease": "10f1",
        "license": "MIT",
    }
    if not isinstance(manifest, dict):
        raise ValueError("package.json must contain an object")
    for field, value in expected.items():
        if manifest.get(field) != value:
            raise ValueError(f"package.json {field} must be {value!r}")
    if PACKAGE.name != manifest["name"]:
        raise ValueError("package name must match its directory")
    version = manifest.get("version")
    if not isinstance(version, str) or not VERSION.fullmatch(version):
        raise ValueError("package.json version must be SemVer")
    dependencies = manifest.get("dependencies", {})
    if not isinstance(dependencies, dict):
        raise ValueError("package.json dependencies must contain an object")
    for name, requirement in dependencies.items():
        if not DEPENDENCY_ID.fullmatch(name) or not isinstance(requirement, str) or not VERSION.fullmatch(requirement):
            raise ValueError("each package dependency must have a Registry package ID and fixed SemVer")
    return version


def validate_licenses(files):
    for filename in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
        original = files[Path(filename)]
        if original != files[PACKAGE / filename]:
            raise ValueError(f"Package {filename} differs from the repository copy")
    if "Copyright (c) 2026 Daruma" not in files[Path("LICENSE")].decode("utf-8").splitlines():
        raise ValueError("LICENSE is missing the existing copyright notice")


def read_package_files(root):
    index = git_index(root)
    if PACKAGE / "package.json" not in index:
        raise ValueError("stage the Package manifest before validating tracked files")
    manifests = [path for path in index if path.parts[0] == "Packages" and path.name == "package.json"]
    if manifests != [PACKAGE / "package.json"]:
        raise ValueError("expected a single UPM Package under Packages/")
    package_files = {}
    for relative, sha in index.items():
        name = relative.name.lower().removesuffix(".meta")
        folders = {part.lower() for part in relative.parts[:-1]}
        if (
            folders & FORBIDDEN_DIRECTORIES
            or relative.as_posix().lower().startswith("assets/plateauworldflow/")
            or Path(name).suffix in FORBIDDEN_SUFFIXES
            or name == ".env" or name.startswith(".env.")
        ):
            raise ValueError(f"forbidden tracked file: {relative}")
        command = ["git", "-C", root, "cat-file"]
        size = int(subprocess.check_output([*command, "-s", sha]))
        if size > MAX_FILE_BYTES:
            raise ValueError(f"tracked file exceeds 2 MiB: {relative}")
        content = subprocess.check_output([*command, "blob", sha])
        if SECRET.search(content):
            raise ValueError(f"potential credential in tracked file: {relative}")
        if relative.parent in {Path("."), PACKAGE}:
            package_files[relative] = content
    return package_files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--tag", help="check a proposed or actual tag without publishing")
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        files = read_package_files(root)
        version = validate_manifest(files)
        validate_licenses(files)
        if args.tag is not None and args.tag != f"v{version}":
            raise ValueError(f"tag must match package.json version (expected v{version})")
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print(f"PASS: {PACKAGE_ID} {version}, license and tracked-file checks")
    if args.tag is not None:
        print("PASS: tag matches package.json version")
    return 0


if __name__ == "__main__":
    sys.exit(main())
