"""Count TouchTyping's own generated material, not people's typing behaviour.

Run with TOUCH_TYPING_ENGINE on a built .NET engine or packaged executable.
Uses only a disposable profile and synthetic samples; never reads a user profile.
"""
import argparse
from collections import Counter
import json
import os
from pathlib import Path
import re
from statistics import median
from tempfile import TemporaryDirectory
import xml.etree.ElementTree as ET

from platform_linux import Engine


def language(value):
    if len(value) == 1 and value.isascii() and value.isalpha():
        return "EN"
    if "а" <= value.lower() <= "я" or value.lower() == "ё":
        return "RU"
    return None


def accepted_first(engine, state):
    if state.get("expectedKind") == "command":
        alias = dict(Ctrl="LCtrl", Shift="LShift", Alt="LAlt", Super="LSuper")
        sequence = [alias.get(key, key) for key in state["expected"].split("+")]
        for seq, down in ((sequence, True), (sequence[::-1], False)):
            for key in seq:
                state = engine.call("typing-physical", key=key, pressed=down, layout=state["wantedLayout"])
    else:
        raw = state["after"][0]
        state = engine.call("typing-key", text="\b" if raw == "⌫" else raw, layout=state["wantedLayout"])
    assert state["progressDone"] == 1, (state["mode"], state["after"])
    return state


def plan(engine, path, mode, layout, level, seed):
    state = engine.call("typing-start", mode=mode, level=level, layout=layout, seed=seed, cycles=1, round=0)
    state = accepted_first(engine, state)
    engine.call("typing-suspend", layout=state["wantedLayout"])
    draft = json.loads(ET.parse(path).findtext("ClassicTypingDraft"))
    assert draft["id"] == state["id"] and draft["mode"] == mode and draft["done"] == 1, mode
    if draft["format"] == "mixed-v1":
        return [(unit["kind"], unit["value"], unit["layout"]) for unit in draft["units"]]
    else:
        assert draft["format"] == "classic-v1"
        return [("text", c, language(c)) for cycle in draft["plan"]["cycles"] for fragment in cycle for c in fragment]


def details(samples):
    features, commands, signs, switches, lengths = Counter(), Counter(), Counter(), [], []
    for sample in samples:
        lengths.append(len(sample))
        # Letter-layer switches count actual script changes, not the app's
        # assigned layout for a digit/space/command that can be neutral.
        script = [language(value) for kind, value, _ in sample if kind == "text" and language(value)]
        switches.append(sum(a != b for a, b in zip(script, script[1:])))
        for kind, value, layout in sample:
            if kind == "command":
                features["command"] += 1
                commands[value] += 1
            elif lang := language(value):
                features[lang + ("_UPPER" if value.isupper() else "_lower")] += 1
            elif value.isdigit():
                features["digit"] += 1
            elif value.isspace():
                features["whitespace"] += 1
            else:
                features["symbol"] += 1
                signs[value] += 1
    return {"samples": len(samples), "units": sum(lengths), "length_min": min(lengths), "length_max": max(lengths),
            "script_switches_min": min(switches), "script_switches_median": median(switches), "script_switches_max": max(switches),
            "counts": dict(sorted(features.items())), "commands": dict(commands.most_common()), "symbols": dict(signs.most_common())}


def audit():
    with TemporaryDirectory(prefix="tt-typing-material-") as tmp:
        os.environ["TOUCH_TYPING_QA_DATA_ROOT"] = tmp
        profile = Path(tmp) / "TouchTypingStudio-QA" / "progress.xml"
        e = Engine(verify=True)
        try:
            lib = e.call("configure", platform="linux", layout="EN")
            for layout, mode in (("RU", "Russian"), ("EN", "English")):
                state = e.call("typing-start", mode=mode, level=0, layout=layout, seed=0)
                miss = e.call("typing-key", text="1", layout=layout)
                assert (miss["errors"], miss["progressDone"]) == (1, 0)
                accepted_first(e, state)
                e.call("typing-stop", layout=layout)
            for title, content, symbols in (("Study text", "Отчёт report 09:30; issue #486", False),
                                            ("Study symbols", "№#_%?!_@=^", True)):
                lib = e.call("typing-save-set", title=title, content=content, symbols=symbols)
            cat = lib["typing"]
            result = {"roster": {kind: [{"id": v["id"], "title": v["title"]} for v in cat[kind]]
                                 for kind in ("modes", "lessons", "projects", "sets")},
                      "primary": {}, "authored": {}, "custom": {}}
            for item in cat["modes"]:
                m = item["id"]
                result["primary"][m] = {str(level): details([plan(e, profile, m, layout, level, seed)
                    for layout in ("RU", "EN") for seed in (3, 11, 19)]) for level in range(3)}
            for item in cat["lessons"] + cat["projects"]:
                m = item["id"]
                result["authored"][m] = details([plan(e, profile, m, "EN", 0, 3)])
            for item in cat["sets"]:
                m = item["id"]
                result["custom"][item["title"]] = details([plan(e, profile, m, "EN", 0, 3)])
            return result
        finally:
            e.close()


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--json", type=Path, required=True)
    options = cli.parse_args()
    result = audit()
    options.json.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print({kind: len(result["roster"][kind]) for kind in result["roster"]})
