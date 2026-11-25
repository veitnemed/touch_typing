"""VS Code curriculum: operate real engine events, inspect nbformat files and result artifacts."""
import json
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_python_editor import gesture, editor

CASES = ("code-notebook-read", "code-notebook-repair", "code-notebook-recheck")
SHIFTED = dict(zip('!@#$%^&*()_+{}|:<>?"', '1234567890-=[]\\;,./\''))
def chord_for(c):
    return "Space" if c == " " else "Shift+" + SHIFTED[c] if c in SHIFTED else c.upper()

def text(e, val):
    for c in val:
        state = gesture(e, chord_for(c), text=c)
    return state

def advance(e, s):
    t = s["task"]
    if t["text"] and (t["id"] in {"quick-choose", "save", "notebook-type"}):
        s = text(e, t["text"])
    if t["keys"]:
        s = gesture(e, t["keys"])
    return s

def options(level, seed):
    return dict(app="code", mode="sandbox", level=level, mission=CASES[level], guidance="guided",
                seconds=0, actionLimit=0, seed=seed, nativeEnglishInput=True)

def files(s):
    return {f["path"].split("/")[-1]: f["content"] for f in s["scene"]["terminal"]["files"]
            if "/task-list/" in f["path"]}

class NotebookCourse(unittest.TestCase):
    def run_route(self, platform, level, seed):
        e = Engine(verify=True)
        try:
            e.call("configure", platform=platform, layout="EN")
            cat=e.call("lab-catalog")
            self.assertEqual({m["id"] for m in cat["missions"] if m["id"] in CASES and m["app"]=="code"}, set(CASES))
            op = options(level, seed)
            s=e.call("lab-start", **op)
            e.call("lab-resume")
            original = files(s)
            path=next(n for n in original if n.endswith(".ipynb"))
            report=path[:-6] + "-result.txt"
            initial=json.loads(original[path])
            self.assertEqual(original[report],"")
            wrong=""
            for _ in range(s["scenarioLength"]):
                t=s["task"]
                old=s["solved"]
                if t["id"]=="save" and t["text"]:
                    # Correct shortcut, wrong file content: never completes the handoff.
                    s=text(e,"999")
                    s=gesture(e,"Ctrl+S")
                    self.assertEqual(s["solved"],old)
                    gesture(e,"Ctrl+A")
                if t["id"]=="notebook-run" and editor(s)["notebook"]["selected"]==2:
                    before=editor(s)["notebook"]["cells"][2]["output"]
                    if level == 2 and before:
                        self.assertTrue(editor(s)["notebook"]["cells"][2]["stale"])
                    s=advance(e,s)
                    out=editor(s)["notebook"]["cells"][2]
                    self.assertTrue(out["output"].isdigit(),out)
                    if before and level:
                        self.assertNotEqual(before,out["output"])
                    wrong=out["output"]
                else:
                    s=advance(e,s)
                self.assertEqual(s["solved"],old+1,(platform,level,t,s["status"],editor(s)["focus"],editor(s)["input"]))
            self.assertTrue(s["finished"],s)
            final=files(s)
            self.assertEqual(final[report],wrong)
            doc=json.loads(final[path]); self.assertEqual(doc["nbformat"],4)
            self.assertEqual(doc["cells"][0],initial["cells"][0]);self.assertEqual(doc["metadata"],initial["metadata"])
            self.assertEqual(doc["cells"][2]["outputs"][0]["data"]["text/plain"],[wrong])
            self.assertEqual(final["app.py"],original["app.py"]);self.assertEqual(final["archive.txt"],original["archive.txt"])
            return path
        finally:e.close()

    def test_all_routes_and_data(self):
        variants=set()
        for p in ("linux","windows"):
            for level in range(3):
                for seed in (0, 1, 5):
                    with self.subTest(platform=p,level=level,seed=seed), tempfile.TemporaryDirectory() as root, patch.dict(os.environ,{"TOUCH_TYPING_QA_DATA_ROOT":root}):
                        variants.add(self.run_route(p,level,seed))
        self.assertEqual(variants,{"weekly.ipynb","delivery.ipynb","checks.ipynb"})

    def test_kernel_scope_edit_and_restore(self):
        with tempfile.TemporaryDirectory() as root, patch.dict(os.environ,{"TOUCH_TYPING_QA_DATA_ROOT":root}):
            e=Engine(verify=True)
            try:
                e.call("configure", platform="linux", layout="EN")
                op=options(1,0)
                s=e.call("lab-start",**op);e.call("lab-resume")
                for _ in range(2):s=advance(e,s)
                self.assertEqual(editor(s)["notebook"]["selected"],0)
                for k in ("Down","Down","Ctrl+Enter"):s=gesture(e,k)
                cell=editor(s)["notebook"]["cells"][2]
                self.assertTrue(cell["failed"]);self.assertIn("NameError",cell["output"])
                for k in ("Up","Ctrl+Enter","Down","Ctrl+Enter","Enter","Ctrl+A"):s=gesture(e,k)
                original=editor(s)["notebook"]["cells"][2]["source"]
                self.assertTrue(editor(s)["notebook"]["editing"])
                s=text(e,"unsupported()");s=gesture(e,"Ctrl+Enter")
                self.assertIn("не выполняется",editor(s)["notebook"]["cells"][2]["output"])
                self.assertFalse(editor(s)["notebook"]["editing"])
                gesture(e,"Enter");s=gesture(e,"Ctrl+A");s=text(e,original[:3])
                snap=editor(s)["notebook"]
                e.call("lab-pause"); e.close();e=Engine(verify=True)
                op.pop("seed")
                s=e.call("lab-start",**op,**{"continue":True})
                self.assertEqual(editor(s)["notebook"],snap)
                e.call("lab-resume")
                s=text(e,original[3:]); s=gesture(e,"Ctrl+Enter")
                n=editor(s)["notebook"]
                self.assertEqual(n["cells"][2]["source"],original);self.assertFalse(n["cells"][2]["failed"])
                s=gesture(e,"Shift+Enter");n=editor(s)["notebook"]
                self.assertEqual((n["selected"],len(n["cells"]),n["cells"][-1]["source"]),(3,4,""))
                self.assertGreater(n["cells"][2]["runs"],0)
                self.assertTrue(n["editing"])
                s=text(e,"sum(totals)")
                self.assertEqual(editor(s)["notebook"]["cells"][3]["source"],"sum(totals)")
            finally:e.close()
if __name__=="__main__":unittest.main()
