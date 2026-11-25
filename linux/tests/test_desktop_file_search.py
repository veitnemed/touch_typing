"""Scoped file lookup and handoff through the real engine protocol, not MockScene."""

import os
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_project_file_practice import active, chord
from test_live_route_policy import advance


APPROVED_ROOT = "/home/student/project/casework/approved/"
ARCHIVE_ROOT = "/home/student/project/casework/archive/"
HANDOFF_ROOT = "/home/student/project/casework/handoff/"
OUTSIDE_ROOT = "/home/student/Downloads/"


def enter_digits(engine, value, layout="EN"):
    for char in value:
        engine.call("lab-key", key=char, pressed=True, text=char, nativeLayout=layout)
        state = engine.call("lab-key", key=char, pressed=False)
    return state


def contents(state):
    return {file["path"]: file["content"] for file in state["scene"]["terminal"]["files"]}


class DesktopFileSearchTests(unittest.TestCase):
    def test_variants_restore_and_keep_wrong_source_out_of_the_handoff(self):
        variants = set()
        for profile in ("ubuntu", "windows"):
            for seed in range(6):
                with self.subTest(profile=profile, seed=seed), TemporaryDirectory() as root, \
                        patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                    opts = dict(app=profile, desktopProfile=profile, mode="sandbox", level=0, seconds=0,
                                actionLimit=0, guidance="guided", preserveDraft=True,
                                nativeEnglishInput=True, routeKind="mission", seed=seed)
                    mission = profile + "-file-search-handoff"
                    e = Engine(verify=True)
                    try:
                        e.call("configure", platform="linux" if profile == "ubuntu" else "windows", layout="RU")
                        catalog = e.call("lab-catalog", desktopProfile=profile)
                        entry = next(item for item in catalog["missions"] if item["id"] == mission)
                        self.assertTrue(entry["curated"])
                        shortcuts = next(item for item in catalog["classes"] if item["id"] == profile)["shortcuts"]
                        main = next(task for task in shortcuts if task["id"] == "file-search-open")
                        key = "Ctrl+E" if profile == "windows" else "Ctrl+F"
                        self.assertEqual(main["keys"], key)
                        e.call("lab-start", mission=mission, **opts)
                        s = e.call("lab-resume")
                        while s["task"]["id"] != "file-search-open":
                            s = advance(e, s)
                        self.assertEqual(s["task"]["keys"], key)
                        if profile == "windows":
                            wrong = chord(e, "Ctrl+F")
                            self.assertEqual((wrong["solved"], active(wrong)["focus"]), (s["solved"], "file-search"))
                            closed = chord(e, "Esc")
                            self.assertEqual(active(closed)["focus"], "body")
                        s = chord(e, key)
                        self.assertEqual((s["task"]["id"], active(s)["cursor"]), ("file-search-query", 0))
                        number = s["task"]["text"]
                        variants.add((profile, number))
                        mismatch = enter_digits(e, "000", "RU")
                        self.assertEqual(mismatch["solved"], s["solved"])
                        self.assertEqual(active(mismatch)["entries"], [])
                        e.call("lab-key", key="Backspace", pressed=True)
                        e.call("lab-key", key="Backspace", pressed=True)  # genuine repeat in a text field
                        e.call("lab-key", key="Backspace", pressed=False)
                        self.assertEqual(active(e.call("lab-state"))["input"], "0")
                        chord(e, "Ctrl+A")
                        partial = enter_digits(e, number[:2], "RU")
                        self.assertEqual(active(partial)["input"], number[:2])
                        self.assertEqual(partial["solved"], s["solved"])
                        self.assertTrue(e.call("lab-stop")["saved"])
                    finally:
                        e.close()

                    e = Engine(verify=True)
                    try:
                        resume_opts = {k: v for k, v in opts.items() if k != "routeKind"}
                        restored = e.call("lab-start", **resume_opts, **{"continue": True})
                        w = active(restored)
                        self.assertEqual((restored["solved"], w["focus"], w["input"], w["cursor"], w["folder"]),
                                         (s["solved"], "file-search", number[:2], 2, "/home/student/project/casework"))
                        e.call("lab-resume")
                        matched = enter_digits(e, number[-1], "RU")
                        w = active(matched)
                        self.assertEqual(matched["solved"], s["solved"] + 1)
                        self.assertEqual(len(w["entries"]), 2)
                        original = next(row["path"] for row in w["entries"] if row["path"].startswith(APPROVED_ROOT))
                        draft = next(row["path"] for row in w["entries"] if row["path"].startswith(ARCHIVE_ROOT))
                        name = original.rsplit("/", 1)[1]
                        before = contents(matched)
                        self.assertIn(OUTSIDE_ROOT + name, before)
                        self.assertNotEqual(before[original], before[OUTSIDE_ROOT + name])
                        decoy = chord(e, "Down")
                        self.assertEqual(decoy["solved"], matched["solved"])
                        self.assertEqual(active(decoy)["entries"][0]["path"], draft)
                        wrong = chord(e, "Ctrl+C")
                        self.assertEqual(wrong["solved"], matched["solved"])
                        self.assertEqual(wrong["scene"]["clipboard"], draft)
                        s = chord(e, "Down")
                        self.assertEqual(s["solved"], matched["solved"] + 1)
                        s = chord(e, "Ctrl+C")
                        self.assertEqual(s["scene"]["clipboard"], original)
                        while s["task"]["id"] != "file-paste":
                            s = advance(e, s)
                        s = chord(e, "Ctrl+V")
                        expected = dict(before, **{HANDOFF_ROOT + name: before[original]})
                        self.assertEqual(contents(s), expected)
                        while s["task"]["id"] != "find":
                            s = advance(e, s)
                        final = active(s)
                        self.assertEqual((final["app"], final["url"], final["body"]),
                                         ("code", HANDOFF_ROOT + name, before[original]))
                        s = chord(e, "Ctrl+F")
                        self.assertEqual(s["task"]["id"], "file-search-verify")
                        enter_digits(e, "00")
                        wrong = chord(e, "Enter")
                        self.assertEqual(wrong["solved"], s["solved"])
                        chord(e, "Ctrl+A")
                        value = s["task"]["text"]
                        enter_digits(e, value, "RU")
                        s = chord(e, "Enter")
                        final = active(s)
                        self.assertEqual(final["body"][final["bodyAnchor"]:final["bodyCursor"]], value)
                        self.assertTrue(s["finished"])
                        self.assertEqual(s["solved"], s["scenarioLength"])
                        self.assertEqual(contents(s), expected)
                    finally:
                        e.close()
        self.assertEqual(variants, {(profile, case) for profile in ("ubuntu", "windows") for case in ("642", "318", "907")})


if __name__ == "__main__":
    unittest.main()
