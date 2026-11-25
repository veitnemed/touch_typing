"""Use actual GTK key delivery to distinguish a current source from a plausible archive."""
from test_chrome_tab_search import ARTIFACT, MISSION, documents
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out
from test_project_file_practice import active, disk
from test_notebook_workbench_native import WorkbenchWindow


class ChromeTabReviewQA(CourseCards):
    def type_visible(self, value):
        yield from self.typed({"id":"type-code","text":value})
        assert active(self.lab.state)["input"] == value, active(self.lab.state)

    def exercise(self):
        self.navigate("home")
        for _ in range(28): yield
        self.focus_window()
        settings, picker, level, mode, actions, timer, start = self.home_configure["chrome"]
        settings.grab_focus()
        yield from self.chord("Space")
        level.set_selected(1)
        mode.set_selected(0)
        actions.set_selected(0)
        timer.set_selected(0)
        titles = picker.get_model()
        picker.set_selected(next(i for i in range(titles.get_n_items())
                                 if titles.get_string(i).startswith("Найти открытый регламент")))
        start.grab_focus()
        yield from self.chord("Enter")
        for _ in range(60):
            if self.lab and self.lab.active and not self.lab.state["paused"]: break
            yield
        lesson = self.lab
        assert lesson.active and lesson.state["mission"] == MISSION, lesson.state
        current, archive, slot, code = documents(lesson.state)
        lesson.desktop.grab_focus()
        self.capture_widget_png(out / "01-request.png")
        yield from WorkbenchWindow.pointer(self, "browser-tabs:open")
        assert lesson.state["solved"] == 0 and active(lesson.state)["focus"] == "tab-search"
        yield from self.chord("Esc")
        assert lesson.state["solved"] == 0 and active(lesson.state)["focus"] == "body"
        yield from self.chord("Ctrl+Shift+A")
        assert active(lesson.state)["focus"] == "tab-search"
        assert lesson.state["solved"] == 1 and len(active(lesson.state)["tabSearch"]["rows"]) == 8
        yield from self.type_visible("archive")
        assert len(active(lesson.state)["tabSearch"]["rows"]) == 1
        yield from self.chord("Enter")
        assert active(lesson.state)["url"] == archive["url"] and lesson.state["solved"] == 1
        assert not lesson.state["finished"]
        self.capture_widget_png(out / "02-archive-rejected.png")
        yield from self.chord("Ctrl+Shift+A")
        yield from self.type_visible(current["url"].split("/")[-2])

        for _ in range(8):
            panel = active(lesson.state)["tabSearch"]
            if panel["rows"][panel["selected"]]["url"] == current["url"]:
                break
            yield from self.chord("Down")
        assert len(panel["rows"]) >= 3 and panel["rows"][panel["selected"]]["url"] == current["url"]
        self.capture_widget_png(out / "03-filtered-current-selected.png")
        yield from self.chord("Enter")
        assert lesson.state["solved"] == 2 and active(lesson.state)["url"] == current["url"]
        assert len(active(lesson.state)["tabs"]) == 8, "Selecting a result must not open another tab"
        yield from self.chord("Ctrl+F")
        yield from self.type_visible("control-code")
        yield from self.chord("Enter")
        window = active(lesson.state)
        assert window["body"][window["bodyAnchor"]:window["bodyCursor"]] == "control-code"
        self.capture_widget_png(out / "04-control-code-found.png")
        for button in ("Esc", "Ctrl+L", "Ctrl+C", f"Ctrl+{slot}", "Tab"):
            yield from self.chord(button)
        assert lesson.state["solved"] == 9 and active(lesson.state)["focus"] == "source-review"
        assert active(lesson.state)["sourceReview"]["copied"] == current["url"]
        yield from self.type_visible(code[::-1])
        yield from self.chord("Enter")
        assert lesson.state["solved"] == 9 and ARTIFACT not in disk(lesson.state)
        self.capture_widget_png(out / "05-wrong-receipt-denied.png")
        yield from self.chord("Ctrl+A")
        yield from self.type_visible(code)
        self.capture_widget_png(out / "06-source-and-code-ready.png")
        yield from self.chord("Enter")
        assert lesson.state["finished"] and lesson.state["solved"] == 10
        assert disk(lesson.state)[ARTIFACT] == f"source={current['url']}\ncontrol-code={code}\n"
        self.capture_widget_png(out / "07-result.png")
        print("PASS native Chrome: fictional archive rejected; live tab selected, find read, wrong code rejected,"
              f" Ctrl+{slot} and correct {code} produced the exact local receipt.", flush=True)


if __name__ == "__main__":
    app = ChromeTabReviewQA()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
