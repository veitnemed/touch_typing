"""Capacity diagnosis of a closed synthetic volume, no host drive I/O."""
import unittest
from decimal import Decimal

import test_terminal_practical as harness
from test_terminal_practical import options, submit, terminal
from test_project_file_practice import chord


def visible_capacity(lines, ps):
    needed = int(next(line for line in lines if line.startswith("Размер архива:")).split()[2]) * 1024
    columns = lines[-1].split()
    amount = columns[2] if ps else columns[3]
    suffix = "G" if ps else amount[-1]
    free = Decimal(amount if ps else amount[:-1]) * (1024 if suffix == "G" else 1)
    return needed, free


class DiskCapacityTests(unittest.TestCase):
    setUp = harness.TerminalPracticalTests.setUp
    open = harness.TerminalPracticalTests.open

    def launch(self, e, desktop, seed=1, guidance="guided", **extra):
        e.call("lab-start", **options(desktop, seed=seed, guidance=guidance,
            routeKind="mission", mission="terminal-disk-space-" + ("powershell" if desktop=="windows" else "bash"), **extra))
        return e.call("lab-resume")

    def test_exact_destination_and_both_conclusions_for_each_shell(self):
        for desktop in ("ubuntu", "windows"):
            ps = desktop == "windows"
            e = self.open("windows" if ps else "linux")
            catalog = e.call("lab-catalog", desktopProfile=desktop)
            lessons = [r for r in catalog["missions"] if r["id"].startswith("terminal-disk-space-")]
            self.assertEqual(len(lessons), 1)
            self.assertFalse(lessons[0]["title"].startswith("Bash ("))
            capacities = set()
            for seed in (0, 1, 5):
                with self.subTest(desktop=desktop, seed=seed):
                    s = self.launch(e, desktop, seed)
                    self.assertEqual(s["scenarioLength"], 2)
                    expected = s["task"]["text"]
                    output = terminal(s)["output"]
                    files = terminal(s)["files"]
                    self.assertTrue(any("Назначение:" in line for line in output))
                    enough = any("Размер архива: 1 ГиБ" in line for line in output)
                    bad = ("Get-PSDrive C", "Get-PSDrive " + expected[-1] + ":", 'Get-PSDrive X',
                           'Get-PSDrive ' + expected[-1] + '; rm something') if ps else (
                           "df -h .", "df -h /a/real/path", "rm notes.txt", "df -h / > stolen.txt")
                    for command in bad:
                        state = submit(e, command)
                        self.assertEqual(state["solved"], 0, (desktop, seed, command))
                        self.assertEqual(terminal(state)["files"], files)
                    if not ps and seed==5:
                        self.assertEqual(submit(e, "cd " + expected.split(" ", 2)[-1])["solved"], 0)
                        expected = "df -h ."
                    state = submit(e, expected)
                    self.assertEqual(state["solved"], 1)
                    self.assertIn("Free (GB)" if ps else "Avail", terminal(state)["output"][-2])
                    requested, free = visible_capacity(terminal(state)["output"], ps)
                    self.assertEqual(free, {0: 1024, 1: 512, 5: 3072}[seed])
                    enough = free >= requested
                    capacities.add(enough)
                    self.assertIsNone(state["commandTheory"])
                    ignored = chord(e, "Enter")
                    self.assertEqual((ignored["solved"], terminal(ignored)["output"]),
                                     (state["solved"], terminal(state)["output"]))
                    state = chord(e, "1" if enough else "2")
                    self.assertEqual(state["solved"], 1, "wrong conclusion cannot pass")
                    self.assertIn("Сравните свободное место", state["status"])
                    state = chord(e, "2" if enough else "1")
                    self.assertTrue(state["finished"])
                    self.assertEqual(state["solved"], 2)
                    self.assertIn("Места хватает:" if enough else "Места не хватает.", e.call("lab-result")["diskCapacityConclusion"])
                    self.assertEqual(terminal(state)["files"], files)
            self.assertEqual(capacities, {False, True})

    def test_restore_destination_report_and_choice_after_restart(self):
        e = self.open()
        s = self.launch(e, "ubuntu", guidance="independent")
        self.assertFalse(s["showGuide"])
        card = " ".join(s["commandTheory"].values())
        self.assertNotIn("df", card)
        self.assertNotIn("/mnt/", card)
        s = submit(e, s["task"]["text"])
        self.assertEqual(s["solved"], 1)
        self.assertTrue(e.call("lab-stop")["saved"])
        restored = self.launch(self.open(), "ubuntu", seed=2, guidance="independent", **{"continue": True})
        self.assertEqual((restored["solved"], restored["task"], terminal(restored)["output"]),
                         (1, s["task"], terminal(s)["output"]))
        needed, free = visible_capacity(terminal(restored)["output"], ps=False)
        self.assertTrue(chord(self.e, "2" if free >= needed else "1")["finished"])

if __name__ == "__main__":
    unittest.main()
