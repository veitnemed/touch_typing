using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TouchTypingStudio;

// A bounded offline teaching kernel, not a Python runtime. Preserve nbformat
// metadata and non-executed cells when editing/saving a supported notebook.
internal sealed partial class EditorNotebook
{
    private readonly JsonObject doc;
    private NotebookPythonKernel kernel = new();
    private readonly HashSet<string> staleFiles = new(StringComparer.Ordinal);
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
    internal bool IsCode(int i) => Kind(i) == "code";
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
    private EditorNotebook(JsonObject root) { doc = root; EnsureIds(); }
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
            Selected = Selected, Caret = Caret, Anchor = Anchor, Editing = Editing, Counter = Counter,
            nextId = nextId, generation = generation, interrupted = interrupted, DeleteArmed = DeleteArmed };
        n.kernel = kernel.Clone(); n.staleFiles.UnionWith(staleFiles);
        foreach (var pair in executions) n.executions.Add(pair.Key, pair.Value);
        n.undo.AddRange(undo);
        n.redo.AddRange(redo);
        foreach (int cell in queue) n.queue.Enqueue(cell);
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
        return !Editing && Select(Selected + direction);
    }
    internal bool SetMode(bool edit)
    {
        if (Editing == edit || edit && Busy) return false;
        Editing = edit; DeleteArmed = false;
        Caret = Anchor = Math.Min(Caret, Source(Selected).Length); return true;
    }
    internal void SelectAll() { Anchor = 0; Caret = Source(Selected).Length; }
    internal void Replace(string text) => ReplaceRange(Math.Min(Anchor, Caret), Math.Max(Anchor, Caret), text);

    internal void ReplaceRange(int lo, int hi, string text)
    {
        string src = Source(Selected);
        if (Busy || src.Length - hi + lo + text.Length > 4096) return;
        Remember();
        Cell(Selected)["source"] = new JsonArray((JsonNode)(src[..lo] + text + src[hi..]));
        Caret = Anchor = lo + text.Length;
    }
    internal bool Run(Func<string, string> read = null)
    {
        if (Busy) return false;
        // Ctrl+Enter runs the cell and focuses its container, not the next cell.
        Editing = false; DeleteArmed = false; interrupted = false;
        Execute(Selected, read);
        return true;
    }

    private void Execute(int index, Func<string, string> read)
    {
        if (Kind(index) == "markdown") return;
        var cell = Cell(index); cell["execution_count"] = ++Counter;
        var outs = new JsonArray(); cell["outputs"] = outs;
        var run = kernel.Run(Source(index), read);
        if (run.Stream.Length > 0)
            outs.Add(new JsonObject { ["output_type"] = "stream", ["name"] = "stdout",
                ["text"] = new JsonArray((JsonNode)run.Stream.TrimEnd('\n')) });
        if (run.Error is NotebookPythonException e)
        {
            var trace = new JsonArray((JsonNode)("Cell In[" + Counter + "]"));
            foreach (string frame in e.Trace.AsEnumerable().Reverse()) trace.Add(frame);
            trace.Add(e.Name + ": " + e.Message);
            outs.Add(new JsonObject { ["output_type"] = "error", ["ename"] = e.Name, ["evalue"] = e.Message, ["traceback"] = trace });
        }
        else if (run.Result is string result)
            outs.Add(new JsonObject { ["output_type"] = "execute_result", ["execution_count"] = Counter,
                ["data"] = new JsonObject { ["text/plain"] = new JsonArray((JsonNode)result) }, ["metadata"] = new JsonObject() });
        executions[Id(index)] = run; staleFiles.Remove(Id(index));
    }
    internal void SynchronizeWorkspace(Func<string, string> read)
    {
        staleFiles.Clear();
        for (int i = 0; i < Count; i++)
            if (executions.TryGetValue(Id(i), out var run) && !kernel.Fresh(run, Source(i), read))
                staleFiles.Add(Id(i));
    }
    internal bool Fresh(int i) => executions.TryGetValue(Id(i), out var run) &&
        !staleFiles.Contains(Id(i)) && kernel.Fresh(run, Source(i), null);
    private bool Stale(int i) => executions.ContainsKey(Id(i)) && !Fresh(i);
    // Display metadata only. A saved nbformat output is useful evidence of a *past*
    // kernel, but it must not read like a result reproduced from current variables.
    private string OutputLabel(int i) => !executions.ContainsKey(Id(i)) && (Runs(i) > 0 || Output(i) != "") ? "Сохранённый вывод" :
        Stale(i) ? "Предыдущий вывод" : "Вывод";

    internal bool RunAndAdvance(Func<string, string> read = null)
    {
        if (Selected == Cells.Count - 1 && Cells.Count >= 128) return false;
        if (!Run(read)) return false;
        return Selected == Cells.Count - 1 ? InsertCell(false, Kind(Selected)) : Move(1);
    }

    internal object Snapshot(bool canPasteCell = false) => new { simulated = true, selected = Selected, editing = Editing, caret = Caret, anchor = Anchor,
        kernel = KernelSnapshot(), canUndo = CanUndo, canRedo = CanRedo, canPasteCell,
        cells = Enumerable.Range(0, Cells.Count).Select(i => new { id = Id(i), kind = Kind(i), source = Source(i),
            runs = Runs(i), stale = Stale(i), outputLabel = OutputLabel(i), output = Output(i), failed = Failed(i),
            traceback = Traceback(i), syntax = Kind(i) == "code" ? PythonHighlight.For(Source(i), "cell.py") : [] }).ToArray() };
    internal string Describe() => "Блокнот · ячейка " + (Selected + 1) + " из " + Cells.Count +
        (Editing ? " · редактирование" : " · выбор ячейки") + ". " + Source(Selected) +
        (Output(Selected) != "" ? ". " + OutputLabel(Selected) + ": " + Output(Selected) :
            Runs(Selected) > 0 ? ". " + OutputLabel(Selected) + ": без вывода" : "");
}
