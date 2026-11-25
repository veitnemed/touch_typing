"""Exercise card records through the same subprocess and profile as both desktops."""
import gzip
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from platform_linux import Engine


class ModeRecords(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.addCleanup(self._restore_env())
        self.file = self.root / "TouchTypingStudio-QA" / "progress.xml"
        self.engine = None
        self.addCleanup(self.close)

    def _restore_env(self):
        p = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": str(self.root)})
        p.start()
        return p.stop

    def close(self):
        if self.engine:
            self.engine.close()
            self.engine = None

    def start(self):
        self.close()
        self.engine = Engine(verify=True)

    def card(self, **config):
        return self.engine.call("typing-records", **config)

    def write(self, rows, old_summary=None):
        self.close()
        root = ET.Element("StudioProfile")
        results = ET.SubElement(root, "Results")
        for attrs, scope in rows:
            node = ET.SubElement(results, "TypingResult")
            for k, v in attrs.items():
                ET.SubElement(node, k).text = str(v)
            if scope is not None:
                conditions = ET.SubElement(node, "TypingConditions")
                for k, v in scope.items():
                    ET.SubElement(conditions, k).text = str(v)
        if old_summary:
            old = ET.SubElement(ET.SubElement(root, "PracticeRecords"), "PracticeRecord")
            for k, v in old_summary.items():
                ET.SubElement(old, k).text = str(v)
        self.file.parent.mkdir(parents=True, exist_ok=True)
        ET.ElementTree(root).write(self.file, encoding="utf-8", xml_declaration=True)
        self.start()

    @staticmethod
    def result(mode, count, errors, seconds, *, activity="TypingPractice", completed=True):
        result = dict(Activity=activity, Task=mode, Count=count, Errors=errors,
                      Completed=str(completed).lower())
        if seconds is not None:
            result["Seconds"] = seconds
        return result

    @staticmethod
    def default(**extra):
        return dict(Level=0, Cycles=1, TimerSeconds=0, Round=-1, Eligible="true") | extra

    def test_intervals_use_integer_counts_even_when_display_rounds_up(self):
        rows = [(self.result(k, count, errors, 17), self.default()) for k, count, errors in [
            ("under90", 89999, 10001), ("at90", 90, 10),
            ("under975", 97499, 2501), ("at975", 39, 1), ("at98", 49, 1)]]
        self.write(rows)
        pairs = self.card()
        self.assertEqual({k: p["band"] for k, p in pairs.items()}, {
            "under90": "red", "at90": "orange", "under975": "orange", "at975": "green", "at98": "green"})
        self.assertEqual(pairs["under975"]["accuracy"], 97.499)
        self.assertEqual(pairs["at98"]["accuracy"], 98)
        self.assertNotIn("unattempted", pairs)  # The client renders this row gray.

    def test_winner_not_two_independent_maxima(self):
        make = lambda n, e, s: self.result("English", n, e, s)
        self.write([
            (make(1, 0, 1) | {"Completed": "false"}, self.default()),
            (make(1, 0, .1), self.default(Eligible="false")),  # time expired before finite goal
            (make(1, 0, .1) | {"Activity": "TypingMistakeReview"}, self.default()),
            (make(9, 1, 1), self.default()),
            (make(49, 1, 65.25), self.default()), (make(98, 2, 14.5), self.default()),
            (make(19499, 501, 2), self.default()),
            (make(49, 1, None), self.default()),
            (self.result("Russian", 39, 1, None), self.default()),
        ])
        value = self.card()
        self.assertEqual((value["English"]["accuracy"], value["English"]["seconds"]), (98, 14.5))
        self.assertEqual(value["Russian"]["accuracy"], 97.5)
        self.assertIsNone(value["Russian"]["seconds"])

    def test_comparable_settings_not_global_best(self):
        self.write([(self.result("Hotkeys", 9, 1, 44), self.default(Layout="RU")),
                    (self.result("Hotkeys", 49, 1, 11), self.default(Layout="EN")),
                    (self.result("Hotkeys", 1, 0, 33), self.default(Cycles=2, Layout="RU")),
                    (self.result("Hotkeys", 4, 1, 22), self.default(Level=2, Layout="RU")),
                    (self.result("Hotkeys", 1, 0, 60), self.default(Cycles=0, TimerSeconds=60, Layout="RU"))])
        self.assertEqual(self.card(layout="RU")["Hotkeys"]["seconds"], 44)
        self.assertEqual(self.card(layout="EN")["Hotkeys"]["seconds"], 11)
        self.assertEqual(self.card(layout="RU", cycles=2)["Hotkeys"]["seconds"], 33)
        self.assertEqual(self.card(layout="RU", level=2)["Hotkeys"]["seconds"], 22)
        self.assertEqual(self.card(layout="RU", infinite=True, seconds=60)["Hotkeys"]["seconds"], 60)
        self.assertNotIn("Hotkeys", self.card(layout="RU", cycles=2, seconds=180))
        self.assertNotIn("Hotkeys", self.card(layout="RU", cycles=0))

    def test_legacy_is_a_labelled_fallback_from_completed_history_only(self):
        self.write([(self.result("Russian", 10, 0, 1, completed=False), None),
                    (self.result("Russian", 39, 1, 52.125, activity="LinuxTyping"), None),
                    (self.result("Russian", 78, 2, 50.5), None),
                    (self.result("Russian", 9, 1, 7), None),
                    (self.result("English", 49, 1, None), None),
                    (self.result("English", 1, 0, 3, activity="TypingMistakeReview"), None)],
                   {"Id": "linux-typing/Symbols", "Speed": 900, "Accuracy": 100})
        self.assertEqual(self.card()["Russian"], {"accuracy": 97.5, "seconds": 50.5, "band": "green", "legacy": True})
        self.assertEqual(self.card()["English"]["seconds"], None)
        self.assertNotIn("Symbols", self.card())
        self.assertNotIn("Russian", self.card(level=1))
        self.assertNotIn("Russian", self.card(cycles=2))
        self.assertNotIn("Russian", self.card(infinite=True))
        self.assertNotIn("Russian", self.card(seconds=60))

    def test_current_result_beats_an_unscoped_legacy_candidate(self):
        self.write([(self.result("English", 1, 0, None), None),
                    (self.result("English", 19, 1, 27), self.default())])
        value = self.card()["English"]
        self.assertEqual((value["accuracy"], value["seconds"], value["legacy"]), (95, 27, False))

    def test_genuine_170_engine_profile_is_read_without_modification(self):
        # An automated v1.7.0 (721a2ed) run through lesson:home-motion,
        # round 0, one incorrect key followed by all 150 correct characters.
        # No personal data. The recorded duration is not a human speed estimate.
        path = Path(__file__).parent / "fixtures/v1.7.0-typing-mode-result.xml.gz"
        original = gzip.decompress(path.read_bytes())
        self.file.parent.mkdir(parents=True)
        self.file.write_bytes(original)
        stored = ET.fromstring(original).find("./Results/TypingResult")
        self.start()
        key = stored.findtext("Task")
        self.assertEqual(self.card()[key], {
            "accuracy": 100.0 * 150 / 151, "seconds": float(stored.findtext("Seconds")),
            "legacy": True, "band": "green",
        })
        self.assertEqual(self.file.read_bytes(), original)

    def finish(self, state):
        for _ in range(state["progressTotal"] - state["progressDone"]):
            c = state["after"][0]
            state = self.engine.call("typing-key", text="\b" if c == "⌫" else c)
        self.assertTrue(state["finished"])
        return state

    def stored_result(self, mode):
        return next(row for row in reversed(ET.parse(self.file).findall("./Results/TypingResult"))
                    if row.findtext("Task") == mode and row.findtext("Activity") == "TypingPractice")

    def test_finished_goal_supplies_exact_measured_result_after_restart(self):
        self.start()
        mode = "English"
        self.engine.call("typing-start", mode=mode, seed=7)
        self.engine.call("typing-key", text="~")  # a miss anywhere in English prose
        self.engine.call("typing-stop")
        self.assertNotIn(mode, self.card())
        state = self.engine.call("typing-start", mode=mode, seed=7, cycles=2, level=1)
        state = self.engine.call("typing-key", text="~")
        self.assertEqual(state["errors"], 1)
        state = self.finish(state)
        row = self.stored_result(mode)
        duration = float(row.findtext("Seconds"))
        self.assertGreater(duration, 0)
        self.assertEqual((row.findtext("Completed"), row.findtext("TypingConditions/Eligible")), ("true", "true"))
        self.assertEqual(int(row.findtext("Count")), state["progressTotal"])
        self.assertEqual(int(row.findtext("Errors")), 1)
        self.start()
        self.assertNotIn(mode, self.card())
        self.assertEqual(self.card(cycles=2, level=1)[mode], {
            "band": "green", "legacy": False, "accuracy": 100.0 * state["count"] / (1 + state["count"]),
            "seconds": duration})

    def test_custom_set_revision_remains_attached_to_restarted_draft(self):
        self.start()
        original = "Sample record. " * 6
        changed = "Other portion. " * 6
        library = self.engine.call("typing-save-set", title="Мой набор", content=original)
        mode = library["typing"]["sets"][0]["id"]
        params = dict(id=mode.removeprefix("set:"), title="Новое имя")
        state = self.engine.call("typing-start", mode=mode)
        self.engine.call("typing-key", text=state["after"][0])
        self.engine.call("typing-suspend")
        self.start()
        self.engine.call("typing-save-set", **params, content=changed)
        resumed = self.engine.call("typing-continue")
        self.assertEqual(resumed["id"], state["id"])
        self.finish(resumed)
        self.assertNotIn(mode, self.card())  # completed old material ≠ new material
        self.engine.call("typing-save-set", **params, content=original)
        self.assertEqual(self.card()[mode]["accuracy"], 100)  # same material, new title

    def test_historical_winner_survives_bounded_history_rotation(self):
        old = self.result("English", 39, 1, 15.25)
        self.write([(old, None)] + [(self.result("ignored", 1, 0, 1, activity="Other"), None)] * 499)
        # Trigger the normal persistence/rotation path. Stopping does not create a
        # record for the handful of keys we actually typed.
        state = self.engine.call("typing-start", mode="Symbols")
        self.engine.call("typing-key", text=state["after"][0])
        self.engine.call("typing-stop")
        self.assertFalse(any(r.findtext("Task") == "English" for r in ET.parse(self.file).findall("./Results/TypingResult")))
        self.start()
        self.assertEqual(self.card()["English"], {"accuracy": 97.5, "seconds": 15.25,
                                                  "band": "green", "legacy": True})
        self.assertNotIn("Symbols", self.card())

    def test_clean_profile(self):
        self.start()
        self.assertEqual(self.card(), {})


if __name__ == "__main__":
    unittest.main()
