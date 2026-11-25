using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

// A bounded offline teaching kernel, not a Python runtime. Preserve nbformat
// metadata and non-executed cells when editing/saving a supported notebook.
internal sealed class EditorNotebook
{
    private readonly JsonObject doc;
    private readonly Dictionary<string, int[]> values = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> executions = new();
    private static readonly Regex Bind = new(@"\A([a-z][a-z0-9_]{0,23})\s*=\s*\[\s*((?:-?[0-9]{1,6}\s*,\s*){0,39}-?[0-9]{1,6})\s*\]\z");
    private static readonly Regex Call = new(@"\A(sum|max|min|len)\(\s*([a-z][a-z0-9_]{0,23})\s*\)\z");
    private readonly Stack<(int Index, EditRevision R)> undo = new(), redo = new();
    internal int Selected, Caret, Anchor, Counter;
    internal bool Editing;
    private JsonArray Cells => (JsonArray)doc["cells"];
    internal int Count => Cells.Count;
    private JsonObject Cell(int i) => (JsonObject)Cells[i];
    private static string Str(JsonNode node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static string Lines(JsonNode node) => node is JsonArray a ? string.Concat(a.Select(Str)) : Str(node);
    private static int Number(JsonNode node) => node is JsonValue v && v.TryGetValue<int>(out int n) ? n : 0;
    internal string Source(int i) => Lines(Cell(i)["source"]);
    private string Kind(int i) => Str(Cell(i)["cell_type"]);
    internal string Output(int i)
    {
        if (Cell(i)["outputs"] is not JsonArray arr) return "";
        return string.Join("\n", arr.OfType<JsonObject>().Select(o => Str(o["output_type"]) switch {
            "error" => Str(o["ename"]) + ": " + Str(o["evalue"]), "stream" => Lines(o["text"]),
            _ => o["data"] is JsonObject data ? Lines(data["text/plain"]) : ""
        }));
    }
    internal int Runs(int i) => Number(Cell(i)["execution_count"]);
    internal bool Failed(int i) => Cell(i)["outputs"] is JsonArray arr && arr.OfType<JsonObject>().Any(o => Str(o["output_type"]) == "error");
    internal string Pack() => doc.ToJsonString();
    private EditorNotebook(JsonObject root) { doc = root; }
    internal static EditorNotebook Open(string path, string body)
    {
        if (!path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase) || body.Length > 512_000) return null;
        try
        {
            if (JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) is not JsonObject root ||
                Number(root["nbformat"]) != 4 || root["cells"] is not JsonArray cells || cells.Count is 0 or > 128 ||
                cells.Any(c => c is not JsonObject row || Str(row["cell_type"]) is not ("markdown" or "code") ||
                    row["source"] is not (JsonArray or JsonValue))) return null;
            var n = new EditorNotebook(root);
            // Opening a saved document does NOT restore live kernel variables.
            return n;
        }
        catch (JsonException) { return null; }
    }
    internal EditorNotebook Clone()
    {
        var n = new EditorNotebook((JsonObject)doc.DeepClone()) {
            Selected = Selected, Caret = Caret, Anchor = Anchor, Editing = Editing, Counter = Counter };
        foreach (var pair in values) n.values.Add(pair.Key, (int[])pair.Value.Clone());
        foreach (var pair in executions) n.executions.Add(pair.Key, pair.Value);
        foreach (var r in undo.Reverse()) n.undo.Push(r);
        foreach (var r in redo.Reverse()) n.redo.Push(r);
        return n;
    }
    internal static string Create(string heading, string binding, string formula)
    {
        JsonObject Code(string id, string s) => new() { ["id"] = id, ["cell_type"] = "code", ["metadata"] = new JsonObject(),
            ["source"] = new JsonArray((JsonNode)s), ["execution_count"] = null, ["outputs"] = new JsonArray() };
        return new JsonObject {
            ["nbformat"] = 4, ["nbformat_minor"] = 5,
            ["metadata"] = new JsonObject { ["kernelspec"] = new JsonObject { ["name"] = "python3", ["display_name"] = "Python 3", ["language"] = "python" } },
            ["cells"] = new JsonArray(new JsonObject { ["id"] = "brief", ["cell_type"] = "markdown", ["metadata"] = new JsonObject(),
                ["source"] = new JsonArray((JsonNode)("# " + heading)) }, Code("dataset", binding), Code("metric", formula))
        }.ToJsonString();
    }
    internal bool Move(int direction)
    {
        if (Editing || Selected + direction < 0 || Selected + direction >= Cells.Count) return false;
        Selected += direction; Caret = Anchor = 0; return true;
    }
    internal bool SetMode(bool edit)
    {
        if (Editing == edit) return false;
        Editing = edit;
        Caret = Anchor = Math.Min(Caret, Source(Selected).Length); return true;
    }
    internal void SelectAll() { Anchor = 0; Caret = Source(Selected).Length; }
    internal void Replace(string text) => ReplaceRange(Math.Min(Anchor, Caret), Math.Max(Anchor, Caret), text);

    internal void ReplaceRange(int lo, int hi, string text)
    {
        string src = Source(Selected);
        if (src.Length - hi + lo + text.Length > 4096) return;
        undo.Push((Selected, new EditRevision(src, Caret, Anchor))); redo.Clear();
        Cell(Selected)["source"] = new JsonArray((JsonNode)(src[..lo] + text + src[hi..]));
        Caret = Anchor = lo + text.Length;
    }
    internal bool Undo(bool forward)
    {
        var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (!from.TryPop(out var last)) return false;
        bool same = Selected == last.Index;
        Selected = last.Index;
        int end = Source(Selected).Length;
        to.Push((Selected, new EditRevision(Source(Selected), same ? Math.Min(Caret, end) : end, same ? Math.Min(Anchor, end) : end)));
        Cell(Selected)["source"] = new JsonArray((JsonNode)last.R.Text);
        Caret = last.R.Cursor; Anchor = last.R.Anchor; return true;
    }

    internal bool Run()
    {
        // The Jupyter Keymap binds Ctrl+Enter to executeAndFocusContainer.
        // Keep the cell selected; ordinary arrows must select cells again.
        Editing = false;
        if (Kind(Selected) == "markdown") return true;
        var cell = Cell(Selected); cell["execution_count"] = ++Counter; var outs = new JsonArray(); cell["outputs"] = outs;
        string code = Source(Selected).Trim();
        // All sources including edited input use the same small grammar.
        // There is no eval, shell, network, host file access or external kernel.
        var bind = Bind.Match(code); var call = Call.Match(code);
        if (bind.Success)
            values[bind.Groups[1].Value] = bind.Groups[2].Value.Split(',').Select(s => int.Parse(s.Trim(),CultureInfo.InvariantCulture)).ToArray();
        else if (call.Success && values.TryGetValue(call.Groups[2].Value, out var numbers))
        {
            int n = call.Groups[1].Value switch { "sum" => numbers.Sum(), "max" => numbers.Max(), "min" => numbers.Min(), _ => numbers.Length };
            outs.Add(new JsonObject { ["output_type"] = "execute_result", ["execution_count"] = Counter,
                ["data"] = new JsonObject { ["text/plain"] = new JsonArray((JsonNode)n.ToString(CultureInfo.InvariantCulture)) }, ["metadata"] = new JsonObject() });
        }
        else
        {
            string error = call.Success ? "NameError" : "Учебное ядро";
            string reason = call.Success ? "Сначала выполните ячейку с данными: " + call.Groups[2].Value :
                "Поддержаны список чисел и sum, max, min, len. Другой код не выполняется.";
            outs.Add(new JsonObject { ["output_type"] = "error", ["ename"] = error, ["evalue"] = reason,
                ["traceback"] = new JsonArray((JsonNode)(error + ": " + reason)) });
        }
        executions[Selected] = InputSignature(Selected);
        return true;
    }
    // A preceding execution number is not evidence that the current source
    // and variable values were used (especially when both old/new tasks ran).
    private string InputSignature(int i)
    {
        string src = Source(i).Trim(); var lhs = Bind.Match(src); var rhs = Call.Match(src);
        string name = lhs.Success ? lhs.Groups[1].Value : rhs.Groups[2].Value;
        return src + ":" + (values.TryGetValue(name, out var data) ? string.Join(",", data) : "unset");
    }
    internal bool Fresh(int i) => executions.TryGetValue(i, out var s) && s == InputSignature(i);
    private bool Stale(int i) => executions.ContainsKey(i) && !Fresh(i);
    internal bool RunAndAdvance()
    {
        if (Selected == Cells.Count - 1 && Cells.Count >= 128) return false;
        if (!Run()) return false;
        bool append = Selected == Cells.Count - 1;
        if (append)
        {
            // Unique and deterministic under draft replay; do not overwrite a real cell ID.
            string id = "practice-" + Cells.Count;
            while (Cells.OfType<JsonObject>().Any(c => Str(c["id"]) == id)) id += "x";
            string kind = Kind(Selected);
            var newCell = new JsonObject { ["id"] = id, ["cell_type"] = kind, ["metadata"] = new JsonObject(),
                ["source"] = new JsonArray() };
            if (kind == "code") { newCell["execution_count"] = null; newCell["outputs"] = new JsonArray(); }
            Cells.Add(newCell);
        }
        if (!Move(1)) return false;
        // Selecting an existing cell focuses its container. A newly inserted
        // cell receives editor focus instead, as in VS Code's executeActions.
        if (append) SetMode(true);
        return true;
    }

    internal object Snapshot() => new { simulated = true, selected = Selected, editing = Editing, caret = Caret, anchor = Anchor,
        cells = Enumerable.Range(0, Cells.Count).Select(i => new { kind = Kind(i), source = Source(i),
            runs = Runs(i), stale = Stale(i), output = Output(i), failed = Failed(i), syntax = Kind(i) == "code" ? PythonHighlight.For(Source(i), "cell.py") : [] }).ToArray() };
    internal string Describe() => "Блокнот · ячейка " + (Selected + 1) + " из " + Cells.Count +
        (Editing ? " · редактирование" : " · выбор ячейки") + ". " + Source(Selected) +
        (Runs(Selected) > 0 ? ". Вывод: " + (Output(Selected) == "" ? "без вывода" : Output(Selected)) + (Stale(Selected) ? " (предыдущий расчёт)" : "") : "");
}
