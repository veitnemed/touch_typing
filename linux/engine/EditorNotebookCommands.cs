using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal bool ReplayLegacyNotebookActions;
    private string notebookClipboard = "";
    private bool CanPasteNotebookCell => notebookClipboard.Length > 0;
    private static readonly (string Action, string Label)[] NotebookCommands = [
        ("new-notebook", "Create: New Jupyter Notebook"),
        ("new-folder", "Explorer: New Folder"),
        ("run-all", "Notebook: Run All Cells"),
        ("run-above", "Notebook: Run All Above"),
        ("run-below", "Notebook: Run All Below"),
        ("restart", "Notebook: Restart Kernel"),
        ("interrupt", "Notebook: Interrupt Kernel"),
        ("clear", "Notebook: Clear All Outputs"),
    ];

    private void SetNotebookClipboard(EditorNotebook n)
    {
        Clipboard = n.Source(n.Selected);
        notebookClipboard = n.CopyCell();
    }

    internal void ResetNotebookChord()
    {
        foreach (var tab in Windows.SelectMany(w => w.Tabs))
            if (tab.Notebook != null) tab.Notebook.DeleteArmed = false;
    }

    // A teaching notebook can only read the current mission's virtual project.
    // The callback is never retained across a clone, draft, or kernel restart.
    private string NotebookRead(string relative) => Terminal.ReadFile(EditorProjectRoot + "/" + relative);

    private bool NotebookCommand(LabWindow w, string action, bool defer = false)
    {
        var n = w.Tab?.Notebook;
        if (n == null) return false;
        bool changed = action switch
        {
            "edit" => n.SetMode(true),
            "leave" => n.SetMode(false),
            "run" => n.Run(NotebookRead),
            "run-next" => n.RunAndAdvance(NotebookRead),
            "run-all" or "run-above" or "run-below" => n.RunBatch(action, defer, NotebookRead),
            "restart" => n.Restart(),
            "interrupt" => n.Interrupt(),
            "clear" => n.ClearOutput(),
            "add-above" => n.InsertCell(true, edit: false),
            "add-below" => n.InsertCell(false, edit: false),
            "add-markdown" => n.InsertCell(false, "markdown"),
            "move-up" => n.ShiftCell(-1),
            "move-down" => n.ShiftCell(1),
            "copy-cell" => CopyNotebookCell(n),
            "cut-cell" when !n.Busy => CopyNotebookCell(n) && n.EraseCell(),
            "paste-cell" => CanPasteNotebookCell && n.PasteCell(notebookClipboard),
            "duplicate-cell" => n.PasteCell(n.CopyCell()),
            "delete-cell" => n.EraseCell(),
            "undo" => n.Undo(false),
            "redo" => n.Undo(true),
            "code" or "markdown" => n.SetKind(action),
            _ => false
        };
        if (changed)
        {
            n.SynchronizeWorkspace(NotebookRead);
            if (action is not ("edit" or "leave" or "copy-cell" or "interrupt" or "restart"))
                w.Body = n.Pack();
            w.Status = action == "interrupt" ? "Выполнение прервано. Уже выполненные ячейки не изменены." : n.Describe();
            Version++;
        }
        return changed;
    }

    private bool CopyNotebookCell(EditorNotebook n) { SetNotebookClipboard(n); return true; }

    internal bool NotebookPointer(string action, int cell, int offset = -1)
    {
        var w = Active;
        if (w?.App != "code" || w.Tab?.Notebook == null || w.Focus is not ("body" or "explorer") ||
            Switcher || Overview || Screenshot || DesktopOverlay.Length > 0) return false;
        if (action is not ("select" or "edit" or "run" or "run-next" or "run-all" or "run-above" or "run-below" or
            "restart" or "interrupt" or "clear" or "add-above" or "add-below" or "add-markdown" or "move-up" or "move-down" or
            "copy-cell" or "paste-cell" or "duplicate-cell" or "delete-cell" or "undo" or "redo" or "code" or "markdown")) return false;
        var n = w.Tab.Notebook;
        if (cell < -1 || cell >= n.Count) return false;
        bool focused = w.Focus != "body"; w.Focus = "body";
        bool selected = cell >= 0 && n.Select(cell);
        if (selected || focused) { w.Status = n.Describe(); Version++; }
        bool done = action != "select" && NotebookCommand(w, action, defer: true);
        if (action == "edit" && n.Editing && offset >= 0)
        {
            string source = n.Source(n.Selected);
            n.Caret = n.Anchor = InsertionBoundary(source, Math.Min(offset, source.Length));
            Version++; done = true;
        }
        return done || selected || focused;
    }

    internal bool TickNotebook()
    {
        foreach (var w in Windows.Where(w => w.App == "code"))
            foreach (var tab in w.Tabs)
                if (tab.Notebook is EditorNotebook n && n.Tick(NotebookRead))
                {
                    n.SynchronizeWorkspace(NotebookRead); tab.Body = n.Pack();
                    if (w.Tab == tab) w.Status = n.Describe();
                    Version++;
                    return true;
                }
        return false;
    }

    private LabEntry[] NotebookPalette(LabWindow w)
    {
        if (w?.App != "code" || w.Focus != "palette") return [];
        string query = w.Input.Trim().TrimStart('>').Trim();
        return NotebookCommands.Where(c => c.Action is "new-notebook" or "new-folder" || w.Tab?.Notebook != null)
            .Where(c => c.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(c => new LabEntry(c.Label, c.Action, "notebook-command")).ToArray();
    }

    private bool? ApplyNotebookPalette(string action, LabWindow w)
    {
        if (ReplayLegacyNotebookActions || w?.App != "code" || w.Focus != "palette" || action is not ("palette-next" or "palette-choose")) return null;
        var available = NotebookPalette(w);
        // Preserve older editor/clone palette behavior unless a notebook
        // command is actually selected from the filtered list.
        if (available.Length == 0) return w.Tab?.Notebook != null ? false : null;
        if (w.Tab?.Notebook == null && w.Input.Trim().Length == 0) return null;
        if (action == "palette-next")
        { w.OptionIndex = (w.OptionIndex + 1) % available.Length; Version++; return true; }
        var command = available[Math.Clamp(w.OptionIndex, 0, available.Length - 1)].Path;
        if (command == "new-notebook") return CreateNotebook(w);
        if (command == "new-folder")
        {
            var target = w.PaletteFrom == "explorer" ? ProjectTree(EditorProjectRoot, w.ExpandedProjectFolders).ElementAtOrDefault(w.Item) : null;
            return BeginNewProjectFolder(w, target == null ? null : target.Kind == "folder" ? target.Path : ParentOf(target.Path));
        }
        bool changed = NotebookCommand(w, command);
        if (changed) { w.Input = ""; w.Focus = "body"; }
        return changed;
    }

    private bool CreateNotebook(LabWindow w)
    {
        if (w.Tabs.Count >= 12) return false;
        string filename, path;
        do
        {
            var count = ++w.UntitledCount;
            filename = count == 1 ? "untitled.ipynb" : "untitled-" + count + ".ipynb";
            path = EditorProjectRoot + "/" + filename;
        }
        while (Terminal.Exists(path) || w.Tabs.Any(t => EditorPath(t) == path));
        var tab = new LabTab(filename, path, EditorNotebook.Empty()) { SavedBody = "" };
        w.Tabs.Add(tab); w.ActiveTab = w.Tabs.Count - 1; w.Focus = "body"; w.Status = "Новый блокнот. Ctrl+S — сохранить.";
        Version++;
        return true;
    }
}
