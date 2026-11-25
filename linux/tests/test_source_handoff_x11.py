"""Native GTK proof for source handoffs (uses XTest, not lab-key protocol injection).

TOUCH_TYPING_QA_SOURCE_MODE=baseline exercises the previous published candidate.
TOUCH_TYPING_QA_SOURCE_MODE=course proves that sequential practice reaches the
new exercise (requires the generalized ordinary-course release).
TOUCH_TYPING_QA_SOURCE_ROUTES selects a comma-delimited subset for video chapters.
TOUCH_TYPING_QA_VIDEO=1 keeps key frames on screen long enough to read.
"""
import os
from test_preview2_native import QA, CaptureConsent, alias, codes, out
from test_source_handoff import CASES, CONFIGS, ROUTES, active, disk
from touch_typing import LabPage

TITLES = {
    "chrome-compare-bulletin": "Сравнить редакции бюллетеня и сверить CSV",
    "workday-verified-bulletin": "Выпустить заметку по актуальному бюллетеню",
    "workday-windows-verified-bulletin": "Выпустить заметку по актуальному бюллетеню",
    "ubuntu-source-window-handoff": "Сверить источник и вернуться к нужному черновику",
    "windows-desktop-source-handoff": "Сверить источник и вернуться к нужному черновику",
    "workday-log-config-repair": "Исправить настройку по журналу проверки",
    "workday-windows-log-config-repair": "Исправить настройку по журналу проверки",
}
PRIMARY = [("chrome", "ubuntu"), ("workday", "ubuntu"), ("ubuntu", "ubuntu"),
           ("windows", "windows"), ("workday", "windows")]


class SourceQA(QA):
    def chord(self, label):
        # XTest can queue events faster than GDK/render + engine storage process
        # them while ffmpeg records. Wait for the final release acknowledgement
        # rather than queuing a second Alt+Tab and undoing the first.
        head=label.split("+",1)[0]
        release=(codes[alias.get(head,head)],False)
        before=self.received.get(release,0)
        yield from super().chord(label)
        for _ in range(120):
            if self.received.get(release,0)>before: return
            yield
        raise AssertionError("GTK did not deliver key release: "+label)

    def checkpoint(self, label, filename):
        self.capture_widget_png(out/filename)
        yield from self.video_hold(label)

    def home_card(self, module, profile):
        self.navigate("home")
        for _ in range(28): yield
        self.live_os_choices[profile].emit("clicked")
        for _ in range(20): yield
        assert module in self.home_cards, (module, list(self.home_cards))
        return self.home_cards[module]

    def opening(self, module, profile):
        button=yield from self.home_card(module, profile)
        button.emit("clicked")
        for _ in range(70):
            if getattr(self,"lab",None) and self.lab.active: break
            yield
        lab = self.lab
        assert lab.active and lab.state["app"] == module
        self.focus_window()
        first=lab.state["mission"]
        for _ in range(3):
            yield from self.advance_goal(lab)
        assert lab.state["solved"]==3,lab.state["status"]
        self.capture_widget_png(out/(module+"-"+profile+"-primary.png"))
        print("DEFAULT CARD", profile,module,first,"three native steps OK",flush=True)
        lab.stop(show=False)
        for _ in range(14): yield

    def configured(self, mission):
        module=mission.split("-")[0]
        profile="windows" if "windows" in mission else "ubuntu"
        yield from self.home_card(module,profile)
        menu, picker, levels, _mode, _length, _timer, button=self.home_configure[module]
        menu.emit("clicked")
        levels.set_selected(ROUTES[mission])
        choices=picker.get_model()
        index=next(i for i in range(choices.get_n_items()) if choices.get_string(i)==TITLES[mission])
        picker.set_selected(index)
        button.emit("clicked")
        for _ in range(70):
            if getattr(self,"lab",None) and self.lab.active and self.lab.state.get("mission")==mission: break
            yield
        lab=self.lab
        assert lab.state["mission"]==mission and lab.active
        self.focus_window()
        captured=set()
        while not lab.state["finished"]:
            state=lab.state
            t=state["task"]["id"]
            w=active(state) if state["scene"]["activeWindow"] else None
            stage=None
            if t=="escape" and w["app"]=="chrome" and "/workshop/" in w["url"] and w["bodyAnchor"] != w["bodyCursor"]:
                stage="archive" if "Архивная редакция" in w["body"] else "current"
            elif t=="downloads":
                stage="download"
            elif t=="file-paste":
                stage="move"
            elif t=="paste":
                if "workday" in mission and "bulletin" in mission:
                    stage="append-row" if w["body"].endswith("CSV:\n") and "[источник]" not in w["body"] else "source-link"
                elif "-handoff" in mission:
                    stage="return-to-caret"
                else:
                    stage="apply-log-line"
            elif t=="win-overlay-next":
                stage="task-view"
            elif t=="minimize" or t=="win-down":
                stage="next-to-source"
            elif t=="file-open" and w.get("folder","").endswith("/reports"):
                stage="moved-file"
            elif t=="save":
                stage="save"
            elif t.startswith("command/python3"):
                stage="run-verification"
            if stage and stage not in captured:
                captured.add(stage)
                yield from self.checkpoint(mission+":"+stage, mission+"-"+stage+".png")
            yield from self.advance_goal(lab)
        state=lab.state; files=disk(state)
        app=active(state)
        if "log-config" in mission:
            assert state["scene"]["terminal"]["lastSuccess"]
            assert any(v+"\nstandby=true\n" in files["/home/student/project/"+key+".env"] for key,(_,v) in
                       CONFIGS.items() if "/home/student/project/"+key+".env" in files)
        elif "handoff" in mission:
            assert app["url"].endswith("-handoff.txt") and "[значение]" not in app["body"]
            assert any(c[-1] in app["body"] for c in CASES.values())
            assert files[app["url"]]==app["body"]
        elif mission.startswith("workday"):
            assert app["url"].endswith("-review.md") and "https://practice.local/workshop/" in app["body"]
            assert any(app["body"].endswith(c[4]+"\n") for c in CASES.values())
            assert files[app["url"]]==app["body"]
        else:
            assert app["url"].startswith("/home/student/Downloads/") and any(c[4]+"\n" in app["body"] for c in CASES.values())
            assert files[app["url"]]==app["body"]
        print("NATIVE SOURCE",profile,mission,state["solved"],app["url"],flush=True)
        yield from self.checkpoint(mission+":result", mission+"-result.png")

    def course(self):
        yield from self.home_card("windows", "windows")
        menu, picker, levels, _mode, length, _timer, start = self.home_configure["windows"]
        menu.emit("clicked"); levels.set_selected(1); length.set_selected(0)
        options=picker.get_model()
        item=next(i for i in range(options.get_n_items()) if options.get_string(i)=="Последовательная практика")
        picker.set_selected(item)
        start.emit("clicked")
        for _ in range(85):
            if self.lab and self.lab.active: break
            yield
        self.focus_window()
        lab=self.lab
        first=lab.state["mission"]
        target="windows-desktop-source-handoff"
        assert lab.state["routeKind"]=="course" and lab.state["actionLimit"]==0 and first!=target
        passed=False; first_boundary=False
        for _ in range(180):
            s=lab.state
            assert lab.active and not s["finished"],s["mission"]
            if not first_boundary and s["mission"]!=first:
                first_boundary=True
                yield from self.checkpoint("windows-course:first-boundary","windows-course-first-boundary.png")
            if s["mission"]==target and s["scenarioCompletedGoals"]+1==s["scenarioLength"]:
                files=disk(s)
                note=next((content for path,content in files.items() if path.endswith("-handoff.txt")),"")
                assert any(case[5] in note for case in CASES.values()) and "[значение]" not in note,note
                yield from self.checkpoint("windows-course:saved-value-in-next-job","windows-course-saved-note.png")
                yield from self.advance_goal(lab)
                assert lab.state["mission"]!=target and lab.state["solved"]==s["solved"]+1
                passed=True
                break
            yield from self.advance_goal(lab)
        assert passed and first_boundary and lab.state["solved"]>=20
        print("NATIVE ORDINARY",first,"->",target,"->",lab.state["mission"],lab.state["solved"],flush=True)

    def exercise(self):
        def inhibit(page):
            # An authenticated disposable X11 display has no Wayland dialog.
            # Every key gesture still traverses GDK and the actual main window.
            page.surface=CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()
        LabPage.acquire=inhibit
        mode=os.environ.get("TOUCH_TYPING_QA_SOURCE_MODE","candidate")
        if mode=="baseline":
            for module,profile in PRIMARY:
                yield from self.opening(module,profile)
            catalogue=set(x["id"] for x in self.call("lab-catalog")["missions"])
            assert not catalogue.intersection(ROUTES),catalogue.intersection(ROUTES)
        elif mode=="course":
            yield from self.course()
        else:
            subset=os.environ.get("TOUCH_TYPING_QA_SOURCE_ROUTES")
            requested=subset.split(",") if subset else list(ROUTES)
            for mission in requested:
                yield from self.configured(mission)


if __name__=="__main__":
    app=SourceQA()
    code=app.run([])
    raise SystemExit(1 if app.smoke_failure else code)
