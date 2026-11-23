"""Exercise native GTK controls with a disposable QA profile and failure screenshots."""
import os
from pathlib import Path
import sys
import traceback

import gi
gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
from gi.repository import Gdk, Gio, GLib, Gtk
from test_lab_window import LabWindowTest
from touch_typing import display_chord
from visuals import PracticeLine


class ControlsTest(LabWindowTest):
    def __init__(self):
        super().__init__()
        if "--deny-capture" in sys.argv:
            self.set_application_id("com.veitnemed.TouchTyping.QADenied")
        self.signal_errors = []
        self.trace = []
        sys.excepthook = self.signal_exception

    def signal_exception(self, kind, error, stack):
        self.signal_errors.append(str(error))
        traceback.print_exception(kind, error, stack)

    def mark(self, message):
        self.trace.append(message)
        print("UI " + message, flush=True)

    @staticmethod
    def widgets(root):
        yield root
        child = root.get_first_child()
        while child:
            yield from ControlsTest.widgets(child)
            child = child.get_next_sibling()

    def click(self, label):
        controls = [widget for widget in self.widgets(self.window)
                    if isinstance(widget, Gtk.Button) and widget.get_visible()
                    and widget.get_sensitive() and widget.get_label() == label]
        assert len(controls) == 1, (label, len(controls), self.trace)
        self.mark("activate " + label)
        controls[0].grab_focus()
        assert controls[0].activate(), label
        for _ in range(40):
            yield
        assert not self.signal_errors, (self.signal_errors, self.trace)

    def finish_live(self):
        # Completion remains available under real compact lesson controls.
        self.lab.settings_menu.popup()
        for _ in range(8):
            yield
        assert self.lab.settings_menu.get_popover().get_visible()
        yield from self.click(self.lab.finish.get_label())

    def check_starting_level_advice(self):
        lab = self.lab
        original_call = lab.host.call

        def catalog_fixture(command, **kwargs):
            catalog = original_call(command, **kwargs)
            if command != "lab-catalog":
                return catalog
            return dict(catalog, drafts=[{"app": "code", "level": 0, "guidance": "guided"}],
                        recommendations=[{"app": "code", "level": 2, "reason": "Самостоятельная практика."},
                                         {"app": "ubuntu", "level": None, "reason": "Пока мало попыток."}])

        lab.host.call = catalog_fixture
        try:
            lab.mode.set_selected(0)
            lab.level.set_selected(0)
            lab.picker.set_selected(next(i for i, item in enumerate(lab.classes) if item["id"] == "code"))
            assert "Рекомендуемый уровень: Сложный. Самостоятельная практика." in lab.summary.get_label()
            assert lab.level.get_selected() == 0 and lab.mode.get_selected() == 0
            assert ("@resume", "Продолжить с сохранённого места") in lab.choices
            lab.level.set_selected(2)
            assert ("@resume", "Продолжить с сохранённого места") not in lab.choices
            assert any(mission for mission, _ in lab.choices)
            lab.level.set_selected(0)
            assert ("@resume", "Продолжить с сохранённого места") in lab.choices
            lab.picker.set_selected(next(i for i, item in enumerate(lab.classes) if item["id"] == "ubuntu"))
            assert "Пока мало попыток." in lab.summary.get_label()
            assert "Рекомендуемый уровень:" not in lab.summary.get_label()
            lab.picker.set_selected(next(i for i, item in enumerate(lab.classes) if item["id"] == "chrome"))
            assert lab.summary.get_label() == lab.classes[lab.picker.get_selected()]["description"]
        finally:
            lab.host.call = original_call
            lab.describe()

    def exercise(self):
        assert PracticeLine.printable(" ", current=True) == " "
        assert display_chord("Alt+F4", {"keyboardProfile": "matebook-d16", "fnMode": "hotkeys"}) == "Fn + Alt + F4"
        assert display_chord("F11", {"keyboardProfile": "matebook-d16", "fnMode": "functions"}) == "F11"
        bus = Gio.bus_get_sync(Gio.BusType.SESSION)
        interface = "org.gnome.Mutter.RemoteDesktop"
        def call(path, api, method, args=None):
            return bus.call_sync(interface, path, api, method, args, None, Gio.DBusCallFlags.NONE, 3000, None)
        session, = call("/org/gnome/Mutter/RemoteDesktop", interface, "CreateSession").unpack()
        call(session, interface + ".Session", "Start")
        def key(code, down):
            call(session, interface + ".Session", "NotifyKeyboardKeycode", GLib.Variant("(ub)", (code, down)))
        try:
            if not self.window.is_active():
                key(1, True); yield
                key(1, False); yield
                self.window.present()
            self.navigate("lab")
            self.mark("starting level advice leaves saved draft and manual levels available")
            self.check_starting_level_advice()
            for title in ("Ubuntu", "Chrome", "VS Code", "Терминал"):
                yield from self.click(title)
                assert self.lab.classes[self.lab.picker.get_selected()]["title"] == title
            for mode in (0, 1):
                self.lab.mode.set_selected(mode)
                for level in (0, 1, 2):
                    self.lab.level.set_selected(level)
                    assert self.lab.level.get_selected() == level
            self.lab.picker.set_selected(0)
            self.lab.level.set_selected(0)
            self.lab.mode.set_selected(0)
            yield from self.click("Начать · Пробел")
            for _ in range(40):yield
            if "--deny-capture" in sys.argv:
                assert self.lab.state["paused"] and not self.window.is_active()
                self.mark("deny capture keeps timer paused")
                key(1, True); yield
                key(1, False); yield
                for _ in range(60):yield
                assert self.lab.state["paused"] and self.lab.state["seconds"] == 0
                assert not self.lab.waiting_capture and self.lab.surface is None
                assert "отклонён" in self.lab.status.get_label()
                yield from self.finish_live()
                assert self.lab.result
                self.mark("PASS denial leaves task unscored and capture released")
                return
            if self.lab.state["paused"] and not self.window.is_active():
                key(28, True); yield
                key(28, False); yield
            for _ in range(100):
                if not self.lab.state["paused"]:break
                yield
            assert not self.lab.state["paused"]
            self.mark("duplicate start is idempotent")
            task = self.lab.state["task"]["id"]
            self.lab.start()
            assert self.lab.state["task"]["id"] == task
            assert self.lab.guide.board.cells["F11"][0].get_label() == "F11\nPrtSc"
            assert not self.lab.guide.board.cells["Print"][0].get_visible()
            original = self.lab.state
            trial = dict(original, feedback="error", lastKey="Print")
            self.lab.render(trial)
            assert "miss" in self.lab.guide.board.cells["F11"][0].get_css_classes()
            self.lab.aids["mistakes"].set_active(False);yield
            assert "miss" not in self.lab.guide.board.cells["F11"][0].get_css_classes()
            self.lab.aids["mistakes"].set_active(True);yield
            self.lab.render(original)
            for aid in ("keys", "hands", "mistakes"):
                control = self.lab.aids[aid]
                initial = control.get_active()
                control.set_active(not initial);yield
                control.set_active(initial);yield
                assert control.get_active() == initial
            assert self.library["showMistakeKey"]
            self.lab.toggle_diagnostic()
            self.lab.key_down("F11", "", keycode=95)
            assert "код 95" in self.lab.diagnostic.get_label()
            self.lab.key_up("F11")
            self.lab.toggle_diagnostic()
            self.mark("pause button, focus, modified control key")
            yield from self.click(self.lab.pause.get_label())
            assert self.lab.state["paused"]
            self.lab.key_down("F8", "", Gdk.ModifierType.CONTROL_MASK)
            assert self.lab.state["paused"]
            self.mark("resume rejects a modifier held before capture")
            key(29, True);yield
            yield from self.click(self.lab.pause.get_label())
            assert self.lab.state["paused"] and "Отпустите" in self.lab.status.get_label()
            key(29, False);yield
            key(28, True);yield
            key(28, False);yield
            assert self.lab.state["paused"], "Enter must not resume a paused class"
            key(57, True);yield
            key(57, False);yield
            for _ in range(50):
                if not self.lab.state["paused"]:break
                yield
            assert not self.lab.state["paused"]
            self.mark("focus loss and close request protect the running lesson")
            probe = Gtk.Window(title="QA focus probe", transient_for=self.window)
            probe.present()
            for _ in range(40):yield
            assert self.lab.state["paused"] and self.lab.surface is None
            probe.destroy()
            self.window.present()
            for _ in range(30):yield
            yield from self.click(self.lab.pause.get_label())
            for _ in range(50):
                if not self.lab.state["paused"]:break
                yield
            if self.lab.state["paused"] and not self.window.is_active():
                key(28, True);yield
                key(28, False);yield
                for _ in range(50):yield
            assert not self.lab.state["paused"]
            self.window.close()
            for _ in range(20):yield
            assert self.window.get_visible() and self.lab.state["paused"] and self.close_dialog
            assert self.lab.surface is None
            self.close_dialog.response(Gtk.ResponseType.CANCEL)
            for _ in range(20):yield
            assert self.close_dialog is None and self.window.get_visible() and self.lab.state["paused"]
            yield from self.finish_live()
            assert self.lab.result and not self.lab.active and self.lab.surface is None
            panel = self.lab.modal_layer.get_first_child()
            found, bounds = panel.compute_bounds(self.window)
            assert found and bounds.get_width() <= 710, bounds
            assert abs(bounds.get_x() + bounds.get_width() / 2 - self.window.get_width() / 2) < 18, bounds
            output = Path(os.environ.get("TOUCH_TYPING_QA_OUTPUT", "."))
            paintable = Gtk.WidgetPaintable.new(self.window)
            snapshot = Gtk.Snapshot()
            paintable.snapshot(snapshot, paintable.get_intrinsic_width(), paintable.get_intrinsic_height())
            self.window.get_renderer().render_texture(snapshot.to_node(), None).save_to_png(str(output / "lab-result-early.png"))
            yield from self.click("Завершить")
            self.mark("editor invalid input and editable custom key")
            self.navigate("editor")
            yield from self.click("Сохранить сочетание")
            assert self.banner.get_visible()
            self.message()
            self.edit_title.set_text("QA keyboard")
            self.edit_group.set_text("QA")
            self.edit_keys.set_text("⊞+Space")
            yield from self.click("Подсказки для пальцев")
            assert "LSuper" in self.finger_widgets and "RSuper" not in self.finger_widgets
            yield from self.click("Сохранить сочетание")
            assert self.edit_id.startswith("own/")
            yield from self.click("Удалить")
            dialog = next(w for w in Gtk.Window.list_toplevels() if isinstance(w, Gtk.MessageDialog))
            dialog.get_widget_for_response(Gtk.ResponseType.CANCEL).activate()
            for _ in range(40):yield
            assert self.edit_id.startswith("own/")
            yield from self.click("Удалить")
            dialog = next(w for w in Gtk.Window.list_toplevels() if isinstance(w, Gtk.MessageDialog))
            dialog.get_widget_for_response(Gtk.ResponseType.OK).activate()
            for _ in range(40):yield
            assert not self.edit_id
            self.editor_choice.set_selected(1);yield
            assert not self.edit_title.get_sensitive()
            yield from self.click("Скрыть")
            yield from self.click("Показать")
            yield from self.click("Сделать личную копию")
            assert self.edit_title.get_sensitive() and not self.edit_id
            yield from self.click("Записать нажатием")
            assert self.recording
            key(1, True);yield
            key(1, False);yield
            assert not self.recording
            yield from self.click("Тексты и символы")
            self.set_title.set_text("QA text")
            self.set_body.get_buffer().set_text("Python signal spectrum")
            yield from self.click("Сохранить набор")
            assert self.set_id
            self.navigate("typing")
            self.typing_pick.set_selected(1)
            yield from self.click("Начать · F5")
            assert self.workout == "typing"
            self.hint_key("keys");yield
            self.hint_key("hands");yield
            self.typing_pause();yield
            assert self.paused
            self.typing_resume();yield
            assert not self.paused
            self.stop();yield
            assert self.result_kind == "typing"
            self.navigate("progress");yield
            self.navigate("lab");yield
            self.lab.start();yield
            self.hold()
            try:
                # Force a real local save failure through ProfileStore's target,
                # in the isolated QA profile only. Not a mocked engine response.
                profile = Path(os.environ.get("TOUCH_TYPING_QA_DATA_ROOT") or os.environ["XDG_DATA_HOME"]) / "TouchTypingStudio-QA"
                profile.mkdir(exist_ok=True, parents=True)
                obstacle = profile / "progress.xml.writing"
                self.lab.release_capture()
                self.lab.waiting_capture = False
                self.call("lab-resume")
                state = self.call("lab-key", key="A", pressed=True, text="a")
                self.call("lab-key", key="A", pressed=False, text="")
                assert state
                obstacle.mkdir()
                self.window.close()
                for _ in range(20):yield
                assert self.close_dialog and self.lab.state["paused"] and self.lab.surface is None
                self.close_dialog.response(Gtk.ResponseType.ACCEPT)
                for _ in range(20):yield
                assert self.window.get_visible(), "Failed save must veto window close"
                assert self.lab.active and self.lab.save_pending and self.lab.state["paused"]
                assert not self.lab.state["saved"] and self.lab.surface is None
                self.lab.acquire()
                assert self.lab.surface is None, "Save retry must not re-acquire keys"
                obstacle.rmdir()
                yield from self.click("Повторить сохранение")
                assert not self.window.get_visible(), "Successful retry must finish the original close"
            finally:
                if 'obstacle' in locals() and obstacle.is_dir():
                    obstacle.rmdir()
                self.release()
            assert not self.signal_errors, (self.signal_errors, self.trace)
            output = Path(os.environ.get("TOUCH_TYPING_QA_OUTPUT", "."))
            (output / "ui-actions.log").write_text("\n".join(self.trace), encoding="utf-8")
            self.mark("PASS controls, forms, validation, confirmations, aids, focus and lifecycle")
        finally:
            call(session, interface + ".Session", "Stop")


if __name__ == "__main__":
    if os.environ.get("WAYLAND_DISPLAY") != "training-qa":
        raise SystemExit("An isolated training-qa compositor is required")
    app = ControlsTest()
    result = app.run([sys.argv[0]])
    sys.exit(1 if app.smoke_failure else result)
