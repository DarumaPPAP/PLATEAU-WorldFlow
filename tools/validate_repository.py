"""Verify repository links, identifiers, Skills, and static workflow policy."""

from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit

import yaml

from static_common import PACKAGE, PACKAGE_ID, ROOT

SKILLS = {
    "plateau-world-planner", "plateau-location-research", "plateau-area-design",
    "plateau-quality-design", "plateau-city-import", "plateau-result-validation",
}
JOB_NAMES = {
    "validate.yml": "Static validation",
    "package-check.yml": "Package validation",
    "release-check.yml": "Release version validation",
}
LINK = re.compile(r"(?<!!)\[[^\]\n]+\]\(([^)\s]+)\)")
CHECKOUT = re.compile(r"actions/checkout@[0-9a-f]{40}")
PACKAGE_TOKEN = re.compile(r"\bcom\.darumappap\.[a-z0-9.-]+")


def verify_skills(root):
    path = root / ".agents/skills"
    files = {file.parent.name: file for file in path.glob("*/SKILL.md")}
    if files.keys() != SKILLS:
        raise ValueError("expected the six Phase 0 Skills")
    for name, file in files.items():
        source = file.read_text(encoding="utf-8")
        if not source.startswith("---\n") or "\n---\n" not in source[4:]:
            raise ValueError(f"{name}: missing YAML frontmatter")
        header, body = source[4:].split("\n---\n", maxsplit=1)
        metadata = yaml.safe_load(header)
        if not isinstance(metadata, dict) or metadata.get("name") != name:
            raise ValueError(f"{name}: frontmatter name does not match its directory")
        if not isinstance(metadata.get("description"), str) or not metadata["description"].strip():
            raise ValueError(f"{name}: missing description")
        sections = ("Trigger", "入力", "出力", "中止条件", "Phase境界", "参照契約")
        for section in sections:
            if not re.search(rf"^## {section}\n\s*\n\S", body, re.MULTILINE):
                raise ValueError(f"{name}: missing section {section}")
        if "未実装" not in body or "docs/contracts/" not in body:
            raise ValueError(f"{name}: missing implementation boundary or contract reference")
        if name == "plateau-city-import" and "実行禁止" not in body:
            raise ValueError(f"{name}: missing Phase 0 execution prohibition")


def verify_links_and_ids(root):
    docs = [root / name for name in (
        "README.md", "AGENTS.md", "CHANGELOG.md", ".github/PULL_REQUEST_TEMPLATE.md",
        PACKAGE / "README.md",
    )]
    docs.extend((root / "docs").rglob("*.md"))
    docs.extend((root / ".agents/skills").glob("*/SKILL.md"))
    for file in docs:
        source = file.read_text(encoding="utf-8")
        for token in PACKAGE_TOKEN.findall(source):
            if token.rstrip(".") != PACKAGE_ID:
                raise ValueError(f"{file.relative_to(root)}: inconsistent Package ID")
        visible = re.sub(r"^\x60{3}[^\n]*\n[\s\S]*?^\x60{3}[ \t]*$", "", source, flags=re.MULTILINE)
        for link in LINK.findall(visible):
            url = urlsplit(link)
            if url.scheme or url.netloc or not url.path:
                continue
            target = (file.parent / unquote(url.path)).resolve()
            if not target.is_relative_to(root) or not target.exists():
                raise ValueError(f"{file.relative_to(root)}: invalid local link {link}")
    for file in ("README.md", "AGENTS.md", "docs/architecture/system-spec.md", PACKAGE / "README.md"):
        if PACKAGE_ID not in (root / file).read_text(encoding="utf-8"):
            raise ValueError(f"{file}: missing Package ID")


def verify_workflows(root):
    for filename, job_name in JOB_NAMES.items():
        path = root / ".github/workflows" / filename
        source = path.read_text(encoding="utf-8")
        workflow = yaml.load(source, Loader=yaml.BaseLoader)
        events = workflow["on"]
        expected_events = {"pull_request", "push"} if filename == "release-check.yml" else {"pull_request"}
        if set(events) != expected_events or workflow["permissions"] != {"contents": "read"}:
            raise ValueError(f"{filename}: unexpected trigger or token permissions")
        if filename == "release-check.yml" and events["push"] != {"tags": ["v*"]}:
            raise ValueError(f"{filename}: unexpected release trigger")
        jobs = workflow["jobs"]
        if len(jobs) != 1 or re.search(r"\$\{\{\s*secrets\s*[.\[]", source, re.IGNORECASE):
            raise ValueError(f"{filename}: unexpected job count or secret reference")
        job = next(iter(jobs.values()))
        if job["name"] != job_name or job["runs-on"] != "ubuntu-24.04" or "permissions" in job:
            raise ValueError(f"{filename}: unexpected runner, job name, or permission override")
        actions = [step for step in job["steps"] if "uses" in step]
        if len(actions) != 1 or not CHECKOUT.fullmatch(actions[0]["uses"]):
            raise ValueError(f"{filename}: expected a SHA-pinned official checkout")
        if actions[0].get("with") != {"persist-credentials": "false"}:
            raise ValueError(f"{filename}: checkout must use the current event and discard credentials")


def main():
    try:
        verify_skills(ROOT)
        verify_links_and_ids(ROOT)
        verify_workflows(ROOT)
    except (OSError, ValueError, yaml.YAMLError, TypeError, KeyError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: 6 Skills, local links, Package ID, and 3 read-only workflows")
    return 0


if __name__ == "__main__":
    sys.exit(main())
