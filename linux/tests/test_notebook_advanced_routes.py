"""Validate actual learner actions and outputs in every new VS Code family."""
import json
import os
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

    def test_three_families_on_two_profiles_with_different_working_data(self):
        for platform in ("linux", "windows"):
            for level in range(3):
                for seed in (0, 1, 5):
                    with self.subTest(platform=platform, level=level, seed=seed):
                        self.play(platform, level, seed, restart=seed == 5, challenge=seed == 1)


if __name__ == "__main__":
    unittest.main()
