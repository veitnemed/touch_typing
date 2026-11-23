"""Exercise the actual JSON protocol/isolated store using a built engine.

Usage: python linux/tests/test_win10_protocol.py /path/to/TouchTyping.Engine
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def one(engine, profile):
    def prefix():
        return [str(engine), "--verify"]

    class Client:
        def __enter__(self):
            env = dict(os.environ, XDG_DATA_HOME=profile)
            env.pop("TOUCH_TYPING_QA_LEGACY_PROJECT", None)
            self.proc = subprocess.Popen(prefix(), env=env, stdin=subprocess.PIPE,
                                         stdout=subprocess.PIPE, text=True, encoding="utf-8")
            return self

        def __exit__(self, *_):
            self.proc.stdin.close()
            assert self.proc.wait(timeout=10) == 0

        def call(self, command, error=False, **fields):
            self.proc.stdin.write(json.dumps(dict(command=command, **fields)) + "\n")
            self.proc.stdin.flush()
            response = json.loads(self.proc.stdout.readline())
            assert response["ok"] != error, response
            if not error:
                assert not response.get("storageError"), response
                return response["value"]

        def event(self, key, pressed):
            return self.call("lab-key", key=key, pressed=pressed, text="")

        def chord(self, chord):
            mods = {"Super": "LSuper", "Ctrl": "LCtrl", "Alt": "LAlt", "Shift": "LShift"}
            keys = [mods.get(key, key) for key in chord.split("+")]
            for key in keys:
                self.event(key, True)
            for key in reversed(keys):
                state = self.event(key, False)
            return state

    def begin(c, **extras):
        return c.call("lab-start", mode="sandbox", seconds=0, guidance="guided",
                      seed=0, level=extras.pop("level", 0), **extras)

    with Client() as c:
        assert c.call("library")["desktopProfile"] == "ubuntu"
        c.call("settings", desktopProfile="linux", error=True)
        old = begin(c, app="workday", mission="workday-report")
        assert old["desktopProfile"] == "ubuntu" and old["scene"]["desktopStyle"] == "ubuntu"
        c.call("lab-resume")
        assert c.chord("Super")["scene"]["overview"]
        assert c.call("lab-stop")["saved"]
        c.call("settings", desktopProfile="windows")
        cat = c.call("lab-catalog")
        assert [p["id"] for p in cat["desktopProfiles"]] == ["ubuntu", "windows"]
        assert {item["app"] for item in cat["recommendations"]} == {item["id"] for item in cat["classes"]}
        ubuntu = c.call("lab-catalog", desktopProfile="ubuntu")
        assert {item["app"] for item in ubuntu["recommendations"]} == {item["id"] for item in ubuntu["classes"]}
        assert "windows" in {v["id"] for v in cat["classes"]}
        assert "ubuntu" not in {v["id"] for v in cat["classes"]}
        assert all(m["id"].startswith("workday-windows-") for m in cat["missions"] if m["app"] == "workday")
        assert {d["desktopProfile"] for d in cat["drafts"] if d["app"] == "workday"} == {"ubuntu"}
        begin(c, app="workday", desktopProfile="windows", error=True, **{"continue": True})
        fallback = begin(c, app="workday", mission="workday-windows-0", **{"continue": True})
        assert fallback["solved"] == 0 and fallback["task"]["id"] == "win-start"
        started = begin(c, app="workday", mission="workday-windows-0")
        assert (started["desktopProfile"], started["scene"]["workspaceCount"]) == ("windows", 1)
        c.call("lab-resume")
        start = c.chord("Super")
        assert start["scene"]["desktopOverlay"] == "start" and start["solved"] == 1
        begin(c, app="workday", level=1, desktopProfile="windows", error=True, **{"continue": True})
        assert c.call("lab-state")["scene"]["desktopOverlay"] == "start"
        assert not c.call("lab-state")["paused"]
        picked = c.chord("Enter")
        assert picked["scene"]["desktopOverlay"] == "" and picked["solved"] == 2
        assert c.call("lab-stop")["saved"]
    with Client() as c:
        assert c.call("library")["desktopProfile"] == "windows"
        assert {d["desktopProfile"] for d in c.call("lab-catalog")["drafts"] if d["app"] == "workday"} == {"windows", "ubuntu"}
        state = begin(c, app="workday", **{"continue": True})
        assert (state["solved"], state["task"]["id"]) == (2, "win-explorer")
        c.call("lab-resume")
        state = c.chord("Super+E")
        scene = state["scene"]
        assert state["solved"] == 3
        assert next(w for w in scene["windows"] if w["active"])["windowsPath"] == r"C:\Users\Student"
        snap = c.chord("Super+Left")["scene"]
        assert snap["desktopOverlay"] == "snap" and len(snap["desktopOverlayWindows"]) == 1
        done = c.chord("Enter")["scene"]
        assert {w["state"] for w in done["windows"]} == {"snapped-left", "snapped-right"}
        c.chord("Super+Up")
        restored = c.chord("Super+Down")["scene"]
        assert next(w for w in restored["windows"] if w["active"])["state"] == "normal"
        minimized = c.chord("Super+Down")["scene"]
        assert any(w["state"] == "minimized" for w in minimized["windows"])
        shown = c.chord("Super+D")["scene"]
        assert shown["showDesktop"]
        assert [w["state"] for w in shown["windows"]] == [w["state"] for w in minimized["windows"]]
        assert not c.chord("Super+D")["scene"]["showDesktop"]
        taskview = c.chord("Super+Tab")["scene"]
        assert taskview["desktopOverlay"] == "task-view" and len(taskview["desktopOverlayWindows"]) == 2
        c.chord("Esc")
        c.event("LAlt", True)
        pending = c.event("Tab", True)["scene"]
        assert pending["switcher"] and pending["activeWindow"] != pending["switchTarget"]
        c.event("Tab", False)
        assert c.event("LAlt", False)["scene"]["activeWindow"] == pending["switchTarget"]
        assert c.chord("Super+Ctrl+Left")["scene"]["workspace"] == 0
        second = c.chord("Super+Ctrl+D")["scene"]
        assert (second["workspace"], second["workspaceCount"], second["activeWindow"]) == (1, 2, None)
        assert c.chord("Super+Ctrl+Right")["scene"]["workspace"] == 1
        assert c.chord("Super+Ctrl+Left")["scene"]["workspace"] == 0
        assert c.chord("Super+Ctrl+Right")["scene"]["workspace"] == 1
        assert c.chord("Super+Ctrl+F4")["scene"]["workspaceCount"] == 1
    with Client() as c:
        for app in ("windows", "workday"):
            prefix = "windows-desktop-" if app == "windows" else "workday-windows-"
            for level in range(3):
                state = begin(c, app=app, level=level, mission=prefix + str(level))
                c.call("lab-resume")
                for _ in range(100):
                    if state["finished"]:
                        break
                    before = state["solved"]
                    state = c.chord(state["task"]["keys"])
                    if state["solved"] == before:
                        assert state["task"]["id"] == "file-next", state["task"]
                assert state["finished"] and state["saved"] and state["solved"] >= 10, state["task"]
        assert begin(c, app="workday", desktopProfile="ubuntu", **{"continue": True})["desktopProfile"] == "ubuntu"


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="tt-win10-test-") as root:
        one(Path(sys.argv[1]).resolve(), root)
    print("PASS Windows 10 desktop protocol, platform independence, profile isolation and six routes")
