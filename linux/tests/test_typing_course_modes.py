"""Exercise the shared course roster the GTK and WPF catalogs actually use."""
import gzip
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from platform_linux import Engine, EngineError
from test_registered_keyboard import SETUP, transitions

# Don't let deletion from the common roster quietly delete a user-visible route.
PREVIOUS_PRIMARY = {"Russian", "English", "WordsRU", "WordsEN", "Symbols", "Project",
                    "WeakRU", "WeakEN", "Digits", "TopRow", "Hotkeys", "WordsDigits", "WordsDigitsHotkeys"}


class CourseModes(unittest.TestCase):
    def open_engine(self):
        self.engine = Engine(verify=True)
        return self.engine.call("configure", platform=self.platform, layout=self.layout,
                                forward=SETUP[self.platform][0], backward=SETUP[self.platform][1])

    def temp_profile(self):
        root = TemporaryDirectory()
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root.name})
        return root, env

    def start(self, mode=None, **options):
        mode_arg = {} if mode is None else {"mode": mode}
        return self.engine.call("typing-start", **mode_arg, layout=self.layout, seed=13, **options)

    def advance(self, s):
        if s.get("expectedKind") == "command":
            def submit(operation, **args):
                return self.engine.call(operation, layout=s["wantedLayout"], **args)
            return transitions("typing-physical", s["expected"], submit)
        if s.get("expectedKind") == "text":
            expected = s["expected"]
            text = {"·": " ", "↵": "\n", "⇥": "\t"}.get(expected, expected)
        else:
            text = s["after"][0]
            if text == "⌫":
                text = "\b"
        self.assertEqual(len(text), 1, s["expected"])
        return self.engine.call("typing-key", text=text, layout=s["wantedLayout"])

    def teach_miss(self, mode):
        s = self.start(mode)
        miss = self.engine.call("typing-key", text="~", layout=s["wantedLayout"])
        self.assertEqual((miss["progressDone"], miss["errors"]), (0, 1))
        s = self.advance(s)
        self.assertEqual(s["progressDone"], 1)
        self.engine.call("typing-stop", layout=s["wantedLayout"])

    def test_roster_constraints_and_all_free_choices_in_both_client_configs(self):
        for self.platform in ("linux", "windows"):
            for self.layout in ("RU", "EN"):
                temp, env = self.temp_profile()
                with temp, env:
                    lib = self.open_engine()
                    try:
                        cat = lib["typing"]
                        modes = [m["id"] for m in cat["modes"]]
                        mixed = {m["id"] for m in cat["actionModes"]}
                        self.assertTrue(PREVIOUS_PRIMARY <= set(modes))
                        arts = [m["id"] for kind in ("lessons", "projects") for m in cat[kind]]
                        self.assertEqual(len(arts), len(set(arts)))
                        self.assertGreaterEqual(len(cat["lessons"]), 36)
                        self.assertGreaterEqual(len(cat["projects"]), 4)
                        self.assertEqual((lib["showKeys"], lib["showHands"], lib["liveShowKeys"], lib["liveShowHands"]),
                                         (False, False, False, False))
                        self.assertIsNone(cat["draft"])
                        first = self.start()
                        self.assertEqual((first["mode"], first["cycles"], first["timeLimit"], first["progressVisible"]),
                                         ("Russian", 1, 0, True))
                        for weak in ("WeakRU", "WeakEN"):
                            with self.assertRaisesRegex(EngineError, "с промахами"):
                                self.start(weak)
                        # The old weak modes are real personalized training, not
                        # invented content when a fresh user has no observations.
                        for prose in ("Russian", "English"):
                            self.teach_miss(prose)
                        for params in (dict(title="Заметка", content="study note\n\nвторая строка"),
                                       dict(title="Символы", content="%?№#-_=/", symbols=True)):
                            cat = self.engine.call("typing-save-set", **params)["typing"]
                        sets = [m["id"] for m in cat["sets"]]
                        self.assertEqual(len(sets), 2)
                        for mode in modes + arts + sets:
                            with self.subTest(client=self.platform, layout=self.layout, mode=mode):
                                if mode == "SystemHotkeys":
                                    # Explicitly opt-in, one fixed-length course. Ordinary
                                    # finite/infinite options never claim systemic keys.
                                    with self.assertRaisesRegex(EngineError, "[Зз]ахват системных"):
                                        self.start(mode)
                                    run = self.start(mode, infinite=True, cycles=2, seconds=180, level=2, systemCapture=True)
                                    self.assertEqual((run["mode"], run["cycles"], run["timeLimit"], run["progressTotal"],
                                                      run["progressVisible"], run["typedActions"]), (mode, 1, 0, 36, True, True))
                                    keys = set()
                                    for n in range(1, 37):
                                        keys.add(run["expected"])
                                        run = self.advance(run)
                                        self.assertEqual(run["progressDone"], n)
                                    self.assertTrue(run["finished"])
                                    self.assertTrue({"Alt+Tab", "Super+Up", "Super+E" if self.platform == "windows"
                                                     else "Super+PageUp"} <= keys)
                                    self.assertNotIn("Super+H", keys)
                                    self.engine.call("typing-stop", layout=self.layout)
                                    continue
                                finite = self.start(mode, cycles=2, level=2)
                                self.assertEqual((finite["mode"], finite["cycles"], finite["progressDone"],
                                                  finite["progressVisible"]), (mode, 2, 0, True))
                                self.assertEqual(bool(finite.get("typedActions")), mode in mixed)
                                self.assertEqual(sum(finite["cycleLengths"]), finite["progressTotal"])
                                self.assertTrue(all(350 <= n <= 550 for n in finite["cycleLengths"]))
                                for n in range(1, 21 if mode in modes else 3):
                                    finite = self.advance(finite)
                                    self.assertEqual(finite["progressDone"], n)
                                self.engine.call("typing-stop", layout=self.layout)
                                infinite = self.start(mode, infinite=True)
                                self.assertEqual((infinite["mode"], infinite["cycles"], infinite["progressTotal"],
                                                  infinite["progressVisible"], infinite["cycleLengths"]), (mode, 0, None, False, []))
                                infinite = self.advance(infinite)
                                self.assertEqual(infinite["progressDone"], 1)
                                self.assertFalse(infinite["finished"])
                                self.engine.call("typing-stop", layout=self.layout)
                        print(f"Typing {self.platform}/{self.layout}: {len(modes)} primary, {len(arts)} authored "
                              f"and {len(sets)} custom routes, ordinary finite/infinite plus 36 captured system gestures.")
                    finally:
                        self.engine.close()

    def test_restart_at_cycle_boundary_preserves_input_and_user_preferences(self):
        for self.platform, self.layout, mode in (("linux", "RU", "Russian"), ("windows", "EN", "WordsDigitsHotkeys")):
            temp, env = self.temp_profile()
            with temp, env:
                self.open_engine()
                try:
                    options = self.engine.call("settings", keys=True, hands=False, liveKeys=False, liveHands=True)
                    self.assertEqual((options["showKeys"], options["showHands"], options["liveShowKeys"], options["liveShowHands"]),
                                     (True, False, False, True))
                    s = self.start(mode, cycles=2)
                    first, total = s["cycleLengths"][0], s["progressTotal"]
                    while s["progressDone"] < first:
                        s = self.advance(s)
                    self.assertEqual((s["cycle"], s["progressFraction"]), (2, first / total))
                    fields = ("id", "progressDone", "progressTotal", "cycleLengths", "after", "expected")
                    wanted = {key: s[key] for key in fields}
                    self.engine.call("typing-suspend", layout=self.layout)
                    self.engine.close()
                    reopened = self.open_engine()
                    self.assertEqual((reopened["showKeys"], reopened["showHands"], reopened["liveShowKeys"],
                                      reopened["liveShowHands"]), (True, False, False, True))
                    self.assertEqual(reopened["typing"]["draft"]["id"], wanted["id"])
                    restored = self.engine.call("typing-continue", id=wanted["id"], layout=self.layout)
                    self.assertEqual({key: restored[key] for key in fields}, wanted)
                    typed = self.advance(restored)
                    self.assertEqual(typed["progressFraction"], (first + 1) / total)
                finally:
                    self.engine.close()

    def test_bilingual_work_note_is_whole_varied_and_resumable_without_new_primary_card(self):
        mode = "lesson:work-note-bilingual"
        for self.platform, self.layout in (("linux", "RU"), ("windows", "EN")):
            temp, env = self.temp_profile()
            with self.subTest(platform=self.platform), temp, env:
                lib = self.open_engine()
                try:
                    primary = {item["id"] for item in lib["typing"]["modes"]}
                    self.assertTrue(PREVIOUS_PRIMARY <= primary)
                    self.assertNotIn(mode, primary)
                    rows = [item for item in lib["typing"]["lessons"] if item["id"] == mode]
                    self.assertEqual([(item["title"], item["language"]) for item in rows],
                                     [("Заметка коллеге · RU / EN", "EN / RU")])
                    notes = [self.start(mode, round=number)["after"] for number in range(6)]
                    self.assertEqual(len(set(notes)), 6)
                    for note in notes:
                        self.assertTrue(350 <= len(note) <= 550)
                        self.assertRegex(note, r"\b[0-9]{2}\.[0-9]{2}\b")
                        self.assertRegex(note, r"\b[0-9]{2}:[0-9]{2}\b")
                        self.assertRegex(note, r"\b[a-z]+/[a-z0-9_]+\.[a-z]+\b")
                        self.assertRegex(note, r"\b[a-z]+@example\.test\b")
                        self.assertTrue(note.splitlines()[0].startswith("Тема: "))
                        self.assertTrue(note.splitlines()[-1].isascii() and note.endswith("."))

                    s = self.start(mode, round=0, cycles=2)
                    self.assertEqual((s["after"], s["cycleLengths"], s["wantedLayout"]),
                                     (notes[0], [len(note) for note in notes[:2]], "RU"))
                    first, second = s["cycleLengths"]
                    wrong = self.engine.call("typing-key", text="T", layout="EN")
                    self.assertEqual((wrong["progressDone"], wrong["errors"]), (0, 1))
                    route = []
                    for i in range(first + second):
                        current = notes[0] if i < first else notes[1]
                        pos = i if i < first else i - first
                        self.assertEqual(s["after"], current[pos:])  # no truncation, extra blank, or duplicate note
                        wanted = s["wantedLayout"]
                        if not route or route[-1] != wanted:
                            route.append(wanted)
                        s = self.advance(s)
                        self.assertEqual(s["progressDone"], i + 1)
                        if i + 1 == first:
                            self.assertEqual((s["cycle"], s["after"], s["progressFraction"]),
                                             (2, notes[1], first / (first + second)))
                        if current[pos:pos + 6] == "pickup":
                            draft = {name: s[name] for name in ("id", "cycle", "progressDone", "progressTotal", "after")}
                            self.engine.call("typing-suspend", layout=wanted)
                            self.engine.close()
                            self.open_engine()
                            s = self.engine.call("typing-continue", id=draft["id"], layout=wanted)
                            self.assertEqual({name: s[name] for name in draft}, draft)
                    self.assertTrue(s["finished"])
                    self.assertIn(["RU", "EN", "RU"], [route[i:i+3] for i in range(len(route))])
                    self.assertEqual((s["progressDone"], s["errors"]), (first + second, 1))
                    self.assertNotIn(mode, self.engine.call("typing-records", cycles=1, layout=self.layout))
                    self.assertEqual(self.engine.call("typing-records", cycles=2, layout=self.layout)[mode]["band"], "green")
                    trial = self.start(mode, infinite=True)
                    text = trial["after"]
                    self.assertIn(text.removesuffix("\n"), notes)
                    self.assertFalse(trial["progressVisible"])
                    for _ in range(len(text)):
                        trial = self.advance(trial)
                    self.assertFalse(trial["finished"])
                    self.assertIn(trial["after"].removesuffix("\n"), notes)
                    self.assertNotEqual(trial["after"], text)
                finally:
                    self.engine.close()

    def test_checkpoint_from_previous_preview_engine(self):
        # Unmodified anonymized XML from an engine built at f905eb4 (preview.6):
        # settings changed, English 447 correct + 1 miss, WordsEN two-cycle
        # draft suspended after 23 symbols. Duration is from automated input.
        self.platform, self.layout = "linux", "EN"
        temp, env = self.temp_profile()
        with temp, env:
            fixture = Path(__file__).parent / "fixtures/preview6-words-draft.xml.gz"
            same = gzip.decompress(fixture.read_bytes())
            trail = Path(temp.name) / "TouchTypingStudio-QA" / "progress.xml"
            trail.parent.mkdir()
            trail.write_bytes(same)
            lib = self.open_engine()
            try:
                self.assertEqual(trail.read_bytes(), same)
                self.assertEqual((lib["showKeys"], lib["showHands"], lib["liveShowKeys"], lib["liveShowHands"]),
                                 (True, False, False, True))
                row = lib["typingHistory"][0]
                self.assertEqual((row["count"], row["errors"]), (447, 1))
                best = self.engine.call("typing-records")["English"]
                self.assertEqual((best["accuracy"], best["seconds"]), (100.0 * 447 / 448, row["seconds"]))
                draft = lib["typing"]["draft"]
                self.assertEqual((draft["mode"], draft["done"], draft["total"]), ("WordsEN", 23, 1026))
                s = self.engine.call("typing-continue", id=draft["id"], layout=self.layout)
                self.assertEqual((s["id"], s["after"], s["progressDone"], s["cycleLengths"]),
                                 (draft["id"], " appeared boat ", 23, [504, 522]))
                self.assertEqual(self.advance(s)["progressDone"], 24)
            finally:
                self.engine.close()


if __name__ == "__main__":
    unittest.main()
