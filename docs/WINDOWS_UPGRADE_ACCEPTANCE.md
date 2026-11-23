# Windows upgrade probe (v1.7.0 → preview)

**Hosted Windows / disposable QA acceptance.** Extract the verified old and new
Windows x64 archives to separate folders. Old v1.7.0 Windows ZIP checksum:
`0f781e9bae0b6f8fae67945e206a9384b0fffa470de66a033190cf411ea3709e`.
Give the candidate's exact hash and commit in the run record. In an unlocked,
isolated x64 Windows desktop, with Python 3.11+ already available, run:

```powershell
.\Test-WindowsUpgrade.ps1 -OldApplication 'C:\old\TouchTyping.exe' `
    -NewApplication 'C:\candidate\TouchTyping.exe' -SourceRoot 'C:\exact-source' `
    -OutputDirectory 'C:\qa\new-upgrade-run'
```

Uses only owned processes and absolute new QA roots: old and current native WPF
launches use `--qa --live`, and the two bundled native engine processes use
`--verify`. The normal launcher is deliberately not called by the automated test.
Checks visible old chooser, four RU/EN typing records, six settings, 21 intact
old Workday credits, step 22, original XML bytes after failure, recovery after
an obstructed save and an interrupted staging file, idempotent restarts,
and visible candidate Workday resume and pause with no duplicate credit. The
harness simulates torn staging, not forced OS sleep, power loss or lock.

**Manual Windows 10/11 (still required for client).** Use a fresh offline
standard account in an isolated VM or test PC, preferably with a Cyrillic path.
Do not reuse a real person's profile. Record winver and exact archive SHA256s.
Run the old normal `Start-Live.cmd`, complete RU and EN typing, toggle keyboard
and hands settings, then practice the Workday lost material mission and save
mid-task. Record current goal, typed text, focus, and credit count; back up
`%LOCALAPPDATA%\TouchTypingStudio\progress.xml` after exiting. Open the new normal
launcher in the same account and check records, settings, goal, text and count.
Complete another step, close and reopen; verify it credited only once. For
retry, work on a copy of the disposable profile: close the app, create the
empty directory `progress.xml.writing`, resume and attempt to save, require
the original checksum unchanged and visible retry UI. Remove the directory,
retry, close and reopen, and record persistence. The old app and its backup
remain available. Installation via Start menu, physical key behavior and
client lock/sleep are separate checks.
