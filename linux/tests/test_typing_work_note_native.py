"""Ordinary GTK typing entry, one complete bilingual note with native XKB input.

Use an isolated US/RU X11 display and a disposable app profile. Read visible
lesson state for the current note; translate the characters using the X server
map, independently of the trainer's suggested physical positions.
"""
import ctypes as C
import os
import subprocess

from test_preview2_native import Gdk, codes, out, ptr, x, xt
from test_typing_mixed_native import NativeMixed, xkb_group


MODE = "lesson:work-note-bilingual"
x.XkbKeycodeToKeysym.argtypes = [C.c_void_p, C.c_ubyte, C.c_int, C.c_int]
x.XkbKeycodeToKeysym.restype = C.c_ulong


def type_map(group):
    allowed = "QWERTYUIOPASDFGHJKLZXCVBNM1234567890`-=[]\\;',./"
    result = {" ": (codes["Space"], False), "\n": (codes["Enter"], False)}
    for token in allowed:
        code = codes[token]
        for shift in (False, True):
            keyval = x.XkbKeycodeToKeysym(ptr, code, group, int(shift))
            glyph = Gdk.keyval_to_unicode(keyval)
            if glyph and glyph > 31:
                result[chr(glyph)] = (code, shift)
    return result


class Notes(NativeMixed):
    def select_note(self):
        yield from self.chord("Alt+2")
        assert self.mode == "typing"
        card = self.typing_rows[MODE]
        node = self.typing_prep.get_first_child()
        while node and (not hasattr(node, "get_expanded") or not node.get_label().startswith("Дополнительные")):
            node = node.get_next_sibling()
        assert node and not node.get_expanded() and not card.get_mapped()
        for _ in range(100):
            focus = self.window.get_focus()
            if focus and (focus == node or focus.is_ancestor(node)):
                break
            yield from self.chord("Tab")
        assert focus and (focus == node or focus.is_ancestor(node)), focus
        yield from self.chord("Space")
        assert node.get_expanded() and card.get_mapped()
        for _ in range(100):
            if self.window.get_focus() is card:
                break
            yield from self.chord("Tab")
        assert self.window.get_focus() is card
        yield from self.landmark("01-work-note-in-catalog")
        yield from self.chord("Space")
        assert self.workout == "typing" and self.state["mode"] == MODE

    def wait_progress(self, want):
        for _ in range(180):
            if self.state["progressDone"] >= want:
                break
            yield
        assert self.state["progressDone"] == want, (self.state["progressDone"], want, self.last_event)

    def exercise(self):
        self.focus_window()
        assert self.mode == "home"
        yield from self.select_note()
        assert self.state["wantedLayout"] == "RU"
        maps = [type_map(0), type_map(1)]
        note = self.state["after"]
        assert 350 <= len(note) <= 550 and "reports/weekly.csv" in note
        assert self.state["progressTotal"] == len(note)
        yield from self.chord("T")
        assert (self.state["progressDone"], self.state["errors"], self.last_event["text"]) == (0, 1, "t")
        pos, group, witnessed = 0, xkb_group(), set()
        markers = {note.index("reports"): "02-file-name-EN", note.index("отчёта"): "03-cyrillic-RU",
                   note.index("@example"): "04-email-symbol-EN"}
        while pos < len(note):
            if note[pos] not in maps[group]:
                group ^= 1
                yield from self.os_layout("RU" if group else "EN")
            assert xkb_group() == group and note[pos] in maps[group], (pos, note[pos], group)
            if pos in markers:
                yield from self.landmark(markers[pos])
                witnessed.add(markers[pos])
            start = pos
            while pos < len(note) and pos - start < 12 and note[pos] in maps[group]:
                if pos > start and pos in markers:
                    break
                code, shift = maps[group][note[pos]]
                if shift:
                    assert xt.XTestFakeKeyEvent(ptr, codes["LShift"], 1, 0)
                assert xt.XTestFakeKeyEvent(ptr, code, 1, 0)
                assert xt.XTestFakeKeyEvent(ptr, code, 0, 0)
                if shift:
                    assert xt.XTestFakeKeyEvent(ptr, codes["LShift"], 0, 0)
                pos += 1
            assert pos > start
            x.XFlush(ptr)
            yield from self.wait_progress(pos)
            assert self.state["errors"] == 1 and self.state["after"] == note[pos:]
        assert witnessed == set(markers.values())
        for _ in range(20):
            if self.result_kind == "typing":
                break
            yield
        assert self.result_kind == "typing" and not self.workout
        yield from self.landmark("05-completed-note")
        print(f"PASS GTK ordinary card: whole note {len(note)} chars; one wrong glyph refused; "
              "RU/EN, filename, email, punctuation and terminal full stop accepted.", flush=True)


if __name__ == "__main__":
    assert os.environ.get("TOUCH_TYPING_QA_DATA_ROOT")
    subprocess.run(["setxkbmap", "-rules", "evdev", "-model", "pc105", "-layout", "us,ru"], check=True)
    app = Notes()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
