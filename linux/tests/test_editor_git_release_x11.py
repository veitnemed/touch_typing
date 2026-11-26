"""Visible GTK: Home → configure VS Code → editing a handoff → Git commit."""
import os
from pathlib import Path
from test_preview2_native import QA
OUT=Path(os.environ["TOUCH_TYPING_QA_OUTPUT"])
class EditorGitWindow(QA):
    def exercise(self):
        self.navigate("home")
        for _ in range(26): yield
        self.focus_window()
        settings, chooser, level, mode, actions, timer, start = self.home_configure["code"]
        settings.grab_focus(); yield from self.chord("Space")
        level.set_selected(1);mode.set_selected(0);actions.set_selected(0);timer.set_selected(0)
        titles=chooser.get_model()
        chooser.set_selected(next(i for i in range(titles.get_n_items()) if titles.get_string(i).startswith("Собрать инструкцию")))
        start.grab_focus();yield from self.chord("Enter")
        for _ in range(35):
            if self.lab and self.lab.active and not self.lab.state["paused"]:break
            yield
        lab=self.lab
        assert lab and lab.active and lab.state["mission"]=="code-publish-handoff"
        self.focus_window();lab.desktop.grab_focus()
        def source(): return next(w for w in lab.state["scene"]["windows"] if w["active"])["sourceControl"]
        for i in range(66):
            if not lab.active:break
            task=lab.state["task"]; old=lab.state["solved"]; tid=task["id"]
            if tid=="scm-commit":
                yield from self.typed({"id":"type-code","text":"Deliver shift handoff"})
                assert source()["message"]=="Deliver shift handoff"
                self.capture_widget_png(OUT/"editor-git-staged.png")
            if task["keys"]:
                for _ in range(18 if tid=="quick-next" else 1):
                    yield from self.chord(task["keys"])
                    if lab.state["solved"]>old:break
            else:yield from self.typed(task)
            assert lab.state["solved"]==old+1,(task,lab.state["status"],lab.state["solved"],old)
            if tid=="scm-menu" and source()["changes"][source()["selected"]]["staged"]:
                self.capture_widget_png(OUT/"editor-git-context.png")
            if tid=="scm-commit":
                assert source()["lastCommit"]=="Deliver shift handoff"
                assert [c["path"] for c in source()["changes"]]==["notes.md"]
                self.capture_widget_png(OUT/"editor-git-committed.png")
        assert not lab.active and lab.state["finished"] and lab.state["solved"]>=30
        assert lab.state["scene"]["terminal"]["output"][-1]==" M notes.md"
        print("PASS GTK ordinary VS Code card settings, two-source rebuild, unstage, stage, commit, shell log/status;",lab.state["solved"],"actions",flush=True)
if __name__=="__main__":
    app=EditorGitWindow();status=app.run([]);raise SystemExit(1 if app.smoke_failure else status)
