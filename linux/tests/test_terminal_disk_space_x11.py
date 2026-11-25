"""Open the published-route UI controls, diagnose the correct volume, read the result."""
import json
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out
from test_terminal_practical import terminal
from test_terminal_disk_space import visible_capacity


class CapacityWindow(CourseCards):
    def shell_line(self, line):
        yield from self.typed({"id":"type-code", "text":line})
        yield from self.chord("Enter")

    def exercise(self):
        seen = []
        for profile, repeat in ((profile, repeat) for profile in ("ubuntu", "windows") for repeat in (False, True)):
            label = profile + ("-repeat" if repeat else "")
            self.navigate("home")
            for _ in range(24):
                yield
            self.focus_window()
            self.live_os_choices[profile].grab_focus()
            yield from self.chord("Space")
            for _ in range(16):
                yield
            settings, chooser, level, mode, actions, timer, start = self.home_configure["terminal"]
            settings.grab_focus()
            yield from self.chord("Space")
            titles = chooser.get_model()
            title = "Проверить место без подсказки" if repeat else "Не сохранился архив: проверить место"
            chooser.set_selected(next(i for i in range(titles.get_n_items()) if titles.get_string(i) == title))
            start.grab_focus()
            yield from self.chord("Enter")
            for _ in range(90):
                if self.lab and self.lab.active and not self.lab.state["paused"]:
                    break
                yield
            lab = self.lab
            ps = profile == "windows"
            suffix = "powershell" if ps else "bash"
            assert lab.state["mission"] == "terminal-disk-" + ("repeat-" if repeat else "space-") + suffix
            assert lab.state["scenarioLength"] == 2
            assert lab.theory_rail.get_mapped() != repeat
            if repeat:
                assert not lab.state["showGuide"]
            else:
                assert ("Free (GB)" if ps else "Avail") in lab.theory_observe.get_label()
            current = terminal(lab.state)
            expected = lab.state["task"]["text"]
            self.capture_widget_png(out / (label+"-01-capacity-theory.png"))
            yield from self.shell_line("Get-PSDrive C" if ps else "df -h .")
            assert lab.state["solved"] == 0 and "другой диск" in lab.state["status"]
            yield from self.shell_line(expected)
            assert lab.state["solved"] == 1 and not lab.theory_rail.get_mapped()
            decision = terminal(lab.state)
            needed, free = visible_capacity(decision["output"], ps)
            if repeat:
                assert needed == 2048 and free in (512, 3072)
            answer = "2" if free >= needed else "1"
            self.capture_widget_png(out / (label+"-02-decision.png"))
            yield from self.chord("1" if answer=="2" else "2")
            assert lab.state["solved"] == 1
            yield from self.chord(answer)
            assert lab.state["solved"] == 2 and not lab.active and lab.result
            verdict = self.call("lab-result")["diskCapacityConclusion"]
            assert (("Места хватает" if answer=="2" else "Места не хватает") in verdict
                    and decision["files"] == current["files"] == terminal(lab.state)["files"])
            assert decision["lastCommand"]==terminal(lab.state)["lastCommand"], "The quiz choice is not a shell command"
            for _ in range(6):
                yield
            self.capture_widget_png(out / (label+"-03-conclusion.png"))
            seen.append(dict(profile=profile, repeat=repeat, command=expected, result=verdict))
            self.return_live_catalog("terminal")
            for _ in range(22):
                yield
        (out/"capacity-witness.json").write_text(json.dumps(seen, indent=2, ensure_ascii=False), encoding="utf-8")
        print("PASS real GTK Live → Terminal → Настроить → Не сохранился архив: both shell profiles", flush=True)


if __name__ == "__main__":
    app = CapacityWindow()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
