"""Keep Bash history as separately selected native GTK practice, not the default course."""
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
        if BASELINE:
            lesson = yield from self.ordinary_card("terminal")
        else:
            settings, chooser, level, mode, actions, timer, start = self.home_configure["terminal"]
            settings.grab_focus()
            yield from self.chord("Space")
            titles = chooser.get_model()
            chooser.set_selected(next(i for i in range(titles.get_n_items())
                                     if titles.get_string(i) == "Разобраться в незнакомом проекте"))
            start.grab_focus()
            yield from self.chord("Enter")
            for _ in range(90):
                if self.lab and self.lab.active and not self.lab.state["paused"]:
                    break
                yield
            lesson = self.lab
        assert lesson and lesson.active and lesson.state["mission"] == "terminal-explore"
        self.focus_window()
        lesson.desktop.grab_focus()
        assert lesson.state["task"]["text"] == "cd /home/student/project"
        hint = tuple(lesson.gesture_tokens)
        assert ("фрагмент" not in hint) and "cd /home/student/project" in hint, hint
        self.capture_widget_png(out / "01-start.png")
        if not BASELINE:
            assert lesson.theory_button.get_mapped() and lesson.theory_rail.get_mapped()
            assert "Перейти к рабочим файлам" in lesson.theory_title.get_label()
            assert "Ctrl+R" in lesson.state["task"]["hint"]
            yield from self.chord("F2")
            assert not lesson.theory_rail.get_mapped()
            assert self.window.get_focus() is lesson.desktop
            self.capture_widget_png(out / "02-theory.png")
            yield from self.video_hold("Скрытие справки не меняет содержимое команды")
            yield from self.chord("F2")
            assert lesson.theory_rail.get_mapped() and self.window.get_focus() is lesson.desktop
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
            assert not lesson.theory_button.get_mapped() and not lesson.theory_rail.get_mapped()
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
        print(f"PASS real GTK {('released .7' if BASELINE else 'fixed')} Terminal history:"
              f" empty Ctrl+R+Enter candidate={candidate!r}, executed={actual!r};"
              " fragment accepted with real cwd; arrows display/behavior separately verified in fixed build.", flush=True)


if __name__ == "__main__":
    app = TerminalHistoryWindow()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
