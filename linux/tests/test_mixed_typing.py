"""Real shared-engine input protocol (no GUI, keyboard hardware or OS-level interception implied)."""
import os
import tempfile
import unittest
from unittest.mock import patch
from platform_linux import Engine, EngineError, PHYSICAL


class MixedTypingTest(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": tmp.name})
        env.start()
        self.addCleanup(env.stop)
        self.engine = Engine(verify=True)
        self.addCleanup(lambda: self.engine.close())

    def start(self, mode, layout="RU", level=0, **options):
        self.os = layout
        self.engine.call("configure", platform="linux", layout=layout,
                         forward="Super+Space", backward="Super+Shift+Space")
        return self.engine.call("typing-start", mode=mode, level=level, layout=layout, **options)

    def key(self, key, down=True, **args):
        return self.engine.call("typing-physical", key=key, pressed=down, layout=self.os, **args)

    def text(self, value):
        return self.engine.call("typing-key", text=value, layout=self.os)

    def chord(self, value, alias=None, side="L", emit=None):
        keys = [side + x if x in ("Ctrl", "Alt", "Shift", "Super") else x for x in value.split("+")]
        if alias:
            keys[-1] = alias
        for k in keys:
            s = self.key(k)
        if emit:
            s = self.text(emit)
        for k in reversed(keys):
            s = self.key(k, False)
        return s

    @staticmethod
    def glyph(s):
        return {"·": " ", "⇥": "\t", "↵": "\n"}.get(s["expected"], s["expected"])

    def answer(self, s):
        if s["expectedKind"] == "command":
            return self.chord(s["expected"], side="R")
        return self.text(self.glyph(s))

    def test_chords_are_physical_and_layout_independent(self):
        for os in ("RU", "EN"):
            with self.subTest(os=os):
                s = self.start("Hotkeys", layout=os)
                seen = {}
                for index in range(29):  # baseline, side-specific Alt, everyday, and additional Alt
                    command = s["expected"]
                    layout = s["wantedLayout"]
                    self.assertEqual((s["expectedKind"], s["progressDone"]), ("command", index))
                    for glyph in command:
                        self.assertEqual(self.text(glyph)["progressDone"], index)
                    self.assertEqual(self.text(s["after"])["progressDone"], index)
                    seen.setdefault(command, set()).add(layout)
                    if command == "Ctrl+V":
                        self.assertEqual(s["expectedKeys"], ["LCtrl", "V"])
                        self.assertEqual([t["key"] for t in s["expectedTargets"]], s["expectedKeys"])
                        s = self.chord("Ctrl+B", emit="и") # the physical key labelled И in RU, not М
                        self.assertEqual(s["progressDone"], index)
                        # Either side; wrong OS character must not change chord matching.
                        self.key("RCtrl"); s=self.key("V", text="м")
                        self.assertEqual(s["progressDone"], index+1)
                        self.assertEqual(self.key("V")["progressDone"], index+1)
                        self.assertEqual(self.text("м")["progressDone"], index+1)
                        self.key("V", False); s = self.key("RCtrl", False)
                    else:
                        s = self.chord(command, side="R")
                    self.assertEqual(s["progressDone"], index + 1)
                self.assertEqual(seen["Ctrl+V"], {"RU", "EN"})
                self.assertEqual(seen["Ctrl+C"], {"RU", "EN"})
                self.assertEqual(seen["F2"], {"RU", "EN"})
                self.assertTrue({"LAlt+Right", "Alt+Home", "Alt+Right"} <= seen.keys())
        # Layout independent physical key positions of the laptop's ANSI rows.
        self.assertEqual([PHYSICAL[k] for k in (12, 55, 56, 68, 79)], ["3", "V", "B", "F2", "NumPad7"])

    def test_modifier_only_native_layout_binding_never_becomes_a_command_or_miss(self):
        for platform in ("windows", "linux"):
            self.os = "RU"
            self.engine.call("configure", platform=platform, layout=self.os, forward="Alt+Shift", backward="Ctrl+Shift")
            for mode in ("Digits", "Hotkeys"):
                with self.subTest(platform=platform, mode=mode):
                    s = self.engine.call("typing-start", mode=mode, layout=self.os, level=2, seed=3)
                    for modifier in ("LAlt", "LShift"):
                        s = self.key(modifier)
                        self.assertFalse(s["consumePhysical"], (s["held"], modifier))
                    for modifier in ("LShift", "LAlt"):
                        s = self.key(modifier, False)
                    self.assertEqual((s["progressDone"], s["errors"], s["held"]), (0, 0, []))
                    for modifier in ("RCtrl", "RShift"):
                        s = self.key(modifier)
                        self.assertFalse(s["consumePhysical"], (s["held"], modifier))
                    for modifier in ("RShift", "RCtrl"):
                        s = self.key(modifier, False)
                    # Wrong modifiers + a non-layout trigger are not excused.
                    if mode == "Hotkeys":
                        self.assertEqual(self.chord("Alt+Shift+F8")["errors"], 1)
                    n = s["errors"] + (mode == "Hotkeys")
                    s = self.answer(s)
                    self.assertEqual((s["progressDone"], s["errors"], s["held"]), (1, n, []))

    def test_toprow_symbols_and_keypad_text_are_composed_not_physical(self):
        for layout, expected_sign in (("RU", "№"), ("EN", "#")):
            with self.subTest(layout=layout):
                s = self.start("TopRow", layout)
                seen = False
                for _ in range(20):
                    if self.glyph(s) == expected_sign:
                        seen = True
                        self.assertEqual(s["expectedKeys"][-1], "3")
                        self.assertIn(s["expectedKeys"][0], ("LShift", "RShift"))
                        i = s["progressDone"]; alt = "#" if layout == "RU" else "№"
                        self.key("LShift"); self.key("3")
                        self.assertEqual(self.text(alt)["progressDone"], i)
                        self.assertEqual(self.text(expected_sign)["progressDone"], i + 1)
                        self.key("3", False); s=self.key("LShift", False)
                        break
                    s = self.answer(s)
                self.assertTrue(seen)
        s = self.start("Digits")
        digit = self.glyph(s)
        for k in ("NumLock", "NumPad" + digit):
            self.key(k); s=self.key(k, False)
        self.assertEqual(s["progressDone"], 0) # navigation/NumLock-off emitted no character
        self.key("NumPad" + digit); self.assertEqual(self.text(digit)["progressDone"], 1)
        self.key("NumPad" + digit, False)
        self.assertEqual(self.text("0123456789")["progressDone"], 1)

    def test_multiple_modifiers_are_exact_and_function_row_is_not_a_character(self):
        for layout in ("EN","RU"):
            s = self.start("Hotkeys", layout=layout, level=2)
            seen=set()
            for _ in range(16):
                wanted=s["expected"]; i=s["progressDone"]
                if wanted == "Ctrl+Shift+P":
                    self.assertEqual(s["expectedKeys"],["LCtrl","LShift","P"])
                    self.assertEqual(self.chord("Ctrl+P")["progressDone"], i)
                    self.assertEqual(self.chord("Ctrl+Shift+Alt+P")["progressDone"],i)
                if wanted == "F12":
                    self.assertEqual(s["expectedKeys"],["F12"])
                    self.assertEqual(self.text("F12")["progressDone"],i)
                if wanted == "Ctrl+2":
                    self.assertEqual(self.chord("Ctrl+NumPad2")["progressDone"],i)
                seen.add(wanted)
                s=self.answer(s); self.assertEqual(s["progressDone"],i+1)
            self.assertTrue({"Ctrl+Shift+P", "F12","Ctrl+2"} <= seen)

    def test_opt_in_system_shortcuts_require_capture_on_start_resume_and_draft(self):
        with self.assertRaises(EngineError):
            self.start("SystemHotkeys")
        normal = self.start("Hotkeys")
        for _ in range(26):
            self.assertNotIn("Super", normal["expected"])
            normal = self.answer(normal)
        self.os = "EN"
        for platform, expected in (("linux", "Super+H"), ("windows", "Super+E")):
            with self.subTest(platform=platform):
                self.engine.call("configure", platform=platform, layout="EN")
                s = self.engine.call("typing-start", mode="SystemHotkeys", systemCapture=True, layout="EN", seed=17,
                                     infinite=True, cycles=5, seconds=300)
                self.assertEqual((s["mode"], s["progressTotal"], s["cycles"], s["timeLimit"]),
                                 ("SystemHotkeys", 36, 1, 0))
                seen = set()
                while s["progressDone"] < 9:
                    wanted = s["expected"]
                    keys = ["L" + name if name in ("Alt", "Super", "Ctrl", "Shift") else name for name in wanted.split("+")]
                    before = s["progressDone"]
                    for key in keys:
                        s = self.key(key)
                    self.assertEqual(s["progressDone"], before + 1)
                    self.assertEqual(self.key(keys[-1])["progressDone"], before + 1)
                    for key in reversed(keys):
                        s = self.key(key, False)
                    seen.add(wanted)
                self.assertIn(expected, seen)
                self.assertIn("Alt+Tab", seen)
                self.assertEqual(s["progressDone"], 9)
                self.engine.call("typing-pause", layout="EN")
                with self.assertRaises(EngineError):
                    self.engine.call("typing-resume", layout="EN")
                s = self.engine.call("typing-resume", layout="EN", systemCapture=True)
                self.assertTrue(s["running"])
                self.engine.call("typing-suspend", layout="EN")
                with self.assertRaises(EngineError):
                    self.engine.call("typing-continue", layout="EN")
                s = self.engine.call("typing-continue", layout="EN", systemCapture=True)
                self.assertEqual(s["progressDone"], 9)

    def test_mixed_switches_and_restarts_without_crediting_paste_or_layout_switch(self):
        s=self.start("WordsDigitsHotkeys", cycles=2)
        total = s["progressTotal"]; self.assertEqual(total,sum(s["cycleLengths"]))
        seen=set()
        for _ in range(24):
            seen.add((s["expectedKind"],s["wantedLayout"]))
            i=s["progressDone"]
            if s["expectedKind"] == "text":
                before_errors=s["errors"]
                s=self.chord("Super+Space")
                self.assertEqual((s["progressDone"],s["errors"]),(i,before_errors))
                self.assertEqual(self.text(s["after"]+" to paste")["progressDone"],i)
            s=self.answer(s); self.assertEqual(s["progressDone"],i+1)
        self.assertEqual(seen,{("text","EN"),("text","RU"),("command","RU"),("command","EN")})
        snapshot=(s["progressDone"],s["expected"],s["after"],s["progressTotal"],s["cycleLengths"])
        self.engine.call("typing-pause")
        self.assertEqual(self.answer(s)["progressDone"], snapshot[0])
        self.engine.close(); self.engine=Engine(verify=True)
        s=self.engine.call("typing-continue",layout=self.os)
        self.assertEqual((s["progressDone"],s["expected"],s["after"],s["progressTotal"],s["cycleLengths"]),snapshot)
        self.assertEqual(self.answer(s)["progressDone"],snapshot[0]+1)


    def test_other_process_finished_same_mixed_attempt_cannot_resurrect_or_duplicate(self):
        s = self.start("Digits", seed=17)
        s = self.answer(s)
        self.engine.call("typing-pause")
        first = self.engine.call("library")["typing"]["draft"]
        self.assertEqual((first["id"],first["done"]), (s["id"],1))

        other = Engine(verify=True)
        self.addCleanup(other.close)
        same = other.call("typing-continue", id=first["id"])
        while not same["finished"]:
            same = other.call("typing-key", text=self.glyph(same), layout="RU")
        self.assertIsNone(other.call("library")["typing"]["draft"])
        # Closing the stale paused original once (and EOF again), or pressing an
        # old card, must not put the already-completed ID back in the catalog.
        self.engine.call("typing-suspend")
        self.engine.call("typing-stop")
        self.engine.close()
        self.engine = Engine(verify=True)
        with self.assertRaises(EngineError):
            self.engine.call("typing-continue", id=first["id"])
        current = self.engine.call("library")
        self.assertIsNone(current["typing"]["draft"])
        self.assertEqual(len(current["typingHistory"]), 1)
        self.assertEqual(current["typingHistory"][0]["count"], same["progressDone"])

    def test_stale_mixed_card_does_not_continue_another_draft(self):
        s = self.start("Digits")
        s = self.answer(s)
        self.engine.call("typing-pause")
        old = s["id"]
        newer = Engine(verify=True); self.addCleanup(newer.close)
        replacement = newer.call("typing-start", mode="WordsDigits", layout="EN")
        replacement = newer.call("typing-key", text=self.glyph(replacement))
        newer.call("typing-suspend")
        with self.assertRaisesRegex(EngineError, "изменился|завершён"):
            self.engine.call("typing-continue", id=old)
        correct = self.engine.call("typing-continue", id=replacement["id"])
        self.assertEqual((correct["mode"],correct["progressDone"]), ("WordsDigits",1))

    def test_cycle_boundary_and_infinite_counter(self):
        for infinite in (False,True):
            s=self.start("Digits",cycles=2,infinite=infinite)
            limit=s["cycleLengths"][0] if not infinite else 550
            for i in range(limit):
                s=self.answer(s)
                self.assertEqual(s["progressDone"],i+1)
            self.assertEqual(s["cycle"],2)
            self.assertFalse(s["finished"])
            if infinite:
                self.assertIsNone(s["progressTotal"])
                self.assertFalse(s["progressVisible"])
            else:
                self.assertEqual(s["progressTotal"],sum(s["cycleLengths"]))
                while not s["finished"]: s=self.answer(s)
                self.assertEqual(s["progressDone"],s["progressTotal"])

if __name__=="__main__":
    unittest.main()
