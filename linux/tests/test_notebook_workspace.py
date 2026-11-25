"""Edit and run the shared .ipynb model through the ordinary simulator protocol."""
import gzip
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_notebooks import advance, files, options, text
from test_python_editor import editor, gesture


class NotebookWorkspace(unittest.TestCase):
    def setUp(self):
        profile = tempfile.TemporaryDirectory()
        self.addCleanup(profile.cleanup)
        self.profile = Path(profile.name)
        env = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile.name})
        env.start()
        self.addCleanup(env.stop)
        self.e = Engine(verify=True)
        self.addCleanup(lambda: self.e.close())

    def open_existing(self, platform="linux"):
        self.e.call("configure", platform=platform, layout="EN")
        state = self.e.call("lab-start", **options(0, 0))
        self.e.call("lab-resume")
        for _ in range(2):
            state = advance(self.e, state)
        return state

    def key(self, chord, **kwargs):
        return gesture(self.e, chord, **kwargs)

    def pointer(self, action, cell=-1):
        return self.e.call("lab-notebook-action", action=action, cell=cell)

    def palette(self, query):
        self.key("Ctrl+Shift+P")
        text(self.e, query)
        return self.key("Enter")

    @staticmethod
    def nb(state):
        return editor(state)["notebook"]

    def test_structure_move_delete_and_restore_on_document_boundaries(self):
        original = self.nb(self.open_existing())
        cells = original["cells"]
        first_id, mid_id, last_id = [c["id"] for c in cells]
        # Up/Alt+Up on first and Down/Alt+Down on last never wrap.
        for chord in ("Up", "Alt+Up", "Delete", "D"):
            n = self.nb(self.key(chord))
            self.assertEqual([c["id"] for c in n["cells"]], [first_id, mid_id, last_id])
        deleted = self.nb(self.key("D"))
        self.assertEqual([c["id"] for c in deleted["cells"]], [mid_id, last_id])
        self.assertEqual(self.nb(self.key("Z"))["cells"], cells)
        n = self.nb(self.key("Down"))
        self.assertEqual(n["selected"], 1)
        n = self.nb(self.key("Alt+Down"))
        self.assertEqual([c["id"] for c in n["cells"]], [first_id, last_id, mid_id])
        self.assertEqual(self.nb(self.key("Alt+Down"))["cells"], n["cells"])
        self.key("D")
        self.assertEqual([c["id"] for c in self.nb(self.key("D"))["cells"]], [first_id, last_id])
        self.assertEqual(self.nb(self.key("Z"))["cells"], n["cells"])
        self.key("C")
        n = self.nb(self.key("V"))
        self.assertEqual(n["cells"][-1]["source"], cells[1]["source"])
        self.assertNotEqual(n["cells"][-1]["id"], mid_id)
        self.assertEqual(n["cells"][-1]["runs"], 0)

    def test_text_delete_leaves_cell_and_type_change_preserves_source(self):
        self.open_existing()
        n = self.nb(self.key("A"))
        self.assertEqual((n["selected"], n["cells"][0]["source"]), (0, ""))
        self.key("Enter")
        text(self.e, "draft")
        self.key("Home")
        n = self.nb(self.key("Delete"))
        self.assertEqual((len(n["cells"]), n["cells"][0]["source"], n["caret"]), (4, "raft", 0))
        s = self.key("Ctrl+Z")
        self.assertEqual(self.nb(s)["cells"][0]["source"], "draft")
        self.key("Esc")
        n = self.nb(self.key("M"))
        self.assertEqual((n["cells"][0]["kind"], n["cells"][0]["source"]), ("markdown", "draft"))
        n = self.nb(self.key("Y"))
        self.assertEqual((n["cells"][0]["kind"], n["cells"][0]["source"]), ("code", "draft"))
        n = self.nb(self.key("Z"))
        self.assertEqual(n["cells"][0]["kind"], "markdown")
        self.key("B")
        self.assertEqual(self.nb(self.key("Enter"))["editing"], True)

    def test_real_batch_interrupt_clear_and_kernel_restart(self):
        self.open_existing("windows")
        n = self.nb(self.pointer("run-all"))
        self.assertEqual(n["kernel"]["state"], "busy")
        n = self.nb(self.e.call("lab-state"))
        self.assertEqual((n["kernel"]["counter"], n["cells"][1]["runs"], n["cells"][2]["runs"]), (1, 1, 0))
        n = self.nb(self.pointer("interrupt"))
        self.assertEqual((n["kernel"]["state"], n["kernel"]["queued"]), ("interrupted", 0))
        n = self.nb(self.e.call("lab-state"))
        self.assertEqual(n["cells"][2]["runs"], 0)
        n = self.nb(self.palette("Run All Cells"))
        result = n["cells"][2]["output"]
        self.assertTrue(result.isdigit(), n)
        n = self.nb(self.palette("Restart Kernel"))
        self.assertEqual((n["kernel"]["generation"], n["kernel"]["counter"]), (2, 0))
        self.assertEqual((n["cells"][2]["output"], n["cells"][2]["outputLabel"]), (result, "Сохранённый вывод"))
        n = self.nb(self.pointer("run", 2))
        self.assertTrue(n["cells"][2]["failed"])
        self.assertIn("NameError", n["cells"][2]["traceback"])
        n = self.nb(self.palette("Clear All Outputs"))
        self.assertTrue(all(not c["output"] and not c["runs"] for c in n["cells"]))
        n = self.nb(self.palette("Run All Cells"))
        self.assertEqual(n["cells"][2]["output"], result)

    def test_create_save_and_reopen_named_ipynb(self):
        self.open_existing()
        state = self.palette("New Jupyter Notebook")
        n = self.nb(state)
        self.assertEqual((len(n["cells"]), n["cells"][0]["source"]), (1, ""))
        name = editor(state)["title"] if editor(state)["title"].endswith(".ipynb") else editor(state)["url"].split("/")[-1]
        self.assertNotIn(name, files(state))  # Unsaved really is unsaved.
        self.key("Enter")
        text(self.e, "x = [1, 2, 3]")
        state = self.key("Ctrl+S")
        self.assertIn(name, files(state))
        self.key("Ctrl+W")
        self.key("Ctrl+P")
        text(self.e, name)
        s = self.key("Enter")
        self.assertEqual(self.nb(s)["cells"][0]["source"], "x = [1, 2, 3]")

    def test_explorer_create_conflict_move_undo_and_exact_focus(self):
        state = self.open_existing()
        origin = editor(state)["url"]

        def by_path(state):
            return {f["path"]: f["content"] for f in state["scene"]["terminal"]["files"]}

        initial = by_path(state)[origin]
        root = origin.rsplit("/", 1)[0]
        state = self.palette("New Folder")
        self.assertEqual((editor(state)["focus"], editor(state)["renamePath"]), ("file-name", root + "/"))
        text(self.e, "archive.txt")
        state = self.key("Enter")
        self.assertEqual(editor(state)["focus"], "file-name")
        self.assertIn("занято", state["status"])
        self.key("Esc")
        self.assertEqual(by_path(self.e.call("lab-state"))[origin], initial)

        self.key("Ctrl+1")
        self.palette("New Folder")
        text(self.e, "verified")
        state = self.key("Enter")
        self.assertIn(root + "/verified", {entry["path"] for entry in editor(state)["explorerEntries"]})
        self.assertTrue(editor(state)["canUndoExplorer"])

        def file_action(action, path=""):
            return self.e.call("lab-explorer-action", action=action, path=path)

        file_action("cut", origin)
        state = file_action("paste", root + "/verified")
        moved = root + "/verified/" + origin.rsplit("/", 1)[1]
        self.assertEqual(editor(state)["url"], moved)
        self.assertNotIn(origin, by_path(state))
        self.assertEqual(by_path(state)[moved], initial)
        state = self.key("Ctrl+Z")
        self.assertEqual(editor(state)["url"], origin)
        self.assertNotIn(moved, by_path(state))
        self.assertEqual(by_path(state)[origin], initial)

        state = file_action("rename", origin)
        self.assertEqual(editor(state)["focus"], "file-name")
        text(self.e, "final")
        state = self.key("Enter")
        renamed = root + "/final.ipynb"
        self.assertEqual(by_path(state)[renamed], initial)
        state = file_action("open", renamed)
        self.assertEqual(editor(state)["focus"], "body")
        state = self.key("F2")  # No file rename from the editing surface.
        self.assertEqual(editor(state)["focus"], "body")
        self.assertEqual(by_path(state)[renamed], initial)
        self.assertGreater(len(self.nb(state)["cells"]), 0)

    def test_windows_case_only_rename_can_be_undone(self):
        state = self.open_existing("windows")
        origin = editor(state)["url"]
        self.e.call("lab-explorer-action", action="rename", path=origin)
        old_bare = origin.rsplit("/", 1)[-1].removesuffix(".ipynb")
        text(self.e, old_bare.upper())
        renamed = self.key("Enter")
        self.assertEqual(editor(renamed)["url"], origin.replace(old_bare, old_bare.upper()))
        undone = self.key("Ctrl+Z")
        self.assertEqual(editor(undone)["url"], origin)
        self.assertIsNotNone(editor(undone)["notebook"])

    def test_preview6_rejected_keys_stay_noops_when_replaying_an_old_draft(self):
        self.e.close()
        path = self.profile / "TouchTypingStudio-QA" / "progress.xml"
        path.parent.mkdir()
        fixture = Path(__file__).with_name("fixtures") / "f905eb4-notebook-commands.xml.gz"
        path.write_bytes(gzip.decompress(fixture.read_bytes()))
        self.e = Engine(verify=True)
        self.e.call("configure", platform="linux", layout="EN")
        state = self.e.call("lab-start", **(options(0, 0) | {"continue": True}))
        n = self.nb(state)
        self.assertEqual(state["solved"], 2)
        self.assertEqual([c["id"] for c in n["cells"]], ["brief", "dataset", "metric"])
        self.e.call("lab-resume")
        self.assertEqual(len(self.nb(self.key("A"))["cells"]), 4)  # Only the new event is actionable.

    def test_pointer_actions_are_replayed_but_cannot_earn_exact_key_credit(self):
        s = self.open_existing()
        self.assertEqual(s["task"]["id"], "notebook-down")
        old = s["solved"]
        s = self.pointer("select", 1)
        self.assertEqual(self.nb(s)["selected"], 1)
        self.assertEqual(s["solved"], old)
        s = self.pointer("edit", 1)
        self.assertTrue(self.nb(s)["editing"])
        self.e.call("lab-pause")
        self.e.close()
        self.e = Engine(verify=True)
        self.e.call("configure", platform="linux", layout="EN")
        s = self.e.call("lab-start", **(options(0, 0) | {"continue": True}))
        self.assertEqual((s["solved"], self.nb(s)["selected"], self.nb(s)["editing"]), (old, 1, True))


if __name__ == "__main__":
    unittest.main()
