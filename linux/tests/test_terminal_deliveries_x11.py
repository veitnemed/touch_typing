"""Two ordinary GTK cards via actual X11 keyboard, with visible copy/hash evidence."""
import json
from test_live_course_cards_x11 import CourseCards
from test_preview2_native import out


class DeliveryWindow(CourseCards):
    def exercise(self):
        route=[]
        for desk in ("ubuntu","windows"):
            self.navigate("home")
            for _ in range(22): yield
            self.focus_window()
            self.live_os_choices[desk].grab_focus()
            yield from self.chord("Space")
            for _ in range(15): yield
            lab=yield from self.ordinary_card("terminal")
            ps=desk=="windows"
            first="terminal-delivery-sync-"+("ps" if ps else "bash")
            assert lab.state["mission"]==first and lab.state["actionLimit"]==40
            assert lab.state["scene"]["terminal"]["shell"]==("powershell" if ps else "bash")
            self.focus_window();lab.desktop.grab_focus()
            assert lab.theory_rail.get_mapped()
            self.capture_widget_png(out/f"{desk}-sync-entry.png")
            yield from self.chord("F2")
            assert not lab.theory_rail.get_mapped() and lab.state["solved"]==0
            yield from self.chord("F2")
            assert lab.theory_rail.get_mapped()
            for i in range(30):
                if i==7:
                    caps = " ".join(lab.gesture_tokens)
                    assert "[CSV]" in caps and "east.csv" not in caps and "west.csv" not in caps
                if i in (1,3,7): self.capture_widget_png(out/f"{desk}-delivery-{i:02}.png")
                route.append({"profile":desk,"n":i,"mission":lab.state["mission"],"goal":lab.state["task"]["title"]})
                yield from self.advance_card(lab)
                if i==0:
                    joined="\n".join(lab.state["scene"]["terminal"]["output"])
                    assert "weekly.txt" in joined and "summary.csv" in joined
                if i==1:
                    f={x["path"]:x["content"] for x in lab.state["scene"]["terminal"]["files"]}
                    root="/home/student/project/"
                    assert f[root+"Work/notes/weekly.txt"]==f[root+"Backup/notes/weekly.txt"]
                    assert f[root+"Backup/archive.txt"].strip()
                    self.return_live_catalog("terminal")
                    for _ in range(16): yield
                    assert "terminal" in self.home_resume
                    lab=yield from self.ordinary_card("terminal")
                    assert lab.state["solved"]==2 and lab.state["task"]["id"]=="command/delivery-read-copy"
                    self.focus_window();lab.desktop.grab_focus()
                if i==3:
                    assert lab.state["mission"]=="terminal-delivery-hash-"+("ps" if ps else "bash") and lab.active
            assert lab.state["solved"]==30 and lab.active
            self.capture_widget_png(out/f"{desk}-delivery-30.png")
            self.return_live_catalog("terminal")
            for _ in range(20): yield
            print("PASS GTK",desk,": normal terminal card, backup, retry, verify actual hash, resume and 30 credited actions",flush=True)
        (out/"delivery-course.json").write_text(json.dumps(route,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")


if __name__=="__main__":
    app=DeliveryWindow(); status=app.run([])
    raise SystemExit(1 if app.smoke_failure else status)
