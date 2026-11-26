"""Cross-binary witness for a saved checkout exercise including a half-typed command.

Usage: PYTHONPATH=linux python3 linux/tests/check_checkout_upgrade.py OLD_ENGINE NEW_ENGINE
Use engines extracted from two separately verified archives.
"""
import argparse
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

from platform_linux import Engine
from test_editor_course import advance, enter, begin
from test_project_file_practice import active, chord, disk

LOG_CMD = "python3 -m unittest test_checkout > checkout-test.txt"
PATH = "/home/student/project/checkout/checkout-test.txt"


def run_pair(old_binary, new_binary, platform):
    options = dict(mission="code-repair-checkout", app="code", level=1,
                   routeKind="mission", actionLimit=0, seconds=0, seed=1)
    with TemporaryDirectory() as profile, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": profile}):
        def open_engine(path):
            with patch.dict(os.environ, {"TOUCH_TYPING_ENGINE": str(path)}):
                client = Engine(verify=True)
            client.call("configure", platform=platform, layout="EN")
            return client

        old = open_engine(old_binary)
        try:
            state = begin(old, **options)
            for _ in range(45):
                if state["task"].get("text") == LOG_CMD:
                    break
                state = advance(old, state)
            assert state["task"]["text"] == LOG_CMD
            prefix = "python3 -m unittest test_"
            enter(old, prefix)
            chord(old, "Left")
            saved = old.call("lab-state")
            assert saved["scene"]["terminalInput"] == prefix, saved["scene"]["terminalInput"]
            assert old.call("lab-stop")["saved"]
        finally:
            old.close()

        new = open_engine(new_binary)
        try:
            restored = begin(new, **{**options, "mission": "", "continue": True})
            assert restored["task"]["text"] == LOG_CMD, restored["task"]
            assert restored["scene"]["terminalInput"] == prefix
            assert (restored["solved"], restored["errors"], active(restored)["cursor"]) == (
                saved["solved"], saved["errors"], active(saved)["cursor"])
            chord(new, "End")
            enter(new, LOG_CMD[len(prefix):])
            state = chord(new, "Enter")
            assert state["solved"] == saved["solved"] + 1, state["status"]
            assert "Ran 2 tests\nOK" in disk(state)[PATH]
            while not state["finished"]:
                state = advance(new, state)
            assert active(state)["url"] == PATH and "Ran 2 tests\nOK" in active(state)["body"]

            state = begin(new, **options)
            for _ in range(45):
                if state["task"].get("text", "").startswith(LOG_CMD):
                    break
                state = advance(new, state)
            assert state["task"]["text"] == LOG_CMD + " 2>&1", state["task"]
            state = advance(new, state)
            assert "Ran 2 tests\nOK" in disk(state)[PATH]
            print(f"PASS {platform} profile: previous command and caret resume, fresh command captures stderr")
        finally:
            new.close()


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("old_engine", type=lambda value: Path(value).resolve(strict=True))
    p.add_argument("new_engine", type=lambda value: Path(value).resolve(strict=True))
    args = p.parse_args()
    for platform in ("linux", "windows"):
        run_pair(args.old_engine, args.new_engine, platform)
