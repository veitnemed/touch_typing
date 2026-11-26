"""Real engine processes; assert edited bytes, index snapshots, working tree and replay.
These are *not* tests of a physical Windows keyboard or of the WPF renderer.
"""
import os
from pathlib import PurePosixPath
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import begin, advance, enter
from test_project_file_practice import active, chord, disk

class EditorGitRelease(unittest.TestCase):
    def test_cases_edit_stage_restart_and_commit(self):
        for move in (False, True):
            mission = "code-publish-" + ("config" if move else "handoff")
            material = set()
            for host in ("ubuntu","windows"):
                for seed in range(3):
                    with self.subTest(mission=mission, desktop=host, seed=seed), tempfile.TemporaryDirectory() as root, patch.dict(
                            os.environ, {"TOUCH_TYPING_QA_DATA_ROOT":root}):
                        setup=dict(level=2 if move else 1, mission=mission, seed=seed, desktopProfile=host, routeKind="mission")
                        def client():
                            engine=Engine(verify=True)
                            engine.call("configure",platform="linux" if host=="ubuntu" else "windows",layout="EN")
                            return engine
                        e=client()
                        try:
                            s=begin(e, **setup)
                            self.assertEqual(s["mission"], mission)
                            folder=PurePosixPath("/home/student/project/handoff")
                            target=folder/("delivery.ini" if move else "handoff.md")
                            notes=folder/("draft.ini" if move else "notes.md")
                            facts=folder/("service.ini" if move else "schedule.md")
                            original=disk(s)
                            # Independently reconstruct desired bytes from the fixture given to this user.
                            def two(path): return "".join(original[str(path)].splitlines(True)[1:3])
                            block_a,block_b=two(facts),two(notes)
                            material.add((block_a,block_b))
                            expected=original[str(target)].splitlines(True)
                            expected[1]=block_a; expected[4]=block_b
                            delivered="".join(expected)
                            route=[]
                            while not s["finished"]:
                                t=s["task"]; task=t["id"]; old=s["solved"]; route.append(task)
                                if task=="scm-unstage":
                                    src=active(s)["sourceControl"]
                                    self.assertEqual([(x["path"],x["staged"]) for x in src["changes"]],[(notes.name, True)])
                                if task=="scm-stage":
                                    src=active(s)["sourceControl"]
                                    self.assertTrue(all(not x["staged"] for x in src["changes"]))
                                    self.assertEqual(src["changes"][src["selected"]]["path"],target.name)
                                    self.assertEqual(disk(s)[str(target)],delivered)
                                if task=="scm-message":
                                    before=active(s)["sourceControl"]
                                    e.call("lab-pause"); e.close(); e=client(); s=begin(e, **setup, **{"continue":True})
                                    self.assertEqual(s["solved"],old)
                                    self.assertEqual(active(s)["sourceControl"],before)
                                    self.assertEqual([(x["path"],x["staged"]) for x in before["changes"]],[(target.name,True),(notes.name,False)])
                                if task=="scm-commit":
                                    s=chord(e,"Ctrl+Enter")
                                    self.assertEqual(s["solved"],old,"an empty commit must not be credited")
                                    self.assertFalse(active(s)["sourceControl"]["lastCommit"])
                                    enter(e,"Test verified handoff")
                                    s=chord(e,"Ctrl+Enter")
                                    self.assertEqual(s["solved"],old+1)
                                    src=active(s)["sourceControl"]
                                    self.assertEqual(src["lastCommit"],"Test verified handoff")
                                    self.assertEqual([(x["path"],x["staged"]) for x in src["changes"]],[(notes.name,False)])
                                else: s=advance(e,s)
                            wanted=original.copy()
                            wanted[str(target)]=delivered
                            if move: wanted[str(notes)]=original[str(notes)].replace(block_b,"")
                            self.assertEqual(disk(s),wanted)
                            self.assertEqual(s["scene"]["terminal"]["output"][-1]," M "+notes.name)
                            self.assertTrue(any(x.endswith("Test verified handoff") for x in s["scene"]["terminal"]["output"]))
                            self.assertGreaterEqual(len(route),30)
                            self.assertIn("cut" if move else "copy",route)
                        finally: e.close()
            self.assertGreaterEqual(len(material),2)
if __name__=="__main__":unittest.main()
