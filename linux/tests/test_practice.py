import os
from pathlib import Path
import tempfile
import unittest

from platform_linux import Engine, EngineError, InputSources, PHYSICAL


class PracticeTest(unittest.TestCase):
    def setUp(self):
        self.data = tempfile.TemporaryDirectory()
        self.addCleanup(self.data.cleanup)
        self.old_root = os.environ.get("XDG_DATA_HOME")
        os.environ["XDG_DATA_HOME"] = self.data.name
        self.addCleanup(self.restore_env)
        self.engine = Engine(verify=True)
        self.addCleanup(lambda: self.engine.close())
        self.library = self.engine.call("configure", layout="EN", forward="Super+Space", backward="Super+Shift+Space")

    def restore_env(self):
        if self.old_root is None:
            os.environ.pop("XDG_DATA_HOME", None)
        else:
            os.environ["XDG_DATA_HOME"] = self.old_root

    def start(self, *skills, **options):
        return self.engine.call("shortcut-start", ids=skills, limit=options.pop("limit", 10), **options)

    def event(self, key, pressed=True, value=""):
        return self.engine.call("shortcut-key", key=key, pressed=pressed, text=value)

    def input_mode(self, state, mode):
        return self.engine.call("shortcut-input-mode", stepToken=state["stepToken"], mode=mode)

    def press(self, *keys, value=""):
        for key in keys:
            self.event(key, value=value if key == keys[-1] else "")
        for key in reversed(keys):
            state = self.event(key, False)
        return state

    def lookup(self, key):
        return next(item for item in self.engine.call("library")["skills"] if item["skill"]["id"] == key)

    def test_typing_stream_keeps_left_context_between_fragments(self):
        state = self.engine.call("typing-start", mode="English", level=0, layout="EN")
        first = state["after"]
        for character in first:
            state = self.engine.call("typing-key", text=character, layout="EN")
        self.assertTrue(state["before"].endswith(first))
        self.assertTrue(state["after"])

    def test_exact_key_order_and_modifiers(self):
        self.start("terminal-right-paste")
        self.assertEqual(self.press("LCtrl", "LShift", "V")["score"]["count"], 1)
        self.assertEqual(self.press("RCtrl", "LShift", "RAlt", "V")["score"]["correct"], 0)
        self.assertEqual(self.press("V", "RCtrl", "LShift")["score"]["correct"], 0)
        self.event("RCtrl")
        self.event("LShift")
        self.event("RCtrl", False)
        self.event("RCtrl")
        self.event("V")
        for key in ("V", "LShift", "RCtrl"):
            state = self.event(key, False)
        self.assertEqual(state["solved"], 0)
        self.event("RCtrl")
        self.event("LShift")
        pending = self.event("V")
        self.assertEqual(pending["solved"], 1)
        self.event("V")  # Autorepeat must not start another attempt.
        self.event("V", False)
        self.event("LShift", False)
        done = self.event("RCtrl", False)
        self.assertEqual((done["solved"], done["score"]["correct"], done["score"]["count"]), (1, 1, 5))
        self.assertIn("17 tests passed", done["verifiedScene"]["body"])

    def test_paste_repeats_with_held_modifiers_in_both_layouts(self):
        for layout, text in (("EN", "V"), ("RU", "М")):
            self.engine.call("configure", layout=layout, forward="Super+Space", backward="Super+Shift+Space")
            self.start("terminal-paste")
            self.event("RCtrl")
            self.event("RShift")
            for count in range(1, 4):
                state = self.event("V", value=text)
                self.assertEqual(state["solved"], count)
                self.assertEqual(state["score"]["verifiedResults"], count)
                self.assertEqual(self.event("V", value=text)["solved"], count)
                self.event("V", False)
            self.event("RCtrl", False)
            done = self.event("RShift", False)
            self.assertEqual(done["score"]["count"], 3)

    def test_lab_sessions_persist_separately_and_do_not_award_early_records(self):
        catalog = self.engine.call("lab-catalog")
        self.assertEqual([item["id"] for item in catalog["classes"]], ["ubuntu", "chrome", "code", "terminal", "workday"])
        state = self.engine.call("lab-start", app="chrome", mode="sandbox", level=1, seconds=180)
        self.assertTrue(state["paused"])
        self.assertEqual(state["remaining"], 180)
        state = self.engine.call("lab-resume")
        keys = [{"Ctrl": "RCtrl", "Shift": "LShift", "Alt": "LAlt", "Super": "LSuper"}.get(key, key)
                for key in state["task"]["keys"].split("+")]
        for _ in range(24):
            state = self.lab_chord(*keys, text="М")
            if state["solved"]:
                break
        self.assertEqual(state["solved"], 1)
        self.engine.call("lab-stop")
        result = self.engine.call("lab-result")
        self.assertFalse(result["newRecord"])
        self.assertFalse(result["completed"])
        self.assertEqual(result["record"], 0)
        self.assertEqual(len(result["history"]), 1)
        self.engine.close()
        self.engine = Engine(verify=True)
        self.engine.call("lab-start", app="chrome", mode="sandbox", level=1)
        self.assertEqual(self.engine.call("lab-result")["history"][0]["solved"], 1)
        self.engine.call("lab-start", app="ubuntu", mode="sandbox", level=1)
        self.assertEqual(self.engine.call("lab-result")["history"], [])

    def lab_chord(self, *keys, text=""):
        for key in keys:
            self.engine.call("lab-key", key=key, pressed=True, text=text if key == keys[-1] else "")
        for key in reversed(keys):
            state = self.engine.call("lab-key", key=key, pressed=False, text="")
        return state

    def open_live(self, app):
        self.engine.call("lab-start", app=app, mode="sandbox", level=0, seconds=0, seed=1)
        return self.engine.call("lab-resume")

    def test_independent_alternative_route_completes_exactly_at_limit(self):
        config = dict(app="chrome", mode="sandbox", level=0, guidance="independent",
                      mission="chrome-reader", seconds=0, actionLimit=3, seed=3)
        self.engine.call("lab-start", **config)
        initial = self.engine.call("lab-resume")
        self.assertFalse(initial["showGuide"])
        self.assertEqual(initial["task"]["id"], "url")
        # Open the comparison tab before copying the source. Both goals belong to this phase.
        created = self.lab_chord("RCtrl", "T")
        self.assertEqual(created["solved"], 1)
        self.lab_chord("RCtrl", "LShift", "Tab")
        self.lab_chord("RCtrl", str(initial["scene"]["windows"][0]["activeTab"] + 1))
        self.lab_chord("RCtrl", "F12")
        self.assertTrue(self.engine.call("lab-hint")["showGuide"])
        alias = self.lab_chord("LAlt", "D")
        address = alias["scene"]["windows"][0]
        self.assertFalse(alias["finished"])
        self.assertEqual(alias["solved"], 2)
        self.assertEqual((address["focus"], address["anchor"], address["cursor"]),
                         ("url", 0, len(address["url"])))
        self.lab_chord("Esc")
        final = self.lab_chord("RCtrl", "L")
        self.assertTrue(final["finished"])
        self.assertEqual((final["solved"], final["errors"]), (3, 2))
        self.assertEqual(final["task"]["id"], "copy")
        result = self.engine.call("lab-result")
        self.assertTrue(result["completed"])
        self.assertEqual(final["scenarioStep"], 4)
        self.assertEqual(final["scene"]["windows"][0]["focus"], "url")
        self.engine.close()
        self.engine = Engine(verify=True)
        drafts = self.engine.call("lab-catalog")["drafts"]
        self.assertFalse(any(item["app"] == "chrome" and item["level"] == 0 and
                             item["guidance"] == "independent" for item in drafts))
        fresh = self.engine.call("lab-start", **dict(config, **{"continue": True}))
        self.assertEqual((fresh["solved"], fresh["errors"], fresh["scenarioStep"]), (0, 0, 1))
        history = self.engine.call("lab-result")["history"]
        self.assertEqual([(item["solved"], item["errors"], item["completed"]) for item in history],
                         [(3, 2, True)])

    def test_live_omnibox_cut_respects_visible_range(self):
        state = self.open_live("chrome")
        clip = state["scene"]["clipboard"]
        url = state["scene"]["windows"][0]["url"]
        state = self.lab_chord("LCtrl", "L")
        w = state["scene"]["windows"][0]
        self.assertEqual((w["anchor"], w["cursor"]), (0, len(url)))
        self.lab_chord("Left")
        state = self.lab_chord("LCtrl", "X")
        self.assertEqual((state["scene"]["windows"][0]["input"], state["scene"]["clipboard"]), (url, clip))
        self.lab_chord("LShift", "Right")
        state = self.lab_chord("LCtrl", "X")
        self.assertEqual((state["scene"]["windows"][0]["input"], state["scene"]["clipboard"]), (url[1:], url[:1]))
        self.assertEqual(state["errors"], 0)

    def test_live_code_line_panel_and_undo(self):
        state = self.open_live("code")
        original = state["scene"]["windows"][0]["body"]
        line, rest = original.split("\n", 1)
        w = self.lab_chord("LCtrl", "L")["scene"]["windows"][0]
        self.assertEqual((w["anchor"], w["cursor"]), (0, len(line) + 1))
        self.assertEqual(self.lab_chord("LCtrl", "X")["scene"]["windows"][0]["body"], rest)
        self.assertEqual(self.lab_chord("LCtrl", "Z")["scene"]["windows"][0]["body"], original)
        self.lab_chord("Left")
        self.assertEqual(self.lab_chord("LCtrl", "X")["scene"]["clipboard"], line + "\n")
        self.assertTrue(self.lab_chord("LCtrl", "J")["scene"]["windows"][0]["panel"])
        self.assertFalse(self.lab_chord("LCtrl", "J")["scene"]["windows"][0]["panel"])
        self.assertEqual(self.lab_chord("LCtrl", "LShift", "X")["scene"]["windows"][0]["focus"], "extensions")

    def test_live_selected_middle_line_comment_delete_and_undo(self):
        first, middle, last = "import numpy as np", "def ifft(x): return x", "print('next')"
        full = "\n".join((first, middle, last))
        begin, end = len(first) + 1, len(first) + len(middle) + 2
        for control, reversed_selection in (("LCtrl", False), ("RCtrl", True)):
            with self.subTest(control=control, reversed=reversed_selection):
                self.open_live("code")
                def edit(*keys, text=""):
                    state = self.lab_chord(*keys, text=text)
                    window = next(w for w in state["scene"]["windows"] if w["active"])
                    return window["body"], window["bodyAnchor"], window["bodyCursor"]
                edit(control, "A")
                edit("A", text=first); edit("Enter"); edit("A", text=middle)
                edit("Enter"); edit("A", text=last)
                edit(control, "Home")
                self.assertEqual(edit("Down"), (full, begin, begin))
                if reversed_selection:
                    edit("Down")
                    self.assertEqual(edit("LShift", "Up"), (full, end, begin))
                    before, after = (end, begin), (end + 2, begin)
                else:
                    self.assertEqual(edit(control, "L"), (full, begin, end))
                    before, after = (begin, end), (begin, end + 2)
                commented = "\n".join((first, "# " + middle, last))
                self.assertEqual(edit(control, "/"), (commented, *after))
                self.assertEqual(edit(control, "Z"), (full, *before))
                self.assertEqual(edit(control, "LShift", "K"), (first + "\n" + last, begin, begin))
                self.assertEqual(edit(control, "Z"), (full, *before))
                self.engine.call("lab-stop")

    def test_code_diagnostic_test_checks_actual_saved_fixture(self):
        config = dict(app="code", mode="sandbox", level=1, mission="code-diagnostic-test",
                      seed=1, seconds=0, guidance="guided")
        self.assertEqual([m["id"] for m in self.engine.call("lab-catalog")["missions"]
                          if m["id"] == config["mission"]], [config["mission"]])
        self.engine.call("lab-start", **config)
        state = self.engine.call("lab-resume")
        def view(result):
            return next(w for w in result["scene"]["windows"] if w["active"])
        self.assertTrue(state["scene"]["diagnosticWorkspace"])
        self.assertEqual(view(state)["projectFiles"], ["test_units.py", "units.py"])
        self.assertEqual(state["scene"]["diagnosticProblemCount"], 1)
        self.assertEqual((view(state)["focus"], state["task"]["keys"]), ("code-problems", "F8"))
        self.assertIn("Неверный знак", view(state)["entries"][0]["title"])
        state = self.lab_chord("F8")
        self.assertEqual((state["solved"], view(state)["focus"], view(state)["bodyCursor"]),
                         (1, "body", len("def kelvin_to_celsius(kelvin):\n    return ")))
        self.assertFalse(state["paused"])
        self.assertTrue(view(state)["url"].endswith("/units.py"))
        state = self.lab_chord("RCtrl", ".")
        self.assertEqual((state["solved"], view(state)["focus"]), (2, "code-quick-fix"))
        self.assertEqual([e["path"] for e in view(state)["entries"]], ["round", "decimal"])
        state = self.lab_chord("Down")
        self.assertEqual(state["solved"], 3)
        self.lab_chord("Up")
        state = self.lab_chord("Enter")
        self.assertEqual(state["solved"], 3)
        self.assertIn("return round(kelvin - 273.15)", view(state)["body"])
        self.lab_chord("LCtrl", "S")
        self.lab_chord("LCtrl", "`")
        command = "python -m unittest test_units.UnitsTests.test_kelvin_to_celsius"
        def run(text):
            for c in text:
                self.lab_chord("Space" if c == " " else c.upper(), text=c)
            return self.lab_chord("Enter")
        wrong = run(command)
        self.assertFalse(wrong["scene"]["terminal"]["lastSuccess"])
        self.assertIn("ожидалось 27.5 °C", "\n".join(wrong["scene"]["terminal"]["output"]))
        self.assertIn("FAILED (failures=2)", "\n".join(wrong["scene"]["terminal"]["output"]))
        self.assertEqual(wrong["solved"], 3)
        self.lab_chord("LCtrl", "`")
        self.lab_chord("LCtrl", "Z")
        self.lab_chord("F8")
        self.lab_chord("LCtrl", ".")
        self.lab_chord("Down")
        state = self.lab_chord("Enter")
        self.assertEqual(state["solved"], 4)
        self.assertIn("return kelvin - 273.15", view(state)["body"])
        self.assertEqual(state["scene"]["diagnosticProblemCount"], 0)
        # Fix exists only in editor buffer. Real test still sees previous saved source.
        self.lab_chord("LCtrl", "`")
        unsaved = run(command)
        self.assertFalse(unsaved["scene"]["terminal"]["lastSuccess"])
        self.assertEqual(unsaved["solved"], 4)
        self.lab_chord("LCtrl", "`")
        self.assertEqual(self.lab_chord("RCtrl", "S")["solved"], 5)
        self.assertEqual(self.lab_chord("RCtrl", "`")["solved"], 6)
        rejected = run("python -c print")
        self.assertFalse(rejected["scene"]["terminal"]["lastSuccess"])
        self.assertEqual(rejected["solved"], 6)
        result = run(command)
        self.assertEqual((result["solved"], result["finished"]), (7, True))
        self.assertTrue(result["scene"]["terminal"]["lastSuccess"])
        self.assertIn("Ran 1 test", result["scene"]["terminal"]["output"])

    def test_code_line_exercise_uses_direct_catalog_and_exact_result(self):
        matches = [m for m in self.engine.call("lab-catalog")["missions"] if m["id"] == "code-line-edit"]
        self.assertEqual(len(matches), 1)
        self.engine.call("lab-start", app="code", mode="sandbox", level=0,
                         mission="code-line-edit", seconds=0, seed=1)
        state = self.engine.call("lab-resume")
        self.assertEqual(state["scenarioLength"], 5)
        self.assertEqual(state["task"]["id"], "select-line")
        # The same endpoint range, created without Ctrl+L, is not a shortcut credit.
        edit = self.lab_chord
        state = edit("LShift", "Down")
        self.assertEqual(state["solved"], 0)
        edit("Left")
        for count, chord in enumerate((("LCtrl", "L"), ("RCtrl", "/"), ("LCtrl", "Z"),
                                      ("RCtrl", "LShift", "K"), ("RCtrl", "Z")), 1):
            state = edit(*chord)
            self.assertEqual(state["solved"], count, state["status"])
        self.assertTrue(state["finished"])
        window = next(w for w in state["scene"]["windows"] if w["active"])
        self.assertEqual(window["url"], "/home/student/project/line-edit.py")
        first = "from tasks import add_task"
        middle = "def count_tasks(tasks): return len(tasks)"
        self.assertEqual(window["body"], first + "\n" + middle + "\nprint('next')")
        self.assertEqual((window["bodyAnchor"], window["bodyCursor"]),
                         (len(first) + 1, len(first) + len(middle) + 2))

    def test_live_word_navigation_keeps_grapheme_clusters(self):
        self.open_live("terminal")
        def edit(*keys, text=""):
            scene = self.lab_chord(*keys, text=text)["scene"]
            active = next(w for w in scene["windows"] if w["active"])
            return scene["terminalInput"], active["anchor"], active["cursor"]

        self.assertEqual(edit("A", text="кра\u0301й слово"), ("кра\u0301й слово", 11, 11))
        self.assertEqual(edit("LCtrl", "Left")[1:], (6, 6))
        self.assertEqual(edit("LCtrl", "Left")[1:], (0, 0))
        self.assertEqual(edit("LCtrl", "Right")[1:], (6, 6))
        self.assertEqual(edit("LCtrl", "LShift", "Left")[1:], (6, 0))
        self.assertEqual(edit("Delete"), ("слово", 0, 0))

        # Supplementary-plane letters participate in words; emoji is a delimiter.
        edit("End")
        edit("A", text=" 😀 𐐀x")
        self.assertEqual(edit("LCtrl", "Left")[1:], (9, 9))
        self.assertEqual(edit("LCtrl", "Left")[1:], (0, 0))
        self.assertEqual(edit("LCtrl", "Right")[1:], (9, 9))

        edit("Home")
        edit("LShift", "End")
        self.assertEqual(edit("A", text="foo_bar baz"), ("foo_bar baz", 11, 11))
        self.assertEqual(edit("LCtrl", "Left")[1:], (8, 8))
        self.assertEqual(edit("LCtrl", "Left")[1:], (4, 4))
        self.assertEqual(edit("LCtrl", "Right")[1:], (8, 8))

    def test_live_vertical_caret_and_reversed_grapheme_selection(self):
        self.open_live("code")
        def edit(*keys, text=""):
            window = next(w for w in self.lab_chord(*keys, text=text)["scene"]["windows"] if w["active"])
            return window["body"], window["anchor"], window["cursor"]

        edit("LCtrl", "A")
        edit("A", text="a")
        edit("Enter")
        edit("A", text="😀b")
        self.assertEqual(edit("LCtrl", "Home"), ("a\n😀b", 0, 0))
        self.assertEqual(edit("End")[1:], (1, 1))
        self.assertEqual(edit("Down")[1:], (4, 4))
        self.assertEqual(edit("LShift", "Left")[1:], (4, 2))
        self.assertEqual(edit("LShift", "Right")[1:], (4, 4))
        self.assertEqual(edit("LShift", "Right")[1:], (4, 5))
        self.assertEqual(edit("A", text="я\u0301"), ("a\n😀я\u0301", 6, 6))
        self.assertEqual(edit("Backspace"), ("a\n😀", 4, 4))
        self.assertEqual(edit("LShift", "Left")[1:], (4, 2))
        self.assertEqual(edit("Delete"), ("a\n", 2, 2))

        edit("A", text="е\u0301ж")
        edit("LCtrl", "Home")
        edit("End")
        self.assertEqual(edit("Down")[1:], (4, 4))  # after the one-grapheme е́, not between base/accent
        self.assertEqual(edit("Up")[1:], (1, 1))

    def test_live_insertion_never_leaves_caret_in_joined_grapheme(self):
        for app in ("code", "terminal", "chrome"):
            with self.subTest(app=app):
                self.open_live(app)
                def edit(*keys, text=""):
                    scene = self.lab_chord(*keys, text=text)["scene"]
                    window = next(w for w in scene["windows"] if w["active"])
                    value = (window["body"] if app == "code" else
                             scene["terminalInput"] if app == "terminal" else window["input"])
                    return value, window["anchor"], window["cursor"]
                if app == "chrome":
                    edit("LCtrl", "L")
                if app != "terminal":
                    edit("LCtrl", "A")
                self.assertEqual(edit("A", text="\u0301b"), ("\u0301b", 2, 2))
                self.assertEqual(edit("Home")[1:], (0, 0))
                self.assertEqual(edit("A", text="a"), ("a\u0301b", 2, 2))
                self.assertEqual(edit("Delete"), ("a\u0301", 2, 2))
                self.assertEqual(edit("Backspace"), ("", 0, 0))

                # Deleting a delimiter can also join its two neighbors.
                edit("A", text="🇺x🇸")
                edit("Home")
                self.assertEqual(edit("Right")[1:], (2, 2))
                self.assertEqual(edit("Delete"), ("🇺🇸", 4, 4))
                self.assertEqual(edit("Backspace"), ("", 0, 0))

                edit("A", text="b")
                edit("Home")
                self.assertEqual(edit("A", text="\u0600"), ("\u0600b", 2, 2))
                self.assertEqual(edit("Backspace"), ("", 0, 0))

    def test_pre_grapheme_draft_replays_old_edit_then_switches_navigation_once(self):
        import json
        import xml.etree.ElementTree as ET

        options = dict(app="code", mode="sandbox", level=0, seconds=0, seed=1, guidance="guided")
        self.engine.call("lab-start", **options)
        self.engine.call("lab-resume")
        self.lab_chord("LCtrl", "A")
        self.lab_chord("A", text="aa")
        self.lab_chord("Enter")
        self.lab_chord("A", text="😀b")
        self.lab_chord("LCtrl", "Home")
        self.lab_chord("End")
        self.lab_chord("Down")
        self.lab_chord("Delete")
        self.assertTrue(self.engine.call("lab-stop")["saved"])
        self.engine.close()

        # The same physical actions were valid before Unicode navigation, but
        # old Down retained two UTF-16 units rather than two whole symbols.
        path = Path(self.data.name) / "TouchTypingStudio-QA" / "progress.xml"
        profile = ET.parse(path)
        trace_tag = "./LiveDrafts/LiveDraft/Trace"
        trace = profile.find(trace_tag)
        events = json.loads(trace.text)
        old_events = [event for event in events if event["Key"] != "@cluster-navigation"]
        self.assertEqual(len(old_events), len(events) - 1)
        trace.text = json.dumps(old_events)
        profile.write(path, encoding="utf-8", xml_declaration=True)

        for _ in range(2):
            self.engine = Engine(verify=True)
            state = self.engine.call("lab-start", **options, **{"continue": True})
            window = next(w for w in state["scene"]["windows"] if w["active"])
            self.assertEqual((window["body"], window["cursor"], window["anchor"]), ("aa\n😀", 5, 5))
            self.assertTrue(self.engine.call("lab-stop")["saved"])
            events = json.loads(ET.parse(path).findtext(trace_tag))
            self.assertEqual([event["Key"] for event in events].count("@cluster-navigation"), 1)
            self.assertEqual(events[:-1], old_events)
            self.engine.close()
        self.engine = Engine(verify=True)

    def test_history_search_runs_long_command_without_retyping(self):
        self.engine.call("lab-start", app="terminal", mode="sandbox", level=0, seconds=0, mission="terminal-explore")
        self.engine.call("lab-resume")
        self.lab_chord("RCtrl", "R")
        for glyph in "cd /home":
            self.lab_chord("Space" if glyph == " " else glyph.upper(), text=glyph)
        state = self.lab_chord("Enter")
        self.assertEqual(state["scene"]["terminal"]["cwd"], "/home/student/project")
        self.assertEqual(state["solved"], 1)

    def test_live_terminal_caret_and_draft(self):
        for key, expected in (("Delete", "abd"), ("Backspace", "acd")):
            self.open_live("terminal")
            for c in "pwd":
                self.lab_chord(c.upper(), text=c)
            self.lab_chord("Enter")
            for c in "abcd":
                self.lab_chord(c.upper(), text=c)
            self.lab_chord("Up")
            self.assertEqual(self.lab_chord("Down")["scene"]["terminalInput"], "abcd")
            self.lab_chord("Left")
            self.lab_chord("Left")
            state = self.lab_chord(key)
            self.assertEqual(state["scene"]["terminalInput"], expected)
            self.assertEqual(state["scene"]["windows"][0]["cursor"], 2 if key == "Delete" else 1)

    def test_layout_verifies_changed_result(self):
        initial = self.start("layout-back")
        self.assertFalse(initial["needsSystem"])
        self.assertEqual(self.input_mode(initial, "substitute")["inputMode"], "exact")
        self.assertEqual(self.press("A", value="a")["activity"], "layout-after")
        unchanged = self.press("A", value="a")
        self.assertEqual(unchanged["score"]["correct"], 0)
        changed = self.press("A", value="ф")
        self.assertEqual(changed["score"]["verifiedResults"], 1)
        self.assertIn("EN → RU", changed["status"])

    def test_terminal_command_remains_in_scene(self):
        self.start("reboot")
        for letter in "reboot -f":
            self.press(letter.upper() if letter != " " else "Space", value=letter)
        self.assertEqual(self.press("Enter")["score"]["correct"], 0)
        for _ in range(3):
            self.press("Backspace")
        done = self.press("Enter")
        self.assertEqual(done["solved"], 1)
        self.assertTrue(done["verifiedScene"]["rebooted"])

    def test_order_and_clipboard_result(self):
        initial = self.start("screenshot-copy")
        self.assertEqual(initial["inputMode"], "waiting")
        self.assertEqual((initial["targetKeys"], initial["inputKeys"], initial["substituteKeys"]),
                         ("Print", "", "Ctrl+Shift+P"))
        untouched = self.press("Print")
        self.assertFalse(untouched["scene"]["captured"])
        self.assertEqual(untouched["score"]["count"], 0)
        self.input_mode(initial, "exact")
        self.assertEqual(self.press("LCtrl", "C")["solved"], 0)
        step = self.press("Print")
        self.assertTrue(step["scene"]["captured"])
        self.assertFalse(step["needsSystem"])
        self.assertEqual((step["targetKeys"], step["inputKeys"]), ("Ctrl+C", "Ctrl+C"))
        capture = step["scene"]["captureId"]
        self.assertTrue(capture)
        self.assertIsNone(step["scene"]["clipboardCaptureId"])
        self.assertNotEqual(step["scene"]["clipboard"], "Отчёт · 17 проверок")
        self.assertEqual(self.press("LCtrl", "V")["scene"]["captureId"], capture)
        done = self.press("LCtrl", "C")
        self.assertEqual(done["solved"], 1)
        self.assertEqual(done["verifiedScene"]["clipboard"], "Отчёт · 17 проверок")
        self.assertEqual(done["verifiedScene"]["clipboardCaptureId"], capture)
        self.assertIsNone(done["scene"]["captureId"])

        alternate = self.input_mode(done, "substitute")
        self.assertEqual(alternate["inputKeys"], "Ctrl+Shift+P")
        self.assertEqual([item["token"] for item in alternate["guide"]["steps"][0]], ["Ctrl", "Shift", "P"])
        step = self.press("LCtrl", "RShift", "P")
        self.assertNotEqual(step["scene"]["captureId"], capture)
        self.assertIsNone(step["scene"]["clipboardCaptureId"])
        done = self.press("LCtrl", "C")
        self.assertEqual(done["solved"], 2)
        self.assertEqual(done["score"]["correct"], 1)
        self.assertEqual(done["score"]["substituteVerifiedResults"], 1)
        self.assertEqual(done["verifiedScene"]["clipboardCaptureId"], step["scene"]["captureId"])

    def test_reserved_close_and_separate_scores_after_reopen(self):
        initial = self.start("window-close")
        self.assertTrue(initial["needsSystem"])
        self.assertEqual(initial["substituteKeys"], "Alt+J")
        self.assertTrue(self.press("LAlt", "F4")["scene"]["window"])
        self.input_mode(initial, "exact")
        self.event("LAlt")
        self.assertEqual(self.input_mode(initial, "exact")["held"], ["LAlt"])
        self.assertEqual(self.input_mode(initial, "waiting")["held"], [])
        self.input_mode(initial, "substitute")
        missed = self.press("LAlt", "F4")
        self.assertTrue(missed["scene"]["window"])
        self.assertEqual(missed["score"]["count"], 0)
        first = self.press("LAlt", "J")
        self.assertFalse(first["verifiedScene"]["window"])
        self.assertEqual(first["inputMode"], "waiting")
        stale = self.input_mode(initial, "exact")
        self.assertEqual(stale["inputMode"], "waiting")
        self.assertNotEqual(stale["stepToken"], initial["stepToken"])

        self.input_mode(first, "exact")
        done = self.press("RAlt", "F4")
        self.assertEqual(done["solved"], 2)
        result = self.engine.call("shortcut-result")
        self.assertEqual(result["score"]["accuracy"], 100)
        self.assertEqual(result["score"]["substituteCount"], 2)
        self.assertEqual(result["score"]["substituteCorrect"], 1)
        self.assertEqual(result["substituteTopMistakes"], [{"key": "F4", "count": 1}])
        self.assertEqual(len(result["series"]), 1)
        self.assertEqual(len(result["substituteSeries"]), 2)
        restarted = self.start("window-close")
        self.assertNotEqual(restarted["stepToken"], initial["stepToken"])
        self.engine.close()
        self.engine = Engine(verify=True)
        score = self.lookup("window-close")["score"]
        self.assertEqual((score["count"], score["substituteCount"]), (1, 2))

    def test_pause_discards_held_keys_without_counting_a_mistake(self):
        self.start("terminal-right-paste")
        self.event("RCtrl")
        self.event("RShift")
        paused = self.engine.call("shortcut-pause")
        self.assertTrue(paused["paused"])
        self.assertEqual(paused["held"], [])
        self.assertEqual(self.press("RCtrl", "RShift", "V")["solved"], 0)
        self.assertEqual(self.engine.call("shortcut-state")["seconds"], paused["seconds"])
        self.assertFalse(self.engine.call("shortcut-resume")["paused"])
        self.assertEqual(self.press("RCtrl", "RShift", "V")["score"]["correct"], 1)

    def test_windows_layout_binding_and_legacy_profile(self):
        self.engine.close()
        root = Path(self.data.name) / "TouchTypingStudio-QA"
        root.mkdir()
        (root / "progress.xml").write_text("""
          <StudioProfile>
            <ShortcutHistory>
              <ShortcutAttempt>
                <Skill>window-close</Skill><Verification>scene</Verification>
                <Correct>true</Correct><Milliseconds>140</Milliseconds>
              </ShortcutAttempt>
            </ShortcutHistory>
          </StudioProfile>
        """, encoding="utf-8")
        self.engine = Engine(verify=True)
        self.assertEqual(self.lookup("window-close")["score"]["correct"], 1)
        default = self.engine.call("configure", platform="windows", layout="EN")
        self.assertEqual((default["forward"], default["backward"]), ("Super+Space", "Super+Space"))
        with self.assertRaises(EngineError):
            self.engine.call("settings", forward="A → B")
        changed = self.engine.call("settings", forward="Shift+Alt", backward="Win+Shift+Space")
        self.assertEqual((changed["forward"], changed["backward"]), ("Alt+Shift", "Shift+Super+Space"))
        self.engine.close()
        self.engine = Engine(verify=True)
        reopened = self.engine.call("configure", platform="windows", layout="EN")
        self.assertEqual((reopened["forward"], reopened["backward"]), (changed["forward"], changed["backward"]))
        linux = self.engine.call("configure", platform="linux", layout="EN", forward="Super+Space",
                                 backward="Super+Shift+Space")
        self.assertEqual(linux["forward"], "Super+Space")

    def test_number_row_targets_match_the_displayed_finger_zones(self):
        row = self.library["keyboard"][0]
        keys = {item["key"]: item["finger"] for item in row}
        for digit, finger in zip("1234567890", (0, 0, 1, 2, 3, 3, 4, 5, 6, 7)):
            self.assertEqual(keys[digit], finger)
            self.assertEqual(self.start("digit-" + digit)["guide"]["steps"][0][0]["finger"], finger)
        self.assertEqual(self.start("alt-right")["guide"]["steps"][0][0]["finger"], 9)

    def test_physical_top_row_and_layout_symbols(self):
        self.assertEqual([PHYSICAL[index] for index in range(10, 20)], list("1234567890"))
        self.start("digit-9")
        self.assertEqual(self.press("NumPad9", value="9")["score"]["correct"], 0)
        self.assertEqual(self.press("9", value="9")["score"]["correct"], 1)
        self.engine.call("configure", layout="RU", forward="Super+Space", backward="Super+Shift+Space")
        self.start("symbol-RU-8470")
        self.assertEqual(self.press("RShift", "3", value="#")["solved"], 0)
        self.assertEqual(self.press("RShift", "3", value="№")["solved"], 1)

    def test_storage_editor_and_reloading(self):
        with self.assertRaises(EngineError):
            self.engine.call("shortcut-delete", id="terminal-paste")
        self.engine.call("shortcut-hide", id="terminal-paste", hide=True)
        item = self.lookup("terminal-paste")["skill"]
        self.assertTrue(item["hidden"])
        self.assertEqual(item["keys"], "Ctrl+Shift+V")
        reply = self.engine.call("shortcut-save", title="Моя вставка", group="Работа",
                                 keys="RCtrl+Shift+V", fingers="RCtrl=6\nLShift=0")
        own = next(item["skill"] for item in reply["skills"] if item["editable"])
        self.start(own["id"])
        self.press("RCtrl", "LShift", "V")
        root = Path(self.data.name) / "TouchTypingStudio-QA"
        self.assertTrue((root / "progress.xml").is_file())
        self.engine.close()
        self.engine = Engine(verify=True)
        own_item = self.lookup(own["id"])
        self.assertTrue(self.lookup("terminal-paste")["skill"]["hidden"])
        self.assertEqual(own_item["score"]["accuracy"], 100)
        self.assertEqual(own_item["skill"]["fingers"], "RCtrl=6\nLShift=0")
        self.engine.call("shortcut-delete", id=own["id"])
        self.assertFalse(any(item["editable"] for item in self.engine.call("library")["skills"]))

    def test_automatic_difficulty_and_studied_series(self):
        digits = [item["skill"]["id"] for item in self.library["skills"] if item["skill"]["group"] == "Цифровой ряд"]
        with self.assertRaises(EngineError):
            self.start(*digits, studied=True)
        state = self.start(*digits, auto=True, limit=25)
        seen = set()
        for _ in range(10):
            key = state["skill"]["keys"]
            seen.add(key)
            state = self.press(key, value=key)
        self.assertEqual(seen, set("0123456789"))
        self.assertEqual(state["tier"], 1)
        state = self.start(*digits, studied=True)
        self.assertIn(state["skill"]["keys"], seen)

    def test_project_typing_and_custom_text(self):
        for level in range(3):
            state = self.engine.call("typing-start", mode="Project", level=level, layout="EN")
            self.assertEqual(state["key"], "D")
            first = state["after"][0]
            self.assertEqual(self.engine.call("typing-key", text=first, layout="EN")["count"], 1)
            self.engine.call("typing-stop", layout="EN")
        library = self.engine.call("typing-save-set", title="Личный текст", content="abcd\n\nefgh")
        item = library["typing"]["sets"][-1]
        state = self.engine.call("typing-start", mode=item["id"], layout="EN")
        self.assertIn(state["after"].strip(), ("abcd", "efgh"))
        self.assertEqual(len(library["typingHistory"]), 3)

    def test_technique_hints_and_result_details(self):
        default = self.engine.call("inspect", keys="Ctrl+Shift+V")["steps"][0]
        self.assertEqual([(item["key"], item["finger"]) for item in default],
                         [("RCtrl", 6), ("RShift", 7), ("V", 3)])
        self.engine.call("settings", paired=False, keys=False)
        alternate = self.engine.call("inspect", keys="Ctrl+Shift+V")["steps"][0]
        self.assertEqual([(item["key"], item["finger"]) for item in alternate],
                         [("RCtrl", 7), ("LShift", 0), ("V", 3)])
        options = self.engine.call("settings", hands=True)
        self.assertFalse(options["showKeys"])
        self.assertTrue(options["showHands"])
        self.start("terminal-right-paste")
        self.press("LCtrl", "LShift", "V")
        self.press("RCtrl", "RShift", "V")
        result = self.engine.call("shortcut-result")
        self.assertEqual(result["score"]["accuracy"], 50)
        self.assertEqual(result["topMistakes"], [{"key": "LCtrl", "count": 1}])
        self.assertEqual(len(result["series"]), 2)

    def test_visual_and_hardware_settings_persist(self):
        self.assertTrue(self.library["showMistakeKey"])
        saved = self.engine.call("settings", mistakeKeys=False, fnMode="functions", keyboardProfile="matebook-d16")
        self.assertFalse(saved["showMistakeKey"])
        self.assertEqual(saved["fnMode"], "functions")
        self.assertEqual(saved["keyboardProfile"], "matebook-d16")
        again = self.engine.call("library")
        self.assertFalse(again["showMistakeKey"])
        with self.assertRaises(EngineError):
            self.engine.call("settings", fnMode="pretend-f11")
        self.assertEqual(self.engine.call("library")["fnMode"], "functions")
        reopened = Engine(verify=True)
        try:
            restored = reopened.call("library")
            self.assertFalse(restored["showMistakeKey"])
            self.assertEqual(restored["fnMode"], "functions")
        finally:
            reopened.close()

    def test_single_left_windows_key(self):
        ids = {item["skill"]["id"] for item in self.library["skills"]}
        self.assertIn("super-left", ids)
        self.assertNotIn("super-right", ids)
        guide = self.engine.call("inspect", keys="⊞+Space")
        self.assertEqual(guide["steps"][0][0]["key"], "LSuper")
        self.assertNotIn("RSuper", {item["key"] for item in guide["choices"]})

    def test_binding_parser(self):
        self.assertEqual(InputSources.as_chord("<Shift><Super>space"), "Shift+Super+Space")
        self.assertEqual(InputSources.as_chord("XF86Keyboard"), "")


if __name__ == "__main__":
    unittest.main(verbosity=2)
