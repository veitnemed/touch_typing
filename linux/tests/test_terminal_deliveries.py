"""Prove selection and real file effects from the ordinary app routes in both profiles."""
import hashlib
import unittest

import test_terminal_practical as harness
from test_terminal_practical import options, submit, terminal


def selected_files(s):
    root="/home/student/project/"
    return {f["path"][len(root):]: f["content"] for f in terminal(s)["files"] if f["path"].startswith(root)}


class DeliveryTests(unittest.TestCase):
    setUp=harness.TerminalPracticalTests.setUp
    open=harness.TerminalPracticalTests.open

    def test_primary_sync_pause_replay_hash_and_read_actual_candidate(self):
        for desktop in ("ubuntu","windows"):
            ps=desktop=="windows"; e=self.open("windows" if ps else "linux")
            opts=options(desktop,seed=2)
            s=e.call("lab-start",**opts); s=e.call("lab-resume")
            self.assertEqual((s["mission"], s["actionLimit"], s["timeLimit"]),
                             ("terminal-delivery-sync-"+("ps" if ps else "bash"),40,0))
            initial=selected_files(s)
            self.assertNotEqual(initial["Work/notes/weekly.txt"],initial["Backup/notes/weekly.txt"])
            cant_copy="robocopy Work Backup /E" if ps else "rsync -rti Work/ Backup/"
            s=submit(e,cant_copy)
            self.assertEqual((s["solved"],selected_files(s)),(0,initial))
            for wrong in (["robocopy Work Backup /MIR /L",r"Get-Content ..\secret"] if ps else
                          ["rsync -rti --delete Work/ Backup/","cat /etc/passwd"]):
                s=submit(e,wrong)
                self.assertEqual((s["solved"],selected_files(s)),(0,initial))
            s=submit(e,s["task"]["text"])
            self.assertEqual((s["solved"],selected_files(s)),(1,initial))
            plan="\n".join(terminal(s)["output"])
            self.assertIn("weekly.txt",plan); self.assertIn("summary.csv",plan); self.assertNotIn("contacts.txt",plan)
            s=submit(e,s["task"]["text"])
            new=selected_files(s)
            self.assertEqual((s["solved"],new["Backup/notes/weekly.txt"],new["Backup/summary.csv"]),
                           (2,initial["Work/notes/weekly.txt"],initial["Work/summary.csv"]))
            for name in ("archive.txt","notes/contacts.txt"): self.assertEqual(new["Backup/"+name],initial["Backup/"+name])
            # Rechecking or repeating a completed copy is harmless. A valid
            # out-of-order command must not permanently poison the next goal.
            preview = (cant_copy + " /L") if ps else "rsync -rtni -- Work/ Backup/"
            for cmd in (preview, cant_copy):
                s=submit(e,cmd)
                self.assertEqual((s["solved"],selected_files(s),terminal(s)["lastSuccess"]),(2,new,True))
            s=submit(e,(r"Get-Content Work\notes\weekly.txt" if ps else "cat Work/notes/weekly.txt"))
            self.assertEqual(s["solved"],2,"Opening identical bytes from the wrong tree must not count")
            e.call("lab-stop"); e=self.open("windows" if ps else "linux")
            s=e.call("lab-start",**(opts|{"continue":True})); s=e.call("lab-resume")
            self.assertEqual((s["solved"],selected_files(s)),(2,new))
            s=submit(e,s["task"]["text"])
            self.assertIn(new["Backup/notes/weekly.txt"].strip(),terminal(s)["output"])
            s=submit(e,s["task"]["text"])
            self.assertEqual((s["solved"],s["mission"],s["finished"]),
                             (4,"terminal-delivery-hash-"+("ps" if ps else "bash"),False))
            material=selected_files(s)
            trusted=material["receipt.txt"].strip().lower()
            names=["east.csv","west.csv"]
            self.assertEqual(len(material[names[0]].encode()),len(material[names[1]].encode()))
            answer,bad=sorted(names,key=lambda name: hashlib.sha256(material[name].encode()).hexdigest()!=trusted)
            self.assertTrue(s["commandTheory"]["why"])
            for filename in ["receipt.txt"]+names:
                s=submit(e,s["task"]["text"])
                actual="\n".join(terminal(s)["output"]).lower()
                self.assertIn(trusted if filename=="receipt.txt" else hashlib.sha256(material[filename].encode()).hexdigest(),actual)
            prefix="Get-Content " if ps else "cat "
            s=submit(e,prefix+bad);self.assertEqual(s["solved"],7)
            s=submit(e,prefix+answer);self.assertEqual(s["solved"],8)
            self.assertTrue(s["mission"].startswith("terminal-disk-space-"))
            e.call("lab-stop")

    def test_backups_rechecked_when_nothing_needs_copying(self):
        for ps in (False, True):
            desktop="windows" if ps else "ubuntu"
            e=self.open("windows" if ps else "linux")
            s=e.call("lab-start",**options(desktop,guidance="independent",mission="terminal-delivery-sync-"+("ps" if ps else "bash"),routeKind="mission"))
            s=e.call("lab-resume")
            self.assertFalse(s["showGuide"])
            for i in range(4):
                if i==3:
                    s=submit(e,"robocopy Work Backup /E" if ps else "rsync -rti -- Work/ Backup/")
                    self.assertEqual(s["solved"],3,"An actual write request cannot count as inspection")
                s=submit(e,s["task"]["text"])
            self.assertTrue(s["finished"]);self.assertEqual(s["solved"],4)
            last=terminal(s)["output"][-1]
            self.assertTrue(last=="Files: copied 0; extras 1" if ps else last.endswith("Work/ Backup/"),last)


if __name__=="__main__": unittest.main()
