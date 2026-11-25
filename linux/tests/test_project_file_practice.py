"""Learner-facing file actions; run against a built or packaged shared engine."""
import os
import tempfile
import unittest
from unittest.mock import patch
from platform_linux import Engine

ROOT = "/home/student/project/"
DOWNLOADS = "/home/student/Downloads/"
CASES = {"inventory.csv": ("weekly-stock", "sku,quantity\nKB-04,18\nUSB-C,32\n"),
         "shipments.csv": ("ready-shipments", "dispatch,status\nPK-31,sent\nPK-42,ready\n"),
         "checks.csv": ("kit-checks", "kit,result\nST-05,pass\nST-08,recheck\n")}

NEW_ROWS = {"inventory.csv": "KB-08,24", "shipments.csv": "PK-53,ready", "checks.csv": "ST-11,pass"}


def active(s):
    return next(w for w in s["scene"]["windows"] if w["active"])


def disk(s):
    return {e["path"]: e["content"] for e in s["scene"]["terminal"]["files"]}


def chord(e, combo, char=""):
    aliases = dict(Ctrl="LCtrl", Alt="LAlt", Shift="LShift", Super="LSuper")
    keys = [aliases.get(k, k) for k in combo.split("+")]
    for k in keys:
        result = e.call("lab-key", key=k, pressed=True, text=char if k == keys[-1] else "")
    for k in reversed(keys):
        result = e.call("lab-key", key=k, pressed=False, text="")
    return result


def type_text(e, value):
    for c in value:
        chord(e, "Space" if c == " " else ("Shift+" if c.isupper() else "") + c.upper(), c)


def begin(e, mission, seed=0, platform="linux"):
    app, level = mission.split("-")[0], int(mission.startswith("workday-") or mission.endswith("-copy"))
    e.call("configure", platform=platform, layout="EN")
    e.call("lab-start", app=app, mode="sandbox", level=level, mission=mission, seconds=0,
           desktopProfile="windows" if "windows" in mission else "ubuntu", seed=seed, guidance="guided")
    return e.call("lab-resume")


def advance(e, s):
    before, t = s["solved"], s["task"]
    if t["id"] == "save" and t["text"]:
        chord(e,"Ctrl+End"); type_text(e,t["text"])
    if t["keys"]:
        for _ in range(40 if t["id"] in {"link-next","file-next"} else 1):
            s = chord(e, t["keys"])
            if s["solved"] != before:
                break
    else:
        type_text(e, t["text"])
        s = chord(e, "Enter")
    assert s["solved"] == before+1, (s["mission"], before, t, s["status"], active(s))
    return s


class ProjectFiles(unittest.TestCase):
    def run_clean(self, fn):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            e = Engine(verify=True)
            try: return fn(e)
            finally: e.close()

    def test_file_route_continues_the_existing_editor_rotation(self):
        def run(e):
            e.call("configure", platform="linux", layout="EN")
            e.call("lab-start", app="code", mode="sandbox", level=0, mission="code-project-file-move",
                   seconds=0, actionLimit=40, seed=0, guidance="guided")
            s = e.call("lab-resume")
            first = s["mission"]
            self.assertEqual(first, "code-project-file-move")
            for _ in range(s["scenarioLength"]):
                s = advance(e, s)
            self.assertEqual(s["actionLimit"], 40)
            self.assertFalse(s["finished"])
            self.assertTrue(s["mission"].startswith("code-"))
            self.assertNotEqual(s["mission"], first)
        self.run_clean(run)

    def test_materials_and_artifacts_for_both_platform_profiles(self):
        for m in ("chrome-project-file-inbox","workday-project-file-inbox","workday-windows-project-file-inbox",
                  "code-project-file-move","code-project-file-copy"):
            for seed in (0, 1, 5):
                with self.subTest(m=m,seed=seed):
                    def run(e):
                        s = begin(e, m, seed, "windows" if "windows" in m else "linux")
                        if "windows" in m: self.assertEqual(s["scene"]["desktopStyle"], "win10")
                        name, stem = None, None
                        while not s["finished"]:
                            if s["task"]["id"] == "file-name-commit":
                                w = active(s); name = w["input"]; stem = s["task"]["text"]
                                self.assertEqual(w["focus"], "file-name")
                                self.assertEqual((w["anchor"],w["cursor"]), (0,len(name)-4))
                            s = advance(e,s)
                        self.assertIn(name, CASES)
                        self.assertEqual(stem, CASES[name][0])
                        final = ROOT+"reports/"+stem+".csv"
                        expected = CASES[name][1] + (NEW_ROWS[name] if m.endswith("-move") else "")
                        self.assertEqual((active(s)["url"],active(s)["body"]), (final,expected))
                        files = disk(s)
                        self.assertEqual(files[final], expected)
                        self.assertEqual(files[ROOT+"archive/"+stem+".csv"], "period,status\nprevious,archived\n")
                        source_dir = DOWNLOADS if "inbox" in m else ROOT
                        self.assertEqual(files[source_dir+"draft.csv"], "state,approved\ndraft,no\n")
                        if m.endswith("copy"):
                            self.assertEqual(files[source_dir+name], CASES[name][1])
                        else: self.assertNotIn(source_dir+name, files)
                        self.assertNotIn(source_dir+stem+".csv", files)
                    self.run_clean(run)

    def test_detour_dirty_document_and_conflict_do_not_corrupt_work(self):
        def run(e):
            s = begin(e,"code-project-file-move",seed=1)
            while s["task"]["id"] != "file-name-start": s=advance(e,s)
            original_files=disk(s)
            s=chord(e,"Enter")  # inspect the selected document and leave an unrelated unsaved edit
            old_path=active(s)["url"]
            original=active(s)["body"]
            s=chord(e,"Ctrl+End"); s=chord(e,"Z","z")
            self.assertEqual(active(s)["body"],original+"z")
            s=chord(e,"Ctrl+Shift+E")
            s=advance(e,s); stem=s["task"]["text"]; s=advance(e,s)
            self.assertEqual((active(s)["url"],active(s)["body"]), (ROOT+stem+".csv",original+"z"))
            s=advance(e,s)  # cut the renamed file
            # Choose archive by hand, not reports. Its different same-name sibling must survive.
            for _ in range(20):
                s=chord(e,"Down")
                w=active(s)
                if w["entries"][w["item"]]["path"] == ROOT+"archive": break
            else: self.fail("archive not reachable")
            before=s["solved"]; before_files=disk(s)
            s=chord(e,"Ctrl+V")
            self.assertEqual((s["solved"],disk(s)),(before,before_files))
            self.assertIn("Ничего не заменено",active(s)["status"])
            # The blocked paste must not discard the clipboard. Achieve the move in the right folder.
            s=advance(e,s); s=advance(e,s)
            dest=ROOT+"reports/"+stem+".csv"
            self.assertEqual((active(s)["url"],active(s)["body"],disk(s)[dest]), (dest,original+"z",original))
            self.assertEqual(disk(s)[ROOT+"archive/"+stem+".csv"],original_files[ROOT+"archive/"+stem+".csv"])
            self.assertNotIn(old_path,disk(s))
            s=advance(e,s)  # Open the relocated tab, preserving its unsaved edit/caret.
            self.assertEqual((active(s)["url"], active(s)["body"], active(s)["bodyCursor"]), (dest, original+"z", len(original)+1))
            self.assertEqual((s["task"]["id"], s["finished"]), ("save", False))
            before=s["solved"]; s=chord(e,"Ctrl+S")
            self.assertEqual((s["solved"], disk(s)[dest]), (before, original+"z"))
            s=chord(e,"Backspace"); row=NEW_ROWS[old_path.rsplit("/",1)[-1]]
            type_text(e,row)
            self.assertEqual(disk(chord(e,"Ctrl+End"))[dest],original+"z")
            s=chord(e,"Ctrl+S")
            self.assertEqual((s["finished"],s["solved"],disk(s)[dest]),(True,before+1,original+row))
            self.assertEqual(disk(s)[ROOT+"archive/"+stem+".csv"],original_files[ROOT+"archive/"+stem+".csv"])
            self.assertNotIn(old_path,disk(s))
            self.assertNotIn(ROOT+stem+".csv",disk(s))
        self.run_clean(run)

    def test_wrong_name_collision_retry_and_pending_edit_survive_restart(self):
        def run(e):
            s = begin(e,"code-project-file-move")
            while s["task"]["id"] != "file-name-commit": s = advance(e,s)
            before, original, stem = s["solved"], active(s)["input"], s["task"]["text"]
            original_files = disk(s)
            type_text(e,"draft")
            s = chord(e,"Enter")
            self.assertEqual((s["solved"], active(s)["focus"], disk(s)), (before, "file-name", original_files))
            self.assertIn("занято",active(s)["status"])
            s = chord(e,"Esc")
            self.assertEqual((s["solved"],active(s)["focus"],disk(s)), (before,"explorer",original_files))
            s = chord(e,"F2")
            type_text(e,stem[:-1])
            e.call("lab-stop")
            # Reopen the engine: reconstruct the virtual disk, selection and pending draft, not just the goal number.
            e.close()
            r = Engine(verify=True)
            try:
                r.call("lab-start",app="code",mode="sandbox",level=0,guidance="guided",seconds=0,**{"continue":True})
                s=r.call("lab-resume")
                self.assertEqual((s["solved"],active(s)["focus"],active(s)["input"]),
                                 (before,"file-name",stem[:-1]+".csv"))
                type_text(r,stem[-1]); s=chord(r,"Enter")
                self.assertEqual(s["solved"],before+1)
                self.assertEqual(disk(s)[ROOT+stem+".csv"],original_files[ROOT+original])
                # Copying when asked to cut may set the clipboard; it must not earn the skill.
                s=chord(r,"Ctrl+C"); self.assertEqual(s["solved"],before+1)
                while not s["finished"]: s=advance(r,s)
            finally: r.close()
        self.run_clean(run)

if __name__ == "__main__": unittest.main()
