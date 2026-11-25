"""Check the real GTK course entry and record badges on a disposable X11 display.

Run with PYTHONPATH=<extracted app>:<repo>/linux/tests, TOUCH_TYPING_QA_OUTPUT
and TOUCH_TYPING_QA_DATA_ROOT set to temporary directories. The engine runs
in ordinary user mode, not verification mode. Seed only the temporary profile;
requests, navigation, record selection and widget rendering are production code.
"""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import shutil


def write_fixture(directory):
    # Deliberate synthetic observations, not results from real learners.
    fixture = Path(__file__).resolve().parents[2] / "qa/fixtures/typing-card-boundaries.xml"
    target = Path(directory) / "TouchTypingStudio"
    target.mkdir(parents=True)
    shutil.copyfile(fixture, target / "progress.xml")


def run_window():
    from gi.repository import GLib
    from test_preview2_native import QA, out

    class RecordWindow(QA):
        def do_activate(self):
            if self.window:
                self.window.present()
                return
            self.smoke = None
            super().do_activate()
            GLib.timeout_add(500, self.smoke_test)

        def exercise(self):
            self.focus_window()
            assert self.mode == "home"
            yield from self.chord("Alt+2")
            assert self.mode == "typing"
            for _ in range(10): yield
            expected = {
                "Russian": ("red", "89,99%", "01:11"),
                "English": ("green", "98%", "00:25"),
                "WordsRU": ("green", "98%", "—"),
                "WordsEN": ("orange", "97,49%", "00:32"),
                "Digits": ("green", "97,5%", "00:20"),
                "TopRow": ("orange", "90%", "00:40"),
                "Hotkeys": ("gray", "—", "—"),
            }
            for mode, (band, accuracy, duration) in expected.items():
                control = self.typing_rows[mode]
                labels = tuple(w.get_label() for w in self.typing_record_labels[mode])
                assert labels == (accuracy, duration), (mode, labels)
                assert control.get_mapped() and control.has_css_class("typing-record-" + band), mode
            assert "прежней версии" in self.typing_rows["WordsRU"].get_tooltip_text()
            assert "прежней версии" not in self.typing_rows["Digits"].get_tooltip_text()
            assert "Нет завершённых" in self.typing_rows["Hotkeys"].get_tooltip_text()
            self.capture_widget_png(out / "typing-course-records.png")
            yield from self.video_hold("typing-menu-accuracy-and-time")
            self.typing_settings.set_expanded(True)
            self.typing_cycles.set_selected(1)
            for _ in range(8): yield
            assert all(self.typing_rows[mode].has_css_class("typing-record-gray") for mode in expected)
            self.typing_cycles.set_selected(0)
            self.typing_settings.set_expanded(False)
            self.typing_rows["English"].grab_focus()
            yield from self.chord("Enter")
            assert self.workout == "typing" and self.state["mode"] == "English", self.state
            yield from self.chord("Esc")
            yield from self.chord("Alt+2")
            assert self.typing_rows["English"].has_css_class("typing-record-green")
            assert tuple(w.get_label() for w in self.typing_record_labels["English"]) == ("98%", "00:25")
            print("PASS GTK course records, 1024×600: actual Alt+2, seven rendered bands/pairs, "
                  "config change to gray, Enter starts row, exit preserves winner.", flush=True)

    app = RecordWindow()
    code = app.run([])
    return 1 if app.smoke_failure else code


if __name__ == "__main__":
    if os.environ.get("TT_RECORDS_UI_FIXTURE") == "prepared":
        raise SystemExit(run_window())
    # Never write to a real default profile.
    with tempfile.TemporaryDirectory(dir=os.environ["TOUCH_TYPING_QA_DATA_ROOT"]) as folder:
        write_fixture(folder)
        subprocess.run([sys.executable, __file__], check=True, env=dict(os.environ,
                       XDG_DATA_HOME=folder, TT_RECORDS_UI_FIXTURE="prepared"))
