"""Finite/infinite proof in the production GTK window and engine (isolated user profile).

Run inside Xvfb with PYTHONPATH pointing at the extracted Linux application and
TOUCH_TYPING_QA_OUTPUT / TOUCH_TYPING_QA_DATA_ROOT set to disposable directories.
The launcher starts and closes the application twice. XTest sends the actual
US-layout key events (including the long first cycle); it does not paste lesson
text or inject engine state. Optional TOUCH_TYPING_QA_VIDEO=1 dwells at landmarks.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from gi.repository import GLib, Gtk
from test_preview2_native import QA, codes, out, ptr, x, xt


class TypingCycles(QA):
    def do_activate(self):
        if self.window:
            self.window.present()
            return
        # Do not open the special smoke startup page or verification engine.
        self.smoke = None
        super().do_activate()
        GLib.timeout_add(500, self.smoke_test)

    def landmark(self, name):
        for _ in range(7):
            yield
        self.capture_widget_png(out / (name + ".png"))
        yield from self.video_hold(name)

    def type_words(self, count):
        """Unmodified Latin/space events; read only what the application shows."""
        left = count
        while left:
            upcoming = self.state["after"][:min(16, left)]
            assert upcoming and all(c == " " or "a" <= c <= "z" for c in upcoming), repr(upcoming)
            wanted = self.state["progressDone"] + len(upcoming)
            for char in upcoming:
                code = codes["Space" if char == " " else char.upper()]
                xt.XTestFakeKeyEvent(ptr, code, 1, 0)
                xt.XTestFakeKeyEvent(ptr, code, 0, 0)
            x.XFlush(ptr)
            for _ in range(160):
                if self.state["progressDone"] == wanted:
                    break
                yield
            assert self.state["progressDone"] == wanted, (self.state["progressDone"], wanted, upcoming)
            left -= len(upcoming)

    def exercise(self):
        self.focus_window()
        assert self.mode == "home"
        yield from self.chord("Alt+2")
        assert self.mode == "typing"
        checkpoint = out / "typing-native-checkpoint.json"
        if os.environ["TT_CYCLES_PHASE"] == "reopen":
            prior = json.loads(checkpoint.read_text())
            assert self.typing_continue.get_mapped()
            self.typing_continue.grab_focus()
            yield from self.chord("Space")
            assert self.workout == "typing" and self.state["progressDone"] == prior["done"]
            assert self.state["after"] == prior["after"] and self.state["progressTotal"] == prior["total"]
            yield from self.type_words(1)
            yield from self.landmark("07-restarted-one-more")
            yield from self.chord("Esc")
            assert self.result_kind == "typing"
            yield from self.chord("Alt+2")
            self.typing_pick.set_selected([item[0] for item in self.typing_options].index("WordsEN"))
            self.typing_settings.set_expanded(True)
            self.typing_cycles.set_selected(5)
            yield from self.chord("F5")
            yield from self.type_words(3)
            assert not self.typing_progress.get_visible() and not self.typing_progress.get_mapped()
            assert self.typing_counter.get_label() == "Набрано: 3"
            assert self.state["progressTotal"] is None
            yield from self.landmark("08-infinite-no-bar")
            yield from self.chord("Esc")
            assert self.result_kind == "typing"
            print("PASS GTK: real restart preserves exact position/plan; infinite removes progress widget.", flush=True)
            return
        assert self.typing_choice == "Russian" and self.typing_cycles.get_selected() == 0
        assert self.typing_time.get_selected() == 0
        yield from self.chord("F5")
        assert self.workout == "typing" and self.typing_progress.get_mapped()
        assert not self.guide.board.get_mapped() and not any(h.get_mapped() for h in self.guide.hands.values())
        assert self.state["cycles"] == 1 and 350 <= self.state["progressTotal"] <= 550
        assert self.typing_counter.get_label() == f'0/{self.state["progressTotal"]}'
        yield from self.landmark("01-classic-one-cycle")
        yield from self.chord("Esc")
        yield from self.chord("Alt+2")
        self.typing_pick.set_selected([item[0] for item in self.typing_options].index("WordsEN"))
        self.typing_settings.set_expanded(True)
        self.typing_cycles.set_selected(1)
        yield from self.landmark("02-two-cycles-setup")
        yield from self.chord("F5")
        first, second = self.state["cycleLengths"]
        total = first + second
        assert 350 <= first <= 550 and 350 <= second <= 550
        yield from self.chord("1")
        assert self.state["progressDone"] == 0 and self.typing_counter.get_label() == f"0/{total}"
        yield from self.type_words(first - 1)
        yield from self.landmark("03-before-boundary")
        yield from self.type_words(1)
        assert self.typing_counter.get_label() == f"{first}/{total}"
        yield from self.landmark("04-cycle-boundary")
        yield from self.type_words(1)
        assert self.typing_counter.get_label() == f"{first+1}/{total}"
        yield from self.landmark("05-next-character")
        yield from self.chord("F8")
        assert self.paused and self.state["progressDone"] == first + 1
        yield from self.chord("Q")
        assert self.state["progressDone"] == first + 1
        yield from self.chord("Space")
        assert not self.paused and self.state["progressDone"] == first + 1
        checkpoint.write_text(json.dumps(dict(done=first+1, total=total, after=self.state["after"])))
        yield from self.landmark("06-before-closing")
        self.window.close()
        assert self.close_dialog is not None
        self.close_dialog.response(Gtk.ResponseType.ACCEPT)
        assert self.allow_close and not self.workout
        print(f"PASS GTK: accepted {first}/{total} → {first+1}/{total}; error and pause do not move cursor.", flush=True)


if __name__ == "__main__":
    if not os.environ.get("TT_CYCLES_PHASE"):
        root = Path(os.environ["TOUCH_TYPING_QA_DATA_ROOT"]) / "gtk-classic-native"
        root.mkdir(parents=True, exist_ok=False)
        for phase in ("start", "reopen"):
            env = dict(os.environ, TT_CYCLES_PHASE=phase, XDG_DATA_HOME=str(root))
            subprocess.run([sys.executable, __file__], env=env, check=True)
    else:
        app = TypingCycles()
        code = app.run([])
        raise SystemExit(1 if app.smoke_failure else code)
