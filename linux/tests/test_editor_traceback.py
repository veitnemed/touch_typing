"""Navigate from actual failure to a saved single-line repair and rerun."""
import os
import re
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import advance, begin, enter
from test_project_file_practice import active, chord, disk


class TracebackEditorTests(unittest.TestCase):
    def test_two_cases_platforms_bad_line_and_restart(self):
        fingerprints = set()
        for desktop in ("ubuntu", "windows"):
            for seed in (0, 1, 2):
                with self.subTest(host=desktop, seed=seed), TemporaryDirectory() as profile, patch.dict(
                        os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
                    e = Engine(verify=True)
                    setup = dict(level=1, desktopProfile=desktop, guidance="guided", mission="code-traceback-line",
                                 seed=seed, routeKind="mission")
                    def configure():
                        e.call("configure", platform="windows" if desktop == "windows" else "linux", layout="EN")
                    try:
                        configure()
                        s = begin(e, **setup)
                        self.assertEqual((s["mission"], s["solved"]), ("code-traceback-line", 0))
                        console = "\n".join(s["scene"]["terminal"]["output"])
                        file, line = re.search(r"Traceback: ([a-z_]+\.py):(\d+)", console).groups()
                        setting, limit = re.search(r"ValueError: (\w+) должен быть (\d+)", console).groups()
                        fingerprints.add((file, line, limit))
                        initial = disk(s)
                        path = "/home/student/project/" + file
                        s = advance(e, s)
                        self.assertEqual(active(s)["focus"], "goto-line")
                        enter(e, str(int(line) + 1))
                        s = chord(e, "Enter")
                        self.assertEqual(s["solved"], 1)
                        chord(e, "Ctrl+G"); enter(e, line)
                        s = chord(e, "Enter")
                        self.assertEqual(s["solved"], 2)
                        self.assertEqual(active(s)["body"][:active(s)["bodyCursor"]].count("\n"), int(line) - 1)
                        chord(e, "End")
                        e.call("lab-pause"); e.close()
                        e = Engine(verify=True); configure()
                        s = begin(e, **setup, **{"continue": True})
                        self.assertEqual((s["solved"], active(s)["focus"]), (2, "body"))
                        self.assertEqual(active(s)["body"][:active(s)["bodyCursor"]].split("\n")[-1], setting + " = 0")
                        chord(e, "Backspace"); enter(e, limit)
                        s = chord(e, "Ctrl+S")
                        self.assertEqual(s["solved"], 3)
                        expected = initial[path].replace(setting + " = 0", setting + " = " + limit)
                        self.assertEqual(disk(s), initial | {path: expected})
                        s = advance(e, s)
                        s = advance(e, s)
                        self.assertTrue(s["finished"])
                        self.assertEqual(s["solved"], 5)
                        self.assertIn("OK: " + setting + " = " + limit, "\n".join(s["scene"]["terminal"]["output"]))
                    finally:
                        e.close()
        self.assertGreaterEqual(len(fingerprints), 2)


if __name__ == "__main__":
    unittest.main()
