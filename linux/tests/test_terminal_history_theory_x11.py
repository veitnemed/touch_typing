"""Replay the reported scenario inside the native ordinary GTK lesson."""
import os
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out

BASELINE = os.environ.get("TOUCH_TYPING_QA_HISTORY_BASELINE") == "1"


def active(lesson):
    return next(w for w in lesson.state["scene"]["windows"] if w["active"])


class TerminalHistoryWindow(CourseCards):
    def exercise(self):
        self.navigate("home")
        for _ in range(25): yield
        self.focus_window()
        lesson = yield from self.ordinary_card("terminal")
        assert lesson and lesson.active and lesson.state["mission"] == "terminal-explore"
        self.focus_window()
        lesson.desktop.grab_focus()
        assert lesson.state["task"]["text"] == "cd /home/student/project"
        hint = tuple(lesson.gesture_tokens)
        assert ("фрагмент" in hint) is not BASELINE, hint
        self.capture_widget_png(out / "01-start.png")
        if not BASELINE:
            assert lesson.theory_button.get_mapped() and not lesson.details.get_reveal_child()
            yield from self.chord("F2")
            assert lesson.details.get_reveal_child() and "↑ возвращает" in lesson.details_text.get_label()
            assert self.window.get_focus() is lesson.desktop
            self.capture_widget_png(out / "02-theory.png")
            yield from self.video_hold("Теория: стрелка только возвращает команду; Enter запускает")
            yield from self.chord("F2")
            assert not lesson.details.get_reveal_child() and self.window.get_focus() is lesson.desktop
        yield from self.chord("Ctrl+R")
        assert active(lesson)["focus"] == "reverse-search"
        candidate = lesson.state["scene"]["terminalInput"]
        yield from self.chord("Enter")
        assert lesson.state["solved"] == 0
        scene = lesson.state["scene"]
        actual = scene["terminal"]["lastCommand"]
        if BASELINE:
            assert (candidate, actual, lesson.state["errors"]) == ("wc -l tags.txt", "wc -l tags.txt", 1)
            yield from self.chord("Ctrl+R")
        else:
            assert (candidate, actual, lesson.state["errors"], active(lesson)["focus"]) == ("", "", 0, "reverse-search")
        self.capture_widget_png(out / "03-empty-query.png")
        yield from self.typed({"id":"type-code", "text":"student/project"})
        assert lesson.state["scene"]["terminalInput"] == "cd /home/student/project"
        self.capture_widget_png(out / "04-match.png")
        yield from self.video_hold("Введён фрагмент, найдена именно команда перехода в проект")
        yield from self.chord("Enter")
        assert lesson.state["solved"] == 1 and lesson.state["scene"]["terminal"]["cwd"] == "/home/student/project"
        self.capture_widget_png(out / "05-credited.png")
        if not BASELINE:
            while lesson.state["task"]["id"] != "terminal-history-up":
                yield from self.advance_card(lesson)
            label = lesson.gesture.get_first_child()
            assert label.get_label() == "↑" and label.has_css_class("reference-keycap")
            self.capture_widget_png(out / "06-physical-arrow.png")
            yield from self.video_hold("Нажмите стрелку ↑, а не буквы U и P")
            before = lesson.state["solved"]
            last = lesson.state["scene"]["terminal"]["lastCommand"]
            yield from self.chord("Up")
            assert lesson.state["solved"] == before + 1 and lesson.state["scene"]["terminalInput"] == "pwd"
            assert lesson.state["scene"]["terminal"]["lastCommand"] == last
            self.capture_widget_png(out / "07-arrow-not-executed.png")
        print(f"PASS real GTK {('released .7' if BASELINE else 'fixed')} ordinary Terminal:"
              f" empty Ctrl+R+Enter candidate={candidate!r}, executed={actual!r};"
              " fragment accepted with real cwd; arrows display/behavior separately verified in fixed build.", flush=True)


if __name__ == "__main__":
    app = TerminalHistoryWindow()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
