"""Windows-profile PowerShell behavior through the actual Live protocol."""
import gzip
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from time import sleep
import unittest
from unittest.mock import patch
from xml.etree import ElementTree as ET

from platform_linux import Engine
from test_editor_course import enter
from test_project_file_practice import active, chord
from test_terminal_practical import PRACTICAL, begin, log as bash_log, options, submit, terminal, wait_for

POWERSHELL = "terminal-powershell-diagnostic"
ROOT = "/home/student/project/"
PS_PROMPT = "PS C:\\Users\\Student\\Project> "


def sync_log(state):
    return next(f["content"] for f in terminal(state)["files"] if f["path"] == ROOT + "sync.log")


class TerminalPowerShellTests(unittest.TestCase):
    def setUp(self):
        profile = TemporaryDirectory()
        self.addCleanup(profile.cleanup)
        self.profile = Path(profile.name)
        context = patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile.name})
        context.start()
        self.addCleanup(context.stop)
        self.e = None
        self.addCleanup(lambda: self.e.close() if self.e else None)

    def open(self, platform="windows"):
        if self.e is not None:
            self.e.close()
        self.e = Engine(verify=True)
        self.e.call("configure", platform=platform, layout="EN")
        return self.e

    def play(self, seed, host, test_scope=False):
        e = self.open(host)
        s = begin(e, "windows", seed)
        self.assertEqual((s["mission"], s["scenarioLength"], s["actionLimit"], s["timeLimit"], s["desktopProfile"]),
                         (POWERSHELL, 4, 0, 0, "windows"))
        self.assertTrue(s["terminalPractical"])
        self.assertEqual(s["scene"]["desktopStyle"], "win10")
        self.assertIn("Windows PowerShell", active(s)["title"])
        self.assertEqual((terminal(s)["shell"], terminal(s)["prompt"]), ("powershell", PS_PROMPT))
        self.assertEqual({f["path"] for f in terminal(s)["files"]}, {ROOT+"sync.log", ROOT+"archive.log"})
        first = sync_log(s)
        old_id = first.splitlines()[1].split()[1]
        if test_scope:
            for invalid in ("grep ERROR sync.log", "Select-String ERROR archive.log", "Select-String ERROR sync.log | Out-Host",
                            'Select-String ERROR "C:\\Windows\\win.ini"', r"Select-String ERROR ..\sync.log",
                            'Select-String "$env:HOME" sync.log', "Select-String [ sync.log",
                            "Get-ChildItem", "whoami"):
                s = submit(e, invalid)
                self.assertEqual((s["solved"], sync_log(s)), (0, first), invalid)
            template = "select-string -Path '.\\SYNC.LOG' -Pattern error -SimpleMatch"
        else:
            template = "Select-String ERROR sync.log"
        s = submit(e, template)
        self.assertEqual((s["solved"], terminal(s)["output"][-1]), (1, "sync.log:2:" + first.splitlines()[1]))
        s = submit(e, "Get-Content sync.log")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (1, False))
        s = submit(e, "Get-Content archive.log -Wait")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (1, False))
        s = submit(e, "Get-Content sync.log -Wait")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (2, True))
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (2, False))
        self.assertIn("Get-Content sync.log -Wait", s["status"])
        s = enter(e, "wrong")
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], s["scene"]["terminalInput"]), (2, ""))
        s = submit(e, "Get-Content sync.log -Wait")
        self.assertEqual((s["solved"], terminal(s)["followingLog"]), (2, True))
        if test_scope:
            wait_for(e, lambda state: sync_log(state) != first)
            paused = e.call("lab-pause")
            sleep(1.06)
            cached = e.call("lab-state")
            self.assertEqual(sync_log(cached), sync_log(paused), "Pausing is neither service cancellation nor extra active feed time")
            e = self.open(host)
            restored = e.call("lab-start", **options("windows", **{"continue": True}))
            self.assertEqual((restored["solved"], sync_log(restored), terminal(restored)["followingLog"]),
                             (2, sync_log(paused), True))
            e.call("lab-resume")
        s = wait_for(e, lambda state: terminal(state)["logIncidentArrived"])
        fresh = next(line for line in sync_log(s).splitlines()[len(first.splitlines()):] if line.startswith("ERROR "))
        new_id = fresh.split()[1]
        self.assertNotEqual(new_id, old_id)
        wrong = chord(e, "Ctrl+Shift+C")
        self.assertEqual((wrong["solved"], terminal(wrong)["followingLog"]), (2, True))
        s = chord(e, "Ctrl+C")
        self.assertEqual((s["solved"], terminal(s)["followingLog"], terminal(s)["output"][-1]), (3, False, "^C"))
        wrong = submit(e, f"Select-String {old_id} sync.log")
        self.assertEqual(wrong["solved"], 3)
        self.assertIn("новой строки ERROR", wrong["status"])
        s = submit(e, f"Select-String {new_id} 'C:\\Users\\Student\\Project\\sync.log'")
        self.assertEqual((s["solved"], s["finished"]), (4, True))
        self.assertEqual(terminal(s)["output"][-2:], [f"sync.log:5:INFO {new_id} started", f"sync.log:6:{fresh}"])
        self.assertTrue(e.call("lab-result")["completed"])
        rows = ET.parse(self.profile / "TouchTypingStudio-QA" / "progress.xml").findall(".//ShortcutAttempt")
        journal = [a for a in rows if a.findtext("Skill") == "lab/terminal/command/powershell-incident"
                   and a.findtext("Correct") == "true"]
        self.assertEqual(len(journal), 1)
        self.assertIn(journal[0].findtext("TerminalCase"), ("1", "2", "3"))
        next_case = begin(e, "windows", seed)
        self.assertNotEqual(sync_log(next_case), first)
        return fresh

    def test_windows_binding_profile_data_scope_and_recovery(self):
        self.assertIn("destination disk full", self.play(0, "windows", test_scope=True))

    def test_windows_simulation_on_linux_host_has_different_incident(self):
        self.assertIn("sharing violation", self.play(1, "linux"))

    def test_saved_bash_and_powershell_are_separate_product_profiles(self):
        fixture = Path(__file__).with_name("fixtures") / "preview9-original-terminal-course.xml.gz"
        target = self.profile / "TouchTypingStudio-QA" / "progress.xml"
        target.parent.mkdir()
        target.write_bytes(gzip.decompress(fixture.read_bytes()))
        e = self.open()
        windows = e.call("lab-catalog", desktopProfile="windows")
        ubuntu = e.call("lab-catalog", desktopProfile="ubuntu")
        self.assertEqual(next(m for m in windows["classes"] if m["id"] == "terminal")["title"], "PowerShell")
        windows_missions = {m["id"]: m for m in windows["missions"] if m["app"] == "terminal"}
        self.assertIn(POWERSHELL, windows_missions)
        self.assertIn("terminal-explore", windows_missions)
        self.assertNotIn(PRACTICAL, windows_missions)
        native_powershell = {
            POWERSHELL,
            "terminal-disk-space-powershell",
            "terminal-disk-repeat-powershell",
        }
        self.assertTrue(all(item["title"].startswith("Bash (учебный Ubuntu) · ")
                            for key, item in windows_missions.items() if key not in native_powershell))
        self.assertEqual(windows_missions["terminal-disk-space-powershell"]["title"],
                         "Не сохранился архив: проверить место")
        self.assertEqual(windows_missions["terminal-disk-repeat-powershell"]["title"],
                         "Проверить место без подсказки")
        ubuntu_missions = {m["id"] for m in ubuntu["missions"] if m["app"] == "terminal"}
        self.assertIn(PRACTICAL, ubuntu_missions)
        self.assertFalse(ubuntu_missions & native_powershell)
        self.assertEqual([(d["mission"], d["desktopProfile"]) for d in windows["drafts"] if d["app"] == "terminal"],
                         [("terminal-explore", "ubuntu")])
        ps = begin(e, "windows", 1)
        enter(e, "Select-S")
        ps_input = e.call("lab-state")
        self.assertEqual((ps_input["scene"]["terminalInput"], active(ps_input)["cursor"]), ("Select-S", 8))
        self.assertTrue(e.call("lab-stop")["saved"])
        e = self.open()
        self.assertEqual({(d["mission"], d["desktopProfile"]) for d in e.call("lab-catalog", desktopProfile="windows")["drafts"]
                          if d["app"] == "terminal"}, {("terminal-explore", "ubuntu"), (POWERSHELL, "windows")})
        old = e.call("lab-start", **options("ubuntu", **{"continue": True}))
        self.assertEqual((old["mission"], terminal(old)["shell"], old["scene"]["terminalInput"], active(old)["cursor"]),
                         ("terminal-explore", "bash", "pw", 2))
        e.call("lab-resume")
        self.assertEqual(submit(e, "d")["solved"], 2)
        self.assertTrue(e.call("lab-stop")["saved"])
        restored = e.call("lab-start", **options("windows", **{"continue": True}))
        self.assertEqual((restored["mission"], terminal(restored)["prompt"], restored["scene"]["terminalInput"], sync_log(restored)),
                         (POWERSHELL, PS_PROMPT, "Select-S", sync_log(ps)))
        e.call("lab-resume")
        self.assertEqual(submit(e, "tring ERROR sync.log")["solved"], 1)
        bash = begin(e, "ubuntu", 0)
        initial = bash_log(bash)
        rejected = submit(e, "Select-String ERROR sync.log")
        self.assertEqual((rejected["mission"], terminal(rejected)["shell"], bash_log(rejected), rejected["solved"]),
                         (PRACTICAL, "bash", initial, 0))


if __name__ == "__main__":
    unittest.main()
