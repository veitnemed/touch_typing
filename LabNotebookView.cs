using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        private const double NotebookToolbar = 32, NotebookLine = 20;
        private const string NotebookEditor = "#161B22", NotebookLineBorder = "#30363D";
        private const string NotebookMuted = "#9DA7B3", NotebookError = "#FF9B92";
        private const string NotebookHealthy = "#7EE787";
        private const string NotebookOverflow = "notebook-ui:more";
        private const string NotebookKernelMenu = "notebook-ui:kernel";
        private const string NotebookScrollDown = "notebook-ui:scroll-down";
        private const string NotebookScrollUp = "notebook-ui:scroll-up";
        internal event Action<string, int, int> NotebookCommand;
        private readonly Dictionary<string, NotebookViewport> notebookViewports = new Dictionary<string, NotebookViewport>();
        private readonly List<NotebookHit> notebookHits = new List<NotebookHit>();
        private NotebookViewport notebookTarget;
        private string notebookMenu = "", notebookMenuDocument = "";
        private int notebookMenuIndex, notebookMenuTop;
        private bool notebookMenuVisible;
        private string[] notebookMenuActions = new string[0];
        private bool[] notebookMenuEnabled = new bool[0];
        private int notebookMenuCell = -1;
        private Matrix notebookTransform = Matrix.Identity;
        private Vector notebookWindowOffset;
        private Rect notebookWindowClip = Rect.Empty;
        private const string NbA11y = "Блокнот: Enter — редактировать, Esc — выбор ячейки, Ctrl+Enter — выполнить, Ctrl+Shift+P — команды, PageUp/PageDown — прокрутка.";

        private sealed class NotebookViewport
        {
            internal Rect Bounds = Rect.Empty;
            internal double Offset, Max, ViewHeight, Side, MaxSide;
            internal string Stamp;
            internal bool Open;
        }

        private sealed class NotebookHit
        {
            internal readonly Rect Box;
            internal readonly string Action, Label, Path;
            internal readonly int Cell, Clicks;
            internal readonly Func<Point, int> Offset;
            internal NotebookHit(Rect box, string action, string label, int cell, int clicks, Func<Point,int> offset, string path)
            { Box = box; Action = action; Label = label; Cell = cell; Clicks = clicks; Offset = offset; Path = path; }
        }

        private sealed class NotebookPaintCell
        {
            internal Data Cell;
            internal bool Code, Editing;
            internal string[] Source, Output;
            internal int Caret, Anchor;
            internal double Top, SourceHeight, OutputHeight, Height;
        }

        private void NotebookStartFrame(double scale, double x, double y)
        {
            notebookTransform = new Matrix(scale, 0, 0, scale, x, y);
            notebookWindowOffset = new Vector();
            notebookWindowClip = new Rect(0, 0, ActualWidth, ActualHeight);
        }

        private void NotebookWindow(Rect bounds)
        {
            notebookWindowOffset = new Vector(bounds.X, bounds.Y);
            bounds.Transform(notebookTransform);
            notebookWindowClip = bounds;
            notebookWindowClip.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
        }

        private Rect NotebookToSurface(Rect rect)
        {
            rect.Offset(notebookWindowOffset.X, notebookWindowOffset.Y);
            rect.Transform(notebookTransform);
            rect.Intersect(notebookWindowClip);
            return rect;
        }

        private bool NotebookInteractionClear()
        {
            return !scene.B("overview") && !scene.B("switcher") && !scene.B("screenshot") &&
                !scene.B("showDesktop") && scene.S("desktopOverlay").Length == 0;
        }

        private void AddNotebookHit(Rect box, string action, string label, int cell = -1, int clicks = 1, Func<Point,int> offset = null, string path = "")
        {
            if (NotebookCommand == null && ExplorerCommand == null || !NotebookInteractionClear() || box.IsEmpty || box.Width < 1 || box.Height < 1)
                return;
            Rect surface = NotebookToSurface(box);
            if (!surface.IsEmpty && surface.Width >= 1 && surface.Height >= 1)
                notebookHits.Add(new NotebookHit(surface, action, label, cell, clicks, offset, path));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var point = e.GetPosition(this);
            var found = notebookHits.LastOrDefault(hit => hit.Box.Contains(point));
            string label = found == null ? null : found.Label;
            if (!Equals(ToolTip, label)) ToolTip = label;
            Cursor = found != null ? found.Action == "edit" ? Cursors.IBeam : Cursors.Hand : null;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point point = e.GetPosition(this);
            var hit = notebookHits.LastOrDefault(item => e.ClickCount >= item.Clicks && item.Box.Contains(point));
            if (hit == null)
            {
                if (notebookMenu.Length != 0 || explorerMenu) { notebookMenu = ""; explorerMenu = false; InvalidateVisual(); }
                return;
            }
            Focus();
            if (OnExplorerHotspot(hit)) { e.Handled = true; return; }
            explorerMenu = false;
            if (hit.Action == NotebookOverflow || hit.Action == NotebookKernelMenu)
            {
                // A local menu only opens a list of implemented actions; it never scores a key.
                notebookMenu = notebookMenu == hit.Action ? "" : hit.Action;
                notebookMenuIndex = notebookMenuTop = 0;
                InvalidateVisual();
            }
            else if (hit.Action == NotebookScrollDown || hit.Action == NotebookScrollUp)
                ScrollNotebookView(hit.Action == NotebookScrollUp ? -1 : 1, false, 1);
            else
            {
                notebookMenu = "";
                NotebookCommand(hit.Action, hit.Cell, hit.Offset == null ? -1 : hit.Offset(point));
                InvalidateVisual();
            }
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            var mouse = e.GetPosition(this);
            if (ExplorerMenuWheel(mouse, e.Delta)) { e.Handled = true; return; }
            if (notebookTarget == null || !notebookTarget.Bounds.Contains(mouse)) return;
            if (notebookMenuVisible && notebookMenu.Length != 0)
            {
                MoveNotebookMenu(-Math.Sign(e.Delta));
                e.Handled = true;
                return;
            }
            bool horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ScrollNotebookView(-Math.Sign(e.Delta), horizontal, Math.Max(1, Math.Abs(e.Delta) / 120)))
                e.Handled = true;
        }

        internal bool NotebookViewportKey(string key, bool shift = false)
        {
            if (ExplorerViewportKey(key, shift)) return true;
            if (key == "F10" && shift)
            {
                if (notebookTarget == null || !notebookTarget.Open || !NotebookInteractionClear() ||
                    !notebookHits.Any(item => item.Action == NotebookOverflow)) return false;
                notebookMenu = NotebookOverflow;
                notebookMenuIndex = notebookMenuTop = 0;
                InvalidateVisual();
                return true;
            }
            if (notebookMenuVisible && notebookMenu.Length != 0)
            {
                if (key == "Esc") { notebookMenu = ""; InvalidateVisual(); return true; }
                if (key == "Up" || key == "Down") { MoveNotebookMenu(key == "Up" ? -1 : 1); return true; }
                if (key == "Enter" && NotebookCommand != null && notebookMenuIndex < notebookMenuActions.Length)
                {
                    if (notebookMenuEnabled[notebookMenuIndex])
                    {
                        string command = notebookMenuActions[notebookMenuIndex];
                        notebookMenu = "";
                        NotebookCommand(command, NotebookMenuCellFor(command, notebookMenuCell), -1);
                        InvalidateVisual();
                    }
                    return true;
                }
            }
            return key == "PageUp" || key == "PageDown" ? ScrollNotebookView(key == "PageUp" ? -1 : 1, shift, 1) : false;
        }

        private void MoveNotebookMenu(int delta)
        {
            if (delta == 0 || notebookMenuEnabled.Length == 0) return;
            int next = notebookMenuIndex;
            do { next += delta; }
            while (next >= 0 && next < notebookMenuEnabled.Length && !notebookMenuEnabled[next]);
            if (next >= 0 && next < notebookMenuEnabled.Length && notebookMenuEnabled[next])
            {
                notebookMenuIndex = next;
                InvalidateVisual();
            }
        }

        private bool ScrollNotebookView(int direction, bool horizontal, double steps)
        {
            if (notebookTarget == null || !notebookTarget.Open || !NotebookInteractionClear() || direction == 0) return false;
            var view = notebookTarget;
            double old = horizontal ? view.Side : view.Offset;
            double page = horizontal ? 105 : Math.Max(42, view.ViewHeight * .76);
            double next = Clamp(old + direction * steps * page, 0, horizontal ? view.MaxSide : view.Max);
            if (horizontal) view.Side = next; else view.Offset = next;
            if (Math.Abs(old - next) > .01) InvalidateVisual();
            return horizontal ? view.MaxSide > .01 : view.Max > .01;
        }

        private static double Clamp(double value, double lo, double hi)
        { return Math.Max(lo, Math.Min(value, Math.Max(lo, hi))); }

        private NotebookViewport NotebookView(string key)
        {
            NotebookViewport result;
            if (notebookViewports.TryGetValue(key, out result)) return result;
            if (notebookViewports.Count >= 20) notebookViewports.Remove(notebookViewports.Keys.First());
            result = new NotebookViewport();
            notebookViewports.Add(key, result);
            return result;
        }

        private static string[] NotebookOutput(Data cell)
        {
            string output = cell.S("output"), trace = cell.S("traceback");
            if (trace.Length > 0) return trace.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return output.Length == 0 ? new string[0] : output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        }

        private static bool NotebookBodyReady(Data window)
        {
            string focus = window.S("focus");
            return window.B("active") && (focus == "body" || focus == "explorer");
        }

        private void DrawNotebookPage(Data window, Data nb, string path, double x, double y, double w, double h, bool primary)
        {
            if (w < 105 || h < NotebookToolbar + 16) return;
            Data[] cells = nb.Items("cells").ToArray();
            int selected = nb.I("selected");
            if (selected < 0 || selected >= cells.Length) return;
            bool bodyFocused = primary && window.B("active") && window.S("focus") == "body";
            bool interactive = primary && NotebookBodyReady(window) && NotebookInteractionClear();
            Data kernel = nb.D("kernel");
            string kernelState = kernel.S("state", "idle");
            bool online = kernel.Exists, busy = kernelState == "busy";
            int toolbarHeight = w < 290 ? 49 : (int)NotebookToolbar;
            double viewTop = y + toolbarHeight, viewH = Math.Max(1, h - toolbarHeight);
            string identity = window.S("id") + "\n" + path;
            var viewport = NotebookView(identity);
            viewport.ViewHeight = viewH;
            viewport.Bounds = NotebookToSurface(new Rect(x, viewTop, w, viewH));
            viewport.Open = bodyFocused;
            if (interactive) notebookTarget = viewport;
            double pad = w < 385 ? 8 : 17, gutter = w < 385 ? 36 : 43;
            double lineX = x + pad + gutter + 10, end = x + w - pad - 10;
            double usable = Math.Max(14, end - lineX - 1);
            // Keep Markdown text readable in the same editor group; do not treat it as code.
            double stack = 8;
            var model = new List<NotebookPaintCell>();
            double longest = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                Data c = cells[i];
                string original = c.S("source");
                bool code = c.S("kind") == "code", editing = i == selected && nb.B("editing");
                string[] source = original.Split('\n');
                string[] output = NotebookOutput(c);
                if (code || editing)
                    longest = Math.Max(longest, source.Max(l => NotebookEstimateWidth(l, true)));
                if (output.Length > 0) longest = Math.Max(longest, output.Max(l => NotebookEstimateWidth(l, true)));
                double sourceH = 14 + (code || editing ? source.Length * NotebookLine : source.Select(MarkdownLineHeight).Sum()) + (code ? 19 : 3);
                double outputH = output.Length > 0 ? output.Length * NotebookLine + 31 : 0;
                var layout = new NotebookPaintCell {
                    Cell = c, Code = code, Editing = editing, Source = source, Output = output, Top = stack,
                    SourceHeight = sourceH, OutputHeight = outputH, Height = sourceH + outputH + 14,
                    Caret = i == selected ? Math.Min(nb.I("caret"), original.Length) : 0,
                    Anchor = i == selected ? Math.Min(nb.I("anchor"), original.Length) : 0
                };
                model.Add(layout); stack += layout.Height;
            }
            var chosen = model[selected];
            viewport.Max = Math.Max(0, stack - viewH);
            viewport.MaxSide = Math.Max(0, longest - usable + 9);
            string stamp = selected + "/" + chosen.Cell.S("id") + "/" + nb.B("editing") + "/" +
                chosen.Caret + "/" + chosen.Cell.I("runs") + "/" + kernel.I("generation") + "/" + bodyFocused;
            if (viewport.Stamp != stamp)
            {
                viewport.Stamp = stamp;
                bool editing = chosen.Editing && bodyFocused;
                int caretRow = 0, caretColumn = 0;
                if (editing)
                {
                    string before = chosen.Cell.S("source").Substring(0, Math.Max(0, chosen.Caret));
                    caretRow = before.Count(c => c == '\n');
                    caretColumn = before.Length - 1 - before.LastIndexOf('\n');
                }
                double top = chosen.Top + (editing ? 7 + caretRow * NotebookLine : 0);
                double reveal = editing ? NotebookLine + 16 : Math.Min(chosen.SourceHeight + chosen.OutputHeight, viewH - 2);
                if (top < viewport.Offset) viewport.Offset = top;
                else if (top + reveal > viewport.Offset + viewH) viewport.Offset = Math.Max(0, top + reveal - viewH);
                if (editing && caretColumn > 0)
                {
                    string activeLine = chosen.Source[Math.Min(caretRow, chosen.Source.Length - 1)];
                    double caretX;
                    using (var measure = new ChromeTextLine(activeLine, mono, CodeFontSize, Ui.Brush(CodeForeground),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip))
                        caretX = measure.Distance(Math.Min(caretColumn, activeLine.Length));
                    viewport.MaxSide = Math.Max(viewport.MaxSide, caretX - usable + 13);
                    if (caretX > viewport.Side + usable - 12) viewport.Side = caretX - usable + 12;
                    else if (caretX < viewport.Side) viewport.Side = Math.Max(0, caretX - 12);
                }
                else viewport.Side = 0;
            }
            viewport.Offset = Clamp(viewport.Offset, 0, viewport.Max);
            viewport.Side = Clamp(viewport.Side, 0, viewport.MaxSide);

            Rectangle(x, y, w, h, CodeCanvas);
            DrawNotebookToolbar(nb, kernel, x, y, w, toolbarHeight, h, selected, interactive);
            var clip = new Rect(x + 1, viewTop, Math.Max(1, w - 2), viewH);
            context.PushClip(new RectangleGeometry(clip));
            try
            {
                for (int i = 0; i < model.Count; i++)
                {
                    var cell = model[i];
                    double row = viewTop + cell.Top - viewport.Offset;
                    if (row + cell.Height <= viewTop || row >= viewTop + viewH) continue;
                    bool current = i == selected;
                    var band = new Rect(x + pad, row, Math.Max(1, w - pad * 2 - 5), cell.Height - 10);
                    if (interactive && online)
                    {
                        var clickCell = Rect.Intersect(band, clip);
                        AddNotebookHit(clickCell, "select", "Выбрать ячейку " + (i + 1), i);
                        var editRegion = Rect.Intersect(new Rect(lineX - 8, row, Math.Max(14, usable + 12), cell.SourceHeight), clip);
                        var pointerOffset = cell.Code || cell.Editing ?
                            NotebookPointerOffset(cell.Source, lineX, row + 8, viewport.Side) : null;
                        if (cell.Code) AddNotebookHit(editRegion, "edit", "Редактировать код ячейки " + (i + 1), i, 1, pointerOffset);
                        else AddNotebookHit(editRegion, "edit", "Править Markdown ячейки " + (i + 1) + " (двойной щелчок)", i, 2, pointerOffset);
                    }
                    DrawNotebookCell(cell, i, selected, bodyFocused, interactive && online, busy, clip, x + pad, lineX,
                        end, row, usable, viewport.Side);
                    if (current && interactive && online && !busy)
                        DrawNotebookCellTools(x + pad + gutter, end, row, selected, model.Count, cell.Code, clip);
                }
                if (viewport.Max > .01)
                {
                    double track = Math.Max(1, viewH - 8);
                    double thumb = Math.Max(18, track * viewH / stack);
                    double thumbY = viewTop + 4 + Math.Max(0, track - thumb) * viewport.Offset / viewport.Max;
                    Rounded(x + w - 6, thumbY, 4, Math.Min(track, thumb), "#535B65", 2);
                    if (interactive && viewport.Offset > .01)
                        AddNotebookHit(new Rect(x + w - 10, viewTop, 10, Math.Max(0, thumbY - viewTop)), NotebookScrollUp, "Прокрутить к предыдущим ячейкам");
                    if (interactive && viewport.Offset < viewport.Max)
                        AddNotebookHit(new Rect(x + w - 10, thumbY + thumb, 10, Math.Max(0, viewTop + viewH - thumbY - thumb)),
                            NotebookScrollDown, "Прокрутить к следующим ячейкам");
                }
            }
            finally { context.Pop(); }
            if (interactive && online && notebookMenu.Length > 0)
            {
                if (notebookMenuDocument != identity) notebookMenu = "";
                else DrawNotebookChoices(nb, x, y, w, h, toolbarHeight, selected, busy);
            }
            notebookMenuDocument = interactive ? identity : notebookMenuDocument;
        }

        private Func<Point, int> NotebookPointerOffset(string[] lines, double lineX, double lineY, double scroll)
        {
            var toSurface = notebookTransform;
            var offset = notebookWindowOffset;
            return delegate(Point clicked)
            {
                var inverse = toSurface;
                inverse.Invert();
                Point local = inverse.Transform(clicked) - offset;
                int row = Math.Max(0, Math.Min(lines.Length - 1, (int)Math.Floor((local.Y - lineY) / NotebookLine)));
                int start = lines.Take(row).Sum(line => line.Length + 1);
                using (var shape = new ChromeTextLine(lines[row].TrimEnd('\r'), mono, CodeFontSize, Ui.Brush(CodeForeground),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip))
                    return start + shape.CharacterAt(local.X - lineX + scroll);
            };
        }

        private static double NotebookEstimateWidth(string line, bool isMono)
        {
            return (line.Length + line.Count(c => c == '\t') * 7 + line.Count(c => c >= 0x2e80)) * (isMono ? 8.45 : 7);
        }

        private void DrawNotebookToolbar(Data nb, Data kernel, double x, double y, double w, int height, double totalHeight, int selected, bool interactive)
        {
            Rectangle(x, y, w, height, CodeCanvas);
            Rectangle(x, y + height - 1, w, 1, NotebookLineBorder);
            bool cramped = w < 290, busy = kernel.S("state") == "busy", hasMenu = totalHeight - height >= 54;
            string state = kernel.S("state", "idle"), name = busy ? "Выполняется" : state == "interrupted" ? "Прервано" : "Готово";
            string status = kernel.Exists ? "Python 3  ·  " + name : "Учебное ядро Python";
            // The same WPF text formatter and DPI draw and measure these captions.
            Func<string, double> toolWidth = text => Math.Max(26, Math.Ceiling(MeasureSceneText(text, 12)) + 14);
            Func<string, double> statusMeasure = text => Math.Ceiling(MeasureSceneText(text, 11)) + 17;
            string[] full = { "+ Код", "+ Markdown", busy ? "■  Прервать" : "▷▷  Выполнить всё" };
            string[] captions = { "+ ▤", "+ M", busy ? "■" : "▷▷" };
            double[] widths = captions.Select(toolWidth).ToArray();
            double more = hasMenu ? toolWidth("···") : 0, statusWidth = statusMeasure(status);
            double available = w - 14 - more;
            if ((!cramped && widths[0] + widths[1] + toolWidth(full[2]) + 9 + 8 + statusWidth > available) || cramped && statusWidth > w - 14)
            {
                status = "Python 3";
                statusWidth = statusMeasure(status);
            }
            if (!cramped) available -= 8 + statusWidth;
            // In a very narrow split, prioritize Run; Add actions are also in the command menu.
            for (int i = 1; i >= 0; i--)
                if (widths.Sum() + (widths[0] + widths[1] > 0 ? 9 : 0) > available) widths[i] = 0;
            available -= widths.Sum() + (widths[0] + widths[1] > 0 ? 9 : 0);
            foreach (int i in new[] { 2, 0, 1 })
            {
                double growth = toolWidth(full[i]) - widths[i];
                if (widths[i] == 0 || growth > available) continue;
                captions[i] = full[i]; widths[i] += growth; available -= growth;
            }
            double restartWidth = toolWidth("↻  Перезапуск");
            double px = x + 7;
            bool canInsert = nb.Items("cells").Count() < 128;
            bool canRun = nb.Items("cells").Any(cell => cell.S("kind") == "code");
            if (kernel.Exists)
            {
                DrawNotebookTool(px, y + 3, widths[0], captions[0], "add-below", "Добавить ячейку кода ниже",
                    selected, interactive && !busy && canInsert); px += widths[0];
                DrawNotebookTool(px, y + 3, widths[1], captions[1], "add-markdown", "Добавить Markdown ниже",
                    selected, interactive && !busy && canInsert); px += widths[1];
                if (widths[0] + widths[1] > 0)
                {
                    Rectangle(px + 3, y + 8, 1, 15, NotebookLineBorder); px += 9;
                }
                DrawNotebookTool(px, y + 3, widths[2], captions[2], busy ? "interrupt" : "run-all",
                    busy ? "Прервать выполнение" : "Выполнить все ячейки по порядку", -1, interactive && (busy || canRun)); px += widths[2];
                if (restartWidth <= available)
                {
                    DrawNotebookTool(px, y + 3, restartWidth, "↻  Перезапуск", "restart",
                        "Перезапустить ядро и сбросить переменные", -1, interactive); px += restartWidth;
                }
                if (hasMenu)
                    DrawNotebookTool(px, y + 3, more, "···", NotebookOverflow, "Команды блокнота · Shift+F10", -1, interactive);
            }
            double statusX = cramped ? x + 7 : x + w - 7 - statusWidth;
            double statusY = cramped ? y + 25 : y + 7;
            Rectangle(statusX, statusY + 7, 6, 6, busy ? "#E3B341" : state == "interrupted" ? NotebookError : NotebookHealthy);
            Text(status, statusX + 11, statusY, 11, CodeForeground, statusWidth - 11);
            if (kernel.Exists && interactive && hasMenu)
                AddNotebookHit(new Rect(statusX - 3, statusY + (cramped ? 2 : -4), statusWidth + 3, cramped ? 21 : 24),
                    NotebookKernelMenu, kernel.S("label", "Python 3 · учебное ядро") + " — " + name, -1);
        }

        private void DrawNotebookTool(double x, double y, double w, string caption, string action, string tooltip,
            int cell, bool enabled, Rect? clip = null)
        {
            if (w < 17) return;
            Text(caption, x + 6, y + 3, 12, enabled ? CodeForeground : NotebookMuted, w - 8);
            if (enabled)
            {
                var box = new Rect(x, y, w, 24);
                AddNotebookHit(clip.HasValue ? Rect.Intersect(clip.Value, box) : box, action, tooltip, cell);
            }
        }

        private void DrawNotebookCellTools(double left, double right, double row, int cell, int count, bool code, Rect clip)
        {
            double width = right - left, top = row - 11;
            if (width < 210) return; // The full command list is still available in the document overflow.
            const int tool = 26;
            double x = right - tool * 5 - 5;
            Rounded(x, top, tool * 5 + 3, 23, CodeInset, 4);
            DrawNotebookTool(x, top, tool, "↑", "move-up", "Переместить ячейку выше", cell, cell > 0, clip); x += tool;
            DrawNotebookTool(x, top, tool, "↓", "move-down", "Переместить ячейку ниже", cell, cell + 1 < count, clip); x += tool;
            DrawNotebookTool(x, top, tool, "⧉", "copy-cell", "Скопировать ячейку", cell, true, clip); x += tool;
            DrawNotebookTool(x, top, tool, code ? "M" : "<>", code ? "markdown" : "code",
                code ? "Преобразовать ячейку в Markdown" : "Преобразовать ячейку в код", cell, true, clip); x += tool;
            DrawNotebookTool(x, top, tool, "×", "delete-cell", "Удалить выбранную ячейку", cell, true, clip);
        }

        private static int NotebookMenuCellFor(string action, int cell)
        {
            return new[] { "run-above", "run-below", "add-above", "add-below", "add-markdown", "move-up", "move-down", "copy-cell",
                "duplicate-cell", "markdown", "code", "delete-cell" }.Contains(action) ? cell : -1;
        }

        private void DrawNotebookChoices(Data nb, double x, double y, double w, double h, int toolbarHeight, int cell, bool busy)
        {
            bool kernel = notebookMenu == NotebookKernelMenu;
            var cells = nb.Items("cells").ToArray();
            bool code = cells[cell].S("kind") == "code";
            int count = cells.Length;
            var labels = kernel ? new[] { "Python 3 · учебное ядро", "Перезапустить ядро", "Прервать выполнение" } :
                new[] { "Выполнить ячейки выше", "Выполнить ячейки ниже", "Перезапустить ядро", "Очистить вывод",
                    "Добавить ячейку выше", "Вставить скопированную ячейку", "Переместить выше", "Переместить ниже",
                    "Скопировать ячейку", "Дублировать ячейку", code ? "Сделать Markdown" : "Сделать кодом",
                    "Удалить ячейку", "Восстановить / отменить", "Повторить изменение", "Добавить ячейку кода ниже", "Добавить Markdown ниже" };
            var actions = kernel ? new[] { "", "restart", "interrupt" } :
                new[] { "run-above", "run-below", "restart", "clear", "add-above", "paste-cell", "move-up", "move-down",
                    "copy-cell", "duplicate-cell", code ? "markdown" : "code", "delete-cell", "undo", "redo", "add-below", "add-markdown" };
            double panel = Math.Min(w - 14, 267), top = y + toolbarHeight, left = x + w - panel - 7;
            int visible = Math.Min(labels.Length, Math.Max(0, (int)((h - toolbarHeight - 24) / 22)));
            if (visible < 1) { notebookMenu = ""; return; }
            notebookMenuVisible = true;
            notebookMenuActions = actions;
            notebookMenuEnabled = actions.Select(action => action.Length > 0 && (!busy || action == "interrupt") &&
                (action != "interrupt" || busy) && (action != "undo" || nb.B("canUndo")) &&
                (action != "redo" || nb.B("canRedo")) && (action != "move-up" || cell > 0) &&
                (action != "move-down" || cell + 1 < count) && (action != "paste-cell" || nb.B("canPasteCell") && count < 128) &&
                (action != "duplicate-cell" && action != "add-above" && action != "add-below" && action != "add-markdown" || count < 128) &&
                (action != "run-above" || cells.Take(cell).Any(c => c.S("kind") == "code")) &&
                (action != "run-below" || cells.Skip(cell).Any(c => c.S("kind") == "code")) &&
                (action != "clear" || cells.Any(c => c.I("runs") > 0 || c.S("output").Length > 0))).ToArray();
            notebookMenuCell = cell;
            if (!notebookMenuEnabled[notebookMenuIndex])
                notebookMenuIndex = Math.Max(0, Array.IndexOf(notebookMenuEnabled, true));
            notebookMenuTop = Math.Max(notebookMenuIndex - visible + 1, Math.Min(notebookMenuIndex, notebookMenuTop));
            int menuH = visible * 22 + 23;
            AddNotebookHit(new Rect(left, top, panel, menuH), notebookMenu, "Закрыть меню");
            Rounded(left, top, panel, menuH, NotebookEditor, 5);
            for (int i = notebookMenuTop; i < Math.Min(labels.Length, notebookMenuTop + visible); i++)
            {
                double row = top + 4 + (i - notebookMenuTop) * 22;
                bool enabled = notebookMenuEnabled[i];
                if (i == notebookMenuIndex && enabled) Rounded(left + 3, row, panel - 6, 21, CodeSelection, 3);
                Text(labels[i], left + 10, row + 2, 12, enabled ? CodeForeground : NotebookMuted, panel - 18);
                if (enabled)
                    AddNotebookHit(new Rect(left + 4, row, panel - 8, 22), actions[i], labels[i], NotebookMenuCellFor(actions[i], cell));
            }
            Text("↑↓   Enter   Esc  ·  " + (notebookMenuIndex + 1) + "/" + actions.Length,
                left + 10, top + menuH - 18, 10, NotebookMuted, panel - 18);
        }

        private void DrawNotebookCell(NotebookPaintCell rowData, int index, int selected, bool bodyFocused, bool interactive,
            bool busy, Rect clip, double left, double lineX, double end, double top, double usable, double xScroll)
        {
            Data cell = rowData.Cell;
            bool chosen = selected == index, editing = rowData.Editing && bodyFocused;
            string kind = rowData.Code ? "Python" : "Markdown";
            double width = Math.Max(1, end - lineX + 13), contentX = lineX - 7, codeTop = top + 8;
            if (chosen) Rounded(left, top + 2, 3, rowData.SourceHeight + rowData.OutputHeight - 2,
                bodyFocused ? CodeCaret : NotebookLineBorder, 1.5);
            if (rowData.Code || rowData.Editing)
            {
                Rounded(contentX, top + 1, width, rowData.SourceHeight - 1, NotebookEditor, 4);
                context.DrawRoundedRectangle(null, new Pen(Ui.Brush(editing ? CodeCaret : NotebookLineBorder), 1),
                    new Rect(contentX + .5, top + 1.5, Math.Max(1, width - 1), Math.Max(1, rowData.SourceHeight - 2)), 4, 4);
            }
            if (rowData.Code)
            {
                double runx = left + 9;
                Text("▷", runx, top + 4, 15, busy ? NotebookMuted : CodeForeground, 22);
                if (interactive && !busy)
                    AddNotebookHit(Rect.Intersect(new Rect(runx - 1, top + 2, contentX - runx, 26), clip),
                        "run", "Выполнить ячейку " + (index + 1), index);
                Text("[" + (cell.I("runs") > 0 ? cell.S("runs") : " ") + "]", left + 8,
                    top + rowData.SourceHeight - 20, 11, NotebookMuted, contentX - left - 9, 1, true);
            }
            else if (rowData.Editing) Text("M", left + 10, top + 7, 12, NotebookMuted, 20);
            if (!rowData.Code && !rowData.Editing)
            {
                bool fenced = false;
                double lineY = codeTop;
                foreach (string raw in rowData.Source)
                {
                    double height = MarkdownLineHeight(raw);
                    if (lineY + height >= clip.Top && lineY < clip.Bottom)
                        DrawNotebookMarkdown(raw, lineX, lineY, usable, ref fenced);
                    else if (raw.TrimStart().StartsWith(new string((char)96, 3), StringComparison.Ordinal))
                        fenced = !fenced;
                    lineY += height;
                }
            }
            else
            {
                int skip = Math.Min(rowData.Source.Length, Math.Max(0, (int)((clip.Top - codeTop - NotebookLine) / NotebookLine)));
                int max = Math.Min(rowData.Source.Length, 1 + Math.Max(0, (int)((clip.Bottom - codeTop) / NotebookLine)));
                int offset = rowData.Source.Take(skip).Sum(s => s.Length + 1);
                var sourceField = new Data(new Dictionary<string, object> { {"bodyCursor", rowData.Caret}, {"bodyAnchor", rowData.Anchor} });
                var syntax = cell.Items("syntax").ToArray();
                for (int n = skip; n < max; n++)
                {
                    string line = rowData.Source[n].TrimEnd('\r');
                    double lineY = codeTop + n * NotebookLine;
                    DrawStyledCodeLine(line, sourceField, syntax, lineX, lineY, usable, offset, rowData.Caret, rowData.Anchor,
                        editing, n + 1 < rowData.Source.Length, xScroll);
                    offset += rowData.Source[n].Length + 1;
                }
                Text(kind, end - 85, top + rowData.SourceHeight - 18, 10, NotebookMuted, 84);
            }
            if (rowData.Output.Length > 0)
            {
                double outputY = top + rowData.SourceHeight + 5;
                Rounded(contentX, outputY, width, rowData.OutputHeight - 2, cell.B("failed") ? "#25191D" : "#11161D", 3);
                string label = cell.S("outputLabel", "Вывод");
                Text(cell.B("failed") ? label + " · ошибка" : label, lineX, outputY + 3, 11,
                    cell.B("failed") ? NotebookError : NotebookMuted, usable);
                for (int j = Math.Max(0, (int)((clip.Top - outputY - 39) / NotebookLine));
                        j < rowData.Output.Length && outputY + 22 + j * NotebookLine < clip.Bottom; j++)
                {
                    var lineY = outputY + 21 + j * NotebookLine;
                    context.PushClip(new RectangleGeometry(new Rect(lineX, lineY, usable, NotebookLine)));
                    using (var line = new ChromeTextLine(rowData.Output[j], mono, 13, Ui.Brush(cell.B("failed") ? NotebookError : CodeForeground),
                        VisualTreeHelper.GetDpi(this).PixelsPerDip))
                        line.Draw(context, new Point(lineX - xScroll, lineY));
                    context.Pop();
                }
            }
        }

        private static int MarkdownHeading(string value)
        {
            value = value.TrimStart();
            int marks = value.TakeWhile(c => c == '#').Count();
            return marks > 0 && marks <= 6 && value.Length > marks && value[marks] == ' ' ? marks : 0;
        }

        private static double MarkdownLineHeight(string raw)
        {
            int heading = MarkdownHeading(raw);
            return heading == 1 ? 43 : heading == 2 ? 35 : heading > 0 ? 27 : NotebookLine;
        }

        private void DrawNotebookMarkdown(string raw, double x, double y, double w, ref bool fenced)
        {
            string value = raw.TrimStart();
            string fence = new string((char)96, 3);
            if (value.StartsWith(fence, StringComparison.Ordinal)) { fenced = !fenced; return; }
            if (fenced) { Text(raw, x + 7, y, 13, "#A5D6FF", w - 7, 1, true); return; }
            int marks = MarkdownHeading(value);
            if (marks > 0)
            {
                var label = new FormattedText(value.Substring(marks + 1), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    LiveUiFont.Typeface(FontWeights.SemiBold), marks == 1 ? 31 : marks == 2 ? 25 : 18,
                    Ui.Brush(CodeForeground), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                label.MaxTextWidth = Math.Max(1, w);
                label.MaxLineCount = 1;
                label.Trimming = TextTrimming.CharacterEllipsis;
                context.DrawText(label, new Point(x, y - 1));
                return;
            }
            if (value.StartsWith("> ", StringComparison.Ordinal))
            { Rectangle(x, y + 2, 2, NotebookLine - 3, NotebookLineBorder); Text(value.Substring(2), x + 9, y, 13, NotebookMuted, w - 10); return; }
            if (value.StartsWith("- ", StringComparison.Ordinal) || value.StartsWith("* ", StringComparison.Ordinal))
                value = "•  " + value.Substring(2);
            Text(value, x, y, 13, CodeForeground, w);
        }
    }
}
