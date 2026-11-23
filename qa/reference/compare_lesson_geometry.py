#!/usr/bin/env python3
"""Compare measured native lesson workspaces without scaling or guessed baselines."""
import argparse
import json
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("baseline", type=Path)
p.add_argument("compact", type=Path)
args = p.parse_args()
baseline, compact = [json.loads(path.read_text(encoding="utf-8-sig")) for path in (args.baseline,args.compact)]
for key in ("app","platform","viewport","scale","dpi"):
    if baseline.get(key) != compact.get(key):
        p.error(f"cannot compare different native fixtures: {key} {baseline.get(key)} vs {compact.get(key)}")
if baseline["capture"] != "baseline" or compact["capture"] != "compact":
    p.error("supply the baseline followed by the compact record")
if not baseline["titleMapped"] or compact["titleMapped"]:
    p.error("expected baseline redundant strip to be present and compact strip absent")
gain = round(compact["scene"][3] - baseline["scene"][3],2)
if gain <= 0:
    p.error(f"actual work surface did not increase: {gain} logical/physical px")
print(f"{compact['platform']} · {compact['app']} · {compact['viewport']}: "
      f"scene {baseline['scene'][3]} → {compact['scene'][3]} px (+{gain:g})")
