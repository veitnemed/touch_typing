"""Run actual mouse/focus checks on the compact Chrome history work surface."""
import ctypes as C

from test_preview2_native import out, ptr, x
from gi.repository import GdkX11
from test_chrome_history_native import ChromeHistoryQA, active
from test_notebook_workbench_native import WorkbenchWindow

x.XResizeWindow.argtypes = (C.c_void_p, C.c_ulong, C.c_uint, C.c_uint)
x.XSync.argtypes = (C.c_void_p, C.c_int)


class HistoryPointer(ChromeHistoryQA):
    def chord(self, label):
        yield from super().chord(label)
        if label != "Ctrl+H":
            return
        window = self.window
        desktop = self.lab.desktop
        xid = GdkX11.X11Surface.get_xid(window.get_surface())
        window.unmaximize()
        x.XResizeWindow(ptr, xid, 652, 600)
        x.XSync(ptr, False)
        for _ in range(120):
            if 500 <= desktop.get_width() < 700:
                break
            yield
        assert 500 <= desktop.get_width() < 700, ("actual compact scene", desktop.get_width())
        before = self.lab.state["solved"]
        history = active(self.lab)
        assert history["url"] == "chrome://history" and history["item"] == -1
        target = history["entries"][0]["path"]
        # The rectangles are read from the rendered viewport; the click itself
        # is a real XTest coordinate event, never an RPC invocation in the test.
        yield from WorkbenchWindow.pointer(self, "browser-history:entry", path=target)
        walked = active(self.lab)
        assert walked["entries"][walked["item"]]["path"] == target and walked["item"] >= 0
        assert self.lab.state["solved"] == before
        self.capture_widget_png(out / "00-compact-history-pointer-row.png")
        yield from WorkbenchWindow.pointer(self, "browser-history:search")
        assert active(self.lab)["item"] == -1 and active(self.lab)["focus"] == "history"
        assert self.lab.state["solved"] == before and self.window.get_focus() is desktop
        self.capture_widget_png(out / "00b-compact-history-pointer-search.png")
        print("PASS real X11 pointer: compact History row selects, Search refocuses without false lesson credit.", flush=True)


if __name__ == "__main__":
    app = HistoryPointer()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
