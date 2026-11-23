"""Verify exact visible boundaries and credit through the real shared engine process."""
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine

SOURCE = """def total_cost(unit_price, quantity, flat_fee):
    marker = flat_fee
    subtotal = unit_price * quantity
    tax = 2
    fee = 1
    if quantity > 0:
        return cost
    return 0
"""
HEADER, MARKER, SUBTOTAL, TAX, FEE, CONDITION, RETURN, FALLBACK = SOURCE.splitlines(keepends=True)
a = len(HEADER + MARKER)
b = a + len(SUBTOTAL)
c = b + len(TAX)
d = c + len(FEE)
e = d + len(CONDITION) - 1
name = b - len("quantity\n")
STATEMENT = "cost = subtotal + fee + tax + marker"
SUITE = "        " + STATEMENT + "\n"
BLANK = SOURCE[:e] + "\n        " + SOURCE[e:]
WRITTEN = HEADER + MARKER + SUBTOTAL + TAX + FEE + CONDITION + SUITE + RETURN + FALLBACK
suite_end = e + len(SUITE)
INSERT_BELOW = WRITTEN[:suite_end] + "\n        " + WRITTEN[suite_end:]
INSERT_ABOVE = WRITTEN[:e + 1] + "        \n" + WRITTEN[e + 1:]
REMOVED = WRITTEN[:name] + WRITTEN[b - 1:]
INDENTED = HEADER + MARKER + "    " + SUBTOTAL + "    " + TAX + FEE + CONDITION + SUITE + RETURN + FALLBACK
MOVED = HEADER + SUBTOTAL + TAX + MARKER + FEE + CONDITION + SUITE + RETURN + FALLBACK
STEPS = [
    ("Enter", BLANK, e + 9, e + 9),
    ("<write>", WRITTEN, suite_end, suite_end),
    ("Left", WRITTEN, suite_end - 1, suite_end - 1),
    ("Ctrl+Enter", INSERT_BELOW, suite_end + 9, suite_end + 9),
    ("Ctrl+Z", WRITTEN, suite_end - 1, suite_end - 1),
    ("Ctrl+Shift+Enter", INSERT_ABOVE, e + 9, e + 9),
    ("Ctrl+Z", WRITTEN, suite_end - 1, suite_end - 1),
    ("Up", WRITTEN, e, e),
    ("Up", WRITTEN, d - 1, d - 1),
    ("Up", WRITTEN, c - 1, c - 1),
    ("Up", WRITTEN, a + len(TAX) - 1, a + len(TAX) - 1),
    ("End", WRITTEN, b - 1, b - 1),
    ("Ctrl+Shift+Left", WRITTEN, name, b - 1),
    ("Ctrl+Backspace", REMOVED, name, name),
    ("Ctrl+Z", WRITTEN, name, b - 1),
    ("Ctrl+Right", WRITTEN, b - 1, b - 1),
    ("Ctrl+Backspace", REMOVED, name, name),
    ("Ctrl+Z", WRITTEN, b - 1, b - 1),
    ("Ctrl+Left", WRITTEN, name, name),
    ("Ctrl+Delete", REMOVED, name, name),
    ("Ctrl+Z", WRITTEN, name, name),
    ("Ctrl+Shift+Right", WRITTEN, b - 1, name),
    ("Home", WRITTEN, a, a),
    ("Shift+Down", WRITTEN, b, a),
    ("Shift+Down", WRITTEN, c, a),
    ("Tab", INDENTED, c + 8, a),
    ("Shift+Tab", WRITTEN, c, a),
    ("Alt+Up", MOVED, c - len(MARKER), a - len(MARKER)),
    ("Alt+Down", WRITTEN, c, a),
    ("Ctrl+Z", MOVED, c - len(MARKER), a - len(MARKER)),
    ("Ctrl+S", MOVED, c - len(MARKER), a - len(MARKER)),
]


def gesture(engine, chord, right=False, text=""):
    parts = chord.split("+")
    side = "R" if right else "L"
    keys = [side + k if k in {"Ctrl", "Alt", "Shift"} else k for k in parts]
    for key in keys:
        state = engine.call("lab-key", key=key, pressed=True, text=text if key == keys[-1] else "")
    for key in reversed(keys):
        state = engine.call("lab-key", key=key, pressed=False)
    return state


def editor(state):
    return next(w for w in state["scene"]["windows"] if w["active"] and w["app"] == "code")


class PythonEditing(unittest.TestCase):
    def check_route(self, platform):
        engine = Engine(verify=True)
        try:
            engine.call("configure", platform=platform, layout="EN")
            self.assertIn("code-python-edit", [m["id"] for m in engine.call("lab-catalog")["missions"]])
            options = dict(app="code", mode="sandbox", level=1, mission="code-python-edit", seconds=0,
                           actionLimit=0, guidance="guided", seed=7)
            state = engine.call("lab-start", **options)
            self.assertEqual(state["scenarioLength"], len(STEPS))
            engine.call("lab-resume")
            for index, (key, expected, caret, anchor) in enumerate(STEPS, 1):
                with self.subTest(key=key, step=index):
                    if key == "<write>":
                        self.assertEqual(state["task"]["text"], STATEMENT)
                        for char in STATEMENT:
                            binding = {" ": "Space", "=": "=", "+": "Shift+="}.get(char, char.upper())
                            state = gesture(engine, binding, right=platform == "windows", text=char)
                            self.assertEqual(state["solved"], index if char == STATEMENT[-1] and
                                             editor(state)["body"] == WRITTEN else index - 1)
                    else:
                        state = gesture(engine, key, right=platform == "windows")
                    self.assertEqual(state["solved"], index, state["status"])
                    view = editor(state)
                    self.assertEqual((view["body"], view["bodyCursor"], view["bodyAnchor"]), (expected, caret, anchor))
                    tab = view["tabs"][view["activeTab"]]
                    self.assertTrue(tab["url"].endswith("/task-list/tasks.py"))
                    expected_saved = MOVED if index == len(STEPS) else SOURCE
                    self.assertEqual(tab["savedBody"], expected_saved)
                    self.assertEqual({f["path"]: f["content"] for f in state["scene"]["terminal"]["files"]}[tab["url"]], expected_saved)
                    if index == 1:
                        # Real text input lands after the generated spaces,
                        # and undo removes the typed character without undoing
                        # the new line or falsely crediting the next goal.
                        written = gesture(engine, "Y", text="y")
                        self.assertEqual(written["solved"], index)
                        self.assertEqual(editor(written)["body"], expected[:caret] + "y" + expected[caret:])
                        self.assertEqual(editor(written)["bodyCursor"], caret + 1)
                        undone = gesture(engine, "Ctrl+Z")
                        self.assertEqual(undone["solved"], index)
                        self.assertEqual(editor(undone)["body"], expected)
                        self.assertEqual(editor(undone)["bodyCursor"], caret)
                    if key == "Ctrl+Shift+Left":
                        # A plain Backspace yields the same text, but cannot
                        # demonstrate the requested Ctrl+Backspace chord.
                        wrong = gesture(engine, "Backspace")
                        self.assertEqual(wrong["solved"], index)
                        self.assertEqual(editor(wrong)["body"], REMOVED)
                        undone = gesture(engine, "Ctrl+Z")
                        self.assertEqual(undone["solved"], index)
                        self.assertEqual(editor(undone)["body"], WRITTEN)
            self.assertTrue(state["finished"] and state["saved"])
            self.assertTrue(engine.call("lab-result")["completed"])
        finally:
            engine.close()

    def test_windows_and_linux_profiles(self):
        for platform in ("linux", "windows"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as directory,                     patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
                self.check_route(platform)


if __name__ == "__main__":
    unittest.main()
