"""Select the regular Live → VS Code lesson in GTK and type into its native window.

Requires the same isolated Xvfb/XTest setup as test_preview2_native. The only
simulated OS operation is Wayland shortcut-inhibition consent, unavailable on X11.
"""
from unittest.mock import patch
from test_preview2_native import QA, CaptureConsent, out
from touch_typing import LabPage

class NotebookWindow(QA):
    def exercise(self):
        def acquired(page):
            page.release_capture()
            page.waiting_capture=False
            page.surface=CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()
        self.navigate("home")
        for _ in range(30): yield
        self.focus_window()
        settings, mission, level, mode, actions, timer, start = self.home_configure["code"]
        settings.emit("clicked")
        level.set_selected(1)
        mode.set_selected(0)
        actions.set_selected(0)
        timer.set_selected(0)
        choices=mission.get_model()
        titles=[choices.get_string(i) for i in range(choices.get_n_items())]
        mission.set_selected(titles.index("Исправить формулу в блокноте по заявке"))
        with patch.object(LabPage,"acquire",acquired):
            start.emit("clicked")
            for _ in range(30): yield
            lab=self.lab
            assert lab.active and lab.state["mission"]=="code-notebook-repair"
            self.focus_window(); lab.desktop.grab_focus()
            active=lambda: next(w for w in lab.state["scene"]["windows"] if w["active"])
            changed=[]; types=set()
            for _ in range(28):
                t=lab.state["task"]; old=lab.state["solved"]; task=t["id"]
                if task == "notebook-type":
                    before = active()["notebook"]
                    index = before["selected"]
                    source = before["cells"][index]["source"]
                    yield from self.chord("Ctrl+Delete")
                    empty = active()["notebook"]
                    assert empty["cells"][index]["source"] == "" and empty["caret"] == empty["anchor"] == 0
                    assert lab.state["solved"] == old
                    yield from self.chord("Ctrl+Z")
                    restored = active()["notebook"]
                    assert restored["cells"][index]["source"] == source
                    assert (restored["caret"], restored["anchor"]) == (before["caret"], before["anchor"])
                if t["text"] and task in {"notebook-type","quick-choose","save"}:
                    # A notebook cell never inserts a newline just to submit its text.
                    yield from self.typed({"id":"type-code","text":t["text"]})
                if t["keys"]: yield from self.chord(t["keys"])
                assert lab.state["solved"]==old+1,(t,lab.state["status"],active()["focus"])
                n=active().get("notebook")
                if n:
                    types.update(c["kind"] for c in n["cells"])
                    if task=="notebook-run" and n["selected"]==2:
                        changed.append(n["cells"][2]["output"])
                        self.capture_widget_png(out/("notebook-wrong.png" if len(changed)==1 else "notebook-corrected.png"))
                    if task=="notebook-type":
                        assert n["editing"] and n["cells"][2]["source"]==t["text"]
                        self.capture_widget_png(out/"notebook-edit.png")
                if not lab.active: break
            assert types=={"markdown","code"} and len(changed)==2 and changed[0]!=changed[1],changed
            s=lab.state
            assert s["finished"] and any(f["path"].endswith("-result.txt") and f["content"]==changed[-1]
                                          for f in s["scene"]["terminal"]["files"])
            print("PASS GTK catalog → app.py → notebook → correction → saved number:", changed,"in",s["solved"],"steps",flush=True)

if __name__=="__main__":
    app=NotebookWindow()
    result=app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
