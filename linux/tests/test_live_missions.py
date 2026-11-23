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
                   skill_app="code", one_mission=False, one_session=False, one_task=False, layout="EN"):
            return [
                {"Mission": "code-" + names[level][0 if one_mission else index % 3],
                 "Skill": f"lab/{skill_app}/" + tasks[0 if one_task else index % 4],
                 "Verification": "lab-scene", "InputMode": "exact", "Layout": layout,
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
            ("unknown-layout", sample(0, layout=None), None),
            ("split-layout", sample(0, count=5) + sample(0, count=5, layout="RU"), None),
            ("only-ru", sample(0, layout="RU"), None),
            ("en-not-confirmed-by-ru", sample(0, one_task=True, layout="RU"), None),
            ("unknown-cannot-confirm", sample(0, one_task=True, layout=None), None),
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
                    if label in {"editor-basic", "only-ru", "unknown-layout", "split-layout"}:
                        engine.call("configure", layout="RU")
                        ru_tip = next(item for item in engine.call("lab-catalog")["recommendations"] if item["app"] == "code")
                        self.assertEqual(ru_tip["level"], 1 if label == "only-ru" else None)
                        self.assertIn("RU", ru_tip["reason"])
                        engine.call("configure", layout="EN")
                        self.assertEqual(profile.read_bytes(), previous)
                    if label in {"en-not-confirmed-by-ru", "unknown-cannot-confirm"}:
                        def mastery():
                            return next(item for item in engine.call("library")["liveSkills"] if item["key"] == "lab/code/explorer")
                        self.assertEqual((mastery()["count"], mastery()["confirmed"]), (10, False))
                        engine.call("configure", layout="RU")
                        self.assertEqual((mastery()["count"], mastery()["confirmed"]), (10, label == "en-not-confirmed-by-ru"))
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

    def test_live_attempt_layout_stamps_before_toggle_and_survives_reopen(self):
        from pathlib import Path
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            profile.parent.mkdir()
            # A real older record had no layout; do not infer one on save.
            profile.write_text(
                "<StudioProfile><ShortcutHistory><ShortcutAttempt>"
                "<Skill>lab/ubuntu/layout</Skill><Mission>ubuntu-languages</Mission>"
                "<Correct>true</Correct></ShortcutAttempt></ShortcutHistory></StudioProfile>",
                encoding="utf-8",
            )
            def records():
                return [(row.findtext("Skill"), row.findtext("Layout"), row.findtext("Correct"))
                        for row in ET.parse(profile).findall("./ShortcutHistory/ShortcutAttempt")]

            options = dict(app="ubuntu", level=1, mode="sandbox", seconds=0,
                           guidance="guided", mission="ubuntu-languages", seed=0)
            sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}
            engine = Engine(verify=True)
            def chord(keys):
                for key in keys:
                    engine.call("lab-key", key=key, pressed=True, text="")
                for key in reversed(keys):
                    result = engine.call("lab-key", key=key, pressed=False, text="")
                return result
            def advance(state):
                task = state["task"]
                self.assertTrue(task["keys"], task["id"])
                for _ in range(24):
                    result = chord([sides.get(key, key) for key in task["keys"].split("+")])
                    if result["solved"] > state["solved"]:
                        return result
                self.fail(("unreachable", task["id"], result))
            try:
                engine.call("lab-start", **options)
                state = engine.call("lab-resume")
                for _ in range(24):
                    if state["task"]["id"] == "layout":
                        break
                    state = advance(state)
                self.assertEqual((state["task"]["id"], state["scene"]["layout"]), ("layout", "EN"))
                state = chord(["RCtrl", "F12"])
                self.assertEqual(records()[-1], ("lab/ubuntu/layout", "EN", "false"))
                state = advance(state)
                self.assertEqual((state["task"]["id"], state["scene"]["layout"]), ("overview", "RU"))
                self.assertEqual(records()[-1], ("lab/ubuntu/layout", "EN", "true"))
                engine.call("lab-stop")
                before = records()
                self.assertEqual(before[0], ("lab/ubuntu/layout", None, "true"))
                engine.close()
                engine = Engine(verify=True)
                state = engine.call("lab-start", **options, **{"continue": True})
                self.assertEqual((state["task"]["id"], state["scene"]["layout"]), ("overview", "RU"))
                self.assertEqual(records(), before)
                state = advance(engine.call("lab-resume"))
                self.assertEqual(records()[-1], ("lab/ubuntu/overview", "RU", "true"))
                for _ in range(24):
                    if state["task"]["id"] == "layout":
                        break
                    state = advance(state)
                self.assertEqual((state["task"]["id"], state["scene"]["layout"]), ("layout", "RU"))
                state = advance(state)
                self.assertEqual(state["scene"]["layout"], "EN")
                self.assertEqual(records()[-1], ("lab/ubuntu/layout", "RU", "true"))
                self.assertEqual(records()[:len(before)], before)
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
                    self.assertEqual(len(missions), 52)
                    self.assertEqual(len({m["id"] for m in missions}), 52)
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

    def test_browser_note_is_copied_saved_and_restored_after_engine_exit(self):
        from pathlib import Path
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        options = dict(app="workday", level=0, mode="sandbox", seconds=0,
                       guidance="guided", mission="workday-source-note", seed=0)
        target = "/home/student/project/notes.txt"
        original = "spectrum\nsignal\nvector\nsignal\n"
        source_url = "https://practice.local/spectrum"
        method = (
            "Спектр сигнала\n"
            "Прямое преобразование возвращает комплексный спектр.\n"
            "Центрированный спектр позволяет сопоставить частоты.\n"
            "Материалы и отчёт доступны ниже."
        )
        sides = {"Ctrl": "RCtrl", "Alt": "LAlt", "Shift": "LShift", "Super": "LSuper"}

        for platform, layout in (("linux", "EN"), ("windows", "RU")):
            with self.subTest(platform=platform, layout=layout), tempfile.TemporaryDirectory() as directory, \
                    patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
                engine = Engine(verify=True)

                def chord(keys, char=""):
                    for key in keys:
                        state = engine.call("lab-key", key=key, pressed=True,
                                            text=char if key == keys[-1] else "")
                        self.assertFalse(engine.storage_error)
                    for key in reversed(keys):
                        state = engine.call("lab-key", key=key, pressed=False, text="")
                        self.assertFalse(engine.storage_error)
                    return state

                def type_command(command):
                    for char in command:
                        chord(["Space" if char == " " else char.upper()], char)
                    return chord(["Enter"])

                def advance(state):
                    task = state["task"]
                    if task["keys"]:
                        for _ in range(24):
                            following = chord([sides.get(k, k) for k in task["keys"].split("+")])
                            if following["solved"] != state["solved"]:
                                break
                    else:
                        self.assertTrue(task["id"].startswith("command/"), task)
                        following = type_command(task["text"])
                    self.assertEqual(following["solved"], state["solved"] + 1, (task, following["status"]))
                    self.assertEqual(following["errors"], state["errors"], (task, following["status"]))
                    return following

                def saved_file(state):
                    return next(f["content"] for f in state["scene"]["terminal"]["files"] if f["path"] == target)

                def active(state):
                    return next(w for w in state["scene"]["windows"] if w["active"])

                def saves():
                    profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
                    return [node for node in ET.parse(profile).findall("./ShortcutHistory/ShortcutAttempt")
                            if node.findtext("Mission") == options["mission"]
                            and node.findtext("Skill") == "lab/workday/save"
                            and node.findtext("Correct") == "true"]

                def start(resume=False):
                    engine.call("configure", platform=platform, layout=layout)
                    return engine.call("lab-start", **options, **{"continue": resume})

                try:
                    start()
                    state = engine.call("lab-resume")
                    milestones = []
                    for _ in range(state["scenarioLength"]):
                        task_id = state["task"]["id"]
                        state = advance(state)
                        if task_id == "copy":
                            self.assertEqual((active(state)["url"], active(state)["body"]), (source_url, method))
                            self.assertEqual(state["scene"]["clipboard"], method)
                            milestones.append(task_id)
                        elif task_id == "paste":
                            self.assertEqual((active(state)["url"], active(state)["body"]), (target, method))
                            self.assertEqual(saved_file(state), original)
                            milestones.append(task_id)
                        elif task_id == "save":
                            self.assertEqual(saved_file(state), method)
                            self.assertEqual((active(state)["cursor"], active(state)["anchor"]), (0, 0))
                            self.assertEqual(len(saves()), 1)
                            milestones.append(task_id)
                            break
                    self.assertEqual(milestones, ["copy", "paste", "save"])
                    step = state["scenarioStep"]
                    # Kill with stdin open: graceful shutdown would save again and mask a missing checkpoint.
                    self.assertFalse(engine.process.stdin.closed)
                    engine.process.kill()
                    self.assertNotEqual(engine.process.wait(timeout=5), 0)
                    engine.close()
                    engine = Engine(verify=True)
                    state = start(resume=True)
                    self.assertEqual(state["scenarioStep"], step)
                    self.assertEqual(saved_file(state), method)
                    self.assertEqual((active(state)["url"], active(state)["body"]), (target, method))
                    self.assertEqual((active(state)["cursor"], active(state)["anchor"]), (0, 0))
                    self.assertEqual(state["scene"]["clipboard"], method)
                    self.assertEqual(len(saves()), 1)
                    state = engine.call("lab-resume")
                    readback = reopened_note = False
                    for _ in range(state["scenarioLength"]):
                        if state["finished"]:
                            break
                        task_id = state["task"]["id"]
                        if task_id == "command/cat notes.txt" and platform == "linux":
                            # Even an extra trailing LF is wrong (terminal output trims it).
                            self.assertEqual(active(chord(["LSuper", "2"]))["app"], "code")
                            chord(["RCtrl", "A"])
                            chord(["RCtrl", "V"])
                            chord(["Enter"])
                            dirty = chord(["RCtrl", "S"])
                            self.assertEqual(saved_file(dirty), method + "\n")
                            self.assertEqual(active(chord(["RCtrl", "LAlt", "T"]))["app"], "terminal")
                            wrong = type_command("cat notes.txt")
                            self.assertTrue(wrong["scene"]["terminal"]["lastSuccess"])
                            self.assertEqual(wrong["scene"]["terminal"]["output"][-4:], method.splitlines())
                            self.assertEqual(wrong["solved"], state["solved"])
                            self.assertEqual(wrong["errors"], state["errors"] + 1)
                            self.assertIn("заметка отличается", wrong["status"])
                            self.assertFalse(wrong["finished"])
                            self.assertEqual(active(chord(["LSuper", "2"]))["app"], "code")
                            chord(["RCtrl", "A"])
                            chord(["RCtrl", "V"])
                            repaired = chord(["RCtrl", "S"])
                            self.assertEqual(saved_file(repaired), method)
                            state = chord(["RCtrl", "LAlt", "T"])
                        state = advance(state)
                        if task_id == "file-open" and active(state)["app"] == "code":
                            self.assertEqual((active(state)["url"], active(state)["body"]), (target, method))
                            reopened_note = True
                        if task_id == "command/cat notes.txt":
                            self.assertTrue(state["scene"]["terminal"]["lastSuccess"])
                            self.assertEqual(state["scene"]["terminal"]["output"][-4:], method.splitlines())
                            readback = True
                    self.assertTrue(state["finished"])
                    self.assertTrue(state["saved"])
                    self.assertTrue(engine.call("lab-result")["completed"])
                    self.assertEqual(state["mission"], options["mission"])
                    self.assertTrue(reopened_note)
                    self.assertTrue(readback)
                    self.assertEqual(active(state)["app"], "terminal")
                    self.assertEqual(saved_file(state), method)
                    self.assertEqual(len(saves()), 1)
                    engine.close()
                    engine = Engine(verify=True)
                    self.assertFalse(any(draft["mission"] == options["mission"]
                                         for draft in engine.call("lab-catalog")["drafts"]))
                    self.assertEqual(len(saves()), 1)
                finally:
                    engine.close()

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
                    self.assertEqual((reopened["solved"], reopened["errors"]), (before["solved"], before["errors"]))
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
                    self.assertEqual((reopened["solved"], reopened["errors"]), (state["solved"], state["errors"]))
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

    def test_resume_original_budget_and_retry_one_final_session(self):
        from pathlib import Path
        from time import sleep
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            options = dict(app="terminal", level=1, mode="sandbox", seconds=180,
                           actionLimit=2, guidance="guided", mission="terminal-access", seed=7)
            profile = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            blocked = profile.with_suffix(".xml.writing")

            def command(engine, text):
                for char in text:
                    key = {" ": "Space", "/": "Slash"}.get(char, char.upper())
                    engine.call("lab-key", key=key, pressed=True, text=char)
                    engine.call("lab-key", key=key, pressed=False, text="")
                return engine.call("lab-key", key="Enter", pressed=True, text="")

            engine = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                first = engine.call("lab-start", **options)
                self.assertEqual((first["timeLimit"], first["actionLimit"]), (0, 2))
                engine.call("lab-resume")
                sleep(1.1)
                missed = command(engine, "wrong")
                self.assertEqual(missed["errors"], 1)
                engine.call("lab-key", key="Enter", pressed=False, text="")
                first = command(engine, first["task"]["text"])
                self.assertEqual((first["solved"], first["errors"]), (1, 1))
                early = engine.call("lab-stop")
                self.assertTrue(early["saved"])
                self.assertGreaterEqual(early["seconds"], first["seconds"])
                self.assertGreaterEqual(early["seconds"], 1)
                self.assertFalse(engine.call("lab-result")["completed"])
                before = ET.parse(profile)
                session = before.findtext("./LiveDrafts/LiveDraft/Progress/Session")
                self.assertTrue(session)
                self.assertEqual(before.findtext("./LabSessions/LabSessionRecord/Session"), session)
                self.assertEqual(len(before.findall("./ShortcutHistory/ShortcutAttempt")), 2)
                # Do not let normal engine shutdown supply any extra save.
                engine.process.kill()
                engine.process.wait(timeout=5)
            finally:
                engine.close()

            sleep(1.1)  # Wall time while closed cannot enter the saved stopwatch.
            engine = Engine(verify=True)
            shadow = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                shadow.call("configure", layout="EN")
                changed = dict(options, mode="drill", seconds=90, actionLimit=0)
                restored = engine.call("lab-start", **changed, **{"continue": True})
                self.assertEqual(restored["task"], early["task"])
                self.assertEqual((restored["mode"], restored["seconds"], restored["timeLimit"], restored["actionLimit"],
                                  restored["solved"], restored["errors"]), ("sandbox", early["seconds"], 0, 2, 1, 1))
                competing = shadow.call("lab-start", **changed, **{"continue": True})
                self.assertEqual(competing["solved"], 1)
                shadow.call("lab-resume")
                engine.call("lab-resume")
                original_bytes = profile.read_bytes()
                blocked.mkdir()
                rejected = command(engine, restored["task"]["text"])
                self.assertEqual((rejected["solved"], rejected["errors"]), (2, 1))
                self.assertTrue(rejected["finished"])
                self.assertFalse(rejected["saved"])
                self.assertTrue(rejected["saveError"])
                self.assertEqual(profile.read_bytes(), original_bytes)
                blocked.rmdir()

                self.assertIsNone(engine.call("lab-result"))
                self.assertTrue(engine.call("lab-stop")["saved"])
                result = engine.call("lab-result")
                self.assertTrue(result["completed"])
                self.assertEqual((result["solved"], result["errors"]), (2, 1))
                self.assertEqual(len(result["series"]), 3)
                self.assertTrue(engine.call("lab-stop")["saved"])
                final = ET.parse(profile)
                self.assertIsNone(final.find("./LiveDrafts/LiveDraft"))
                records = final.findall("./LabSessions/LabSessionRecord")
                self.assertEqual([(r.findtext("Session"), r.findtext("Completed")) for r in records], [(session, "true")])
                credits = final.findall("./ShortcutHistory/ShortcutAttempt")
                self.assertEqual([row.findtext("Session") for row in credits], [session] * 3)
                committed_bytes = profile.read_bytes()
                stale = command(shadow, competing["task"]["text"])
                self.assertFalse(stale["saved"])
                self.assertIn("в другом окне", stale["saveError"])
                self.assertFalse(shadow.call("lab-stop")["saved"])
                self.assertEqual(profile.read_bytes(), committed_bytes)
            finally:
                shadow.close()
                engine.close()

    def test_expired_timer_cleans_checkpoint_even_without_credited_actions(self):
        from time import sleep
        from unittest.mock import patch

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            options = dict(app="terminal", level=1, mode="sandbox", seconds=2,
                           guidance="guided", mission="terminal-access", seed=7)
            engine = Engine(verify=True)
            try:
                engine.call("lab-start", **options)
                engine.call("lab-resume")
                sleep(0.1)
                paused = engine.call("lab-pause")
                self.assertEqual((paused["solved"], paused["errors"]), (0, 0))
                self.assertFalse(paused["finished"])
                self.assertEqual(len(engine.call("lab-catalog")["drafts"]), 1)
                engine.process.kill()
                engine.process.wait(timeout=5)
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                restored = engine.call("lab-start", **dict(options, seconds=0), **{"continue": True})
                self.assertTrue(restored["paused"])
                self.assertEqual(restored["timeLimit"], 2)
                engine.call("lab-resume")
                sleep(2.1)
                done = engine.call("lab-pause")
                self.assertTrue(done["finished"])
                self.assertTrue(done["saved"])
                self.assertTrue(engine.call("lab-result")["completed"])
                self.assertEqual(engine.call("lab-catalog")["drafts"], [])
            finally:
                engine.close()

    def test_soft_timer_choice_restarts_with_same_input_and_one_result(self):
        from pathlib import Path
        from time import monotonic, sleep
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            opts = dict(app="terminal", level=1, mode="sandbox", seconds=2, guidance="guided",
                        mission="terminal-access", seed=7, softFinish=True, combinedLimits=True, actionLimit=20)
            saved_path = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"

            def type_text(engine, value):
                for char in value:
                    key = {" ": "Space", "/": "Slash"}.get(char, char.upper())
                    engine.call("lab-key", key=key, pressed=True, text=char)
                    engine.call("lab-key", key=key, pressed=False, text="")

            def crash(engine):
                engine.process.kill()
                engine.process.wait(timeout=5)

            engine = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                started = engine.call("lab-start", **opts)
                self.assertTrue(started["task"]["text"].startswith("cd "))
                target = started["task"]["text"]
                prefix = target[:-3]
                engine.call("lab-resume")
                type_text(engine, prefix)
                deadline = monotonic() + 6
                pending = engine.call("lab-state")
                while not pending["limitDecisionPending"] and monotonic() < deadline:
                    sleep(0.05)
                    pending = engine.call("lab-state")
                self.assertTrue(pending["limitDecisionPending"])
                self.assertEqual(pending["limitReached"], "timer")
                self.assertTrue(pending["paused"])
                self.assertFalse(pending["finished"])
                self.assertEqual(pending["scene"]["terminalInput"], prefix)
                self.assertEqual(pending["remaining"], 0)
                self.assertIsNone(engine.call("lab-result"))
                session = ET.parse(saved_path).findtext("./LiveDrafts/LiveDraft/Progress/Session")
                self.assertTrue(session)
                crash(engine)
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                changed = dict(opts, seconds=0, actionLimit=100, **{"continue": True})
                held = engine.call("lab-start", **changed)
                self.assertTrue(held["limitDecisionPending"])
                self.assertEqual(held["scene"]["terminalInput"], prefix)
                self.assertEqual((held["timeLimit"], held["actionLimit"], held["limitReached"]), (2, 20, "timer"))
                self.assertTrue(engine.call("lab-resume")["paused"])
                side = saved_path.with_suffix(".xml.writing")
                original = saved_path.read_bytes()
                side.mkdir()
                rejected = engine.call("lab-limit-choice", choice="continue")
                self.assertTrue(rejected["limitDecisionPending"])
                self.assertFalse(rejected["finishingMission"])
                self.assertTrue(rejected["saveError"])
                self.assertEqual(saved_path.read_bytes(), original)
                side.rmdir()
                extended = engine.call("lab-limit-choice", choice="continue")
                self.assertTrue(extended["finishingMission"])
                self.assertEqual(extended["limitReached"], "timer")
                self.assertFalse(extended["limitDecisionPending"])
                self.assertTrue(extended["paused"])
                self.assertEqual(extended["scene"]["terminalInput"], prefix)
                self.assertIsNone(engine.call("lab-result"))
                self.assertTrue(engine.call("lab-stop")["saved"])  # close/pause without grading
                self.assertIsNone(engine.call("lab-result"))
                self.assertFalse(ET.parse(saved_path).findall("./LabSessions/LabSessionRecord"))
                crash(engine)
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                extended = engine.call("lab-start", **changed)
                self.assertTrue(extended["finishingMission"])
                self.assertEqual(extended["limitReached"], "timer")
                self.assertFalse(extended["limitDecisionPending"])
                engine.call("lab-resume")
                type_text(engine, target[-3:])
                engine.call("lab-key", key="Enter", pressed=True, text="")
                counted = engine.call("lab-key", key="Enter", pressed=False, text="")
                self.assertEqual((counted["solved"], counted["errors"]), (1, 0))
                self.assertFalse(counted["finished"])
                self.assertIsNone(engine.call("lab-result"))
                side = saved_path.with_suffix(".xml.writing")
                original = saved_path.read_bytes()
                side.mkdir()
                blocked_finish = engine.call("lab-limit-choice", choice="finish")
                self.assertTrue(blocked_finish["finished"])
                self.assertFalse(blocked_finish["saved"])
                self.assertTrue(blocked_finish["saveError"])
                self.assertIsNone(engine.call("lab-result"))
                self.assertEqual(saved_path.read_bytes(), original)
                side.rmdir()
                done = engine.call("lab-limit-choice", choice="finish")
                self.assertTrue(done["finished"])
                self.assertTrue(done["saved"])
                self.assertTrue(engine.call("lab-result")["completed"])
                for _ in range(2):
                    engine.call("lab-limit-choice", choice="finish")
                    engine.call("lab-stop")
                profile = ET.parse(saved_path)
                self.assertFalse(profile.findall("./LiveDrafts/LiveDraft"))
                self.assertEqual([row.findtext("Session") for row in profile.findall("./LabSessions/LabSessionRecord")],
                                 [session])
                self.assertEqual([row.findtext("Session") for row in profile.findall("./ShortcutHistory/ShortcutAttempt")],
                                 [session])
            finally:
                engine.close()

    def test_combined_action_cap_wins_and_survives_restart(self):
        from pathlib import Path
        from unittest.mock import patch
        import xml.etree.ElementTree as ET

        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": directory}):
            opts = dict(app="terminal", level=1, mode="sandbox", seconds=180, guidance="guided",
                        mission="terminal-access", seed=7, softFinish=True, combinedLimits=True, actionLimit=1)
            profile_path = Path(directory) / "TouchTypingStudio-QA" / "progress.xml"
            engine = Engine(verify=True)
            try:
                engine.call("configure", layout="EN")
                initial = engine.call("lab-start", **opts)
                self.assertEqual((initial["timeLimit"], initial["actionLimit"]), (180, 1))
                self.assertIsNone(initial["limitReached"])
                engine.call("lab-resume")
                for char in initial["task"]["text"]:
                    key = {" ": "Space", "/": "Slash"}.get(char, char.upper())
                    engine.call("lab-key", key=key, pressed=True, text=char)
                    engine.call("lab-key", key=key, pressed=False, text="")
                waiting = engine.call("lab-key", key="Enter", pressed=True, text="")
                self.assertTrue(waiting["limitDecisionPending"])
                self.assertEqual((waiting["limitReached"], waiting["solved"]), ("actions", 1))
                self.assertGreater(waiting["remaining"], 0)
                session = ET.parse(profile_path).findtext("./LiveDrafts/LiveDraft/Progress/Session")
                engine.process.kill()
                engine.process.wait(timeout=5)
            finally:
                engine.close()

            engine = Engine(verify=True)
            try:
                restored = engine.call("lab-start", **dict(opts, seconds=600, actionLimit=100, **{"continue": True}))
                self.assertTrue(restored["limitDecisionPending"])
                self.assertEqual((restored["timeLimit"], restored["actionLimit"], restored["limitReached"]),
                                 (180, 1, "actions"))
                self.assertEqual(restored["scene"]["terminalInput"], waiting["scene"]["terminalInput"])
                self.assertTrue(engine.call("lab-limit-choice", choice="finish")["saved"])
                profile = ET.parse(profile_path)
                self.assertEqual([r.findtext("Session") for r in profile.findall("./LabSessions/LabSessionRecord")], [session])
                self.assertEqual([r.findtext("Session") for r in profile.findall("./LabRecords/LabSessionRecord")], [session])
                self.assertFalse(profile.findall("./LiveDrafts/LiveDraft"))
            finally:
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
