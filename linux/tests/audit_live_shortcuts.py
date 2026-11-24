"""Audit distinct guided Live goals through the same protocol as test_live_missions.

Place beside test_live_missions.py and run from the repository root:
    dotnet build linux/engine/TouchTyping.Engine.csproj
    PYTHONPATH=linux python3 linux/tests/audit_live_shortcuts.py
Set TOUCH_TYPING_ENGINE to test a particular CLI engine build. This is an
engine test; no XKB, Win32 or real hardware shortcut delivery is exercised.
"""
from collections import Counter
import os
from tempfile import TemporaryDirectory
from unittest.mock import patch

from platform_linux import Engine
from test_live_missions import MissionTest


def main():
    totals, reached, failures = Counter(), set(), []
    for profile in ("ubuntu", "windows"):
        with TemporaryDirectory() as data, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": data}):
            engine = Engine(verify=True)
            try:
                call = engine.call
                call("settings", desktopProfile=profile)
                catalogue = call("lab-catalog")
                assert catalogue["desktopProfile"] == profile, catalogue["desktopProfile"]
                missions = catalogue["missions"]
                assert len(missions) == len({mission["id"] for mission in missions})
                for mission in missions:
                    goal_keys = {}

                    def observe(operation, **args):
                        state = call(operation, **args)
                        if operation in ("lab-start", "lab-resume", "lab-key") and not state.get("finished"):
                            task = state["task"]
                            if task["keys"]:
                                goal_keys[state["solved"]] = task["keys"]
                        return state

                    engine.call = observe
                    try:
                        MissionTest().walk(engine, mission)
                    except AssertionError as error:
                        failures.append((profile, mission["id"], str(error)))
                    else:
                        totals[profile, "routes"] += 1
                        totals[profile, "keyed_goals"] += len(goal_keys)
                        reached.update(goal_keys.values())
                    finally:
                        engine.call = call
            finally:
                engine.close()

    for profile in ("ubuntu", "windows"):
        print(f"{profile}: {totals[profile, 'routes']} passing routes, "
              f"{totals[profile, 'keyed_goals']} keyed goals")
    print(f"Distinct guided strings: {len(reached)}")
    print("\n".join(sorted(reached)))
    for profile, mission, error in failures:
        print(f"FAIL {profile}/{mission}: {error}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
