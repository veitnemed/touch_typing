using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace TouchTypingStudio;

internal sealed partial class EditorNotebook
{
    private const int MaxCells = 128, MaxHistory = 96, MaxStructureHistory = 12;
    private sealed record Revision(string Id, int Index, int Caret, int Anchor, bool Editing,
        string Source = null, JsonArray Structure = null);
    private readonly List<Revision> undo = [], redo = [];
    private readonly Queue<int> queue = new();
    private readonly Dictionary<string, NotebookPythonKernel.Execution> executions = new(StringComparer.Ordinal);
    private int nextId, generation = 1;
    private bool interrupted;
    internal bool DeleteArmed;
    internal int Generation => generation;
    internal bool Busy => queue.Count != 0;
    internal bool CanUndo => undo.Count != 0;
    internal bool CanRedo => redo.Count != 0;

    private string Id(int i) => Str(Cell(i)["id"]);
    private string NewId()
    {
        string id;
        do { id = "practice-" + ++nextId; }
        while (Cells.OfType<JsonObject>().Any(c => Str(c["id"]) == id));
        return id;
    }

    private void EnsureIds()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject cell in Cells)
            if (!used.Add(Str(cell["id"])) || Str(cell["id"]).Length == 0)
            {
                var id = NewId();
                cell["id"] = id;
                used.Add(id);
            }
    }

    private static JsonObject NewCell(string id, string kind, string source = "")
    {
        var cell = new JsonObject { ["id"] = id, ["cell_type"] = kind, ["metadata"] = new JsonObject(),
            ["source"] = new JsonArray((JsonNode)source) };
        if (kind == "code") { cell["execution_count"] = null; cell["outputs"] = new JsonArray(); }
        return cell;
    }

    internal static string Empty() => new JsonObject {
        ["nbformat"] = 4, ["nbformat_minor"] = 5,
        ["metadata"] = new JsonObject { ["kernelspec"] = new JsonObject {
            ["name"] = "python3", ["display_name"] = "Python 3", ["language"] = "python" } },
        ["cells"] = new JsonArray(NewCell("practice-1", "code"))
    }.ToJsonString();

    private void Push(List<Revision> stack, Revision revision)
    {
        stack.Add(revision);
        if (stack.Count > MaxHistory) stack.RemoveAt(0);
        if (stack.Count(item => item.Structure != null) > MaxStructureHistory)
            // Older text edits can't be replayed across a missing structural edit.
            stack.RemoveRange(0, stack.FindIndex(item => item.Structure != null) + 1);
    }

    private Revision Capture(bool structure) => new(Id(Selected), Selected, Caret, Anchor, Editing,
        structure ? null : Source(Selected), structure ? (JsonArray)Cells.DeepClone() : null);
    private void Remember(Revision before)
    {
        Push(undo, before); redo.Clear();
    }
    private void Remember(bool structure = false) => Remember(Capture(structure));

    private sealed record CellClip(string Type, string Source);
    internal string CopyCell() => System.Text.Json.JsonSerializer.Serialize(new CellClip(Kind(Selected), Source(Selected)));
    internal bool PasteCell(string clip)
    {
        if (string.IsNullOrEmpty(clip) || clip.Length > 40_000) return false;
        try
        {
            var item = System.Text.Json.JsonSerializer.Deserialize<CellClip>(clip);
            return item?.Type is "code" or "markdown" && item.Source != null && item.Source.Length <= 4096 &&
                InsertCell(false, item.Type, item.Source);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    internal bool Select(int index)
    {
        if (index < 0 || index >= Cells.Count || index == Selected && !Editing) return false;
        Selected = index; Caret = Anchor = 0; Editing = false; DeleteArmed = false;
        return true;
    }

    internal bool InsertCell(bool above, string kind = "code", string source = "", bool edit = true)
    {
        if (Busy || Cells.Count >= MaxCells) return false;
        if (source.Length > 4096) return false;
        var before = Capture(true);
        int pos = above ? Selected : Selected + 1, previousId = nextId;
        Cells.Insert(pos, NewCell(NewId(), kind, source));
        if (!FitsDocument())
        {
            Cells.RemoveAt(pos); nextId = previousId; packed = null;
            return false;
        }
        Remember(before); ClearProblem();
        Selected = pos; Caret = Anchor = 0; Editing = edit; DeleteArmed = false;
        return true;
    }

    internal bool ShiftCell(int delta)
    {
        int dest = Selected + delta;
        if (Busy || dest < 0 || dest >= Count) return false;
        Remember(structure: true);
        var cell = Cells[Selected];
        Cells.RemoveAt(Selected);
        Cells.Insert(dest, cell); packed = null;
        Selected = dest; Editing = false; DeleteArmed = false;
        return true;
    }

    internal bool EraseCell()
    {
        if (Busy) return false;
        var before = Capture(true);
        int previousId = nextId;
        Cells.RemoveAt(Selected);
        if (Count == 0) Cells.Add(NewCell(NewId(), "code"));
        if (!FitsDocument())
        {
            doc["cells"] = before.Structure.DeepClone(); nextId = previousId; packed = null;
            return false;
        }
        Remember(before); ClearProblem();
        Selected = Math.Min(Selected, Count - 1);
        Caret = Anchor = 0; Editing = false; DeleteArmed = false;
        return true;
    }

    internal bool SetKind(string kind)
    {
        if (Busy || Kind(Selected) == kind || kind is not ("code" or "markdown")) return false;
        var before = Capture(true);
        var cell = Cell(Selected);
        cell["cell_type"] = kind;
        if (kind == "code")
        { cell["outputs"] = new JsonArray(); cell["execution_count"] = null; }
        else
        { cell.Remove("outputs"); cell.Remove("execution_count"); }
        if (!FitsDocument()) { doc["cells"] = before.Structure.DeepClone(); packed = null; return false; }
        Remember(before); ClearProblem();
        executions.Remove(Id(Selected));
        DeleteArmed = false;
        return true;
    }

    internal bool Undo(bool forward)
    {
        if (Busy) return false;
        var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (from.Count == 0) return false;
        var last = from[^1]; Revision inverse;
        if (last.Structure != null)
        {
            // The snapshot was accepted under the same document limit.
            inverse = Capture(true); doc["cells"] = last.Structure.DeepClone(); packed = null;
        }
        else
        {
            int index = CellIndex(last.Id);
            if (index < 0) return false;
            int length = Source(index).Length;
            inverse = new Revision(last.Id, index, Selected == index ? Caret : length, Selected == index ? Anchor : length,
                Editing, Source(index));
            var cell = Cell(index); var previous = cell["source"];
            cell["source"] = new JsonArray((JsonNode)last.Source);
            if (!FitsDocument()) { cell["source"] = previous; packed = null; return false; }
        }
        from.RemoveAt(from.Count - 1); Push(to, inverse); ClearProblem();
        Selected = last.Index;
        Caret = last.Caret; Anchor = last.Anchor; Editing = last.Editing; DeleteArmed = false;
        return true;
    }

    internal bool ClearOutput()
    {
        if (Busy || !Enumerable.Range(0, Count).Any(i => Runs(i) > 0 || Output(i).Length > 0)) return false;
        for (int i = 0; i < Count; i++)
        {
            if (Kind(i) != "code") continue;
            Cell(i)["execution_count"] = null; Cell(i)["outputs"] = new JsonArray();
        }
        executions.Clear(); packed = null; ClearProblem();
        return true;
    }

    internal bool Restart()
    {
        ClearProblem(); queue.Clear(); kernel.Reset(); executions.Clear(); staleFiles.Clear(); Counter = 0; generation++; interrupted = false;
        Editing = false; DeleteArmed = false;
        return true;
    }

    internal bool Interrupt()
    {
        if (!Busy) return false;
        queue.Clear(); interrupted = true;
        return true;
    }

    internal bool RunBatch(string action, bool defer, Func<string, string> read)
    {
        if (Busy) return false;
        int start = action == "run-below" ? Selected : 0;
        int end = action == "run-above" ? Selected : Count;
        foreach (int i in Enumerable.Range(start, end - start).Where(i => Kind(i) == "code")) queue.Enqueue(i);
        if (!Busy) return false;
        Editing = false; DeleteArmed = false; interrupted = false;
        if (!defer) while (Tick(read)) { }
        return true;
    }

    internal bool Tick(Func<string, string> read)
    {
        if (!queue.TryDequeue(out int cell)) return false;
        if (!Execute(cell, read) || Failed(cell)) queue.Clear();
        return true;
    }

    private string KernelState => Busy ? "busy" : interrupted ? "interrupted" : "idle";
    private object KernelSnapshot() => new { label = "Python 3 · учебное ядро", state = KernelState,
        generation, counter = Counter, queued = queue.Count };
    private string Traceback(int i) => Cell(i)["outputs"] is JsonArray outs ? string.Join("\n",
        outs.OfType<JsonObject>().Where(o => Str(o["output_type"]) == "error").Select(o =>
            o["traceback"] is JsonArray trace ? string.Join("\n", trace.Select(Str)) : "")) : "";
}
