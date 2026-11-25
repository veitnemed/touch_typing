"""Pointer navigation must change the real simulated page without earning a keyboard action."""
import os
import tempfile
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_live_route_policy import options
from test_project_file_practice import active, chord


class ChromeSurfaceTests(unittest.TestCase):
    def test_surface_changes_state_not_keyboard_credit_and_resumes(self):
        for os_profile in ("linux","windows"):
            with self.subTest(os_profile=os_profile), tempfile.TemporaryDirectory() as root, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": root}):
                e = Engine(verify=True)
                opts = options("chrome", 0, "ubuntu", routeKind="mission", mission="chrome-reader", actionLimit=0, seconds=0, seed=1)
                try:
                    def open_(resume=False):
                        e.call("configure",platform=os_profile,layout="EN")
                        e.call("lab-start",**(opts|{"continue":resume}))
                        return e.call("lab-resume")
                    def click(action, slot=-1, owner=None):
                        return e.call("lab-browser-surface-action",action=action,owner=active(e.call("lab-state"))["id"] if owner is None else owner,slot=slot)
                    s=open_(); w=active(s); expected=s["task"]["id"]; start=s["solved"]
                    old_url=w["url"]; old_slot=w["activeTab"]
                    self.assertGreater(len(w["tabs"]),1)
                    self.assertFalse(w.get("canGoBack",False))
                    self.assertEqual((active(click("back"))["url"],s["solved"]),(old_url,start))
                    self.assertNotEqual(active(click("address",owner="window-999"))["focus"],"url")
                    s=click("address"); w=active(s)
                    self.assertEqual((w["focus"],w["input"],w["anchor"],w["cursor"]),("url",old_url,0,len(old_url)))
                    self.assertEqual((s["task"]["id"],s["solved"]),(expected,start))
                    target=next(i for i,tab in enumerate(w["tabs"]) if i!=old_slot and tab["url"]!=old_url)
                    self.assertEqual(active(click("tab",99))["url"],old_url)
                    self.assertEqual(active(click("tab",target))["activeTab"],target)
                    self.assertEqual(active(e.call("lab-state"))["url"],w["tabs"][target]["url"])
                    self.assertEqual(active(click("tab",old_slot))["url"],old_url)
                    s=click("address")
                    for letter in "https://practice.local/task-list":
                        s=chord(e,"X",letter)
                    s=chord(e,"Enter")
                    self.assertNotEqual(active(s)["url"],old_url)
                    self.assertTrue(active(s)["canGoBack"])
                    before=(s["solved"],s["task"]["id"])
                    s=click("back"); w=active(s)
                    self.assertEqual((w["url"],w["focus"],w["canGoBack"]),(old_url,"body",False))
                    self.assertEqual((s["solved"],s["task"]["id"]),before)
                    s=click("tab",target); s=click("address")
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close(); e=Engine(verify=True); s=open_(True); w=active(s)
                    self.assertEqual((w["activeTab"],w["focus"],w["selected"]),(target,"url",True))
                    self.assertEqual((s["solved"],s["task"]["id"]),before)
                    click("tab",old_slot); s=chord(e,"Ctrl+L")
                    self.assertEqual(s["solved"],before[0]+1)  # actual required key in its original tab
                finally: e.close()
