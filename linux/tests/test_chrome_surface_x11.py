"""An ordinary Chrome card and real X11 pointer: address, tab, actionable Back."""
from test_preview2_native import out
from test_live_course_cards_x11 import CourseCards
from test_chrome_history_native import active
from test_notebook_workbench_native import WorkbenchWindow


class ChromeSurfaceX11(CourseCards):
    def exercise(self):
        self.navigate("home")
        for _ in range(24): yield
        self.focus_window()
        lab=yield from self.ordinary_card("chrome")
        assert lab and lab.active and not lab.state["paused"] and lab.state["task"]["keys"]=="Ctrl+L"
        w=active(lab); first=w["activeTab"]; url=w["url"]; owner=w["id"]
        target=next(i for i,tab in enumerate(w["tabs"]) if i!=first and tab["url"]!=url)
        other=w["tabs"][target]["url"]
        def pointer(kind,slot=-1):
            return WorkbenchWindow.pointer(self,"browser-surface:"+kind,cell=slot,path=owner)
        def missing(command):
            return not any(part[1]=="browser-surface:"+command for part in lab.desktop._notebook_hits)
        assert missing("back") and missing("forward"),"disabled history buttons must not consume clicks"
        yield from pointer("address")
        w=active(lab); assert (w["focus"],w["anchor"],w["cursor"],lab.state["solved"])==("url",0,len(url),0)
        self.capture_widget_png(out/"01-pointer-selected-address.png")
        yield from pointer("tab",target)
        assert active(lab)["url"]==other and lab.state["solved"]==0
        self.capture_widget_png(out/"02-pointer-switched-tab.png")
        yield from pointer("tab",first)
        assert active(lab)["url"]==url and lab.state["solved"]==0
        yield from pointer("address")
        yield from self.typed({"id":"type-code","text":"report"})
        yield from self.chord("Enter")
        visiting=active(lab); assert visiting["canGoBack"] and "report" in visiting["url"] and visiting["url"]!=url
        before=lab.state["solved"]
        self.capture_widget_png(out/"03-pointer-back-enabled.png")
        assert visiting["url"]=="https://www.google.com/search?q=report" and len(visiting["entries"])>=2
        hit_index=next(i for i,e in enumerate(visiting["entries"]) if e["path"].endswith("/shipments"))
        yield from pointer("search-result",hit_index)
        assert active(lab)["url"]==visiting["entries"][hit_index]["path"] and lab.state["solved"]==before
        self.capture_widget_png(out/"03b-report-opened.png")
        yield from pointer("back")
        assert active(lab)["url"]==visiting["url"] and active(lab)["item"]==hit_index

        yield from pointer("back")
        assert active(lab)["url"]==url and not active(lab)["canGoBack"] and lab.state["solved"]==before
        for _ in range(10): yield
        assert missing("back") and not missing("forward")
        self.capture_widget_png(out/"04-pointer-back-restored.png")
        yield from pointer("forward")
        assert active(lab)["url"]==visiting["url"] and active(lab)["canGoForward"]
        yield from pointer("forward")
        assert active(lab)["url"]==visiting["entries"][hit_index]["path"] and not active(lab)["canGoForward"]
        yield from pointer("back")
        yield from pointer("refresh")
        assert "обновлена" in active(lab)["status"] and lab.state["solved"]==before
        yield from pointer("back")
        self.capture_widget_png(out/"05-pointer-forward-and-back.png")
        yield from pointer("new-tab")
        assert active(lab)["url"]=="about:blank" and active(lab)["focus"]=="url"
        self.capture_widget_png(out/"06-pointer-new-tab.png")
        # Close a background tab, keeping the active address field and its text.
        yield from self.typed({"id":"type-code","text":"draft"})
        yield from pointer("close-tab",target)
        assert active(lab)["input"]=="draft" and active(lab)["focus"]=="url" and active(lab)["cursor"]==5
        source=next(i for i,t in enumerate(active(lab)["tabs"]) if t["url"]==url)
        yield from pointer("tab",source)
        assert lab.state["solved"]==before
        yield from pointer("bookmark"); assert url in active(lab)["savedBookmarks"]
        yield from pointer("downloads"); assert active(lab)["url"]=="chrome://downloads"
        yield from pointer("close-tab",active(lab)["activeTab"])
        yield from pointer("tab",source)
        yield from pointer("home"); assert active(lab)["url"]=="https://www.google.com/"
        yield from pointer("back"); assert active(lab)["url"]==url and lab.state["solved"]==before
        yield from self.chord("Ctrl+L")
        assert lab.state["solved"]==1 and active(lab)["selected"]
        print(f"PASS native X11 ordinary Chrome {lab.desktop.get_width()}×{lab.desktop.get_height()}: Back/Forward, reload, new/close and background-tab draft; restored {url}; no mouse credit; physical Ctrl+L earned 1.",flush=True)


if __name__=="__main__":
    app=ChromeSurfaceX11(); app.window_size=(1024,600)
    code=app.run([]); raise SystemExit(1 if app.smoke_failure else code)
