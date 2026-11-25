"""The Handbook Backup route and the training browser history are real data flows."""
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_live_route_policy import advance, enter, options
from test_project_file_practice import active, chord


class ChromeHistoryFilter(unittest.TestCase):
    def test_handbook_search_real_history_and_draft(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as root, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                opts = options("chrome", 2, "ubuntu", routeKind="mission", mission="chrome-restore-source",
                               actionLimit=0, seconds=0, seed=1)
                e = Engine(verify=True)
                try:
                    e.call("configure", platform=platform, layout="EN")
                    e.call("lab-start", **opts)
                    s = e.call("lab-resume")
                    s = chord(e, "Ctrl+L")
                    self.assertEqual((s["solved"], s["task"]["text"], active(s)["focus"]), (1, "handbook backup", "url"))
                    enter(e, "Handbook  Backup")
                    s = chord(e, "Enter")
                    self.assertEqual(s["solved"], 2)  # visible query case/spacing differ; results match
                    self.assertIn("Handbook%20%20Backup", active(s)["url"])
                    while s["task"]["id"] != "history":
                        s = advance(e, s)
                    s = chord(e, "Ctrl+H")
                    w = active(s)
                    before = s["solved"]
                    original = set(row["path"] for row in w["entries"])
                    self.assertEqual((w["url"], w["focus"], w["historySearch"], w["item"]),
                                     ("chrome://history", "history", "", -1))
                    for letter in "Резервная":
                        s = chord(e, "X", letter)
                    self.assertTrue(active(s)["entries"])
                    self.assertTrue(all("Резервная" in row["title"] for row in active(s)["entries"]))
                    s = chord(e, "Ctrl+F")
                    self.assertEqual((active(s)["anchor"], active(s)["cursor"]), (0, len("Резервная")))
                    enter(e, "not-visited")
                    self.assertFalse(active(e.call("lab-state"))["entries"])
                    s = chord(e, "Enter")
                    self.assertEqual((active(s)["url"], s["solved"]), ("chrome://history", before))
                    chord(e, "Ctrl+F")
                    enter(e, "Handbook Backup")
                    s = e.call("lab-state")
                    w = active(s)
                    matches = {row["path"] for row in w["entries"]}
                    self.assertTrue(matches < original)
                    self.assertTrue(all("/handbook/backup" in path or path.startswith("https://www.google.com/search?q=Handbook") for path in matches))
                    self.assertIn("https://practice.local/handbook/backup#procedure", matches)
                    self.assertEqual((w["input"], w["historySearch"], w["item"]), ("Handbook Backup", "Handbook Backup", -1))
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close()
                    e = Engine(verify=True)
                    e.call("configure", platform=platform, layout="EN")
                    e.call("lab-start", **(opts | {"continue": True}))
                    s = e.call("lab-resume")
                    self.assertEqual((active(s)["focus"], active(s)["historySearch"], s["solved"]),
                                     ("history", "Handbook Backup", before))
                    self.assertEqual(set(row["path"] for row in active(s)["entries"]), matches)
                    s = e.call("lab-browser-history-focus", action="entry", path="https://practice.local/not-visited")
                    self.assertEqual(active(s)["item"], -1)
                    s = e.call("lab-browser-history-focus", action="entry", path=next(iter(matches)))
                    self.assertGreaterEqual(active(s)["item"], 0)
                    self.assertEqual(s["solved"], before)
                    s = e.call("lab-browser-history-focus", action="search")
                    self.assertEqual((active(s)["item"], s["solved"]), (-1, before))
                    while s["task"]["id"] != "url":
                        s = advance(e, s)
                    self.assertEqual(active(s)["url"], "https://practice.local/handbook/backup#procedure")
                    self.assertGreater(s["solved"], before)
                finally:
                    e.close()
