"""Validate actual learner actions and outputs in every new VS Code family."""
import gzip
import json
import os
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_notebooks import text
from test_python_editor import editor, gesture

ROUTES = ("code-notebook-structure", "code-notebook-clean-rerun", "code-project-file-csv-notebook")
BASELINE = {"code-notebook-read", "code-notebook-repair", "code-notebook-recheck", "code-project-file-move", "code-project-file-copy"}
CSV_EXPECTED = {"warehouse-count": "38", "delivery-minutes": "26", "sample-duration": "16"}


def filemap(state):
    return {f["path"]: f["content"] for f in state["scene"]["terminal"]["files"] if "/task-list/" in f["path"]}


def find_and_replace(e, old, new):
    gesture(e, "Ctrl+F")
    gesture(e, "Ctrl+A")
    text(e, old)
    gesture(e, "Enter")
    gesture(e, "Esc")
    s = e.call("lab-state")
    w = editor(s)
    lo, hi = sorted((w["bodyCursor"], w["bodyAnchor"]))
    if w["body"][lo:hi] != old:
        raise AssertionError(("failed to select", old, w["body"][lo:hi], w["focus"]))
    return text(e, new)


def step(e, s):
    t = s["task"]
    if t["id"] == "notebook-delete-cell":
        armed = gesture(e, "D")
        assert armed["solved"] == s["solved"]
        return gesture(e, "D")
    if t["id"] == "file-next":
        for _ in range(30):
            s2 = gesture(e, t["keys"])
            if s2["solved"] > s["solved"]:
                return s2
        return s2
    if t["text"] and t["id"] in {"quick-choose", "palette-choose", "notebook-type", "file-name-commit"}:
        s = text(e, t["text"])
        if t["id"] == "file-name-commit":
            return gesture(e, "Enter")
    if t["id"] == "save" and t["text"]:
        if "from csv import" in t["text"]:
            original = editor(s)["body"]
            proposed = t["text"]
            old_path, new_path = (re.search(r'data/[a-z-]+\.csv', body)[0] for body in (original, proposed))
            old_calc, new_calc = (re.search(r'return [a-z]+\(values\)', body)[0] for body in (original, proposed))
            find_and_replace(e, old_path, new_path)
            find_and_replace(e, old_calc, new_calc)
        else:
            s = text(e, t["text"])
    if t["keys"]:
        s = gesture(e, t["keys"])
    return s


class ExpandedNotebookRoutes(unittest.TestCase):
    def test_native_preview7_quick_open_typo_replays_its_existing_scratch_tab(self):
        fixture = Path(__file__).with_name("fixtures") / "preview7-original-quick-open-typo.xml.gz"
        with tempfile.TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
            profile = Path(root) / "TouchTypingStudio-QA"
            profile.mkdir()
            (profile / "progress.xml").write_bytes(gzip.decompress(fixture.read_bytes()))

            e = Engine(verify=True)
            try:
                e.call("configure", platform="windows", layout="EN")
                e.call("lab-start", app="code", mode="sandbox", guidance="guided", level=2,
                       desktopProfile="ubuntu", routeKind="mission", nativeEnglishInput=True,
                       **{"continue": True})
                restored = e.call("lab-resume")
                self.assertEqual((restored["solved"], restored["errors"], restored["task"]["id"], editor(restored)["url"]),
                                 (1, 0, "quick-choose", "incoming/waehouse-"))
                before = gesture(e, "Ctrl+P")
                tabs = [tab["url"] for tab in editor(before)["tabs"]]
                text(e, "incoming/no-file?")
                rejected = gesture(e, "Enter")
                self.assertEqual((rejected["solved"], editor(rejected)["focus"], editor(rejected)["url"]),
                                 (1, "quick-open", "incoming/waehouse-"))
                self.assertEqual([tab["url"] for tab in editor(rejected)["tabs"]], tabs)

                gesture(e, "Ctrl+A")
                text(e, rejected["task"]["text"])
                opened = gesture(e, "Enter")
                self.assertEqual((opened["solved"], editor(opened)["url"], filemap(opened)),
                                 (2, "/home/student/project/task-list/incoming/warehouse-count.csv", filemap(before)))
            finally:
                e.close()

    def test_quick_open_typo_stays_in_picker_preserving_the_scene_and_draft(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as root, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                config = dict(app="code", mode="sandbox", level=2, mission=ROUTES[2], seed=1,
                              actionLimit=0, seconds=0, guidance="guided", nativeEnglishInput=True)
                e = Engine(verify=True)
                try:
                    e.call("configure", platform=platform, layout="EN")
                    e.call("lab-start", **config)
                    self.assertFalse(e.call("lab-resume")["paused"])
                    opened = gesture(e, "Ctrl+P")
                    expect = opened["task"]["text"]
                    typo = expect[:5] + "?" + expect[5:]
                    w = editor(opened)
                    tabs = [tab["url"] for tab in w["tabs"]]
                    original = filemap(opened)
                    text(e, typo)
                    refused = gesture(e, "Enter")
                    view = editor(refused)
                    self.assertEqual((view["focus"], view["input"], view["entries"], view["url"]),
                                     ("quick-open", typo, [], w["url"]))
                    self.assertEqual([tab["url"] for tab in view["tabs"]], tabs)
                    self.assertEqual((refused["solved"], filemap(refused)), (1, original))
                    self.assertIn("Совпадений нет", refused["status"])
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close()
                    e = Engine(verify=True)
                    e.call("configure", platform=platform, layout="EN")
                    e.call("lab-start", **(config | {"continue": True}))
                    restored = e.call("lab-resume")
                    self.assertEqual((restored["solved"], editor(restored)["focus"], editor(restored)["input"]),
                                     (1, "quick-open", typo))
                    self.assertEqual([tab["url"] for tab in editor(restored)["tabs"]], tabs)
                    gesture(e, "Ctrl+A")
                    before_return = text(e, expect)
                    self.assertEqual(len(editor(before_return)["entries"]), 1)
                    opened = gesture(e, "Enter")
                    self.assertEqual((opened["solved"], editor(opened)["focus"], filemap(opened)), (2, "body", original))
                    self.assertIn("/incoming/" + expect.split("/")[-1], editor(opened)["url"])
                finally:
                    e.close()

    def play(self, platform, level, seed, restart=False, challenge=False):
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform=platform, layout="EN")
                catalog = e.call("lab-catalog")
                advertised = {m["id"] for m in catalog["missions"] if m["app"] == "code"}
                self.assertTrue((BASELINE | set(ROUTES)).issubset(advertised))
                config = dict(app="code", mode="sandbox", level=level, mission=ROUTES[level], seed=seed,
                              actionLimit=0, seconds=0, guidance="guided", nativeEnglishInput=True)
                s = e.call("lab-start", **config)
                s = e.call("lab-resume")
                original = filemap(s)
                expected_count = [28, 23, 39][level]
                self.assertEqual(s["scenarioLength"], expected_count)
                seen_conflict, seen_stale, seen_old, jumped, rebuilt = False, False, "", "", False
                for index in range(expected_count):
                    before, task = s["solved"], s["task"]
                    if level == 2 and task["id"] == "code-definition" and challenge:
                        gesture(e, "Esc")
                        refused = gesture(e, "F12")
                        self.assertEqual(refused["solved"], before)
                        self.assertTrue(editor(refused)["url"].endswith(".ipynb"))
                        gesture(e, "Enter")
                    if level == 2 and task["id"] == "save" and "from csv import" in task["text"] and challenge:
                        original_op = re.search(r'return [a-z]+\(values\)', editor(s)["body"])[0]
                        find_and_replace(e, original_op, "return 38")
                        bad = gesture(e, "Ctrl+S")
                        self.assertEqual(bad["solved"], before)
                        # Restore only the test detour before making the branch's real edit.
                        find_and_replace(e, "return 38", original_op)
                        s = gesture(e, "Ctrl+S")
                        self.assertEqual(s["solved"], before)
                    s = step(e, s)
                    self.assertEqual(s["solved"], before + 1,
                                     (platform, level, seed, index, task, s["status"], editor(s)["focus"], editor(s)["status"]))
                    w = editor(s)
                    if level == 2 and task["text"] == "!conflict":
                        seen_conflict = True
                        self.assertIn("Ничего не заменено", w["status"])
                        self.assertEqual(filemap(s), original)
                    if level == 2 and task["id"] == "file-paste" and task["text"] != "!conflict" and not rebuilt:
                        if restart:
                            s = e.call("lab-pause")
                            e.close()
                            e = Engine(verify=True)
                            e.call("configure", platform=platform, layout="EN")
                            restored = e.call("lab-start", **(config | {"continue": True}))
                            self.assertEqual((restored["solved"], editor(restored)["url"], filemap(restored)),
                                             (s["solved"], w["url"], filemap(s)))
                            s = e.call("lab-resume")
                        rebuilt = True
                    if level == 2 and task["id"] == "notebook-run":
                        n = w["notebook"]
                        if n and n["selected"] == 2:
                            seen_old = n["cells"][2]["output"]
                    if level == 2 and task["id"] == "code-definition":
                        jumped = w["url"]
                        self.assertTrue(jumped.endswith("_metrics.py") and not jumped.endswith("archive_metrics.py"))
                        self.assertEqual(w["body"][w["bodyCursor"]-4:w["bodyCursor"]], "def ")
                    if level == 2 and task["id"] == "code-back":
                        n = w["notebook"]
                        self.assertEqual((n["selected"], n["editing"], n["caret"]), (2, True, 0))
                        self.assertTrue(n["cells"][2]["stale"], n)
                        seen_stale = True
                self.assertTrue(s["finished"], s["status"])
                final = filemap(s)
                if level == 2:
                    nb_name = next(path for path in final if "/verified/" in path)
                    stem = nb_name.rsplit("/", 1)[-1].removesuffix("-verification.ipynb")
                    n = editor(s)["notebook"]
                    self.assertTrue(seen_conflict and seen_stale and rebuilt and jumped)
                    self.assertEqual(n["cells"][2]["output"], CSV_EXPECTED[stem])
                    self.assertNotEqual(seen_old, n["cells"][2]["output"])
                    self.assertEqual(n["cells"][2]["outputLabel"], "Сохранённый вывод")
                    self.assertEqual(n["kernel"]["counter"], 0)
                    base = nb_name.split("/verified/")[0] + "/"
                    self.assertNotIn(base + stem + "-verification.ipynb", final)
                    self.assertNotIn(base + "incoming/" + stem + ".csv", final)
                    self.assertEqual(final[base + "data/" + stem + ".csv"], original[base + "data/" + stem + ".csv"])
                    self.assertEqual(final[base + "data/" + stem + "-updated.csv"], original[base + "incoming/" + stem + ".csv"])
                    self.assertEqual(final[base + "archive_metrics.py"], original[base + "archive_metrics.py"])
                else:
                    report = next((text for path, text in final.items() if path.endswith("-result.txt")))
                    notebook = json.loads(next(text for path, text in final.items() if path.endswith(".ipynb")))
                    self.assertEqual(notebook["cells"][2]["outputs"][0]["data"]["text/plain"], [report])
                    if level == 0:
                        self.assertEqual([c["id"] for c in notebook["cells"][:4]], ["brief", "dataset", "metric", "source-note"])
                        self.assertEqual(len(notebook["cells"]), 5)
                        self.assertEqual(notebook["cells"][3]["cell_type"], "markdown")
            finally:
                e.close()

    @staticmethod
    def csv_options(guidance):
        return dict(app="code", mode="sandbox", mission=ROUTES[2], level=2, seed=0, seconds=0,
                    actionLimit=0, guidance=guidance, routeKind="mission", nativeEnglishInput=True)

    @staticmethod
    def csv_start(e, guidance):
        e.call("lab-start", **ExpandedNotebookRoutes.csv_options(guidance))
        e.call("lab-resume")
        gesture(e, "Ctrl+P")
        text(e, "incoming/sample")  # real partial path, not the compiler's entire hint
        opened = gesture(e, "Enter")
        assert editor(opened)["url"].endswith("/incoming/sample-duration.csv"), opened["task"]
        return gesture(e, "Ctrl+Shift+E")

    @staticmethod
    def selected_path(s):
        w = editor(s)
        return w["entries"][w["item"]]["path"].partition("/task-list/")[2]

    def test_independent_can_use_up_after_inspecting_other_folder(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as profile, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
                e = Engine(verify=True)
                try:
                    e.call("configure", platform=platform, layout="EN")
                    s = self.csv_start(e, "independent")
                    original = filemap(s)
                    for key, path in (("Down", "data"), ("Enter", "data"), ("Down", "data/sample-duration.csv")):
                        s = gesture(e, key)
                        self.assertEqual((s["solved"], self.selected_path(s)), (3, path))
                    # Inspect the same-named archived CSV first, with exact keyboard input.
                    archived = gesture(e, "Enter")
                    self.assertEqual(archived["solved"], 3)
                    self.assertTrue(editor(archived)["url"].endswith("/data/sample-duration.csv"))
                    gesture(e, "Ctrl+Shift+E")
                    for key, count, path in (("Down", 4, "incoming"), ("Enter", 5, "incoming"),
                                             ("Down", 6, "incoming/sample-duration.csv"),
                                             ("Ctrl+X", 7, "incoming/sample-duration.csv")):
                        s = gesture(e, key)
                        self.assertEqual((s["solved"], self.selected_path(s)), (count, path), (key, s["status"]))
                    # Neither selecting the origin nor pasting into it is the data goal.
                    wrong = gesture(e, "Up")
                    self.assertEqual((wrong["solved"], self.selected_path(wrong)), (7, "incoming"))
                    wrong = gesture(e, "Ctrl+V")
                    self.assertEqual((wrong["solved"], filemap(wrong)), (7, original))
                    adjacent = gesture(e, "Up")
                    self.assertEqual((adjacent["solved"], self.selected_path(adjacent)), (7, "data/sample-duration.csv"))
                    target = gesture(e, "Up")
                    self.assertEqual((target["solved"], self.selected_path(target), target["task"]["text"]),
                                     (8, "data", "!conflict"))
                    attempt = e.call("lab-result")["series"][-1]
                    self.assertEqual((attempt["key"], attempt["task"], attempt["correct"]), ("Up", "file-prev", True))
                    s = gesture(e, "Ctrl+V")
                    self.assertEqual((s["solved"], filemap(s)), (9, original))
                    self.assertIn("Ничего не заменено", editor(s)["status"])
                    self.assertTrue(s["phase"].startswith("Разрешите конфликт"))
                    s = gesture(e, "Ctrl+V")
                    self.assertEqual((s["solved"], filemap(s)), (9, original))
                    # The detour opened a different tab and left data expanded. Complete
                    # the remaining keyboard actions without resetting either state.
                    while not s["finished"]:
                        count, task = s["solved"], s["task"]
                        s = step(e, s)
                        self.assertEqual(s["solved"], count + 1, (task, s["status"], editor(s)["status"]))
                    self.assertEqual(s["solved"], 39)
                    nb = editor(s)["notebook"]
                    self.assertEqual(nb["cells"][2]["output"], "16")
                    self.assertTrue(editor(s)["url"].endswith("/verified/sample-duration-verification.ipynb"))
                    self.assertEqual(filemap(s)[next(path for path in original if path.endswith("/data/sample-duration.csv"))],
                                     original[next(path for path in original if path.endswith("/data/sample-duration.csv"))])
                finally:
                    e.close()

    def test_guided_rejects_up_and_conflict_requires_a_new_physical_paste(self):
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                s = self.csv_start(e, "guided")
                original = filemap(s)
                while s["solved"] < 7:
                    s = step(e, s)
                gesture(e, "Up")
                s = gesture(e, "Up")
                self.assertEqual((s["solved"], self.selected_path(s), s["task"]["keys"]), (7, "data", "Down"))
                s = step(e, s)
                self.assertEqual((s["solved"], s["task"]["text"]), (8, "!conflict"))
                # A real pointer-triggered conflict is visible but does not
                # demonstrate the requested Ctrl+V. It leaves the same state.
                s = e.call("lab-explorer-action", action="paste", path="")
                self.assertEqual((s["solved"], filemap(s)), (8, original))
                self.assertIn("Ничего не заменено", editor(s)["status"])
                # Ctrl+V in the editor cannot earn a file operation even with
                # that old conflict still present in the Explorer model.
                gesture(e, "Ctrl+1")
                s = gesture(e, "Ctrl+V")
                self.assertEqual((s["solved"], editor(s)["focus"], filemap(s)), (8, "body", original))
                gesture(e, "Ctrl+Z")
                gesture(e, "Ctrl+Shift+E")
                s = gesture(e, "Ctrl+V")
                self.assertEqual((s["solved"], filemap(s)), (9, original))
                self.assertIn("Ничего не заменено", editor(s)["status"])
            finally:
                e.close()

    def test_preview7_draft_with_uncredited_conflict_keeps_its_progress_then_recovers(self):
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
            fixture = Path(__file__).with_name("fixtures") / "preview7-original-csv-context.xml.gz"
            target = Path(profile) / "TouchTypingStudio-QA" / "progress.xml"
            target.parent.mkdir()
            target.write_bytes(gzip.decompress(fixture.read_bytes()))
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                options = self.csv_options("independent") | {"continue": True}
                s = e.call("lab-start", **options)
                self.assertEqual((s["solved"], s["scenarioStep"], self.selected_path(s), s["task"]["id"]),
                                 (7, 8, "data", "file-next"))
                original = filemap(s)
                self.assertIn("Ничего не заменено", editor(s)["status"])
                e.call("lab-resume")
                gesture(e, "Down")
                s = gesture(e, "Up")
                self.assertEqual((s["solved"], self.selected_path(s), s["task"]["text"]), (8, "data", "!conflict"))
                s = gesture(e, "Ctrl+V")
                self.assertEqual((s["solved"], filemap(s)), (9, original))
                e.call("lab-pause")
                e.close()
                e = Engine(verify=True)
                e.call("configure", platform="linux", layout="EN")
                restored = e.call("lab-start", **options)
                self.assertEqual((restored["solved"], restored["scenarioStep"], restored["task"], filemap(restored)),
                                 (9, 10, s["task"], original))
            finally:
                e.close()

    def test_jump_history_tracks_the_actual_relocated_notebook_path(self):
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="windows", layout="EN")
                e.call("lab-start", app="code", mode="sandbox", mission=ROUTES[2], level=2, seed=1,
                       seconds=0, actionLimit=0, nativeEnglishInput=True)
                s = e.call("lab-resume")
                while s["task"]["id"] != "code-definition":
                    s = step(e, s)
                # Leave a nonzero caret in the original import-referenced call.
                s = gesture(e, "Right")
                original = editor(s)["url"]
                previous = editor(s)["notebook"]
                s = step(e, s)  # F12
                saved = filemap(s)[original]
                e.call("lab-explorer-action", action="new-folder")
                text(e, "other-review")
                gesture(e, "Enter")
                folder = original.rsplit("/", 1)[0] + "/other-review"
                e.call("lab-explorer-action", action="cut", path=original)
                s = e.call("lab-explorer-action", action="paste", path=folder)
                target = folder + "/" + original.rsplit("/", 1)[-1]
                self.assertEqual(filemap(s)[target], saved)
                self.assertNotIn(original, filemap(s))
                gesture(e, "Ctrl+1")
                s = gesture(e, "Alt+Left")
                self.assertEqual(editor(s)["url"], target)
                actual = editor(s)["notebook"]
                self.assertEqual((actual["selected"], actual["caret"], actual["editing"]),
                                 (previous["selected"], previous["caret"], previous["editing"]))
            finally:
                e.close()

    def test_three_families_on_two_profiles_with_different_working_data(self):
        for platform in ("linux", "windows"):
            for level in range(3):
                for seed in (0, 1, 5):
                    with self.subTest(platform=platform, level=level, seed=seed):
                        self.play(platform, level, seed, restart=seed == 5, challenge=seed == 1)


if __name__ == "__main__":
    unittest.main()
