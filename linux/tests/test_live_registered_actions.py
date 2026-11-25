"""Exercise named registry actions that need not be a goal in a seeded route.

These events use the real LabScene protocol and check independent state effects,
not just the authoring descriptor or a successful parse.
"""
import os
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_registered_keyboard import transitions


class RegisteredLiveEffects(unittest.TestCase):
    def cases(self):
        for self.platform in ("linux", "windows"):
            for self.language in ("RU", "EN"):
                with self.subTest(platform=self.platform, layout=self.language), TemporaryDirectory() as folder, \
                        patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": folder}):
                    self.eng = Engine(verify=True)
                    try:
                        self.eng.call("configure", platform=self.platform, layout=self.language, forward="Super+Space")
                        yield
                    finally:
                        self.eng.close()

    def scene(self, app, mission, level=0, env="ubuntu"):
        self.eng.call("lab-start", app=app, mission=mission, level=level, seconds=0, mode="sandbox", seed=0,
                      nativeEnglishInput=True, desktopProfile=env, guidance="guided")
        return self.eng.call("lab-resume")

    def stroke(self, label, text=""):
        def send(operation, **args):
            if args.get("text"):
                args["nativeLayout"] = "EN" if text.isascii() else "RU"
            return self.eng.call(operation, **args)
        return transitions("lab-key", label, send, text, right=self.language == "RU")

    def type_ascii(self, text):
        for c in text:
            key = "Space" if c == " " else c.upper()
            state = self.stroke(key, c)
        return state

    def active(self, state):
        return next(w for w in state["scene"]["windows"] if w["active"])

    def test_chrome_zoom_refresh_bookmarks_and_next_find(self):
        for _ in self.cases():
            s = self.scene("chrome", "chrome-reader")
            original = self.active(s)
            s = self.stroke("Ctrl+-")
            self.assertEqual((self.active(s)["zoom"], self.active(s)["url"]), (original["zoom"] - 10, original["url"]))
            s = self.stroke("Ctrl+R")
            self.assertEqual((self.active(s)["status"], self.active(s)["url"]), ("Страница обновлена", original["url"]))
            s = self.stroke("Ctrl+Shift+B")
            self.assertEqual(self.active(s)["bookmarks"], not original["bookmarks"])
            s = self.stroke("Ctrl+F")
            self.assertEqual(self.active(s)["focus"], "find")
            body = self.active(s)["body"].lower()
            terms = [c for c in "abcde" if body.count(c) >= 2]
            # Neutral Chrome materials have repeated Russian letters, too.
            if terms:
                a = terms[0]
                s = self.type_ascii(a)
            else:
                a = "о"
                self.assertGreaterEqual(body.count(a), 2)
                s = self.stroke("J", a)  # Russian О is on physical J
            s = self.stroke("Enter")
            first = self.active(s)
            self.assertEqual(first["find"], a)
            s = self.stroke("Ctrl+G")
            second = self.active(s)
            self.assertNotEqual(first["bodyCursor"], second["bodyCursor"])
            self.assertEqual(body[second["bodyAnchor"]:second["bodyCursor"]], a)
            self.assertEqual(second["url"], original["url"])

    def test_terminal_word_moves_kill_copy_and_reverse_search_use_command_buffer(self):
        for _ in self.cases():
            s = self.scene("terminal", "terminal-essentials-locate")
            self.assertEqual(self.active(s)["app"], "terminal")
            self.type_ascii("pwd")
            s = self.stroke("Enter")
            self.assertEqual(s["scene"]["terminalInput"], "")
            s = self.type_ascii("abc def")
            self.assertEqual(self.active(s)["cursor"], 7)
            s = self.stroke("Alt+B")
            self.assertEqual((s["scene"]["terminalInput"], self.active(s)["cursor"]), ("abc def", 4))
            s = self.stroke("Alt+F")
            self.assertEqual((s["scene"]["terminalInput"], self.active(s)["cursor"]), ("abc def", 7))
            self.stroke("Alt+B")
            s = self.stroke("Ctrl+K")
            self.assertEqual(s["scene"]["terminalInput"], "abc ")
            self.stroke("Shift+Home")
            s = self.stroke("Ctrl+Shift+C")
            self.assertEqual(s["scene"]["clipboard"], "abc ")
            self.stroke("Ctrl+R")
            s = self.type_ascii("p")
            self.assertEqual(self.active(s)["focus"], "reverse-search")
            self.assertEqual(s["scene"]["terminalInput"], "pwd")

    def test_second_editor_group_and_reverse_window_switch(self):
        for _ in self.cases():
            s = self.scene("code", "code-guide")
            self.assertEqual(self.active(s)["app"], "code")
            s = self.stroke("Ctrl+\\")
            self.assertTrue(self.active(s)["split"])
            s = self.stroke("Ctrl+1")
            self.assertEqual(self.active(s)["group"], 0)
            s = self.stroke("Ctrl+2")
            self.assertEqual(self.active(s)["group"], 1)
            s = self.stroke("Ctrl+G")
            self.assertEqual(self.active(s)["focus"], "goto-line")

            s = self.scene("ubuntu", "ubuntu-gnome-same-app", level=1)
            prior = self.active(s)["id"]
            self.assertGreaterEqual(sum(w["workspace"] == s["scene"]["workspace"] for w in s["scene"]["windows"]), 2)
            s = self.stroke("Alt+Shift+Tab")
            self.assertNotEqual(self.active(s)["id"], prior)
            self.assertFalse(s["scene"]["switcher"])  # released Alt commits a real simulated window choice

    def test_notebook_single_letter_commands_follow_focus_not_cyrillic_glyphs(self):
        from test_editor_notebooks import advance, options
        for _ in self.cases():
            classes = self.eng.call("lab-catalog")["classes"]
            editor_class = next(c for c in classes if c["id"] == "code")
            if not {"A", "C", "V", "X", "Y"}.issubset({t["keys"] for t in editor_class["shortcuts"]}):
                self.skipTest("The preceding preview did not advertise notebook command-mode structural shortcuts.")
            s = self.eng.call("lab-start", **options(0, 0))
            s = self.eng.call("lab-resume")
            for _ in range(2):
                s = advance(self.eng, s)
            cap = dict(A="ф", C="с", V="м", X="ч", Y="н", M="ь")
            def letter(key):
                return self.stroke(key, cap[key] if self.language == "RU" else key.lower())
            def notebook(state):
                return self.active(state)["notebook"]

            n = notebook(s)
            before = len(n["cells"])
            self.assertFalse(n["editing"])
            n = notebook(letter("A"))
            self.assertEqual((len(n["cells"]), n["cells"][n["selected"]]["source"]), (before + 1, ""))
            self.assertEqual(notebook(letter("M"))["cells"][n["selected"]]["kind"], "markdown")
            self.assertEqual(notebook(letter("Y"))["cells"][n["selected"]]["kind"], "code")

            n = notebook(self.stroke("Down"))
            selected = n["cells"][n["selected"]]
            self.assertTrue(selected["source"])
            s = letter("C")
            self.assertEqual(s["scene"]["clipboard"], selected["source"])
            less = notebook(letter("X"))
            self.assertEqual(len(less["cells"]), before)
            self.assertNotIn(selected["id"], [c["id"] for c in less["cells"]])
            n = notebook(letter("V"))
            paste = n["cells"][n["selected"]]
            self.assertEqual((len(n["cells"]), paste["source"], paste["kind"]),
                             (before + 1, selected["source"], selected["kind"]))
            self.assertNotEqual(paste["id"], selected["id"])

            # Enter edits a selected cell; the previous paste may already have
            # entered editing. Explicitly prove the two contexts separately.
            n = notebook(self.stroke("Esc"))
            self.assertFalse(n["editing"])
            n = notebook(self.stroke("Enter"))
            self.assertTrue(n["editing"])
            self.assertEqual(n["cells"][n["selected"]]["source"], selected["source"])
            n = notebook(self.stroke("Home"))
            at = n["caret"]
            n = notebook(letter("A"))
            self.assertTrue(n["editing"])
            self.assertEqual(len(n["cells"]), before + 1)
            mark = cap["A"] if self.language == "RU" else "a"
            self.assertEqual(n["cells"][n["selected"]]["source"], selected["source"][:at] + mark + selected["source"][at:])
            self.assertEqual((n["caret"], n["anchor"]), (at + 1, at + 1))


if __name__ == "__main__":
    unittest.main()
