"""Check OS-generated Backspace autorepeat through a native GTK Live window.

Run on an isolated authenticated Xvfb, with its PID in
TOUCH_TYPING_QA_XVFB_PID, fresh absolute XDG_DATA_HOME, and
TOUCH_TYPING_QA_OUTPUT naming an evidence directory. For a package test set
PYTHONPATH to the extracted package and TOUCH_TYPING_ENGINE to its engine.
Only the inhibitor handshake is shimmed; editing enters via XTest and GDK.
"""
import os
from pathlib import Path
import re
import sys
import time

from platform_linux import PHYSICAL
from record_daily_use_x11 import Recorder


class BrowserRepeat(Recorder):
    def exercise_daily_use(self, chord, _frame, output, _boundary, _switch_preview):
        lab = self.lab
        lab.picker.set_selected(next(i for i, row in enumerate(lab.classes) if row["id"] == "chrome"))
        lab.level.set_selected(0)
        lab.limit.set_selected(0)
        lab.time_limit.set_selected(0)
        lab.mission_picker.set_selected(next(i for i, row in enumerate(lab.choices)
                                             if row[0] == "chrome-search-clone"))
        lab.start()
        assert lab.active and not lab.state["paused"] and self.window.get_focus() == lab.desktop

        def chrome():
            return next(w for w in lab.state["scene"]["windows"] if w["app"] == "chrome" and w["active"])

        yield from chord("Ctrl+L")
        assert chrome()["focus"] == "url", chrome()
        for key in "ABCDEFGHIJ" * 2:
            yield from chord(key)
        yield from chord("Left")
        before = chrome()
        score = lab.state["solved"]
        assert before["input"] == "abcdefghij" * 2 and before["cursor"] == 19, before
        self.capture_widget_png(output / "backspace-01-before-hold.png")

        observed = (next(code for code, key in PHYSICAL.items() if key == "Backspace"), True)
        received = self.received.get(observed, 0)
        self.send([("Backspace", True)])
        try:
            deadline = time.monotonic() + 5
            while self.received.get(observed, 0) < received + 4 and time.monotonic() < deadline:
                yield
        finally:
            self.send([("Backspace", False)])
        for _ in range(6):
            yield

        presses = self.received.get(observed, 0) - received
        assert presses >= 4, ("No OS-generated repeats reached GTK", presses)
        after = chrome()
        removed = len(before["input"]) - len(after["input"])
        assert removed == presses, ("Repeat ignored", presses, after["input"], after["cursor"])
        assert after["cursor"] == before["cursor"] - removed, after
        assert after["input"] == before["input"][:after["cursor"]] + before["input"][before["cursor"]:], after
        assert not after["selected"] and lab.state["solved"] == score
        self.capture_widget_png(output / "backspace-02-after-hold.png")

        deadline = time.monotonic() + .55
        while time.monotonic() < deadline:
            yield
        assert chrome() == after, "Key-up did not stop edits"
        print(f"PASS native XTest/GDK: Backspace delivered {presses} keydowns "
              f"and left {after['input']!r} at caret {after['cursor']}; no extra goal credit",
              flush=True)


if __name__ == "__main__":
    display = re.fullmatch(r"(?:127\.0\.0\.1)?:([0-9]+)", os.environ.get("DISPLAY", ""))
    pid = int(os.environ["TOUCH_TYPING_QA_XVFB_PID"])
    command = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    if (not display or not command or Path(os.fsdecode(command[0])).name != "Xvfb"
            or f":{display[1]}".encode() not in command or b"-auth" not in command):
        raise SystemExit("Owned isolated Xvfb display with authentication required")
    profile = Path(os.environ["XDG_DATA_HOME"])
    if (not profile.is_absolute() or not profile.is_dir() or profile == Path.home() / ".local/share"
            or (profile / "TouchTypingStudio-QA/progress.xml").exists()
            or os.environ.get("TOUCH_TYPING_QA_DATA_ROOT")):
        raise SystemExit("Fresh absolute input test profile required")
    app = BrowserRepeat()
    status = app.run([sys.argv[0]])
    raise SystemExit(1 if app.smoke_failure else status)
