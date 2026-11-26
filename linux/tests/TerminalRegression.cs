using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace TouchTypingStudio;

internal static class TerminalRegression
{
    private static int checks;
    private static string context = "";
    private static string Preview(string value) => value.Length <= 160 ? value : value[..160] + "… (" + value.Length + " characters)";
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new InvalidOperationException(context + reason); }
    private static TerminalCommandResult Run(VirtualTerminal terminal, string command)
    {
        var result = terminal.Execute(command);
        Check(result.Success, Preview(command) + " failed: " + string.Join("\n", result.Output));
        return result;
    }
    private static string StableState(VirtualTerminal terminal)
    {
        var state = JsonSerializer.SerializeToNode(terminal.Snapshot()).AsObject();
        foreach (string key in new[] { "output", "version", "lastCommand", "lastSuccess" }) state.Remove(key);
        return state.ToJsonString();
    }
    private static void Reject(VirtualTerminal terminal, string command, bool unchanged = true)
    {
        string before = StableState(terminal);
        var result = terminal.Execute(command);
        Check(!result.Success && !terminal.LastSuccess && result.Output.Length > 0, "Rejected command must explain failure: " + Preview(command));
        if (unchanged) Check(StableState(terminal) == before, "Invalid command changed workspace: " + Preview(command));
    }
    private static void Invariants(VirtualTerminal terminal, string trace)
    {
        var state = JsonSerializer.SerializeToElement(terminal.Snapshot());
        var directories = state.GetProperty("directories").EnumerateArray().Select(value => value.GetString()).ToHashSet();
        var files = state.GetProperty("files").EnumerateArray().ToArray();
        Check(directories.Contains(terminal.Cwd), "Current directory exists after " + trace);
        Check(files.All(file => !directories.Contains(file.GetProperty("path").GetString())), "Files and directories do not overlap after " + trace);
        Check(files.All(file => directories.Contains(file.GetProperty("path").GetString()[..file.GetProperty("path").GetString().LastIndexOf('/')])), "File parent exists after " + trace);
        Check(files.All(file => file.GetProperty("content").GetString().Length <= 1048576), "Individual files stay bounded after " + trace);
        Check(files.Sum(file => (long)file.GetProperty("content").GetString().Length) <= 8388608, "Workspace stays bounded after " + trace);
        Check(state.GetProperty("packages").EnumerateArray().Any(value => value.GetString() == "bash"), "Core shell retained after " + trace);
        Check(state.GetProperty("reboots").GetInt32() >= 0 && state.GetProperty("shutdowns").GetInt32() >= 0, "Lifecycle counters valid after " + trace);
    }
    private static void CapacityQueriesAreScopedAndReadOnly()
    {
        var bash = new VirtualTerminal();
        bash.SetTrainingVolumes([new("/dev/test-root", "/", 24576, 8192),
            new("/dev/test-team", "/mnt/team", 16384, 512)], "Учебная заявка");
        bash.AddTrainingFolder("/mnt/team/Incoming reports");
        string untouched = StableState(bash);
        var linuxVolume = Run(bash, "df -h -- '/mnt/team/Incoming reports'");
        Check(linuxVolume.Output[0] == "Проверен диск /mnt/team · свободно 0,5 ГиБ" &&
            linuxVolume.Output[1].Contains(" Avail ") &&
            linuxVolume.Output[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)[3] == "512M" &&
            bash.LastCapacityQuery is { Mount: "/mnt/team", FreeMiB: 512 },
            "Bash capacity output reports the mount of its quoted path");
        Check(StableState(bash) == untouched, "Capacity query must not mutate the workspace");
        Run(bash, "df -h .");
        Check(bash.LastCapacityQuery is { Mount: "/", FreeMiB: 8192 },
            "Current directory resolves to the system mount, not the destination");
        Reject(bash, "df -h /mnt/team > result.txt");
        Check(bash.LastCapacityQuery == null, "A failed query cannot reuse a stale capacity observation");
        Check(bash.Clone().MatchesCommand("df -h /mnt/team", "df -h -- '/mnt/team/Incoming reports'"),
            "Cloned workspace retains its virtual volume layout and command equivalence");

        var ps = new VirtualTerminal();
        ps.UsePowerShell();
        ps.SetTrainingVolumes([new("C", @"C:\", 24576, 8192),
            new("E", @"E:\", 16384, 3072)], "Учебная заявка");
        untouched = StableState(ps);
        var windowsVolume = Run(ps, "get-psdrive -Name 'e'");
        Check(windowsVolume.Output[0] == @"Проверен диск E:\ · свободно 3 ГиБ" &&
            windowsVolume.Output[1].Contains("Free (GB)") &&
            windowsVolume.Output[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)[2] == "3.00" &&
            ps.LastCapacityQuery is { Name: "E", FreeMiB: 3072 },
            "PowerShell drive name and capacity parse correctly");
        Check(StableState(ps) == untouched, "PowerShell capacity query must not mutate the workspace");
        Reject(ps, "Get-PSDrive E:");
        Check(ps.LastCapacityQuery == null, "Invalid drive syntax must clear the capacity observation");
    }

    private static void Adversarial()
    {
        var shell = new VirtualTerminal();
        foreach (string command in new[] {
            "ls --unsupported", "cp --unknown project/README.md copy.md", "mv -z x y", "mkdir --root directory", "touch --bad file",
            "rm --wild project", "cat -z project/README.md", "head -n nope project/README.md", "tail -n -1 project/README.md",
            "grep -z word project/README.md", "find . -delete", "wc --bad project/README.md", "sort -n project/notes.txt", "uniq -z project/notes.txt",
            "chmod 888 project/README.md", "chmod 755 absent", "chown root project/README.md", "kill 999999999999999999999999",
            "kill -1", "kill 1", "sudo apt install unknown-package", "sudo apt remove bash", "sudo apt --random", "sudo apt upgrade",
            "shutdown tomorrow", "reboot --force", "cd '/etc'", "cat /etc/passwd", "cat /proc/self/environ", "cat ../../../../etc/passwd",
            "touch /home/host-file", "touch /real-host-file", "cd ''", "touch ''", "git init", "python3 project/app.py", "bash -c reboot",
            "echo 'unterminated", "echo trailing\\", "echo $(pwd)", "echo `pwd`", "echo \"$HOME\"", "echo hi; reboot", "pwd && reboot",
            "| pwd", "pwd |", "pwd || ls", "echo hi > > invalid", "echo hi > invalid >", "echo hi < absent", "echo hi > missing/child"
        }) Reject(shell, command);
        Check(!shell.Exists("/etc/passwd") && !shell.Exists("/home/host-file"), "Host paths never enter virtual workspace");
        Check(Run(shell, "echo '>|;&$HOME'").Output.Single() == ">|;&$HOME", "Quoted metacharacters are literal");
        Check(Run(shell, "echo one\\ two").Output.Single() == "one two", "Escaped whitespace remains one argument");
        Run(shell, "mkdir 'space dir'");
        Run(shell, "echo spaced > 'space dir/file name.txt'");
        Check(shell.ReadFile("space dir/file name.txt") == "spaced\n", "Spaces in directory and file names");
        Check(shell.Complete("space").SequenceEqual(new[] { "space dir/" }), "Completion returns real directory with spaces");
        Check(shell.Complete("space dir/file").SequenceEqual(new[] { "space dir/file name.txt" }), "Nested spaced completion");
        Check(shell.Complete("").Contains("project/") && shell.Complete("").Contains("Downloads/"), "Empty completion lists real children");
        Check(shell.Complete("/does-not-exist/").Length == 0, "Missing parent completion empty");
        Run(shell, "touch alpha.txt alphabet.txt");
        Check(shell.Complete("alpha").Length == 2, "Multiple completion options retained");
        Run(shell, "echo first > alpha.txt"); Run(shell, "echo second > alphabet.txt");
        Check(Run(shell, "cat alpha*.txt").Output.SequenceEqual(new[] { "first", "second" }), "Unquoted glob expands in path order");
        Reject(shell, "cat 'alpha*.txt'");
        Reject(shell, "echo partial > kept.txt | not-a-command", false);
        Check(shell.ReadFile("kept.txt") == "partial\n", "Runtime pipeline failure preserves completed earlier stage");
        Reject(shell, "touch made-before-failure | not-a-command", false);
        Check(shell.Exists("made-before-failure"), "Earlier pipeline side effect preserved");
        Reject(shell, "touch not-created |", true);
        Check(!shell.Exists("not-created"), "Malformed pipeline is rejected before any execution");
        string pipeline = "echo chain" + string.Concat(Enumerable.Repeat(" | cat", 31));
        Check(Run(shell, pipeline).Output.Single() == "chain", "Supported pipeline at maximum length");
        Reject(shell, pipeline + " | cat");
        Reject(shell, "echo " + new string('x', 32769));
        Reject(shell, "echo nul\0byte");
        Reject(shell, "touch " + new string('a', 256));
        Reject(shell, "touch '" + new string('é', 128) + "'");
        Check(shell.Complete(new string('a', 256) + "/").Length == 0, "Overlong completion path is controlled");
        Run(shell, "touch " + new string('a', 220));
        string wildcard = string.Concat(Enumerable.Repeat("a*", 40)) + "b";
        Check(Run(shell, "find . -name '" + wildcard + "'").Output.Length == 0, "Pathological wildcard remains bounded");
        Reject(shell, "grep '[' project/notes.txt");
        Run(shell, "echo " + new string('a', 30000) + "X > regex.txt");
        Reject(shell, "grep '(a+)+$' regex.txt");
        Run(shell, "echo protected > permissions.txt");
        Run(shell, "chmod 000 permissions.txt");
        Reject(shell, "cat permissions.txt"); Reject(shell, "echo lost > permissions.txt");
        Check(Run(shell, "sudo cat permissions.txt").Output.Single() == "protected", "Sudo can read protected virtual file");
        Run(shell, "sudo chmod 644 permissions.txt"); Run(shell, "sudo chown root:root permissions.txt");
        Reject(shell, "chmod 600 permissions.txt"); Reject(shell, "echo lost > permissions.txt");
        Reject(shell, "sudo echo lost > permissions.txt");
        Run(shell, "sudo cp project/README.md permissions.txt");
        Check(shell.ReadFile("permissions.txt").Contains("piling-main"), "Sudo command mutates protected destination");
        Run(shell, "mkdir restricted"); Run(shell, "touch restricted/note"); Run(shell, "chmod 000 restricted");
        Reject(shell, "cd restricted"); Reject(shell, "cat restricted/note"); Reject(shell, "touch restricted/new");
        Check(shell.Complete("restricted/").Length == 0, "Completion respects directory access");
        Run(shell, "sudo chmod 755 restricted");
        Run(shell, "rm permissions.txt");
        Check(!shell.Exists("permissions.txt"), "Writable parent permits deleting read-only root-owned file");
        Run(shell, "sudo apt update"); Run(shell, "sudo apt install tree git");
        Reject(shell, "sudo apt install curl absent-package");
        Check(!JsonSerializer.SerializeToElement(shell.Snapshot()).GetProperty("packages").EnumerateArray().Any(value => value.GetString() == "curl"), "Invalid package batch does not partially install");
        Run(shell, "kill -9 245"); Reject(shell, "kill 245"); Run(shell, "sudo reboot"); Run(shell, "shutdown now");
        Check(shell.Cwd == "/home/student", "Lifecycle restores prompt directory");
        var metadata = new VirtualTerminal();
        Run(metadata, "mkdir reused"); Run(metadata, "chmod 000 reused"); Run(metadata, "rmdir reused"); Run(metadata, "mkdir reused");
        Check(metadata.Complete("reused/").Length == 0, "New empty directory completion");
        Run(metadata, "touch reused/new");
        Run(metadata, "echo readonly > original"); Run(metadata, "chmod 444 original"); Run(metadata, "cp original copied");
        Reject(metadata, "echo overwritten > copied");
        Run(metadata, "sudo chown root:root original"); Run(metadata, "sudo chmod 000 original"); Run(metadata, "mv original moved");
        Check(!metadata.Exists("original") && metadata.Exists("moved"), "Move needs parent permission, not source read permission");
        Reject(metadata, "cat moved");
        Run(metadata, "sudo touch root-owned"); Reject(metadata, "echo user > root-owned");
        Run(metadata, "sudo mkdir root-dir"); Reject(metadata, "touch root-dir/user");
        GitAdversarial();
        var bounded = new VirtualTerminal(); Run(bounded, "echo x > growth.txt");
        bool refused = false;
        for (int i = 0; i < 25; i++)
        {
            string before = bounded.ReadFile("growth.txt");
            var result = bounded.Execute("cat growth.txt >> growth.txt");
            if (!result.Success) { refused = true; Check(bounded.ReadFile("growth.txt") == before, "Refused oversized append preserves file"); }
            Invariants(bounded, "append iteration " + i);
        }
        Check(refused, "Exponential append reaches bounded explicit failure");
        for (int i = 0; i < 12; i++) bounded.Execute("cp growth.txt copy" + i);
        Invariants(bounded, "workspace limit");
        var random = new Random(92841);
        var generated = new VirtualTerminal();
        var trace = new Queue<string>();
        for (int step = 0; step < 600; step++)
        {
            string[] commands = generated.SuggestedCommands(2);
            string command = commands[random.Next(commands.Length)];
            trace.Enqueue(command); if (trace.Count > 12) trace.Dequeue();
            context = "seed92841 step" + step + " trace=" + string.Join(" -> ", trace) + "\n";
            string previous = StableState(generated);
            Check(generated.MatchesCommand("  " + command + "  ", command), "Equivalent whitespace at seed92841 step" + step);
            Check(StableState(generated) == previous, "Equivalence check preserves workspace at step" + step);
            Run(generated, command);
            if (step % 7 == 0) Reject(generated, "echo malformed > > failed" + step);
            Invariants(generated, "seed92841 step" + step + " " + string.Join(" -> ", trace));
        }
        context = "";
    }
    private static void GitAdversarial()
    {
        var git = new VirtualTerminal(); Run(git, "sudo apt install git"); Run(git, "cd project");
        Reject(git, "git status"); Run(git, "git init");
        Reject(git, "git commit -m empty"); Reject(git, "git add missing.txt"); Reject(git, "git add ../outside");
        Reject(git, "git switch absent"); Reject(git, "git branch ../bad");
        Run(git, "git add ."); Run(git, "git commit -m base"); Run(git, "git switch -c topic");
        Run(git, "echo feature > feature.txt"); Run(git, "git add feature.txt"); Run(git, "git commit -m feature");
        Run(git, "git switch main");
        Check(!Run(git, "git log --oneline").Output.Any(line => line.EndsWith(" feature")), "Branch log excludes commits from other branch");
        Run(git, "echo untracked > feature.txt");
        Reject(git, "git switch topic");
        Check(git.ReadFile("feature.txt") == "untracked\n", "Branch switch protects untracked collision");
        Run(git, "rm feature.txt"); Run(git, "git switch topic");
        Check(git.ReadFile("feature.txt") == "feature\n", "Branch restores its tracked file");
        Check(Run(git, "git log --oneline").Output.Any(line => line.EndsWith(" feature")), "Topic log retains its commit");
        Run(git, "echo changed >> feature.txt"); Reject(git, "git switch main");
        string before = StableState(git);
        Check(!git.MatchesCommand("git commit -m wrong", "git commit -m correct"), "Different commit messages rejected");
        Check(!git.MatchesCommand("echo altered > README.md", "echo expected > README.md"), "Different output mutation rejected");
        Check(StableState(git) == before, "Equivalence probes preserve dirty working tree");
        Run(git, "git add feature.txt"); Run(git, "git commit -m edited"); Run(git, "git switch main");
        Check(!git.ReadFile("README.md").Contains("altered"), "Rejected equivalence mutation never leaked");
    }

    private static void Main()
    {
        var terminal = new VirtualTerminal();
        Check(terminal.Cwd == "/home/student", "Home at launch");
        Check(Run(terminal, "pwd").Output.Single() == "/home/student", "pwd result");
        Check(Run(terminal, "ls").Output.Contains("project/"), "Initial project");
        Run(terminal, "cd project");
        Check(terminal.ReadFile("app.py").StartsWith("def fft("), "Project source fixture");
        Run(terminal, "mkdir -p 'practice folder/nested'");
        Run(terminal, "echo 'one two' > 'practice folder/nested/example.txt'");
        Check(terminal.ReadFile("practice folder/nested/example.txt") == "one two\n", "Quoted path and redirect");
        Run(terminal, "echo three >> 'practice folder/nested/example.txt'");
        Check(terminal.ReadFile("practice folder/nested/example.txt") == "one two\nthree\n", "Append redirect");
        Check(Run(terminal, "cat < 'practice folder/nested/example.txt' | wc -l").Output.Single() == "2", "Input redirect and pipe");
        Check(Run(terminal, "cat notes.txt | sort | uniq -c").Output.SequenceEqual(new[] { "2 signal", "1 spectrum", "1 vector" }), "Sort and uniq pipeline");
        Check(Run(terminal, "grep -n ERROR logs/app.log").Output.Single() == "2:ERROR missing sample", "Grep line numbering");
        Check(Run(terminal, "find . -name '*.txt' -type f").Output.Length == 2, "Quoted find expression");
        Check(Run(terminal, "head -n 1 notes.txt").Output.Single() == "spectrum", "head count");
        Check(Run(terminal, "tail -n 1 notes.txt").Output.Single() == "signal", "tail count");
        Run(terminal, "cp notes.txt backup.txt");
        Check(terminal.ReadFile("backup.txt") == terminal.ReadFile("notes.txt"), "cp preserves content");
        Run(terminal, "mv backup.txt moved.txt");
        Check(terminal.Exists("moved.txt") && !terminal.Exists("backup.txt"), "mv changes source");
        Run(terminal, "mkdir copies");
        Run(terminal, "cp -r logs copies");
        Check(terminal.ReadFile("copies/logs/app.log") == terminal.ReadFile("logs/app.log"), "Recursive cp");
        Run(terminal, "rm -r copies");
        Check(!terminal.Exists("copies/logs/app.log"), "Recursive rm");
        Run(terminal, "rm moved.txt");
        Check(!terminal.Exists("moved.txt"), "File rm");
        Check(!terminal.Execute("rm -rf /home/student/project").Success, "Current directory protection");
        Check(!terminal.Execute("echo x > missing/target.txt").Success, "Missing parent is error");
        Check(!terminal.Execute("echo 'unterminated").Success, "Unterminated quote is error");
        Check(Run(terminal, "echo \"\\n\"").Output.Single() == "\\n", "Double quoted non-special backslash retained");
        Check(!terminal.Execute("echo \"$HOME\"").Success, "Unsupported expansion is explained");
        Check(Run(terminal, "echo '$HOME'").Output.Single() == "$HOME", "Single quoted literal dollar retained");
        Check(!terminal.Execute("echo x > > broken.txt").Success && !terminal.Exists("broken.txt"), "Separated redirects are syntax errors");
        Check(!terminal.Execute("cat notes.txt || ls").Success, "Unsupported boolean pipeline is error");
        Check(!terminal.Execute("python3 app.py").Success, "Unsupported executable never launched");
        Run(terminal, "chmod u+x app.py");
        Check(JsonSerializer.Serialize(terminal.Snapshot()).Contains("744"), "chmod alters mode");
        Check(!terminal.Execute("chown root app.py").Success, "chown requires sudo");
        Run(terminal, "sudo chown root app.py");
        Check(JsonSerializer.Serialize(terminal.Snapshot()).Contains("root"), "chown alters owner");
        Check(Run(terminal, "ps aux").Output.Any(line => line.Contains("245")), "Virtual process visible");
        Run(terminal, "kill 245");
        Check(!Run(terminal, "ps").Output.Any(line => line.Contains("245")), "Virtual process killed");
        Check(!terminal.Execute("kill 1").Success, "Infrastructure process protected");
        Check(!terminal.Execute("apt install git").Success, "apt mutation requires sudo");
        Run(terminal, "sudo apt update");
        Run(terminal, "sudo apt install git");
        Check(Run(terminal, "apt list --installed").Output.Any(line => line.StartsWith("git ")), "Virtual package installed");
        Run(terminal, "sudo apt remove git");
        Check(!Run(terminal, "apt list --installed").Output.Any(line => line.StartsWith("git ")), "Virtual package removed");
        Check(!terminal.Execute("git init").Success, "Git executable requires its virtual package");
        Run(terminal, "sudo apt install git");
        Run(terminal, "git init");
        Run(terminal, "git add README.md");
        Run(terminal, "git commit -m 'Document the project'");
        Check(Run(terminal, "git log --oneline").Output.Single().Contains("Document the project"), "Commit retained");
        Check(Run(terminal, "git diff").Output.Length == 0, "Untracked files excluded from git diff");
        Run(terminal, "git switch -c lesson");
        Check(terminal.Exists("notes.txt"), "Switch preserves untracked files");
        Run(terminal, "echo next >> README.md");
        Check(Run(terminal, "git diff").Output.Any(line => line == "+next"), "Working tree diff");
        Check(!terminal.Execute("git switch main").Success, "Unsaved tracked edits block branch switch");
        Run(terminal, "git add README.md");
        Run(terminal, "git commit -m 'Extend documentation'");
        Run(terminal, "git switch main");
        Check(!terminal.ReadFile("README.md").Contains("next"), "Branch restores committed files");
        Check(terminal.Exists("notes.txt"), "Untracked files survive return");
        Run(terminal, "sudo reboot");
        Check(terminal.Cwd == "/home/student", "Virtual reboot returns home");
        Run(terminal, "shutdown -h now");
        Check(JsonSerializer.Serialize(terminal.Snapshot()).Contains("shutdowns\":1"), "Shutdown only changes virtual state");
        var completion = new VirtualTerminal();
        Check(completion.Complete("p").SequenceEqual(new[] { "project/" }), "Completion uses actual home files");
        Check(completion.Complete("~/pro").SequenceEqual(new[] { "~/project/" }), "Completion preserves home spelling");
        Check(completion.Complete("/home/stu").SequenceEqual(new[] { "/home/student/" }), "Completion preserves absolute spelling");
        Run(completion, "cd project");
        Check(completion.Complete("p").Length == 0, "Absent child project not invented");
        Check(completion.Complete("logs/").SequenceEqual(new[] { "logs/app.log" }), "Nested completion uses directory files");
        Check(completion.Complete("../pro").SequenceEqual(new[] { "../project/" }), "Completion resolves parent path");
        Run(completion, "touch created.txt");
        Check(completion.Complete("cre").SequenceEqual(new[] { "created.txt" }), "Created file immediately available");
        Run(completion, "rm created.txt");
        Check(completion.Complete("cre").Length == 0, "Removed file disappears from completion");
        var equivalent = new VirtualTerminal();
        Check(equivalent.MatchesCommand("cd /home/student/project", "cd ~/project"), "Absolute and home-relative cd equivalent");
        Check(equivalent.MatchesCommand("ls -al", "ls -la"), "Flag order equivalent");
        Check(equivalent.MatchesCommand("sudo ls .", "ls"), "Harmless sudo and current path equivalent");
        Check(equivalent.MatchesCommand("  echo   'training'  ", "echo training"), "Whitespace and quoting equivalent");
        Check(equivalent.MatchesCommand("cat /home/student/project/README.md", "cat ~/project/README.md"), "Absolute file spelling equivalent");
        Check(equivalent.Cwd == "/home/student" && equivalent.Version == 0 && equivalent.History.Length == 1, "Equivalence check does not mutate workspace/history");
        equivalent.Execute("pwd");
        equivalent.Execute("clear");
        Check(equivalent.History.Length == 0, "clear removes scrollback only");
        var restoredHistory = equivalent.Execute("history");
        Check(restoredHistory.Success && restoredHistory.Output.Any(line => line.EndsWith("  pwd")) &&
            restoredHistory.Output.Any(line => line.EndsWith("  clear")), "history survives screen clearing");
        Check(!equivalent.MatchesCommand("cd /tmp", "cd ~/project"), "Different destination rejected");
        Check(!equivalent.MatchesCommand("echo /home/student", "pwd"), "Matching output does not replace learned command family");
        Check(!equivalent.MatchesCommand("touch first", "touch second"), "Different file effect rejected");
        Check(!equivalent.MatchesCommand("echo changed", "echo training"), "Different output rejected");
        Check(!equivalent.MatchesCommand("echo broken > > file", "echo training"), "Malformed alternative rejected");
        Check(!equivalent.MatchesCommand("echo broken > > file", "echo broken > > file"), "Identical malformed commands cannot match");
        Check(!equivalent.MatchesCommand("not-a-command", "not-a-command"), "Identical unsupported commands cannot match");
        for (int level = 0; level <= 2; level++)
        {
            var initial = new VirtualTerminal();
            foreach (string command in initial.SuggestedCommands(level))
                Run(new VirtualTerminal(), command);
        }
        var random = new Random(17);
        var linked = new VirtualTerminal();
        for (int step = 0; step < 250; step++)
        {
            string[] candidates = linked.SuggestedCommands(2);
            Run(linked, candidates[random.Next(candidates.Length)]);
        }
        CapacityQueriesAreScopedAndReadOnly();
        Adversarial();
        Console.WriteLine("PASS: " + checks + " isolated terminal checks including seeded adversarial sessions");
    }
}
