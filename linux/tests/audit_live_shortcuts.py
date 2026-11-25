"""Walk every named Live mission through the app's shared exercise protocol.

    dotnet build linux/engine/TouchTyping.Engine.csproj
    PYTHONPATH=linux python3 linux/tests/audit_live_shortcuts.py --matrix --json path.json

The full matrix crosses the client config (Linux/Windows), desktop simulation
(Ubuntu/Windows 10), and initially active RU/EN. An explicit engine executable
may be supplied through TOUCH_TYPING_ENGINE. This does not exercise compositor
delivery, OS-generated characters, or a physical keyboard. The root course
contract independently covers continued practice from the actual main card.
"""
import argparse
from collections import Counter
import itertools
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

from platform_linux import Engine, EngineError
from test_live_missions import MissionTest


def audit_entry(client, desktop, language):
    counts = Counter()
    courses, failures = [], []
    with TemporaryDirectory() as data, patch.dict(os.environ, {"TOUCH_TYPING_QA_DATA_ROOT": data}):
        engine = Engine(verify=True)
        try:
            call = engine.call
            lib = call("configure", platform=client, layout=language,
                       forward="Super+Space", backward="Super+Shift+Space")
            call("settings", desktopProfile=desktop)
            catalogue = call("lab-catalog")
            assert catalogue["desktopProfile"] == desktop, catalogue["desktopProfile"]
            missions = catalogue["missions"]
            assert len(missions) == len({mission["id"] for mission in missions})
            all_keys = {cap["key"] for row in lib["keyboard"] for cap in row} | {
                cap["key"] for cap in lib.get("keyboardAux", ())
            } | {"Esc", "Print", *[f"F{n}" for n in range(1, 13)]}
            for mission in missions:
                steps = {}
                scenario_length = 0

                def observe(operation, **args):
                    nonlocal scenario_length
                    if operation == "lab-start":
                        args.update(desktopProfile=desktop, nativeEnglishInput=True)
                    elif operation == "lab-key" and args.get("text") and len(args["text"]) == 1:
                        # Live clients require EN when printing English task text.
                        # The active setting at the start/for shortcuts stays
                        # in the tested layout; the driver types required text
                        # after the logical switch. No native keyboard claim.
                        args["nativeLayout"] = "EN" if args["text"].isascii() else "RU"
                    state = call(operation, **args)
                    if operation == "lab-start":
                        scenario_length = state["scenarioLength"]
                    if operation in ("lab-start", "lab-resume", "lab-key") and state["solved"] < scenario_length:
                        task = state["task"]
                        steps[state["solved"]] = {k: task[k] for k in ("id", "keys", "text")}
                    return state

                engine.call = observe
                try:
                    MissionTest().walk(engine, mission)
                    final = call("lab-state")
                    assert final["solved"] == scenario_length == len(steps), final["status"]
                except (AssertionError, EngineError) as error:
                    failures.append({"mission": mission["id"], "diagnostic": str(error), "trace": list(steps.values())})
                else:
                    counts[mission["app"]] += 1
                    courses.append({"id": mission["id"], "app": mission["app"], "level": mission["level"],
                                    "steps": list(steps.values()), "solved": final["solved"],
                                    "finished": final["finished"], "reachedNext": final["mission"] != mission["id"]})
                finally:
                    engine.call = call
            reached = sorted({step["keys"] for row in courses for step in row["steps"] if step["keys"]})
            registered = sorted({task["keys"] for cls in catalogue["classes"] for task in cls["shortcuts"] if task["keys"]})
            missing_caps = set()
            for gesture in sorted(set(reached) | set(registered)):
                try:
                    desc = call("inspect", keys=gesture)
                    # Canonicalized modifier order may differ from authored prompt;
                    # the exact same physical tokens must still be highlighted.
                    expected = [set(chord.split("+")) for chord in gesture.split(" → ")]
                    actual = [{cap["token"] for cap in chord} for chord in desc["steps"]]
                    assert expected == actual, (gesture, desc)
                    missing_caps.update(cap["key"] for chord in desc["steps"] for cap in chord
                                        if cap["key"] not in all_keys)
                except (AssertionError, EngineError) as error:
                    failures.append({"gesture": gesture, "diagnostic": str(error)})
            return {"client": client, "desktop": desktop, "layout": language,
                    "routes": courses, "routeCount": len(courses), "applications": dict(counts), "gestures": reached,
                    "registered": registered, "registeredNotPlayed": sorted(set(registered) - set(reached)),
                    "keyedGoals": sum(bool(s["keys"]) for c in courses for s in c["steps"]),
                    "missingCaps": sorted(missing_caps), "failures": failures}
        finally:
            engine.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", action="store_true", help="all eight profile combinations")
    parser.add_argument("--client", choices=("linux", "windows"))
    parser.add_argument("--desktop", choices=("ubuntu", "windows"))
    parser.add_argument("--layout", choices=("RU", "EN"))
    parser.add_argument("--json", type=Path, metavar="PATH", help="write measured route/goal inventory")
    parser.add_argument("--require-caps", action="store_true", help="fail if an observed physical target cannot be drawn")
    args = parser.parse_args()
    if args.matrix or args.client or args.desktop or args.layout:
        entries = itertools.product((args.client,) if args.client else ("linux", "windows"),
                                    (args.desktop,) if args.desktop else ("ubuntu", "windows"),
                                    (args.layout,) if args.layout else ("RU", "EN"))
    else:
        entries = (("linux", "ubuntu", "EN"), ("linux", "windows", "EN"))
    report = []
    for profile in entries:
        row = audit_entry(*profile)
        report.append(row)
        print(f"{'/'.join(profile)}: {row['routeCount']} passing routes, {row['keyedGoals']} keyed goals; "
              f"{len(row['gestures'])} replayed, {len(row['registered'])} registered; {len(row['failures'])} failures", flush=True)
        if row["missingCaps"]:
            print("Cannot draw: " + ", ".join(row["missingCaps"]), flush=True)
        for failure in row["failures"]:
            print(f"FAIL {failure.get('mission', failure.get('gesture'))}: {failure['diagnostic']}", flush=True)
    if args.json:
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    reached = set(gesture for row in report for gesture in row["gestures"])
    registered = set(gesture for row in report for gesture in row["registered"])
    print(f"Distinct authored gestures played across matrix: {len(reached)}; "
          f"registered: {len(registered)}, registered-only: {len(registered - reached)}")
    print("\n".join(sorted(reached)))
    return int(any(row["failures"] or (args.require_caps and row["missingCaps"]) for row in report))


if __name__ == "__main__":
    raise SystemExit(main())
