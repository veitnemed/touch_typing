"""Hosted X11: use real ordinary cards and continue active practice past the first task."""
import json
from test_preview2_native import QA, alias, codes, out

TEXT_PREFIX = {"code-rename-confirm", "project-search-submit", "exact-find-submit", "quick-choose"}
TYPED_FINISH = {"notebook-type", "python-edit-write", "file-search-query"}


class CourseCards(QA):
    def chord(self, label):
        # XTest posts to the display asynchronously. Under CI load a fixed
        # number of GTK timer ticks can elapse before routing the events. Wait
        # for observed press AND release on every physical key; never repeat a
        # credited shortcut merely because the renderer has not seen it yet.
        keys = [codes[alias.get(key, key)] for key in label.split("+")]
        receipts = {(key, down): self.received.get((key, down), 0) for key in keys for down in (True, False)}
        yield from super().chord(label)
        for _ in range(100):
            if all(self.received.get(event, 0) > prior for event, prior in receipts.items()):
                return
            yield
        observed = {event: self.received.get(event, 0) - prior for event, prior in receipts.items()}
        raise AssertionError(("native X11 delivery did not settle", label, observed))

    def advance_card(self, lab):
        s = lab.state
        t, m = s["task"], s["mission"]
        goal, previous = t["id"], s["solved"]
        value = t.get("text","")
        if goal == "project-search-repair":
            for key in ("End","Backspace","5"):
                yield from self.chord(key)
        elif goal == "save" and m == "code-project-file-move":
            yield from self.chord("Ctrl+End")
            yield from self.typed({"id":"type-code","text":value})
        elif value and (goal in TEXT_PREFIX or m.startswith("code-notebook-") and goal == "save"
                        or m == "code-find-exact-edit" and goal == "save"):
            yield from self.typed({"id":"type-code","text":value})
        if t["keys"]:
            for _ in range(28 if t["keys"] in ("Down","Up","Tab","Shift+Tab") else 1):
                yield from self.chord(t["keys"])
                if lab.state["solved"] > previous:
                    break
        elif goal in TYPED_FINISH:
            yield from self.typed({"id":"type-code","text":value})
        else:
            yield from self.typed(t)
        assert lab.state["solved"] == previous+1, (
            m, goal, value, s["phase"], t.get("keys"), lab.state.get("status"), lab.state["solved"], previous
        )

    def ordinary_card(self, app):
        self.home_cards[app].grab_focus()
        yield from self.chord("Enter")
        for _ in range(90):
            if self.lab and self.lab.active and not self.lab.state["paused"]:
                break
            yield
        return self.lab

    def exercise(self):
        witness = []
        for environment, apps in (
            ("ubuntu", ("chrome","code","terminal","ubuntu","workday")),
            ("windows", ("windows","terminal","workday")),
        ):
            for app in apps:
                self.navigate("home")
                for _ in range(25): yield
                self.focus_window()
                self.live_os_choices[environment].grab_focus()
                yield from self.chord("Space")
                for _ in range(18): yield
                lab = yield from self.ordinary_card(app)
                assert (lab and lab.active and not lab.state["paused"] and lab.window_only_keys and lab.surface is None
                    and lab.state["app"] == app and lab.state["actionLimit"] == (0 if app == "terminal" else 40)), (environment,app,lab.state if lab else None)
                assert not lab.prep.get_visible(), "a card must not open a new catalog"
                self.focus_window()
                lab.desktop.grab_focus()
                first=lab.state["mission"]
                length=lab.state["scenarioLength"]
                trace=[]
                self.capture_widget_png(out/f"card-{environment}-{app}-01-start.png")
                if app == "terminal":
                    expected = "terminal-powershell-diagnostic" if environment == "windows" else "terminal-practical-incident"
                    assert first == expected and length == 4
                    for _ in range(2):
                        trace.append(lab.state["task"]["text"])
                        yield from self.advance_card(lab)
                    assert lab.state["scene"]["terminal"]["followingLog"]
                    for _ in range(600):
                        if lab.state["scene"]["terminal"]["logIncidentArrived"]:
                            break
                        yield
                    assert lab.state["scene"]["terminal"]["logIncidentArrived"]
                    self.capture_widget_png(out/f"card-{environment}-{app}-02-journal.png")
                    trace.append("Ctrl+C")
                    yield from self.advance_card(lab)
                    trace.append(lab.state["task"]["text"])
                    yield from self.advance_card(lab)
                    assert lab.state["finished"] and lab.state["solved"] == 4 and not lab.active
                    witness.append(dict(environment=environment, app=app, initial=first, first_actions=length,
                                        second=None, successful=lab.state["solved"], trace=trace))
                    self.return_live_catalog(app)
                    for _ in range(22):
                        yield
                    print("PASS GTK", environment, app, first, "4 meaningful actions", flush=True)
                    continue
                while lab.state["solved"] < 20 or lab.state["mission"] == first:
                    s=lab.state
                    assert lab.active and not s.get("limitDecisionPending") and s["solved"] < 100, (app,s["solved"],s["mission"],length)
                    yield from self.advance_card(lab)
                    trace.append(s["task"]["id"])
                s=lab.state
                assert lab.active and not s["finished"] and s["mission"] != first, (app,s["mission"])
                self.capture_widget_png(out/f"card-{environment}-{app}-02-continuation.png")
                witness.append(dict(environment=environment,app=app,initial=first,first_actions=length,
                                    second=s["mission"],successful=s["solved"],trace=trace))
                self.return_live_catalog(app)
                for _ in range(22): yield
                assert app in self.home_resume, (environment,app,"missing practice draft")
                assert self.window.get_focus() is self.home_cards[app], (environment,app,"wrong actual focus")
                assert not any(card.has_css_class("reference-selected") for card in self.home_cards.values())
                if app == "code":
                    self.capture_widget_png(out/f"card-{environment}-{app}-03-returned-focus.png")
                restored = yield from self.ordinary_card(app)
                assert restored and not restored.state["paused"], (environment, app, "resume is paused")
                assert (restored.state["solved"], restored.state["mission"], restored.state["task"]) == (
                    s["solved"], s["mission"], s["task"]), (environment, app, "did not resume the same goal")
                self.return_live_catalog(app)
                for _ in range(20): yield
                print("PASS GTK", environment, app, first, "→", s["mission"], s["solved"], flush=True)
        (out/"live-card-success.json").write_text(json.dumps(witness,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        print("PASS GTK card boundaries:", [(x["environment"],x["app"],x["successful"]) for x in witness],flush=True)


if __name__=="__main__":
    app=CourseCards()
    result=app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
