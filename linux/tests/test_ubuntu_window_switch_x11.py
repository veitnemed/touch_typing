"""Actual ordinary GTK course with raw X11 input; optional stock XFWM host.

The default runner uses stock Xvfb without a window manager. For the actual
host-interception branch, use a disposable Xvfb session and session bus with
TT_ALT_HOST_WM=1, xfwm4, and xmessage. Never run this fixture on the desktop.
"""
import os
import subprocess
from pathlib import Path

from test_preview2_native import QA, out


class WindowSwitch(QA):
    def __init__(self):
        super().__init__()
        self.wm = None
        self.peer = None

    def desktop_location(self):
        s = self.lab.state
        active = next(w for w in s["scene"]["windows"] if w["active"])
        return s["solved"], s["scenarioCompletedGoals"], s["task"]["id"], active["app"]

    def focus_trainer(self):
        self.window.present()
        self.focus_window()

    def exercise(self):
        self.navigate("home")
        for _ in range(40):
            yield
        self.focus_window()
        self.home_cards["ubuntu"].grab_focus()
        yield from self.chord("Enter")
        for _ in range(60):
            if self.lab and self.lab.active and not self.lab.state["paused"]:
                break
            yield
        lab = self.lab
        assert lab and lab.active and lab.window_only_keys and lab.state["mission"] == "ubuntu-start"
        assert lab.state["routeKind"] == "course"
        while lab.state["task"]["id"] != "switch-next":
            yield from self.advance_goal(lab)
        before = self.desktop_location()
        assert before == (4, 4, "switch-next", "code"), before
        assert lab.state["task"]["keys"] == "Alt+Tab", lab.state["task"]
        if os.environ.get("TT_ALT_HOST_WM") == "1":
            self.wm = subprocess.Popen(["xfwm4", "--replace", "--compositor=off"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(120):
                yield
            assert self.wm.poll() is None, "Cannot launch stock Xfwm"
            self.peer = subprocess.Popen(["xmessage", "-geometry", "260x100+750+40", "External X11 window"])
            for _ in range(60):
                yield
            self.focus_trainer()
            for _ in range(70):
                yield
            if lab.state["paused"]:
                yield from self.chord("Space")
            assert self.window.is_active() and not lab.state["paused"], (self.window.is_active(), lab.state["paused"])
        keys_before = dict(self.received)
        yield from self.chord("Alt+Tab")
        for _ in range(100):
            yield
        events = {mark: count - keys_before.get(mark, 0) for mark, count in self.received.items()
                  if count > keys_before.get(mark, 0)}
        print("Raw Gtk events:", events, "trainer active=", self.window.is_active(),
              "scene/task=", self.desktop_location(), flush=True)
        if self.wm:
            assert events.get((64, True), 0) == 1
            assert events.get((23, True), 0) == 0 and not self.window.is_active(), events
            assert lab.state["paused"] and self.desktop_location() == before
            self.focus_trainer()
            for _ in range(70):
                yield
            assert lab.state["paused"] and self.window.is_active()
            self.capture_widget_png(out / "alt-01-host-owns-tab.png")
            assert lab.pause_switch_notice.startswith("Тренажёр потерял фокус"), lab.pause_switch_notice
            alt = next(b for b in lab.pause_actions if b.get_label() == "Учебное переключение")
            assert self.window.get_focus() == lab.pause_actions[0]
            yield from self.chord("Enter")
            assert self.window.get_focus() == lab.pause_actions[0] and self.desktop_location() == before
            yield from self.chord("Tab")
            assert self.window.get_focus() == alt, type(self.window.get_focus())
            self.capture_widget_png(out / "alt-02-separate-training-action.png")
            yield from self.video_hold("Системный Alt+Tab ушёл X11, а учебная кнопка не подтверждает сочетание")
            yield from self.chord("Enter")
            assert lab.state["paused"] and self.desktop_location()[:2] == (4, 5)
            assert self.desktop_location()[3] == "chrome"
            yield from self.chord("Space")
            assert not lab.state["paused"]
            assert lab.state["task"]["id"] != "switch-next" and self.window.get_focus() == lab.desktop
            result = lab.host.call("lab-result")
            assert result["solved"] == 4 and result["simulatedWindowSwitches"] == 1
            assert not any(x["task"] == "switch-next" for x in result["series"])
            self.capture_widget_png(out / "alt-03-lesson-continues.png")
            yield from self.video_hold("Учебное окно изменилось. Клавиши не засчитаны, занятие продолжено")
        else:
            assert all(events.get(item) == 1 for item in ((64, True), (23, True), (23, False), (64, False))), events
            assert self.desktop_location()[:2] == (5, 5) and self.desktop_location()[3] == "chrome"
            assert lab.host.call("lab-result")["series"][-1]["task"] == "switch-next"
            self.capture_widget_png(out / "alt-04-exact-physical-tab.png")
        print("PASS native ordinary Live→Ubuntu", "host reservation" if self.wm else "exact delivered Alt+Tab", flush=True)

    def do_shutdown(self):
        super().do_shutdown()
        for process in (self.peer, self.wm):
            if process and process.poll() is None:
                process.terminate()
                process.wait(timeout=3)


if __name__ == "__main__":
    app = WindowSwitch()
    code = app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
