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

    def launch(self, e, desktop, seed=1, guidance="guided", repeat=False, **extra):
        e.call("lab-start", **options(desktop, seed=seed, guidance=guidance,
            routeKind="mission", mission="terminal-disk-" + ("repeat-" if repeat else "space-")
            + ("powershell" if desktop == "windows" else "bash"), **extra))
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
                    self.assertTrue(output[0].startswith("Не удалось сохранить архив "))
                    self.assertTrue(output[1].startswith("Назначение: "))
                    self.assertTrue(output[2].endswith(" ГиБ."))
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
                    shown = {0: "1", 1: "0,5", 5: "3"}[seed]
                    mount = expected[-1] + ":\\" if ps else output[1].split(": ", 1)[1].rsplit("/", 1)[0]
                    self.assertEqual(terminal(state)["output"][-3], f"Проверен диск {mount} · свободно {shown} ГиБ")
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

    def test_independent_repeat_identical_request_opposite_visible_results(self):
        for desktop in ("ubuntu", "windows"):
            ps = desktop == "windows"
            e = self.open("windows" if ps else "linux")
            catalog = e.call("lab-catalog", desktopProfile=desktop)
            variants = [r for r in catalog["missions"] if r["id"].startswith("terminal-disk-repeat-")]
            self.assertEqual([r["title"] for r in variants], ["Проверить место без подсказки"])
            requests, readings = [], []
            for seed in (1, 5):
                with self.subTest(desktop=desktop, seed=seed):
                    state = self.launch(e, desktop, seed, repeat=True)
                    self.assertFalse(state["showGuide"])
                    requests.append(tuple(terminal(state)["output"]))
                    target_command = state["task"]["text"]
                    self.assertEqual(submit(e, "Get-PSDrive C" if ps else "df -h .")["solved"], 0)
                    state = submit(e, target_command)
                    self.assertEqual(state["solved"], 1)
                    self.assertTrue(e.call("lab-stop")["saved"])
                    e = self.open("windows" if ps else "linux")
                    restored = self.launch(e, desktop, 0 if seed == 1 else 1, guidance="independent", repeat=True, **{"continue": True})
                    self.assertEqual(restored["solved"], 1)
                    self.assertEqual(terminal(restored)["output"], terminal(state)["output"])
                    needed, free = visible_capacity(terminal(restored)["output"], ps)
                    readings.append((needed, int(free)))
                    completed = chord(e, "2" if free >= needed else "1")
                    self.assertTrue(completed["finished"])
                    self.assertEqual(completed["solved"], 2)
                    self.assertIn("Места хватает:" if free >= needed else "Места не хватает.",
                                  e.call("lab-result")["diskCapacityConclusion"])
            self.assertEqual(requests[0], requests[1])
            self.assertCountEqual(readings, [(2048, 512), (2048, 3072)])

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
