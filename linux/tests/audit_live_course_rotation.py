"""Drive the entire Live rotation, optionally against a pre-change catalog.

Usage: PYTHONPATH=linux:linux/tests python3 linux/tests/audit_live_course_rotation.py \
    --baseline path/to/live-before.json > rotation.jsonl
Each line represents completed scenarios, not mere catalog entries.
"""
import argparse
import json

from test_live_route_policy import LiveRoutePolicyTests, advance, begin, terminal_course


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--baseline")
    cli.add_argument("--app", help="Optional single section")
    cli.add_argument("--desktop", choices=("ubuntu", "windows"))
    cli.add_argument("--level", type=int, choices=(0, 1, 2))
    args = cli.parse_args()
    baseline = {}
    if args.baseline:
        with open(args.baseline, encoding="utf-8") as stream:
            baseline = {(r["profile"], r["app"], r["level"]): set(r["missions"]) for r in json.load(stream)}
    profiles = LiveRoutePolicyTests()
    for desktop in (args.desktop,) if args.desktop else ("ubuntu", "windows"):
        for app in (desktop, "chrome", "code", "terminal", "workday"):
            if args.app and app != args.app:
                continue
            for level in (args.level,) if args.level is not None else range(3):
                def walk(engine):
                    catalog = engine.call("lab-catalog", desktopProfile=desktop)
                    missions = [m for m in catalog["missions"] if m["app"] == app and m["level"] == level]
                    expected = {m["id"] for m in missions}
                    curated = {m["id"] for m in missions if m.get("curated")}
                    old = baseline.get((desktop, app, level), set())
                    legacy_in_ubuntu = set()
                    if app == "terminal" and desktop == "windows":
                        ubuntu = engine.call("lab-catalog", desktopProfile="ubuntu")
                        legacy_in_ubuntu = old & {m["id"] for m in ubuntu["missions"]
                                                  if m["app"] == "terminal" and m["level"] == level}
                    assert old <= expected | legacy_in_ubuntu, (desktop, app, level, sorted(old - expected - legacy_in_ubuntu))
                    s = begin(engine, app, level, desktop, actionLimit=0, seconds=0, adaptiveGuide=True)
                    if app == "terminal":
                        # Cross-level native tasks plus four historical Bash projects per
                        # level. One-word drills remain in the explicit selector only.
                        assert s["mission"] == terminal_course(desktop)
                        expected = {m["id"] for m in catalog["missions"] if m["app"] == "terminal" and
                                    (m["desktopProfile"] == desktop or desktop == "ubuntu" and not m["curated"] and m["level"] == level)}
                        curated = {m["id"] for m in catalog["missions"] if m["id"] in expected and m["curated"]}
                    completed = []
                    for _ in range(2 * len(expected) + 2):
                        if expected <= {name for name, _ in completed}:
                            break
                        before = s["solved"]
                        mission, length = s["mission"], s["scenarioLength"]
                        for i in range(length):
                            assert s["mission"] == mission and s["scenarioCompletedGoals"] == i, (
                                desktop, app, level, mission, length, i, s["scenarioCompletedGoals"])
                            if app == "terminal":
                                assert s["task"].get("text") != "whoami", "No routine name quizzes"
                                assert s["task"].get("keys") != "Ctrl+R", "No forced history search"
                            s = advance(engine, s)
                            assert s["solved"] == before + i + 1 and not s["finished"]
                        completed.append((mission, length))
                    seen = {name for name, _ in completed}
                    assert expected <= seen, (desktop, app, level, sorted(expected - seen))
                    print(json.dumps({"desktop": desktop, "app": app, "level": level,
                        "available": len(expected), "old": len(old), "curated": len(curated),
                        "completed": len(seen), "credits": s["solved"], "sections": completed}, ensure_ascii=False), flush=True)
                profiles.run_profile(walk, desktop)


if __name__ == "__main__":
    main()
