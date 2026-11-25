"""Native Chrome → Downloads → Files and VS Code Explorer transfer. See test_preview2_native.py."""
from test_preview2_native import QA, CaptureConsent, out
from touch_typing import LabPage


class FileWindowQA(QA):
    def exercise(self):
        def inhibit(page):
            # Xvfb doesn't implement the Wayland inhibition handshake. All key presses below
            # still traverse actual X11 → GTK → package → engine, not lab-key protocol calls.
            page.surface = CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()
        LabPage.acquire = inhibit
        for app, level, mission, title in (
            ("chrome",0,"chrome-project-file-inbox","Скачать CSV, переименовать и передать в проект"),
            ("code",0,"code-project-file-move","Переименовать файл и переместить его по проекту"),
            ("code",1,"code-project-file-copy","Подготовить рабочую копию, сохранив оригинал")):
            self.navigate("home")
            for _ in range(24): yield
            menu, choices, levels, mode, actions, timer, start = self.home_configure[app]
            menu.emit("clicked"); levels.set_selected(level)
            catalog = choices.get_model()
            idx = next(i for i in range(catalog.get_n_items()) if catalog.get_string(i) == title)
            choices.set_selected(idx)
            start.emit("clicked")
            for _ in range(70):
                if self.lab and self.lab.active and self.lab.state.get("mission") == mission: break
                yield
            lab = self.lab; assert lab.state["mission"] == mission and lab.active
            self.focus_window()
            while not lab.state["finished"]:
                state = lab.state; task = state["task"]
                w = next(x for x in state["scene"]["windows"] if x["active"])
                if (w["url"] == "chrome://downloads" and task["id"] == "link-open" and
                    w["entries"][w["item"]]["kind"] == "reveal"):
                    self.capture_widget_png(out/(mission+"-download.png"))
                if w["focus"] == "file-name":
                    assert w["input"][w["cursor"]:] == ".csv" and w["anchor"] == 0, w
                    self.capture_widget_png(out/(mission+"-rename.png"))
                if task["id"] == "file-paste":
                    self.capture_widget_png(out/(mission+"-destination.png"))
                yield from self.advance_goal(lab)
            state=lab.state
            w=next(x for x in state["scene"]["windows"] if x["active"])
            files={f["path"]:f["content"] for f in state["scene"]["terminal"]["files"]}
            assert w["app"] == "code" and "/project/reports/" in w["url"]
            assert files[w["url"]] == w["body"] and len(w["body"].splitlines())==3
            print("NATIVE GTK:",mission,state["solved"],w["url"],repr(w["body"]),flush=True)

if __name__ == "__main__":
    app=FileWindowQA()
    code=app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
