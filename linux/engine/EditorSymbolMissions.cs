using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal sealed record SymbolLocation(string Path, int Offset, int Length, string Kind);
internal sealed record EditorJump(string Path, int Anchor, int Caret, int NotebookCell = -1, bool NotebookEditing = false, string NotebookIdentity = null);
internal sealed record SymbolBinding(string Path, string Name, SymbolLocation Definition, SymbolLocation[] Occurrences);

internal sealed partial class LabScene
{
    private const string SymbolProject = Project + "/symbol-lab";
    private const string SymbolMain = SymbolProject + "/src/main.py";
    private const string SymbolModule = SymbolProject + "/src/signal_ops.py";
    private const string SymbolDecoy = SymbolProject + "/src/helpers.py";
    private bool symbolWorkspace;
    internal const string RenameTarget = "scale_sample";

    internal void PrepareSymbolCase(string missionId)
    {
        if (missionId is not ("code-go-definition" or "code-rename-symbol") || Terminal.Exists(SymbolMain)) return;
        symbolWorkspace = true;
        Terminal.CreateFolder(SymbolProject + "/src");
        Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0;
        const string main = "from signal_ops import normalize\n\nfirst = normalize(12)\nsecond = normalize(24)\nlabel = \"normalize\"\n\ndef local_demo():\n    normalize = 7\n    return normalize\n";
        const string signal = "def normalize(value):\n    return value / 12\n\ndef fallback():\n    return normalize(0)\n";
        const string decoy = "def normalize(value):\n    return value + 100\n";
        foreach (var file in new[] { (SymbolMain, main), (SymbolModule, signal), (SymbolDecoy, decoy) })
        {
            Terminal.StoreFile(file.Item1, file.Item2);
            originalFiles[file.Item1] = file.Item2;
        }
        OpenDocument(SymbolMain);
        Active.Cursor = main.IndexOf("normalize(12)", StringComparison.Ordinal) + 2;
    }

    // This is a small simulation of flat Python modules using an unaliased
    // "from module import name[, name]". It is not a general Python language server:
    // dotted access, aliases, dynamic lookup and closures are not resolved.
    private static readonly Regex PythonLexemes = new(
        """(?s)(?<omit>\#[^\r\n]*|[rRuUbBfF]{0,2}(""" + "\"\"\"" + """(?:\\.|(?!""" + "\"\"\"" + """).)*""" + "\"\"\"" + """|'''(?:\\.|(?!''').)*'''|"(?:\\.|[^"\\\r\n])*"|'(?:\\.|[^'\\\r\n])*'))|(?<name>[A-Za-z_]\w*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Definitions = new(@"(?m)^def\s+(?<name>[A-Za-z_]\w*)\((?<params>[^\r\n()]*)\)\s*:", RegexOptions.Compiled);
    private static readonly Regex Imports = new(@"(?m)^from[ \t]+(?<module>[A-Za-z_]\w*)[ \t]+import[ \t]+(?<name>[A-Za-z_]\w*)(?:[ \t]*,[ \t]*(?<name>[A-Za-z_]\w*))*[ \t]*(?=\r?$)", RegexOptions.Compiled);
    private static readonly Regex TopStatements = new(@"(?m)^[^\s]", RegexOptions.Compiled);
    private static readonly HashSet<string> PythonKeywords =
        new("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield".Split(' '));

    private static string MaskPython(string source) => PythonLexemes.Replace(source, match => match.Groups["omit"].Success
        ? new string(match.Value.Select(c => c is '\n' or '\r' ? c : ' ').ToArray()) : match.Value);

    private Dictionary<string, string> SymbolDocuments(LabWindow w) =>
        ProjectDocuments().Where(p => p.EndsWith(".py", StringComparison.Ordinal)).ToDictionary(p => p,
            p => w.Tabs.FirstOrDefault(t => EditorPath(t) == p)?.Body ?? Terminal.ReadFile(p) ?? "");

    private static IEnumerable<Group> NamedDefinitions(string code, string name) =>
        Definitions.Matches(code).Select(m => m.Groups["name"]).Where(g => g.Value == name);

    private static IEnumerable<(string Module, Capture Name)> NamedImports(string code, string name) =>
        Imports.Matches(code).SelectMany(m => m.Groups["name"].Captures.Where(c => c.Value == name)
            .Select(c => (m.Groups["module"].Value, c)));

    private SymbolBinding ResolveSymbol(LabWindow w)
    {
        if (w.Tab == null) return null;
        var documents = SymbolDocuments(w);
        string current = EditorPath(w.Tab);
        if (!documents.TryGetValue(current, out var body)) return null;
        var masked = documents.ToDictionary(kv => kv.Key, kv => MaskPython(kv.Value));
        Match token = PythonLexemes.Matches(masked[current]).Cast<Match>()
            .FirstOrDefault(m => m.Groups["name"].Success && m.Index <= w.Cursor && w.Cursor < m.Index + m.Length);
        if (token == null) return null;
        string name = token.Value;
        var definitions = NamedDefinitions(masked[current], name).ToArray();
        string definingPath;
        if (definitions.Length == 1) definingPath = current;
        else
        {
            var imports = NamedImports(masked[current], name).ToArray();
            if (definitions.Length != 0 || imports.Length != 1) return null;
            string module = imports[0].Module;
            string folder = current[..(current.LastIndexOf('/') + 1)];
            definingPath = folder + module + ".py";
            if (!masked.ContainsKey(definingPath)) return null;
            definitions = NamedDefinitions(masked[definingPath], name).ToArray();
            if (definitions.Length != 1) return null;
        }
        var def = new SymbolLocation(definingPath, definitions[0].Index, name.Length, "definition");
        var spans = new List<SymbolLocation> { def };
        foreach (var (path, source) in masked.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (path != definingPath)
            {
                var imports = NamedImports(source, name).ToArray();
                if (imports.Length != 1 ||
                    path[..(path.LastIndexOf('/') + 1)] + imports[0].Module + ".py" != definingPath ||
                    NamedDefinitions(source, name).Any()) continue;
                spans.Add(new(path, imports[0].Name.Index, name.Length, "import"));
            }
            var scopes = Definitions.Matches(source).Select(m =>
            {
                int end = TopStatements.Match(source, m.Index + m.Length) is { Success: true } next ? next.Index : source.Length;
                string escaped = Regex.Escape(name);
                // A reference call beginning an indented line is not itself a binding.
                bool shadowed = Regex.IsMatch(m.Groups["params"].Value, @"(?:^|,)\s*" + escaped + @"\s*(?:$|[:,=])")
                    || Regex.IsMatch(source[m.Index..end], @"(?m)^[ \t]+(?:" + escaped + @"[ \t]*(?:=\s*(?!=)|:[^\r\n=]+=)|def[ \t]+" + escaped + @"\s*\(|for[ \t]+" + escaped + @"[ \t]+in\b)");
                return (m.Index, End: end, Shadowed: shadowed);
            }).ToArray();
            // Bare calls only; do not count .normalize(), definitions or strings.
            foreach (Match call in Regex.Matches(source, @"(?<![.\w])" + Regex.Escape(name) + @"(?=[ \t]*\()"))
            {
                int line = source.LastIndexOf('\n', Math.Max(0, call.Index - 1)) + 1;
                string prefix = source[line..call.Index];
                if (Regex.IsMatch(prefix, @"\bdef[ \t]+$") || prefix.TrimEnd().EndsWith(".") ||
                    scopes.Any(s => s.Shadowed && call.Index >= s.Index && call.Index < s.End)) continue;
                spans.Add(new(path, call.Index, name.Length, "call"));
            }
        }
        var ordered = spans.OrderBy(s => s.Path, StringComparer.Ordinal).ThenBy(s => s.Offset).ToArray();
        return ordered.Any(s => s.Path == current && s.Offset == token.Index) ? new(definingPath, name, def, ordered) : null;
    }

    private LabEntry[] ReferenceEntries(LabWindow w)
    {
        var docs = SymbolDocuments(w);
        return w.SymbolReferences.Where(s => docs.TryGetValue(s.Path, out var content) && s.Offset < content.Length).Select(s =>
        {
            string content = docs[s.Path];
            int start = LineStart(content, s.Offset);
            int line = content[..start].Count(c => c == '\n') + 1;
            int column = s.Offset - start + 1;
            return new LabEntry($"{NameOf(s.Path)}:{line}:{column} · {content[start..LineEnd(content, s.Offset)].Trim()}", s.Path + ":" + s.Offset, s.Kind);
        }).ToArray();
    }

    private bool? ApplySymbols(string action, LabWindow w)
    {
        if (w?.App != "code") return null;
        switch (action)
        {
            case "code-definition":
                if (w.Tab?.Notebook != null) return NotebookDefinition(w);
                var symbol = ResolveSymbol(w);
                if (symbol == null) return false;
                w.SymbolBack.Push(new(EditorPath(w.Tab), w.Anchor, w.Cursor));
                GoToSymbol(symbol.Definition);
                return true;
            case "code-back":
                if (!w.SymbolBack.TryPop(out var origin)) return false;
                OpenDocument(origin.Path);
                if (origin.NotebookCell >= 0)
                {
                    if (w.Tab?.Notebook is not EditorNotebook notebook) return false;
                    int index = origin.NotebookIdentity == null ? origin.NotebookCell : notebook.CellIndex(origin.NotebookIdentity);
                    if (index < 0 || index >= notebook.Count)
                    {
                        w.Status = "Исходная ячейка удалена. Открыт сам блокнот.";
                        return false;
                    }
                    notebook.Select(index); notebook.SetMode(origin.NotebookEditing);
                    string source = notebook.Source(notebook.Selected);
                    notebook.Anchor = InsertionBoundary(source, Math.Clamp(origin.Anchor, 0, source.Length));
                    notebook.Caret = InsertionBoundary(source, Math.Clamp(origin.Caret, 0, source.Length));
                }
                else w.Select(origin.Anchor, origin.Caret);
                return true;
            case "code-references":
                var referenced = ResolveSymbol(w);
                w.SymbolReferences = referenced?.Occurrences.Where(s => s.Kind == "call").ToArray() ?? [];
                if (w.SymbolReferences.Length == 0) return false;
                w.Focus = "symbol-references"; w.Input = referenced.Name; w.Cursor = 0; w.OptionIndex = 0;
                return true;
            case "code-reference-next":
            case "code-reference-prev":
                if (w.Focus != "symbol-references" || w.SymbolReferences.Length == 0) return false;
                w.OptionIndex = (w.OptionIndex + (action == "code-reference-next" ? 1 : w.SymbolReferences.Length - 1)) % w.SymbolReferences.Length;
                return true;
            case "code-reference-open":
                if (w.Focus != "symbol-references" || w.OptionIndex >= w.SymbolReferences.Length) return false;
                var reference = w.SymbolReferences[w.OptionIndex];
                w.SymbolReferences = [];
                GoToSymbol(reference);
                return true;
            case "code-rename":
                var renamed = ResolveSymbol(w);
                if (renamed == null) return false;
                w.PendingSymbol = renamed;
                w.Focus = "symbol-rename"; w.Input = renamed.Name; w.Select(0, renamed.Name.Length);
                return true;
            case "code-rename-cancel":
                if (w.Focus != "symbol-rename") return false;
                w.PendingSymbol = null; w.Input = ""; w.Focus = "body"; return true;
            case "code-rename-confirm":
                return RenameSymbol(w);
            default:
                return null;
        }
    }

    private bool NotebookDefinition(LabWindow w)
    {
        var n = w.Tab.Notebook;
        if (!n.Editing || !n.IsCode(n.Selected)) return false;
        string source = MaskPython(n.Source(n.Selected));
        var token = PythonLexemes.Matches(source).Cast<Match>()
            .FirstOrDefault(m => m.Groups["name"].Success && m.Index <= n.Caret && n.Caret < m.Index + m.Length);
        if (token == null) return false;
        string name = token.Value;
        var imports = Enumerable.Range(0, n.Selected + 1).Where(n.IsCode)
            .SelectMany(i => NamedImports(MaskPython(n.Source(i)), name)).Select(item => item.Module).Distinct().ToArray();
        if (imports.Length != 1) return false;
        string path = EditorProjectRoot + "/" + imports[0] + ".py";
        string body = w.Tabs.FirstOrDefault(t => EditorPath(t) == path)?.Body ?? Terminal.ReadFile(path);
        if (body == null) return false;
        var def = NamedDefinitions(MaskPython(body), name).ToArray();
        if (def.Length != 1) return false;
        w.SymbolBack.Push(new(EditorPath(w.Tab), n.Anchor, n.Caret, n.Selected, n.Editing, n.CellIdentity(n.Selected)));
        GoToSymbol(new(path, def[0].Index, name.Length, "definition"));
        return true;
    }

    private void GoToSymbol(SymbolLocation location)
    {
        OpenDocument(location.Path);
        Active.Cursor = location.Offset;
    }

    private bool RenameSymbol(LabWindow w)
    {
        if (w.Focus != "symbol-rename") return false;
        string name = w.Input;
        var symbol = w.PendingSymbol;
        if (symbol == null || name == symbol.Name || PythonKeywords.Contains(name) || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z_0-9]*\z")) return false;
        var documents = SymbolDocuments(w);
        // Restrict the simulated workspace edit to names not already in use:
        // Python binding conflicts should not silently change the exercise.
        var grouped = symbol.Occurrences.GroupBy(s => s.Path).ToArray();
        if (grouped.Any(group => Regex.IsMatch(MaskPython(documents[group.Key]), @"\b" + Regex.Escape(name) + @"\b"))) return false;
        string origin = EditorPath(w.Tab);
        int originalCaret = w.Tab.Position.Caret, caret = originalCaret;
        foreach (var group in grouped)
        {
            string path = group.Key;
            OpenDocument(path);
            Remember(w);
            string body = documents[path];
            foreach (var span in group.OrderByDescending(s => s.Offset))
            {
                body = body[..span.Offset] + name + body[(span.Offset + span.Length)..];
                if (path == origin && span.Offset < originalCaret)
                    caret += name.Length - symbol.Name.Length;
            }
            w.Body = body;
        }
        OpenDocument(origin);
        w.Cursor = Math.Clamp(caret, 0, w.Body.Length);
        w.PendingSymbol = null;
        return true;
    }

    internal object SymbolEvidence(string action)
    {
        var w = Active;
        return action switch
        {
            "code-definition" or "code-back" or "code-reference-open" => new { w?.Url, w?.Focus, w?.Anchor, w?.Cursor },
            "code-references" or "code-reference-next" or "code-reference-prev" => new { w?.Focus, w?.OptionIndex, references = w?.SymbolReferences },
            "code-rename" => new { w?.Url, w?.Focus, w?.Input, w?.Anchor, w?.Cursor },
            "code-rename-cancel" or "code-rename-confirm" => new { w?.Focus, w?.Url,
                files = w == null ? null : SymbolDocuments(w).Where(p => p.Key is SymbolMain or SymbolModule or SymbolDecoy).OrderBy(p => p.Key).ToArray() },
            _ => null
        };
    }
}
