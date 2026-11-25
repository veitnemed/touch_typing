"""The ordinary GTK card, actual input, a live log file and exact recovery."""
import json
from platform_linux import Engine
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out
from test_terminal_practical import log, terminal


class PracticalJournal(CourseCards):
    def run_command(self, value):
        yield from self.typed({"id": "type-code", "text": value})
        yield from self.chord("Enter")

    def rendered_frame(self, name):
        for _ in range(12):
            yield
        self.capture_widget_png(out / name)

    def exercise(self):
        self.navigate("home")
        for _ in range(24):
            yield
        self.focus_window()
        lesson = yield from self.ordinary_card("terminal")
        assert lesson and lesson.window_only_keys and lesson.state["mission"] == "terminal-practical-incident"
        assert lesson.state["actionLimit"] == 0 and lesson.state["scenarioLength"] == 4
        assert lesson.gesture_tokens == ("grep ERROR app.log",), lesson.gesture_tokens
        initial = log(lesson.state)
        yield from self.rendered_frame("terminal-practical-01-start.png")
        assert lesson.theory_rail.get_mapped() and lesson.theory_button.get_mapped()
        assert lesson.stage.get_allocation().x + lesson.stage.get_width() <= lesson.theory_rail.get_allocation().x
        assert lesson.theory_shell.get_label().endswith("Bash") and "grep" in lesson.theory_why.get_label()
        yield from self.typed({"id": "type-code", "text": "grep"})
        yield from self.chord("F2")
        assert not lesson.theory_rail.get_mapped() and self.window.get_focus() is lesson.desktop
        assert lesson.state["scene"]["terminalInput"] == "grep" and lesson.state["solved"] == 0
        yield from self.run_command(" ERROR app.log")
        assert lesson.state["solved"] == 1 and log(lesson.state) == initial
        assert not lesson.theory_rail.get_mapped()  # explicit collapse survives the next command
        yield from self.chord("F2")
        assert lesson.theory_rail.get_mapped() and "-f" in lesson.theory_why.get_label()
        yield from self.rendered_frame("terminal-practical-follow-theory.png")
        old_id = terminal(lesson.state)["output"][-1].split()[1]
        yield from self.run_command("tail -f app.log")
        assert lesson.state["solved"] == 2 and terminal(lesson.state)["followingLog"]
        assert not lesson.theory_rail.get_mapped() and not lesson.theory_button.get_mapped()
        for _ in range(300):
            if log(lesson.state) != initial:
                break
            yield
        assert log(lesson.state) != initial
        yield from self.chord("F8")
        expected = log(lesson.state)
        assert lesson.state["paused"] and terminal(lesson.state)["followingLog"]
        self.return_live_catalog("terminal")
        for _ in range(22):
            yield
        assert "terminal" in self.home_resume
        self.engine.close()
        self.engine = Engine(verify=True)
        self.library = self.engine.call("configure", platform="linux", layout="EN")
        self.return_live_catalog("terminal")
        lesson = yield from self.ordinary_card("terminal")  # the card resumes its compatible draft
        assert lesson.state["solved"] == 2 and terminal(lesson.state)["followingLog"]
        assert log(lesson.state).startswith(expected)
        for _ in range(300):
            if terminal(lesson.state)["logIncidentArrived"]:
                break
            yield
        assert terminal(lesson.state)["logIncidentArrived"]
        fresh = next(line for line in log(lesson.state).splitlines()[len(initial.splitlines()):] if line.startswith("ERROR "))
        yield from self.rendered_frame("terminal-practical-02-live-log.png")
        yield from self.video_hold("После восстановления в журнал поступила новая ошибка")
        yield from self.chord("Ctrl+C")
        assert lesson.state["solved"] == 3 and not terminal(lesson.state)["followingLog"]
        assert terminal(lesson.state)["output"][-1] == "^C" and lesson.gesture_tokens == ("grep [ID] app.log",)
        yield from self.rendered_frame("terminal-practical-03-interrupted.png")
        yield from self.video_hold("Ctrl+C остановил просмотр, приглашение вернулось")
        yield from self.run_command(f"grep {old_id} app.log")
        assert lesson.state["solved"] == 3 and "новой строки ERROR" in lesson.state["status"]
        new_id = fresh.split()[1]
        assert lesson.theory_rail.get_mapped() and new_id not in lesson.theory_why.get_label() + lesson.theory_observe.get_label()
        yield from self.run_command(f"grep {new_id} app.log")
        assert lesson.state["solved"] == 4 and lesson.state["finished"]
        assert terminal(lesson.state)["output"][-2:] == [f"INFO {new_id} started", fresh]
        yield from self.rendered_frame("terminal-practical-04-result.png")
        (out / "terminal-practical-witness.json").write_text(json.dumps(dict(
            old_id=old_id, new_id=new_id, result=terminal(lesson.state)["output"][-2:], solved=4,
            real_entry="Live → Terminal", backend="GTK X11"), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        yield from self.video_hold("Разобран реальный ID новой ошибки, без серии пустых команд")
        print(f"PASS real GTK ordinary Terminal: native keys, live append, restart, Ctrl+C, {new_id}, 4/4", flush=True)


if __name__ == "__main__":
    app = PracticalJournal()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
