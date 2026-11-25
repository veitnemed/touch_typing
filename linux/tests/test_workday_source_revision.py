"""Content/selection, durable interrupted handoff, wrong destination, real ordinary rotation."""
import os
import tempfile
import unittest
from unittest.mock import patch
from platform_linux import Engine
from test_source_handoff import advance, until, type_text
from test_project_file_practice import active, chord, disk
from test_live_route_policy import options, advance as course_advance

NOTE = "/home/student/project/передача-смены.md"
SLUGS = {"handoff", "backup", "accounts"}


def mission(profile):
    return "workday-" + ("windows-" if profile == "windows" else "") + "repair-source-link"


def opts(profile, seed, **extra):
    return options("workday", 1, profile, routeKind="mission", mission=mission(profile),
                   seconds=0, actionLimit=0, seed=seed) | extra


def configure(e, profile):
    e.call("configure", platform="linux" if profile == "ubuntu" else "windows", layout="EN")


class WorkdayRevision(unittest.TestCase):
    def test_real_history_identity_preserves_exact_old_note_and_survives_shutdown(self):
        for desktop in ("ubuntu", "windows"):
            seen = set()
            for seed in (0, 1, 5):
                with self.subTest(desktop=desktop, seed=seed), tempfile.TemporaryDirectory() as root, patch.dict(
                        os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                    e = Engine(verify=True)
                    try:
                        configure(e, desktop)
                        e.call("lab-start", **opts(desktop, seed))
                        s = e.call("lab-resume")
                        original = disk(s)[NOTE]
                        old = original.splitlines()[-1]
                        slug = old.split("/")[-2]; seen.add(slug)
                        target = old.rsplit("/", 1)[0] + "#procedure"
                        self.assertEqual(active(s)["url"],NOTE)
                        s = advance(e, s)  # the old citation was visible, now open Chrome
                        s = advance(e, s)  # actual browser history
                        type_text(e, slug)
                        entries = active(e.call("lab-state"))["entries"]
                        self.assertEqual({x["path"] for x in entries}, {target, old, old.rsplit("/", 1)[0]})
                        before = s["solved"]
                        # A real typed filter with no matching entry does not score.
                        type_text(e, "notfound")
                        wrong = chord(e, "Enter")
                        self.assertEqual((wrong["solved"], active(wrong)["url"]), (before, "chrome://history"))
                        chord(e, "Ctrl+F"); type_text(e, slug)
                        s = until(e, e.call("lab-state"), lambda x: x["task"]["id"] == "paste")
                        w = active(s)
                        self.assertEqual((w["url"], w["body"][min(w["anchor"],w["cursor"]):max(w["anchor"],w["cursor"])]),
                                         (NOTE, old))
                        self.assertEqual(s["scene"]["clipboard"], target)
                        self.assertTrue(e.call("lab-stop")["saved"])
                        e.close()
                        e = Engine(verify=True); configure(e, desktop)
                        e.call("lab-start", **(opts(desktop, seed) | {"continue":True}))
                        r = e.call("lab-resume")
                        self.assertEqual((r["solved"],active(r)["url"],active(r)["anchor"],active(r)["cursor"],disk(r)[NOTE]),
                                         (s["solved"],NOTE,w["anchor"],w["cursor"],original))
                        s = advance(e,r)
                        self.assertEqual(active(s)["body"],original[:-len(old)] + target)
                        self.assertEqual(disk(s)[NOTE],original)
                        if seed == 1:
                            # A saved valid address plus an extra edit in the accepted note cannot earn credit.
                            chord(e, "X", "x")
                            rejected = chord(e, "Ctrl+S")
                            self.assertEqual(rejected["solved"],s["solved"])
                            self.assertNotEqual(disk(rejected)[NOTE],original[:-len(old)] + target)
                            s=chord(e,"Ctrl+Z")
                        s = advance(e,s)
                        expected = original[:-len(old)] + target
                        self.assertEqual(disk(s)[NOTE],expected)
                        while not s["finished"]: s = advance(e,s)
                        self.assertEqual((active(s)["url"],disk(s)[NOTE]),(target,expected))
                        self.assertFalse(active(s)["body"].startswith("Это предыдущий порядок"))
                        self.assertIn(active(s)["body"].splitlines()[0],expected)
                    finally:
                        e.close()
            self.assertEqual(seen,SLUGS)

    def test_previous_and_new_work_reachable_via_real_sequential_and_adaptive_selection(self):
        for profile in ("ubuntu","windows"):
            for adaptive in (False,True):
                with self.subTest(profile=profile, adaptive=adaptive), tempfile.TemporaryDirectory() as root, patch.dict(
                        os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                    e = Engine(verify=True)
                    try:
                        configure(e,profile)
                        e.call("lab-start",**options("workday",1,profile,routeKind="course",adaptiveGuide=adaptive,
                                                    actionLimit=0,seconds=0,seed=1))
                        s = e.call("lab-resume")
                        first = s["mission"]; reached=False; visited=[]
                        for _ in range(1200):
                            if not visited or visited[-1] != s["mission"]: visited.append(s["mission"])
                            if s["mission"] == mission(profile):
                                reached=True
                                if s["task"]["id"] == "navigate-pasted":
                                    previous = s["solved"]
                                    self.assertTrue(disk(s)[NOTE].endswith("#procedure"))
                                    s = course_advance(e,s)
                                    self.assertEqual(s["solved"],previous+1)
                                    self.assertNotEqual(s["mission"],mission(profile))
                                    break
                            s = course_advance(e,s)
                        else: self.fail(f"new work not completed in rotation: {visited}")
                        self.assertTrue(reached)
                        self.assertNotEqual(first,mission(profile))
                        self.assertIn("workday-" + ("windows-" if profile=="windows" else "") + "verified-bulletin",visited)
                        print("WORKDAY ROTATION",profile,adaptive,s["solved"],visited,"->",s["mission"],flush=True)
                    finally:
                        e.close()


if __name__ == "__main__":
    unittest.main()
