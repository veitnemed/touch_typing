"""Prove ordinary journal practice through the actual engine, including restart."""
import gzip
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from time import monotonic, sleep
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import enter
from test_project_file_practice import active, chord

PRACTICAL = "terminal-practical-incident"


def options(desktop="ubuntu", **extra):
    return dict(app="terminal", level=0, mode="sandbox", guidance="guided", routeKind="course",
                desktopProfile=desktop, softFinish=True, combinedLimits=True,
                nativeEnglishInput=True, seed=0) | extra


def begin(engine, desktop, seed):
    engine.call("lab-start", **options(desktop, seed=seed))
    return engine.call("lab-resume")


def terminal(s):
    return s["scene"]["terminal"]


def log(s):
    return next(file["content"] for file in terminal(s)["files"] if file["path"] == "/home/student/project/app.log")


def submit(e, command):
    enter(e, command)
    return chord(e, "Enter")


def wait_for(e, ready):
    deadline = monotonic() + 8
    while monotonic() < deadline:
        s = e.call("lab-state")
        if ready(s):
            return s
        sleep(.07)
    raise AssertionError(("Journal did not advance", s["task"], terminal(s)["output"]))


class TerminalPracticalTests(unittest.TestCase):
    def setUp(self):
        profile = TemporaryDirectory()
        self.addCleanup(profile.cleanup)
        self.profile = Path(profile.name)
        context = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile.name})
        context.start()
        self.addCleanup(context.stop)
        self.e = None
        self.addCleanup(lambda: self.e.close() if self.e else None)

    def open(self, platform="linux"):
        if self.e is not None:
            self.e.close()
        self.e = Engine(verify=True)
        self.e.call("configure", platform=platform, layout="EN")
        return self.e

    def play(self, seed, platform="linux"):
        desktop = "ubuntu"
        e = self.open(platform)
        s = begin(e, desktop, seed)
        self.assertEqual((s["mission"], s["scenarioLength"], s["actionLimit"], s["timeLimit"]),
                         (PRACTICAL, 4, 0, 0))
        self.assertTrue(s["terminalPractical"])
        self.assertIn("Терминал Ubuntu", active(s)["title"])
        self.assertNotIn("Ctrl+R", s["task"]["hint"])
        s = chord(e, "Ctrl+R")
        s = enter(e, "grep")
        s = chord(e, "Enter")
        self.assertEqual((active(s)["focus"], s["scene"]["terminalInput"], terminal(s)["lastCommand"], s["solved"]),
                         ("reverse-search", "", "", 0), "Future lesson commands must not be preloaded")
        chord(e, "Esc")
        initial = log(s)
        s = submit(e, "grep INFO app.log")
        self.assertEqual((s["solved"], log(s)), (0, initial))
        s = submit(e, "grep ERROR app.log")
        self.assertEqual((s["solved"], log(s)), (1, initial))
        self.assertTrue(terminal(s)["output"][-1].startswith("ERROR "))
        old_id = terminal(s)["output"][-1].split()[1]
        s = submit(e, "tail -f app.log")
        self.assertEqual((s["solved"], terminal(s)["followingLog"], s["scene"]["terminalInput"]), (2, True, ""))
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (2, False))
        self.assertIn("до новой ошибки", s["status"])
        enter(e, "grep")
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], s["scene"]["terminalInput"]), (2, ""))
        s = submit(e, "tail -f app.log")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (2, True))
        s = wait_for(e, lambda state: log(state) != initial)
        paused = e.call("lab-pause")
        sleep(.55)
        unchanged = e.call("lab-state")
        self.assertEqual((unchanged["solved"], log(unchanged), terminal(unchanged)["followingLog"]),
                         (2, log(paused), True), "Paused time must not generate future log events")
        e = self.open(platform)
        restored = e.call("lab-start", **options(desktop, **{"continue": True}))
        self.assertEqual((restored["solved"], log(restored), terminal(restored)["followingLog"]),
                         (2, log(paused), True))
        e.call("lab-resume")
        s = wait_for(e, lambda state: terminal(state)["logIncidentArrived"])
        self.assertEqual(s["task"]["id"], "terminal-interrupt")
        line = next(line for line in log(s).splitlines()[len(initial.splitlines()):] if line.startswith("ERROR "))
        new_id = line.split()[1]
        self.assertNotEqual(new_id, old_id)
        wrong = chord(e, "Ctrl+Shift+C")
        self.assertEqual((wrong["solved"], terminal(wrong)["followingLog"]), (2, True))
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], terminal(s)["followingLog"], terminal(s)["output"][-1]), (3, False, "^C"))
        s = submit(e, f"grep {old_id} app.log")
        self.assertEqual(s["solved"], 3, "A real earlier failure is not the new incident")
        self.assertIn("новой строки ERROR", s["status"])
        s = submit(e, f"grep {new_id} app.log")
        self.assertEqual((s["solved"], s["finished"]), (4, True))
        self.assertEqual(terminal(s)["output"][-2:], [f"INFO {new_id} started", line])
        receipt = e.call("lab-result")
        self.assertTrue(receipt["completed"])
        self.assertTrue(next(r for r in receipt["series"] if r["task"] == "terminal-interrupt" and r["correct"]))
        follow = begin(e, desktop, seed)
        self.assertNotEqual(log(follow), initial, "The next completed run must use another incident")
        return new_id, line

    def test_lab_jobs_have_distinct_failures_and_read_only_results(self):
        first = self.play(0)
        self.assertIn("permission denied", first[1])

    def test_bash_remains_ubuntu_when_host_bindings_are_windows(self):
        second = self.play(1, "windows")
        self.assertIn("missing source file", second[1])

    def test_old_ordinary_draft_preserves_command_pointer_and_buffer(self):
        fixture = Path(__file__).with_name("fixtures") / "preview9-original-terminal-course.xml.gz"
        target = self.profile / "TouchTypingStudio-QA" / "progress.xml"
        target.parent.mkdir()
        target.write_bytes(gzip.decompress(fixture.read_bytes()))
        e = self.open()
        restored = e.call("lab-start", **options(**{"continue": True}))
        self.assertEqual((restored["mission"], restored["solved"], restored["task"]["id"],
                          restored["scene"]["terminalInput"], active(restored)["cursor"], restored["actionLimit"]),
                         ("terminal-explore", 1, "command/pwd", "pw", 2, 40))
        e.call("lab-resume")
        s = submit(e, "d")
        self.assertEqual((s["solved"], terminal(s)["lastCommand"], terminal(s)["output"][-1]),
                         (2, "pwd", "/home/student/project"))


if __name__ == "__main__":
    unittest.main()
