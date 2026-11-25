"""Additive feature contract: every Preview 3 manual Live route stays available."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine


class LiveCatalogContract(unittest.TestCase):
    def test_both_desktop_profiles_retain_the_full_older_curriculum(self):
        baseline = Path(__file__).resolve().parents[2] / "qa/fixtures/live-catalog-preview3.json"
        fields = json.loads(baseline.read_text(encoding="utf-8"))
        self.assertEqual(fields["source"], "4c94d245e9885d6ef88e31b98b3dca148835f32d")
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
