using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal sealed record TerminalCommandResult(bool Success, string[] Output);

/// <summary>An entirely in-memory Linux workspace used by terminal lessons.</summary>
internal sealed class VirtualTerminal
{
    private const string Home = "/home/student";
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly HashSet<string> directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> modes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> owners = new(StringComparer.Ordinal);
    private readonly List<string> history = [];
    private readonly Dictionary<string, Func<VirtualTerminal, string>> lessonUnitTests = new(StringComparer.Ordinal);
    private readonly HashSet<string> packages = new(StringComparer.Ordinal) { "bash", "coreutils" };
    private readonly Dictionary<int, string> processes = new() { [1] = "systemd", [120] = "bash", [245] = "python3 worker.py" };
    private static readonly Dictionary<string, string> AvailablePackages = new(StringComparer.Ordinal)
    { ["git"] = "2.43", ["python3"] = "3.12", ["curl"] = "8.5", ["tree"] = "2.1", ["vim"] = "9.1", ["htop"] = "3.3" };
    private readonly Dictionary<string, string> committed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> index = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> branches = new(StringComparer.Ordinal);
    private readonly List<string> commits = [];
    private string repository;
    private string branch = "main";
    private bool packagesUpdated;
    private int reboots, shutdowns;
    private bool elevated;
    private readonly Dictionary<string, List<int>> branchLogs = new(StringComparer.Ordinal);

    internal string Cwd { get; private set; } = Home;
    internal string[] History => history.TakeLast(160).ToArray();
    internal string LastCommand { get; private set; } = "";
    internal string[] LastOutput { get; private set; } = [];
    internal bool LastSuccess { get; private set; }
    internal int Version { get; private set; }

    internal VirtualTerminal()
    {
        foreach (string directory in new[] { "/", "/home", Home, Home + "/project", Home + "/project/logs", Home + "/Downloads", "/tmp" })
            directories.Add(directory);
        files[Home + "/project/README.md"] = "# piling-main\nSignal analysis project\nRun the tests before committing.\n";
        files[Home + "/project/notes.txt"] = "spectrum\nsignal\nvector\nsignal\n";
        files[Home + "/project/logs/app.log"] = "INFO started\nERROR missing sample\nINFO finished\n";
        files[Home + "/project/app.py"] = "def fft(signal: np.ndarray):\n\n    fft_values = np.fft.fft(signal)\n    fft_centred = np.fft.fftshift(fft_values)\n    return fft_centred\n";
        history.Add("Учебный Ubuntu Terminal. Проект находится в ~/project.");
    }

    internal string[] Entries(string path) => Children(Resolve(path)).OrderByDescending(directories.Contains).ThenBy(Name, StringComparer.OrdinalIgnoreCase).ToArray();
    internal void StoreFile(string path, string body) => Write(Resolve(path), body);
    internal void Transfer(string source, string destination, bool move) => CopyMove(move ? [source, destination] : ["-r", source, destination], move);
    internal void CreateFolder(string path) => MakeDirectories(["-p", path]);
    internal string ReadFile(string path) => files.GetValueOrDefault(Resolve(path));
    internal bool Exists(string path) => files.ContainsKey(Resolve(path)) || directories.Contains(Resolve(path));
    // A closed, in-memory exercise fixture. No learner-supplied source or
    // shell command is executed by the host Python or operating system.
    internal void InstallUnitTest(string selector, Func<VirtualTerminal, string> run) => lessonUnitTests.Add(selector, run);
    internal bool HasUnitTest(string selector) => lessonUnitTests.ContainsKey(selector);
    internal bool DirectoryExists(string path) => directories.Contains(Resolve(path));
    internal object Snapshot() => new
    {
        cwd = Cwd, output = History,
        files = files.OrderBy(pair => pair.Key).Select(pair => new { path = pair.Key, content = pair.Value,
            mode = modes.GetValueOrDefault(pair.Key, "644"), owner = owners.GetValueOrDefault(pair.Key, "student") }).ToArray(),
        directories = directories.Order().ToArray(), packages = packages.Order().ToArray(),
        processes = processes.Select(pair => new { pid = pair.Key, command = pair.Value }).ToArray(),
        reboots, shutdowns, branch, version = Version, lastCommand = LastCommand, lastSuccess = LastSuccess
    };

    internal string[] Complete(string prefix)
    {
        if (prefix == "~") return ["~/"];
        int slash = prefix.LastIndexOf('/');
        string directoryText = slash < 0 ? "" : prefix[..(slash + 1)];
        string directory;
        try { directory = slash < 0 ? Cwd : Resolve(directoryText); }
        catch (ArgumentException) { return []; }
        string name = prefix[(slash + 1)..];
        if (!directories.Contains(directory)) return [];
        try { Access(directory, 5); }
        catch (ArgumentException) { return []; }
        return Children(directory).Where(path => Name(path).StartsWith(name, StringComparison.Ordinal))
            .Where(path => name.StartsWith('.') || !Name(path).StartsWith('.')).Order(StringComparer.Ordinal)
            .Select(path => directoryText + Name(path) + (directories.Contains(path) ? "/" : "")).ToArray();
    }

    internal string[] SuggestedCommands(int level)
    {
        var commands = new List<string> { "pwd", "ls", "ls -la", "whoami", "echo training" };
        if (Cwd != Home + "/project" && directories.Contains(Home + "/project")) commands.Add("cd ~/project");
        if (Cwd != Home) commands.Add("cd ~");
        if (Cwd != "/") commands.Add("cd ..");
        commands.Add("mkdir -p practice");
        if (DirectoryExists("practice"))
        {
            commands.Add("touch practice/note.txt");
            commands.Add("echo ready > practice/note.txt");
            if (Exists("practice/note.txt"))
            {
                commands.Add("cat practice/note.txt");
                commands.Add("cp practice/note.txt practice/copy.txt");
            }
            if (Exists("practice/copy.txt")) commands.Add("mv practice/copy.txt practice/draft.txt");
            if (Exists("practice/draft.txt")) commands.Add("rm practice/draft.txt");
        }
        string notes = Home + "/project/notes.txt";
        string readme = Home + "/project/README.md";
        string log = Home + "/project/logs/app.log";
        if (files.ContainsKey(readme)) commands.Add("cat ~/project/README.md");
        if (level >= 1)
        {
            commands.AddRange(["id", "ps", "find . -name '*.py'", "sudo apt update", "apt search tree", "sudo reboot"]);
            if (files.ContainsKey(notes)) commands.AddRange(["head -n 2 ~/project/notes.txt", "tail -n 2 ~/project/notes.txt", "cat ~/project/notes.txt | sort | uniq", "wc -l ~/project/notes.txt"]);
            if (files.ContainsKey(log) && files[log].Contains("ERROR")) commands.Add("grep -n ERROR ~/project/logs/app.log");
            if (packages.Contains("tree")) commands.Add("tree"); else commands.Add("sudo apt install tree");
        }
        if (level >= 2)
        {
            commands.AddRange(["uname -a", "date", "ps aux", "sudo shutdown -r now", "apt show git"]);
            if (Exists("practice/note.txt")) commands.Add("chmod 600 practice/note.txt");
            if (processes.ContainsKey(245)) commands.Add("kill 245");
            if (!packages.Contains("git")) commands.Add("sudo apt install git");
            else if (repository == null && Cwd == Home + "/project") commands.Add("git init");
            else if (repository != null && Within(Cwd, repository))
            {
                commands.AddRange(["git status", "git diff", "git log --oneline", "git branch"]);
                var current = RepositoryFiles();
                if (!Same(current, index)) commands.Add("git add .");
                if (!Same(index, committed)) commands.Add("git commit -m 'Save lesson'");
                if (Same(current.Where(pair => committed.ContainsKey(pair.Key) || index.ContainsKey(pair.Key)).ToDictionary(), committed) && Same(index, committed))
                {
                    if (!branches.ContainsKey("lesson") && branch != "lesson") commands.Add("git switch -c lesson");
                    if (branch != "main" && branches.ContainsKey("main")) commands.Add("git switch main");
                    if (branch != "lesson" && branches.ContainsKey("lesson")) commands.Add("git switch lesson");
                }
            }
            if (packagesUpdated) commands.Add("sudo apt upgrade");
            if (packages.Contains("tree")) commands.Add("sudo apt remove tree");
        }
        return commands.Distinct().Where(command => CopyWorkspace().Execute(command).Success).ToArray();
    }

    internal bool MatchesCommand(string submitted, string expected)
    {
        try
        {
            var actualTokens = Tokenize(submitted);
            var targetTokens = Tokenize(expected);
            if (actualTokens.Select(TokenMeaning).SequenceEqual(targetTokens.Select(TokenMeaning))) return CopyWorkspace().Execute(expected).Success;
            var actualStages = SplitPipeline(actualTokens);
            var targetStages = SplitPipeline(targetTokens);
            if (actualStages.Count != targetStages.Count ||
                !actualStages.Select(CommandFamily).SequenceEqual(targetStages.Select(CommandFamily))) return false;
            var actual = CopyWorkspace();
            var target = CopyWorkspace();
            var actualResult = actual.Execute(submitted);
            var targetResult = target.Execute(expected);
            return actualResult.Success && targetResult.Success && actualResult.Output.SequenceEqual(targetResult.Output) &&
                actual.SameWorkspace(target);
        }
        catch (ArgumentException) { return false; }
    }

    private static (string Value, bool Operator, bool Glob) TokenMeaning(Token token) =>
        (token.Value, token.Operator, token.Expand && (token.Value.Contains('*') || token.Value.Contains('?')));

    private static string CommandFamily(List<Token> stage)
    {
        int position = 0;
        while (position < stage.Count && !stage[position].Operator && stage[position].Value == "sudo") position++;
        if (position >= stage.Count || stage[position].Operator) return "";
        string command = stage[position].Value;
        if (command == "reboot" || command == "shutdown" && stage.Any(token => token.Value == "-r")) return "restart";
        return command;
    }

    internal VirtualTerminal Clone() => CopyWorkspace();

    private VirtualTerminal CopyWorkspace()
    {
        var copy = new VirtualTerminal
        {
            Cwd = Cwd, repository = repository, branch = branch, packagesUpdated = packagesUpdated,
            reboots = reboots, shutdowns = shutdowns, Version = Version, LastSuccess = LastSuccess, LastCommand = LastCommand, LastOutput = LastOutput
        };
        Replace(copy.files, files); Replace(copy.modes, modes); Replace(copy.owners, owners);
        copy.directories.Clear(); copy.directories.UnionWith(directories);
        copy.packages.Clear(); copy.packages.UnionWith(packages);
        copy.processes.Clear(); foreach (var pair in processes) copy.processes[pair.Key] = pair.Value;
        Replace(copy.committed, committed); Replace(copy.index, index);
        copy.commits.Clear(); copy.commits.AddRange(commits);
        copy.branches.Clear(); foreach (var pair in branches) copy.branches[pair.Key] = new(pair.Value);
        foreach (var pair in branchLogs) copy.branchLogs[pair.Key] = new(pair.Value);
        foreach (var pair in lessonUnitTests) copy.lessonUnitTests[pair.Key] = pair.Value;
        copy.history.Clear(); copy.history.AddRange(history);
        return copy;
    }

    private bool SameWorkspace(VirtualTerminal other) =>
        Cwd == other.Cwd && repository == other.repository && branch == other.branch && packagesUpdated == other.packagesUpdated &&
        reboots == other.reboots && shutdowns == other.shutdowns && directories.SetEquals(other.directories) && packages.SetEquals(other.packages) &&
        Same(files, other.files) && Same(modes, other.modes) && Same(owners, other.owners) && Same(committed, other.committed) && Same(index, other.index) &&
        processes.Count == other.processes.Count && processes.All(pair => other.processes.GetValueOrDefault(pair.Key) == pair.Value) &&
        commits.SequenceEqual(other.commits) && branches.Count == other.branches.Count && branches.All(pair => other.branches.TryGetValue(pair.Key, out var value) && Same(pair.Value, value)) &&
        branchLogs.Count == other.branchLogs.Count && branchLogs.All(pair => other.branchLogs.TryGetValue(pair.Key, out var value) && pair.Value.SequenceEqual(value));

    internal TerminalCommandResult Execute(string command)
    {
        LastCommand = command;
        LastOutput = [];
        history.Add($"student@ubuntu:{Abbreviate(Cwd)}$ {command}");
        if (string.IsNullOrWhiteSpace(command))
        {
            LastSuccess = true;
            return new(true, []);
        }
        try
        {
            if (command.Length > 32768 || command.Contains('\0')) throw Error("input", "слишком длинная строка или недопустимый нулевой символ");
            var stages = SplitPipeline(Tokenize(command));
            if (stages.Count > 32) throw Error("input", "учебный конвейер ограничен 32 командами");
            string input = "";
            foreach (var stage in stages)
                input = ExecuteStage(stage, input);
            var output = Lines(input);
            LastSuccess = true;
            history.AddRange(output); LastOutput = output;
            return new(true, output);
        }
        catch (ArgumentException error)
        {
            LastSuccess = false;
            history.Add(error.Message);
            return new(false, [error.Message]);
        }
    }

    private string ExecuteStage(List<Token> tokens, string input)
    {
        if (tokens.Count == 0) throw Error("syntax", "ожидалась команда");
        string redirect = null, operation = null;
        var arguments = new List<string>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Operator)
            {
                if (i + 1 >= tokens.Count || tokens[i + 1].Operator)
                    throw Error("syntax", "после перенаправления нужно имя файла");
                if (token.Value == "<") input = File(Resolve(tokens[++i].Value));
                else
                {
                    if (redirect != null) throw Error("syntax", "допускается одно выходное перенаправление");
                    operation = token.Value;
                    redirect = Resolve(tokens[++i].Value);
                    EnsureParent(redirect);
                }
                continue;
            }
            arguments.AddRange(token.Expand ? Expand(token.Value) : [token.Value]);
        }
        string output = Run(arguments.ToArray(), input);
        if (redirect == null) return output;
        Write(redirect, operation == ">>" ? files.GetValueOrDefault(redirect, "") + output : output);
        return "";
    }

    private string Run(string[] words, string input, bool privileged = false)
    {
        bool previous = elevated;
        elevated = privileged;
        try { return RunCommand(words, input); }
        finally { elevated = previous; }
    }

    private string RunCommand(string[] words, string input)
    {
        if (words.Length == 0) throw Error("syntax", "ожидалась команда");
        string command = words[0];
        string[] args = words[1..];
        switch (command)
        {
            case "python" or "python3" when lessonUnitTests.Count > 0:
                if (args.Length == 3 && args[0] == "-m" && args[1] == "unittest" &&
                    lessonUnitTests.TryGetValue(args[2], out var lessonTest))
                    return lessonTest(this);
                throw Error(command, "доступны только тесты текущего учебного проекта через -m unittest");
            case "sudo":
                if (args.Length == 0) throw Error("sudo", "укажите команду");
                return Run(args, input, true);
            case "pwd": NoArgs(command, args); return Cwd + "\n";
            case "whoami": NoArgs(command, args); return "student\n";
            case "id": NoArgs(command, args); return "uid=1000(student) gid=1000(student) groups=1000(student),27(sudo)\n";
            case "cd":
                AtMost(command, args, 1);
                string next = args.Length == 0 ? Home : Resolve(args[0]);
                if (!directories.Contains(next)) throw Error(command, next + ": каталог не найден");
                Access(next, 1);
                Cwd = next; Version++; return "";
            case "ls": return ListFiles(args);
            case "tree":
                if (!packages.Contains("tree")) throw Error("tree", "команда не установлена, выполните sudo apt install tree");
                return Tree(args);
            case "mkdir": return MakeDirectories(args);
            case "touch":
                RequireOperands(command, args);
                foreach (string value in args)
                {
                    string path = Resolve(value); EnsureParent(path);
                    if (directories.Contains(path)) continue;
                    if (!files.ContainsKey(path)) Write(path, "");
                    else { if (!elevated && Owner(path).User != "student") Access(path, 2); Version++; }
                }
                return "";
            case "cp": return CopyMove(args, false);
            case "mv": return CopyMove(args, true);
            case "rm": return Remove(args);
            case "rmdir":
                RequireOperands(command, args);
                foreach (string value in args)
                {
                    string path = Resolve(value);
                    if (!directories.Contains(path)) throw Error(command, "каталог не найден: " + value);
                    if (files.Keys.Any(p => Within(p, path)) || directories.Any(p => p != path && Within(p, path)))
                        throw Error(command, "каталог не пуст: " + value);
                    GuardDirectoryRemoval(path); Access(Parent(path), 3); directories.Remove(path); modes.Remove(path); owners.Remove(path); Version++;
                }
                return "";
            case "cat": return Cat(args, input);
            case "less":
                RequireOperands(command, args); return Cat(args, input);
            case "head": return Slice(args, input, false);
            case "tail": return Slice(args, input, true);
            case "grep": return Grep(args, input);
            case "find": return Find(args);
            case "wc": return Count(args, input);
            case "sort": return Sort(args, input);
            case "uniq": return Unique(args, input);
            case "echo":
                bool newline = args.Length == 0 || args[0] != "-n";
                return string.Join(" ", newline ? args : args[1..]) + (newline ? "\n" : "");
            case "clear": NoArgs(command, args); history.Clear(); return "";
            case "history": NoArgs(command, args); return string.Join("\n", history.Where(line => line.StartsWith("student@ubuntu:"))) + "\n";
            case "chmod": return Chmod(args);
            case "chown":
                if (!elevated) throw Error("chown", "операция требует sudo");
                return Chown(args);
            case "ps":
                if (args.Length > 1 || args.Any(arg => arg != "aux" && arg != "-ef")) throw Error(command, "поддерживаются ps, ps aux и ps -ef");
                return "PID COMMAND\n" + string.Join("\n", processes.Select(p => p.Key + " " + p.Value)) + "\n";
            case "top": NoArgs(command, args); return "PID COMMAND\n" + string.Join("\n", processes.Select(p => p.Key + " " + p.Value)) + "\n";
            case "kill": return Kill(args);
            case "apt":
                if (!elevated && args.FirstOrDefault() is "update" or "upgrade" or "install" or "remove")
                    throw Error("apt", "операция требует sudo");
                return Apt(args);
            case "git":
                if (!packages.Contains("git")) throw Error("git", "команда не установлена, выполните sudo apt install git");
                return Git(args);
            case "uname":
                if (args.Length > 1 || args.Any(arg => arg is not ("-a" or "-s" or "-r" or "-m"))) throw Error("uname", "поддерживаются -a, -s, -r и -m");
                return (args.FirstOrDefault() switch { "-a" => "Linux ubuntu-training 6.8.0 #1 SMP x86_64 GNU/Linux", "-r" => "6.8.0", "-m" => "x86_64", _ => "Linux" }) + "\n";
            case "date": NoArgs(command, args); return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n";
            case "reboot": NoArgs(command, args); reboots++; Cwd = Home; Version++; return "Учебная система перезагружена.\n";
            case "shutdown":
                if (args.Any(arg => arg != "now" && arg != "-h" && arg != "-r")) throw Error(command, "поддерживаются shutdown now, shutdown -h now, shutdown -r now");
                if (args.Contains("-r")) reboots++; else shutdowns++;
                Cwd = Home; Version++; return args.Contains("-r") ? "Учебная система перезагружена.\n" : "Учебная система выключена и готова к следующему заданию.\n";
            default: throw Error(command, "команда пока не поддерживается в учебной системе");
        }
    }

    private string ListFiles(string[] args)
    {
        var flags = args.Where(arg => arg.StartsWith('-')).ToArray();
        if (flags.Any(arg => arg[1..].Any(c => c is not ('a' or 'l' or 'h')))) throw Error("ls", "поддерживаются ключи -a, -l и -h");
        var paths = args.Where(arg => !arg.StartsWith('-')).ToArray();
        if (paths.Length == 0) paths = [Cwd];
        bool detailed = flags.Any(flag => flag.Contains('l'));
        bool hidden = flags.Any(flag => flag.Contains('a'));
        var result = new List<string>();
        foreach (string value in paths)
        {
            string path = Resolve(value);
            if (files.ContainsKey(path)) result.Add(detailed ? DescribeFile(path) : Name(path));
            else if (directories.Contains(path))
            {
                Access(path, 5);
                var children = Children(path).Where(child => hidden || !Name(child).StartsWith('.')).Order().ToArray();
                result.AddRange(children.Select(child => detailed ? DescribeFile(child) : Name(child) + (directories.Contains(child) ? "/" : "")));
            }
            else throw Error("ls", value + ": путь не найден");
        }
        return JoinLines(result);
    }

    private string Tree(string[] args)
    {
        AtMost("tree", args, 1);
        string path = args.Length == 0 ? Cwd : Resolve(args[0]);
        if (!directories.Contains(path)) throw Error("tree", "каталог не найден");
        Access(path, 5);
        return JoinLines(new[] { path }.Concat(directories.Concat(files.Keys).Where(p => p != path && Within(p, path)).Order()
            .Select(p => "  " + p[(path == "/" ? 1 : path.Length + 1)..])));
    }

    private string MakeDirectories(string[] args)
    {
        bool parents = args.FirstOrDefault() == "-p";
        if (parents) args = args[1..];
        RequireOperands("mkdir", args);
        foreach (string value in args)
        {
            string path = Resolve(value);
            if (files.ContainsKey(path)) throw Error("mkdir", value + ": уже существует файл");
            if (directories.Contains(path))
            {
                if (!parents) throw Error("mkdir", value + ": уже существует");
                continue;
            }
            if (parents)
            {
                var chain = new Stack<string>();
                for (string item = path; !directories.Contains(item); item = Parent(item)) chain.Push(item);
                while (chain.TryPop(out string item))
                {
                    if (files.ContainsKey(item)) throw Error("mkdir", item + ": существует файл");
                    Access(Parent(item), 3); directories.Add(item); if (elevated) owners[item] = "root:root"; Version++;
                }
            }
            else { EnsureParent(path); Access(Parent(path), 3); directories.Add(path); if (elevated) owners[path] = "root:root"; Version++; }
        }
        return "";
    }

    private string CopyMove(string[] args, bool move)
    {
        bool recursive = !move && args.Length > 0 && args[0] is "-r" or "-R";
        if (recursive) args = args[1..];
        string command = move ? "mv" : "cp";
        if (args.Length != 2 || args.Any(arg => arg.StartsWith('-'))) throw Error(command, "укажите источник и назначение");
        string source = Resolve(args[0]), destination = Resolve(args[1]);
        if (!Exists(source)) throw Error(command, args[0] + ": путь не найден");
        if (directories.Contains(destination)) destination = Combine(destination, Name(source));
        if (source == destination) throw Error(command, "источник и назначение совпадают");
        EnsureParent(destination);
        if (move) Access(Parent(source), 3);
        if (files.ContainsKey(source))
        {
            if (move)
            {
                if (directories.Contains(destination)) throw Error(command, "назначение является каталогом");
                Access(Parent(destination), 3);
                files[destination] = files[source]; modes[destination] = Mode(source);
                owners[destination] = owners.GetValueOrDefault(source, "student:student");
                files.Remove(source); modes.Remove(source); owners.Remove(source); Version++;
            }
            else
            {
                bool existed = files.ContainsKey(destination);
                Write(destination, File(source));
                if (!existed) modes[destination] = Mode(source);
            }
            return "";
        }
        if (!recursive && !move) throw Error(command, "для каталога нужен ключ -r");
        if (Within(destination, source)) throw Error(command, "нельзя переместить каталог внутрь себя");
        if (files.ContainsKey(destination)) throw Error(command, "назначение является файлом");
        if (move) GuardDirectoryRemoval(source);
        if (move && directories.Contains(destination) && Children(destination).Any()) throw Error(command, "каталог назначения не пуст");
        Access(source, 5); Access(Parent(destination), 3);
        foreach (string path in directories.Where(p => Within(p, source)).OrderBy(p => p.Length).ToArray())
        {
            string target = destination + path[source.Length..];
            if (directories.Add(target))
            { modes[target] = Mode(path); if (move) owners[target] = owners.GetValueOrDefault(path, "student:student"); else if (elevated) owners[target] = "root:root"; }
        }
        foreach (var file in files.Where(p => Within(p.Key, source)).ToArray())
        {
            string target = destination + file.Key[source.Length..];
            bool existed = files.ContainsKey(target);
            if (move) files[target] = file.Value; else Write(target, File(file.Key));
            if (!existed || move) modes[target] = Mode(file.Key);
            if (move) owners[target] = owners.GetValueOrDefault(file.Key, "student:student");
        }
        if (move)
        {
            foreach (string path in files.Keys.Where(p => Within(p, source)).ToArray()) { files.Remove(path); modes.Remove(path); owners.Remove(path); }
            foreach (string path in directories.Where(p => Within(p, source)).ToArray()) { directories.Remove(path); modes.Remove(path); owners.Remove(path); }
        }
        Version++; return "";
    }

    private string Remove(string[] args)
    {
        bool recursive = false, force = false;
        var paths = new List<string>();
        foreach (string arg in args)
        {
            if (!arg.StartsWith('-')) paths.Add(arg);
            else
            {
                if (arg[1..].Any(c => c is not ('r' or 'R' or 'f'))) throw Error("rm", "поддерживаются ключи -r и -f");
                recursive |= arg.Contains('r') || arg.Contains('R'); force |= arg.Contains('f');
            }
        }
        if (paths.Count == 0) throw Error("rm", "укажите путь");
        foreach (string value in paths)
        {
            string path = Resolve(value);
            if (files.ContainsKey(path))
            { Access(Parent(path), 3); files.Remove(path); modes.Remove(path); owners.Remove(path); Version++; continue; }
            if (!directories.Contains(path)) { if (!force) throw Error("rm", value + ": путь не найден"); continue; }
            if (!recursive) throw Error("rm", value + ": это каталог, нужен -r");
            GuardDirectoryRemoval(path); Access(Parent(path), 3);
            foreach (string file in files.Keys.Where(p => Within(p, path)).ToArray()) { files.Remove(file); modes.Remove(file); owners.Remove(file); }
            foreach (string directory in directories.Where(p => Within(p, path)).ToArray()) { directories.Remove(directory); modes.Remove(directory); owners.Remove(directory); }
            Version++;
        }
        return "";
    }

    private void GuardDirectoryRemoval(string path)
    {
        if (path == "/" || path == "/home" || path == Home || Within(Cwd, path))
            throw Error("workspace", "сначала выйдите из текущего каталога, системные каталоги учебной среды защищены");
    }

    private string Cat(string[] args, string input)
    {
        if (args.Any(arg => arg.StartsWith('-'))) throw Error("cat", "ключи пока не поддерживаются");
        return args.Length == 0 ? input : string.Concat(args.Select(arg => File(Resolve(arg))));
    }

    private string Slice(string[] args, string input, bool tail)
    {
        int count = 10;
        if (args.Length > 0 && args[0] == "-n")
        {
            if (args.Length < 2 || !int.TryParse(args[1], out count) || count < 0) throw Error(tail ? "tail" : "head", "после -n нужно неотрицательное число");
            args = args[2..];
        }
        if (args.Any(arg => arg.StartsWith('-'))) throw Error(tail ? "tail" : "head", "поддерживается -n ЧИСЛО");
        string[] lines = Lines(Cat(args, input));
        return JoinLines(tail ? lines.TakeLast(count) : lines.Take(count));
    }

    private string Grep(string[] args, string input)
    {
        var flags = new HashSet<char>();
        int i = 0;
        while (i < args.Length && args[i].StartsWith('-'))
        {
            foreach (char flag in args[i][1..])
                if (flag is 'i' or 'n' or 'v' or 'c' or 'F') flags.Add(flag);
                else throw Error("grep", "поддерживаются ключи -i, -n, -v, -c, -F");
            i++;
        }
        if (i == args.Length) throw Error("grep", "нужно выражение для поиска");
        string pattern = args[i++];
        string[] sources = args[i..];
        string value = sources.Length == 0 ? input : string.Concat(sources.Select(path => File(Resolve(path))));
        var lines = Lines(value);
        var matches = new List<string>();
        Regex regex = null;
        if (!flags.Contains('F'))
        {
            try { regex = new Regex(pattern, flags.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(50)); }
            catch (ArgumentException) { throw Error("grep", "неверное регулярное выражение"); }
        }
        for (int n = 0; n < lines.Length; n++)
        {
            bool match;
            try { match = regex == null ? lines[n].Contains(pattern, flags.Contains('i') ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) : regex.IsMatch(lines[n]); }
            catch (RegexMatchTimeoutException) { throw Error("grep", "слишком сложное выражение"); }
            if (flags.Contains('v')) match = !match;
            if (match) matches.Add(flags.Contains('n') ? (n + 1) + ":" + lines[n] : lines[n]);
        }
        return flags.Contains('c') ? matches.Count + "\n" : JoinLines(matches);
    }

    private string Find(string[] args)
    {
        string supplied = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : ".";
        string path = Resolve(supplied);
        if (args.Length > 0 && !args[0].StartsWith('-')) args = args[1..];
        if (!Exists(path)) throw Error("find", "путь не найден");
        string pattern = null, type = null;
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length) throw Error("find", "после ключа нужно значение");
            if (args[i] == "-name") pattern = args[i + 1];
            else if (args[i] == "-type" && args[i + 1] is "f" or "d") type = args[i + 1];
            else throw Error("find", "поддерживаются -name ШАБЛОН и -type f/d");
        }
        return JoinLines(directories.Concat(files.Keys).Where(p => Within(p, path))
            .Where(p => type == null || (type == "d" ? directories.Contains(p) : files.ContainsKey(p)))
            .Where(p => pattern == null || Wildcard(Name(p), pattern)).Order()
            .Select(p => supplied.StartsWith('/') || supplied.StartsWith('~') ? p : supplied.TrimEnd('/') + p[path.Length..]));
    }

    private string Count(string[] args, string input)
    {
        string option = args.FirstOrDefault()?.StartsWith('-') == true ? args[0] : "";
        if (option != "") args = args[1..];
        if (option is not ("" or "-l" or "-w" or "-c")) throw Error("wc", "поддерживаются -l, -w и -c");
        string value = Cat(args, input);
        int lines = value.Count(c => c == '\n');
        int words = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        int bytes = Encoding.UTF8.GetByteCount(value);
        return (option switch { "-l" => lines.ToString(), "-w" => words.ToString(), "-c" => bytes.ToString(), _ => $"{lines} {words} {bytes}" }) + "\n";
    }

    private string Sort(string[] args, string input)
    {
        bool reverse = args.FirstOrDefault() == "-r";
        if (reverse) args = args[1..];
        string[] lines = Lines(Cat(args, input));
        Array.Sort(lines, StringComparer.Ordinal);
        if (reverse) Array.Reverse(lines);
        return JoinLines(lines);
    }

    private string Unique(string[] args, string input)
    {
        bool count = args.FirstOrDefault() == "-c";
        if (count) args = args[1..];
        string[] lines = Lines(Cat(args, input));
        var output = new List<string>();
        for (int i = 0; i < lines.Length;)
        {
            int end = i + 1;
            while (end < lines.Length && lines[end] == lines[i]) end++;
            output.Add(count ? $"{end - i} {lines[i]}" : lines[i]);
            i = end;
        }
        return JoinLines(output);
    }

    private string Chmod(string[] args)
    {
        if (args.Length < 2) throw Error("chmod", "укажите режим и путь");
        string mode = args[0];
        if (!Regex.IsMatch(mode, "^[0-7]{3}$") && mode is not ("+x" or "u+x" or "-x" or "u-x"))
            throw Error("chmod", "поддерживаются числовой режим, +x, u+x, -x и u-x");
        foreach (string arg in args[1..])
        {
            string path = Resolve(arg); if (!Exists(path)) throw Error("chmod", arg + ": путь не найден");
            if (!elevated && Owner(path).User != "student") throw Error("chmod", arg + ": изменение режима требует владельца или sudo");
            string current = Mode(path);
            if (mode.Length == 3 && mode.All(char.IsDigit)) modes[path] = mode;
            else
            {
                var digits = current.Select(c => c - '0').ToArray();
                for (int i = 0; i < (mode.StartsWith('u') ? 1 : 3); i++) digits[i] = mode.Contains('+') ? digits[i] | 1 : digits[i] & ~1;
                modes[path] = string.Concat(digits.Select(d => (char)('0' + d)));
            }
            Version++;
        }
        return "";
    }

    private string Chown(string[] args)
    {
        if (args.Length < 2 || !Regex.IsMatch(args[0], "^[a-z_][a-z0-9_-]*(?::[a-z_][a-z0-9_-]*)?$")) throw Error("chown", "укажите пользователя и путь");
        foreach (string arg in args[1..])
        {
            string path = Resolve(arg); if (!Exists(path)) throw Error("chown", arg + ": путь не найден");
            owners[path] = args[0]; Version++;
        }
        return "";
    }

    private string Kill(string[] args)
    {
        if (args.FirstOrDefault() is "-9" or "-15") args = args[1..];
        if (args.Length == 0 || args.Any(arg => !int.TryParse(arg, out _))) throw Error("kill", "укажите PID, допускаются сигналы -9 и -15");
        foreach (string value in args)
        {
            int pid = int.Parse(value);
            if (pid is 1 or 120) throw Error("kill", "процесс учебной среды защищён");
            if (!processes.Remove(pid)) throw Error("kill", "процесс не найден: " + pid);
            Version++;
        }
        return "";
    }

    private string Apt(string[] args)
    {
        if (args.Length == 0) throw Error("apt", "укажите update, upgrade, install, remove, search, show или list");
        string action = args[0]; args = args[1..];
        if (action == "update") { NoArgs("apt update", args); packagesUpdated = true; Version++; return "Учебные списки пакетов обновлены.\n"; }
        if (action == "upgrade") { NoArgs("apt upgrade", args); if (!packagesUpdated) throw Error("apt", "сначала выполните apt update"); Version++; return "Все учебные пакеты актуальны.\n"; }
        if (action == "list") { if (args.Length > 1 || args.Length == 1 && args[0] != "--installed") throw Error("apt", "поддерживается list --installed"); return JoinLines(packages.Order().Select(p => p + " [installed]")); }
        if (action is "install" or "remove")
        {
            args = args.Where(arg => arg != "-y").ToArray(); RequireOperands("apt " + action, args);
            foreach (string package in args)
            {
                if (action == "install" && !AvailablePackages.ContainsKey(package)) throw Error("apt", "пакет не найден: " + package);
                if (action == "remove" && (!packages.Contains(package) || package is "bash" or "coreutils")) throw Error("apt", "пакет не установлен или защищён: " + package);
            }
            foreach (string package in args) { if (action == "install") packages.Add(package); else packages.Remove(package); }
            Version++; return JoinLines(args.Select(p => p + (action == "install" ? " установлен в учебной системе" : " удалён из учебной системы")));
        }
        if (action == "search")
        {
            if (args.Length != 1) throw Error("apt search", "укажите поисковое слово");
            return JoinLines(AvailablePackages.Where(p => p.Key.Contains(args[0], StringComparison.OrdinalIgnoreCase)).Select(p => p.Key + " " + p.Value));
        }
        if (action == "show")
        {
            if (args.Length != 1 || !AvailablePackages.TryGetValue(args[0], out string version)) throw Error("apt show", "пакет не найден");
            return $"Package: {args[0]}\nVersion: {version}\nStatus: {(packages.Contains(args[0]) ? "installed" : "available")}\n";
        }
        throw Error("apt", "неподдерживаемое действие");
    }

    private string Git(string[] args)
    {
        if (args.Length == 0) throw Error("git", "укажите подкоманду");
        string action = args[0]; args = args[1..];
        if (action == "init")
        {
            AtMost("git init", args, 1);
            string path = args.Length == 0 ? Cwd : Resolve(args[0]);
            if (!directories.Contains(path)) throw Error("git init", "каталог не найден");
            if (repository != null && repository != path) throw Error("git init", "учебная сессия поддерживает один репозиторий");
            Access(path, 3);
            repository = path;
            branchLogs.TryAdd(branch, []);
            Version++; return "Initialized empty Git repository in " + path + "/.git/\n";
        }
        if (repository == null || !Within(Cwd, repository)) throw Error("git", "текущий каталог не является репозиторием");
        var current = RepositoryFiles();
        switch (action)
        {
            case "status":
                if (args.Any(a => a is not ("--short" or "-s"))) throw Error("git status", "поддерживаются --short и -s");
                var status = new List<string> { "On branch " + branch };
                foreach (string path in current.Keys.Union(committed.Keys).Order())
                {
                    bool staged = current.GetValueOrDefault(path) == index.GetValueOrDefault(path) && index.GetValueOrDefault(path) != committed.GetValueOrDefault(path);
                    string prefix = !committed.ContainsKey(path) ? (index.ContainsKey(path) ? "A " : "??") : !current.ContainsKey(path) ? " D" : current.GetValueOrDefault(path) != committed.GetValueOrDefault(path) ? staged ? "M " : " M" : "";
                    if (prefix.Length > 0) status.Add(prefix + " " + path);
                }
                if (status.Count == 1) status.Add("nothing to commit, working tree clean");
                return JoinLines(status);
            case "add":
                bool all = args.FirstOrDefault() is "-A" or "--all";
                if (all) args = args[1..];
                if (args.Length == 0 && !all) throw Error("git add", "укажите путь");
                if (args.Length == 0) args = ["."];
                foreach (string arg in args)
                {
                    string path = Resolve(arg);
                    if (!Within(path, repository)) throw Error("git add", "путь находится вне репозитория");
                    string relative = path == repository ? "" : path[(repository.Length + 1)..];
                    var matches = current.Keys.Union(index.Keys).Where(p => relative == "" || p == relative || p.StartsWith(relative + "/", StringComparison.Ordinal)).ToArray();
                    if (matches.Length == 0 && !directories.Contains(path)) throw Error("git add", "путь не найден: " + arg);
                    foreach (string match in matches)
                    {
                        if (current.TryGetValue(match, out string value)) index[match] = value; else index.Remove(match);
                    }
                }
                Version++; return "";
            case "commit":
                if (args.Length != 2 || args[0] != "-m" || string.IsNullOrWhiteSpace(args[1])) throw Error("git commit", "используйте git commit -m \"сообщение\"");
                if (Same(index, committed)) throw Error("git commit", "нет подготовленных изменений");
                Replace(committed, index); commits.Add(args[1]); branches[branch] = new(committed);
                branchLogs.TryAdd(branch, []); branchLogs[branch].Add(commits.Count - 1); Version++;
                return $"[{branch} {commits.Count:x7}] {args[1]}\n";
            case "diff":
                if (args.Any(a => a is not ("--cached" or "--staged"))) throw Error("git diff", "поддерживаются --cached и --staged");
                var changed = args.Length == 0 ? current.Where(pair => index.ContainsKey(pair.Key) || committed.ContainsKey(pair.Key)).ToDictionary() : index;
                var original = args.Length == 0 ? index : committed;
                return JoinLines(changed.Keys.Union(original.Keys).Where(p => changed.GetValueOrDefault(p) != original.GetValueOrDefault(p)).Order()
                    .SelectMany(p => new[] { "diff --git a/" + p + " b/" + p, "--- a/" + p, "+++ b/" + p }.Concat(Lines(original.GetValueOrDefault(p, "")).Select(l => "-" + l)).Concat(Lines(changed.GetValueOrDefault(p, "")).Select(l => "+" + l))));
            case "log":
                if (args.Any(a => a != "--oneline")) throw Error("git log", "поддерживается --oneline");
                return JoinLines(branchLogs.GetValueOrDefault(branch, []).Select(i => $"{i + 1:x7} {commits[i]}").Reverse());
            case "branch":
                AtMost("git branch", args, 1);
                if (args.Length == 0) return JoinLines(branches.Keys.Append(branch).Distinct().Order().Select(b => (b == branch ? "* " : "  ") + b));
                CreateBranch(args[0]); return "";
            case "switch":
                bool create = args.FirstOrDefault() == "-c";
                if (create) args = args[1..];
                if (args.Length != 1) throw Error("git switch", "укажите ветку, новая ветка создаётся с -c");
                var tracked = current.Where(pair => committed.ContainsKey(pair.Key) || index.ContainsKey(pair.Key)).ToDictionary();
                if (!Same(tracked, committed) || !Same(index, committed)) throw Error("git switch", "сначала сохраните изменения коммитом");
                if (create) CreateBranch(args[0]);
                if (args[0] != branch && !branches.ContainsKey(args[0])) throw Error("git switch", "ветка не найдена");
                var targetFiles = branches.GetValueOrDefault(args[0], committed);
                if (targetFiles.Keys.Any(path => current.ContainsKey(path) && !tracked.ContainsKey(path)))
                    throw Error("git switch", "неотслеживаемые файлы будут перезаписаны, сначала сохраните их");
                branches[branch] = new(committed); branch = args[0]; Replace(committed, branches.GetValueOrDefault(branch, new())); Replace(index, committed);
                foreach (string path in tracked.Keys) files.Remove(Combine(repository, path));
                foreach (var pair in committed) Write(Combine(repository, pair.Key), pair.Value);
                Version++; return "Switched to branch '" + branch + "'\n";
            default: throw Error("git", "поддерживаются init, status, add, commit, diff, log, branch и switch");
        }
    }

    private void CreateBranch(string name)
    {
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_/-]*$") || name.Contains("..") || branches.ContainsKey(name) || name == branch)
            throw Error("git branch", "ветка уже существует или имя неверно");
        branches[name] = new(committed);
        branchLogs[name] = new(branchLogs.GetValueOrDefault(branch, [])); Version++;
    }

    private Dictionary<string, string> RepositoryFiles() => files.Where(p => Within(p.Key, repository))
        .ToDictionary(p => p.Key[(repository == "/" ? 1 : repository.Length + 1)..], p => p.Value, StringComparer.Ordinal);
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
    private static void Replace(Dictionary<string, string> target, Dictionary<string, string> source)
    { target.Clear(); foreach (var pair in source) target[pair.Key] = pair.Value; }

    private string File(string path)
    {
        if (!files.TryGetValue(path, out string content)) throw Error("file", path + ": файл не найден");
        Access(path, 4); return content;
    }
    private void Write(string path, string content)
    {
        EnsureParent(path);
        if (directories.Contains(path)) throw Error("file", path + ": это каталог");
        if (content.Length > 1048576 || files.Where(pair => pair.Key != path).Sum(pair => (long)pair.Value.Length) + content.Length > 8388608)
            throw Error("file", "превышен объём учебной файловой системы");
        Access(files.ContainsKey(path) ? path : Parent(path), files.ContainsKey(path) ? 2 : 3);
        if (!files.ContainsKey(path) && elevated) owners[path] = "root:root";
        files[path] = content; Version++;
    }

    private string Mode(string path) => modes.GetValueOrDefault(path, path == "/tmp" ? "777" : directories.Contains(path) ? "755" : "644");
    private (string User, string Group) Owner(string path)
    {
        string owner = owners.GetValueOrDefault(path, path is "/" or "/home" or "/tmp" ? "root:root" : "student:student");
        string[] parts = owner.Split(':');
        return (parts[0], parts.Length == 2 ? parts[1] : "student");
    }
    private void Access(string path, int rights)
    {
        if (elevated) return;
        for (string parent = Parent(path); parent != "/"; parent = Parent(parent)) RequireRights(parent, 1);
        RequireRights(path, rights);
    }
    private void RequireRights(string path, int rights)
    {
        var owner = Owner(path);
        int position = owner.User == "student" ? 0 : owner.Group == "student" ? 1 : 2;
        if (((Mode(path)[position] - '0') & rights) != rights) throw Error("permission", path + ": доступ запрещён, проверьте права или используйте sudo");
    }
    private void EnsureParent(string path)
    { if (!directories.Contains(Parent(path))) throw Error("file", Parent(path) + ": каталог не найден"); }
    private IEnumerable<string> Children(string directory) => files.Keys.Concat(directories).Where(p => p != directory && Parent(p) == directory);
    private string DescribeFile(string path)
    {
        bool directory = directories.Contains(path);
        string mode = modes.GetValueOrDefault(path, directory ? "755" : "644");
        string rights = string.Concat(mode.Select(c => (c - '0') switch
        { 0 => "---", 1 => "--x", 2 => "-w-", 3 => "-wx", 4 => "r--", 5 => "r-x", 6 => "rw-", _ => "rwx" }));
        string owner = owners.GetValueOrDefault(path, "student");
        int size = directory ? 4096 : Encoding.UTF8.GetByteCount(files[path]);
        return $"{(directory ? 'd' : '-')}{rights} 1 {owner} student {size} {Name(path)}";
    }
    private string Resolve(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('\0')) throw Error("path", "пустой путь или недопустимый нулевой символ");
        if (value == "~") value = Home;
        else if (value.StartsWith("~/", StringComparison.Ordinal)) value = Home + value[1..];
        if (!value.StartsWith('/')) value = Combine(Cwd, value);
        var parts = new List<string>();
        foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else
            {
                if (Encoding.UTF8.GetByteCount(part) > 255) throw Error("path", "компонент имени превышает 255 байт");
                parts.Add(part);
            }
        }
        string result = "/" + string.Join("/", parts);
        if (Encoding.UTF8.GetByteCount(result) >= 4096) throw Error("path", "полный путь превышает предел учебной файловой системы");
        return result;
    }
    private IEnumerable<string> Expand(string value)
    {
        if (!value.Contains('*') && !value.Contains('?')) return [value];
        string absolute = Resolve(value), parent = Parent(absolute), pattern = Name(absolute);
        var matches = Children(parent).Where(path => !Name(path).StartsWith('.') && Wildcard(Name(path), pattern)).Order().ToArray();
        return matches.Length == 0 ? [value] : matches;
    }
    private static bool Wildcard(string value, string pattern) => Regex.IsMatch(value, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    private static bool Within(string path, string directory) => path == directory || (directory == "/" ? path.StartsWith('/') : path.StartsWith(directory + "/", StringComparison.Ordinal));
    private static string Parent(string path) { int split = path.LastIndexOf('/'); return split <= 0 ? "/" : path[..split]; }
    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
    private static string Combine(string directory, string path) => directory.TrimEnd('/') + "/" + path;
    private static string Abbreviate(string path) => path == Home || path.StartsWith(Home + "/", StringComparison.Ordinal) ? "~" + path[Home.Length..] : path;
    private static string[] Lines(string value) => value.Length == 0 ? [] : value.TrimEnd('\n').Split('\n');
    private static string JoinLines(IEnumerable<string> lines) { var values = lines.ToArray(); return values.Length == 0 ? "" : string.Join("\n", values) + "\n"; }
    private static ArgumentException Error(string command, string message) => new(command + ": " + message);
    private static void NoArgs(string command, string[] args) { if (args.Length != 0) throw Error(command, "лишние аргументы"); }
    private static void AtMost(string command, string[] args, int count) { if (args.Length > count) throw Error(command, "слишком много аргументов"); }
    private static void RequireOperands(string command, string[] args)
    { if (args.Length == 0 || args.Any(arg => arg.StartsWith('-'))) throw Error(command, "укажите пути, этот вариант ключей пока не поддерживается"); }

    private sealed record Token(string Value, bool Operator, bool Expand = false);
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var value = new StringBuilder();
        bool started = false, quoted = false, escaped = false;
        char quote = '\0';
        void Flush()
        {
            if (started) tokens.Add(new(value.ToString(), false, !quoted));
            value.Clear(); started = quoted = false;
        }
        for (int position = 0; position < text.Length; position++)
        {
            char c = text[position];
            if (escaped) { value.Append(c); started = true; escaped = false; continue; }
            if (c == '\\' && quote != '\'')
            {
                if (quote == '"' && position + 1 < text.Length && text[position + 1] is not ('"' or '\\' or '$' or '`' or '\n'))
                { value.Append(c); started = quoted = true; continue; }
                escaped = true; started = true; quoted = true; continue;
            }
            if (quote == '"' && c is '$' or '`')
                throw Error("syntax", "подстановка переменных и команд пока не поддерживается");
            if (quote != '\0')
            {
                if (c == quote) quote = '\0'; else value.Append(c);
                started = quoted = true; continue;
            }
            if (c is '\'' or '"') { quote = c; started = quoted = true; continue; }
            if (char.IsWhiteSpace(c)) { Flush(); continue; }
            if (c is '|' or '>' or '<')
            {
                Flush();
                if (c == '>' && position + 1 < text.Length && text[position + 1] == '>')
                { tokens.Add(new(">>", true)); position++; }
                else tokens.Add(new(c.ToString(), true));
                continue;
            }
            if (c is ';' or '&' or '`' || c == '$') throw Error("syntax", "эта конструкция пока не поддерживается, используйте отдельные команды");
            value.Append(c); started = true;
        }
        if (quote != '\0' || escaped) throw Error("syntax", "незавершённая кавычка или экранирование");
        Flush(); return tokens;
    }
    private static List<List<Token>> SplitPipeline(List<Token> tokens)
    {
        var stages = new List<List<Token>> { new() };
        foreach (Token token in tokens)
        {
            if (token.Operator && token.Value == "|")
            {
                if (stages[^1].Count == 0) throw Error("syntax", "пустая команда в конвейере");
                stages.Add([]);
            }
            else stages[^1].Add(token);
        }
        if (stages[^1].Count == 0) throw Error("syntax", "пустая команда");
        return stages;
    }
}
