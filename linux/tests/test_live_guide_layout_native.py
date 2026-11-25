"""Real XKB RU→EN and identical physical shortcut in ordinary GTK Live."""
import subprocess
from unittest.mock import patch
from test_preview2_native import QA, CaptureConsent, out
from touch_typing import LabPage


class GuideLayoutQA(QA):
    def exercise(self):
        def consent(page):
            page.surface = CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()

        self.navigate("home")
        for _ in range(30):
            yield
        self.focus_window()
        subprocess.run(["setxkbmap", "-layout", "ru"], check=True)
        # Route one real physical letter through GTK so the active input source
        # is observed before starting the lesson. It does not start the exercise.
        for _ in range(10):
            yield
        yield from self.chord("D")
        assert self.layout == "RU"
        with patch.object(LabPage, "acquire", consent):
            controls = self.home_configure["chrome"]
            settings, mission, level, mode, actions, timer, start = controls
            settings.emit("clicked")
            level.set_selected(0)
            mode.set_selected(0)
            actions.set_selected(0)
            timer.set_selected(0)
            choices = mission.get_model()
            idx = next(i for i in range(choices.get_n_items())
                       if choices.get_string(i) == "Найти репозиторий и открыть учебную копию")
            mission.set_selected(idx)
            start.emit("clicked")
            for _ in range(50):
                if self.lab and self.lab.active:
                    break
                yield
            lesson = self.lab
            assert lesson.active and lesson.state["task"]["keys"] == "Ctrl+L", lesson.state
            self.focus_window()
            lesson.desktop.grab_focus()
            # The aid can be hidden by profile, but its actual rendered labels,
            # targets and prompt are built regardless; show it for evidence.
            if not lesson.guide.show_keys:
                lesson.aids["keys"].set_active(True)
            keys = lesson.guide.board.cells
            token = tuple(t for t in lesson.gesture_tokens if t != "+")
            assert token == ("Ctrl", "L"), token
            assert keys["L"][0].get_label().lower() == "д"
            assert "target" in keys["L"][0].get_css_classes()
            assert any("target" in keys[side][0].get_css_classes() for side in ("LCtrl", "RCtrl"))
            self.capture_widget_png(out / "live-ru-physical-ctrl-l.png")
            yield from self.chord("Ctrl+L")
            assert lesson.state["solved"] == 1
            subprocess.run(["setxkbmap", "-layout", "us"], check=True)
            for _ in range(10):
                yield
            yield from self.chord("Esc")
            assert self.layout == "EN"
            assert keys["L"][0].get_label().lower() == "l"
            self.capture_widget_png(out / "live-en-physical-ctrl-l.png")
        print("PASS GTK: standard Ctrl+L and physical L/Д teach the selected layout", flush=True)


if __name__ == "__main__":
    app = GuideLayoutQA()
    status = app.run([])
    raise SystemExit(1 if app.smoke_failure else status)
