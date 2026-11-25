using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        // The engine owns paths, file edits, collisions and undo. The viewport gets the
        // same tree for editor and Explorer focus; no path is reconstructed from a label.
        internal event Action<string, string> ExplorerCommand;
        private const string ExplorerPrefix = "explorer:";
        private const string ExplorerMenuAction = "explorer-ui:menu";
        private bool explorerMenu, explorerMenuVisible, explorerFocused, explorerCanOpenMenu;
        private Rect explorerMenuBounds = Rect.Empty;
        private int explorerMenuIndex, explorerMenuTop;
        private ExplorerChoice[] explorerChoices = new ExplorerChoice[0];
        private string explorerSelectedPath = "";

        private sealed class ExplorerChoice
        {
            internal readonly string Action, Name;
            internal readonly bool Enabled;
            internal ExplorerChoice(string action, string name, bool enabled = true)
            { Action = action; Name = name; Enabled = enabled; }
        }

        private void ExplorerStartFrame()
        {
            explorerMenuVisible = false;
            explorerFocused = false;
            explorerCanOpenMenu = false;
            explorerMenuBounds = Rect.Empty;
        }

        private bool ExplorerMenuWheel(Point point, int delta)
        {
            return explorerMenuVisible && explorerMenuBounds.Contains(point) && delta != 0 &&
                ExplorerViewportKey(delta > 0 ? "Up" : "Down", false);
        }

        private bool OnExplorerHotspot(NotebookHit hit)
        {
            if (hit.Action == ExplorerMenuAction)
            {
                explorerMenu = !explorerMenu;
                explorerMenuIndex = explorerMenuTop = 0;
                notebookMenu = "";
                InvalidateVisual();
                return true;
            }
            if (!hit.Action.StartsWith(ExplorerPrefix, StringComparison.Ordinal) || ExplorerCommand == null) return false;
            explorerMenu = false;
            ExplorerCommand(hit.Action.Substring(ExplorerPrefix.Length), hit.Path);
            InvalidateVisual();
            return true;
        }

        private bool ExplorerViewportKey(string key, bool shift)
        {
            if (key == "F10" && shift && explorerFocused && explorerCanOpenMenu)
            {
                explorerMenu = true;
                notebookMenu = "";
                explorerMenuIndex = explorerMenuTop = 0;
                InvalidateVisual();
                return true;
            }
            if (!explorerMenuVisible || !explorerMenu) return false;
            if (key == "Esc") { explorerMenu = false; InvalidateVisual(); return true; }
            if (key == "Up" || key == "Down")
            {
                int step = key == "Up" ? -1 : 1, next = explorerMenuIndex;
                do { next += step; }
                while (next >= 0 && next < explorerChoices.Length && !explorerChoices[next].Enabled);
                if (next >= 0 && next < explorerChoices.Length) { explorerMenuIndex = next; InvalidateVisual(); }
                return true;
            }
            if (key != "Enter" || explorerMenuIndex >= explorerChoices.Length || ExplorerCommand == null) return false;
            ExplorerChoice choice = explorerChoices[explorerMenuIndex];
            if (choice.Enabled)
            {
                explorerMenu = false;
                ExplorerCommand(choice.Action, explorerSelectedPath);
                InvalidateVisual();
            }
            return true;
        }

        private static string ExplorerDescription(Data window)
        {
            string status = window.S("status");
            if (window.S("focus") == "file-name")
                return (window.S("renamePath").EndsWith("/",StringComparison.Ordinal) ? "Новая папка. " : "Имя файла. ") +
                    "Введено: " + window.S("input") + ". " + status;
            var selected = window.Items("explorerEntries").ElementAtOrDefault(window.I("item",-1));
            return "Проводник " + CodeProjectName(window) + ". " + (selected == null ? "" :
                "Выбрано: " + selected.S("title") + ". ") + status + " Ctrl+P — поиск файла; Shift+F10 — действия.";
        }

        private void DrawWorkingExplorer(Data window, double x, double width, double h)
        {
            var entries = window.Items("explorerEntries").ToArray();
            string focus = window.S("focus");
            bool nameField = focus == "file-name", folderName = nameField && window.S("renamePath").EndsWith("/", StringComparison.Ordinal);
            bool treeFocus = focus == "explorer" || nameField;
            bool canAct = window.B("active") && NotebookInteractionClear() && ExplorerCommand != null && (focus == "body" || treeFocus);
            explorerFocused = window.B("active") && treeFocus;
            var clip = new Rect(x + 1, 30, Math.Max(1, width - 2), Math.Max(1, h - 53));
            context.PushClip(new System.Windows.Media.RectangleGeometry(clip));
            try
            {
                string projectLabel = CodeProjectName(window).ToUpperInvariant();
                Text("⌄  " + projectLabel, x + 8, 64, 11, CodeForeground, width - (canAct ? 70 : 16));
                if (canAct)
                {
                    if (nameField)
                    {
                        ExplorerButton(x + width - 32, 58, 24, "×", "cancel", "Отменить ввод имени (Esc)");
                    }
                    else
                    {
                        ExplorerButton(x + width - 58, 58, 25, "▱+", "new-folder", "Создать папку");
                        if (h >= 154)
                        {
                            ExplorerButton(x + width - 30, 58, 25, "···", ExplorerMenuAction, "Действия проводника · Shift+F10");
                            explorerCanOpenMenu = true;
                        }
                    }
                }
                double row = 85;
                if (folderName)
                {
                    double indent = x + 8;
                    Text("▱", indent, row + 3, 12, "#D3AE78", 20);
                    var bounds = new Rect(indent + 20, row + 1, Math.Max(1, width - 34), 21);
                    Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, NotebookLineBorder);
                    Rectangle(bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), 19, CodeCanvas);
                    Field(window.S("input"), window, bounds.X + 4, bounds.Y + 1, bounds.Width - 8, 12, CodeForeground, 0, true, face: regular);
                    row += 25;
                }
                string status = treeFocus ? window.S("status") : "";
                bool note = status.Length > 0 && (nameField || status.StartsWith("Уже", StringComparison.OrdinalIgnoreCase) ||
                    status.IndexOf("существует", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("конфликт", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("отмен", StringComparison.OrdinalIgnoreCase) >= 0);
                double limit = h - (note ? 69 : 24);
                int chosen = treeFocus ? window.I("item", -1) : -1;
                var expanded = new HashSet<string>(window.Items("expandedFolders").Select(item => item.Value()), StringComparer.Ordinal);
                var cursorEntry = entries.ElementAtOrDefault(Math.Max(-1, chosen));
                string selectedPath = cursorEntry == null ? "" : cursorEntry.S("path");
                if (explorerFocused) explorerSelectedPath = selectedPath;
                int max = Math.Max(1, (int)((limit - row) / 22)), first = Math.Max(0, chosen - max + 1);
                string currentPath = window.S("url");
                int rootDepth = window.I("projectRootDepth", 3);
                for (int i = first; i < Math.Min(entries.Length, first + max) && row < limit; i++, row += 22)
                {
                    Data entry = entries[i];
                    string path = entry.S("path"), caption = entry.S("title"), kind = entry.S("kind");
                    int depth = Math.Min(6, Math.Max(0, path.Count(ch => ch == '/') - rootDepth - 1));
                    double left = x + 8 + depth * 12, remain = x + width - left - 19;
                    if (remain < 8) continue;
                    bool folder = kind == "folder";
                    bool selected = i == chosen || !treeFocus && path == currentPath;
                    if (selected) Rectangle(x+1, row, width-3, 22, treeFocus ? CodeSelection : CodeExplorerSelection);
                    if (folder)
                    {
                        Text(expanded.Contains(path) ? "⌄" : "›", left, row+2, 12, NotebookMuted, 12);
                        Text("▱", left+12, row+2, 12, "#D3AE78", 14);
                        left += 11;
                    }
                    else DrawCodeFileIcon(caption, left, row+3);
                    left += 16;
                    double textWidth = Math.Max(1, x+width-left-6);
                    if (i == chosen && nameField && !folderName)
                    {
                        Rectangle(left-2, row+1, textWidth+2, 20, CodeCaret);
                        Rectangle(left-1, row+2, textWidth, 18, CodeCanvas);
                        Field(window.S("input"), window, left+2, row+2, textWidth-4, 12, CodeForeground, 0, true, face: regular);
                    }
                    else
                    {
                        Text(caption, left, row+2, 12, selected ? CodeForeground : NotebookMuted, textWidth);
                        if (canAct && !nameField)
                        {
                            var hit = new Rect(x+1,row,width-3,22);
                            AddNotebookHit(hit, ExplorerPrefix+"select", "Выбрать " + caption + " · F2 — имя файла", path: path);
                            AddNotebookHit(hit, ExplorerPrefix+"open",
                                folder ? "Раскрыть или свернуть " + caption : "Открыть " + caption + " (двойной щелчок)",
                                clicks: 2, path: path);
                            if (folder)
                                AddNotebookHit(new Rect(left - 27,row,25,22), ExplorerPrefix+"open",
                                    "Раскрыть или свернуть " + caption, path: path);
                        }
                    }
                }
                if (note)
                {
                    Rectangle(x+1, limit+1,width-2,42,NotebookEditor);
                    Text(status, x+8, limit+6, 11, (status.IndexOf("существует",StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("занято",StringComparison.OrdinalIgnoreCase) >= 0) ?
                        NotebookError : CodeForeground, width-17, 2);
                }
                if (explorerMenu && canAct && !nameField)
                    DrawExplorerChoices(x+4, 82, width-8, h-111, selectedPath, window);
            }
            finally { context.Pop(); }
        }

        private void ExplorerButton(double x, double y, double width, string label, string command, string accessible)
        {
            Text(label, x+4, y+3, 13, CodeForeground, width-4);
            AddNotebookHit(new Rect(x,y,width,23), command == ExplorerMenuAction ? command : ExplorerPrefix+command, accessible);
        }

        private void DrawExplorerChoices(double x, double y, double w, double h, string selected, Data window)
        {
            int rows = Math.Max(0, (int)((h - 21) / 24));
            if (rows < 1) return;
            explorerMenuVisible = true;
            explorerSelectedPath = selected;
            Data entryAtPath = window.Items("explorerEntries").FirstOrDefault(item => item.S("path") == selected);
            bool hasSelection = entryAtPath != null;
            bool fileSelected = hasSelection && entryAtPath.S("kind") == "file";
            explorerChoices = new[] {
                new ExplorerChoice("new-folder","Новая папка"),
                new ExplorerChoice("quick-open","Найти файл · Ctrl+P"),
                new ExplorerChoice("rename","Переименовать · F2",fileSelected),
                new ExplorerChoice("cut","Вырезать · Ctrl+X",hasSelection),
                new ExplorerChoice("copy","Копировать · Ctrl+C",hasSelection),
                new ExplorerChoice("paste","Вставить · Ctrl+V",window.B("explorerCanPaste")),
                new ExplorerChoice("undo","Отменить перенос",window.B("canUndoExplorer"))
            };
            rows = Math.Min(rows, explorerChoices.Length);
            if (!explorerChoices[explorerMenuIndex].Enabled) explorerMenuIndex = 0;
            explorerMenuTop = Math.Max(explorerMenuIndex - rows + 1, Math.Min(explorerMenuTop, explorerMenuIndex));
            double panelHeight = rows*24 + 21;
            Rounded(x,y,w,panelHeight,NotebookEditor,4);
            explorerMenuBounds = NotebookToSurface(new Rect(x,y,w,panelHeight));
            AddNotebookHit(new Rect(x,y,w,panelHeight), ExplorerMenuAction, "Закрыть меню проводника");
            for (int n = explorerMenuTop; n < Math.Min(explorerMenuTop + rows, explorerChoices.Length); n++)
            {
                ExplorerChoice entry = explorerChoices[n];
                double row = y+4+(n-explorerMenuTop)*24;
                if (n == explorerMenuIndex) Rounded(x+2,row,w-4,23,CodeSelection,3);
                Text(entry.Name,x+7,row+3,11,entry.Enabled ? CodeForeground : NotebookMuted,w-13);
                if (entry.Enabled)
                    AddNotebookHit(new Rect(x,row,w,24),ExplorerPrefix+entry.Action,entry.Name,path:selected);
            }
            Text("↑↓   Enter   Esc",x+7,y+panelHeight-15,9,NotebookMuted,w-14);
        }
    }
}
