import json
from pathlib import Path
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = Path("Packages/com.darumappap.plateau-worldflow")
SCRIPT = ROOT / "tools/validate_package.py"


class PackageCliTests(unittest.TestCase):
    def setUp(self):
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        shutil.copytree(ROOT / PACKAGE, self.root / PACKAGE)
        for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
            shutil.copyfile(ROOT / name, self.root / name)
        self.git("init", "-q")
        self.git("add", ".")

    def git(self, *args):
        subprocess.run(["git", "-C", self.root, *args], check=True, capture_output=True)

    def check_cli(self, expected_code, expected_text, *args):
        result = subprocess.run(
            [sys.executable, SCRIPT, "--root", self.root, *args],
            capture_output=True,
            text=True,
        )
        output = result.stdout + result.stderr
        self.assertEqual(result.returncode, expected_code, output)
        self.assertIn(expected_text, output)

    def change_manifest(self, **changes):
        path = self.root / PACKAGE / "package.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        path.write_text(json.dumps({**manifest, **changes}), encoding="utf-8")
        self.git("add", str(path))

    def test_valid_package(self):
        self.check_cli(0, "PASS")

    def test_invalid_package_id(self):
        self.change_manifest(name="com.example.wrong")
        self.check_cli(1, "name")

    def test_external_git_dependency(self):
        self.change_manifest(dependencies={"com.example.fixture": "https://example.invalid/a.git"})
        self.check_cli(1, "dependency")

    def test_registry_dependency(self):
        self.change_manifest(dependencies={"com.example.fixture": "1.2.3"})
        self.check_cli(0, "PASS")

    def test_forbidden_tracked_asset(self):
        path = self.root / "fixture.gml"
        path.touch()
        self.git("add", path.name)
        self.check_cli(1, "forbidden tracked file")

    def test_unstaged_change_cannot_hide_invalid_staged_manifest(self):
        manifest = self.root / PACKAGE / "package.json"
        original = manifest.read_bytes()
        self.change_manifest(name="com.example.wrong")
        manifest.write_bytes(original)
        self.check_cli(1, "name")

    def test_tracked_generated_meta(self):
        path = self.root / "fixture.png.meta"
        path.touch()
        self.git("add", path.name)
        self.check_cli(1, "forbidden tracked file")

    def test_license_drift(self):
        (self.root / PACKAGE / "LICENSE").write_text("fixture", encoding="utf-8")
        self.git("add", str(PACKAGE / "LICENSE"))
        self.check_cli(1, "LICENSE")

    def test_release_tag_match(self):
        manifest = json.loads((self.root / PACKAGE / "package.json").read_text(encoding="utf-8"))
        self.check_cli(0, "tag matches", f"--tag=v{manifest['version']}")

    def test_release_tag_mismatch(self):
        self.check_cli(1, "tag must match", "--tag=v99.0.0")
