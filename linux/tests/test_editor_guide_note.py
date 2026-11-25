"""Prove the default VS Code card materially uses every captured fragment."""
import gzip
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import advance, begin
from test_project_file_practice import ROOT, active, chord, disk

NOTE = ROOT + "review-notes.txt"


def source_fragments(state):
    virtual = disk(state)
    def lines(filename, starts, number):
        src = virtual[ROOT + filename].splitlines(keepends=True)
        i = next(i for i, line in enumerate(src) if line.startswith(starts))
        return "".join(src[i:i + number])
    return (
        lines("storage.py", "def save_tasks(", 2),
        lines("tasks.py", "def add_task(", 1),
        lines("app.py", "from tasks import ", 1),
    )


class GuideNoteTests(unittest.TestCase):
    def use_profile(self, platform):
        profile = tempfile.TemporaryDirectory()
        context = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile.name})
        self.addCleanup(profile.cleanup)
        context.start()
        self.addCleanup(context.stop)
        self.profile = Path(profile.name)
        self.platform = platform
        self.e = self.engine_for_profile()
        self.addCleanup(lambda: self.e.close())

    def engine_for_profile(self):
        result = Engine(verify=True)
        result.call("configure", platform=self.platform, layout="EN")
        return result

    def reopen(self, state):
        old = self.e.call("lab-pause")
        self.e.close()
        self.e = self.engine_for_profile()
        result = begin(self.e, **{"continue": True})
        self.assertEqual((result["solved"], result["task"], result["scene"]["clipboard"]),
                         (old["solved"], state["task"], state["scene"]["clipboard"]))
        self.assertEqual(disk(result), disk(state))
        return result

    def through(self, state, n):
        while state["solved"] < n:
            state = advance(self.e, state)
        return state

    def check_course(self, platform):
        self.use_profile(platform)
        s = begin(self.e, editorCourse=True, seed=0)
        self.assertEqual((s["mission"], s["scenarioLength"]), ("code-guide", 33))
        chunks = source_fragments(s)
        original = {name: disk(s)[ROOT + name] for name in ("app.py", "storage.py", "tasks.py")}
        s = self.reopen(self.through(s, 8))
        self.assertEqual(s["scene"]["clipboard"], chunks[0])
        s = self.through(s, 10)
        self.assertEqual((active(s)["url"], active(s)["body"]), ("review-notes.txt", chunks[0]))
        self.assertNotIn(NOTE, disk(s))  # Ctrl+V alone doesn't fabricate a saved file.
        s = self.through(s, 17)
        self.assertEqual(s["scene"]["clipboard"], chunks[1])
        s = self.reopen(self.through(s, 21))
        self.assertEqual(disk(s)[NOTE], "".join(chunks[:2]))
        s = self.through(s, 24)
        self.assertEqual((active(s)["url"].split("/")[-1], s["scene"]["clipboard"]), ("app.py", chunks[2]))
        s = self.through(s, 25)
        self.assertEqual((active(s)["url"], active(s)["body"], active(s)["bodyCursor"]),
                         ("review-notes.txt", "".join(chunks[:2]), len("".join(chunks[:2]))))
        s = self.through(s, 32)
        self.assertEqual(disk(s)[NOTE], "".join(chunks))
        w = active(s)
        self.assertEqual((w["focus"], w["entries"][w["item"]]["path"]), ("explorer", NOTE))
        for name, content in original.items():
            self.assertEqual(disk(s)[ROOT + name], content)
        self.assertEqual([name for name in disk(s) if "notes-" in name or name.endswith("review-notes.txt")], [NOTE])
        s = self.through(s, 33)
        self.assertEqual((s["mission"], s["solved"], s["finished"]), ("code-notebook-read", 33, False))

    def test_ordinary_gtk_profile_transfers_all_three_fragments_and_resumes(self):
        self.check_course("linux")

    def test_ordinary_wpf_profile_transfers_all_three_fragments_and_resumes(self):
        self.check_course("windows")

    def test_blank_or_corrupt_note_cannot_finish_with_correct_key_alone(self):
        self.use_profile("linux")
        s = begin(self.e, editorCourse=True, seed=0)
        s = self.through(s, 9)
        before = s["solved"]
        # Detour to app.py, copy a neighboring import, and try to paste it where
        # the guide expects the actual storage fragment.
        chord(self.e, "Ctrl+Tab")
        chord(self.e, "Ctrl+L")
        s = chord(self.e, "Ctrl+C")
        self.assertTrue(s["scene"]["clipboard"].startswith("from tasks import"))
        chord(self.e, "Ctrl+Home")  # return to the original unselected entry position
        chord(self.e, "Ctrl+Shift+Tab")
        rejected = chord(self.e, "Ctrl+V")
        self.assertEqual(rejected["solved"], before)
        self.assertNotIn(NOTE, disk(rejected))
        chord(self.e, "Ctrl+Z")  # no hidden reset of the incorrect paste
        chord(self.e, "Ctrl+Shift+Tab")
        chord(self.e, "Ctrl+C")  # the two-line source selection is still there
        chord(self.e, "Ctrl+Tab")
        s = self.through(self.e.call("lab-state"), 31)
        saved = disk(s)[NOTE]
        # Correct Ctrl+1 in the right pane cannot hide a changed open document.
        s = chord(self.e, "X", "x")
        self.assertEqual(active(s)["body"], saved + "x")
        s = self.through(s, 32)
        s = chord(self.e, "Ctrl+1")
        self.assertEqual((s["mission"], s["solved"], disk(s)[NOTE]), ("code-guide", 32, saved))
        self.assertIn("Заметка не совпала", s["status"])
        chord(self.e, "Ctrl+Z")
        chord(self.e, "Ctrl+Shift+E")
        final = chord(self.e, "Ctrl+1")
        self.assertEqual((final["mission"], final["solved"]), ("code-notebook-read", 33))

    def test_old_guide_trace_continues_instead_of_replaying_into_new_steps(self):
        self.use_profile("linux")
        self.e.close()
        profile = self.profile / "TouchTypingStudio-QA" / "progress.xml"
        profile.parent.mkdir()
        fixture = Path(__file__).with_name("fixtures") / "preview7-original-guide.xml.gz"
        profile.write_bytes(gzip.decompress(fixture.read_bytes()))
        self.e = self.engine_for_profile()
        s = begin(self.e, **{"continue": True})
        self.assertEqual((s["solved"], s["task"]["id"], active(s)["url"]), (12, "close-tab", "notes-1.py"))
        self.assertIn(ROOT + "notes-1.py", disk(s))
        self.assertNotIn(NOTE, disk(s))
        s = self.through(s, 33)
        self.assertEqual((s["mission"], s["solved"]), ("code-notebook-read", 33))


if __name__ == "__main__":
    unittest.main()
