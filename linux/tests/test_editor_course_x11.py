"""Exercise the same Live → VS Code card and Continue entry as the learner."""
import json
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import checkpoint, first_difference
from test_project_file_practice import active, disk
from test_editor_guide_note import NOTE, source_fragments
from test_preview2_native import QA, CaptureConsent, out
from touch_typing import LabPage


class CourseWindowQA(QA):
    def exercise(self):
        def acquired(page):
            # X11 cannot offer Wayland shortcut-inhibition consent. Real
            # XTest/GDK key events still reach the normal application handling.
            page.release_capture()
            page.waiting_capture = False
            page.surface = CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()

        self.navigate("home")
        for _ in range(30):
            yield
        self.focus_window()
        with patch.object(LabPage, "acquire", acquired):
            self.home_cards["code"].grab_focus()
            yield from self.chord("Enter")
            for _ in range(55):
                if self.lab and self.lab.active:
                    break
                yield
            lab = self.lab
            assert lab and lab.active and lab.state["editorCourse"] and not lab.prep.get_visible()
            assert (lab.state["mission"], lab.state["solved"], lab.state["timeLimit"], lab.state["actionLimit"]) == (
                "code-guide", 0, 0, 40)
            count = lab.state["scenarioLength"]
            self.capture_widget_png(out/"course-01-original.png")
            yield from self.video_hold("VS Code: прежний курс при обычном входе")
            expected_note = "".join(source_fragments(lab.state))
            while lab.state["mission"] == "code-guide":
                yield from self.advance_goal(lab)
                if lab.state["solved"] == 27:
                    assert disk(lab.state)[NOTE] == active(lab.state)["body"] == expected_note
                    self.capture_widget_png(out/"course-01b-three-source-note.png")
                    yield from self.video_hold("Одна сохранённая заметка использует все три исходных фрагмента")
            assert lab.active and lab.state["mission"] == "code-notebook-read" and lab.state["solved"] == count == 33
            assert abs(lab.lesson_meter.get_fraction() - 33 / 40) < .001
            yield from self.advance_goal(lab)  # Ctrl+P from the ordinary editor.
            yield from self.typed({"id": "type-code", "text": lab.state["task"]["text"]})
            yield from self.advance_goal(lab)  # open the named notebook
            for key in ("Enter", "End", "Shift+Left"):
                yield from self.chord(key)
            notebook = active(lab.state)["notebook"]
            assert (lab.state["solved"], notebook["selected"], notebook["editing"]) == (35, 0, True)
            assert notebook["anchor"] == notebook["caret"] + 1 and notebook["caret"] > 0
            self.capture_widget_png(out/"course-02-notebook-at-35.png")
            yield from self.video_hold("Блокнот внутри курса после 33 действий редактора")
            expected = checkpoint(lab.state)
            self.return_live_catalog("code")
            for _ in range(20):
                yield
            assert "code" in self.home_resume and self.window.get_focus() == self.home_cards["code"]
            # Reopen the packaged shared engine from the persisted practice profile.
            self.engine.close()
            self.engine = Engine(verify=True)
            self.library = self.engine.call("configure", platform="linux", layout="EN")
            self.return_live_catalog("code")
            resume = self.home_resume["code"]
            resume.popup()
            for _ in range(14):
                yield
            resume.get_popover().get_child().get_first_child().emit("clicked")
            for _ in range(50):
                if self.lab and self.lab.active:
                    break
                yield
            lab = self.lab
            assert lab.active and lab.state["editorCourse"]
            actual = checkpoint(lab.state)
            if actual != expected:
                for name, value in (("saved", expected), ("resumed", actual)):
                    (out/f"course-checkpoint-{name}.json").write_text(json.dumps(value, ensure_ascii=False, indent=2))
                raise AssertionError(first_difference(expected, actual))
            assert abs(lab.lesson_meter.get_fraction() - 35 / 40) < .001
            yield from self.chord("Esc")
            assert lab.state["solved"] == 35 and not active(lab.state)["notebook"]["editing"]
            while not lab.state["limitDecisionPending"]:
                yield from self.advance_goal(lab)
            assert lab.active and lab.state["solved"] == 40 and lab.state["limitReached"] == "actions"
            self.capture_widget_png(out/"course-03-action-limit.png")
            yield from self.video_hold("Общий лимит 40, можно доработать текущий эпизод")
            _, extend = lab.limit_actions
            extend.grab_focus()
            yield from self.chord("Enter")
            for _ in range(30):
                yield
            assert lab.active and lab.state["finishingMission"] and not lab.state["paused"]
            while lab.active:
                task = lab.state["task"]
                if task["text"] and task["id"] in ("quick-choose", "save"):
                    yield from self.typed({"id": "type-code", "text": task["text"]})
                yield from self.advance_goal(lab)
            assert lab.state["finished"] and lab.state["solved"] == 42
            self.capture_widget_png(out/"course-04-completed.png")
            yield from self.video_hold("Результат полноценного курса")
            print("PASS GTK ordinary VS Code card: 33 editor steps → notebook → resume from 35 → 40 → 42", flush=True)


if __name__ == "__main__":
    app = CourseWindowQA()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
