"""Actual local shell tools on fully disposable files (not simulated lesson evidence)."""
import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def run(args, cwd, allowed=(0,)):
    p = subprocess.run(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45)
    assert p.returncode in allowed, (args, p.returncode, p.stdout[-3000:], p.stderr[-3000:])
    return p


def verify(require):
    win = os.name == "nt"
    copier = shutil.which("robocopy" if win else "rsync")
    powershell = shutil.which("powershell") or shutil.which("pwsh")
    if require and not copier:
        raise AssertionError("Missing native copy tool")
    with tempfile.TemporaryDirectory(prefix="tt-native-delivery-") as directory:
        root = Path(directory)
        files = {
            "Work/notes/weekly.txt": (b"east: ready Fri\n",2),
            "Backup/notes/weekly.txt": (b"east: ready Tue\n",1),
            "Work/notes/contacts.txt": (b"desk: 14\n",1), "Backup/notes/contacts.txt": (b"desk: 14\n",1),
            "Work/summary.csv": (b"week,total\n4,16\n",2),
            "Backup/archive.txt": (b"Keep signed record\n",1),
        }
        for name,(body,stamp) in files.items():
            f=root/name
            f.parent.mkdir(parents=True, exist_ok=True)
            f.write_bytes(body); os.utime(f, (1700000000+stamp*10,)*2)
        for d in (root/"Work/notes", root/"Backup/notes", root/"Work", root/"Backup"):
            os.utime(d, (1700000000,)*2)
        def tree():
            return {f.relative_to(root/"Backup").as_posix(): (f.read_bytes(),f.stat().st_mtime_ns)
                    for f in (root/"Backup").rglob("*") if f.is_file()}
        before=tree()
        if copier:
            def change(dry, expected):
                args=([copier,"Work","Backup","/E","/R:1","/W:1"]+(["/L"] if dry else [])) if win else (
                    [copier,"-rtni" if dry else "-rti","--","Work/","Backup/"])
                p=run(args,root,range(8) if win else (0,))
                report=p.stdout.decode(errors="replace")
                # Both commands display regular files to copy, not unchanged files.
                actual={name for name in ("weekly.txt","summary.csv","contacts.txt") if name in report}
                assert actual == expected, (args,report)
                if win and not dry:
                    # Robocopy reports destination extras, which we deliberately preserve.
                    assert p.returncode & 2 and bool(p.returncode & 1)==bool(expected), (p.returncode,report)
                return p.returncode
            expected={"weekly.txt","summary.csv"}
            preview_code=change(True,expected)
            assert tree()==before
            applied_code=change(False,expected); updated=tree()
            assert updated["archive.txt"]==before["archive.txt"] and updated["notes/contacts.txt"]==before["notes/contacts.txt"]
            assert updated["notes/weekly.txt"][0]==files["Work/notes/weekly.txt"][0]
            assert updated["summary.csv"][0]==files["Work/summary.csv"][0]
            repeat_code=change(True,set())
            no_op=change(False,set()); assert tree()==updated
            print(f"{Path(copier).name}: 2 planned (0 writes), 2 copied, 0 on repeat; unchanged and destination-only preserved; exits {preview_code}/{applied_code}/{repeat_code}/{no_op}")
        else:
            print("SKIP real copy tool (not installed)")
        body=b"invoice,amount\n312,1140\n"
        for name,data in (("east.csv",body),("west.csv",body.replace(b"invoice",b"invo1ce"))):
            (root/name).write_bytes(data)
            if win:
                assert powershell, "requires stock PowerShell"
                args=[powershell,"-NoProfile","-NonInteractive","-Command",f"(Get-FileHash -LiteralPath {name}).Hash"]
            else:
                args=["sha256sum",name]
            digest=run(args,root).stdout.decode().split()[0]
            assert digest.lower()==hashlib.sha256(data).hexdigest()
        print("Actual SHA-256 command identifies two different equal-size downloads.")
        (root/"test_checkout.py").write_text("import unittest\nclass T(unittest.TestCase):\n def test_a(self): self.assertEqual(1,1)\n def test_b(self): self.assertEqual(2,2)\n",encoding="utf-8")
        p=run([sys.executable,"-m","unittest","test_checkout"],root)
        assert p.stdout==b"" and b"Ran 2 tests" in p.stderr and b"OK" in p.stderr
        if not win:
            for redirection,contains in (("> result.txt",False),("> result.txt 2>&1",True),("2>&1 > result.txt",False)):
                p=run(["bash","-c",'"$1" -m unittest test_checkout '+redirection,"test",sys.executable],root)
                assert (b"Ran 2 tests" in (root/"result.txt").read_bytes())==contains
            (root/"test_broken.py").write_text("import unittest\nclass T(unittest.TestCase):\n def test_failure(self): self.fail('wrong total')\n")
            (root/"result.txt").write_text("Stale OK",encoding="utf-8")
            bad=run(["bash","-c",'"$1" -m unittest test_broken > result.txt 2>&1',"test",sys.executable],root,allowed=(1,))
            assert not bad.stdout and not bad.stderr
            assert b"FAILED (failures=1)" in (root/"result.txt").read_bytes() and b"Stale OK" not in (root/"result.txt").read_bytes()
        print("CPython unittest writes its 2-test OK report to stderr.")


if __name__=="__main__":
    ap=argparse.ArgumentParser(); ap.add_argument("--require-native",action="store_true")
    verify(ap.parse_args().require_native)
