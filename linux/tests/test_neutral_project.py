"""Test the learner-visible clean workspace and optionally migrate real preview.6 drafts."""
from contextlib import contextmanager
from hashlib import sha256
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from platform_linux import Engine

APP_HASH = "f5f482eb437481c7f209fb5409715ec29f12cce8076227c8ebaddfdade536115"
PROJECT = "/home/student/project/"
FILES = ["app.py", "storage.py", "tasks.py", "test_tasks.py"]
OPTIONS = dict(app="code", mode="sandbox", level=0, guidance="guided",
               mission="code-guide", seconds=0, seed=0)


@contextmanager
def open_engine(platform, executable=None):
    # The fixture override is for historical QA only, never enabled in this test.
    env = {"TOUCH_TYPING_QA_LEGACY_PROJECT": ""}
    if executable:
        env["TOUCH_TYPING_ENGINE"] = executable
    with patch.dict(os.environ, env):
        engine = Engine(verify=True)
        try:
            engine.call("configure", platform=platform, layout="EN")
            yield engine
        finally:
            engine.close()


def selected(state, app="code"):
    return next(w for w in state["scene"]["windows"] if w["active"] and w["app"] == app)


def key(engine, *keys, value=""):
    for item in keys:
        engine.call("lab-key", key=item, pressed=True, text=value if item == keys[-1] else "")
    for item in reversed(keys):
        result = engine.call("lab-key", key=item, pressed=False, text="")
    return result


class NeutralProjectTest(unittest.TestCase):
    def clean_workspace(self, engine):
        state = engine.call("lab-start", **OPTIONS)
        w = selected(state)
        self.assertEqual((w["projectName"], w["projectRelative"], w["projectFiles"], len(w["tabs"])),
                         ("task-list", "app.py", FILES, 1))
        self.assertEqual(sha256(w["body"].encode()).hexdigest(), APP_HASH)
        self.assertEqual(w["body"], w["tabs"][0]["savedBody"])
        self.assertEqual(w["bodyCursor"], 0)
        paths = {f["path"] for f in state["scene"]["terminal"]["files"]}
        self.assertEqual({f for f in paths if f.endswith(".py")}, {PROJECT + name for name in FILES})
        return state

    def test_fresh_training_reference_and_restart_both_keyboard_profiles(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as root, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                with open_engine(platform) as engine:
                    self.clean_workspace(engine)
                    engine.call("lab-resume")
                    for _ in range(7):
                        key(engine, "Down")
                    key(engine, "Home")
                    for _ in range(4):
                        key(engine, "Right")
                    for _ in range(8):
                        state = key(engine, "LShift", "Right")
                    w = selected(state)
                    self.assertEqual(w["body"][w["bodyAnchor"]:w["bodyCursor"]], "add_task")
                    self.assertEqual((w["bodyAnchor"], w["bodyCursor"]), (
                        w["body"].index('add_task(tasks, "Buy tea")'),
                        w["body"].index('add_task(tasks, "Buy tea")') + len("add_task")))
                    engine.call("lab-stop")
                    self.assertFalse(engine.storage_error)
                profile = Path(root) / "TouchTypingStudio-QA" / "progress.xml"
                self.assertEqual(ET.parse(profile).findtext("./LiveDrafts/LiveDraft/ProjectFixture"),
                                 "task-list/v1")
                with open_engine(platform) as engine:
                    replay = selected(engine.call("lab-start", **OPTIONS, **{"continue": True}))
                    self.assertEqual(tuple(replay[x] for x in ("projectName", "projectRelative", "body",
                                                              "bodyAnchor", "bodyCursor")),
                                     tuple(w[x] for x in ("projectName", "projectRelative", "body",
                                                         "bodyAnchor", "bodyCursor")))

    def test_neutral_browser_start_and_bookmark_both_keyboard_profiles(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as root, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}), open_engine(platform) as engine:
                missions = engine.call("lab-catalog")["missions"]
                mission = next(m for m in missions if m["app"] == "chrome" and m["level"] == 0)
                opts = dict(app="chrome", mode="sandbox", level=0, guidance="guided",
                            mission=mission["id"], seconds=0, seed=0)
                state = engine.call("lab-start", **opts)
                w = selected(state, "chrome")
                page = "https://practice.local/task-list"
                self.assertEqual(w["tabs"][1]["url"], page)
                self.assertTrue(all("piling" not in t["url"] for t in w["tabs"]))
                self.assertTrue(any(e["title"] == "Как вести список задач" for e in w["entries"]))
                engine.call("lab-resume")
                state = key(engine, "LCtrl", "Tab")
                self.assertEqual(selected(state, "chrome")["url"], page)
                state = key(engine, "LCtrl", "D")
                self.assertIn(page, selected(state, "chrome")["savedBookmarks"])

    def test_task_list_terminal_checks_persisted_known_variant_and_refuses_other_source(self):
        with tempfile.TemporaryDirectory() as root, patch.dict(
                os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}), open_engine("linux") as engine:
            self.clean_workspace(engine)
            engine.call("lab-resume")

            def type_text(value):
                for char in value:
                    physical = {" ": "Space", "_": "-", "~": "`", "/": "/", ".": "."}.get(char, char.upper())
                    modifiers = ["LShift"] if char in "_~" or char.isupper() else []
                    state = key(engine, *modifiers, physical, value=char)
                return state

            def command(value):
                type_text(value)
                return key(engine, "Enter")["scene"]["terminal"]

            key(engine, "LCtrl", "P")
            type_text("tasks.py")
            state = key(engine, "Enter")
            self.assertEqual(selected(state)["projectRelative"], "tasks.py")
            key(engine, "LCtrl", "Home")
            for _ in range(7):
                key(engine, "Down")
            key(engine, "End")
            for _ in range(4):
                key(engine, "LShift", "Left")
            state = type_text("False")
            self.assertIn('task["done"] = False', selected(state)["body"])
            key(engine, "LCtrl", "`")
            self.assertTrue(command("cd ~/project")["lastSuccess"])
            self.assertTrue(command("python3 -m unittest test_tasks")["lastSuccess"],
                            "unsaved buffer must not affect the virtual test")
            key(engine, "LCtrl", "`")
            key(engine, "LCtrl", "S")
            key(engine, "LCtrl", "`")
            failed = command("python3 -m unittest test_tasks")
            self.assertFalse(failed["lastSuccess"])
            self.assertIn("test_mark_done", " ".join(failed["output"][-3:]))
            key(engine, "LCtrl", "`")
            key(engine, "End")
            type_text("x")
            key(engine, "LCtrl", "S")
            key(engine, "LCtrl", "`")
            unknown = command("python3 -m unittest test_tasks")
            self.assertFalse(unknown["lastSuccess"])
            self.assertIn("нет результата", " ".join(unknown["output"][-3:]))

    def test_upgrade_legacy_draft_through_actual_preview6_binary(self):
        old_binary = os.environ.get("TOUCH_TYPING_PREVIEW6_ENGINE")
        if not old_binary:
            self.skipTest("set TOUCH_TYPING_PREVIEW6_ENGINE to the actual 845766c8 released executable")
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as root, patch.dict(
                    os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                with open_engine(platform, old_binary) as old:
                    old.call("lab-start", **OPTIONS)
                    state = old.call("lab-resume")
                    for _ in range(30):
                        keys = state["task"]["keys"]
                        next_state = key(old, *(dict(Ctrl="LCtrl", Alt="LAlt", Shift="LShift",
                                                     Super="LSuper").get(k, k) for k in keys.split("+")))
                        if next_state["solved"] == 3:
                            state = next_state
                            break
                        state = next_state
                    self.assertEqual(state["solved"], 3)
                    body = selected(state)
                    old.call("lab-stop")
                profile = Path(root) / "TouchTypingStudio-QA" / "progress.xml"
                saved = profile.read_bytes()
                self.assertNotIn(b"ProjectFixture", saved)
                with open_engine(platform) as current:
                    new = current.call("lab-start", **OPTIONS, **{"continue": True})
                    self.assertEqual((new["solved"], new["task"]["id"]), (3, state["task"]["id"]))
                    restored = selected(new)
                    for field in ("url", "body", "bodyAnchor", "bodyCursor", "focus"):
                        self.assertEqual(restored[field], body[field], field)
                    self.assertEqual(profile.read_bytes(), saved)
                    self.clean_workspace(current)


if __name__ == "__main__":
    unittest.main()
