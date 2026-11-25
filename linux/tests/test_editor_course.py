"""Regression for the VS Code card, rather than a manually selected exercise."""
from copy import deepcopy
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


def checkpoint(state):
    """Compare learner state, not the process-local identity of a tab widget."""
    scene = state["scene"]
    restored = deepcopy({name: state[name] for name in (
        "solved", "editorCourse", "mission", "task", "phase", "actionLimit", "timeLimit",
        "scenarioStep", "scenarioLength", "scenarioCompletedGoals", "objectives")})
    restored.update(windows=deepcopy(scene["windows"]), terminal=deepcopy(scene["terminal"]))
    for w in restored["windows"]:
        for tab in w["tabs"]:
            del tab["id"]
        if w.get("otherTab"):
            del w["otherTab"]["id"]
    return restored


def first_difference(before, after, path="state"):
    if isinstance(before, dict) and isinstance(after, dict):
        for key in before.keys() | after.keys():
            if key not in before or key not in after:
                return f"{path}.{key}: field missing on one side"
            difference = first_difference(before[key], after[key], f"{path}.{key}")
            if difference:
                return difference
    elif isinstance(before, list) and isinstance(after, list):
        if len(before) != len(after):
            return f"{path}: count {len(before)} → {len(after)}"
        for index, (old, new) in enumerate(zip(before, after)):
            difference = first_difference(old, new, f"{path}[{index}]")
            if difference:
                return difference
    elif before != after:
        return f"{path}: {before!r:.320} → {after!r:.320}"
    return None


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
                    # Preserve a real non-zero caret and selection inside the first cell.
                    for key in ("Enter", "End", "Shift+Left"):
                        state = chord(e, key)
                    notebook = active(state)["notebook"]
                    self.assertEqual((state["solved"], notebook["selected"], notebook["editing"]), (35, 0, True))
                    self.assertGreater(notebook["caret"], 0)
                    self.assertEqual(notebook["anchor"], notebook["caret"] + 1)
                    workspace = checkpoint(state)
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close(); e = Engine(verify=True)
                    e.call("configure", platform=platform, layout="EN")
                    state = begin(e, seconds=180, actionLimit=20, **{"continue": True})
                    resumed = checkpoint(state)
                    self.assertEqual(resumed, workspace, first_difference(workspace, resumed))
                    self.assertEqual((state["editorCourse"], state["actionLimit"], state["timeLimit"]),
                                     (True, BUDGET, 0))
                    state = chord(e, "Esc")
                    self.assertFalse(active(state)["notebook"]["editing"])
                    self.assertEqual(state["solved"], 35)
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
