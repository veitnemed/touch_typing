"""Regression for the VS Code card, rather than a manually selected exercise."""
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_notebooks import chord_for
from test_project_file_practice import active, chord, disk

EXTRAS = ("code-notebook-", "code-project-file-")
BUDGET = 40
DEFAULT = dict(app="code", mode="sandbox", guidance="guided", desktopProfile="ubuntu",
               preserveDraft=True, softFinish=True, combinedLimits=True, nativeEnglishInput=True)
CATALOG = (
    {"code-guide", "code-first-fix", "code-scratch", "code-navigation",
     "code-go-definition", "code-rename-symbol", "code-search-error", "code-find-exact-edit",
     "code-line-edit", "code-notebook-read", "code-project-file-move"},
    {"code-refactor", "code-extract", "code-console", "code-symbols", "code-python-edit",
     "code-diagnostic-test", "code-notebook-repair", "code-project-file-copy"},
    {"code-handover", "code-repair", "code-inspection", "code-publish", "code-notebook-recheck"},
)


def supplement(mission):
    return mission.startswith(EXTRAS)


def enter(e, value):
    for c in value:
        chord(e, "Shift+" + c if c.isupper() else chord_for(c), c)


def begin(e, **options):
    e.call("lab-start", **{**DEFAULT, **options})
    return e.call("lab-resume")


def advance(e, state):
    task, mission, before = state["task"], state["mission"], state["solved"]
    task_id, keys, value = task["id"], task["keys"], task["text"]
    if task_id == "project-search-repair":
        chord(e, "End"); chord(e, "Backspace"); enter(e, "5")
    elif mission == "code-project-file-move" and task_id == "save":
        chord(e, "Ctrl+End"); enter(e, value)
    elif (not keys or task_id in ("code-rename-confirm", "project-search-submit") or
          mission.startswith("code-notebook-") and task_id in ("quick-choose", "save") or
          mission == "code-find-exact-edit" and task_id in ("quick-choose", "exact-find-submit", "save")):
        enter(e, value)
    if not keys and task_id not in ("notebook-type", "python-edit-write"):
        keys = "Enter"
    for _ in range(32 if keys in ("Down", "Tab") else int(bool(keys))):
        result = chord(e, keys)
        if result["solved"] > before:
            break
    if not keys:
        result = e.call("lab-state")
    assert result["solved"] == before + 1, (mission, task, result["status"], active(result).get("focus"))
    return result


class EditorCourseTests(unittest.TestCase):
    def test_card_default_spans_original_work_and_saves_inside_notebook(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as directory, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
                e = Engine(verify=True)
                try:
                    e.call("configure", platform=platform, layout="EN")
                    state = begin(e, editorCourse=True, seed=0)
                    self.assertEqual((state["mission"], state["phase"], state["editorCourse"],
                                      state["timeLimit"], state["actionLimit"]),
                                     ("code-guide", "Открыть функции сохранения задач", True, 0, BUDGET))
                    initial = state["scenarioLength"]
                    self.assertGreater(initial, 20)
                    seen = set()
                    while state["mission"] == "code-guide":
                        seen.add(state["task"]["id"])
                        state = advance(e, state)
                    self.assertTrue({"quick-open", "select-line", "copy", "paste", "save", "project-search", "explorer"} <= seen, seen)
                    self.assertEqual((state["solved"], state["mission"], state["finished"]),
                                     (initial, "code-notebook-read", False))
                    for _ in range(2):
                        state = advance(e, state)
                    self.assertTrue(active(state)["url"].endswith(".ipynb"))
                    workspace = (disk(state), active(state)["url"], state["solved"], state["task"])
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close(); e = Engine(verify=True)
                    e.call("configure", platform=platform, layout="EN")
                    state = begin(e, seconds=180, actionLimit=20, **{"continue": True})
                    self.assertEqual((disk(state), active(state)["url"], state["solved"], state["task"]), workspace)
                    self.assertEqual((state["editorCourse"], state["actionLimit"], state["timeLimit"]),
                                     (True, BUDGET, 0))
                    while not state["limitDecisionPending"]:
                        state = advance(e, state)
                    self.assertEqual((state["solved"], state["finished"], state["limitReached"]),
                                     (BUDGET, False, "actions"))
                    e.call("lab-limit-choice", choice="continue")
                    state = e.call("lab-resume")
                    while not state["finished"]:
                        state = advance(e, state)
                    self.assertGreater(state["solved"], BUDGET)
                    self.assertEqual(state["mission"], "code-notebook-read")
                    self.assertTrue(e.call("lab-result")["completed"])
                finally:
                    e.close()

    def test_previous_use_neither_promotes_notebook_nor_overwrites_a_manual_draft(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                # Recreate the prior profile state that recommended a notebook after a single credit.
                state = begin(e, mission="code-guide", seconds=0, actionLimit=0, seed=0)
                state = advance(e, state)
                checkpoint = (state["solved"], state["mission"], state["task"])
                self.assertTrue(e.call("lab-stop")["saved"])
                self.assertEqual(e.call("lab-next-mission", app="code")["mission"], "code-first-fix")
                fresh = begin(e, editorCourse=True, seed=0)
                self.assertEqual((fresh["mission"], fresh["actionLimit"], fresh["solved"]), ("code-first-fix", BUDGET, 0))
                advance(e, fresh)
                self.assertTrue(e.call("lab-stop")["saved"])
                previous = begin(e, **{"continue": True})
                self.assertEqual((previous["solved"], previous["mission"], previous["task"]), checkpoint)
                self.assertFalse(previous["editorCourse"])
                # Configure → choose a notebook explicitly remains short and stays a standalone project.
                manual = begin(e, editorCourse=True, mission="code-notebook-read", seconds=0, actionLimit=0, seed=0)
                self.assertFalse(manual["editorCourse"])
                while not manual["finished"]:
                    manual = advance(e, manual)
                self.assertEqual((manual["mission"], manual["solved"]), ("code-notebook-read", 9))
                self.assertTrue(e.call("lab-result")["completed"])
            finally:
                e.close()

    def test_every_established_and_supplemental_section_remains_in_course_and_catalog(self):
        for level, expected in enumerate(CATALOG):
            with self.subTest(level=level), tempfile.TemporaryDirectory() as directory, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
                e = Engine(verify=True)
                try:
                    e.call("configure", platform="linux", layout="EN")
                    missions = {m["id"] for m in e.call("lab-catalog")["missions"] if m["app"] == "code" and m["level"] == level}
                    self.assertEqual(missions, expected)
                    state = begin(e, level=level, editorCourse=True, actionLimit=0, seconds=0, seed=0)
                    self.assertFalse(supplement(state["mission"]))
                    seen = []
                    for _ in range(len(expected)):
                        current = state["mission"]
                        self.assertNotIn(current, seen)
                        self.assertFalse(seen and supplement(seen[-1]) and supplement(current))
                        seen.append(current)
                        while state["mission"] == current:
                            state = advance(e, state)
                            self.assertFalse(state["finished"])
                    self.assertEqual(set(seen), expected)
                    self.assertIn(state["mission"], expected)
                finally:
                    e.close()


if __name__ == "__main__":
    unittest.main()
