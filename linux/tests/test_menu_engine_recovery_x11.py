"""Real GTK menu controls recover after loss of the local engine while idle.

The fault is injected only into the owned QA child; it reproduces an
unresponsive launch, not a claim about why a particular user session failed.
"""
from test_preview2_native import QA


class Recovery(QA):
    def end_engine(self):
        previous = self.engine.process
        previous.kill()
        assert previous.wait(timeout=3) != 0
        return previous

    def exercise(self):
        self.focus_window()
        self.navigate("typing")
        for _ in range(12):
            yield
        yield from self.chord("F5")
        assert self.workout == "typing"
        yield from self.chord("A")
        yield from self.chord("Esc")
        assert self.result_kind == "typing"
        self.navigation["typing"].emit("clicked")
        assert not self.result_kind and not self.workout
        dead = self.end_engine()
        yield from self.chord("F5")
        assert self.workout == "typing", self.banner.get_label()
        assert self.engine.process is not dead
        yield from self.chord("Esc")
        self.navigation["home"].emit("clicked")
        assert not self.workout
        dead = self.end_engine()
        self.home_cards["chrome"].grab_focus()
        yield from self.chord("Enter")
        for _ in range(50):
            if self.lab and self.lab.active:
                break
            yield
        assert self.lab and self.lab.active, self.banner.get_label()
        assert self.engine.process is not dead
        assert self.lab.state["app"] == "chrome"
        self.return_live_catalog("chrome")
        assert self.lab is None and "chrome" in self.home_cards
        self.home_cards["chrome"].emit("clicked")
        for _ in range(50):
            if self.lab and self.lab.active:
                break
            yield
        assert self.lab and self.lab.active, self.banner.get_label()
        print("PASS: GTK menu starts typing and Live after an engine exit; return and relaunch work.")


if __name__ == "__main__":
    app = Recovery()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
