"""Verify the reported Chrome workflow through the actual GTK entry."""
import os
import subprocess
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out

BASELINE = os.environ.get("TOUCH_TYPING_QA_HISTORY_BASELINE") == "1"


def active(lesson):
    return next(w for w in lesson.state["scene"]["windows"] if w["active"])


class ChromeHistoryQA(CourseCards):
    def exercise(self):
        self.navigate("home")
        for _ in range(28): yield
        self.focus_window()
        settings, picker, level, mode, actions, timer, start = self.home_configure["chrome"]
        settings.grab_focus(); yield from self.chord("Space")
        level.set_selected(2); mode.set_selected(0); actions.set_selected(0); timer.set_selected(0)
        titles = picker.get_model()
        picker.set_selected(next(i for i in range(titles.get_n_items())
                                 if titles.get_string(i).startswith("Восстановить рабочий источник")))
        start.grab_focus(); yield from self.chord("Enter")
        for _ in range(60):
            if self.lab and self.lab.active and not self.lab.state["paused"]: break
            yield
        lesson = self.lab
        assert lesson.active and lesson.state["mission"] == "chrome-restore-source"
        lesson.desktop.grab_focus()
        yield from self.chord("Ctrl+L")
        assert active(lesson)["focus"] == "url"
        assert lesson.state["task"]["text"] == "handbook backup", lesson.state["task"]
        yield from self.typed({"id":"type-code","text":"handbook backup"})
        assert active(lesson)["input"] == "handbook backup"
        yield from self.chord("Enter")
        assert lesson.state["solved"] == 2
        self.capture_widget_png(out / "01-google-input.png")
        while lesson.state["task"]["id"] != "history":
            yield from self.advance_card(lesson)
        yield from self.chord("Ctrl+H")
        w = active(lesson)
        assert w["url"] == "chrome://history"
        before = lesson.state["solved"]
        if BASELINE:
            assert w["focus"] == "body"
            yield from self.typed({"id":"type-code","text":"backup"})
            assert active(lesson)["input"] == "" and active(lesson)["entries"] == w["entries"]
            subprocess.run(["setxkbmap", "-layout", "ru"], check=True)
            for _ in range(10): yield
            yield from self.chord("H")
            assert active(lesson)["input"] == "" and lesson.state["solved"] == before
            subprocess.run(["setxkbmap", "-layout", "us"], check=True)
            self.capture_widget_png(out / "02-old-history.png")
            prefix = "old .8: Google input works, History focus=body rejects letters in EN and RU"
        else:
            assert w["focus"] == "history" and w["item"] == -1
            self.capture_widget_png(out / "02-history-focused.png")
            subprocess.run(["setxkbmap", "-layout", "ru"], check=True)
            for _ in range(12): yield
            # Actual Cyrillic text in an unrestricted search field, not a
            # layout-substitution shortcut or a protocol-injected character.
            for code in ("Shift+H", "T", "P", "T", "H", "D", "Y", "F", "Z"):
                yield from self.chord(code)
            ru = active(lesson)
            assert ru["historySearch"] == "Резервная" and len(ru["entries"]) == 1, (ru["input"], ru["entries"])
            self.capture_widget_png(out / "02b-history-ru.png")
            subprocess.run(["setxkbmap", "-layout", "us"], check=True)
            for _ in range(12): yield
            yield from self.chord("Ctrl+F")
            yield from self.typed({"id":"type-code","text":"Handbook Backup"})
            w = active(lesson)
            assert w["historySearch"] == "Handbook Backup" and w["item"] == -1
            assert w["entries"] and all("/handbook/backup" in item["path"] or item["path"].startswith("https://www.google.com/search?q=handbook") for item in w["entries"])
            assert any(item["path"].endswith("#procedure") for item in w["entries"])
            self.capture_widget_png(out / "03-history-filtered.png")
            pre = lesson.state["solved"]
            yield from self.chord("Enter")
            assert lesson.state["solved"] == pre and active(lesson)["focus"] == "history"
            prefix = "new: actual History input case-insensitively filtered reachable entries"
        while lesson.state["task"]["id"] != "url":
            yield from self.advance_card(lesson)
        assert active(lesson)["url"].endswith("/handbook/backup#procedure") and lesson.state["solved"] > before
        self.capture_widget_png(out / "04-restored-section.png")
        print("PASS real GTK clone: " + prefix + "; exact useful instruction reopened.", flush=True)


if __name__ == "__main__":
    app = ChromeHistoryQA()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
