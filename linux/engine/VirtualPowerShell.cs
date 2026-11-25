using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal sealed partial class VirtualTerminal
{
    private const string PsProject = @"C:\Users\Student\Project";
    private const string VirtualProject = "/home/student/project";
    internal bool IsPowerShell { get; private set; }
    internal int LogInterval => IsPowerShell ? 1000 : 420;
    internal string Prompt => IsPowerShell ? $"PS {PsProject}> " : $"student@ubuntu:{Abbreviate(Cwd)}$ ";

    internal void UsePowerShell()
    {
        IsPowerShell = true;
        // This plain Windows exercise starts with a fresh simulated workspace.
        // It does not expose the Bash fixture or files from a real C: drive.
        files.Clear(); modes.Clear(); owners.Clear();
        history.Clear();
        history.Add("Учебный Windows PowerShell. Журналы находятся в " + PsProject + ".");
        StartInsideProject();
    }

    private sealed record PsWord(string Value, bool Quoted);

    private static List<PsWord> PowerShellWords(string input)
    {
        // This is deliberately not a shell interpreter. Only literals and the
        // two supported cmdlets can reach the task's virtual files.
        var words = new List<PsWord>();
        for (int i = 0; i < input.Length;)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i == input.Length) break;
            var value = new StringBuilder();
            char quote = input[i] is '\'' or '"' ? input[i++] : '\0';
            if (quote != '\0')
            {
                bool closed = false;
                while (i < input.Length)
                {
                    char c = input[i++];
                    if (c == quote)
                    {
                        if (i < input.Length && input[i] == quote) { value.Append(quote); i++; continue; }
                        closed = true;
                        break;
                    }
                    if (c == '`' || c == '$' && quote == '"')
                        throw Error("PowerShell", "в учебном стенде используйте буквальные значения, без подстановки");
                    value.Append(c);
                }
                if (!closed || i < input.Length && !char.IsWhiteSpace(input[i]))
                    throw Error("PowerShell", "закройте кавычки и отделите следующие аргументы пробелом");
            }
            else
            {
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                {
                    char c = input[i++];
                    if (c is '\'' or '"' or '|' or '>' or '<' or ';' or '&' or '(' or ')' or '$' or '`' or ',')
                        throw Error("PowerShell", "поддерживается одна команда с буквальными аргументами, без сценариев и конвейеров");
                    value.Append(c);
                }
            }
            words.Add(new(value.ToString(), quote != '\0'));
        }
        return words;
    }

    private string PowerShellFile(string input)
    {
        string part = input.Replace('/', '\\');
        if (part.StartsWith(PsProject + "\\", StringComparison.OrdinalIgnoreCase)) part = part[(PsProject.Length + 1)..];
        else if (part.StartsWith(".\\", StringComparison.Ordinal)) part = part[2..];
        if (part.Length == 0 || part.Contains('\\') || part.IndexOfAny([':', '*', '?']) >= 0 ||
            part is "." or "..") throw Error("PowerShell", "выберите файл только из учебной папки Project");
        string path = VirtualProject + "/" + part;
        return files.Keys.FirstOrDefault(name => name.Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? throw Error("PowerShell", "файл не найден: " + input);
    }

    private string[] CompletePowerShell(string prefix)
    {
        int sep = Math.Max(prefix.LastIndexOf('\\'), prefix.LastIndexOf('/'));
        string folder = sep < 0 ? "" : prefix[..(sep + 1)];
        string normalized = folder.Replace('/', '\\');
        if (!(normalized is "" or ".\\" || normalized.Equals(PsProject + "\\", StringComparison.OrdinalIgnoreCase))) return [];
        string name = prefix[(sep + 1)..];
        return Children(VirtualProject).Where(files.ContainsKey).Select(Name)
            .Where(leaf => leaf.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).Select(leaf => folder + leaf).ToArray();
    }

    private string RunPowerShell(string text)
    {
        var words = PowerShellWords(text);
        if (words.Count == 0 || words[0].Quoted) throw Error("PowerShell", "введите имя команды");
        string verb = words[0].Value.ToUpperInvariant();
        bool select = verb == "SELECT-STRING";
        if (!select && verb != "GET-CONTENT")
            throw Error(words[0].Value, "в Windows PowerShell этого задания доступны Select-String и Get-Content");
        var positionals = new Queue<string>();
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string file = null, pattern = null;
        int? tail = null;
        for (int i = 1; i < words.Count; i++)
        {
            var word = words[i];
            if (word.Quoted || !word.Value.StartsWith('-')) { positionals.Enqueue(word.Value); continue; }
            if (!flags.Add(word.Value)) throw Error(words[0].Value, "параметр указан дважды: " + word.Value);
            string Value()
            {
                if (++i >= words.Count || !words[i].Quoted && words[i].Value.StartsWith('-'))
                    throw Error(words[0].Value, "после " + word.Value + " нужно значение");
                return words[i].Value;
            }
            switch (word.Value.ToUpperInvariant())
            {
                case "-PATTERN" when select: pattern = Value(); break;
                case "-PATH":
                case "-LITERALPATH":
                    if (file != null) throw Error(words[0].Value, "укажите один путь");
                    file = Value(); break;
                case "-SIMPLEMATCH" or "-CASESENSITIVE" when select: break;
                case "-WAIT" when !select: break;
                case "-TAIL" when !select:
                    if (!int.TryParse(Value(), out int count) || count < 0) throw Error("Get-Content", "после -Tail нужно неотрицательное число");
                    tail = count; break;
                default: throw Error(words[0].Value, "неподдерживаемый параметр: " + word.Value);
            }
        }
        if (select && pattern == null && positionals.Count > 0) pattern = positionals.Dequeue();
        if (file == null && positionals.Count > 0) file = positionals.Dequeue();
        if (string.IsNullOrEmpty(file) || select && string.IsNullOrEmpty(pattern) || positionals.Count != 0)
            throw Error(words[0].Value, select ? "укажите выражение и путь к одному файлу" : "укажите путь к одному файлу");
        string path = PowerShellFile(file);
        var lines = Lines(File(path));
        if (!select)
        {
            if (flags.Contains("-wait")) StartFollowingLog(path, "Get-Content");
            return JoinLines(tail is int count ? lines.TakeLast(count) : lines);
        }
        bool ignoreCase = !flags.Contains("-CaseSensitive");
        bool literal = flags.Contains("-SimpleMatch");
        try
        {
            Regex expression = literal ? null : new Regex(pattern,
                RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromMilliseconds(50));
            return JoinLines(lines.Select((line, index) => (line, index)).Where(item => literal
                ? item.line.Contains(pattern, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                : expression.IsMatch(item.line)).Select(item => $"{Name(path)}:{item.index + 1}:{item.line}"));
        }
        catch (RegexMatchTimeoutException) { throw Error("Select-String", "слишком сложное выражение"); }
        catch (ArgumentException) { throw Error("Select-String", "неверное регулярное выражение"); }
    }

    private bool PowerShellMatches(string submitted, string expected)
    {
        var actualWords = PowerShellWords(submitted);
        var targetWords = PowerShellWords(expected);
        if (actualWords.Count == 0 || targetWords.Count == 0 ||
            !actualWords[0].Value.Equals(targetWords[0].Value, StringComparison.OrdinalIgnoreCase)) return false;
        var actual = CopyWorkspace();
        var target = CopyWorkspace();
        var seen = actual.Execute(submitted);
        var wanted = target.Execute(expected);
        return seen.Success && wanted.Success && seen.Output.SequenceEqual(wanted.Output) && actual.SameWorkspace(target);
    }
}
