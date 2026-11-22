"""Replay reference routes through physical events and verify the visible mission advances."""
import os
import tempfile
import unittest

from platform_linux import Engine


class MissionTest(unittest.TestCase):
    def test_routes(self):
        with tempfile.TemporaryDirectory() as directory:
            original = os.environ.get("XDG_DATA_HOME")
            os.environ["XDG_DATA_HOME"] = directory
            try:
                engine = Engine(verify=True)
                try:
                    missions = engine.call("lab-catalog")["missions"]
                    self.assertEqual(len(missions), 51)
                    self.assertEqual(len({m["id"] for m in missions}), 51)
                    for mission in missions:
                        with self.subTest(mission=mission["id"]):
                            self.walk(engine, mission)
                finally:
                    engine.close()
            finally:
                if original is None:
                    os.environ.pop("XDG_DATA_HOME", None)
                else:
                    os.environ["XDG_DATA_HOME"] = original

    def walk(self, engine, mission):
        state = engine.call("lab-start", app=mission["app"], level=mission["level"], mode="sandbox",
                            seconds=0, guidance="guided", mission=mission["id"], seed=0)
        total = state["scenarioLength"]
        self.assertGreaterEqual(total, 20)
        state = engine.call("lab-resume")
        def chord(keys, char=""):
            for key in keys:
                engine.call("lab-key", key=key, pressed=True, text=char if key == keys[-1] else "")
            for key in reversed(keys):
                result = engine.call("lab-key", key=key, pressed=False, text="")
            return result
        sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
        for step in range(total):
            self.assertEqual(state["mission"], mission["id"])
            task = state["task"]
            for attempt in range(24):
                if task["keys"]:
                    state = chord([sides.get(k, k) for k in task["keys"].split("+")])
                else:
                    for char in task["text"]:
                        chord(["Space" if char == " " else char.upper()], char)
                    state = chord(["Enter"])
                if state["solved"] == step + 1:
                    break
            self.assertEqual(state["solved"], step + 1, (task, state["status"], state["phase"]))
            if task["id"] == "command/head -n 2 spectrum-report.txt":
                files = {f["path"]: f["content"] for f in state["scene"]["terminal"]["files"]}
                self.assertNotIn("/home/student/Downloads/spectrum-report.txt", files)
                report = files["/home/student/project/spectrum-report.txt"]
                self.assertTrue(report.startswith("# "), report)
                self.assertIn(report.splitlines()[0], state["scene"]["terminal"]["output"])
            self.assertEqual(state["errors"], 0, (mission["id"], task, state["status"]))


if __name__ == "__main__":
    unittest.main()
