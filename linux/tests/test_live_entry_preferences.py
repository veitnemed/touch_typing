"""Durable aid independence and explicit new-versus-resume entry policy."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from platform_linux import Engine

class LiveEntryPreferencesTest(unittest.TestCase):
    def test_live_aids_do_not_inherit_typing_and_survive_restarts(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(
            os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}
        ):
            profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            profile.parent.mkdir()
            profile.write_text(
                "<StudioProfile><ShowKeys>false</ShowKeys><ShowHands>true</ShowHands></StudioProfile>",
                encoding="utf-8",
            )
            def aids(reply):
                return tuple(reply[name] for name in (
                    "showKeys", "showHands", "liveShowKeys", "liveShowHands"))
            engine = Engine(verify=True)
            try:
                self.assertEqual(aids(engine.call("library")), (False, True, False, False))
                self.assertEqual(aids(engine.call("settings", liveKeys=True)), (False, True, True, False))
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                self.assertEqual(aids(engine.call("library")), (False, True, True, False))
                self.assertEqual(aids(engine.call("settings", hands=False, liveHands=True)),
                                 (False, False, True, True))
                self.assertEqual(aids(engine.call("settings", liveKeys=False)), (False, False, False, True))
            finally:
                engine.close()
            engine = Engine(verify=True)
            try:
                self.assertEqual(aids(engine.call("library")), (False, False, False, True))
            finally:
                engine.close()

    def test_fresh_card_activity_does_not_discard_compatible_checkpoint(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(
            os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}
        ):
            profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            options = dict(app="code", mode="sandbox", level=0, guidance="guided",
                           seconds=0, seed=0, desktopProfile="ubuntu", preserveDraft=True)
            key_sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
            def advance(engine):
                state = engine.call("lab-resume")
                keys = [key_sides.get(key, key) for key in state["task"]["keys"].split("+")]
                for key in keys:
                    engine.call("lab-key", key=key, pressed=True)
                for key in reversed(keys):
                    after = engine.call("lab-key", key=key, pressed=False)
                self.assertGreater(after["solved"], state["solved"])
                return after
            def draft_and_history():
                stored = ET.parse(profile)
                drafts = stored.findall("./LiveDrafts/LiveDraft")
                self.assertEqual(len(drafts), 1)
                return ET.tostring(drafts[0]), len(stored.findall("./ShortcutHistory/ShortcutAttempt"))
            engine = Engine(verify=True)
            try:
                engine.call("lab-start", **options, mission="code-guide")
                step = advance(engine)["task"]["id"]
                engine.call("lab-stop")
                old_draft, old_history = draft_and_history()
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                fresh = engine.call("lab-start", **options, mission="code-first-fix")
                self.assertEqual(fresh["solved"], 0)
                self.assertEqual(fresh["mission"], "code-first-fix")
                advance(engine)
                engine.call("lab-stop")
                persisted_draft, persisted_history = draft_and_history()
                self.assertEqual(persisted_draft, old_draft)
                self.assertGreater(persisted_history, old_history)
            finally:
                engine.close()
            engine = Engine(verify=True)
            try:
                restored = engine.call("lab-start", **options, **{"continue": True})
                self.assertEqual((restored["mission"], restored["task"]["id"]), ("code-guide", step))
            finally:
                engine.close()

if __name__ == "__main__":
    unittest.main()
