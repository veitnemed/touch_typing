"""Recover the reported Explorer/VS Code focus detour via real scene input."""
import os
import tempfile
import unittest
from unittest.mock import patch
from platform_linux import Engine
from test_project_file_practice import active, chord

HOME = "/home/student"
PROJECT = HOME + "/project"
LOGS = PROJECT + "/logs"
LOG = LOGS + "/app.log"
OPTIONS = dict(app="windows", level=0, desktopProfile="windows", mode="sandbox",
               guidance="guided", seconds=0, actionLimit=0, seed=1)


def start(e):
    e.call("configure", platform="windows", layout="EN")
    e.call("lab-start", **OPTIONS, mission="windows-desktop-return-0")
    state = e.call("lab-resume")
    assert state["task"]["id"] == "win-explorer", state["task"]
    state = chord(e, "Super+E")
    assert state["task"]["text"] == "project", state["task"]
    return state


def select(e, state, path, gesture):
    for _ in range(40):
        w = active(state)
        i = w["item"]
        if i >= 0 and w["entries"][i]["path"] == path:
            return state
        state = chord(e, gesture)
    raise AssertionError(("Path wasn't reached",path,active(state)))


def take_detour(e):
    state = start(e)
    baseline = state["solved"]
    # Up may change the selection, but the displayed exact Down goal has
    # not been earned. Enter then allows realistic off-script exploration.
    state = select(e, state, PROJECT, "Up")
    assert state["solved"] == baseline
    state = chord(e, "Enter")
    assert active(state)["folder"] == PROJECT
    state = select(e, state, LOGS, "Down")
    state = chord(e, "Enter")
    assert active(state)["folder"] == LOGS
    state = select(e, state, LOG, "Down")
    state = chord(e, "Enter")
    assert (active(state)["app"], active(state)["url"], state["task"]["text"], state["solved"]) == ("code", LOG, "project", baseline)
    return state


def go_home(e, state, mouse=False):
    score = state["solved"]
    if mouse:
        explorer = next(w for w in state["scene"]["windows"] if w["app"] == "files")
        state = e.call("lab-explorer-action", action="desktop-focus", path=explorer["id"])
    else:
        state = chord(e, "Alt+Tab")
    assert active(state)["folder"] == LOGS
    state = chord(e, "Alt+Up")
    assert active(state)["folder"] == PROJECT
    state = chord(e, "Alt+Up")
    assert (active(state)["folder"], state["solved"]) == (HOME, score)
    return state


class Recovery(unittest.TestCase):
    def run_clean(self, test):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": folder}):
            e = Engine(verify=True)
            try:
                test(e)
            finally:
                e.close()

    def test_exact_key_recovers_while_the_unrelated_editor_stays_open(self):
        def run(e):
            scene = take_detour(e)
            score = scene["solved"]
            wrong = chord(e, "Down")
            self.assertEqual(wrong["solved"], score)
            self.assertEqual(active(wrong)["app"], "code")
            state = go_home(e,wrong)
            state = select(e,state,PROJECT,"Down")
            self.assertEqual((state["solved"],state["task"]["id"]), (score+1,"file-open"))
            self.assertEqual(next(w for w in state["scene"]["windows"] if w["app"]=="code")["url"],LOG)
            for step,want in (("Enter","win-explorer-parent"), ("Alt+Up","win-desktop"),
                              ("Super+D","win-desktop"), ("Super+D",None)):
                state=chord(e,step)
                if want: self.assertEqual(state["task"]["id"], want)
            self.assertTrue(state["finished"])
            self.assertEqual(state["solved"],score+5)
        self.run_clean(run)

    def test_mouse_can_restore_or_close_simulated_windows_but_not_score(self):
        def run(e):
            state=take_detour(e)
            score=state["solved"]
            code=active(state)["id"]
            state=e.call("lab-explorer-action",action="desktop-maximize",path=code)
            self.assertEqual(active(state)["state"],"maximized")
            state=e.call("lab-explorer-action",action="desktop-maximize",path=code)
            self.assertEqual(active(state)["state"],"normal")
            state=e.call("lab-explorer-action",action="desktop-minimize",path=code)
            self.assertEqual(active(state)["folder"],LOGS)
            state=e.call("lab-explorer-action",action="desktop-taskbar",path="code")
            self.assertEqual((active(state)["state"],active(state)["url"]),("normal",LOG))
            state=go_home(e,state,mouse=True)
            self.assertEqual(state["solved"],score)
            state=select(e,state,PROJECT,"Down")
            self.assertEqual(state["solved"],score+1)
            state=e.call("lab-explorer-action",action="desktop-close",path=code)
            self.assertFalse(any(w["id"]==code for w in state["scene"]["windows"]))
            self.assertEqual(state["solved"],score+1)
        self.run_clean(run)

    def test_opening_second_explorer_does_not_make_folder_goal_impossible(self):
        def run(e):
            state=take_detour(e)
            score=state["solved"]
            state=chord(e,"Super+E")
            self.assertEqual((active(state)["app"],active(state)["folder"],state["solved"]),("files",HOME,score))
            self.assertEqual(sum(w["app"]=="files" for w in state["scene"]["windows"]),2)
            state=select(e,state,PROJECT,"Down")
            self.assertEqual(state["solved"],score+1)
            self.assertEqual(state["task"]["id"],"file-open")
        self.run_clean(run)

    def test_legacy_checkpoint_keeps_old_counts_and_new_focus_survives_second_restart(self):
        old_engine=os.environ.get("TOUCH_TYPING_RECOVERY_BEFORE_ENGINE")
        if not old_engine: self.skipTest("supply a ca621b0 engine checkout for upgrade verification")
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": folder}):
            with patch.dict(os.environ,{"TOUCH_TYPING_ENGINE":old_engine}):
                old=Engine(verify=True)
                try:
                    established=take_detour(old)
                    old.call("lab-stop")
                finally: old.close()
            def reopen():
                e=Engine(verify=True)
                e.call("lab-start", **OPTIONS, **{"continue":True})
                return e,e.call("lab-resume")
            current,state=reopen()
            try:
                self.assertEqual((state["solved"],active(state)["url"]),(established["solved"],LOG))
                state=go_home(current,state,mouse=True)
                state=select(current,state,PROJECT,"Down")
                self.assertEqual(state["solved"],established["solved"]+1)
                current.call("lab-stop")
            finally: current.close()
            again,state2=reopen()
            try:
                self.assertEqual((state2["solved"],state2["task"]["id"],active(state2)["folder"]),
                                 (state["solved"],"file-open",HOME))
            finally: again.close()


if __name__ == "__main__": unittest.main()
