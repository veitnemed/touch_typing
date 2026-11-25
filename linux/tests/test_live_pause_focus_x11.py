"""Drive the ordinary Live → Chrome pause overlay with actual X11 key events."""
from test_preview2_native import QA, out
from gi.repository import Gtk
from touch_typing import inside_widget


class PauseFocusWindow(QA):
    def focus_is(self, label):
        focused = self.window.get_focus()
        assert isinstance(focused, Gtk.Button) and inside_widget(focused, self.lab.modal_layer), (
            "Focus escaped the live pause overlay", label, type(focused).__name__,
            focused.get_label() if isinstance(focused, Gtk.Button) else None)
        assert focused.get_label() == label, (label, focused.get_label())
        assert not self.lab.content.get_can_focus(), "Covered lesson remains eligible for focus"

    @staticmethod
    def location(lab):
        state = lab.state
        return state["mission"], state["task"], state["solved"], state["errors"]

    def wait_pause(self, paused):
        for _ in range(90):
            if self.lab.state["paused"] == paused and self.lab.modal_layer.get_visible() == paused:
                return
            yield
        raise AssertionError(("Pause did not transition", paused, self.lab.state["paused"]))

    def exercise(self):
        self.navigate("home")
        for _ in range(25):
            yield
        self.focus_window()
        self.home_cards["chrome"].grab_focus()
        yield from self.chord("Enter")
        for _ in range(90):
            if self.lab and self.lab.active and not self.lab.state["paused"]:
                break
            yield
        lab = self.lab
        assert lab and lab.active and lab.window_only_keys and lab.surface is None
        assert self.window.get_focus() is lab.desktop
        yield from self.chord("Ctrl+L")
        assert lab.state["solved"] == 1
        before = self.location(lab)
        assert lab.bindings()[0] == "F8"

        yield from self.chord("F8")
        yield from self.wait_pause(True)
        self.focus_is("Продолжить")
        self.capture_widget_png(out / "live-pause-01-resume-focus.png")
        for gesture, label in (
            ("Shift+Tab", "Завершить"), ("Shift+Tab", "Начать сначала"), ("Shift+Tab", "Продолжить"),
            ("Tab", "Начать сначала"), ("Tab", "Завершить"), ("Tab", "Продолжить"),
        ):
            yield from self.chord(gesture)
            self.focus_is(label)
            assert self.location(lab) == before and lab.state["paused"]
        yield from self.chord("Enter")
        self.focus_is("Продолжить")
        assert lab.state["paused"], "Enter from an exercise must not resume it"
        yield from self.chord("Space")
        yield from self.wait_pause(False)
        assert self.window.get_focus() is lab.desktop and lab.content.get_can_focus()
        assert self.location(lab) == before

        yield from self.chord("F8")
        yield from self.wait_pause(True)
        self.focus_is("Продолжить")
        yield from self.chord("Tab")
        self.focus_is("Начать сначала")
        yield from self.chord("F8")
        yield from self.wait_pause(False)
        assert self.window.get_focus() is lab.desktop and self.location(lab) == before

        yield from self.chord("F8")
        yield from self.wait_pause(True)
        self.focus_is("Продолжить")
        yield from self.chord("Shift+Tab")
        self.focus_is("Завершить")
        self.capture_widget_png(out / "live-pause-02-finish-focus.png")
        yield from self.chord("Enter")
        for _ in range(90):
            if lab.result and not lab.active:
                break
            yield
        assert lab.result and not lab.active and lab.state["solved"] == before[2]
        print("PASS GTK Live pause: actual Resume focus, two-way modal Tab, "
              "Enter protected on Resume, Space/F8 restore the same goal, and Finish stays keyboard-operable.", flush=True)


if __name__ == "__main__":
    app = PauseFocusWindow()
    status = app.run([])
    raise SystemExit(1 if app.smoke_failure else status)
