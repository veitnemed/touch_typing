"""Terminal class uses a real match, a working directory, and native key IDs."""
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_live_route_policy import advance, begin, enter
from test_project_file_practice import active, chord


class TerminalHistoryTheory(unittest.TestCase):
    def campaign(self, platform, test):
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform=platform, layout="EN")
                test(e)
            finally:
                e.close()

    def test_empty_wrong_then_correct_search_and_arrow_feedback(self):
        def check(e):
            s = begin(e, "terminal", routeKind="mission", mission="terminal-explore", adaptiveGuide=True)
            self.assertEqual((s["mission"], s["task"]["text"]),
                             ("terminal-explore", "cd /home/student/project"))
            self.assertIn("фрагмент", s["task"]["hint"])
            s = chord(e, "Ctrl+R")
            self.assertEqual((active(s)["focus"], s["scene"]["terminalInput"], s["solved"]),
                             ("reverse-search", "", 0))
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], s["errors"], s["scene"]["terminal"]["lastCommand"], active(s)["focus"]),
                             (0, 0, "", "reverse-search"))
            enter(e, "wc -l tags.txt")
            rejected = chord(e, "Enter")
            self.assertEqual((rejected["solved"], rejected["scene"]["terminal"]["lastCommand"]),
                             (0, "wc -l tags.txt"))
            self.assertNotEqual(rejected["scene"]["terminal"]["cwd"], "/home/student/project")
            chord(e, "Ctrl+R")
            enter(e, "student/project")
            s = e.call("lab-state")
            self.assertEqual(s["scene"]["terminalInput"], "cd /home/student/project")
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], s["scene"]["terminal"]["cwd"], s["task"]["id"]),
                             (1, "/home/student/project", "command/pwd"))

            while s["task"]["id"] != "terminal-history-up":
                s = advance(e, s)
            old = s["solved"]
            self.assertEqual(s["task"]["keys"], "Up")
            enter(e, "Up")
            s = chord(e, "Enter")
            self.assertEqual(s["solved"], old)
            result = chord(e, "Up")
            self.assertEqual((result["solved"], result["scene"]["terminalInput"]), (old, "Up"))
            result = chord(e, "Up")
            self.assertEqual((result["solved"], result["scene"]["terminalInput"], result["scene"]["terminal"]["lastCommand"]),
                             (old + 1, "pwd", "Up"))
        for profile in ("linux", "windows"):
            with self.subTest(profile=profile):
                self.campaign(profile, check)

    def test_query_miss_never_executes_stale_match_and_esc_restores_selection(self):
        def check(e):
            s = begin(e, "terminal", routeKind="mission", mission="terminal-explore")
            s = advance(e, s)  # cd /home/student/project
            s = advance(e, s)  # pwd genuinely executes before the search
            self.assertEqual(s["scene"]["terminal"]["lastCommand"], "pwd")
            s = enter(e, "abXde"); s = chord(e, "Home"); s = chord(e, "Right")
            s = chord(e, "Shift+Right")
            expected = (s["scene"]["terminalInput"], active(s)["anchor"], active(s)["cursor"], s["solved"], s["errors"])
            s = chord(e, "Ctrl+R")
            enter(e, "pw")
            self.assertEqual(e.call("lab-state")["scene"]["terminalInput"], "pwd")
            s = chord(e, "Z", "z")
            self.assertEqual((active(s)["query"], s["scene"]["terminalInput"]), ("pwz", ""))
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], s["errors"], s["scene"]["terminal"]["lastCommand"], active(s)["focus"]),
                             (expected[3], expected[4], "pwd", "reverse-search"))
            s = chord(e, "Backspace")
            self.assertEqual(s["scene"]["terminalInput"], "pwd")
            s = chord(e, "Esc")
            self.assertEqual((s["scene"]["terminalInput"], active(s)["anchor"], active(s)["cursor"],
                              s["solved"], s["errors"]), expected)
        self.campaign("linux", check)
