"""Native GTK: both desktop profiles via ordinary cards, with a real file handoff."""

import json

from test_live_course_cards_x11 import CourseCards, out
from test_desktop_file_search import APPROVED_ROOT, ARCHIVE_ROOT, HANDOFF_ROOT, OUTSIDE_ROOT, active, contents


class DesktopSearchNative(CourseCards):
    def assert_persistence(self, lab):
        assert not self.engine.storage_error and not lab.state.get("saveError"), (
            lab.state["mission"], lab.state["task"], self.engine.storage_error, lab.state.get("saveError"))

    def advance_card(self, lab):
        yield from super().advance_card(lab)
        self.assert_persistence(lab)

    def exercise(self):
        witness = []
        for profile, initial, key in (
            ("ubuntu", "ubuntu-start", "Ctrl+F"),
            ("windows", "windows-desktop-0", "Ctrl+E"),
        ):
            self.navigate("home")
            for _ in range(25):
                yield
            self.focus_window()
            self.live_os_choices[profile].grab_focus()
            yield from self.chord("Space")
            for _ in range(18):
                yield
            lab = yield from self.ordinary_card(profile)
            assert lab.active and not lab.prep.get_visible() and lab.state["mission"] == initial
            self.focus_window()
            lab.desktop.grab_focus()
            for _ in range(42):
                if lab.state["task"]["id"] == "file-search-open" and lab.state["mission"] != initial:
                    break
                assert lab.active and not lab.state["limitDecisionPending"]
                yield from self.advance_card(lab)
            start = lab.state
            assert (start["mission"], start["task"]["keys"]) == (profile + "-file-search-handoff", key), start["task"]
            yield from self.chord(key)
            self.assert_persistence(lab)
            query = lab.state["task"]["text"]
            wrote = lab.state["solved"]
            assert (active(lab.state)["focus"], active(lab.state)["cursor"]) == ("file-search", 0)
            yield from self.typed({"id": "type-code", "text": "000"})
            assert lab.state["solved"] == wrote and not active(lab.state)["entries"]
            self.capture_widget_png(out / (profile + "-no-matches.png"))
            yield from self.chord("Ctrl+A")
            yield from self.typed({"id": "type-code", "text": query})
            self.assert_persistence(lab)
            s = lab.state
            entries = active(s)["entries"]
            assert active(s)["input"] == query and len(entries) == 2
            assert entries[0]["path"].startswith(ARCHIVE_ROOT) and entries[1]["path"].startswith(APPROVED_ROOT)
            path = entries[1]["path"]
            name = path.rsplit("/", 1)[1]
            before = contents(s)
            assert before[OUTSIDE_ROOT + name] != before[path]
            self.capture_widget_png(out / (profile + "-scoped-results.png"))
            yield from self.video_hold(profile + " · Область поиска и обе версии отчёта")
            yield from self.chord("Down")
            assert lab.state["solved"] == s["solved"] and active(lab.state)["item"] == 0  # draft is not approved
            yield from self.chord("Down")
            selected = lab.state
            assert active(selected)["item"] == 1 and selected["task"]["id"] == "file-copy"
            # The user goes back to the same card. Reopening preserves query,
            # scope, choice and action (the protocol suite also restarts the engine).
            self.return_live_catalog(profile)
            for _ in range(23):
                yield
            assert profile in self.home_resume
            lab = yield from self.ordinary_card(profile)
            self.focus_window()
            lab.desktop.grab_focus()
            restored = lab.state
            self.assert_persistence(lab)
            assert (restored["solved"], active(restored)["query"], active(restored)["item"], restored["task"]) == (
                selected["solved"], query, 1, selected["task"])
            continued_past_budget = False
            while lab.state["task"]["id"] != "file-search-verify":
                if lab.state["limitDecisionPending"]:
                    for _ in range(25):
                        if lab.limit_actions:
                            break
                        yield
                    assert len(lab.limit_actions) == 2
                    lab.limit_actions[1].grab_focus()
                    yield from self.chord("Enter")
                    for _ in range(45):
                        if not lab.state["limitDecisionPending"] and not lab.state["paused"]:
                            break
                        yield
                    lab.desktop.grab_focus()
                    continued_past_budget = True
                assert lab.active and not lab.state["limitDecisionPending"]
                yield from self.advance_card(lab)
                if lab.state.get("scene", {}).get("windows") and active(lab.state)["status"].startswith("Создана копия:"):
                    self.capture_widget_png(out / (profile + "-copy-created.png"))
            s = lab.state
            editor = active(s)
            assert (editor["app"], editor["url"], editor["body"]) == ("code", HANDOFF_ROOT + name, before[path])
            assert contents(s) == dict(before, **{HANDOFF_ROOT + name: before[path]})
            self.capture_widget_png(out / (profile + "-handoff-open.png"))
            yield from self.video_hold(profile + " · Открыта копия из handoff с исходными данными")
            value = s["task"]["text"]
            assert editor["body"].splitlines()[1].split(",") == [query, "approved", value]
            yield from self.advance_card(lab)
            witness.append(dict(profile=profile, initial=initial, action=key, query=query, selected=path,
                                final_path=HANDOFF_ROOT + name, value=value, continued_past_budget=continued_past_budget,
                                credits=s["solved"] + 1, verified_original_and_decoys=True))
            print("PASS ordinary GTK desktop files:", witness[-1], flush=True)
        (out / "desktop-file-search.json").write_text(json.dumps(witness, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    app = DesktopSearchNative()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
