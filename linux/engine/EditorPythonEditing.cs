using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal bool ReplayLegacyPythonEditing, PythonEditingExercise;
    private const int PythonIndentWidth = 4;
    private static readonly Regex PythonSuite = new(
        @"^\s*(?:def|class|for|if|elif|else|while|try|with|finally|except|async)\b.*?:\s*$",
        RegexOptions.CultureInvariant);
    private static bool IsEditorBody(LabWindow w) => w?.App == "code" && w.Focus == "body" && w.Tab != null;
    private static bool IsPythonBody(LabWindow w) => IsEditorBody(w) &&
        w.Tab.Title.EndsWith(".py", StringComparison.OrdinalIgnoreCase);

    private string EditorNewLine(LabWindow w, int at)
    {
        // DebugDay's exact replacement target is already published with a
        // bare LF. Keep that mission's reference artifact unchanged.
        if (ReplayLegacyPythonEditing || DebugDay || !IsPythonBody(w)) return "\n";
        string before = w.Body[LineStart(w.Body, at)..at];
        return "\n" + IndentPrefix(before) + (PythonSuite.IsMatch(before) ? new string(' ', PythonIndentWidth) : "");
    }

    private static string IndentPrefix(string source) =>
        source[..source.TakeWhile(ch => ch is ' ' or '\t').Count()];

    private static void ReplaceEditor(LabWindow w, string text, int anchor, int cursor)
    {
        Remember(w);
        w.Body = text;
        w.Tab.ExtraMatches.Clear();
        w.Select(anchor, cursor);
    }

    private static bool EditIndent(LabWindow w, bool outdent)
    {
        string source = w.Body;
        if (!w.Selected && !outdent)
        {
            int column = w.Cursor - LineStart(source, w.Cursor);
            int count = PythonIndentWidth - column % PythonIndentWidth;
            ReplaceEditor(w, source.Insert(w.Cursor, new string(' ', count)), w.Cursor + count, w.Cursor + count);
            return true;
        }
        var (first, last) = AffectedLines(w);
        var edits = new List<(int At, int Remove)>();
        for (int start = first; ; start = LineEnd(source, start) + 1)
        {
            int count = 0;
            if (outdent)
                while (count < PythonIndentWidth && start + count < source.Length && source[start + count] is ' ' or '\t')
                {
                    if (source[start + count++] == '\t') break;
                }
            if (!outdent || count != 0) edits.Add((start, count));
            if (start == last) break;
        }
        if (edits.Count == 0) return false;
        int Map(int position)
        {
            int shift = 0;
            foreach (var edit in edits)
            {
                // Keep a selection beginning at column 1 on the line edge.
                if (position < edit.At || position == edit.At && w.Selected && w.Start == position) break;
                if (outdent && position < edit.At + edit.Remove) return edit.At + shift;
                shift += outdent ? -edit.Remove : PythonIndentWidth;
            }
            return position + shift;
        }
        int anchor = Map(w.Anchor), cursor = Map(w.Cursor);
        foreach (var edit in edits.AsEnumerable().Reverse())
            source = outdent ? source.Remove(edit.At, edit.Remove) : source.Insert(edit.At, new string(' ', PythonIndentWidth));
        ReplaceEditor(w, source, anchor, cursor);
        return true;
    }

    private static bool MoveEditorBlock(LabWindow w, bool up)
    {
        string source = w.Body;
        var (first, last) = AffectedLines(w);
        int end = LineEnd(source, last);
        if (up && first == 0 || !up && end == source.Length) return false;
        int from, to, shift;
        // Swap line payloads, not their terminating newlines, so a final line
        // without a LF keeps this property after moving either direction.
        string result;
        bool follows = w.Selected && w.End == end + 1;
        if (up)
        {
            from = LineStart(source, first - 1);
            to = end;
            shift = from - first;
            result = source[..from] + source[first..end] + "\n" + source[from..(first - 1)] + source[to..];
        }
        else
        {
            from = first;
            int next = end + 1;
            to = LineEnd(source, next);
            shift = to - next + 1;
            result = source[..from] + source[next..to] + "\n" + source[first..end] + source[to..];
        }
        int Map(int position) => follows && position == w.End
            ? Math.Min(result.Length, end + shift + 1) : position + shift;
        ReplaceEditor(w, result, Map(w.Anchor), Map(w.Cursor));
        return true;
    }

    private static void RemoveEditorWord(LabWindow w, bool left)
    {
        string source = w.Body;
        int start = w.Start, end = w.End;
        if (!w.Selected)
        {
            int target = left ? EditorWordEdge(source, w.Cursor, true) : EditorDeleteRightEdge(source, w.Cursor);
            start = Math.Min(target, w.Cursor);
            end = Math.Max(target, w.Cursor);
        }
        if (start != end)
        {
            string result = source.Remove(start, end - start);
            int at = InsertionBoundary(result, start);
            ReplaceEditor(w, result, at, at);
        }
    }

    // VS Code's word separators do not include underscore; Python names
    // such as unit_price should remain one word. Navigation never splits a
    // combining character, surrogate or joined emoji.
    private static int EditorWordEdge(string source, int caret, bool left)
    {
        int lineEdge = left ? LineStart(source, caret) : LineEnd(source, caret);
        if (caret == lineEdge) return left ? PreviousBoundary(source, caret) : NextBoundary(source, caret);
        int[] units = StringInfo.ParseCombiningCharacters(source);
        int i = ElementAtOrBefore(units, source.Length, caret);
        int Category(int at) =>
            char.IsWhiteSpace(source[units[at]]) ? 0 :
            "`~!@#$%^&*()-=+[{]}\\|;:'\",.<>/?".Contains(source[units[at]]) ? 2 : 1;
        if (left)
        {
            while (i > 0 && units[i - 1] >= lineEdge && Category(i - 1) == 0) i--;
            int kind = i > 0 ? Category(i - 1) : 0;
            while (i > 0 && units[i - 1] >= lineEdge && Category(i - 1) == kind) i--;
        }
        else
        {
            while (i < units.Length && units[i] < lineEdge && Category(i) == 0) i++;
            int kind = i < units.Length ? Category(i) : 0;
            while (i < units.Length && units[i] < lineEdge && Category(i) == kind) i++;
        }
        return i == units.Length ? source.Length : units[i];
    }

    private static int EditorDeleteRightEdge(string source, int caret)
    {
        int last = LineEnd(source, caret);
        if (caret == last) return NextBoundary(source, caret);
        int start = caret;
        while (start < last && char.IsWhiteSpace(source[start])) start++;
        // Ctrl+Delete removes to the word end, not the next word start.
        if (start == last) return start;
        int end = EditorWordEdge(source, start, false);
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        return end;
    }

    private bool InsertEditorLine(LabWindow w, bool above)
    {
        string source = w.Body;
        int start = LineStart(source, w.Cursor), end = LineEnd(source, w.Cursor);
        int at = above ? start : end;
        string text = above ? IndentPrefix(source[start..end]) + "\n" : EditorNewLine(w, end);
        int caret = at + text.Length - (above ? 1 : 0);
        ReplaceEditor(w, source.Insert(at, text), caret, caret);
        return true;
    }

    internal bool PerformPythonEdit(string action)
    {
        var w = Active;
        if (!IsEditorBody(w) || !action.StartsWith("python-edit-", StringComparison.Ordinal)) return false;
        switch (action["python-edit-".Length..])
        {
            case "write":
                foreach (char c in PythonEditStatement)
                    if (!Text(c.ToString().ToUpperInvariant(), c.ToString())) return false;
                return true;
            case "newline": return Text("Enter", "");
            case "indent": return EditIndent(w, false);
            case "outdent": return EditIndent(w, true);
            case "word-left": return Text("Left", "", word: true);
            case "word-right": return Text("Right", "", word: true);
            case "select-word-left": return Text("Left", "", extend: true, word: true);
            case "select-word-right": return Text("Right", "", extend: true, word: true);
            case "erase-left": RemoveEditorWord(w, true); return true;
            case "erase-right": RemoveEditorWord(w, false); return true;
            case "insert-below": return InsertEditorLine(w, false);
            case "insert-above": return InsertEditorLine(w, true);
            case "up": return Text("Up", "");
            case "left": return Text("Left", "");
            case "home": return Text("Home", "");
            case "end": return Text("End", "");
            case "select-down": return Text("Down", "", extend: true);
            default: return false;
        }
    }
}
