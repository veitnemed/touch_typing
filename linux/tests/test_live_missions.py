"""Replay reference routes through physical events and verify the visible mission advances."""
import os
import tempfile
import unittest

from platform_linux import Engine


class MissionTest(unittest.TestCase):
    def test_starting_recommendations_are_advisory_and_never_select_a_draft(self):
        from pathlib import Path
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        names = [("guide", "first-fix", "scratch"), ("refactor", "extract", "console"),
                 ("handover", "repair", "inspection")]
        tasks = ("explorer", "quick-open", "quick-choose", "save")

        def sample(level, *, count=10, independent=True, assisted=False, correct=True,
                   skill_app="code", one_mission=False, one_session=False, one_task=False):
            return [
                {"Mission": "code-" + names[level][0 if one_mission else index % 3],
                 "Skill": f"lab/{skill_app}/" + tasks[0 if one_task else index % 4],
                 "Verification": "lab-scene", "InputMode": "exact",
                 "Session": f"session-{0 if one_session else index % 2}",
                 "Independent": str(independent).lower(), "Assisted": str(assisted).lower(),
                 "Correct": str(correct).lower(), "FirstTry": str(correct).lower()}
                for index in range(count)
            ]

        serial = [
            {**x, "Mission": "code-" + name, "Session": f"serial-{session}"}
            for session, name in enumerate(names[0])
            for x in sample(0, count=25)
        ]

        for label, history, expected in (
            ("new", [], None),
            ("guided", sample(0, independent=False, assisted=True), None),
            ("helped", sample(0, assisted=True), None),
            ("cross-class", sample(0, skill_app="chrome"), None),
            ("unknown-mission", [{**x, "Mission": "missing"} for x in sample(0)], None),
            ("single-mission", sample(0, one_mission=True), None),
            ("single-session", sample(0, one_session=True), None),
            ("single-task", sample(0, one_task=True), None),
            ("recent-misses", sample(0) + sample(0, count=3, correct=False), None),
            ("three-complete-sessions", serial, 1),
            ("three-complete-sessions-then-misses", serial + sample(0, count=3, correct=False), None),
            ("tiers-not-pooled", sample(0, count=5) + sample(1, count=5), None),
            ("at-boundary", sample(0, count=9) + sample(0, count=1, correct=False), 1),
            ("editor-basic", sample(0), 1),
            ("editor-middle", sample(1), 2),
            ("editor-advanced", sample(2), 2),
        ):
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory, \
                    patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
                profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
                profile.parent.mkdir()
                root = ET.Element("StudioProfile")
                old_attempts = ET.SubElement(root, "ShortcutHistory")
                for record in [{}] + history:  # includes an old/incomplete history row
                    item = ET.SubElement(old_attempts, "ShortcutAttempt")
                    for key, value in record.items():
                        ET.SubElement(item, key).text = value
                drafts = ET.SubElement(root, "LiveDrafts")
                draft = ET.SubElement(drafts, "LiveDraft")
                for key, value in {"App": "code", "Level": "0", "Mission": "code-first-fix",
                                   "Layout": "EN", "Guidance": "guided", "Trace": "[]"}.items():
                    ET.SubElement(draft, key).text = value
                ET.ElementTree(root).write(profile, encoding="utf-8")
                previous = profile.read_bytes()

                engine = Engine(verify=True)
                try:
                    catalog = engine.call("lab-catalog")
                    tips = {item["app"]: item for item in catalog["recommendations"]}
                    self.assertEqual(set(tips), {entry["id"] for entry in catalog["classes"]})
                    self.assertEqual(tips["code"]["level"], expected)
                    self.assertTrue(all(tips[entry]["level"] is None for entry in tips if entry != "code"))
                    self.assertEqual(catalog["drafts"], [dict(app="code", level=0, mission="code-first-fix", guidance="guided")])
                    self.assertEqual(profile.read_bytes(), previous)
                    if label == "editor-basic":
                        self.assertIn("ещё не подтверждён", tips["code"]["reason"])
                        # Learner ignores advisory level 1, chooses level 2,
                        # then resumes the still-present basic draft.
                        options = dict(app="code", mode="sandbox", seconds=0, guidance="guided", seed=0)
                        manual = engine.call("lab-start", **options, level=2, mission="code-handover")
                        self.assertEqual(manual["mission"], "code-handover")
                        resumed = engine.call("lab-start", **options, level=0, **{"continue": True})
                        self.assertEqual(resumed["mission"], "code-first-fix")
                finally:
                    engine.close()

    def test_routes(self):
        with tempfile.TemporaryDirectory() as directory:
            original = os.environ.get("XDG_DATA_HOME")
            os.environ["XDG_DATA_HOME"] = directory
            try:
                engine = Engine(verify=True)
                try:
                    missions = engine.call("lab-catalog")["missions"]
                    self.assertEqual(len(missions), 51)
                    self.assertEqual(len({m["id"] for m in missions}), 51)
                    for mission in missions:
                        with self.subTest(mission=mission["id"]):
                            self.walk(engine, mission)
                finally:
                    engine.close()
            finally:
                if original is None:
                    os.environ.pop("XDG_DATA_HOME", None)
                else:
                    os.environ["XDG_DATA_HOME"] = original

    def test_file_transfer_survives_profile_restart(self):
        with tempfile.TemporaryDirectory() as directory:
            original = os.environ.get("XDG_DATA_HOME")
            os.environ["XDG_DATA_HOME"] = directory
            engine = None
            try:
                options = dict(app="ubuntu", level=0, mode="sandbox", seconds=0, guidance="guided")
                engine = Engine(verify=True)
                engine.call("lab-start", seed=0, **options)
                engine.call("lab-resume")

                def chord(*keys):
                    for key in keys:
                        engine.call("lab-key", key=key, pressed=True, text="")
                    for key in reversed(keys):
                        state = engine.call("lab-key", key=key, pressed=False, text="")
                    return state

                def workspace():
                    return engine.call("lab-state")["scene"]

                def active(scene):
                    return next(w for w in scene["windows"] if w["id"] == scene["activeWindow"])

                def select(name):
                    for _ in range(30):
                        window = active(workspace())
                        self.assertEqual(window["app"], "files")
                        item = window["item"]
                        if item >= 0 and window["entries"][item]["title"] == name:
                            return
                        desired = next(i for i, entry in enumerate(window["entries"]) if entry["title"] == name)
                        chord("Down" if item < desired else "Up")
                    self.fail("selection never reached " + name)

                def contents(scene):
                    return {f["path"]: f["content"] for f in scene["terminal"]["files"]}

                def observable(scene):
                    # Tab IDs are per-process handles, not persisted lesson state.
                    for window in scene["windows"]:
                        for tab in window["tabs"]:
                            tab.pop("id")
                        if window["otherTab"]:
                            window["otherTab"].pop("id")
                    return scene

                def restart():
                    nonlocal engine
                    before = engine.call("lab-stop")
                    self.assertEqual(before["held"], [])
                    engine.close()
                    engine = Engine(verify=True)
                    self.assertTrue(any(item["app"] == options["app"] for item in
                                        engine.call("lab-catalog")["drafts"]))
                    reopened = engine.call("lab-start", **{"continue": True}, **options)
                    self.assertEqual(reopened["mission"], before["mission"])
                    self.assertEqual(reopened["scenarioStep"], before["scenarioStep"])
                    self.assertEqual(reopened["task"], before["task"])
                    self.assertEqual(observable(reopened["scene"]), observable(before["scene"]))
                    self.assertEqual((reopened["solved"], reopened["errors"]), (0, 0))
                    engine.call("lab-resume")

                chord("LSuper", "4")
                chord("LAlt", "Home")
                select("project")
                chord("Enter")
                select("notes.txt")
                src = "/home/student/project/notes.txt"
                dst = "/home/student/Downloads/notes.txt"
                expected = contents(workspace())[src]
                self.assertTrue(expected)
                chord("RCtrl", "X")
                before = workspace()
                self.assertEqual(before["clipboard"], src)
                same_folder = chord("RCtrl", "V")
                self.assertEqual(same_folder["scene"], before)
                self.assertEqual(same_folder["feedback"], "error")
                restart()

                chord("LAlt", "Home")
                select("Downloads")
                chord("Enter")
                after_cut = chord("RCtrl", "V")
                self.assertNotIn(src, contents(after_cut["scene"]))
                self.assertEqual(contents(after_cut["scene"])[dst], expected)
                self.assertEqual(active(after_cut["scene"])["entries"][active(after_cut["scene"])["item"]]["path"], dst)

                chord("RCtrl", "C")
                chord("Delete")
                trashed = workspace()
                moved = [(path, data) for path, data in contents(trashed).items() if "/.Trash/" in path]
                self.assertEqual(len(moved), 1)
                self.assertEqual(moved[0][1], expected)
                self.assertNotIn(dst, contents(trashed))
                self.assertEqual(trashed["clipboard"], dst)
                chord("LAlt", "Home")
                select("project")
                chord("Enter")
                before = workspace()
                stale = chord("RCtrl", "V")
                self.assertEqual(stale["scene"], before)
                self.assertEqual(stale["feedback"], "error")
                restart()

                restored = chord("RCtrl", "Z")["scene"]
                self.assertEqual(contents(restored)[dst], expected)
                self.assertNotIn(moved[0][0], contents(restored))
                copied = chord("RCtrl", "V")["scene"]
                self.assertEqual(contents(copied)[src], expected)
                self.assertEqual(contents(copied)[dst], expected)
            finally:
                if engine is not None:
                    engine.close()
                if original is None:
                    os.environ.pop("XDG_DATA_HOME", None)
                else:
                    os.environ["XDG_DATA_HOME"] = original

    def test_v170_workday_draft_retains_earned_progress_without_issuing_receipt(self):
        import gzip
        import json
        from datetime import datetime, timezone
        from pathlib import Path
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        fixture = Path(__file__).parent / "fixtures" / "v1.7.0-workday-report.xml.gz"
        # Generated by the actual v1.7.0 Linux engine on a new QA profile:
        # follow the reference route; before the final head command, copy the
        # report back to Downloads; pass head; stop at the screenshot task.
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            profile.parent.mkdir()
            profile.write_bytes(gzip.decompress(fixture.read_bytes()))

            def records():
                def value(field):
                    # XmlSerializer writes local offsets; compare the same instant
                    # on runners in a different time zone from the fixture.
                    return (datetime.fromisoformat(field.text).astimezone(timezone.utc)
                            if field.tag == "Date" else field.text)
                return [tuple((field.tag, value(field)) for field in item)
                        for item in ET.parse(profile).findall("./ShortcutHistory/ShortcutAttempt")]

            def trace():
                return json.loads(ET.parse(profile).find("./LiveDrafts/LiveDraft/Trace").text)

            before = records()
            self.assertEqual(len(before), 21)
            self.assertNotIn("@strict-report", [event["Key"] for event in trace()])
            options = dict(app="workday", level=0, mode="sandbox", seconds=0, guidance="guided", mission="workday-report", seed=0)
            for _ in range(2):
                engine = Engine(verify=True)
                try:
                    state = engine.call("lab-start", **options, **{"continue": True})
                    self.assertEqual((state["scenarioStep"], state["task"]["id"]), (22, "screenshot"))
                    self.assertIsNone(state["reportArtifact"])
                    files = {item["path"]: item["content"] for item in state["scene"]["terminal"]["files"]}
                    expected = "# piling-main · спектральный анализ\nstatus: draft\nsource: src/spectrum.py\n"
                    self.assertEqual(files["/home/student/project/spectrum-report.txt"], expected)
                    self.assertEqual(files["/home/student/Downloads/spectrum-report.txt"], expected)
                    self.assertTrue(engine.call("lab-stop")["saved"])
                    self.assertEqual(records(), before)
                    self.assertEqual(sum(event["Key"] == "@strict-report" for event in trace()), 1)
                finally:
                    engine.close()

    def test_acknowledged_report_survives_engine_sigkill(self):
        import signal
        from pathlib import Path
        from unittest.mock import patch

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            options = dict(app="workday", level=0, mode="sandbox", seconds=0, guidance="guided")
            engine = Engine(verify=True)
            marker = "lab/workday/command/head -n 2 spectrum-report.txt"
            sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}

            def chord(keys, char=""):
                for key in keys:
                    state = engine.call("lab-key", key=key, pressed=True, text=char if key == keys[-1] else "")
                    self.assertFalse(engine.storage_error)
                for key in reversed(keys):
                    state = engine.call("lab-key", key=key, pressed=False, text="")
                    self.assertFalse(engine.storage_error)
                return state

            def credit():
                return next((row["count"] for row in engine.call("library")["liveSkills"] if row["key"] == marker), 0)

            def kill_engine():
                # Closing stdin first would execute Program.FinishLab and mask lost checkpoints.
                self.assertFalse(engine.process.stdin.closed)
                engine.process.kill()
                self.assertEqual(engine.process.wait(timeout=5), -signal.SIGKILL)
                engine.close()

            try:
                engine.call("lab-start", mission="workday-report", seed=0, **options)
                state = engine.call("lab-resume")
                for _ in range(state["scenarioLength"]):
                    if state["reportArtifact"]:
                        break
                    task = state["task"]
                    old = state["solved"]
                    if task["keys"]:
                        for _ in range(25):
                            state = chord([sides.get(k, k) for k in task["keys"].split("+")])
                            if state["solved"] > old:
                                break
                    else:
                        for char in task["text"]:
                            chord(["Space" if char == " " else char.upper()], char)
                        state = chord(["Enter"])
                    self.assertEqual(state["solved"], old + 1, (task, state["status"]))
                receipt = state["reportArtifact"]
                self.assertTrue(receipt)
                self.assertEqual(credit(), 1)
                kill_engine()
                # Simulate an interrupted future temporary write beside the committed profile.
                profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
                profile.with_suffix(".xml.writing").write_text("<StudioProfile><LiveDrafts>", encoding="utf-8")
                # Kill two different engine processes. Replay must not enqueue another credit.
                for _ in range(2):
                    engine = Engine(verify=True)
                    self.assertTrue(any(d["mission"] == "workday-report"
                                        for d in engine.call("lab-catalog")["drafts"]))
                    reopened = engine.call("lab-start", **{"continue": True}, **options)
                    self.assertFalse(engine.storage_error)
                    self.assertEqual(reopened["scenarioStep"], state["scenarioStep"])
                    self.assertEqual(reopened["task"], state["task"])
                    self.assertEqual(reopened["reportArtifact"], receipt)
                    files = {f["path"]: f["content"] for f in reopened["scene"]["terminal"]["files"]}
                    self.assertEqual(files[receipt["path"]], receipt["content"])
                    self.assertNotIn("/home/student/Downloads/spectrum-report.txt", files)
                    self.assertEqual((reopened["solved"], reopened["errors"]), (0, 0))
                    self.assertEqual(credit(), 1)
                    kill_engine()
            finally:
                if not engine.process.stdout.closed:
                    engine.close()

    def test_pause_checkpoints_unscored_terminal_edit(self):
        import signal
        from pathlib import Path
        from unittest.mock import patch

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            options = dict(app="terminal", level=0, mode="sandbox", seconds=0, guidance="guided")
            engine = Engine(verify=True)
            try:
                engine.call("lab-start", seed=0, **options)
                engine.call("lab-resume")
                def type_key(key):
                    engine.call("lab-key", key=key, pressed=True, text=key.lower())
                    return engine.call("lab-key", key=key, pressed=False, text="")
                type_key("A")
                state = type_key("B")
                self.assertEqual(state["scene"]["terminalInput"], "ab")
                self.assertEqual((state["solved"], state["errors"]), (0, 0))
                profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
                self.assertFalse(profile.exists(), "An ordinary character must not rewrite the profile")
                engine.call("lab-pause")
                self.assertFalse(engine.storage_error)
                self.assertTrue(profile.is_file())
                self.assertFalse(engine.process.stdin.closed)
                engine.process.kill()
                self.assertEqual(engine.process.wait(timeout=5), -signal.SIGKILL)
                engine.close()
                engine = Engine(verify=True)
                reopened = engine.call("lab-start", **{"continue": True}, **options)
                self.assertEqual(reopened["scene"]["terminalInput"], "ab")
                self.assertEqual((reopened["solved"], reopened["errors"]), (0, 0))
            finally:
                if not engine.process.stdout.closed:
                    engine.close()

    def test_failed_save_keeps_old_run_until_explicit_retry(self):
        from pathlib import Path
        from unittest.mock import patch

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            engine = Engine(verify=True)
            try:
                old = dict(app="workday", level=0, mode="sandbox", seconds=0, guidance="guided")
                new = dict(old, app="terminal")
                started = engine.call("lab-start", mission="workday-report", seed=0, **old)
                self.assertFalse(started["startBlocked"])
                engine.call("lab-resume")
                blocked_path = Path(directory) / "TouchTypingStudio-QA" / "progress.xml.writing"
                # Not a fake store: obstruct the real atomic writer, before publication.
                blocked_path.mkdir(parents=True)
                task = started["task"]
                sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
                keys = [sides.get(key, key) for key in task["keys"].split("+")]
                for key in keys:
                    engine.call("lab-key", key=key, pressed=True, text="")
                for key in reversed(keys):
                    progressed = engine.call("lab-key", key=key, pressed=False, text="")
                self.assertEqual(progressed["solved"], 1)
                self.assertTrue(engine.storage_error)
                self.assertTrue(progressed["saveError"])
                marker = "lab/workday/" + task["id"]

                def credit():
                    return next((row["count"] for row in engine.call("library")["liveSkills"] if row["key"] == marker), 0)

                self.assertEqual(credit(), 0)
                rejected = engine.call("lab-start", **new)
                self.assertTrue(rejected["startBlocked"])
                self.assertFalse(rejected["saved"])
                self.assertTrue(rejected["paused"])
                self.assertFalse(rejected["finished"])
                self.assertEqual(rejected["mission"], progressed["mission"])
                self.assertEqual(rejected["scenarioStep"], progressed["scenarioStep"])
                self.assertEqual(rejected["scene"], progressed["scene"])

                failed_stop = engine.call("lab-stop")
                self.assertTrue(failed_stop["finished"])
                self.assertFalse(failed_stop["saved"])
                self.assertEqual(failed_stop["solved"], 1)
                self.assertEqual(credit(), 0)
                # Polling or reading a result must not perform a hidden retry, even after storage recovers.
                blocked_path.rmdir()
                self.assertFalse(engine.call("lab-state")["saved"])
                self.assertIsNone(engine.call("lab-result"))
                self.assertEqual(credit(), 0)

                saved = engine.call("lab-stop")
                self.assertTrue(saved["saved"])
                self.assertIsNone(saved["saveError"])
                self.assertFalse(engine.storage_error)
                self.assertEqual(saved["scene"], failed_stop["scene"])
                self.assertEqual(credit(), 1)
                self.assertTrue(engine.call("lab-stop")["saved"])
                self.assertEqual(credit(), 1)
                result = engine.call("lab-result")
                self.assertEqual(result["solved"], 1)
                self.assertEqual(len(result["history"]), 1)

                replacement = engine.call("lab-start", **new)
                self.assertFalse(replacement["startBlocked"])
                self.assertEqual(replacement["app"], "terminal")
                self.assertEqual(credit(), 1)
            finally:
                engine.close()

    def walk(self, engine, mission):
        state = engine.call("lab-start", app=mission["app"], level=mission["level"], mode="sandbox",
                            seconds=0, guidance="guided", mission=mission["id"], seed=0)
        total = state["scenarioLength"]
        self.assertGreaterEqual(total, 20)
        state = engine.call("lab-resume")
        def chord(keys, char=""):
            for key in keys:
                engine.call("lab-key", key=key, pressed=True, text=char if key == keys[-1] else "")
            for key in reversed(keys):
                result = engine.call("lab-key", key=key, pressed=False, text="")
            return result
        sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
        for step in range(total):
            self.assertEqual(state["mission"], mission["id"])
            task = state["task"]
            for attempt in range(24):
                if task["keys"]:
                    state = chord([sides.get(k, k) for k in task["keys"].split("+")])
                else:
                    for char in task["text"]:
                        chord(["Space" if char == " " else char.upper()], char)
                    state = chord(["Enter"])
                if state["solved"] == step + 1:
                    break
            self.assertEqual(state["solved"], step + 1, (task, state["status"], state["phase"]))
            if task["id"] == "command/head -n 2 spectrum-report.txt":
                files = {f["path"]: f["content"] for f in state["scene"]["terminal"]["files"]}
                self.assertNotIn("/home/student/Downloads/spectrum-report.txt", files)
                report = files["/home/student/project/spectrum-report.txt"]
                self.assertTrue(report.startswith("# "), report)
                self.assertIn(report.splitlines()[0], state["scene"]["terminal"]["output"])
                if mission["id"] == "workday-report":
                    self.assertEqual(state["reportArtifact"], {
                        "path": "/home/student/project/spectrum-report.txt",
                        "content": report, "downloadStillPresent": False,
                    })
                else:
                    self.assertIsNone(state["reportArtifact"])
            self.assertEqual(state["errors"], 0, (mission["id"], task, state["status"]))


if __name__ == "__main__":
    unittest.main()
