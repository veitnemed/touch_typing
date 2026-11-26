"""Native keyboard path for the guided checkout repair on an isolated X11 display."""
import os
from pathlib import Path

from test_preview2_native import QA

OUT = Path(os.environ["TOUCH_TYPING_QA_OUTPUT"])
MISSION = "code-repair-checkout"
PATH = "/home/student/project/checkout/checkout.py"
EXPECTED = (
    "def checkout_total(price, count):\n"
    "    subtotal = price * count\n"
    "    fee = 2\n"
    "    total = subtotal + fee\n"
    "    return total\n"
)


def editor(lab):
    return next(w for w in lab.state["scene"]["windows"] if w["active"])


class CheckoutWindow(QA):
    def exercise(self):
        self.navigate("home")
        for _ in range(26):
            yield
        self.focus_window()
        settings, chooser, level, mode, actions, timer, start = self.home_configure["code"]
        settings.grab_focus()
        yield from self.chord("Space")
        level.set_selected(1)
        mode.set_selected(0)
        actions.set_selected(0)
        timer.set_selected(0)
        titles = chooser.get_model()
        i = next(i for i in range(titles.get_n_items()) if titles.get_string(i).startswith("Исправить порядок"))
        chooser.set_selected(i)
        start.grab_focus()
        yield from self.chord("Enter")
        for _ in range(35):
            if self.lab and self.lab.active and not self.lab.state["paused"]:
                break
            yield
        lab = self.lab
        assert lab and lab.active and lab.window_only_keys and lab.state["mission"] == MISSION
        assert lab.state["actionLimit"] == lab.state["timeLimit"] == 0
        self.focus_window()
        lab.desktop.grab_focus()
        self.capture_widget_png(OUT / "checkout-01-invalid-order.png")
        yield from self.video_hold("Сначала return стоит до трёх вычислений.")
        reviews = 0
        for _ in range(45):
            if not lab.active:
                break
            task, before = lab.state["task"], lab.state["solved"]
            if task["id"] == "python-edit-write-fee":
                w = editor(lab)
                start, end = sorted((w["bodyCursor"], w["bodyAnchor"]))
                assert w["body"][start:end] == "    fee = 0\n    total = subtotal", w
                self.capture_widget_png(OUT / "checkout-03-selected-block.png")
                yield from self.video_hold("Выделены только два неверных присваивания.")
            if task["keys"]:
                for _ in range(20 if task["id"] == "quick-next" else 1):
                    yield from self.chord(task["keys"])
                    if lab.state["solved"] > before:
                        break
            elif task["id"].startswith("python-edit-write"):
                yield from self.typed({"id": "type-code", "text": task["text"][:-1]})
                assert lab.state["solved"] == before, "Partial text cannot receive credit"
                yield from self.typed({"id": "type-code", "text": task["text"][-1]})
            else:
                yield from self.typed(task)
            assert lab.state["solved"] == before + 1, (task, lab.state["status"])
            if task["id"] == "changes":
                reviews += 1
                source = editor(lab)["sourceControl"]
                changes = source["changes"]
                assert len(changes) == 1 and changes[0]["path"] == "checkout.py" and changes[0]["state"] == "M"
                assert any(line.startswith("- ") for line in changes[0]["diff"])
                assert any(line.startswith("+ ") for line in changes[0]["diff"])
                self.capture_widget_png(OUT / ("checkout-02-first-diff.png" if reviews == 1 else "checkout-04-final-diff.png"))
                yield from self.video_hold("Source Control показывает сохранённую правку и строки до/после.")
        assert reviews == 2 and lab.state["solved"] >= 30 and not lab.active and lab.state["finished"]
        w = editor(lab)
        assert w["url"].endswith("/checkout/checkout-test.txt") and "Ran 2 tests\nOK" in w["body"]
        saved = next(file["content"] for file in lab.state["scene"]["terminal"]["files"] if file["path"] == PATH)
        assert saved == EXPECTED and lab.state["errors"] == 0
        checks = {}
        exec(compile(saved, "checkout.py", "exec"), {}, checks)
        assert checks["checkout_total"](4, 3) == 14 and checks["checkout_total"](1, 0) == 2
        self.capture_widget_png(OUT / "checkout-05-result.png")
        yield from self.video_hold("Сохранена исправная функция; шаги зачтены без ошибок.")
        print("PASS GTK native checkout: line order, selected range, real diff, exact saved project source, zero errors", flush=True)


if __name__ == "__main__":
    app = CheckoutWindow()
    status = app.run([])
    raise SystemExit(1 if app.smoke_failure else status)
