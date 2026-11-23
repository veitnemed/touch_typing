# Hosted Windows normal launch / save / reopen
This job verifies the same extracted, hash-checked Windows x64 ZIP as other
candidate jobs, but on a newly provisioned GitHub-hosted VM separate from WPF
and upgrade QA. It refuses to run outside the dedicated job or with any existing
TouchTyping profile in the runner's actual LocalApplicationData. It never
creates accounts, changes desktop ACLs/settings, or accesses a real profile.

The test runs the packaged Start-Live.cmd unchanged with no QA flags, identifies
the extracted WPF process by full path and PID, opens a terminal lesson, types
a draft, saves and checks the normal Windows profile path. The actual launcher
must open a second process that resumes the same terminal text and caret,
accepts Delete, and saves one draft without a leftover sidecar. It retains
only a log, never the profile; the VM and test profile are discarded by GitHub.

This does not prove clean Windows 10/11 or non-admin operation. Standard
GitHub-hosted Windows VMs run as administrators with UAC disabled; the test
logs the actual Windows version and Windows token information. A disposable
standard-user Windows 10/11 check of the normal launcher is still needed.
[GitHub-hosted runner behavior](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
