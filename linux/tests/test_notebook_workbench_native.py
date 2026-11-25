"""Exercise a real packed GTK notebook through X11 keyboard *and* pointer input.

Use the same clean Xvfb profile as test_editor_notebooks_x11.py. Only the
Wayland shortcut-consent handshake is bypassed because X11 has no such API.
The click targets' coordinates come from real current paint bounds; no action
calls the engine directly or mutates its data. Pointer actions never claim a
required physical keyboard step. PNGs are captured from the mapped GTK window.
"""
import ctypes as C
from unittest.mock import patch

from test_preview2_native import QA, CaptureConsent, codes, out, ptr, x, xt
from gi.repository import GdkX11, Graphene
from touch_typing import LabPage

x.XDefaultRootWindow.argtypes = [C.c_void_p]
x.XDefaultRootWindow.restype = C.c_ulong
x.XTranslateCoordinates.argtypes = [C.c_void_p, C.c_ulong, C.c_ulong, C.c_int, C.c_int,
                                   C.POINTER(C.c_int), C.POINTER(C.c_int), C.POINTER(C.c_ulong)]
xt.XTestFakeMotionEvent.argtypes = [C.c_void_p,C.c_int,C.c_int,C.c_int,C.c_ulong]
xt.XTestFakeButtonEvent.argtypes = [C.c_void_p,C.c_uint,C.c_int,C.c_ulong]


class WorkbenchWindow(QA):
    def press(self, labels):
        """Send a batch of literal scan-code taps, waiting for a painted frame."""
        for name in labels:
            xt.XTestFakeKeyEvent(ptr,codes[name],1,0)
            xt.XTestFakeKeyEvent(ptr,codes[name],0,0)
        x.XFlush(ptr)
        for _ in range(11):
            yield

    def pointer(self, action, cell=None, path=None):
        desktop = self.lab.desktop
        for _ in range(12):
            yield  # a real frame lays out these controls
        bounds = [r for r, command, _, i, clicks, _, key in desktop._notebook_hits
                  if command == action and (cell is None or cell == i) and (path is None or key == path) and clicks == 1]
        assert bounds, ("control not visible", action, cell,
                        [(command,i) for _,command,_,i,*_ in desktop._notebook_hits])
        left, top, width, height = bounds[-1]
        local = Graphene.Point(); local.init(left+width/2, top+height/2)
        ok, pixel = desktop.compute_point(self.window, local)
        assert ok
        sx, sy, child = C.c_int(),C.c_int(),C.c_ulong()
        xid = GdkX11.X11Surface.get_xid(self.window.get_surface())
        assert x.XTranslateCoordinates(ptr,xid,x.XDefaultRootWindow(ptr),round(pixel.x),round(pixel.y),
                                       C.byref(sx),C.byref(sy),C.byref(child))
        assert self.window.get_surface().get_width() and self.window.get_mapped()
        xt.XTestFakeMotionEvent(ptr,-1,sx.value,sy.value,0)
        xt.XTestFakeButtonEvent(ptr,1,1,0); xt.XTestFakeButtonEvent(ptr,1,0,0)
        x.XFlush(ptr)
        for _ in range(11):
            yield

    def exercise_explorer(self, lab, active):
        yield from self.chord("Ctrl+Shift+E")
        scene = active()
        assert scene["explorerActions"] and scene["focus"]=="explorer"
        rows = lambda: active()["explorerEntries"]
        app_path = next(entry["path"] for entry in rows() if entry["title"]=="app.py")
        yield from self.pointer("explorer:select", path=app_path)
        assert active()["focus"]=="explorer" and active()["explorerEntries"][active()["item"]]["path"]==app_path
        yield from self.chord("F2")
        assert active()["focus"]=="file-name" and active()["input"]=="app.py"
        yield from self.chord("Esc")
        assert any(entry["path"]==app_path for entry in rows())
        yield from self.pointer("explorer:new-folder")
        assert active()["focus"]=="file-name" and active()["renamePath"].endswith("/")
        yield from self.typed({"id":"type-code","text":"reviewed"})
        yield from self.chord("Enter")
        assert [entry["title"] for entry in rows()].count("reviewed")==1
        yield from self.pointer("explorer:new-folder")
        yield from self.typed({"id":"type-code","text":"reviewed"})
        yield from self.chord("Enter")
        assert active()["focus"]=="file-name" and "занято" in active()["status"],active()["status"]
        assert [entry["title"] for entry in rows()].count("reviewed")==1
        self.capture_widget_png(out/"explorer-01-no-overwrite.png")
        yield from self.video_hold("Конфликт имени папки: данные не заменены")
        yield from self.pointer("explorer:cancel")
        yield from self.chord("Shift+F10")
        for _ in range(11): yield
        assert lab.desktop._explorer_menu_visible
        assert any(item[0]=="undo" and item[2] for item in lab.desktop._explorer_choices)
        for _ in range(6): yield from self.chord("Down")
        yield from self.chord("Enter")
        assert all(entry["title"]!="reviewed" for entry in rows()) and any(entry["path"]==app_path for entry in rows())
        self.capture_widget_png(out/"explorer-02-undo.png")
        print("PASS native GTK Explorer selection, contextual F2/cancel, real create/collision/undo",flush=True)

    def exercise(self):
        def accept_without_wayland(page):
            page.release_capture(); page.waiting_capture=False
            page.surface=CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()

        self.navigate("home")
        for _ in range(26): yield
        self.focus_window()
        settings, chooser, level, mode, actions, timer, start = self.home_configure["code"]
        settings.emit("clicked")
        level.set_selected(1); mode.set_selected(0); actions.set_selected(0); timer.set_selected(0)
        titles = chooser.get_model()
        selected = next(i for i in range(titles.get_n_items()) if titles.get_string(i) == "Исправить формулу в блокноте по заявке")
        chooser.set_selected(selected)
        with patch.object(LabPage,"acquire",accept_without_wayland):
            start.emit("clicked")
            for _ in range(32): yield
            lab = self.lab
            assert lab.active
            self.focus_window(); lab.desktop.grab_focus()
            active = lambda: next(w for w in lab.state["scene"]["windows"] if w["active"])
            nb = lambda: active()["notebook"]
            for _ in range(4):
                if active().get("notebook"):
                    break
                goal = lab.state["task"]
                if goal["text"]:
                    yield from self.typed({"id":"type-code","text":goal["text"]})
                if goal["keys"]:
                    yield from self.chord(goal["keys"])
            assert active()["notebook"] and active()["focus"] == "body"
            assert nb()["kernel"]["state"] == "idle" and nb()["cells"][0]["kind"] == "markdown"
            self.capture_widget_png(out/"workbench-01-open.png")
            yield from self.video_hold("Блокнот: Markdown, код и панель исполнения")
            solved = lab.state["solved"]

            # The third code cell uses a previously unbound name. Running it with
            # the real triangle proves both a target-specific command and traceback.
            assert len(nb()["cells"]) == 3
            yield from self.pointer("run",2)
            assert nb()["selected"]==2 and not nb()["editing"]
            cell = nb()["cells"][2]
            assert cell["failed"] and "NameError" in cell["output"] and len(cell["traceback"].splitlines())>=3
            assert lab.state["solved"]==solved
            self.capture_widget_png(out/"workbench-02-traceback.png")
            yield from self.video_hold("Щелчок по Run: traceback под выбранной ячейкой")

            # The Python input and the Markdown itself use the same keyboard
            # and focus pipeline as other lesson actions.
            yield from self.pointer("edit", 2)
            assert nb()["editing"] and nb()["selected"] == 2
            yield from self.chord("Ctrl+A")
            yield from self.typed({"id":"type-code","text":"print('"})
            yield from self.press(["X"]*14)
            for _ in range(12):
                yield from self.press(["X"]*10)
            yield from self.typed({"id":"type-code","text":"')"})
            for _ in range(29):
                yield from self.chord("Enter")
                yield from self.typed({"id":"type-code","text":"print(1)"})
            assert len(nb()["cells"][2]["source"].splitlines())>=25 and nb()["editing"]
            self.capture_widget_png(out/"workbench-03-caret-on-long-cell.png")
            yield from self.video_hold("Длинная ячейка: каретка видна в конце")
            yield from self.chord("Esc")
            yield from self.chord("Ctrl+Enter")
            cell = nb()["cells"][2]
            assert not cell["failed"] and not nb()["editing"]
            assert cell["output"].splitlines() == ["x"*134] + ["1"]*29, cell["output"]
            view = lab.desktop._notebook_view
            assert view.max_vertical > view.height and view.max_horizontal > 120
            vertical = view.vertical
            yield from self.chord("PageDown")
            assert lab.desktop._notebook_view.vertical>vertical
            horizontal = view.horizontal
            yield from self.chord("Shift+PageDown")
            assert lab.desktop._notebook_view.horizontal>horizontal
            for _ in range(6): yield from self.chord("PageDown")
            self.capture_widget_png(out/"workbench-04-long-output-scrolled.png")
            yield from self.video_hold("Длинный вывод прокручивается до последних строк")

            # The fixed toolbar remains reachable while the long output is scrolled.
            yield from self.pointer("add-markdown")
            assert len(nb()["cells"])==4 and nb()["cells"][3]["kind"]=="markdown" and nb()["editing"]
            yield from self.typed({"id":"type-code","text":"# Notes"})
            yield from self.chord("Esc")
            assert nb()["cells"][3]["source"]=="# Notes" and not nb()["editing"]
            self.capture_widget_png(out/"workbench-05-rendered-markdown.png")
            yield from self.chord("Shift+F10")
            for _ in range(9): yield
            assert lab.desktop._notebook_menu and "run-above" in [a for a,_,allowed in lab.desktop._notebook_menu_states if allowed]
            yield from self.chord("Esc")
            assert not lab.desktop._notebook_menu and nb()["selected"]==3
            generation = nb()["kernel"]["generation"]
            yield from self.pointer("notebook-ui:kernel")
            yield from self.pointer("restart")
            assert nb()["kernel"]["generation"]>generation and nb()["cells"][3]["source"]=="# Notes"
            assert lab.state["solved"]==solved
            print("PASS native GTK notebook toolbar, per-cell click and clue-free pointer, source/output scrolling, Markdown, menu and kernel reset.",flush=True)
            yield from self.exercise_explorer(lab,active)


if __name__ == "__main__":
    app = WorkbenchWindow()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
