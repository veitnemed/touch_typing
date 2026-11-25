"""Keyboard evidence for Tab Search and the exact source retained in a review."""
import os
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import enter
from test_project_file_practice import active, chord, disk

MISSION = "chrome-open-tab-review"
BASICS_MISSION = "chrome-open-tab-review-basics"
MISSIONS = frozenset((MISSION, BASICS_MISSION))
ARTIFACT = "/home/student/project/source-review.txt"
ROOT = "https://practice.local/tab-review/"


def review_options(seed=1, platform="ubuntu", **custom):
    return dict(app="chrome", level=1, mission=MISSION, routeKind="mission", desktopProfile=platform,
                mode="sandbox", guidance="guided", actionLimit=0, seconds=0,
                preserveDraft=True, softFinish=True, combinedLimits=True, nativeEnglishInput=True,
                seed=seed) | custom


def start(engine, **kwargs):
    engine.call("lab-start", **review_options(**kwargs))
    return engine.call("lab-resume")


def documents(s):
    tabs = active(s)["tabs"]
    current = next(tab for tab in tabs if tab["url"].endswith("/current"))
    archive = next(tab for tab in tabs if tab["url"].endswith("/archive"))
    slot = next(i for i, tab in enumerate(tabs, 1) if tab["url"].endswith("/handoff"))
    code = current["body"].split("control-code: ", 1)[1].splitlines()[0]
    return current, archive, slot, code


def advance_tabs(engine, state):
    goal = state["task"]
    current, _, _, code = documents(state)
    if goal["id"] == "browser-tabs-choose":
        visible = active(state)
        if visible["input"]:
            chord(engine, "Ctrl+A")
        enter(engine, current["url"].split("/")[-2])
        for _ in range(len(visible["tabs"])):
            visible = active(engine.call("lab-state"))["tabSearch"]
            if visible["rows"][visible["selected"]]["url"] == current["url"]:
                break
            chord(engine, "Down")
    if goal["id"] == "browser-review-read":
        enter(engine, "control-code")
    if goal["id"] == "browser-review-submit":
        enter(engine, code)
    result = chord(engine, goal["keys"])
    assert result["solved"] == state["solved"] + 1, (goal, result["status"], active(result))
    return result


class ChromeTabSearch(unittest.TestCase):
    def clean(self, callback, profile="ubuntu"):
        with TemporaryDirectory() as data, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": data}):
            e = Engine(verify=True)
            e.call("configure", platform="windows" if profile == "windows" else "linux", layout="EN")
            try:
                return callback(e)
            finally:
                e.close()

    def test_three_materials_on_both_profiles_and_real_saved_artifact(self):
        for profile in ("ubuntu", "windows"):
            with self.subTest(profile=profile):
                def run(e):
                    seen = {}
                    for seed in range(12):
                        s = start(e, seed=seed, platform=profile)
                        current, archive, slot, code = documents(s)
                        context = current["url"], tuple(tab["url"] for tab in active(s)["tabs"])
                        if context[0] in seen:
                            continue
                        self.assertEqual(len(context[1]), 8)
                        self.assertNotEqual(current["body"], archive["body"])
                        self.assertNotEqual(context[1].index(current["url"])+1, slot)
                        while not s["finished"]:
                            s = advance_tabs(e, s)
                        receipt = "source=" + current["url"] + "\ncontrol-code=" + code + "\n"
                        self.assertEqual((s["solved"], disk(s)[ARTIFACT]), (10, receipt))
                        self.assertEqual(active(s)["sourceReview"]["saved"], receipt)
                        self.assertEqual(e.call("lab-result")["sourceReview"], {"url":current["url"], "code":code})
                        seen[context[0]] = slot, code
                    self.assertEqual(set(seen.values()), {(5,"041"),(2,"237"),(8,"802")})
                self.clean(run, profile)

    def test_empty_search_archive_wrong_context_or_code_do_not_verify(self):
        def run(e):
            s = start(e)
            current, archive, slot, code = documents(s)
            s = chord(e, "Ctrl+Shift+A")
            self.assertEqual((s["solved"], active(s)["focus"]), (1, "tab-search"))
            enter(e, "not-in-the-open-tabs")
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], active(s)["tabSearch"]["rows"]), (1, []))
            self.assertIn("нет", s["status"].lower())
            self.assertTrue(active(s)["url"].endswith("/request"))
            # Filter by host, but no active source matches a wrong destination.
            chord(e, "Ctrl+A")
            enter(e, "archive")
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], active(s)["url"]), (1, archive["url"]))
            self.assertIn("архив", s["status"].lower())
            s = chord(e, "Ctrl+Shift+A")
            while s["solved"] < 7:
                s = advance_tabs(e, s)
            wrong_slot = "Ctrl+4" if s["task"]["keys"] != "Ctrl+4" else "Ctrl+3"
            s = chord(e, wrong_slot)
            self.assertEqual(s["solved"], 7)
            decoy = active(s)["url"]
            self.assertNotEqual(decoy, current["url"])
            chord(e, "Ctrl+L")
            chord(e, "Ctrl+C")
            s = chord(e, "Ctrl+"+str(slot))
            s = chord(e, "Tab")
            self.assertEqual((s["solved"], active(s)["focus"]), (9,"source-review"))
            enter(e, code)
            s = chord(e, "Enter")
            self.assertEqual(s["solved"], 9)
            self.assertNotIn(ARTIFACT, disk(s))
            self.assertIn("адрес в буфере", s["status"].lower())
            # Recover the exact original source without restarting the mission.
            chord(e, "Ctrl+Shift+A")
            enter(e, current["url"])
            chord(e, "Enter")
            chord(e, "Ctrl+L")
            chord(e, "Ctrl+C")
            s = chord(e, "Ctrl+"+str(slot))
            s = chord(e, "Tab")
            self.assertEqual(s["solved"], 9)
            enter(e, code[::-1])
            s = chord(e, "Enter")
            self.assertEqual((s["solved"], s["finished"]), (9,False))
            self.assertNotIn(ARTIFACT, disk(s))
            self.assertIn("код не совпадает", s["status"].lower())
            chord(e,"Ctrl+A")
            s = advance_tabs(e, e.call("lab-state"))
            self.assertEqual(s["solved"], 10)
            self.assertTrue(s["finished"])
        self.clean(run)

    def test_numeric_positions_and_click_does_not_claim_the_shortcut(self):
        def run(e):
            s = start(e)
            current, _, _, _ = documents(s)
            initial = [(t["id"], t["url"]) for t in active(s)["tabs"]]
            for number in range(2, 9):
                s = chord(e, f"Ctrl+{number}")
                self.assertEqual((0, number-1), (s["solved"], active(s)["activeTab"]))
            s = chord(e, "Ctrl+1")
            s = chord(e, "Ctrl+L")
            s = e.call("lab-browser-tab-search", action="open", index="")
            self.assertEqual((s["solved"], active(s)["focus"]), (0, "tab-search"))
            s = chord(e, "Esc")
            self.assertEqual((s["solved"], active(s)["focus"], active(s)["selected"]), (0,"url",True))
            s = chord(e, "Ctrl+Shift+A")
            self.assertEqual(s["solved"], 1)
            s = e.call("lab-browser-tab-search", action="select", index="900")
            self.assertEqual(active(s)["focus"], "tab-search")
            enter(e, current["url"].split("/")[-2])
            s = e.call("lab-state")
            index = next(i for i, row in enumerate(active(s)["tabSearch"]["rows"]) if row["url"] == current["url"])
            s = e.call("lab-browser-tab-search", action="select", index=str(index))
            self.assertEqual((s["solved"], active(s)["url"]), (1, current["url"]))
            self.assertEqual(initial, [(t["id"],t["url"]) for t in active(s)["tabs"]])
            s = chord(e, "Ctrl+Shift+A")
            s = advance_tabs(e, s)
            self.assertEqual(s["solved"], 2)
        self.clean(run)

    def test_restores_filtered_row_and_receipt_only_after_actual_submit(self):
        with TemporaryDirectory() as data, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": data}):
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                s = start(e, guidance="independent")
                self.assertFalse(s["showGuide"])
                current, _, _, _ = documents(s)
                s = chord(e,"Ctrl+Shift+A")
                enter(e,current["url"].split("/")[-2])
                s = chord(e,"Down")
                expected = active(s)["tabSearch"]
                exact = active(s)["input"]
                self.assertTrue(e.call("lab-stop")["saved"])
            finally:
                e.close()
            e = Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                s = start(e, guidance="independent", **{"continue": True})
                self.assertEqual((s["solved"], active(s)["tabSearch"],active(s)["input"]), (1,expected,exact))
                s = advance_tabs(e,s)
                self.assertEqual(active(s)["url"],current["url"])
                self.assertNotIn(ARTIFACT,disk(s))
            finally:
                e.close()


if __name__ == "__main__":
    unittest.main()
