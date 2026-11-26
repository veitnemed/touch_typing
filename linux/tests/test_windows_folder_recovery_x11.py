"""Reproduce and recover the reported Win10 exercise in the actual GTK window.

Run with a disposable X11 display (see test_preview2_native.py). Set
TOUCH_TYPING_QA_RECOVERY_BEFORE=1 to verify the old failure, not the new behavior.
"""
import ctypes as C
import os
from test_preview2_native import QA, out, ptr, x, xt, codes, alias
from test_windows_folder_recovery import HOME, LOGS, LOG, PROJECT, active
from gi.repository import GdkX11

x.XDefaultRootWindow.argtypes = [C.c_void_p]
x.XDefaultRootWindow.restype = C.c_ulong
x.XTranslateCoordinates.argtypes = [C.c_void_p,C.c_ulong,C.c_ulong,C.c_int,C.c_int,
    C.POINTER(C.c_int),C.POINTER(C.c_int),C.POINTER(C.c_ulong)]
x.XTranslateCoordinates.restype = C.c_int
xt.XTestFakeMotionEvent.argtypes = [C.c_void_p,C.c_int,C.c_int,C.c_int,C.c_ulong]
xt.XTestFakeButtonEvent.argtypes = [C.c_void_p,C.c_uint,C.c_int,C.c_ulong]


class NativeRecovery(QA):
    def chord(self, label):
        head = label.split("+",1)[0]
        release = (codes[alias.get(head,head)],False)
        before = self.received.get(release,0)
        yield from super().chord(label)
        for _ in range(100):
            if self.received.get(release,0)>before:
                return
            yield
        raise AssertionError("Unacknowledged native release: "+label)

    def selection(self, lab, path, key):
        for _ in range(30):
            w = active(lab.state)
            if w["item"] >= 0 and path == w["entries"][w["item"]]["path"]:
                return
            yield from self.chord(key)
        raise AssertionError(("Unreachable file", path, active(lab.state)["folder"]))

    def tap(self, lab, command, owner, horizontal=.5, vertical=.5):
        # Read the same live hit region the GTK widget actually registered.
        # The following click goes through XTest/GDK, not a direct engine call.
        for _ in range(7): yield
        name = "explorer:desktop-"+command
        box = next(hit[0] for hit in reversed(lab.desktop._notebook_hits) if hit[1]==name and hit[-1]==owner)
        valid, view = lab.desktop.compute_bounds(self.window)
        assert valid
        px = int(view.get_x()+box[0]+box[2]*horizontal)
        py = int(view.get_y()+box[1]+box[3]*vertical)
        rx, ry, child = C.c_int(), C.c_int(), C.c_ulong()
        xid = GdkX11.X11Surface.get_xid(self.window.get_surface())
        assert x.XTranslateCoordinates(ptr,xid,x.XDefaultRootWindow(ptr),px,py,C.byref(rx),C.byref(ry),C.byref(child))
        xt.XTestFakeMotionEvent(ptr,-1,rx.value,ry.value,0)
        xt.XTestFakeButtonEvent(ptr,1,1,0); xt.XTestFakeButtonEvent(ptr,1,0,0)
        x.XFlush(ptr)
        for _ in range(10): yield

    def prepare(self):
        self.navigate("home")
        for _ in range(20): yield
        self.focus_window()
        self.live_os_choices["windows"].grab_focus()
        yield from self.chord("Space")
        for _ in range(16): yield
        menu, picker, level, mode, actions, time, start = self.home_configure["windows"]
        menu.grab_focus()
        yield from self.chord("Space")
        options = picker.get_model()
        i = next(i for i in range(options.get_n_items()) if options.get_string(i) == "Windows 10: вернуться к папке проекта")
        picker.set_selected(i); actions.set_selected(0); time.set_selected(0)
        start.grab_focus()
        yield from self.chord("Enter")
        for _ in range(100):
            if self.lab and self.lab.active and self.lab.state.get("mission")=="windows-desktop-return-0":
                break
            yield
        assert self.lab and self.lab.active and self.lab.state["mission"]=="windows-desktop-return-0"
        self.focus_window()
        self.lab.desktop.grab_focus()
        return self.lab

    def exercise(self):
        old = os.environ.get("TOUCH_TYPING_QA_RECOVERY_BEFORE") == "1"
        lab = yield from self.prepare()
        yield from self.chord("Super+E")
        assert lab.state["task"]["text"]=="project"
        score = lab.state["solved"]
        yield from self.selection(lab,PROJECT,"Up")
        yield from self.chord("Enter")
        yield from self.selection(lab,LOGS,"Down")
        yield from self.chord("Enter")
        yield from self.selection(lab,LOG,"Down")
        yield from self.chord("Enter")
        assert not self.engine.storage_error and active(lab.state)["url"]==LOG
        assert lab.state["task"]["text"]=="project" and lab.state["solved"]==score
        if not old:
            assert lab.status.get_visible() and "Проводник не в фокусе" in lab.status.get_label()
        self.capture_widget_png(out/"01-detour-app-log.png")
        if old:
            yield from self.chord("Alt+Tab")
        else:
            explorer = next(w for w in lab.state["scene"]["windows"] if w["app"]=="files")
            editor = active(lab.state)
            yield from self.tap(lab,"maximize",editor["id"])
            assert active(lab.state)["state"]=="maximized"
            self.capture_widget_png(out/"02-maximize-restoration.png")
            yield from self.tap(lab,"maximize",editor["id"])
            assert active(lab.state)["state"]=="normal"
            # The still-exposed left part of Explorer must become the actual focus.
            yield from self.tap(lab,"focus",explorer["id"],.08,.025)
        assert active(lab.state)["folder"]==LOGS
        if not old:
            assert lab.status.get_visible() and "Alt+↑" in lab.status.get_label()
        yield from self.chord("Alt+Up")
        yield from self.chord("Alt+Up")
        assert active(lab.state)["folder"]==HOME
        yield from self.selection(lab,PROJECT,"Down")
        self.capture_widget_png(out/"03-correct-project.png")
        if old:
            assert lab.state["solved"]==score and lab.state["task"]["id"]=="file-next"
            print("REPRO ca621b0: correct Explorer path and project selection, no credit",flush=True)
            return
        assert lab.state["solved"]==score+1 and lab.state["task"]["id"]=="file-open"
        # The user may finish with keyboard only while the unrelated editor exists.
        for key in ("Enter","Alt+Up","Super+D","Super+D"):
            yield from self.chord(key)
        assert lab.state["finished"] and lab.state["solved"]==score+5
        assert any(w["url"]==LOG for w in lab.state["scene"]["windows"])
        self.capture_widget_png(out/"04-completion.png")
        print("PASS native GTK Windows profile: actual window clicks recovery, exact keys, original editor and completion",flush=True)


if __name__ == "__main__":
    app = NativeRecovery()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
