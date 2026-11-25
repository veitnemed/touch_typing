"""The GTK learner actually sees hint → objective only → new workspace."""
from unittest.mock import patch

from test_preview2_native import QA, CaptureConsent, out
from test_project_file_practice import active
from touch_typing import LabPage


class CoachWindowQA(QA):
    def exercise(self):
        def acquire(page):
            page.release_capture()
            page.surface = CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()

        self.navigate("home")
        for _ in range(22):
            yield
        self.focus_window()
        with patch.object(LabPage, "acquire", acquire):
            self.home_cards["chrome"].grab_focus()
            yield from self.chord("Enter")
            for _ in range(42):
                if self.lab and self.lab.active:
                    break
                yield
            lab = self.lab
            assert lab.active and lab.state["adaptiveGuide"] and lab.state["mission"] == "chrome-reader"
            yield from self.chord("Alt+D")
            assert lab.state["solved"] == 0 and lab.state["coachStage"] == "practice" and lab.gesture.get_visible()
            self.capture_widget_png(out/"coach-01-visible-hint.png")
            yield from self.chord("Ctrl+L")
            assert lab.state["solved"] == 1
            for _ in range(24):
                if lab.state["coachStage"] == "recall":
                    break
                yield from self.advance_goal(lab)
            assert lab.state["coachStage"] == "recall" and not lab.gesture.get_visible()
            assert lab.task.get_label() == "Выделите адрес страницы"
            original_context = active(lab.state)["url"]
            self.capture_widget_png(out/"coach-02-blind-recall.png")
            yield from self.chord("Ctrl+L")
            for _ in range(15):
                if lab.state["coachStage"] == "transfer":
                    break
                yield from self.advance_goal(lab)
            assert lab.state["mission"] == "chrome-docs-reference" and lab.state["coachStage"] == "transfer"
            assert active(lab.state)["url"] == "https://www.google.com/" != original_context
            assert not lab.gesture.get_visible()
            self.capture_widget_png(out/"coach-03-blind-transfer.png")
            before = lab.state["solved"]
            yield from self.chord("Ctrl+L")
            assert lab.state["solved"] == before+1 and active(lab.state)["focus"] == "url"
            print("PASS GTK coaching: visible correction → hidden recall → hidden transfer on a different page", flush=True)


if __name__ == "__main__":
    app=CoachWindowQA()
    code=app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
