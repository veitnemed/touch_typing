"""Record actual GTK X11 windows through two daily-use routes on owned Xvfb.

This is a local QA utility, not an installer dependency. It requires ffmpeg
x11grab, Xvfb, XTest, GTK4, a fresh disposable profile and an already built
engine. Native GNOME/Wayland capture approval is not being exercised here.
"""
import ctypes as C
import os
from pathlib import Path
import re
import subprocess
import sys
import traceback

from test_lab_window import LabWindowTest
from platform_linux import PHYSICAL
from gi.repository import GLib


class Recorder(LabWindowTest):
    def __init__(self):
        self.current_capture = None
        self.next_delay = 0
        super().__init__()

    def smoke_test(self):
        try:
            self.navigate("lab")
            self.output = Path(os.environ["TOUCH_TYPING_QA_OUTPUT"]).resolve()
            self.output.mkdir(parents=True, exist_ok=True)
            self.xlib = C.CDLL("libX11.so.6")
            self.xt = C.CDLL("libXtst.so.6")
            self.xlib.XOpenDisplay.argtypes = [C.c_char_p]
            self.xlib.XOpenDisplay.restype = C.c_void_p
            self.xlib.XSetInputFocus.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_ulong]
            self.xlib.XGetInputFocus.argtypes = [C.c_void_p, C.POINTER(C.c_ulong), C.POINTER(C.c_int)]
            self.xlib.XFlush.argtypes = [C.c_void_p]
            self.xt.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
            self.xd = self.xlib.XOpenDisplay(None)
            assert self.xd
            self.xid = self.window.get_surface().get_xid()
            self.xlib.XSetInputFocus(self.xd, self.xid, 1, 0)
            self.xlib.XFlush(self.xd)
            self.steps = self.exercise_daily_use(self.chord, self.frame, self.output, self.recording_boundary,
                                                 self.switch_preview)
            GLib.timeout_add(250, self.advance_slowly)
        except Exception:
            self.fail()
        return GLib.SOURCE_REMOVE

    def call(self, operation, **payload):
        if operation == "lab-start":
            # Shortcut inhibition is compositor-specific. This shim applies to
            # the isolated Xvfb runner only; all subsequent input is XTest.
            lab = self.lab
            class XvfbCapture:
                def get_property(self, name):
                    assert name == "shortcuts-inhibited"
                    return True
                def disconnect(self, _handler):
                    pass
                def restore_system_shortcuts(self):
                    pass
            def acquire():
                assert self.window.is_active()
                lab.surface = XvfbCapture()
                lab.waiting_capture = False
                lab.render(super(Recorder, self).call("lab-resume"))
                lab.desktop.grab_focus()
            lab.acquire = acquire
        return super().call(operation, **payload)

    def send(self, keys):
        focused, revert = C.c_ulong(), C.c_int()
        self.xlib.XGetInputFocus(self.xd, C.byref(focused), C.byref(revert))
        assert self.xid == focused.value and self.window.is_active(), keys
        physical = {value: key for key, value in PHYSICAL.items()}
        modifiers = {"Ctrl": "LCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
        for key, down in keys:
            assert self.xt.XTestFakeKeyEvent(self.xd, physical[modifiers.get(key, key)], down, 0)
        self.xlib.XFlush(self.xd)

    def chord(self, label):
        keys = label.split("+")
        self.send([(key, True) for key in keys] + [(key, False) for key in reversed(keys)])
        self.next_delay = 210
        yield

    def switch_preview(self, frame):
        self.send([("Alt", True), ("Tab", True), ("Tab", False)])
        yield
        assert self.lab.state["scene"]["switcher"]
        yield from frame("gnome-04a-switcher")
        self.send([("Alt", False)])
        self.next_delay = 210
        yield

    def frame(self, path):
        self.capture_widget_png(path)
        self.next_delay = 780

    def recording_boundary(self, name):
        if self.current_capture:
            proc, log = self.current_capture
            self.current_capture = None
            if proc.poll() is None:
                proc.stdin.write(b"q\n")
                proc.stdin.flush()
            assert proc.wait(timeout=8) == 0
            log.close()
        if name:
            target = self.output / f"TouchTyping-{name}.mp4"
            log = (self.output / f"{name}-ffmpeg.log").open("w")
            proc = subprocess.Popen(["ffmpeg", "-hide_banner", "-loglevel", "warning", "-y",
                "-f", "x11grab", "-window_id", str(self.xid), "-draw_mouse", "0", "-framerate", "20",
                "-i", os.environ["DISPLAY"], "-an", "-c:v", "libx264", "-preset", "ultrafast",
                "-crf", "19", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(target)],
                stdin=subprocess.PIPE, stdout=log, stderr=log)
            self.current_capture = proc, log

    def advance_slowly(self):
        try:
            next(self.steps)
            delay = self.next_delay or 24
            self.next_delay = 0
            GLib.timeout_add(delay, self.advance_slowly)
        except StopIteration:
            self.recording_boundary(None)
            self.quit()
        except Exception:
            self.fail()
        return GLib.SOURCE_REMOVE

    def fail(self):
        traceback.print_exc()
        self.smoke_failure = True
        try:
            self.recording_boundary(None)
        finally:
            self.quit()


if __name__ == "__main__":
    display = re.fullmatch(r"(?:127\.0\.0\.1)?:([0-9]+)", os.environ.get("DISPLAY", ""))
    pid = int(os.environ["TOUCH_TYPING_QA_XVFB_PID"])
    command = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    if (not display or not command or Path(os.fsdecode(command[0])).name != "Xvfb"
            or f":{display[1]}".encode() not in command or "-auth".encode() not in command):
        raise SystemExit("Owned isolated Xvfb display with authentication required")
    profile = Path(os.environ["XDG_DATA_HOME"])
    if (not profile.is_absolute() or not profile.is_dir() or profile == Path.home() / ".local/share"
            or (profile / "TouchTypingStudio-QA/progress.xml").exists()
            or os.environ.get("TOUCH_TYPING_QA_DATA_ROOT")):
        raise SystemExit("Fresh absolute input test profile required")
    app = Recorder()
    status = app.run([sys.argv[0]])
    raise SystemExit(1 if app.smoke_failure else status)
