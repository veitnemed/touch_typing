"""Shared-engine keyboard acceptance (protocol events, not OS/hardware delivery).

The historical shortcut registry, the complete physical-key vocabulary, and
the mode entry below use the same `library` / `inspect` / `shortcut-*`
protocol as the app. `audit_live_shortcuts.py --matrix` additionally visits
the context-specific gestures from every authored Live mission.
"""
import os
import tempfile
import unittest
from collections import Counter
from unittest.mock import patch

from platform_linux import Engine, EngineError, PHYSICAL

CONTROL = ("Ctrl", "Shift", "Alt", "Super")
SETUP = {
    "linux": ("Super+Space", "Shift+Super+Space"),
    "windows": ("Alt+Shift", "Ctrl+Shift"),
}


def transitions(operation, gesture, send, text="", right=False):
    """One physical stroke, including release; command labels are not typed."""
    tokens = gesture.split("+")
    keys = [(("R" if right else "L") + k) if k in CONTROL else k for k in tokens]
    for key in keys:
        state = send(operation, key=key, pressed=True, text=text if key == keys[-1] else "")
    for key in reversed(keys):
        state = send(operation, key=key, pressed=False, text="")
    return state


class RegisteredKeyboard(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": tmp.name})
        env.start()
        self.addCleanup(env.stop)
        self.engine = Engine(verify=True)
        self.addCleanup(self.engine.close)

    def configure(self, platform, layout):
        forward, backward = SETUP[platform]
        lib = self.engine.call("configure", platform=platform, layout=layout, forward=forward, backward=backward)
        self.assertEqual((lib["layout"], lib["forward"], lib["backward"]), (layout, forward, backward))
        return lib

    def test_physical_vocabulary_has_stable_latin_gesture_labels_in_ru_and_en(self):
        # Fn is not an OS-delivered key. Two distinct labels, e.g. NumPad7
        # and Home, are different physical keys even when they can do one job.
        self.assertEqual(len(PHYSICAL), len(set(PHYSICAL.values())))
        for platform in SETUP:
            for layout in ("RU", "EN"):
                with self.subTest(platform=platform, layout=layout):
                    lib = self.configure(platform, layout)
                    caps = {cap["key"]: cap for row in lib["keyboard"] for cap in row}
                    expected_positions = {"V": ("v", "м"), "B": ("b", "и"), "C": ("c", "с"),
                                          "P": ("p", "з"), "3": ("#", "№")}
                    for key, labels in expected_positions.items():
                        names = ("enShift", "ruShift") if key == "3" else ("en", "ru")
                        self.assertEqual(tuple(caps[key][n] for n in names), labels)
                    aux = {cap["key"]: cap for cap in lib["keyboardAux"]}
                    self.assertEqual(aux["NumPad7"]["alias"], "Home")
                    self.assertEqual(aux["NumDecimal"]["alias"], "Delete")
                    self.assertTrue(set(PHYSICAL.values()) <= set(caps) | set(aux) | {"Esc", "Print"} |
                                    {f"F{i}" for i in range(1, 13)})
                    for position in PHYSICAL.values():
                        with self.subTest(position=position):
                            description = self.engine.call("inspect", keys=position)
                            self.assertEqual(description["keys"], position)
                            self.assertEqual(description["steps"][0][0]["key"], position)
                    # Fn is a firmware layer on this profile, never pretend an
                    # app-delivered key was captured when only Fn was pressed.
                    with self.assertRaises(EngineError):
                        self.engine.call("inspect", keys="Fn")
                    for letter, alt in (("B", "и"), ("V", "м")):
                        klass = self.engine.call("inspect", keys=f"Ctrl+{letter}")
                        self.assertEqual(klass["keys"], f"Ctrl+{letter}")
                        self.assertEqual(klass["steps"][0][-1]["key"], letter)
                        self.assertEqual(caps[klass["steps"][0][-1]["key"]]["ru"], alt)
        self.assertEqual(PHYSICAL[55], "V")
        self.assertEqual(PHYSICAL[56], "B")
        self.assertEqual(PHYSICAL[79], "NumPad7")
        self.assertEqual(PHYSICAL[110], "Home")

    def test_every_visible_registered_shortcut_can_complete_in_both_layouts(self):
        verified = Counter()
        for platform in SETUP:
            for layout in ("RU", "EN"):
                lib = self.configure(platform, layout)
                caps = {cap["key"]: cap for row in lib["keyboard"] for cap in row}
                skills = [entry["skill"] for entry in lib["skills"] if not entry["skill"]["hidden"]]
                for skill in skills:
                    with self.subTest(platform=platform, layout=layout, skill=skill["id"]):
                        self._complete_shortcut(skill, layout, caps)
                        verified[platform, layout] += 1
        print("Registered shortcut practice, layout × platform (including text/scene goals): " + ", ".join(f"{p}/{l}={n}" for (p, l), n in verified.items()))

    def test_numlock_home_alternate_is_not_the_top_row(self):
        self.configure("linux", "RU")
        lib = self.engine.call("shortcut-save", title="Добраться до домашней папки",
                               group="цифровой блок", keys="Alt+Home")
        id = next(e["skill"]["id"] for e in lib["skills"] if e["editable"])
        run = self.engine.call("shortcut-start", ids=[id], limit=3)
        hit = lambda keys: transitions("shortcut-key", keys, self.engine.call)
        self.assertEqual(hit("Alt+7")["solved"], 0)
        self.assertEqual(hit("Home")["solved"], 0)
        self.assertEqual(hit("Alt+NumPad7")["solved"], 1)
        self.assertEqual(hit("Alt+Home")["solved"], 2)
        self.assertEqual(hit("Alt+NumPad7")["solved"], 3)
        self.assertTrue(self.engine.call("shortcut-result")["finished"])

    def _complete_shortcut(self, skill, layout, caps):
        engine = self.engine.call
        run = engine("shortcut-start", ids=[skill["id"]], level=skill["tier"], limit=3)
        before = run["solved"]
        action = skill["action"]
        def chord(keys, text="", right=False):
            return transitions("shortcut-key", keys, engine, text, right)

        if action in ("layout", "layout-back"):
            other = "ф" if layout == "EN" else "a"
            first = "a" if layout == "EN" else "ф"
            run = chord("A", first)
            self.assertEqual((run["solved"], run["activity"]), (before, "layout-after"))
            run = chord(skill["keys"])
            self.assertEqual(run["solved"], before)  # system switch alone isn't proof
            run = chord("A", other)
        elif action == "reboot":
            for letter in "reboot":
                run = chord(letter.upper(), letter)
            run = chord("Enter")
            self.assertTrue(run["verifiedScene"]["rebooted"])
        else:
            for step, label in enumerate(skill["keys"].split(" → ")):
                if run["inputMode"] == "waiting":
                    # Observe the exact branch after consent. No invented substitute.
                    self.assertEqual(run["targetKeys"], label)
                    run = engine("shortcut-input-mode", mode="exact", stepToken=run["stepToken"])
                self.assertEqual(run["inputKeys"], label)
                guide = run["guide"]
                self.assertEqual(guide["keys"], skill["keys"])
                self.assertEqual([cell["token"] for cell in guide["steps"][step]], label.split("+"))
                terminal = label.split("+")[-1]
                text = skill["character"] or caps.get(terminal, {}).get(layout.lower(), "")
                run = chord(label, text, right=layout == "RU")
                if step + 1 < len(guide["steps"]):
                    self.assertEqual((run["solved"], run["step"]), (before, step + 1))
        self.assertEqual(run["solved"], before + 1, (skill, run["status"]))
        self.assertEqual(run["score"]["accuracy"], 100)
        self.assertEqual(run["score"]["substituteCount"], 0)


if __name__ == "__main__":
    unittest.main()
