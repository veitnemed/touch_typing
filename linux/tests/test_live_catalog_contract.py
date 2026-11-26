"""Manual Live routes from explicit historical releases remain reachable by ID."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine


class LiveCatalogContract(unittest.TestCase):
    def test_preview3_catalog(self):
        self.check_baseline("live-catalog-preview3.json", "4c94d245e9885d6ef88e31b98b3dca148835f32d")

    def test_v192_catalog(self):
        self.check_baseline("live-catalog-v192.json", "2da29dfa0d54514ba43af8a43c0174122c5613a6")

    def check_baseline(self, name, source):
        baseline = Path(__file__).resolve().parents[2] / "qa/fixtures" / name
        fields = json.loads(baseline.read_text(encoding="utf-8"))
        self.assertEqual(fields["source"], source)
        for environment in ("ubuntu", "windows"):
            with self.subTest(environment=environment), tempfile.TemporaryDirectory() as directory, patch.dict(
                os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}
            ):
                engine = Engine(verify=True)
                try:
                    engine.call("configure", layout="EN", platform="linux" if environment == "ubuntu" else "windows")
                    missions = engine.call("lab-catalog", desktopProfile=environment)["missions"]
                    available = {(m["app"], m["id"]) for m in missions}
                    for app, lesson_ids in fields["environments"][environment].items():
                        missing = {(app, lesson_id) for lesson_id in lesson_ids} - available
                        self.assertFalse(missing, f"Previously shipped Live routes disappeared: {sorted(missing)}")
                finally:
                    engine.close()


if __name__ == "__main__":
    unittest.main()
