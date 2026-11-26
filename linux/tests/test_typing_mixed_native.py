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

import gi
gi.require_version("Gdk", "4.0")
gi.require_version("GdkX11", "4.0")
gi.require_version("Graphene", "1.0")
gi.require_version("Gtk", "4.0")
from gi.repository import Gdk, GdkX11, Graphene, Gtk
from test_typing_cycles_native import TypingCycles, x, xt, ptr, out
from test_preview2_native import alias, codes

# XkbUseCoreKbd; a new Xvfb with US/RU and the standard Mod2 NumLock mapping.
DEV = 0x0100
x.XkbLockGroup.argtypes = [C.c_void_p, C.c_uint, C.c_uint]
x.XkbLockModifiers.argtypes = [C.c_void_p, C.c_uint, C.c_uint, C.c_uint]
x.XSync.argtypes = [C.c_void_p, C.c_int]
x.XkbGetState.argtypes = [C.c_void_p, C.c_uint, C.c_void_p]
x.XDefaultRootWindow.argtypes = [C.c_void_p]
x.XDefaultRootWindow.restype = C.c_ulong
x.XTranslateCoordinates.argtypes = [C.c_void_p, C.c_ulong, C.c_ulong, C.c_int, C.c_int,
                                   C.POINTER(C.c_int), C.POINTER(C.c_int), C.POINTER(C.c_ulong)]
xt.XTestFakeMotionEvent.argtypes = [C.c_void_p, C.c_int, C.c_int, C.c_int, C.c_ulong]
xt.XTestFakeButtonEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]


def xkb_group():
    # Witness the server state independently of the GDK effective event group
    # and the production layout helper. The first byte in XkbStateRec is group.
    snapshot = (C.c_ubyte * 32)()
    assert x.XkbGetState(ptr, DEV, C.byref(snapshot)) == 0
    return snapshot[0]


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

    def chord(self, label):
        # GTK can defer queued XTest events while redrawing the keyboard.
        # Wait for actual press/release delivery before judging a native chord.
        keys = [codes[alias.get(key, key)] for key in label.split("+")]
        receipts = {(key, down): self.received.get((key, down), 0)
                    for key in keys for down in (True, False)}
        yield from super().chord(label)
        for _ in range(100):
            if all(self.received.get(event, 0) > prior for event, prior in receipts.items()):
                return
            yield
        raise AssertionError(("undelivered keyboard input", label, self.last_event))

    def os_layout(self, layout):
        group = 1 if layout == "RU" else 0
        assert x.XkbLockGroup(ptr, DEV, group)
        x.XSync(ptr, False)
        for _ in range(3):
            yield
        assert xkb_group() == group, ("XKB group did not switch", layout, group, xkb_group())

    def click_card(self, card):
        assert card.get_mapped()
        center = Graphene.Point().init(card.get_width()/2, card.get_height()/2)
        ok, point = card.compute_point(self.window, center)
        assert ok, "card has no position in the application window"
        xid = GdkX11.X11Surface.get_xid(self.window.get_surface())
        xx, yy, child = C.c_int(), C.c_int(), C.c_ulong()
        assert x.XTranslateCoordinates(ptr, xid, x.XDefaultRootWindow(ptr), int(point.x), int(point.y),
                                       C.byref(xx), C.byref(yy), C.byref(child))
        assert xt.XTestFakeMotionEvent(ptr, -1, xx.value, yy.value, 0)
        x.XSync(ptr, False)
        for _ in range(3):
            yield
        assert xt.XTestFakeButtonEvent(ptr, 1, 1, 0)
        assert xt.XTestFakeButtonEvent(ptr, 1, 0, 0)
        x.XSync(ptr, False)
        for _ in range(4):
            yield

    def choose(self, mode, layout=None, pointer=False):
        if self.workout:
            yield from self.chord("Esc")
            assert self.result_kind == "typing"
            yield from self.chord("Alt+2")
        b = self.typing_rows[mode]
        if layout:
            # Move focus with the *other* layout. Then switch OS group right
            # before activation, with no letter event to refresh the app's cache.
            yield from self.os_layout("RU" if layout == "EN" else "EN")
        # Find the real card via the keyboard chain, then use an actual activation
        # (Space or native mouse) rather than invoking its application handler.
        for _ in range(65):
            if self.window.get_focus() is b:
                break
            yield from self.chord("Tab")
        assert self.window.get_focus() is b, (mode, self.window.get_focus())
        old_layout = self.layout
        if layout:
            yield from self.os_layout(layout)
        self.last_event = None
        if pointer:
            yield from self.click_card(b)
        else:
            yield from self.chord("Space")
        for _ in range(30):
            if self.workout == "typing":
                break
            yield
        assert self.workout == "typing" and self.state["mode"] == mode, (mode, self.state)
        assert self.state["progressDone"] == 0 and self.typing_progress.get_mapped()
        if layout:
            keyboard = self.window.get_display().get_default_seat().get_keyboard()
            get_index = getattr(keyboard, "get_active_layout_index", None)
            row = dict(case="start-layout", mode=mode, activation="pointer" if pointer else "Space",
                       wanted=layout, cached_before_switch=old_layout, xkb_group=xkb_group(),
                       gtk=".".join(str(v()) for v in (Gtk.get_major_version, Gtk.get_minor_version, Gtk.get_micro_version)),
                       gdk_active=get_index() if get_index else None, activation_key=self.last_event,
                       app_layout=self.layout, exercise_layout=self.state["wantedLayout"])
            print("GTK start layout: " + json.dumps(row, ensure_ascii=False), flush=True)
            assert self.last_event is None if pointer else self.last_event["keysym"] == "space", row
            assert row["xkb_group"] == (layout == "RU"), row
            assert row["app_layout"] == row["exercise_layout"] == layout, row
            self.evidence.append(row)

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

    def russian_shared_keys(self):
        yield from self.choose("WordsDigitsHotkeys", layout="RU")
        # Follow one real on-screen Russian word. Do not touch XKB again
        # after activation; the user has not changed layout between letters,
        # the space and the following number.
        letters = ""
        for _ in range(15):
            unit = self.state["expected"]
            if self.state["expectedKeys"] == ["Space"]:
                break
            assert self.state["expectedKind"] == "text" and self.state["wantedLayout"] == "RU"
            assert len(unit) == 1 and ("а" <= unit.lower() <= "я" or unit.lower() == "ё"), unit
            yield from self.type_unit()
            letters += unit
        assert letters and self.state["expectedKeys"] == ["Space"] and xkb_group() == 1
        assert self.layout == "RU" and self.layout_chips["RU"].has_css_class("active")
        yield from self.landmark("09a-ru-before-space")
        before = self.state["progressDone"]
        yield from self.chord("Space")
        assert self.last_event["text"] == " ", self.last_event
        event = self.last_event.copy()
        assert self.state["progressDone"] == before + 1 and xkb_group() == 1, (event, self.state)
        assert self.layout == "RU" and self.layout_chips["RU"].has_css_class("active"), (letters, event, self.layout)
        assert not self.layout_chips["EN"].has_css_class("active")
        assert self.state["wantedLayout"] == "RU" and self.state["expected"].isdecimal()
        assert "Смените раскладку" not in self.typing_detail.get_label(), self.typing_detail.get_label()
        yield from self.landmark("09b-ru-after-space-no-layout-change")
        yield from self.type_unit()
        assert self.layout == "RU" and xkb_group() == 1
        row = dict(case="ru-word-space-digit", word=letters, before=before, after=before+1,
                   server_group=xkb_group(), activation_key=event, app_layout=self.layout)
        print("GTK shared key: " + json.dumps(row, ensure_ascii=False), flush=True)
        self.evidence.append(row)

    def exercise(self):
        self.focus_window()
        yield from self.os_layout("EN")
        assert self.mode == "home"
        yield from self.chord("Alt+2")
        assert self.mode == "typing"
        required = ("Digits", "TopRow", "Hotkeys", "WordsDigits", "WordsDigitsHotkeys")
        assert all(key in self.typing_rows and self.typing_rows[key].get_visible() for key in required)
        yield from self.landmark("09-compact-mode-list")
        yield from self.russian_shared_keys()
        if os.environ.get("TOUCH_TYPING_QA_MIXED_BASE") == "1":
            (out / "mixed-native-base-results.json").write_text(json.dumps(self.evidence, ensure_ascii=False, indent=2))
            return

        # Start on EN, switch the actual OS as required for composing words; the
        # exercise chooses its cap legends independently. Cross both text/chord boundaries.
        yield from self.choose("WordsDigitsHotkeys", layout="EN")
        assert not self.guide.board.get_mapped()
        yield from self.chord("F4")  # explicit opt-in, not the clean-profile default
        assert self.guide.board.get_mapped()
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

        # The ordinary card teaches both physical Alt positions without
        # foreground capture. Keep native US in this part to exercise Alt_R.
        yield from self.os_layout("EN")
        assert self.physical_keyboard.right_alt_mode() == "alt"
        while self.state["progressDone"] < 14:
            yield from self.type_unit()
        for label, wrong, finger in (("LAlt+Right", "RAlt+Right", 8), ("RAlt+D", "LAlt+D", 9)):
            s = self.state
            assert s["expected"] == label and s["expectedTargets"][0] == {
                "key": label.split("+")[0], "finger": finger}
            assert len({k["finger"] for k in s["expectedTargets"]}) == len(s["expectedTargets"])
            assert "большим пальцем" in self.typing_detail.get_label()
            yield from self.landmark("12-alt-" + label.split("+")[0])
            yield from self.chord(wrong)
            assert self.state["progressDone"] == s["progressDone"], (label, wrong)
            yield from self.type_unit()
        extra = set()
        for _ in range(11):
            extra.add(self.state["expected"])
            yield from self.type_unit()
        assert extra == {"Ctrl+F", "Ctrl+Y", "Ctrl+T", "Ctrl+W", "Ctrl+Shift+T",
                         "Ctrl+Left", "Ctrl+Right", "Ctrl+Backspace", "Ctrl+Delete", "Shift+Home", "Shift+End"}
        self.evidence.append(dict(case="physical-left-right-alt-and-everyday-chords", shortcuts=sorted(extra)))

        added = []
        for _ in range(8):
            added.append(self.state["expected"])
            yield from self.type_unit()
        assert added == ["Alt+Home", "Alt+Right", "Shift+Left", "Shift+Right", "Shift+Tab", "Alt+Down", "F3", "Shift+F3"]
        self.evidence.append(dict(case="foreground-selection-fields-and-find", shortcuts=added))

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
            yield from self.choose("TopRow", layout=layout, pointer=layout == "RU")
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
        # Cover switching the other way immediately before Space on older GDK:
        # the Space event itself may still report a single effective group 0.
        yield from self.choose("TopRow", layout="RU")

        # AltGr is now actually mapped on the disposable X server. It must
        # reach the physical position lesson, but never drive an Alt command.
        subprocess.run(["setxkbmap", "-layout", "us,ru", "-option", "",
                        "-option", "lv3:ralt_switch"], check=True)
        yield from self.os_layout("EN")
        assert self.physical_keyboard.right_alt_mode() == "altgr"
        yield from self.choose("Hotkeys", layout="EN")
        for _ in range(14):
            yield from self.type_unit()
        s = self.state
        assert s["expected"] == "LAlt+Right"
        yield from self.chord("RAlt+Right")
        assert (self.state["progressDone"], self.state["errors"]) == (14, s["errors"])
        yield from self.type_unit()
        assert self.state["expected"] == "RAlt" and self.state["expectedTargets"] == [{"key":"RAlt","finger":9}]
        assert "только положение" in self.typing_detail.get_label()
        yield from self.landmark("14-altgr-position")
        yield from self.chord("LAlt")
        assert self.state["progressDone"] == 15
        yield from self.type_unit()
        assert self.state["progressDone"] == 16 and self.last_event["keysym"] == "ISO_Level3_Shift"
        self.evidence.append(dict(case="altgr-is-position-only", physical=self.last_event,
                                  after=self.state["progressDone"]))
        (out / "mixed-native-results.json").write_text(json.dumps(self.evidence, ensure_ascii=False, indent=2))
        print("PASS GTK/XTest: native mixed RU/EN, physical Ctrl+V, NumLock off/on, RU № and EN #.", flush=True)


if __name__ == "__main__":
    root = Path(os.environ["TOUCH_TYPING_QA_DATA_ROOT"]) / "mixed-ordinary-profile"
    root.mkdir(parents=True, exist_ok=False)
    os.environ["XDG_DATA_HOME"] = str(root)
    config = ["-rules", "base", "-model", "pc105"] if os.environ.get("TOUCH_TYPING_QA_MIXED_BASE") == "1" else []
    subprocess.run(["setxkbmap", *config, "-layout", "us,ru", "-option", ""], check=True)
    app = NativeMixed()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
