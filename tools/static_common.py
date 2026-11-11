import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ID = "com.darumappap.plateau-worldflow"
PACKAGE = Path("Packages") / PACKAGE_ID


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON property")
        result[key] = value
    return result


def reject_constant(_value):
    raise ValueError("non-JSON numeric constant")


def parse_json(text):
    return json.loads(text, object_pairs_hook=unique_object, parse_constant=reject_constant)


def load_json(path):
    return parse_json(path.read_text(encoding="utf-8"))


def git_index(root):
    result = subprocess.run(
        ["git", "-C", root, "ls-files", "--stage", "-z"],
        check=True, capture_output=True,
    )
    files = {}
    for entry in result.stdout.decode("utf-8").split("\0"):
        if not entry:
            continue
        metadata, name = entry.split("\t", 1)
        mode, sha, stage = metadata.split()
        if mode not in {"100644", "100755"} or stage != "0":
            raise ValueError(f"expected a regular, resolved index entry: {name}")
        files[Path(name)] = sha
    return files
