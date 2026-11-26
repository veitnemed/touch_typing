"""Pointer navigation must change the real simulated page without earning a keyboard action."""
import os
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch

from platform_linux import Engine
from test_live_route_policy import options
from test_project_file_practice import active, chord


class ChromeSurfaceTests(unittest.TestCase):
    def test_old_query_draft_opens_at_the_old_url_then_uses_new_search(self):
        # Created by the unmodified v1.9.2 engine with a disposable profile:
        # ordinary card, click address, type report, Enter, save. No learner data.
        with tempfile.TemporaryDirectory() as root, patch.dict(os.environ,{"TOUCH_TYPING_QA_DATA_ROOT":root}):
            target=Path(root)/"TouchTypingStudio-QA"/"progress.xml"
            target.parent.mkdir()
            target.write_bytes((Path(__file__).resolve().parents[2]/"qa/fixtures/chrome-ordinary-query-v1.9.2.xml").read_bytes())
            opts=options("chrome",0,"ubuntu",routeKind="course",actionLimit=40,seconds=0,seed=1)
            e=Engine(verify=True)
            try:
                def open_():
                    e.call("configure",platform="linux",layout="EN")
                    e.call("lab-start",**(opts|{"continue":True}))
                    return e.call("lab-resume")
                s=open_(); w=active(s)
                self.assertEqual(w["url"],"https://search.local/?q=report"); self.assertEqual(len(w["entries"]),4)
                receipt=(s["solved"],s["task"]["id"])
                e.call("lab-browser-surface-action",owner=w["id"],action="address",slot=-1)
                for k in "report": chord(e,"X",k)
                s=chord(e,"Enter"); rows=active(s)["entries"]
                self.assertTrue(any(x["path"].endswith("/shipments") for x in rows)); e.call("lab-stop")
                e.close(); e=Engine(verify=True); s=open_()
                self.assertEqual((s["solved"],s["task"]["id"]),receipt)
                self.assertEqual((active(s)["url"],active(s)["entries"]),("https://www.google.com/search?q=report",rows))
            finally: e.close()

    def test_ordinary_card_can_search_authored_pages_without_changing_the_lesson(self):
        for profile in ("linux","windows"):
            with self.subTest(platform=profile), tempfile.TemporaryDirectory() as root, patch.dict(os.environ,{"TOUCH_TYPING_QA_DATA_ROOT":root}):
                e=Engine(verify=True)
                try:
                    e.call("configure",platform=profile,layout="EN")
                    e.call("lab-start",**options("chrome",0,"ubuntu",routeKind="course",actionLimit=40,seconds=0,seed=1))
                    s=e.call("lab-resume"); owner=active(s)["id"]
                    def click(action,slot=-1):
                        return e.call("lab-browser-surface-action",owner=owner,action=action,slot=slot)
                    def submit(query):
                        click("page-search")
                        for k in query: chord(e,"X",k)
                        return chord(e,"Enter")
                    initial=(s["mission"],s["task"]["id"],s["solved"])
                    s=click("new-tab")
                    self.assertEqual(active(s)["entries"],[])  # no unrelated generic links
                    s=submit("report")
                    w=active(s); self.assertEqual(w["url"],"https://www.google.com/search?q=report")
                    self.assertGreaterEqual(len(w["entries"]),2)
                    idx=next(i for i,entry in enumerate(w["entries"]) if entry["path"].endswith("/shipments"))
                    target=w["entries"][idx]
                    s=click("search-result",idx)
                    self.assertEqual((active(s)["url"],active(s)["tabs"][active(s)["activeTab"]]["title"]),(target["path"],target["title"]))
                    s=click("back"); self.assertEqual((active(s)["url"],active(s)["item"]),(w["url"],idx))
                    s=submit("no-such-needle")
                    self.assertTrue(active(s)["url"].endswith("no-such-needle")); self.assertEqual(active(s)["entries"],[])
                    self.assertEqual((s["mission"],s["task"]["id"],s["solved"]),initial)
                finally: e.close()

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
                    visited=active(s)["url"]
                    self.assertFalse(active(s)["canGoForward"])
                    before=(s["solved"],s["task"]["id"])
                    s=click("back"); w=active(s)
                    self.assertEqual((w["url"],w["focus"],w["canGoBack"]),(old_url,"body",False))
                    self.assertEqual((s["solved"],s["task"]["id"]),before)
                    self.assertTrue(w["canGoForward"])
                    w=active(click("forward")); self.assertEqual((w["url"],w["canGoBack"],w["canGoForward"]),(visited,True,False))
                    self.assertEqual(active(click("forward"))["url"],visited)
                    self.assertIn("обновлена",active(click("refresh"))["status"])
                    s=click("back")
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close(); e=Engine(verify=True); s=open_(True)
                    self.assertEqual((active(s)["url"],active(s)["canGoForward"]),(old_url,True))
                    # Behind the active editing tab, close and restore a neighbour.
                    # A click cannot lose its draft, reset its cursor, or earn a shortcut.
                    original_count=len(active(s)["tabs"]); w=active(click("bookmark")); self.assertIn(old_url,w["savedBookmarks"])
                    self.assertEqual(active(click("downloads"))["url"],"chrome://downloads")
                    count=len(active(click("downloads"))["tabs"]); self.assertEqual(count,original_count+1)
                    w=active(e.call("lab-state")); s=click("close-tab",w["activeTab"])
                    s=click("new-tab"); fresh=active(s)
                    self.assertEqual((fresh["url"],fresh["focus"]),("about:blank","url"))
                    self.assertFalse(fresh["canGoForward"])
                    for char in "draft": s=chord(e,"X",char)
                    w=active(click("close-tab",target))
                    self.assertEqual((w["url"],w["input"],w["cursor"]),(fresh["url"],"draft",5))
                    self.assertEqual(len(w["tabs"])+1,len(fresh["tabs"]))
                    restored_target=active(chord(e,"Ctrl+Shift+T")); self.assertEqual(restored_target["url"],fresh["tabs"][target]["url"])
                    fresh_slot=next(i for i,tab in enumerate(restored_target["tabs"]) if tab["url"]=="about:blank")
                    w=active(click("close-tab",fresh_slot))
                    old_slot=next(i for i,tab in enumerate(w["tabs"]) if tab["url"]==old_url)
                    target=w["activeTab"]; self.assertNotEqual(target,old_slot)
                    self.assertEqual(len(w["tabs"]),len(fresh["tabs"])-1)
                    # A click on Home navigates inside the simulator, not a host browser.
                    original_target=active(e.call("lab-state"))["url"]
                    self.assertEqual(active(click("home"))["url"],"https://www.google.com/")
                    self.assertEqual(active(click("back"))["url"],original_target)
                    s=click("address")
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e.close(); e=Engine(verify=True); s=open_(True); w=active(s)
                    self.assertEqual((w["activeTab"],w["focus"],w["selected"]),(target,"url",True))
                    self.assertEqual((s["solved"],s["task"]["id"]),before)
                    click("tab",old_slot); s=chord(e,"Ctrl+L")
                    self.assertEqual(s["solved"],before[0]+1)  # actual required key in its original tab
                finally: e.close()
