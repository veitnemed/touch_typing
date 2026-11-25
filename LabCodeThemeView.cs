using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        // Primer GitHub Dark Default: primer/github-vscode-theme, src/theme.js.
        private const string CodeCanvas = "#0D1117", CodeInset = "#010409", CodeBorder = "#30363D";
        private const string CodeForeground = "#E6EDF3", CodeMuted = "#7D8590", CodeAccent = "#F78166";
        private const string CodeGutter = "#6E7681", CodeExplorerSelection = "#666E7681";
        private const string CodeActiveLine = "#1A6E7681", CodeCaret = "#2F81F7";
        // No Primer override: inherited VS Code dark selection and its half-alpha inactive state.
        private const string CodeSelection = "#264F78", CodeInactiveSelection = "#80264F78";
        private const double CodeFontSize = 14;

        private static string CodeRelativePath(Data window)
        {
            string relative = window.S("projectRelative").Trim('/');
            if (relative.Length > 0) return relative;
            string url = window.S("url").Replace('\\', '/').Trim('/');
            return url.Split('/').Last();
        }

        private static string CodeProjectName(Data window)
        {
            string project = window.S("projectName");
            return project.Length > 0 ? project : "EXPLORER";
        }

        private void DrawCodeTitle(Data window, double width)
        {
            Rectangle(0, 0, width, 29, CodeCanvas);
            Rectangle(0, 28, width, 1, CodeBorder);
            Icon("code", 8, 6, 16);
            if (width >= 790)
                Text("File   Edit   Selection   View   Go   Run   Terminal   Help", 34, 6, 12, CodeForeground, 375);
            double fieldWidth = Math.Min(380, width < 790 ? width * .5 : width * .36);
            double fieldX = (width - fieldWidth) / 2 + (width >= 790 ? 65 : 0);
            Rounded(fieldX, 4, fieldWidth, 21, CodeActiveLine, 5);
            string title = CodeProjectName(window);
            if (title == "EXPLORER") title = "Visual Studio Code";
            Text("⌕   " + title, fieldX + 10, 6, 12, CodeMuted, fieldWidth - 18);
            Text("−       □       ×", width - 98, 5, 13, CodeMuted, 96);
        }

        private void DrawCodeActivity(Data window, double width, double h)
        {
            Rectangle(0, 29, width, h - 50, CodeCanvas);
            Rectangle(width - 1, 29, 1, h - 50, CodeBorder);
            string[] glyphs = { "▤", "⌕", "⑂", "▷", "▦" };
            string view = window.S("view");
            int current = view == "project-search" ? 1 : view == "changes" ? 2 : view == "extensions" ? 4 : 0;
            for (int index = 0; index < glyphs.Length && 39 + index * 44 < h - 66; index++)
            {
                double y = 29 + index * 44;
                if (current == index && window.B("sidebar", true)) Rectangle(0, y, 2, 44, CodeForeground);
                Text(glyphs[index], 12, y + 9, 23, current == index ? CodeForeground : CodeMuted, 29);
            }
            if (h > 270) Text("◉", 12, h - 93, 22, CodeMuted, 27);
            Text("⚙", 12, h - 54, 22, CodeMuted, 27);
        }

        private void DrawCodeSidebar(Data window, double x, double width, double h)
        {
            Rectangle(x, 29, width, h - 50, CodeInset);
            Rectangle(x + width - 1, 29, 1, h - 50, CodeBorder);
            string view = window.S("view");
            string heading = view == "changes" ? "SOURCE CONTROL" : view == "project-search" ? "SEARCH" :
                view == "extensions" ? "EXTENSIONS" : "EXPLORER";
            Text(heading, x + 16, 41, 11, CodeForeground, width - 40);
            Text("···", x + width - 27, 39, 15, CodeMuted, 20);
            bool projectView = view != "changes" && view != "project-search" && view != "extensions";
            if (projectView && window.B("explorerActions"))
            {
                DrawWorkingExplorer(window, x, width, h);
                return;
            }
            if (projectView)
                Text("⌄   " + CodeProjectName(window).ToUpperInvariant(), x + 6, 65, 12, CodeForeground, width - 12);
            var names = view == "extensions" ? new[] { "Python", "Pylance" } :
                window.Items(view == "changes" ? "changedFiles" : view == "project-search" ? "searchResults" : "projectFiles").Select(item => item.Value()).ToArray();
            string active = CodeRelativePath(window);
            double row = projectView ? 85 : 68;
            if (projectView && (window.S("focus") == "explorer" || window.S("focus") == "file-name"))
            {
                var entries = window.Items("entries").ToArray();
                var expanded = new HashSet<string>(window.Items("expandedFolders").Select(item => item.Value()), StringComparer.Ordinal);
                int selected = window.I("item", -1), capacity = Math.Max(1, (int)((h - 39 - row)/22));
                int first = Math.Max(0, selected-capacity+1);
                for (int i = first; i < Math.Min(entries.Length, first+capacity); i++, row += 22)
                {
                    var entry = entries[i];
                    string path = entry.S("path");
                    int depth = Math.Max(0, path.Count(c => c == '/') - window.I("projectRootDepth", 3) - 1);
                    double left = x + 8 + depth * 13;
                    if (i == selected) Rectangle(x+1, row, width-3, 22, CodeSelection);
                    Text(entry.S("kind") == "folder" ? expanded.Contains(path) ? "⌄" : "›" : "▤",
                        left, row+2, 12, CodeMuted, 16);
                    left += 16;
                    double textWidth = Math.Max(1, x + width - left - 5);
                    if (i == selected && window.S("focus") == "file-name")
                    {
                        Rounded(left-2, row+1, textWidth, 20, CodeCanvas, 2);
                        Field(window.S("input"), window, left, row+2, textWidth-2, 12, CodeForeground, 0, true, face: regular);
                    }
                    else Text(entry.S("title"), left, row+2, 12, CodeForeground, textWidth);
                }
                return;
            }
            var shownFolders = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in names)
            {
                if (row >= h - 39) break;
                string[] path = projectView ? value.Trim('/').Split('/') : new[] { value };
                string folder = "";
                for (int depth = 0; depth < path.Length - 1 && row < h - 39; depth++)
                {
                    folder += "/" + path[depth];
                    if (!shownFolders.Add(folder)) continue;
                    double offset = x + 14 + depth * 13;
                    Text("⌄   " + path[depth], offset, row + 2, 12, CodeForeground, x + width - offset - 6);
                    row += 22;
                }
                if (row >= h - 39) break;
                string file = path[path.Length - 1];
                bool selected = projectView && (value == active || path.Length == 1 && active.Split('/').Last() == file);
                if (selected) Rectangle(x, row, width - 1, 22, CodeExplorerSelection);
                double itemX = x + 15 + Math.Min(5, path.Length - 1) * 13;
                DrawCodeFileIcon(file, itemX, row + 3);
                Text(file, itemX + 17, row + 2, 13, selected ? CodeForeground : CodeMuted, x + width - itemX - 20);
                row += 22;
            }
        }

        private void DrawCodeFileIcon(string file, double x, double y)
        {
            if (file.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                // Small two-tone Python mark, instead of a textual placeholder prefix.
                Rounded(x + 1, y + 1, 9, 7, "#58A6FF", 2);
                Rounded(x + 4, y + 7, 9, 7, "#E3B341", 2);
                Rectangle(x + 6, y + 3, 1, 1, CodeCanvas);
                Rectangle(x + 9, y + 11, 1, 1, CodeCanvas);
            }
            else if (file.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase))
            {
                var ring = new Pen(Ui.Brush("#F4A261"), 1.2);
                context.DrawEllipse(null, ring, new Point(x + 7, y + 7), 5.5, 4);
                Rectangle(x + 11, y + 1, 2, 2, "#F4A261");
                Rectangle(x + 2, y + 11, 2, 2, "#B1BAC4");
            }
            else Text("▪", x + 2, y - 2, 14, CodeMuted, 12);
        }

        private void DrawCodeStatus(Data window, double w, double h, int line, int column, string file)
        {
            Rectangle(0, h - 22, w, 22, CodeCanvas);
            Rectangle(0, h - 22, w, 1, CodeBorder);
            string language = file.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? "Python" :
                file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "Markdown" : "Plain Text";
            double rightWidth = Math.Min(w * .64, 345);
            Text("⑂ main   ⓧ " + scene.I("diagnosticProblemCount") + "   ⚠ 0",
                12, h - 19, 12, CodeMuted, w - rightWidth - 18);
            if (window.D("notebook").Exists)
            {
                Data nb = window.D("notebook");
                string note = "Ячейка " + (nb.I("selected") + 1) + "/" + nb.Items("cells").Count() + "   ·   " +
                    (window.S("focus") != "body" ? "Фокус вне блокнота" : nb.B("editing") ? "Редактирование" : "Выбор ячеек");
                Text(note, w - rightWidth, h - 19, 12, NotebookMuted, rightWidth - 6);
                return;
            }
            Text(string.Format(CultureInfo.InvariantCulture, "Ln {0}, Col {1}     Spaces: 4     UTF-8     LF     {2}", line, column, language),
                w - rightWidth, h - 19, 12, CodeMuted, rightWidth - 6);
        }

        private void CodeRightText(string value, double x, double y, double width, double size, string color)
        {
            var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                mono, size, Ui.Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            text.TextAlignment = TextAlignment.Right;
            text.MaxTextWidth = Math.Max(1, width);
            context.DrawText(text, new Point(x, y));
        }

        private static string CodeTokenColor(string kind)
        {
            switch (kind)
            {
                case "keyword": case "operator": return "#FF7B72";
                case "string": return "#A5D6FF";
                case "comment": return "#8B949E";
                case "number": return "#79C0FF";
                case "function": return "#D2A8FF";
                case "variable": return "#FFA657";
                default: return null;
            }
        }

        private void DrawStyledCodeLine(string value, Data window, Data[] syntax, double x, double y, double width,
            int offset, int caret, int anchor, bool focused, bool hasLineBreak, double manualScroll = -1)
        {
            if (width <= 0) return;
            int localCaret = caret - offset, localAnchor = anchor - offset;
            bool drawCaret = focused && localCaret >= 0 && localCaret <= value.Length;
            bool hasSelection = window.I("bodyAnchor") != window.I("bodyCursor");
            int low = Math.Min(localCaret, localAnchor), high = Math.Max(localCaret, localAnchor);
            bool selectBreak = hasSelection && hasLineBreak && low <= value.Length && high > value.Length;
            var colors = new TextEffectCollection();
            foreach (Data span in syntax)
            {
                int start = Math.Max(0, span.I("start") - offset);
                int end = Math.Min(value.Length, span.I("end") - offset);
                if (end <= start) continue;
                string color = CodeTokenColor(span.S("kind"));
                if (color != null) colors.Add(new TextEffect(null, Ui.Brush(color), null, start, end - start));
            }

            using (var line = new ChromeTextLine(value, mono, CodeFontSize, Ui.Brush(CodeForeground),
                VisualTreeHelper.GetDpi(this).PixelsPerDip, colors))
            {
                double cx = drawCaret ? line.Distance(localCaret) : 0;
                double breakWidth = Math.Ceiling(CodeFontSize * .6);
                double shift = manualScroll >= 0 ? manualScroll : Math.Max(0, cx - width + (selectBreak ? breakWidth + 2 : 3));
                var origin = new Point(x - shift, y);
                context.PushClip(new RectangleGeometry(new Rect(x, y, width, 20)));
                try
                {
                    int matchLength = Math.Abs(localAnchor - localCaret);
                    foreach (Data match in window.Items("matches"))
                    {
                        int mark = match.Integer() - offset;
                        DrawProportionalBounds(line, origin, mark, mark + matchLength, Ui.Brush("#8063461C"));
                    }
                    string selection = focused ? CodeSelection : CodeInactiveSelection;
                    if (hasSelection) DrawProportionalBounds(line, origin, low, high, Ui.Brush(selection));
                    if (selectBreak)
                        Rectangle(origin.X + line.WidthWithTrailingWhitespace, y, breakWidth, 17, selection);
                    line.Draw(context, origin);
                    if (drawCaret) Rectangle(origin.X + cx, y + 1, 1.5, 17, CodeCaret);
                }
                finally { context.Pop(); }
            }
        }
    }
}
