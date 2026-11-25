"""Drive the same line protocol that GTK and WPF use, with an isolated profile per test."""
import os
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
from time import sleep
import xml.etree.ElementTree as ET

from platform_linux import Engine, EngineError


class ClassicCycles(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.profile = Path(tmp.name)
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": tmp.name})
        env.start()
        self.addCleanup(env.stop)
        self.start_engine()
        self.addCleanup(lambda: self.e.close())

    def start_engine(self):
        self.e = Engine(verify=True)
        self.e.call("configure", platform="linux", layout="EN")

    def key(self, text):
        return self.e.call("typing-key", text=text, layout="EN")

    def through(self, state, goal):
        """Type what is currently visible, never invoke an internal advance/complete hook."""
        while state["progressDone"] < goal:
            self.assertTrue(state["after"])
            char = state["after"][0]
            state = self.key("\b" if char == "⌫" else char)
        return state

    def test_ordinary_start_is_finite_and_ends_at_last_key(self):
        s = self.e.call("typing-start")
        self.assertEqual((s["mode"], s["cycles"], s["timeLimit"]), ("Russian", 1, 0))
        total = s["progressTotal"]
        self.assertGreaterEqual(total, 350)
        self.assertLessEqual(total, 550)
        self.assertEqual(s["cycleLengths"], [total])
        self.assertTrue(s["progressVisible"])
        before = s
        self.assertEqual(self.key("not a keystroke")["progressDone"], 0)
        self.assertEqual(self.key("!")["progressDone"], 0)
        s = self.through(before, total - 1)
        self.assertFalse(s["finished"])
        self.assertEqual(s["errors"], 1)
        end = self.through(s, total)
        self.assertEqual((end["progressDone"], end["progressFraction"], end["finished"],
                          end["running"], end["after"]), (total, 1, True, False, ""))
        self.assertEqual(self.key("!")["progressDone"], total)
        self.assertEqual(len(self.e.call("library")["typingHistory"]), 1)
        self.assertIsNone(self.e.call("library")["typing"]["draft"])

    def test_asymmetric_cycles_have_one_denominator_across_pause_and_restart(self):
        library = self.e.call("typing-save-set", title="Два отрывка", content="a" * 400 + "\n\n" + "b" * 500)
        tag = library["typing"]["sets"][-1]["id"]
        # The seeded verification profile starts with the 400-character passage.
        s = self.e.call("typing-start", mode=tag, cycles=2, seed=1)
        self.assertEqual(s["cycleLengths"], [400, 500])
        self.assertEqual(s["progressTotal"], 900)
        s = self.through(s, 399)
        self.assertEqual(self.key("x")["progressDone"], 399)
        s = self.through(s, 400)
        self.assertEqual((s["cycle"], s["progressDone"], s["progressTotal"], s["after"][0]), (2, 400, 900, "b"))
        paused = self.e.call("typing-pause")
        sleep(1.05)
        self.assertEqual(self.key("b")["progressDone"], 400)
        self.assertEqual(self.e.call("typing-state")["seconds"], paused["seconds"])
        item = self.e.call("library")["typing"]["draft"]
        self.assertEqual((item["id"], item["done"], item["total"]), (s["id"], 400, 900))
        self.e.close()
        self.start_engine()
        s = self.e.call("typing-continue", layout="EN")
        self.assertEqual((s["progressDone"], s["errors"], s["progressTotal"], s["id"]), (400, 1, 900, item["id"]))
        s = self.key("b")
        self.assertAlmostEqual(s["progressFraction"], 401 / 900)
        self.assertEqual(s["progressDone"], 401)
        self.e.call("typing-suspend")
        self.e.close()
        self.start_engine()
        s = self.e.call("typing-continue")
        end = self.through(s, 900)
        self.assertTrue(end["finished"])
        self.assertEqual(end["topMistakes"], [{"key": "a", "count": 1}])
        h = self.e.call("library")
        self.assertEqual(len(h["typingHistory"]), 1)
        self.assertEqual(h["typingHistory"][0]["count"], 900)
        self.assertIsNone(h["typing"]["draft"])
        with self.assertRaises(EngineError):
            self.e.call("typing-continue")

    def test_infinite_opt_in_has_no_ratio_or_false_completion(self):
        s = self.e.call("typing-start", mode="English", cycles=3, infinite=True)
        self.assertEqual((s["progressVisible"], s["progressTotal"], s["cycleLengths"], s["cycles"]),
                         (False, None, [], 0))
        s = self.through(s, 600)
        self.assertTrue(s["running"])
        self.assertFalse(s["finished"])
        self.e.call("typing-pause")
        self.assertIsNone(self.e.call("library")["typing"]["draft"])
        self.e.call("typing-resume")
        self.assertEqual(self.through(s, 601)["progressDone"], 601)
        self.e.call("typing-stop")
        self.assertEqual(self.e.call("library")["typingHistory"][0]["count"], 601)
        s = self.e.call("typing-start", mode="English")
        self.assertTrue(s["progressVisible"])
        self.assertLessEqual(s["progressTotal"], 550)

    def test_timer_finishes_without_marking_an_incomplete_plan_as_completed(self):
        s = self.e.call("typing-start", mode="English", seconds=1)
        self.key(s["after"][0])
        sleep(1.05)
        state = self.e.call("typing-state")
        self.assertTrue(state["finished"])
        self.assertLess(state["progressDone"], state["progressTotal"])
        rows = ET.parse(self.profile / "TouchTypingStudio-QA" / "progress.xml")
        typed = next(row for row in rows.findall("./Results/TypingResult")
                     if row.findtext("Activity") == "TypingPractice")
        self.assertEqual(typed.findtext("Completed"), "false")
        self.assertIsNone(self.e.call("library")["typing"]["draft"])

    def test_stopped_attempt_cannot_be_resumed_or_saved_twice(self):
        s = self.e.call("typing-start", mode="English")
        state = self.key(s["after"][0])
        self.e.call("typing-stop")
        self.assertFalse(self.e.call("typing-resume")["running"])
        self.assertEqual(self.key(state["after"][0])["progressDone"], 1)
        self.e.call("typing-stop")
        self.assertEqual(len(self.e.call("library")["typingHistory"]), 1)

    def test_two_processes_completed_draft_cannot_resurrect_after_old_window_closes(self):
        lib = self.e.call("typing-save-set", title="Граница", content="a" * 400 + "\n\n" + "b" * 500)
        tag = lib["typing"]["sets"][-1]["id"]
        start = self.e.call("typing-start", mode=tag, cycles=2, seed=1)
        self.through(start, 400)
        self.e.call("typing-pause")
        other = Engine(verify=True)
        self.addCleanup(other.close)
        s = other.call("typing-continue", id=start["id"])
        self.assertEqual((s["progressDone"], s["progressTotal"]), (400, 900))
        for n in range(401, 901):
            s = other.call("typing-key", text="b")
            self.assertEqual(s["progressDone"], n)
        self.assertTrue(s["finished"])
        self.assertIsNone(other.call("library")["typing"]["draft"])
        # A paused first window is still at 400/900. Its explicit Close followed
        # by engine EOF must not bring back the now-completed draft.
        self.e.call("typing-suspend")
        self.e.close()
        self.start_engine()
        cat = self.e.call("library")
        self.assertIsNone(cat["typing"]["draft"])
        self.assertEqual([r["count"] for r in cat["typingHistory"]], [900])
        with self.assertRaises(EngineError):
            self.e.call("typing-continue", id=start["id"])

    def test_stale_card_or_diverged_window_cannot_overwrite_newer_draft(self):
        s = self.e.call("typing-start", mode="English", seed=2)
        s = self.key(s["after"][0])
        self.e.call("typing-pause")
        other = Engine(verify=True)
        self.addCleanup(other.close)
        # If the user's card became stale, never substitute a different session.
        with self.assertRaisesRegex(EngineError, "черновик"):
            other.call("typing-continue", id="not-the-selected-id")
        r = other.call("typing-continue", id=s["id"])
        r = other.call("typing-key", text=r["after"][0])
        other.call("typing-pause")
        self.assertEqual(r["progressDone"], 2)
        self.e.call("typing-resume")
        self.key(s["after"][0])
        self.key(s["after"][1])
        self.e.call("typing-pause")
        self.assertIn("другом окне", self.e.storage_error)
        # A second normal Close can discard the obsolete view after the notice.
        self.e.call("typing-suspend")
        self.assertIsNone(self.e.storage_error)
        self.e.close()
        self.start_engine()
        resumed = self.e.call("typing-continue", id=r["id"])
        self.assertEqual((resumed["progressDone"], resumed["after"]), (2, r["after"]))
        self.assertEqual(len(self.e.call("library")["typingHistory"]), 0)

    def test_named_modes_generate_bounded_plans(self):
        cat = self.e.call("library")["typing"]
        for mode in ("Russian", "English", "WordsRU", "WordsEN", "Symbols", "Project",
                     cat["lessons"][0]["id"], cat["projects"][0]["id"]):
            for difficulty in (0, 2):
                with self.subTest(mode=mode, level=difficulty):
                    s = self.e.call("typing-start", mode=mode, level=difficulty, cycles=5)
                    self.assertEqual(len(s["cycleLengths"]), 5)
                    self.assertTrue(all(350 <= n <= 550 for n in s["cycleLengths"]))
                    self.assertEqual(s["progressTotal"], sum(s["cycleLengths"]))
                    self.assertTrue(s["running"])

    def test_corrupt_draft_is_not_treated_as_a_fresh_successful_continuation(self):
        s = self.e.call("typing-start", mode="English")
        self.key(s["after"][0])
        self.e.call("typing-suspend")
        self.e.close()
        file = self.profile / "TouchTypingStudio-QA" / "progress.xml"
        xml = ET.parse(file)
        xml.find("ClassicTypingDraft").text = '{"format":"classic-v1","id":"bad"}'
        xml.write(file, encoding="utf-8", xml_declaration=True)
        self.start_engine()
        self.assertIsNone(self.e.call("library")["typing"]["draft"])
        with self.assertRaises(EngineError):
            self.e.call("typing-continue")
        fresh = self.e.call("typing-start")
        self.assertEqual(fresh["progressDone"], 0)
        self.assertTrue(fresh["progressVisible"])


if __name__ == "__main__":
    unittest.main()
