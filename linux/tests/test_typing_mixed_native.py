"""Exercise mixed input through an actual GTK window on an isolated X11 display.

Uses production startup (no QA engine, plan injection, or GUI handlers called
directly). XTest sends key transitions and XKB changes the OS keyboard group.
The widget/state reads are observations, never a substitute for delivering keys.
Requires test_typing_cycles_native.py and x11-xkb-utils in the test environment.
"""
import ctypes as C
import json
import os
from pathlib import Path
import subprocess

from gi.repository import Gdk
from test_typing_cycles_native import TypingCycles, x, ptr, out

# XkbUseCoreKbd; a new Xvfb with US/RU and the standard Mod2 NumLock mapping.
DEV = 0x0100
x.XkbLockGroup.argtypes = [C.c_void_p, C.c_uint, C.c_uint]
x.XkbLockModifiers.argtypes = [C.c_void_p, C.c_uint, C.c_uint, C.c_uint]
x.XSync.argtypes = [C.c_void_p, C.c_int]


class NativeMixed(TypingCycles):
    def __init__(self):
        super().__init__()
        self.last_event = None
        self.evidence = []

    def key_pressed(self, controller, keyval, keycode, modifiers):
        self.last_event = dict(code=keycode, keysym=Gdk.keyval_name(keyval),
                               text=chr(u) if (u := Gdk.keyval_to_unicode(keyval)) else "",
                               group=controller.get_group())
        return super().key_pressed(controller, keyval, keycode, modifiers)

    def os_layout(self, layout):
        assert x.XkbLockGroup(ptr, DEV, 1 if layout == "RU" else 0)
        x.XSync(ptr, False)
        for _ in range(3):
            yield

    def choose(self, mode):
        if self.workout:
            yield from self.chord("Esc")
            assert self.result_kind == "typing"
            yield from self.chord("Alt+2")
        b = self.typing_rows[mode]
        # Find the visible card with the ordinary keyboard focus chain, activate
        # with Space; no selection by protocol or programmatic button click.
        for _ in range(65):
            if self.window.get_focus() is b:
                break
            yield from self.chord("Tab")
        assert self.window.get_focus() is b, (mode, self.window.get_focus())
        yield from self.chord("Space")
        for _ in range(30):
            if self.workout == "typing":
                break
            yield
        assert self.workout == "typing" and self.state["mode"] == mode, (mode, self.state)
        assert self.state["progressDone"] == 0 and self.typing_progress.get_mapped()

    def type_unit(self):
        s = self.state
        value = s["expected"]
        if s["expectedKind"] == "command":
            assert self.typing_text.command == s["expectedDisplay"] == "⟦" + value + "⟧"
            label = value
        else:
            # Use exactly the physical position(s) shown. Let XKB produce text.
            label = "+".join(s["expectedKeys"])
        yield from self.chord(label)
        assert self.state["progressDone"] == s["progressDone"] + 1, (value, label, self.last_event, self.state)

    def exercise(self):
        self.focus_window()
        yield from self.os_layout("EN")
        assert self.mode == "home"
        yield from self.chord("Alt+2")
        assert self.mode == "typing"
        required = ("Digits", "TopRow", "Hotkeys", "WordsDigits", "WordsDigitsHotkeys")
        assert all(key in self.typing_rows and self.typing_rows[key].get_visible() for key in required)
        yield from self.landmark("09-compact-mode-list")

        # Start on EN, switch the actual OS as required for composing words; the
        # exercise chooses its cap legends independently. Cross both text/chord boundaries.
        yield from self.choose("WordsDigitsHotkeys")
        witnessed = set()
        for i in range(60):
            s = self.state
            target = (s["expectedKind"], s["wantedLayout"])
            yield from self.os_layout(target[1])
            if target not in witnessed:
                witnessed.add(target)
                if target[0] == "command":
                    yield from self.landmark("10-mixed-chord-" + target[1])
                elif target[1] == "RU":
                    yield from self.landmark("11-mixed-cyrillic")
            yield from self.type_unit()
            if i > 12 and witnessed == {("text", "RU"), ("text", "EN"), ("command", "RU"), ("command", "EN")}:
                break
        assert len(witnessed) == 4
        self.evidence.append(dict(case="mixed-action-alternation", actions=self.state["progressDone"],
                                  targets=sorted(witnessed), errors=self.state["errors"]))

        # The seven beginner chords run in both layouts. Target the actual RU
        # V position and distinguish it from B even with the same right modifier.
        yield from self.choose("Hotkeys")
        for _ in range(14):
            s = self.state
            if s["expected"] == "Ctrl+V" and s["wantedLayout"] == "RU":
                break
            yield from self.type_unit()
        s = self.state
        assert s["expected"] == "Ctrl+V" and s["wantedLayout"] == "RU"
        yield from self.os_layout("RU")
        cells = self.guide.board.cells
        assert "м" in cells["V"][0].get_label().lower() and "и" in cells["B"][0].get_label().lower()
        assert cells["V"][0].has_css_class("target") and cells["LCtrl"][0].has_css_class("target")
        assert not cells["B"][0].has_css_class("target")
        assert self.typing_text.command == "⟦Ctrl+V⟧"
        before = s["progressDone"]
        yield from self.chord("RCtrl+B")
        assert self.state["progressDone"] == before and self.state["errors"] == s["errors"]+1
        # As on the physical ANSI board, XKB labels V with Cyrillic М and B with И.
        yield from self.landmark("12-ctrl-v-not-b-russian")
        yield from self.chord("RCtrl+V")
        assert self.state["progressDone"] == before + 1 and self.last_event["code"] == 55
        self.evidence.append(dict(case="right-ctrl-v-russian", physical=self.last_event.copy(),
                                  before=before, after=self.state["progressDone"]))

        # NumLock off: keypad navigation is not a typed digit. Turn it on and
        # use the same key; report the GDK key symbols that were actually delivered.
        yield from self.choose("Digits")
        assert x.XkbLockModifiers(ptr, DEV, 1 << 4, 0)
        x.XSync(ptr, False)
        digit = self.state["expected"]
        yield from self.chord("NumPad" + digit)
        event_off = self.last_event.copy()
        assert self.state["progressDone"] == 0 and event_off["text"] != digit, event_off
        yield from self.chord("NumLock")
        yield from self.chord("NumPad" + digit)
        event_on = self.last_event.copy()
        assert self.state["progressDone"] == 1 and event_on["text"] == digit, event_on
        self.evidence.append(dict(case="numpad-numlock", expected=digit, off=event_off, on=event_on))

        for layout, sign, wrong in (("RU","№","#"),("EN","#","№")):
            yield from self.os_layout(layout)
            yield from self.choose("TopRow")
            assert self.state["wantedLayout"] == layout
            for _ in range(20):
                if self.state["expected"] == sign:
                    break
                yield from self.type_unit()
            s = self.state
            assert s["expected"] == sign and s["expectedKeys"][-1] == "3"
            # Same hardware position, opposite OS group produces actual wrong text.
            yield from self.os_layout("EN" if layout == "RU" else "RU")
            yield from self.chord("Shift+3")
            event_wrong = self.last_event.copy()
            assert self.state["progressDone"] == s["progressDone"] and event_wrong["text"] == wrong, (s, event_wrong)
            yield from self.os_layout(layout)
            yield from self.type_unit()
            assert self.last_event["text"] == sign
            self.evidence.append(dict(case="shift-3", layout=layout, wrong=event_wrong, right=self.last_event.copy()))
        (out / "mixed-native-results.json").write_text(json.dumps(self.evidence, ensure_ascii=False, indent=2))
        print("PASS GTK/XTest: native mixed RU/EN, physical Ctrl+V, NumLock off/on, RU № and EN #.", flush=True)


if __name__ == "__main__":
    root = Path(os.environ["TOUCH_TYPING_QA_DATA_ROOT"]) / "mixed-ordinary-profile"
    root.mkdir(parents=True, exist_ok=False)
    os.environ["XDG_DATA_HOME"] = str(root)
    subprocess.run(["setxkbmap", "-layout", "us,ru", "-option", ""], check=True)
    app = NativeMixed()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
