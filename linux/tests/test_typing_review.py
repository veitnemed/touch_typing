"""Real shared-engine requests used by both native clients; not a GUI/keyboard test."""
from collections import Counter
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from platform_linux import Engine, EngineError


class TypingReviewProtocol(unittest.TestCase):
    def setUp(self):
        remote = tempfile.TemporaryDirectory()
        self.addCleanup(remote.cleanup)
        self.root = Path(remote.name)
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": str(self.root)})
        env.start()
        self.addCleanup(env.stop)
        self.eng = Engine(verify=True)
        self.eng.call("configure", layout="EN", platform="linux")
        self.addCleanup(lambda: self.eng.close())

    def result(self):
        return self.eng.call("typing-result", layout="EN")["state"]

    def start_review(self, id):
        return self.eng.call("typing-review-start", sourceId=id, layout="EN")

    def xml(self):
        return ET.parse(self.root / "TouchTypingStudio-QA" / "progress.xml")

    def ticket(self, id):
        return next(x for x in self.xml().findall("./TypingReviews/TypingReviewTicket") if x.findtext("SourceId") == id)

    def reopen(self):
        self.eng.close()
        self.eng = Engine(verify=True)
        return self.eng.call("configure", layout="EN", platform="linux")

    def main(self, first, second=None, errors=None, chars=400):
        # Fixed authored blocks in a real custom set, as made in the parameter panel.
        # Use the ordinary source protocol; no fake finish or injection of a result row.
        original = self.eng.call("typing-save-set", title="Один исходный результат",
                                 content=first * chars + (("\n\n" + second * 500) if second else ""))
        mode = original["typing"]["sets"][-1]["id"]
        state = self.eng.call("typing-start", mode=mode, seed=1, cycles=2 if second else 1)
        if second:
            self.assertEqual(state["cycleLengths"], [chars, 500])
        schedule = dict(errors or {})
        while not state["finished"]:
            at = state["progressDone"]
            for _ in range(schedule.pop(at, 0)):
                wrong = "x" if state["after"][0] != "x" else "q"
                state = self.eng.call("typing-key", text=wrong)
                self.assertEqual(at, state["progressDone"])
            state = self.eng.call("typing-key", text=state["after"][0])
        self.assertFalse(schedule)
        self.eng.call("typing-stop")
        return self.result()

    def physical(self, keys):
        for key in keys:
            self.eng.call("typing-physical", key=key, pressed=True)
        for key in reversed(keys):
            state = self.eng.call("typing-physical", key=key, pressed=False)
        return state

    def test_weighted_scope_all_cycles_exact_counts_restart_and_single_result(self):
        # 1st cycle: ж 20 misses. 2nd: 7 ten and % six misses (full mistakes, not only last fragment).
        original = self.eng.call("typing-save-set", title="ж, 7, %: два цикла",
                                 content="ж" * 400 + "\n\n" + "7%" * 250)
        state = self.eng.call("typing-start", mode=original["typing"]["sets"][-1]["id"], seed=1, cycles=2)
        self.assertEqual(state["cycleLengths"], [400, 500])
        hits = {0: ("ж", 20), 400: ("7", 10), 401: ("%", 6)}
        while not state["finished"]:
            expected = state["after"][0]
            if state["progressDone"] in hits:
                char, n = hits.pop(state["progressDone"])
                self.assertEqual(char, expected)
                for _ in range(n):
                    self.eng.call("typing-key", text="!")
            state = self.eng.call("typing-key", text=expected)
        source = self.result()
        self.assertTrue(source["reviewAvailable"])
        self.assertEqual(source["errors"], 36)
        id = source["attemptId"]
        self.assertEqual(self.result()["attemptId"], id)
        self.assertTrue(self.eng.call("typing-history-result", sourceId=id)["state"]["reviewAvailable"])
        self.assertEqual(self.eng.call("library")["typing"]["reviewDrafts"], [])
        start = self.start_review(id)
        self.assertTrue(start["isReview"])
        self.assertFalse(start["reviewAvailable"])
        self.assertLessEqual(150, start["progressTotal"])
        self.assertLessEqual(start["progressTotal"], 200)
        frozen = self.ticket(id).findtext("Plan")
        units = json.loads(frozen)["units"]
        frequencies = Counter(a["value"] for a in units)
        self.assertEqual(set(frequencies), {"ж", "7", "%"})
        self.assertGreater(frequencies["ж"], frequencies["7"])
        self.assertGreater(frequencies["7"], frequencies["%"])
        self.assertEqual(self.start_review(id)["progressDone"], 0)  # double activation
        for atom in units[:23]:
            state = self.eng.call("typing-key", text=atom["value"])
        self.eng.call("typing-key", text="!")
        self.assertEqual(self.eng.call("typing-pause")["progressDone"], 23)
        library = self.reopen()
        self.assertEqual(library["typing"]["reviewDrafts"][0]["sourceId"], id)
        restored = self.start_review(id)
        self.assertEqual((restored["progressDone"], restored["errors"]), (23, 1))
        self.assertEqual(self.ticket(id).findtext("Plan"), frozen)
        for atom in units[23:]:
            state = self.eng.call("typing-key", text=atom["value"])
        self.assertTrue(state["finished"])
        for _ in range(2):
            self.eng.call("typing-stop")
            self.assertTrue(self.result()["isReview"])
            self.assertFalse(self.result()["reviewAvailable"])
        self.reopen()
        with self.assertRaises(EngineError):
            self.start_review(id)
        self.assertEqual(self.eng.call("library")["typing"]["reviewDrafts"], [])
        history = self.eng.call("typing-history-result", sourceId=id)["state"]
        self.assertTrue(history["isHistory"])
        self.assertFalse(history["reviewAvailable"])
        self.assertEqual(history["topMistakes"][0], {"key": "ж", "count": 20})
        matches = [x for x in self.xml().findall("./Results/TypingResult") if x.findtext("ReviewOf") == id]
        self.assertEqual(len(matches), 1)
        self.assertEqual((matches[0].findtext("Completed"), matches[0].findtext("Errors")), ("true", "1"))
        self.assertEqual(matches[0].findtext("Activity"), "TypingMistakeReview")
        # The correction (not just the original exercise) is independently
        # openable after restart, and this is a strictly read-only operation.
        review_id = "review/" + id
        ledger = self.root / "TouchTypingStudio-QA" / "progress.xml"
        before = ledger.read_bytes()
        entry = next(r for r in self.eng.call("library")["typingHistory"] if r["sourceId"] == review_id)
        first = self.eng.call("typing-history-result", sourceId=review_id)["state"]
        second = self.eng.call("typing-history-result", sourceId=review_id)["state"]
        self.assertEqual(first, second)
        self.assertEqual((first["count"], first["errors"], first["accuracy"], first["attemptId"]),
                         (entry["count"], entry["errors"], entry["accuracy"], review_id))
        self.assertEqual((first["isReview"], first["isHistory"], first["finished"], first["reviewAvailable"]),
                         (True, True, True, False))
        self.assertEqual(sum(m["count"] for m in first["topMistakes"]), 1)
        with self.assertRaises(EngineError):
            self.start_review(review_id)
        self.assertEqual(ledger.read_bytes(), before)
        # Only source-specific mistakes: a second attempt's sole 'a' must not be diluted by these errors.
        other = self.main("a", errors={0:25})
        self.assertNotEqual(other["attemptId"], id)
        self.start_review(other["attemptId"])
        other_plan = json.loads(self.ticket(other["attemptId"]).findtext("Plan"))["units"]
        self.assertEqual({a["value"] for a in other_plan}, {"a"})

    def test_true_threshold_97_5_but_not_exact_98_or_abandoned(self):
        # 390/(390+10)=97.5; 392/(392+8)=98 with finite, pre-planned lengths.
        src = self.main("e", errors={0:10}, chars=390)
        self.assertEqual(src["accuracy"], 97.5)
        self.assertTrue(src["reviewAvailable"])
        equal = self.main("e", errors={0:8}, chars=392)
        self.assertEqual(equal["accuracy"], 98.0)
        self.assertFalse(equal["reviewAvailable"])
        with self.assertRaises(EngineError):
            self.start_review(equal["attemptId"])
        state = self.eng.call("typing-start", mode="English", infinite=True)
        self.eng.call("typing-key", text="ж")
        self.eng.call("typing-key", text=state["after"][0])
        self.eng.call("typing-stop")
        self.assertFalse(self.result()["reviewAvailable"])

    def test_equal_source_miss_weights_do_not_create_artificial_weakest_key(self):
        source = self.main("a", "b", errors={0: 20, 400: 20})
        self.assertTrue(source["reviewAvailable"])
        state = self.start_review(source["attemptId"])
        units = json.loads(self.ticket(source["attemptId"]).findtext("Plan"))["units"]
        counts = Counter(u["value"] for u in units)
        self.assertEqual(set(counts), {"a", "b"})
        self.assertEqual(sum(counts.values()), state["progressTotal"])
        self.assertLessEqual(abs(counts["a"] - counts["b"]), 1)

    def test_save_failure_is_retryable_without_a_second_completion(self):
        source = self.main("a", errors={0:20})
        id = source["attemptId"]
        state = self.start_review(id)
        total = state["progressTotal"]
        for _ in range(total - 1):
            self.eng.call("typing-key", text="a")
        # A local directory cannot be overwritten as the profile's temporary XML.
        # This transient I/O fault is scoped exclusively to the disposable test profile.
        barrier = self.root / "TouchTypingStudio-QA" / "progress.xml.writing"
        barrier.mkdir()
        try:
            with self.assertRaises(EngineError):
                self.eng.call("typing-key", text="a")
            self.assertFalse(self.ticket(id).findtext("Completed") == "true")
        finally:
            barrier.rmdir()
        self.assertTrue(self.eng.call("typing-state")["finished"])
        self.assertFalse(self.result()["reviewAvailable"])
        self.assertEqual(self.ticket(id).findtext("Completed"), "true")
        self.assertEqual(sum(r.findtext("ReviewOf")==id for r in self.xml().findall("./Results/TypingResult")), 1)

    def test_a_failed_source_save_never_offers_a_phantom_correction(self):
        original = self.eng.call("typing-save-set", title="Durable source", content="a" * 390)
        state = self.eng.call("typing-start", mode=original["typing"]["sets"][-1]["id"])
        for _ in range(10):
            self.eng.call("typing-key", text="z")
        for _ in range(state["progressTotal"] - 1):
            self.eng.call("typing-key", text="a")
        barrier = self.root / "TouchTypingStudio-QA" / "progress.xml.writing"
        barrier.mkdir()
        try:
            self.eng.call("typing-key", text="a")
            with self.assertRaises(EngineError):
                self.result()
            self.assertFalse(self.xml().findall("./TypingReviews/TypingReviewTicket"))
        finally:
            barrier.rmdir()
        self.eng.call("typing-stop")
        self.assertTrue(self.result()["reviewAvailable"])

    def test_second_window_cannot_rewind_or_duplicate_a_review(self):
        source = self.main("a", errors={0:20})
        id = source["attemptId"]
        self.start_review(id)
        second = Engine(verify=True)
        try:
            second.call("typing-review-start", sourceId=id)
            for _ in range(3):
                self.eng.call("typing-key", text="a")
            self.eng.call("typing-pause")
            second.call("typing-key", text="a")
            with self.assertRaises(EngineError):
                second.call("typing-pause")
            self.assertEqual(self.ticket(id).findtext("Position"), "3")
            reloaded = second.call("typing-review-start", sourceId=id)
            self.assertEqual(reloaded["progressDone"], 3)
            while not reloaded["finished"]:
                reloaded = second.call("typing-key", text="a")
            self.eng.call("typing-resume")
            self.eng.call("typing-key", text="a")
            with self.assertRaises(EngineError):
                self.eng.call("typing-suspend")
            self.assertEqual(self.ticket(id).findtext("Completed"), "true")
            self.assertEqual(sum(r.findtext("ReviewOf")==id for r in self.xml().findall("./Results/TypingResult")), 1)
        finally:
            second.close()

    def test_only_legitimately_finished_timed_infinite_attempt_qualifies(self):
        for mode in ("English", "Digits"):
            with self.subTest(mode=mode):
                state = self.eng.call("typing-start", mode=mode, infinite=True, seconds=1)
                self.eng.call("typing-key", text="ж")
                self.eng.call("typing-key", text=state["after"][0])
                time.sleep(1.04)
                self.assertTrue(self.eng.call("typing-state")["finished"])
                self.assertTrue(self.result()["reviewAvailable"])

    def test_damaged_new_draft_never_breaks_existing_profile_or_silently_starts_over(self):
        src = self.main("a", errors={0:20})
        self.start_review(src["attemptId"])
        self.eng.close()
        path = self.root / "TouchTypingStudio-QA" / "progress.xml"
        doc = ET.parse(path)
        doc.find("./TypingReviews/TypingReviewTicket/Plan").text = '{"units":null,"groupEnds":[0]}'
        doc.write(path, encoding="utf-8", xml_declaration=True)
        self.eng = Engine(verify=True)
        library = self.eng.call("configure", layout="EN", platform="linux")
        self.assertFalse(library["typing"]["reviewDrafts"])
        self.assertEqual(len(library["typingHistory"]), 1)
        with self.assertRaises(EngineError):
            self.start_review(src["attemptId"])
        self.assertTrue(self.eng.call("typing-start", mode="English")["running"])

    def test_command_drill_never_accepts_command_name_as_text(self):
        s = self.eng.call("typing-start", mode="Hotkeys", level=0, layout="RU")
        inserted = False
        while not s["finished"]:
            if s["expected"] == "Ctrl+V" and not inserted:
                for _ in range(25):
                    self.physical(["F10"])
                inserted = True
            s = self.physical(s["expectedKeys"])
        self.assertTrue(inserted)
        src = self.result()
        self.assertTrue(src["reviewAvailable"])
        state = self.start_review(src["attemptId"])
        self.assertEqual((state["expectedKind"], state["expected"]), ("command", "Ctrl+V"))
        for literal in ("Ctrl+V", *"Ctrl+V"):
            self.assertEqual(self.eng.call("typing-key", text=literal)["progressDone"], 0)
        self.assertEqual(self.physical(["RCtrl", "V"])["progressDone"], 1)
        self.eng.call("typing-suspend")
        self.reopen()
        state = self.start_review(src["attemptId"])
        self.assertEqual((state["progressDone"], state["expected"]), (1, "Ctrl+V"))
        self.assertEqual(self.physical(["LCtrl", "V"])["progressDone"], 2)


if __name__ == "__main__":
    unittest.main()
