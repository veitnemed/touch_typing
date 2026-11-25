"""Real adaptive Live input, restart, and evidence across different workspaces."""
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from platform_linux import Engine
from test_live_route_policy import begin, advance, enter
from test_project_file_practice import active, chord

PROFILE = "TouchTypingStudio-QA/progress.xml"


def seek(e, s, condition, maximum=80):
    for _ in range(maximum):
        if condition(s):
            return s
        assert not s["finished"] and not s["limitDecisionPending"], s["mission"]
        s = advance(e, s)
    raise AssertionError(("condition is unreachable", s["mission"], s["task"]))


class LiveCoachTests(unittest.TestCase):
    def start_chrome_with_a_miss(self, e):
        e.call("configure", platform="linux", layout="EN")
        s = begin(e, "chrome", adaptiveGuide=True, actionLimit=100)
        self.assertEqual((s["task"]["id"], s["coachStage"], s["showGuide"]),
                         ("url", "", True))
        s = chord(e, "Alt+D")
        self.assertEqual((s["solved"], s["errors"], s["coachStage"], s["showGuide"]),
                         (0, 1, "practice", True))
        self.assertIsNotNone(s["guide"])
        s = chord(e, "Ctrl+L")
        self.assertEqual(s["solved"], 1)
        s = seek(e, s, lambda state: state["coachStage"] == "recall")
        self.assertGreaterEqual(s["solved"], 3)
        self.assertEqual((s["task"]["id"], s["showGuide"], s["coachPrompt"], s["guide"]),
                         ("url", False, "Выделите адрес страницы", None))
        return s

    def test_help_then_blind_recall_then_real_transfer_survive_restart(self):
        with TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            e = Engine(verify=True)
            try:
                s = self.start_chrome_with_a_miss(e)
                before = s["solved"], s["mission"], active(s)["url"], s["coachReason"]
                self.assertTrue(e.call("lab-stop")["saved"])
                e.close(); e = Engine(verify=True)
                e.call("configure", platform="linux", layout="EN")
                s = begin(e, "chrome", adaptiveGuide=False, **{"continue": True})
                self.assertEqual((s["solved"], s["mission"], active(s)["url"], s["coachReason"]), before)
                self.assertTrue(s["adaptiveGuide"])
                self.assertEqual((s["coachStage"], s["showGuide"], s["guide"]), ("recall", False, None))

                s = chord(e, "Ctrl+L")
                self.assertEqual(s["solved"], before[0] + 1)
                s = seek(e, s, lambda state: state["coachStage"] == "transfer")
                self.assertEqual((s["mission"], active(s)["url"], s["showGuide"], s["guide"]),
                                 ("chrome-docs-reference", "https://www.google.com/", False, None))
                self.assertNotEqual((s["mission"], active(s)["url"]), before[1:3])
                chord(e, "Ctrl+L")
                self.assertTrue(e.call("lab-stop")["saved"])
                events = ET.parse(Path(root) / PROFILE).findall("./ShortcutHistory/ShortcutAttempt")
                receipts = [row for row in events if row.findtext("Skill") == "lab/chrome/url"
                            and row.findtext("Correct") == "true" and row.findtext("CoachStage")]
                self.assertEqual([r.findtext("CoachStage") for r in receipts], ["practice", "recall", "transfer"])
                self.assertEqual([r.findtext("Assisted") for r in receipts], ["true", "false", "false"])
                self.assertEqual([r.findtext("FirstTry") for r in receipts], ["false", "true", "true"])
                self.assertEqual([r.findtext("Independent") for r in receipts], ["false", "true", "true"])
                self.assertNotEqual(receipts[1].findtext("CoachContext"), receipts[2].findtext("CoachContext"))
            finally:
                e.close()

    def test_explicit_hint_never_credit_for_remembering(self):
        with TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            e = Engine(verify=True)
            try:
                s = self.start_chrome_with_a_miss(e)
                s = e.call("lab-hint")
                self.assertEqual((s["coachStage"], s["showGuide"]), ("recall", True))
                self.assertIsNotNone(s["guide"])
                self.assertTrue(e.call("lab-stop")["saved"])
                e.close(); e = Engine(verify=True)
                e.call("configure", platform="linux", layout="EN")
                s = begin(e, "chrome", **{"continue": True})
                self.assertEqual((s["coachStage"], s["showGuide"]), ("recall", True))
                s = chord(e, "Ctrl+L")
                s = seek(e, s, lambda state: state["mission"] == "chrome-docs-reference")
                self.assertEqual((s["coachStage"], s["showGuide"]), ("recall", False))
                self.assertTrue(e.call("lab-stop")["saved"])
                events = ET.parse(Path(root) / PROFILE).findall("./ShortcutHistory/ShortcutAttempt")
                receipts = [r for r in events if r.findtext("CoachStage") == "recall" and r.findtext("Correct") == "true"]
                self.assertEqual([e.findtext("Assisted") for e in receipts], ["true"])
            finally:
                e.close()

    def test_terminal_connection_requires_a_new_working_directory_for_transfer(self):
        with TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                s = begin(e, "terminal", adaptiveGuide=True, actionLimit=100)
                s = seek(e, s, lambda state: state["task"]["id"] == "command/pwd")
                enter(e, "pwf")
                s = chord(e, "Enter")
                self.assertEqual((s["coachStage"], s["showGuide"]), ("practice", True))
                s = advance(e, s)
                s = seek(e, s, lambda state: state["coachStage"] == "recall")
                self.assertEqual((s["task"]["id"], s["coachPrompt"], s["showGuide"]),
                                 ("command/pwd", "Узнайте, в какой папке вы находитесь", False))
                prior = s["scene"]["terminal"]["cwd"]
                s = advance(e, s)
                s = seek(e, s, lambda state: state["mission"] == "terminal-essentials-locate")
                same_folder = seek(e, s, lambda state: state["task"]["id"] == "command/pwd")
                self.assertEqual(same_folder["scene"]["terminal"]["cwd"], prior)
                self.assertNotEqual(same_folder["coachStage"], "transfer")
                s = seek(e, advance(e, same_folder), lambda state: state["coachStage"] == "transfer")
                self.assertEqual((s["mission"], s["task"]["id"], s["showGuide"]),
                                 ("terminal-essentials-locate", "command/pwd", False))
                self.assertNotEqual(s["scene"]["terminal"]["cwd"], prior)
                s = advance(e, s)
                self.assertTrue(s["scene"]["terminal"]["output"][-1].strip() == "/home/student")
            finally:
                e.close()

    def test_repeated_weakness_cannot_starve_old_unrelated_routes(self):
        with TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                expected = {m["id"] for m in e.call("lab-catalog")["missions"] if m["app"] == "chrome" and m["level"] == 0}
                s = begin(e, "chrome", adaptiveGuide=True, actionLimit=0, seconds=0)
                visited = set()
                for _ in range(len(expected)):
                    section = s["mission"]
                    self.assertNotIn(section, visited)
                    visited.add(section)
                    while s["mission"] == section:
                        if s["task"]["id"] == "url":
                            old = s["solved"]
                            s = chord(e, "Alt+D")
                            self.assertEqual(s["solved"], old)
                        s = advance(e, s)
                self.assertEqual(visited, expected)
            finally:
                e.close()


if __name__ == "__main__":
    unittest.main()
