"""Windows upgrade check for real bundled engines; never touches the regular profile.

Run with Python 3.11+ from the current source tree, PYTHONPATH=<source>/linux:
    python windows/Test-UpgradeProfile.py OLD_ENGINE NEW_ENGINE NEW_ISOLATED_OUTPUT
Use the two Windows PE engines for Windows storage/protocol proof. Linux engines
can exercise the same test mechanics, not Windows behavior. No GUI assertions.
"""
import os, sys, json, signal
from pathlib import Path
from datetime import datetime, timezone
import xml.etree.ElementTree as ET
from platform_linux import Engine

if not __debug__:
    raise RuntimeError("QA assertions must not be disabled")

old_bin, new_bin, output = map(lambda p: Path(p).resolve(), sys.argv[1:])
assert old_bin.is_file() and new_bin.is_file() and old_bin != new_bin, "Two distinct bundled engines required"
for binary in (old_bin, new_bin):
    with binary.open("rb") as stream:
        assert stream.read(2) == (b"MZ" if os.name == "nt" else b"\x7fE"), f"Unexpected host binary: {binary}"
old, new = str(old_bin), str(new_bin)
output.mkdir(exist_ok=False)
print(f"profile check host={sys.platform}; profiles={output}", flush=True)
opts = dict(app="workday", level=0, mode="sandbox", seconds=0, guidance="guided", mission="workday-report", seed=0)
report = "/home/student/project/spectrum-report.txt"
download = "/home/student/Downloads/spectrum-report.txt"
sides = dict(Ctrl="RCtrl", Alt="LAlt", Shift="LShift", Super="LSuper")
cfg = dict(keys=False, hands=True, mistakeKeys=False, fnMode="functions", keyboardProfile="matebook-d16", paired=False)
visible_cfg = dict(showKeys=False, showHands=True, showMistakeKey=False, fnMode="functions", keyboardProfile="matebook-d16", rightHandChord=False)

def open_engine(bin):
    os.environ["TOUCH_TYPING_ENGINE"] = bin
    engine = Engine(verify=True)
    engine.call("configure", platform="windows", layout="EN")
    return engine

def chord(e, keys, char=""):
    for k in keys:
        state=e.call("lab-key", key=k, pressed=True, text=char if k == keys[-1] else "")
    for k in reversed(keys):
        state=e.call("lab-key", key=k, pressed=False, text="")
    return state

def command(e, text):
    for c in text:
        chord(e, [("Space" if c==" " else c.upper())], c)
    return chord(e, ["Enter"])

def state_files(s):
    return {f["path"]: f["content"] for f in s["scene"]["terminal"]["files"]}

def assert_settings(engine):
    lib=engine.call("library")
    assert {k:lib[k] for k in visible_cfg} == visible_cfg

def prepare(e, case):
    e.call("settings", **cfg)
    assert_settings(e)
    for lang, mode in [("RU","Russian"), ("EN","English"), ("RU","Russian"), ("EN","English")]:
        state=e.call("typing-start", mode=mode, layout=lang)
        sample=state["after"][:40]
        assert len(sample)>10
        assert any(c.isalpha() and (ord(c)>127) == (lang=="RU") for c in sample)
        for ch in sample:
            e.call("typing-key", text=ch, layout=lang)
        e.call("typing-stop", layout=lang)
    e.call("lab-start", **opts)
    state=e.call("lab-resume")
    while state["scenarioStep"]<22:
        t=state["task"]
        if t["id"]=="command/head -n 2 spectrum-report.txt":
            if case=="leftover":
                assert download in state_files(command(e,"cp spectrum-report.txt /home/student/Downloads/spectrum-report.txt"))
            elif case=="altered":
                assert "EXTRA" in state_files(command(e,"echo EXTRA >> spectrum-report.txt"))[report]
        last=state["solved"]
        if t["keys"]:
            for _ in range(25):
                state=chord(e, [sides.get(k,k) for k in t["keys"].split("+")])
                if state["solved"]>last:
                    break
        else:
            state=command(e,t["text"])
        assert state["solved"] == last+1,(t,state["status"])
    assert (state["scenarioStep"],state["task"]["id"],state["solved"]) == (22,"screenshot",21)
    e.call("lab-stop")
    assert not e.storage_error
    return state

def canon(node):
    value = node.text if node.text and node.text.strip() else None
    if node.tag in {"Date", "Last"} and value:
        value = datetime.fromisoformat(value).astimezone(timezone.utc).isoformat()
    return (node.tag, value, tuple((k,v) for k,v in sorted(node.attrib.items())), tuple(map(canon,node)))

def read_profile(path):
    root=ET.parse(path)
    history=[canon(x) for x in root.findall("./ShortcutHistory/ShortcutAttempt")]
    typing=[canon(x) for x in root.findall("./Results/Result")]
    # Result item type is read via XML children to avoid a false positive on differing tags.
    typing=[canon(x) for x in root.find("./Results") if x.findtext("Activity") in {"LinuxTyping","TypingPractice"}]
    trace=json.loads(root.findtext("./LiveDrafts/LiveDraft/Trace"))
    return history,typing,trace

def assert_resumed(e, before, case, state_expected):
    state=e.call("lab-start", **opts, **{"continue":True})
    assert (state["scenarioStep"],state["task"]["id"],state["solved"],state["errors"]) == (22,"screenshot",0,0)
    assert state_files(state).get(report)==state_files(before).get(report)
    assert (download in state_files(state))==(case=="leftover")
    if state_expected is not None:
        assert state_files(state)==state_expected
    assert bool(state["reportArtifact"])==(case=="control"), state["reportArtifact"]
    return state

def kill(e):
    e.process.kill()
    code = e.process.wait(timeout=5)
    assert code != 0 if os.name == "nt" else code == -signal.SIGKILL, code
    e.close()

for case in ["control","leftover","altered"]:
    td = output / case
    td.mkdir()
    # Both tagged builds honor this absolute root only with --verify.
    os.environ["TOUCH_TYPING_QA_DATA_ROOT"] = str(td)
    path=td/"TouchTypingStudio-QA"/"progress.xml"
    side=path.with_suffix(".xml.writing")
    e=open_engine(old)
    before=prepare(e,case)
    e.close()
    data=path.read_bytes()
    (td / "v1.7.0-progress.xml.baseline").write_bytes(data)
    history, typing, trace=read_profile(path)
    assert len(history)==21 and len(typing)==4,(len(history),len(typing))
    assert not any(x["Key"]=="@strict-report" for x in trace)
    e=open_engine(new)
    assert_settings(e)
    baseline=assert_resumed(e,before,case,None)
    assert path.read_bytes()==data
    side.mkdir()
    failed=e.call("lab-stop")
    assert not failed["saved"] and failed["saveError"] and e.storage_error
    assert path.read_bytes()==data
    side.rmdir()
    side.write_text("<StudioProfile><LiveDrafts>",encoding="utf-8")
    kill(e)
    assert path.read_bytes()==data
    e=open_engine(new)
    assert_settings(e)
    assert_resumed(e,before,case,state_files(baseline))
    assert e.call("lab-stop")["saved"] and not e.storage_error
    assert not side.exists()
    first=read_profile(path)
    assert first[:2] == (history,typing)
    assert sum(x["Key"]=="@strict-report" for x in first[2])==1
    e.close()
    e=open_engine(new)
    assert_settings(e)
    assert_resumed(e,before,case,state_files(baseline))
    assert e.call("lab-stop")["saved"] and not e.storage_error
    assert read_profile(path)==first
    e.close()
    print(f"{case}: step 22, 21 credits exactly, 4 RU/EN results, 6 settings, blocked first save and torn sidecar recovered, second reopen idempotent; strict receipt {bool(baseline['reportArtifact'])}",flush=True)
