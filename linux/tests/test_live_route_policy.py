"""The ordinary Live cards must continue on real scene results, not on placeholders."""
import os
from tempfile import TemporaryDirectory
from time import monotonic, sleep
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_debug_day import png_fixture
from test_editor_course import advance as advance_editor, enter
from test_project_file_practice import active, chord

TERMINAL_FULL = (
    {"terminal-explore", "terminal-handoff", "terminal-backup", "terminal-investigate"},
    {"terminal-access", "terminal-catalog", "terminal-triage", "terminal-transfer"},
    {"terminal-revision", "terminal-snapshot", "terminal-incident", "terminal-changes"},
)
TERMINAL_PRACTICAL = "terminal-practical-incident"
TERMINAL_POWERSHELL = "terminal-powershell-diagnostic"


def terminal_course(desktop):
    return TERMINAL_POWERSHELL if desktop == "windows" else TERMINAL_PRACTICAL

BASE_START = {
    "ubuntu": ("ubuntu-start", "ubuntu-research", "ubuntu-briefing"),
    "windows": ("windows-desktop-0", "windows-desktop-1", "windows-desktop-2"),
    "chrome": ("chrome-reader", "chrome-library", "chrome-review"),
    "code": ("code-guide", "code-refactor", "code-handover"),
    "terminal": (TERMINAL_PRACTICAL,) * 3,
    "workday/ubuntu": ("workday-report", "workday-analysis", "workday-handover"),
    "workday/windows": ("workday-windows-0", "workday-windows-1", "workday-windows-2"),
}


def options(app, level, desktop, **overrides):
    return dict(app=app, level=level, mode="sandbox", guidance="guided", desktopProfile=desktop,
                softFinish=True, combinedLimits=True, preserveDraft=True, nativeEnglishInput=True,
                seed=0) | overrides


def begin(e, app, level=0, desktop="ubuntu", **overrides):
    e.call("lab-start", **options(app, level, desktop, **{"routeKind": "course"} | overrides))
    return e.call("lab-resume")


def advance(e, s):
    if s["app"] == "code":
        return advance_editor(e, s)
    if s["task"]["id"] == "terminal-interrupt":
        deadline = monotonic() + 8
        while not s["scene"]["terminal"]["logIncidentArrived"] and monotonic() < deadline:
            sleep(.09)
            s = e.call("lab-state")
        assert s["scene"]["terminal"]["logIncidentArrived"], s["scene"]["terminal"]
    task = s["task"]
    before = s["solved"]
    key = task["keys"]
    if task["id"] == "project-search-submit":
        enter(e, task["text"])
    if key:
        for _ in range(35 if key in ("Down", "Tab") else 1):
            state = chord(e, key, png_fixture() if task["id"] == "debug-window-shot" else "")
            if state["solved"] > before:
                break
    else:
        if task["id"].startswith("open-"):
            # Application goals name a target; their explicit submit opens
            # the named app, which must actually become the active window.
            assert not s["scene"].get("overview") or s["scene"].get("search", "") == ""
        enter(e, task["text"])
        state = chord(e, "Enter")
    assert state["solved"] == before + 1, (s["mission"], task, state["status"], state["scene"]["layout"])
    return state


class LiveRoutePolicyTests(unittest.TestCase):
    def run_profile(self, callback, desktop="ubuntu"):
        with TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="windows" if desktop == "windows" else "linux", layout="EN")
                return callback(e)
            finally:
                e.close()

    def test_all_default_cards_start_foundation_and_progress_past_it(self):
        observations = []
        for desktop in ("ubuntu", "windows"):
            for app in (desktop, "chrome", "code", "terminal", "workday"):
                for level in range(3):
                    with self.subTest(desktop=desktop, app=app, level=level):
                        def check(e):
                            catalog = e.call("lab-catalog", desktopProfile=desktop)
                            ids = {m["id"] for m in catalog["missions"] if m["app"] == app and m["level"] == level}
                            first = (terminal_course(desktop) if app == "terminal" else
                                     BASE_START[f"workday/{desktop}" if app == "workday" else app][level])
                            # An explicit 100 allows a full first route even in
                            # advanced Workday instead of overriding the cap.
                            limits = {} if level == 0 else {"actionLimit": 100}
                            s = begin(e, app, level, desktop, **limits)
                            self.assertEqual((s["mission"], s["routeKind"], s["timeLimit"], s["actionLimit"]),
                                             (first, "course", 0, limits.get("actionLimit", 0 if app == "terminal" else 40)))
                            start_length = s["scenarioLength"]
                            if app == "terminal":
                                for _ in range(start_length):
                                    s = advance(e, s)
                                self.assertEqual((s["solved"], s["finished"]), (4, True))
                                self.assertTrue(e.call("lab-result")["completed"])
                                observations.append(f"{desktop}/{app}/{level}: {first}(4), цель выполнена без повторов")
                                return
                            visited = [first]
                            for _ in range(max(20, start_length + 1)):
                                if s["limitDecisionPending"]:
                                    # A rehearsal may have a more verbose first
                                    # phase on a different key profile. Respect
                                    # the cap rather than forcing an extra credit.
                                    self.fail((app, level, start_length, s["solved"]))
                                old_section = s["scenarioStep"]
                                s = advance(e, s)
                                if s["mission"] != visited[-1] or s["scenarioStep"] < old_section:
                                    visited.append(s["mission"])
                            self.assertFalse(s["finished"])
                            self.assertGreaterEqual(len(visited), 2)
                            self.assertTrue(set(visited) <= ids)
                            # Windows basic routes can finish two distinct
                            # sections before the twentieth action.
                            if len(ids) > 1:
                                self.assertNotEqual(visited[1], first)
                            observations.append(f"{desktop}/{app}/{level}: {first}({start_length}) → " +
                                                " → ".join(visited[1:]) + f" ({s['solved']} credited)")
                        self.run_profile(check, desktop)
        self.assertEqual(len(observations), 30)
        print("\nCourse checks (" + str(len(observations)) + ")\n" + "\n".join(observations))

    def test_all_twelve_original_terminal_workflows_remain_directly_selectable(self):
        for desktop in ("ubuntu", "windows"):
            for level, original in enumerate(TERMINAL_FULL):
                with self.subTest(desktop=desktop, level=level):
                    def check(e):
                        # Bash remains selectable in its original simulation on either physical host.
                        catalog = e.call("lab-catalog", desktopProfile="ubuntu")
                        ids = {m["id"] for m in catalog["missions"] if m["app"] == "terminal" and m["level"] == level}
                        self.assertTrue(original <= ids)
                        self.assertFalse(set.union(*[ids for i, ids in enumerate(TERMINAL_FULL) if i != level]) & ids)
                        for mission in sorted(original):
                            s = begin(e, "terminal", level, "ubuntu", routeKind="mission", mission=mission,
                                      actionLimit=0, seconds=0)
                            self.assertGreaterEqual(s["scenarioLength"], 20)
                            for _ in range(s["scenarioLength"]):
                                self.assertEqual(s["mission"], mission)
                                s = advance(e, s)
                            self.assertTrue(s["finished"] and e.call("lab-result")["completed"], mission)
                    self.run_profile(check, desktop)

    def test_history_chooses_a_different_established_route_and_preserves_the_old_draft(self):
        for desktop in ("ubuntu", "windows"):
            for app in (desktop, "chrome", "code", "terminal", "workday"):
                with self.subTest(desktop=desktop, app=app):
                    def check(e):
                        profile = "ubuntu" if app == "terminal" else desktop
                        old = "terminal-explore" if app == "terminal" else BASE_START[
                            f"workday/{desktop}" if app == "workday" else app][0]
                        e.call("lab-start", **options(app, 0, profile, mission=old, actionLimit=0, seconds=0))
                        used = advance(e, e.call("lab-resume"))
                        self.assertTrue(e.call("lab-stop")["saved"])
                        fresh = begin(e, app, desktop=profile)
                        self.assertNotEqual(fresh["mission"], old)
                        self.assertEqual((fresh["solved"], fresh["actionLimit"]), (0, 0 if app == "terminal" else 40))
                        self.assertFalse(fresh["mission"].startswith(("code-notebook-", "code-project-file-", "terminal-essentials-")))
                        advance(e, fresh)
                        self.assertTrue(e.call("lab-stop")["saved"])
                        restored = begin(e, app, desktop=profile, **{"continue": True})
                        self.assertEqual((restored["mission"], restored["solved"], restored["task"]),
                                         (old, used["solved"], used["task"]))
                        self.assertEqual((restored["routeKind"], restored["actionLimit"], restored["timeLimit"]), ("", 0, 0))
                    self.run_profile(check, desktop)

    def test_finite_selection_and_both_budget_choices(self):
        def check(e):
            s = begin(e, "chrome", routeKind="mission", mission="chrome-reader", actionLimit=100, seconds=180)
            length = s["scenarioLength"]
            for _ in range(length):
                s = advance(e, s)
            self.assertEqual((s["solved"], s["routeKind"], s["mission"], s["finished"]),
                             (length, "mission", "chrome-reader", True))
            self.assertTrue(e.call("lab-result")["completed"])
            s = begin(e, "chrome", actionLimit=20, seconds=180)
            while s["solved"] < 20:
                s = advance(e, s)
            self.assertEqual((s["limitReached"], s["limitDecisionPending"], s["paused"]), ("actions", True, True))
            e.call("lab-limit-choice", choice="continue")
            s = e.call("lab-resume")
            while not s["finished"]:
                s = advance(e, s)
            self.assertGreater(s["solved"], 20)
            self.assertTrue(e.call("lab-result")["completed"])
            s = begin(e, "windows", desktop="windows", actionLimit=20, seconds=1)
            s = advance(e, s)
            deadline = monotonic() + 3
            while not s["limitDecisionPending"] and monotonic() < deadline:
                sleep(.025)
                s = e.call("lab-state")
            self.assertEqual((s["limitReached"], s["limitDecisionPending"], s["paused"]), ("timer", True, True))
            s = e.call("lab-limit-choice", choice="finish")
            self.assertEqual((s["solved"],e.call("lab-result")["completed"]), (1,True))
        self.run_profile(check)

    def test_chrome_exact_key_and_context_with_retry(self):
        def check(e):
            s = begin(e, "chrome")
            self.assertEqual(s["task"]["keys"], "Ctrl+L")
            s = chord(e, "Alt+D")
            self.assertEqual((active(s)["focus"], s["solved"]), ("url", 0))
            s = chord(e, "Ctrl+L")
            self.assertEqual((active(s)["focus"], s["solved"]), ("url", 1))
            self.assertEqual(s["task"]["keys"], "Ctrl+C")
            s = chord(e, "Esc")
            self.assertNotEqual(active(s)["focus"], "url")
            s = chord(e, "Ctrl+C")
            self.assertEqual(s["solved"], 1)
            chord(e, "Ctrl+L")
            s = chord(e, "Ctrl+C")
            self.assertEqual(s["solved"], 2)
            self.assertTrue(s["scene"]["clipboard"].startswith("https://"))
        self.run_profile(check)

    def test_terminal_native_style_caret_delete_backspace_and_repeat(self):
        for desktop in ("ubuntu", "windows"):
            for erase, expected in (("Delete", "abXd"), ("Backspace", "aXcd")):
                with self.subTest(desktop=desktop, erase=erase):
                    def check(e):
                        s = begin(e, "terminal", desktop=desktop)
                        self.assertEqual(s["mission"], terminal_course(desktop))
                        enter(e, "abcd")
                        chord(e, "End")
                        chord(e, "Left")
                        s = chord(e, "Left")
                        self.assertEqual(s["scene"]["terminalInput"], "abcd")
                        self.assertEqual(active(s)["cursor"], 2)
                        chord(e, erase)
                        s = chord(e, "Shift+X", "X")
                        self.assertEqual((s["scene"]["terminalInput"], s["solved"]), (expected, 0))
                        self.assertEqual(active(s)["cursor"], expected.index("X") + 1)
                        chord(e, "End")
                        for _ in range(3):
                            s = e.call("lab-key", key="Backspace", pressed=True)
                        self.assertEqual(s["scene"]["terminalInput"], "a")
                        s = e.call("lab-key", key="Backspace", pressed=False)
                        self.assertEqual(s["solved"], 0)
                        # The failed command genuinely enters shell history.
                        # Recalling it must place the cursor at its real end.
                        enter(e, "bcd")
                        s = chord(e, "Enter")
                        self.assertEqual((s["solved"], s["scene"]["terminalInput"]), (0, ""))
                        s = chord(e, "Up")
                        self.assertEqual((s["scene"]["terminalInput"], active(s)["cursor"]), ("abcd", 4))
                        chord(e, "Left")
                        chord(e, "Left")
                        chord(e, erase)
                        s = chord(e, "Shift+X", "X")
                        self.assertEqual((s["scene"]["terminalInput"], active(s)["cursor"], s["solved"]),
                                         (expected, expected.index("X") + 1, 0))
                    self.run_profile(check, desktop)


if __name__ == "__main__":
    unittest.main()
