using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        // Render the document, not a second simulator; the data source is the
        // same nbformat-backed view as GTK, including output and selection.
        private void DrawNotebookPage(Data nb, double x, double y, double w, double h, bool active)
        {
            if (w < 54 || h < 24) return;
            Data[] cells = nb.Items("cells").ToArray();
            int selected = nb.I("selected");
            if (selected < 0 || selected >= cells.Length) return;
            int[] heights = cells.Select(c => Math.Min(3, c.S("source").Split('\n').Length) * 19 +
                (c.S("output").Length > 0 ? 37 : 13)).ToArray();
            int first = selected, used = heights[first];
            while (first > 0 && used + heights[first - 1] <= h - 24) used += heights[--first];
            Text("JUPYTER   ·   Учебное ядро Python", x + 12, y, 11, CodeMuted, w - 20);
            double row = y + 24;
            context.PushClip(new RectangleGeometry(new Rect(x, row, w, Math.Max(1, h - 24))));
            try
            {
                for (int i = first; i < cells.Length && row < y + h; i++)
                {
                    Data cell = cells[i];
                    bool chosen = i == selected, editing = active && chosen && nb.B("editing");
                    int height = heights[i];
                    Rounded(x + 6, row, w - 12, height - 3, chosen ? "#171B22" : CodeCanvas, 3);
                    if (chosen) Rectangle(x + 6, row + 1, 3, height - 5, active ? CodeCaret : CodeBorder);
                    if (editing)
                    {
                        Rectangle(x + 6, row, w - 12, 1, CodeCaret);
                        Rectangle(x + w - 7, row, 1, height - 3, CodeCaret);
                        Rectangle(x + 6, row + height - 4, w - 12, 1, CodeCaret);
                    }
                    bool code = cell.S("kind") == "code";
                    if (code) Text("[" + (cell.I("runs") > 0 ? cell.S("runs") : " ") + "]", x + 12, row + 4, 11, CodeMuted, 35, 1, true);
                    string src = cell.S("source");
                    string[] lines = src.Split('\n');
                    int caret = editing ? Math.Min(nb.I("caret"), src.Length) : 0, anchor = editing ? nb.I("anchor") : 0;
                    int begin = editing ? Math.Max(0, src.Substring(0, caret).Count(ch => ch == '\n') - 2) : 0;
                    int offset = lines.Take(begin).Sum(l => l.Length + 1);
                    var field = new Data(new Dictionary<string, object> { {"bodyCursor", caret}, {"bodyAnchor", anchor} });
                    Data[] syntax = cell.Items("syntax").ToArray();
                    for (int j = begin; j < Math.Min(lines.Length, begin + 3); j++)
                    {
                        double lineY = row + 3 + (j - begin) * 19;
                        if (!code && !editing) Text(lines[j].TrimStart('#', ' '), x + 16, lineY, 15, CodeForeground, w - 30);
                        else DrawStyledCodeLine(lines[j], field, syntax, x + 51, lineY, w - 67, offset, caret, anchor, editing, j + 1 < lines.Length);
                        offset += lines[j].Length + 1;
                    }
                    if (cell.S("output").Length != 0) Text(cell.S("outputLabel") + "   " + cell.S("output"), x + 51, row + height - 27,
                        13, cell.B("failed") ? "#FF7B72" : "#7EE787", w - 64, 1, true);
                    row += height;
                }
            }
            finally { context.Pop(); }
        }
    }
}
