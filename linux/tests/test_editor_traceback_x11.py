"""Open the shipped Live VS Code card and navigate from a displayed traceback."""
import re
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out


class NativeEditorTraceback(CourseCards):
    def exercise(self):
        self.navigate("home")
        for _ in range(28): yield
        self.focus_window()
        settings, picker, level, mode, actions, timer, start = self.home_configure["code"]
        settings.grab_focus(); yield from self.chord("Space")
        level.set_selected(1); mode.set_selected(1); actions.set_selected(0); timer.set_selected(0)
        titles = picker.get_model()
        picker.set_selected(next(i for i in range(titles.get_n_items())
                                 if titles.get_string(i).startswith("Найти строку ошибки")))
        start.grab_focus(); yield from self.chord("Enter")
        for _ in range(80):
            if self.lab and self.lab.active and not self.lab.state["paused"]: break
            yield
        lesson = self.lab
        assert lesson.active and lesson.state["mission"] == "code-traceback-line"
        active = lambda: next(w for w in lesson.state["scene"]["windows"] if w["active"])
        lesson.desktop.grab_focus()
        w = active()
        assert w["panel"] and w["focus"] == "body"
        console = "\n".join(lesson.state["scene"]["terminal"]["output"])
        file, row = re.search(r"Traceback: ([a-z_]+\.py):(\d+)", console).groups()
        setting, limit = re.search(r"ValueError: (\w+) должен быть (\d+)", console).groups()
        path = "/home/student/project/" + file
        original = {item["path"]: item["content"] for item in lesson.state["scene"]["terminal"]["files"]}
        self.capture_widget_png(out / "01-visible-traceback.png")
        yield from self.video_hold("VS Code: номер строки читается в исходном выводе")
        yield from self.chord("Ctrl+G")
        assert active()["focus"] == "goto-line" and lesson.state["solved"] == 1
        yield from self.typed({"id": "type-code", "text": row})
        yield from self.chord("Enter")
        assert active()["body"][:active()["bodyCursor"]].count("\n") == int(row)-1 and lesson.state["solved"] == 2
        self.capture_widget_png(out / "02-exact-source-line.png")
        yield from self.video_hold("Вводим номер, каретка попадает в строку ошибки")
        for cut in ("End", "Backspace", limit):
            # Visible digits reach the focused code editor as physical key events.
            yield from self.chord(cut)
        yield from self.chord("Ctrl+S")
        saved = {item["path"]: item["content"] for item in lesson.state["scene"]["terminal"]["files"]}
        assert saved == original | {path: original[path].replace(setting + " = 0", setting + " = " + limit)}
        assert lesson.state["solved"] == 3
        while lesson.active:
            yield from self.advance_card(lesson)
        assert lesson.state["finished"] and lesson.state["solved"] == 5
        assert "OK: " + setting + " = " + limit in "\n".join(lesson.state["scene"]["terminal"]["output"])
        self.capture_widget_png(out / "03-real-check-result.png")
        yield from self.video_hold("Тест проверил сохранённую правку, а не нажатие само по себе")
        print(f"PASS GTK real keys: {file}:{row} -> {setting}={limit}; exactly one source change and actual test", flush=True)


if __name__ == "__main__":
    app = NativeEditorTraceback()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
