"""Run both workstation-debug cases through normal lab-key protocol."""
import base64
from contextlib import ExitStack
import os
import struct
import tempfile
import unittest
import zlib
from unittest.mock import patch

from platform_linux import Engine


def png_fixture():
    """Protocol input bytes only; this bitmap is not product/visual evidence."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return base64.b64encode(b"\x89PNG\r\n\x1a\n" +
                            chunk(b"IHDR", struct.pack(">IIBBBBB", 24, 16, 8, 0, 0, 0, 0)) +
                            chunk(b"IDAT", zlib.compress(bytes(25 * 16))) + chunk(b"IEND", b"")).decode()


CASES = {
    "guided": dict(caller="billing.py", module="factors.py", symbol="price_in_cents", call="price_in_cents(45)",
                   doc="100 cents", incorrect="    return value * 1000", corrected="    return value * 100",
                   test="test_billing.PriceTests.test_price_in_cents", report="billing-report.md", expected="4500"),
    "independent": dict(caller="clock_app.py", module="duration.py", symbol="to_seconds", call="to_seconds(4200)",
                        doc="1000 milliseconds", incorrect="    return value / 10", corrected="    return value / 1000",
                        test="test_clock.DurationTests.test_to_seconds", report="clock-report.md", expected="4.2"),
}


class DebugDay(unittest.TestCase):
    def test_guided_and_independent(self):
        for mode, case in CASES.items():
            with self.subTest(mode=mode):
                uninterrupted = self.route(mode, case, restart_midway=False)
                self.assertEqual(self.route(mode, case, restart_midway=True), uninterrupted)

    def route(self, mode, case, restart_midway):
        with tempfile.TemporaryDirectory() as directory, patch.dict(
                os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}), ExitStack() as cleanup:
            return self._run_route(mode, case, restart_midway, cleanup)

    def _run_route(self, mode, case, restart_midway, cleanup):
        def open_engine():
            client = Engine(verify=True)
            cleanup.callback(client.close)
            return client

        engine = open_engine()
        engine.call("configure", layout="EN", dock=["chrome", "code", "terminal", "files"])
        options = dict(app="workday", level=1, mode="sandbox", seconds=0, actionLimit=0,
                       guidance="guided" if mode == "independent" else "independent", mission=f"workday-debug-{mode}", seed=7)
        self.assertIn(options["mission"], [m["id"] for m in engine.call("lab-catalog")["missions"]])
        state = engine.call("lab-start", **options)
        self.assertIs(state["showGuide"], mode == "guided")
        initial_total = state["scenarioLength"]

        def gesture(value, text=""):
            nonlocal state
            keys = [{"Ctrl":"LCtrl", "Alt":"LAlt", "Shift":"LShift", "Super":"LSuper"}.get(k, k)
                    for k in value.split("+")]
            for key in keys:
                state = engine.call("lab-key", key=key, pressed=True, text=text if key == keys[-1] else "")
            for key in reversed(keys):
                state = engine.call("lab-key", key=key, pressed=False)
            return state

        def type_text(value):
            for char in value:
                gesture("Space" if char == " " else char.upper() if char.isascii() and char.isalpha() else "A", char)

        def step(value, text=""):
            before = state["solved"]
            gesture(value, text)
            self.assertEqual(state["solved"], before + 1, (value, state["task"], state["status"]))
            if mode == "independent":
                self.assertFalse(state["showGuide"])

        def restart():
            nonlocal engine, state
            old = state
            engine.call("lab-pause")
            self.assertIsNone(engine.storage_error)
            state = engine.call("lab-resume")
            # A force-kill must recover the newest committed snapshot, not an
            # edit made after it which did not complete a credited action.
            before = state["solved"]
            type_text("z")
            self.assertEqual(state["solved"], before)
            prior = next(w for w in old["scene"]["windows"] if w["active"])
            if prior["app"] == "code":
                self.assertNotEqual(active("code")["body"], prior["body"])
            else:
                self.assertNotEqual(state["scene"]["terminalInput"], old["scene"]["terminalInput"])
            engine.process.kill()
            engine.close()
            engine = open_engine()
            engine.call("configure", layout="EN", dock=["chrome", "code", "terminal", "files"])
            state = engine.call("lab-start", **dict(options, guidance=mode, **{"continue": True}))
            self.assertEqual(state["solved"], old["solved"])
            self.assertTrue(state["paused"])
            state = engine.call("lab-resume")
            return old

        def active(app):
            return next(w for w in state["scene"]["windows"] if w["active"] and w["app"] == app)

        def no_credit(action):
            before = state["solved"]
            action()
            self.assertEqual(state["solved"], before, state["status"])

        def run_test():
            type_text("python3 -m unittest " + case["test"])
            gesture("Enter")

        self.assertFalse(state["scene"]["terminal"]["lastSuccess"])
        self.assertTrue(any("AssertionError" in row and case["expected"] in row
                            for row in state["scene"]["terminal"]["output"]))
        state = engine.call("lab-resume")
        step("Super+2")
        step("Ctrl+Shift+F")
        no_credit(lambda: gesture("Esc"))
        no_credit(lambda: gesture("Ctrl+Shift+F"))
        type_text(case["call"])
        step("Enter")
        step("Enter")
        window = active("code")
        self.assertTrue(window["tabs"][window["activeTab"]]["url"].endswith(case["caller"]))
        # Wrong F2 can open a rename editor, but cannot satisfy Go to Definition.
        no_credit(lambda: gesture("F2"))
        no_credit(lambda: gesture("Esc"))
        step("F12")
        window = active("code")
        self.assertTrue(window["tabs"][window["activeTab"]]["url"].endswith(case["module"]))
        self.assertIn(case["incorrect"], window["body"])
        step("Super+1")
        step("Ctrl+F")
        type_text(case["doc"])
        step("Enter")
        self.assertEqual(active("chrome")["find"], case["doc"])
        step("Esc")
        step("Super+2")
        self.assertIn(case["incorrect"], active("code")["body"])
        step("Down")
        step("Ctrl+L")
        type_text(case["corrected"][:-2])
        if restart_midway:
            gesture("Shift+Left")
            before_restart = active("code")
            self.assertNotEqual(before_restart["bodyCursor"], before_restart["bodyAnchor"])
            restart()
            for field in ("activeTab", "bodyCursor", "bodyAnchor", "body", "focus"):
                self.assertEqual(active("code")[field], before_restart[field], field)
            self.assertEqual([{k: v for k, v in t.items() if k != "id"} for t in active("code")["tabs"]],
                             [{k: v for k, v in t.items() if k != "id"} for t in before_restart["tabs"]])
            gesture("End")
        type_text(case["corrected"][-2:])
        step("Enter")
        self.assertIn(case["corrected"], active("code")["body"])
        self.assertIn(case["incorrect"], active("code")["tabs"][active("code")["activeTab"]]["savedBody"])
        # Unsaved fix must not pass the simulated test.
        no_credit(lambda: gesture("Ctrl+Alt+T"))
        no_credit(run_test)
        self.assertFalse(state["scene"]["terminal"]["lastSuccess"])
        no_credit(lambda: gesture("Super+2"))
        step("Ctrl+S")
        step("Ctrl+Alt+T")
        before = state["solved"]
        run_test()
        self.assertEqual(state["solved"], before + 1)
        self.assertTrue(state["scene"]["terminal"]["lastSuccess"])
        self.assertTrue(any("OK" in row for row in state["scene"]["terminal"]["output"]))
        # A valid image supplied for the wrong active application cannot earn credit.
        no_credit(lambda: gesture("Super+1"))
        self.assertEqual(active("chrome")["app"], "chrome")
        no_credit(lambda: gesture("Alt+Print", png_fixture()))
        no_credit(lambda: gesture("Ctrl+Alt+T"))
        # A generic desktop screenshot isn't the evidence this route requires.
        no_credit(lambda: gesture("Print"))
        no_credit(lambda: gesture("Esc"))
        no_credit(lambda: gesture("Alt+Print"))
        step("Alt+Print", png_fixture())
        self.assertEqual(state["scene"]["clipboardMime"], "image/png")
        step("Super+2")
        step("Ctrl+P")
        self.assertTrue(active("code")["entries"][active("code")["optionIndex"]]["path"].endswith(case["report"]))
        step("Enter")
        step("Ctrl+End")
        step("Ctrl+V")
        self.assertIn("](evidence.png)", active("code")["body"])
        step("Ctrl+S")
        step("Ctrl+Alt+T")
        type_text("cat " + case["report"][:-2])
        if restart_midway:
            gesture("Left")
            before_restart = active("terminal")
            self.assertEqual(state["scene"]["terminalInput"], "cat " + case["report"][:-2])
            self.assertEqual(before_restart["cursor"], len(state["scene"]["terminalInput"]) - 1)
            old = restart()
            self.assertEqual(state["scene"]["terminal"], old["scene"]["terminal"])
            self.assertEqual(state["scene"]["terminalInput"], old["scene"]["terminalInput"])
            for field in ("cursor", "anchor", "focus"):
                self.assertEqual(active("terminal")[field], before_restart[field], field)
            gesture("End")
        type_text(case["report"][-2:])
        step("Enter")
        self.assertEqual(state["solved"], initial_total)
        self.assertTrue(state["finished"] and state["saved"])
        files = {f["path"]: f["content"] for f in state["scene"]["terminal"]["files"]}
        report = next(k for k in files if k.endswith("/" + case["report"]))
        self.assertIn("](evidence.png)", files[report])
        self.assertTrue(files[report.rsplit("/",1)[0] + "/evidence.png"].startswith("data:image/png;base64,iVBORw0KGgo"))
        self.assertTrue(engine.call("lab-result")["completed"])
        self.assertNotIn(options["mission"], [draft["mission"] for draft in engine.call("lab-catalog")["drafts"]])
        def counts(client):
            skills = client.call("configure", layout="EN")["liveSkills"]
            return {row["key"]: row["count"] for row in skills}

        credits = counts(engine)
        self.assertTrue(credits)
        # A fresh process sees exactly the same credits after the completed attempt.
        reopened = Engine(verify=True)
        try:
            self.assertEqual(counts(reopened), credits)
            self.assertNotIn(options["mission"], [draft["mission"] for draft in reopened.call("lab-catalog")["drafts"]])
        finally:
            reopened.close()
        return credits


def exercise_native(self, mode, chord, capture, output):
    def type_text(value):
        for char in value:
            key = {" ": "Space", "(": "Shift+9", ")": "Shift+0", "_": "Shift+-", "*": "Shift+8"}.get(char, char.upper())
            if char.isupper() and char.isalpha():
                key = "Shift+" + key
            yield from chord(key)
    for _ in range(4):
        yield from chord('A')
        if self.layout == 'EN' and getattr(self, 'last_character', '').lower() == 'a':
            break
        yield from chord('Super+Space')
        for _ in range(30):
            yield
    else:
        raise AssertionError("Could not verify native EN input")
    lab = self.lab
    if lab.active:
        assert lab.stop(), lab.status.get_label()
    self.navigate('lab')
    lab = self.lab

    def state():
        return lab.state
    def step(key):
        before=state()['solved']
        yield from chord(key)
        assert state()['solved']==before+1, (key,state()['task'],state()['status'])
    lab.picker.set_selected(next(i for i,c in enumerate(lab.classes) if c['id'] == 'workday'))
    lab.level.set_selected(1)
    lab.limit.set_selected(0)
    lab.time_limit.set_selected(0)
    lab.mission_picker.set_selected(next(i for i,(key,_) in enumerate(lab.choices) if key == 'workday-debug-' + mode))
    yield
    lab.start()
    yield from self.await_lesson_capture(chord)

    def open_app(app, launcher_text):
        dock = state()['scene']['dock']
        if app in dock:
            yield from step(f'Super+{dock.index(app) + 1}')
        else:
            before = state()['solved']
            yield from chord('Super')
            assert state()['scene']['overview']
            yield from type_text(launcher_text)
            yield from chord('Enter')
            assert state()['solved'] == before + 1, (app, state()['task'], state()['status'])
    case = CASES[mode]
    yield from open_app('code', 'code')
    yield from step('Ctrl+Shift+F')
    yield from type_text(case['call'])
    for key in ('Enter', 'Enter', 'F12'):
        yield from step(key)
    yield from open_app('chrome', 'chrome')
    yield from step('Ctrl+F')
    yield from type_text(case['doc'])
    for key in ('Enter', 'Esc'):
        yield from step(key)
    yield from open_app('code', 'code')
    for key in ('Down', 'Ctrl+L'):
        yield from step(key)
    yield from type_text(case['corrected'])
    for key in ('Enter','Ctrl+S','Ctrl+Alt+T'):
        yield from step(key)
    yield from type_text('python3 -m unittest '+case['test'])
    yield from step('Enter')
    capture(output / f'debug-{mode}-test-before-shot.png')
    yield
    yield from step('Alt+Print')
    assert state()['scene']['clipboardMime']=='image/png'
    yield from open_app('code', 'code')
    for key in ('Ctrl+P', 'Enter', 'Ctrl+End', 'Ctrl+V'):
        yield from step(key)
    files = {f['path']: f['content'] for f in state()['scene']['terminal']['files']}
    blob = next(v for k,v in files.items() if k.endswith('/evidence.png'))
    png = base64.b64decode(blob.split(',', 1)[1], validate=True)
    assert png[:8] == b'\x89PNG\r\n\x1a\n'
    width, height = struct.unpack('>II', png[16:24])
    assert width <= 680 and height <= 360, (width, height)
    (output / f'debug-{mode}-terminal.png').write_bytes(png)
    capture(output / f'debug-{mode}-report-with-evidence.png')
    yield
    for key in ('Ctrl+S','Ctrl+Alt+T'):
        yield from step(key)
    yield from type_text('cat '+case['report'])
    yield from step('Enter')
    assert lab.result, state()
    print(f'{mode}: native GTK passed through saved report')


if __name__ == "__main__":
    unittest.main()
