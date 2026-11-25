"""Open the normal GTK settings, then deliver actual X11 keyboard events to the unmodified lesson view."""
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out
from test_project_file_practice import active, disk
from test_workday_source_revision import NOTE


class RevisionWindow(CourseCards):
    def exercise(self):
        self.navigate("home")
        for _ in range(28): yield
        self.focus_window()
        settings, picker, levels, mode, count, timer, start = self.home_configure["workday"]
        settings.grab_focus(); yield from self.chord("Space")
        levels.set_selected(1); mode.set_selected(0); count.set_selected(0); timer.set_selected(0)
        model = picker.get_model()
        picker.set_selected(next(i for i in range(model.get_n_items())
                                  if model.get_string(i) == "Исправить только устаревшую ссылку в передаче смены"))
        start.grab_focus(); yield from self.chord("Enter")
        for _ in range(100):
            if self.lab and self.lab.active and not self.lab.state["paused"]: break
            yield
        lab = self.lab
        assert lab.active and lab.state["mission"] == "workday-repair-source-link"
        lab.desktop.grab_focus()
        original = disk(lab.state)[NOTE]; old = original.splitlines()[-1]
        assert active(lab.state)["url"] == NOTE
        self.capture_widget_png(out/"00-saved-note-before.png")
        target = old.rsplit("/",1)[0] + "#procedure"
        while not lab.state["finished"]:
            s=lab.state; w=active(s); action=s["task"]["id"]
            if action == "link-next":
                yield from self.typed({"id":"type-code","text":old.split("/")[-2]})
                assert len(active(lab.state)["entries"]) == 3
                self.capture_widget_png(out/"01-chrome-history.png")
            if action == "paste" and w["app"] == "code":
                assert w["body"][min(w["anchor"],w["cursor"]):max(w["anchor"],w["cursor"])] == old
                self.capture_widget_png(out/"02-old-url-selected.png")
            if action == "select-line":
                assert disk(s)[NOTE] == original[:-len(old)] + target
                self.capture_widget_png(out/"03-saved-note.png")
            yield from self.advance_card(lab)
        assert active(lab.state)["url"] == target
        assert disk(lab.state)[NOTE] == original[:-len(old)] + target
        print("PASS GTK Workday: actual filtered history, retained selection, exact one-link edit, persisted note and reader tab",target,lab.state["solved"], flush=True)


if __name__ == "__main__":
    app=RevisionWindow()
    status=app.run([])
    raise SystemExit(1 if app.smoke_failure else status)
