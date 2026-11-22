"""Exercise the actual GTK window using a private headless GNOME Shell.

Run against a dedicated test session with WAYLAND_DISPLAY=training-qa and
XDG_RUNTIME_DIR pointing at its private runtime directory.
"""
import os
from pathlib import Path
import sys
import traceback

from gi.repository import Gio, GLib
from touch_typing import Studio


class WindowTest(Studio):
    def __init__(self):
        super().__init__(smoke="-", smoke_size=(960, 600))
        self.notifications = 0

    def close_request(self, *args):
        self.notifications += 1
        return super().close_request(*args)

    def smoke_test(self):
        self.steps = self.exercise()
        GLib.timeout_add(150, self.advance)
        return GLib.SOURCE_REMOVE

    def advance(self):
        try:
            next(self.steps)
            return GLib.SOURCE_CONTINUE
        except StopIteration:
            print("PASS: GNOME preview, start, Alt+F4, Alt+J, Ctrl+Shift+V, Esc and typed text", flush=True)
        except Exception:
            traceback.print_exc()
            self.smoke_failure = True
        self.quit()
        return GLib.SOURCE_REMOVE

    def exercise(self):
        bus = Gio.bus_get_sync(Gio.BusType.SESSION)
        interface = "org.gnome.Mutter.RemoteDesktop"
        def call(path, api, method, args=None):
            return bus.call_sync(interface, path, api, method, args, None, Gio.DBusCallFlags.NONE, 3000, None)
        session, = call("/org/gnome/Mutter/RemoteDesktop", interface, "CreateSession").unpack()
        call(session, interface + ".Session", "Start")
        def keys(*codes):
            for code in codes:
                call(session, interface + ".Session", "NotifyKeyboardKeycode",
                     GLib.Variant("(ub)", (abs(code), code > 0)))
                yield
        try:
            # A new GNOME session may initially show its overview.
            if not self.window.is_active():
                yield from keys(1, -1)
                self.window.present()
                yield
                yield
            assert self.window.is_active(), "The trainer must have focus on the private compositor"
            self.set_group("window-close")
            self.start_button.grab_focus()
            yield from keys(57, -57)
            yield
            assert self.workout == "shortcuts" and self.state["inputKeys"] == "Alt+J"
            yield from keys(56, 36, -36, -56)
            assert self.state["solved"] == 1 and self.state["score"]["substituteCorrect"] == 1
            assert not self.state["verifiedScene"]["window"] and self.state["status"]
            yield from keys(64, -64)  # F6 requests the physical combination
            for _ in range(4):
                yield
            deny = "--deny-capture" in sys.argv
            if self.state["inputMode"] == "waiting" and not self.window.is_active():
                reply = 1 if deny else 28
                yield from keys(reply, -reply)
            mode = "substitute" if deny else "exact"
            for _ in range(36):
                if self.state["inputMode"] == mode and self.window.is_active():
                    break
                yield
            assert self.state["inputMode"] == mode and self.window.is_active()
            if deny:
                assert self.capture_note and not self.paused
                yield from keys(56, 36, -36, -56)
                assert self.state["score"]["substituteVerifiedResults"] == 2
            else:
                requests = self.notifications
                yield from keys(56, 62, -62, -56)
                assert self.notifications == requests, "Captured Alt+F4 must act only inside the lesson"
                assert self.state["score"]["verifiedResults"] == 1
            assert self.window.is_visible() and self.state["solved"] == 2 and not self.state["verifiedScene"]["window"]
            yield from keys(1, -1)
            assert self.result_kind == "shortcuts"
            self.navigate("shortcuts")
            self.set_group("terminal-paste")
            self.start_button.grab_focus()
            yield from keys(28, -28)
            yield
            yield from keys(97, 42, 47)
            assert self.state["held"] and self.state["solved"] == 1
            yield from keys(-47, -42, -97)
            assert self.state["solved"] == 1 and self.state["score"]["verifiedResults"] == 1
            yield from keys(1, -1)
            yield from keys(30, -30)  # Probe actual system layout before text input.
            if self.layout != "EN":
                yield from keys(125, 57, -57, -125)
                for _ in range(4):yield
                yield from keys(30, -30)
            assert self.layout == "EN"
            self.navigate("typing")
            choice = [item[0] for item in self.typing_options].index("Project")
            self.typing_pick.set_selected(choice)
            self.typing_pick.grab_focus()
            yield from keys(63, -63)  # F5 also works from the exercise selector
            yield
            assert self.workout == "typing" and self.state["key"] == "D"
            yield from keys(32, -32)
            assert self.state["count"] == 1 and self.state["before"].endswith("d")
        finally:
            call(session, interface + ".Session", "Stop")


if __name__ == "__main__":
    socket = Path(os.environ.get("XDG_RUNTIME_DIR", "")) / "training-qa"
    if os.environ.get("WAYLAND_DISPLAY") != "training-qa" or not socket.is_socket():
        sys.exit("Use an isolated GNOME test compositor named training-qa.")
    # Verify the bus controls the same isolated compositor as our Wayland window.
    bus = Gio.bus_get_sync(Gio.BusType.SESSION)
    pid, = bus.call_sync("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus",
                         "GetConnectionUnixProcessID", GLib.Variant("(s)", ("org.gnome.Mutter.RemoteDesktop",)),
                         None, Gio.DBusCallFlags.NONE, 3000, None).unpack()
    command = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    if not {b"--headless", b"--wayland-display=training-qa"}.issubset(command):
        sys.exit("The supplied D-Bus session must belong to the headless test compositor.")
    app = WindowTest()
    status = app.run([sys.argv[0]])
    sys.exit(1 if app.smoke_failure else status)
