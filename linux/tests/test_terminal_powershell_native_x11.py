"""Native keyboard from Live to Windows PowerShell, including a real process restart."""
import json
from pathlib import Path
import subprocess
import sys

from test_live_course_cards_x11 import CourseCards
from test_preview2_native import GdkX11, QA, out, ptr, x
from gi.repository import Gtk
from test_terminal_powershell import POWERSHELL, PS_PROMPT, sync_log
from test_terminal_practical import terminal


class PowerShellWindow(CourseCards):
    def __init__(self, resuming):
        self.resuming = resuming
        super().__init__()

    def run_command(self, value):
        yield from self.typed({"id": "type-code", "text": value})
        yield from self.chord("Enter")

    def frame(self, name):
        for _ in range(12):
            yield
        self.capture_widget_png(out / name)

    def wait_state(self, condition, frames=650):
        for _ in range(frames):
            if condition():
                return
            yield
        raise AssertionError((self.lab.state["task"], self.lab.state["status"], terminal(self.lab.state)["output"]))

    def exercise(self):
        for _ in range(24):
            yield
        self.focus_window()
        self.navigation["home"].grab_focus()
        yield from self.chord("Enter")
        for _ in range(22):
            yield
        if not self.resuming:
            self.live_os_choices["windows"].grab_focus()
            yield from self.chord("Space")
            for _ in range(22):
                yield
        assert self.lab_desktop_profile() == "windows"
        assert "PowerShell" in self.home_cards["terminal"].get_tooltip_text()
        assert ("terminal" in self.home_resume) == self.resuming
        saved = out / "powershell-checkpoint.json"
        if not self.resuming:
            yield from self.frame("powershell-01-catalog.png")
        lab = yield from self.ordinary_card("terminal")
        assert lab and lab.active and lab.window_only_keys
        assert (lab.state["mission"], lab.state["scenarioLength"], lab.state["actionLimit"], lab.state["desktopProfile"]) == (
            POWERSHELL, 4, 0, "windows")
        assert (lab.state["scene"]["desktopStyle"], terminal(lab.state)["prompt"]) == ("win10", PS_PROMPT)
        if not self.resuming:
            assert lab.gesture_tokens == ("Select-String ERROR sync.log",), lab.gesture_tokens
            assert lab.theory_rail.get_mapped() and lab.theory_shell.get_label().endswith("PowerShell")
            assert "Select-String" in lab.theory_why.get_label()
            before = sync_log(lab.state)
            yield from self.frame("powershell-02-start.png")
            yield from self.video_hold("Windows 10 → PowerShell: своё окно, приглашение и журнал")
            yield from self.run_command("Select-String ERROR sync.log")
            assert lab.state["solved"] == 1 and sync_log(lab.state) == before
            assert "-Wait" in lab.theory_why.get_label()
            first_error = terminal(lab.state)["output"][-1]
            assert first_error == "sync.log:2:" + before.splitlines()[1]
            yield from self.run_command("Get-Content sync.log")
            assert lab.state["solved"] == 1 and not terminal(lab.state)["followingLog"]
            yield from self.run_command("Get-Content sync.log -Wait")
            assert lab.state["solved"] == 2 and terminal(lab.state)["followingLog"]
            yield from self.wait_state(lambda: sync_log(lab.state) != before)
            yield from self.chord("F8")
            assert lab.state["paused"] and terminal(lab.state)["followingLog"]
            saved.write_text(json.dumps({"initial": before, "checkpoint": sync_log(lab.state),
                                         "old_id": first_error.split()[1]}, ensure_ascii=False) + "\n", encoding="utf-8")
            yield from self.frame("powershell-03-paused.png")
            yield from self.video_hold("Остановка времени занятия не стирает живой журнал")
            self.window.close()
            for _ in range(18):
                yield
            dialog = self.close_dialog
            assert dialog and dialog.get_mapped(), "Ordinary close asks whether to save and close"
            xid = GdkX11.X11Surface.get_xid(dialog.get_surface())
            x.XSetInputFocus(ptr, xid, 1, 0)
            x.XFlush(ptr)
            dialog.get_widget_for_response(Gtk.ResponseType.ACCEPT).grab_focus()
            yield from QA.chord(self, "Enter")
            for _ in range(90):
                if not self.window.get_mapped():
                    return
                yield
            raise AssertionError("App did not close through ordinary confirmation")

        baseline = json.loads(saved.read_text(encoding="utf-8"))
        initial, old_id = baseline["initial"], baseline["old_id"]
        assert lab.state["solved"] == 2 and terminal(lab.state)["followingLog"]
        assert not lab.theory_rail.get_mapped() and not lab.theory_button.get_mapped()
        assert sync_log(lab.state).startswith(baseline["checkpoint"])
        yield from self.wait_state(lambda: terminal(lab.state)["logIncidentArrived"])
        new_line = next(line for line in sync_log(lab.state).splitlines()[len(initial.splitlines()):]
                        if line.startswith("ERROR "))
        yield from self.frame("powershell-04-live.png")
        yield from self.video_hold("После нового запуска ошибка пришла в тот же журнал")
        yield from self.chord("Ctrl+Shift+C")
        assert lab.state["solved"] == 2 and terminal(lab.state)["followingLog"]
        yield from self.chord("Ctrl+C")
        assert lab.state["solved"] == 3 and not terminal(lab.state)["followingLog"]
        assert terminal(lab.state)["output"][-1] == "^C"
        assert lab.gesture_tokens == ("Select-String [ID] sync.log",), lab.gesture_tokens
        yield from self.frame("powershell-05-prompt-restored.png")
        yield from self.video_hold("Ctrl+C остановил только просмотр; поиск ID без готового значения")
        yield from self.run_command(f"Select-String {old_id} sync.log")
        assert lab.state["solved"] == 3 and "новой строки ERROR" in lab.state["status"]
        new_id = new_line.split()[1]
        yield from self.run_command(f"Select-String {new_id} sync.log")
        assert lab.state["solved"] == 4 and lab.state["finished"] and not lab.active
        result = terminal(lab.state)["output"][-2:]
        assert result == [f"sync.log:5:INFO {new_id} started", f"sync.log:6:{new_line}"], result
        yield from self.frame("powershell-06-result.png")
        (out / "powershell-witness.json").write_text(json.dumps(dict(
            old_id=old_id, new_id=new_id, result=result, solved=4, restarts=1,
            real_entry="Live → Windows 10 → PowerShell", backend="GTK / X11 (Windows simulation, not host)"),
            ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        yield from self.video_hold("Старый ID отклонён; две реальные строки по новой попытке зачтены")
        print(f"PASS real GTK ordinary PowerShell: native keys, new application process, Ctrl+C, {new_id}, 4/4", flush=True)


if __name__ == "__main__":
    if len(sys.argv) == 1:
        for mode in ("pause", "resume"):
            subprocess.check_call([sys.executable, str(Path(__file__).resolve()), mode])
    else:
        app = PowerShellWindow(resuming=sys.argv[1] == "resume")
        code = app.run([])
        raise SystemExit(1 if app.smoke_failure else code)
