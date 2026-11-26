"""The step must name the object on screen, not merely ask for 'the selected one'."""
import os
import tempfile
import unittest
from unittest.mock import patch
from platform_linux import Engine
from test_editor_course import begin,advance
from test_project_file_practice import active

class EditorObjectiveNames(unittest.TestCase):
    def test_real_objects_are_named_at_their_selection_points(self):
        checks={
            "code-project-file-move":(0,{"file-name-start","file-open"}),
            "code-rename-symbol":(0,{"code-reference-next","code-reference-open"}),
            "code-search-error":(0,{"project-search-next","project-search-open"}),
            "code-diagnostic-test":(1,{"code-fix-next","code-fix-apply"}),
            "code-guide":(0,{"symbol-open","quick-choose"}),
            "code-notebook-structure":(2,{"notebook-edit","notebook-delete-cell","notebook-down"}),
        }
        with tempfile.TemporaryDirectory() as profile, patch.dict(os.environ,{"TOUCH_TYPING_QA_DATA_ROOT":profile}):
            e=Engine(verify=True)
            try:
                e.call("configure",platform="linux",layout="EN")
                catalog={m["id"]:m for m in e.call("lab-catalog")["missions"] if m["app"]=="code"}
                for name,(_,required) in checks.items():
                    with self.subTest(mission=name):
                        state=begin(e,level=catalog[name]["level"],mission=name,seed=2,routeKind="mission",preserveDraft=False)
                        seen=set()
                        while not state["finished"]:
                            t,w=state["task"],active(state)
                            id=t["id"]
                            if id in required:
                                seen.add(id)
                                if id.startswith("notebook-"):
                                    nb=w["notebook"]; index=nb["selected"]+int(id=="notebook-down")
                                    wanted="№"+str(index+1)
                                elif id in ("file-name-start","file-open"):
                                    wanted=w["entries"][w["item"]]["title"]
                                else:
                                    entries=w["entries"]; index=(w["optionIndex"]+id.endswith("-next"))%len(entries)
                                    wanted=entries[index]["title"]
                                    if id=="quick-choose": wanted=entries[index]["path"].split("/")[-1]
                                self.assertIn(wanted,t["title"],(name,t,wanted))
                            state=advance(e,state)
                        self.assertEqual(seen,required)
            finally:e.close()
if __name__=="__main__":unittest.main()
