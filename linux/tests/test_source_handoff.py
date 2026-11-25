"""Observe actual source/body, window focus/caret and saved virtual files.

Run against a package or another SHA using TOUCH_TYPING_ENGINE.
These protocol tests are not a substitute for platform window input tests.
"""
import os
import tempfile
import unittest
from contextlib import contextmanager
from unittest.mock import patch
from platform_linux import Engine
from test_project_file_practice import chord, disk, active

PROJECT = "/home/student/project/"
DOWNLOADS = "/home/student/Downloads/"
URLS = "https://practice.local/workshop/"
CASES = {
    "audit-retention": ("LG-04", "3.6", "3.7", "LG-04,7,days", "LG-04,14,days", "LG-04: 14 days"),
    "dispatch-window": ("TR-18", "1.4", "1.5", "TR-18,15,min", "TR-18,24,min", "TR-18: 24 min"),
    "receiving-limit": ("RX-09", "2.1", "2.2", "RX-09,32,boxes", "RX-09,36,boxes", "RX-09: 36 boxes"),
}
CONFIGS = {
    "stock-import": ("delimiter=semicolon", "delimiter=comma"),
    "upload-gateway": ("timeout=15", "timeout=35"),
    "dispatch-review": ("dataset=draft.csv", "dataset=approved.csv"),
}
ROUTES = {
    "chrome-compare-bulletin": 1, "workday-verified-bulletin": 1, "workday-windows-verified-bulletin": 1,
    "ubuntu-source-window-handoff": 1, "windows-desktop-source-handoff": 1,
    "workday-log-config-repair": 2, "workday-windows-log-config-repair": 2,
}
SEEDS = (0,1,5)  # all three cases, reproducible in the packaged engine


@contextmanager
def clean_engine():
    with tempfile.TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
        e = Engine(verify=True)
        try:
            yield e
        finally:
            e.close()


def start_options(mission):
    windows = "windows" in mission
    return dict(app=mission.split('-')[0], level=ROUTES[mission], mode="sandbox", guidance="guided",
                seconds=0, desktopProfile="windows" if windows else "ubuntu")


def begin(e, mission, seed):
    options = start_options(mission)
    e.call("configure", layout="EN", platform="windows" if "windows" in mission else "linux")
    s = e.call("lab-start", **options, mission=mission, seed=seed)
    assert s["mission"] == mission, s["mission"]
    return e.call("lab-resume")


def type_text(e, text):
    for c in text:
        combo = "Space" if c == " " else "Shift+" + c if c.isupper() else c.upper()
        chord(e, combo, c)


def key_for_next(e, s):
    t = s["task"]
    if t["keys"]:
        return chord(e,t["keys"])
    type_text(e,t["text"])
    return chord(e,"Enter")


def advance(e, s):
    old, t = s["solved"],s["task"]
    for _ in range(40 if t["id"] in {"link-next","file-next","quick-next","win-overlay-next","switch-group-next"} else 1):
        s=key_for_next(e,s)
        if s["solved"] > old: break
    assert s["solved"] == old + 1, (s["mission"], old, t, s["status"], s["scene"]["activeWindow"],
                                  [(w["app"],w["id"],w["url"],w["focus"],w["item"],w["status"])
                                   for w in s["scene"]["windows"]],s["scene"]["terminal"]["lastCommand"])
    return s


def until(e, s, predicate):
    while not predicate(s):
        assert not s["finished"], "already finished before intended step"
        s=advance(e,s)
    return s


def body(w):
    return w["body"]


class SourceHandoffTest(unittest.TestCase):
    def test_all_cases_check_the_actual_artifact_and_retain_catalogue(self):
        expected_baseline = {
            "chrome": {"chrome-library","chrome-release-compare","chrome-download-verify","chrome-project-file-inbox"},
            "workday-u": {"workday-report","workday-source-note","workday-download-handoff","workday-handover"},
            "workday-w": {"workday-windows-1","workday-windows-download-handoff"},
            "ubuntu": {"ubuntu-gnome-windows","ubuntu-gnome-workspaces","ubuntu-gnome-same-app"},
            "windows": {"windows-desktop-1","windows-desktop-return-1"},
        }
        collected = {m:set() for m in ROUTES}
        for m in ROUTES:
            for seed in SEEDS:
                with self.subTest(m=m, seed=seed), clean_engine() as e:
                    s=begin(e,m,seed)
                    style="win10" if "windows" in m else "ubuntu"
                    self.assertEqual(s["scene"]["desktopStyle"],style)
                    choices=e.call("lab-catalog", desktopProfile="windows" if "windows" in m else "ubuntu")
                    ids={x["id"] for x in choices["missions"]}
                    self.assertIn(m,ids)
                    app=m.split("-")[0]
                    self.assertLessEqual(expected_baseline[app if app!="workday" else "workday-"+("w" if "windows" in m else "u")],ids)
                    observed={"matched":[],"files":[],"windows":[]}
                    while not s["finished"]:
                        t = s["task"]["id"]
                        s=advance(e,s)
                        if t == "find-next": observed["matched"].append((active(s)["url"], body(active(s))))
                        if t == "file-paste": observed["files"].append(active(s)["folder"])
                        if t in {"tile-left","tile-right","minimize","move-next","win-overlay-open","win-down","win-new-desktop"}:
                            observed["windows"].append((t,s["scene"]["workspace"], [(w["state"],w["url"],w["bodyCursor"],w["bodyAnchor"]) for w in s["scene"]["windows"]]))
                    files=disk(s)
                    if m.endswith("-log-config-repair"):
                        conf=next(path for path in files if path.startswith(PROJECT) and path.endswith(".env") and "/archive/" not in path)
                        case=conf.rsplit("/",1)[-1][:-4]; collected[m].add(case)
                        before,after=CONFIGS[case]
                        self.assertEqual(files[conf],after+"\nstandby=true\n")
                        self.assertEqual(files[PROJECT+"archive/"+case+"-example.env"],before+"\nstandby=true\n")
                        self.assertTrue(s["scene"]["terminal"]["lastSuccess"])
                        self.assertIn("Ran 2 tests",str(s["scene"]["terminal"]["output"]))
                    elif "source" in m:
                        path=active(s)["url"]
                        self.assertTrue(path.endswith("-handoff.txt"),path)
                        case=path.rsplit("/",1)[-1][:-len("-handoff.txt")]; collected[m].add(case)
                        self.assertIn(CASES[case][-1],files[path])
                        self.assertNotIn("[значение]",files[path])
                        self.assertEqual(files[PROJECT+"черновик.txt"],"Старое значение не подтверждено. Не изменять.\n")
                        self.assertTrue(any(x[1]==1 for x in observed["windows"]))
                        self.assertTrue(any(any("minimized"==w[0] and w[2]!=w[3] for w in x[2]) for x in observed["windows"]))
                    else:
                        path=active(s)["url"]
                        case=next(slug for slug in CASES if slug in path)
                        collected[m].add(case)
                        key,previous,current,old,new,_=CASES[case]
                        self.assertEqual(len(observed["matched"]),2)
                        self.assertEqual(observed["matched"][0][0],URLS+case+"/"+current)
                        self.assertIn(new,observed["matched"][0][1])
                        self.assertEqual(observed["matched"][1][0],URLS+case+"/"+previous)
                        self.assertIn(old,observed["matched"][1][1])
                        archive=next(w for w in s["scene"]["windows"] if w["app"]=="chrome")
                        self.assertIn(URLS+case+"/"+current,archive["savedBookmarks"])
                        if m.startswith("workday"):
                            csv=PROJECT+"reports/"+case+"-"+current+".csv"
                            self.assertIn(new+"\n",files[csv])
                            self.assertIn(old+"\n",files[PROJECT+"archive/"+case+"-"+previous+".csv"])
                            self.assertNotIn(DOWNLOADS+case+"-"+current+".csv",files)
                            self.assertEqual(path,PROJECT+case+"-review.md")
                            self.assertIn(URLS+case+"/"+current+"\n",files[path])
                            self.assertTrue(files[path].endswith(new+"\n"))
                            self.assertNotIn(old,files[path])
                            self.assertEqual(observed["files"], [PROJECT+"reports"])
                        else:
                            self.assertEqual(path,DOWNLOADS+case+"-"+current+".csv")
                            self.assertIn(new+"\n",body(active(s)))
        self.assertTrue(all(len(cases)==3 for cases in collected.values()),collected)

    def test_link_in_note_and_source_restore_after_actual_process_reopen(self):
        m="workday-verified-bulletin"
        with clean_engine() as e:
            s=begin(e,m,1)
            s=until(e,s,lambda x:x["task"]["id"]=="research-find")
            before=s["solved"]
            type_text(e,"CONTROL")
            wrong=chord(e,"Enter")
            self.assertEqual(wrong["solved"],before)
            self.assertEqual(active(wrong)["focus"],"find")
            chord(e,"Ctrl+A"); type_text(e,s["task"]["text"])
            s=chord(e,"Enter"); self.assertEqual(s["solved"],before+1)
            s=until(e,s,lambda x:x["task"]["id"]=="paste" and active(x)["url"].endswith("-review.md"))
            s=advance(e,s)
            w,solved,text=active(s),s["solved"],s["scene"]["clipboard"]
            self.assertIn(text,w["body"])
            self.assertTrue(w["tabs"][w["activeTab"]]["modified"])
            e.call("lab-stop"); e.close()
            other=Engine(verify=True)
            try:
                other.call("lab-start",**start_options(m),**{"continue":True})
                r=other.call("lab-resume")
                self.assertEqual((r["solved"],active(r)["body"],r["scene"]["clipboard"]),
                                 (solved,w["body"],text))
                while not r["finished"]: r=advance(other,r)
                self.assertFalse(active(r)["tabs"][active(r)["activeTab"]]["modified"])
            finally:
                other.close()

    def test_windows_task_view_wrong_draft_does_not_score_and_recovers_exact_caret(self):
        m="windows-desktop-source-handoff"
        with clean_engine() as e:
            s=begin(e,m,1)
            pending=until(e,s,lambda x:x["task"]["id"]=="win-overlay-open" and x["scene"]["desktopOverlay"]=="task-view")
            solved=pending["solved"]
            s=chord(e,"Down")  # unrelated VS Code draft
            s=chord(e,"Enter")
            self.assertEqual((s["solved"],active(s)["url"]), (solved,PROJECT+"черновик.txt"))
            s=chord(e,"Super+Tab")
            for _ in range(4):
                scene=s["scene"]; idx=scene["desktopOverlaySelection"]
                selection=next(w for w in scene["windows"] if w["id"]==scene["desktopOverlayWindows"][idx])
                if selection["url"].endswith("-handoff.txt"): break
                s=chord(e,"Down")
            else: self.fail("minimized draft missing from Task View")
            s=advance(e,s)
            w=active(s)
            self.assertEqual(w["body"][min(w["anchor"],w["cursor"]):max(w["anchor"],w["cursor"])],"[значение]")
            self.assertEqual(s["task"]["id"],"paste")
            wrong=chord(e,"Ctrl+Shift+V")
            self.assertEqual(wrong["solved"],s["solved"])
            while not s["finished"]: s=advance(e,s)
            self.assertIn("TR-18: 24 min",active(s)["body"])

    def test_verifier_fails_before_saving_and_correct_key_cannot_edit_archive(self):
        with clean_engine() as e:
            s=begin(e,"workday-log-config-repair",5)
            s=advance(e,s)  # cd before attempting to run the shipped test
            before=s["solved"]
            type_text(e,"python3 -m unittest test_live_config")
            s=chord(e,"Enter")
            self.assertEqual(s["solved"],before)
            self.assertFalse(s["scene"]["terminal"]["lastSuccess"])
            s=until(e,s,lambda x:x["task"]["id"]=="paste")
            # A tab navigation is real, but pasting there cannot earn the configured step.
            before=s["solved"]
            chord(e,"Ctrl+Tab")
            s=chord(e,"Ctrl+V")
            self.assertEqual(s["solved"],before)
            self.assertIn("/logs/",active(s)["url"])
            chord(e,"Ctrl+Z")
            s=chord(e,"Ctrl+Tab")
            while not s["finished"]: s=advance(e,s)
            self.assertTrue(s["scene"]["terminal"]["lastSuccess"])


if __name__ == "__main__":
    unittest.main()
