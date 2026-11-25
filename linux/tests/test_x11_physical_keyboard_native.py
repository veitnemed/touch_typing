"""Check normal Live entry and edits in a real GTK X11 window on each X.Org map.

Run in a disposable Xvfb (not a user's desktop), before starting GTK:
  setxkbmap -rules "$TOUCH_TYPING_QA_XKB_RULES" -model pc105 -layout us,ru
Set rules=base or evdev and supply fresh XDG_DATA_HOME, TOUCH_TYPING_QA_DATA_ROOT
and TOUCH_TYPING_QA_OUTPUT. Use PYTHONPATH pointing at the unpacked candidate.
XTest sends independent recorded raw XKB positions; no acquisition or input mocks.
"""
import ctypes as C
import json
import os

from test_preview2_native import QA, Gdk, GLib, alias, codes, out, ptr, x, xt


RULES = os.environ["TOUCH_TYPING_QA_XKB_RULES"]
# Raw XFree86 PC positions, not derived from the production resolver.
BASE = {"Home": 97, "Up": 98, "PageUp": 99, "Left": 100, "Right": 102, "End": 103,
        "Down": 104, "PageDown": 105, "Insert": 106, "Delete": 107, "NumEnter": 108,
        "RCtrl": 109, "Pause": 110, "Print": 111, "NumDivide": 112, "RAlt": 113,
        "LSuper": 115, "RSuper": 116, "Menu": 117}
assert RULES in ("base", "evdev")
RAW = {**codes, **(BASE if RULES == "base" else {})}
x.XkbLockGroup.argtypes = [C.c_void_p, C.c_uint, C.c_uint]
x.XSync.argtypes = [C.c_void_p, C.c_int]


class PhysicalMapWindow(QA):
    def __init__(self):
        super().__init__()
        self.events = []

    def do_activate(self):
        if self.window:
            self.window.present()
            return
        self.smoke = None  # The ordinary startup uses the real installed application and clean profile.
        super().do_activate()
        GLib.timeout_add(500, self.smoke_test)

    def key_pressed(self, controller, keyval, keycode, modifiers):
        handled = super().key_pressed(controller, keyval, keycode, modifiers)
        key = self.physical_keyboard.down.get(keycode)
        self.events.append([keycode, Gdk.keyval_name(keyval), key])
        return handled

    def chord(self, label):
        sequence = [RAW[alias.get(key, key)] for key in label.split("+")]
        for key in sequence:
            assert xt.XTestFakeKeyEvent(ptr, key, 1, 0)
        for key in reversed(sequence):
            assert xt.XTestFakeKeyEvent(ptr, key, 0, 0)
        x.XSync(ptr, False)
        for _ in range(7):
            yield

    def exercise(self):
        self.focus_window()
        self.home_cards["terminal"].grab_focus()
        yield from self.chord("Enter")
        lab = self.lab
        assert lab and lab.active and lab.window_only_keys and not lab.state["paused"]
        assert self.window.get_focus() is lab.desktop and not lab.prep.get_visible()
        assert self.physical_keyboard.codes.get(104) == ("Down" if RULES == "base" else "NumEnter")

        def field():
            scene = lab.state["scene"]
            terminal = next(w for w in scene["windows"] if w["app"] == "terminal" and w["active"])
            return scene["terminalInput"], terminal["cursor"]

        def edit(expected, *steps):
            for key in steps:
                yield from self.chord(key)
            assert field() == expected, (RULES, steps, field(), expected)

        for key in "ABCD":
            yield from self.chord(key)
        yield from edit(("abcd", 0), "Home")
        yield from edit(("abd", 2), "Right", "Right", "Delete")
        self.capture_widget_png(out / f"{RULES}-caret-delete.png", lab.desktop)
        yield from edit(("ad", 1), "Backspace")
        yield from edit(("", 0), "RCtrl+C")
        yield from edit(("", 0), "P", "W", "D", "Enter")
        yield from edit(("pwd", 3), "A", "B", "Up")
        yield from edit(("ab", 2), "Down")
        assert self.events[-1] == [RAW["Down"], "Down", "Down"], self.events[-1]
        self.capture_widget_png(out / f"{RULES}-history-down.png", lab.desktop)

        # Raw group change proves the same key is V with or without Cyrillic М.
        assert x.XkbLockGroup(ptr, 0x0100, 1)
        x.XSync(ptr, False)
        yield from self.chord("V")
        assert self.events[-1] == [55, "Cyrillic_em", "V"], self.events[-1]
        yield from self.chord("B")
        assert self.events[-1] == [56, "Cyrillic_i", "B"], self.events[-1]
        assert x.XkbLockGroup(ptr, 0x0100, 0)
        x.XSync(ptr, False)
        yield from self.chord("V")
        assert self.events[-1] == [55, "v", "V"], self.events[-1]

        # NumLock must change the glyph, not the keypad's identity. Dedicated
        # Home and Enter are different even on XFree86's overlapping raw codes.
        keyboard = self.window.get_display().get_default_seat().get_keyboard()
        if keyboard.get_num_lock_state():
            yield from self.chord("NumLock")
        yield from self.chord("NumPad7")
        assert self.events[-1] == [79, "KP_Home", "NumPad7"], self.events[-1]
        yield from self.chord("NumLock")
        yield from self.chord("NumPad7")
        assert self.events[-1] == [79, "KP_7", "NumPad7"], self.events[-1]
        yield from self.chord("NumEnter")
        assert self.events[-1] == [RAW["NumEnter"], "KP_Enter", "NumEnter"], self.events[-1]
        assert not self.physical_keyboard.down and not lab.physical_down, "unbalanced physical key releases"
        (out / f"{RULES}-physical-codes.json").write_text(json.dumps(self.events, indent=2) + "\n", encoding="utf-8")
        print(f"PASS native GTK {RULES}: dedicated Home, arrows, Delete and right Ctrl; "
              "Up/Down restore history draft; RU/EN V/B, keypad with and without NumLock", flush=True)


if __name__ == "__main__":
    app = PhysicalMapWindow()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
