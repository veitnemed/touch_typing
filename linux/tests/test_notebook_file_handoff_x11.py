"""Walk the connected VS Code lesson with native pressed/released keys.

Run against extracted GTK assets. Only the Wayland consent handshake is
replaced, because X11 has no such API.
"""
import re
from unittest.mock import patch

from test_notebook_advanced_routes import CSV_EXPECTED, filemap
from test_preview2_native import QA, CaptureConsent, out
from touch_typing import LabPage


def active(lab):
    return next(w for w in lab.state["scene"]["windows"] if w["active"])


class NotebookHandoffWindow(QA):
    def send_text(self, value):
        yield from self.typed({"id": "type-code", "text": value})

    def replace_fragment(self, lab, old, new):
        for key in ("Ctrl+F", "Ctrl+A"):
            yield from self.chord(key)
        yield from self.send_text(old)
        for key in ("Enter", "Esc"):
            yield from self.chord(key)
        editor = active(lab)
        lo, hi = sorted((editor["bodyCursor"], editor["bodyAnchor"]))
        assert editor["body"][lo:hi] == old and editor["focus"] == "body"
        yield from self.send_text(new)

    def enter_goal(self, lab):
        task, before = lab.state["task"], lab.state["solved"]
        key, ident, value = task["keys"], task["id"], task["text"]
        if ident in ("quick-choose", "palette-choose", "file-name-commit"):
            yield from self.send_text(value)
        if ident == "save" and "from csv import" in value:
            existing = active(lab)["body"]
            for pattern in (r'data/[a-z-]+\.csv', r'return [a-z]+\(values\)'):
                old, new = (re.search(pattern, body)[0] for body in (existing, value))
                yield from self.replace_fragment(lab, old, new)
        for _ in range(35 if key in ("Down", "Tab") else 1):
            yield from self.chord(key or "Enter")
            if lab.state["solved"] == before + 1:
                break
        assert lab.state["solved"] == before + 1, (task, lab.state["status"], active(lab)["focus"])

    def evidence(self, name, caption):
        for _ in range(8):
            yield
        self.capture_widget_png(out / ("nb-flow-" + name + ".png"))
        yield from self.video_hold(caption)

    def exercise(self):
        def accept_without_wayland(page):
            page.release_capture()
            page.waiting_capture = False
            page.surface = CaptureConsent()
            page.render(page.host.call("lab-resume"))
            page.desktop.grab_focus()

        self.navigate("home")
        for _ in range(26):
            yield
        self.focus_window()
        settings, chooser, level, mode, actions, timer, start = self.home_configure["code"]
        settings.grab_focus()
        yield from self.chord("Space")
        level.set_selected(2)
        mode.set_selected(0)
        actions.set_selected(0)
        timer.set_selected(0)
        titles = chooser.get_model()
        i = next(i for i in range(titles.get_n_items()) if titles.get_string(i).startswith("Сверить выгрузку,"))
        chooser.set_selected(i)
        yield from self.evidence("01-config", "VS Code: связанное поручение с выгрузкой CSV")
        with patch.object(LabPage, "acquire", accept_without_wayland):
            start.grab_focus()
            yield from self.chord("Enter")
            for _ in range(35):
                if self.lab and self.lab.active:
                    break
                yield
            lab = self.lab
            assert lab and lab.active and lab.state["mission"] == "code-project-file-csv-notebook"
            assert lab.state["actionLimit"] == lab.state["timeLimit"] == 0
            self.focus_window()
            lab.desktop.grab_focus()
            before_files = filemap(lab.state)
            incoming = next(path for path in before_files if "/incoming/" in path)
            stem = incoming.rsplit("/", 1)[-1].removesuffix(".csv")
            root = incoming.split("/incoming/")[0]
            archived, moved = (root + "/data/" + stem + suffix for suffix in (".csv", "-updated.csv"))
            final_notebook = root + "/verified/" + stem + "-verification.ipynb"
            prior, fresh = None, False
            for _ in range(50):
                if not lab.active:
                    break
                task = lab.state["task"]
                yield from self.enter_goal(lab)
                editor = active(lab)
                if task["text"] == "!conflict":
                    assert "Ничего не заменено" in editor["status"] and filemap(lab.state) == before_files
                    yield from self.evidence("02-conflict", "Две одноимённые выгрузки: ничего не перезаписано")
                if (task["id"] == "file-paste" and task["text"] != "!conflict"
                        and moved in filemap(lab.state) and editor["url"] == moved):
                    assert filemap(lab.state)[moved] == before_files[incoming] and incoming not in filemap(lab.state)
                    assert filemap(lab.state)[archived] == before_files[archived]
                    yield from self.evidence("03-input-moved", "Новый CSV перенесён и открыт по фактическому пути")
                if task["id"] == "notebook-run":
                    n = editor["notebook"]
                    if n and n["selected"] == 2 and n["cells"][2]["output"]:
                        prior = n["cells"][2]["output"]
                        yield from self.evidence("04-prior-result", "Прежний расчёт не соответствует новой выгрузке")
                if task["id"] == "code-definition":
                    assert editor["url"].endswith("_metrics.py") and not editor["url"].endswith("archive_metrics.py")
                    caret = editor["bodyCursor"]
                    assert editor["body"][caret - 4:caret] == "def "
                    yield from self.evidence("05-definition", "F12 привёл к импортированному определению")
                if task["id"] == "code-back":
                    n = editor["notebook"]
                    assert (n["selected"], n["caret"], n["editing"]) == (2, 0, True) and n["cells"][2]["stale"]
                    yield from self.evidence("06-stale", "Курсор вернулся; прежний вывод явно отмечен")
                if task["text"] == "Run All Cells":
                    n = editor["notebook"]
                    assert n["kernel"]["generation"] == 2 and not n["cells"][2]["stale"]
                    assert prior != n["cells"][2]["output"] == CSV_EXPECTED[stem]
                    fresh = True
                    yield from self.evidence("07-fresh", "Чистое ядро использовало исправленный модуль и свежий CSV")
            assert not lab.active and lab.state["finished"] and lab.state["solved"] == 39 and fresh
            editor = active(lab)
            assert editor["url"] == final_notebook and final_notebook in filemap(lab.state)
            n = editor["notebook"]
            assert (n["kernel"]["counter"], n["cells"][2]["output"], n["cells"][2]["outputLabel"]) == (
                0, CSV_EXPECTED[stem], "Сохранённый вывод")
            yield from self.evidence("08-result", "Сохранённый блокнот открыт снова из verified")
            print(f"PASS GTK: конфликт → перенос CSV → F12 и исправление → чистый расчёт {CSV_EXPECTED[stem]} "
                  "→ сохранить, переместить и открыть блокнот. 39 проверенных действий.", flush=True)


if __name__ == "__main__":
    app = NotebookHandoffWindow()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
