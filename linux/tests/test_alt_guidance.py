"""One physical finger per simultaneous key, and beginner practice for each Alt."""
import os
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine, PhysicalKeyboard
from test_registered_keyboard import transitions


class AltGuidance(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        environment = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": folder.name})
        environment.start()
        self.addCleanup(environment.stop)
        self.engine = Engine(verify=True)
        self.addCleanup(self.engine.close)

    def guide(self, keys, **options):
        return self.engine.call("inspect", keys=keys, **options)

    @staticmethod
    def positions(guide):
        return [(row["key"], row["finger"]) for row in guide["steps"][0]]

    def test_alt_tab_uses_right_thumb_and_left_little_and_other_collisions_are_avoided(self):
        for platform in ("linux", "windows"):
            for layout in ("RU", "EN"):
                with self.subTest(platform=platform, layout=layout):
                    self.engine.call("configure", platform=platform, layout=layout, rightAltMode="alt")
                    a = self.guide("Alt+Tab")
                    self.assertEqual((a["keys"], a["note"], self.positions(a)),
                                     ("Alt+Tab", "", [("RAlt", 9), ("Tab", 0)]))
                    self.assertEqual(self.positions(self.guide("Ctrl+Tab")), [("RCtrl", 7), ("Tab", 0)])
                    self.assertEqual(self.positions(self.guide("Alt+Right")), [("LAlt", 8), ("Right", 7)])
                    for chord in ("Alt+Shift+Tab", "Ctrl+Shift+Tab", "Ctrl+Shift+F12", "Super+Space", "LCtrl+Tab", "Alt+Super+Space"):
                        items = self.positions(self.guide(chord))
                        fingers = {f for _, f in items}
                        self.assertTrue(-1 not in fingers and len(fingers) == len(items), (chord, items))
                    self.assertEqual(self.positions(self.guide("LAlt+Tab"))[0], ("LAlt", 8))
                    self.assertEqual(self.positions(self.guide("Alt+Tab", fingers="LAlt=8"))[0], ("LAlt", 8))
                    self.assertEqual(self.positions(self.guide("Ctrl+Shift+V")), [("RCtrl", 6), ("RShift", 7), ("V", 3)])

    def test_altgr_and_unconfirmed_native_layouts_do_not_get_right_alt_as_alt(self):
        for capability in ("altgr", "unknown"):
            with self.subTest(capability=capability):
                a = self.guide("Alt+Tab", rightAltMode=capability, fingers="RAlt=9")
                self.assertEqual(self.positions(a), [("LAlt", 8), ("Tab", 0)])
                self.assertIn("левый Alt", a["note"])
                explicit = self.guide("RAlt+D", rightAltMode=capability)
                self.assertEqual(self.positions(explicit), [("RAlt", 9), ("D", 2)])
                self.assertIn("не подтверждён", explicit["note"])

    def test_default_hotkeys_catalog_preserves_originals_and_added_sides_are_exact(self):
        baseline = {"Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+S", "Ctrl+A", "F2"}
        for platform in ("linux", "windows"):
            for layout in ("RU", "EN"):
                for mode in ("alt", "altgr"):
                    with self.subTest(platform=platform, layout=layout, mode=mode):
                        self.engine.call("configure", platform=platform, layout=layout, rightAltMode=mode)
                        s = self.engine.call("typing-start", mode="Hotkeys", layout=layout, seed=5)
                        before = set()
                        for _ in range(14):
                            before.add(s["expected"])
                            s = transitions("typing-physical", s["expected"], self.engine.call)
                        self.assertEqual(before, baseline)
                        self.assertEqual((s["progressDone"], s["expected"]), (14, "LAlt+Right"))
                        self.assertEqual(s["expectedTargets"][0], {"key": "LAlt", "finger": 8})
                        self.assertIn("Вперёд по истории", s["expectedNote"])
                        self.assertEqual(transitions("typing-physical", "RAlt+Right", self.engine.call)["progressDone"], 14)
                        s = transitions("typing-physical", "LAlt+Right", self.engine.call)
                        self.assertEqual((s["progressDone"], s["expected"]), (15, "RAlt+D" if mode == "alt" else "LAlt+Left"))
                        if mode == "alt":
                            self.assertEqual(s["expectedTargets"], [{"key": "RAlt", "finger": 9}, {"key": "D", "finger": 2}])
                            self.assertIn("адресную строку", s["expectedNote"])
                            self.assertEqual(transitions("typing-physical", "LAlt+D", self.engine.call)["progressDone"], 15)
                            stopped = self.engine.call("typing-state", rightAltMode="altgr")
                            self.assertIn("не подтверждён", stopped["expectedNote"])
                            failed = transitions("typing-physical", "RAlt+D", self.engine.call)
                            self.assertEqual((failed["progressDone"], failed["errors"]), (15, stopped["errors"]))
                            self.engine.call("typing-state", rightAltMode="alt")
                        s = transitions("typing-physical", s["expected"], self.engine.call)
                        self.assertEqual(s["progressDone"], 16)
                        self.assertEqual(s["held"], [])

    def test_linux_queries_right_alt_role_from_real_position_not_the_keyboard_language_name(self):
        gdk = SimpleNamespace(keyval_name=lambda value: value)
        mode = SimpleNamespace(name="GdkX11Display")
        native_group = [0]
        device = SimpleNamespace(get_active_layout_index=lambda: native_group[0])
        report = SimpleNamespace(__gtype__=mode, get_name=lambda: ":22",
                                 get_default_seat=lambda: SimpleNamespace(get_keyboard=lambda: device))
        with patch("platform_linux._x11_physical_codes", return_value={113: "RAlt", 38: "A"}):
            keyboard = PhysicalKeyboard(gdk, report)
        def map_keycode(code):
            self.assertEqual(code, 113)
            return True, [SimpleNamespace(group=0, level=0), SimpleNamespace(group=0, level=1)], ["Alt_R", "Meta_R"]
        report.map_keycode = map_keycode
        self.assertEqual(keyboard.right_alt_mode(), "alt")
        report.map_keycode = lambda _: (True, [SimpleNamespace(group=i, level=0) for i in range(2)], ["Alt_R", "ISO_Level3_Shift"])
        self.assertEqual(keyboard.right_alt_mode(), "alt")
        native_group[0] = 1
        self.assertEqual(keyboard.right_alt_mode(), "altgr")
        report.map_keycode = lambda _: (True, [SimpleNamespace(group=0, level=0)], ["Super_R"])
        self.assertEqual(keyboard.right_alt_mode(), "unknown")


if __name__ == "__main__":
    unittest.main()
