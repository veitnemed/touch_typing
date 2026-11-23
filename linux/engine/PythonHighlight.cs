using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

// Offsets refer to the live UTF-16 buffer, not visual columns. Colors are solely
// a client concern. No save, parse, compile, or formatter changes the document.
internal static class PythonHighlight
{
    internal sealed record Span(int Start, int End, string Kind);
    private sealed record Result(Span[] Spans);
    private static readonly ConditionalWeakTable<string, Result> Cache = new();
    private static readonly HashSet<string> Keywords = new(("and as assert async await break case class continue def del elif else except finally for from global if import in is lambda match nonlocal not or pass raise return try while with yield").Split(' '));
    private static readonly HashSet<string> Constants = ["True", "False", "None"];
    private static readonly HashSet<string> Prefixes = new(["r", "b", "u", "f", "br", "rb", "fr", "rf"], System.StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Number = new(@"\G(?:0[xX](?:_?[0-9A-Fa-f])+|0[bB](?:_?[01])+|0[oO](?:_?[0-7])+|(?:[0-9](?:_?[0-9])*(?:\.(?:[0-9](?:_?[0-9])*)?)?|\.[0-9](?:_?[0-9])*)(?:[eE][+-]?[0-9](?:_?[0-9])*)?[jJ]?)", RegexOptions.CultureInvariant, System.TimeSpan.FromMilliseconds(100));
    internal static Span[] For(string body, string url) =>
        string.IsNullOrEmpty(body) || !url.EndsWith(".py", System.StringComparison.OrdinalIgnoreCase) ?
        [] : Cache.GetValue(body, source => new(new Lexer(source).Read())).Spans;

    private sealed class Lexer(string text)
    {
        private readonly List<Span> spans = [];
        private int i;
        private char At(int index) => index < text.Length ? text[index] : '\0';
        private bool AtQuote(int index) => At(index) is '\'' or '"';
        private static bool Digit(char c) => c is >= '0' and <= '9';
        private int WordWidth(int index, bool first)
        {
            if (index >= text.Length) return 0;
            if (At(index) == '_') return 1;
            if (!Rune.TryGetRuneAt(text, index, out var rune)) return 0;
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetter(rune) || category == UnicodeCategory.LetterNumber ||
                !first && (Rune.IsDigit(rune) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation))
                return rune.Utf16SequenceLength;
            return 0;
        }
        private void Add(int start, int end, string kind)
        {
            if (end > start) spans.Add(new(start, end, kind));
        }
        internal Span[] Read()
        {
            Code(false, 0);
            return [.. spans];
        }
        private void Code(bool expression, int recursion)
        {
            string prior = "";
            int nesting = 0;
            while (i < text.Length)
            {
                char c = At(i);
                if (expression && nesting == 0 && c is '}' or ':') return;
                if (expression && nesting == 0 && c == '!' && At(i + 1) != '=') return;
                if (c == '#')
                {
                    int start = i++;
                    while (i < text.Length && At(i) is not ('\n' or '\r')) i++;
                    Add(start, i, "comment");
                    continue;
                }
                if (AtQuote(i))
                {
                    String(i, false, recursion);
                    prior = "";
                    continue;
                }
                int width = WordWidth(i, true);
                if (width > 0)
                {
                    int start = i;
                    i += width;
                    while ((width = WordWidth(i, false)) > 0) i += width;
                    string word = text[start..i];
                    if (AtQuote(i) && Prefixes.Contains(word))
                    {
                        String(start, word.Contains('f') || word.Contains('F'), recursion);
                        prior = "";
                        continue;
                    }
                    int next = i;
                    while (At(next) is ' ' or '\t') next++;
                    if (Keywords.Contains(word)) Add(start, i, "keyword");
                    else if (Constants.Contains(word)) Add(start, i, "number");
                    else if (prior == "def" || prior == "class" || At(next) == '(') Add(start, i, "function");
                    else if (At(next) == '=' && At(next + 1) != '=') Add(start, i, "variable");
                    prior = word;
                    continue;
                }
                if (Digit(c) || c == '.' && Digit(At(i + 1)))
                {
                    var match = Number.Match(text, i);
                    if (match.Success && match.Index == i)
                    {
                        Add(i, i + match.Length, "number");
                        i += match.Length;
                        prior = "";
                        continue;
                    }
                }
                if (c is '(' or '[' or '{') nesting++;
                else if ((c is ')' or ']' or '}') && nesting > 0) nesting--;
                if ("+-*/%@=<>!&|^~".Contains(c)) Add(i, i + 1, "operator");
                if (c is '\n' or '\r' || !char.IsWhiteSpace(c)) prior = "";
                i += char.IsHighSurrogate(c) && char.IsLowSurrogate(At(i + 1)) ? 2 : 1;
            }
        }
        private void String(int beginning, bool formatted, int recursion)
        {
            char delimiter = At(i);
            bool triple = At(i + 1) == delimiter && At(i + 2) == delimiter;
            int size = triple ? 3 : 1;
            int start = beginning;
            i += size;
            while (i < text.Length)
            {
                char current = At(i);
                if (current == delimiter && (!triple || At(i + 1) == delimiter && At(i + 2) == delimiter))
                {
                    i += size;
                    break;
                }
                if (current is '\n' or '\r' && !triple) break;
                if (current == '\\')
                {
                    i += At(i + 1) == '\r' && At(i + 2) == '\n' ? 3 : i + 1 < text.Length ? 2 : 1;
                    continue;
                }
                if (formatted && (current is '{' or '}') && At(i + 1) == current)
                {
                    i += 2;
                    continue;
                }
                if (formatted && current == '{' && recursion < 16)
                {
                    Add(start, i, "string"); Add(i, i + 1, "operator"); i++;
                    Replacement(recursion + 1);
                    start = i;
                    continue;
                }
                i++;
            }
            Add(start, i, "string");
        }
        private void Replacement(int recursion)
        {
            Code(true, recursion);
            if (At(i) == '!')
            {
                int start = i++;
                if (At(i) is 'r' or 's' or 'a') i++;
                Add(start, i, "operator");
            }
            if (At(i) == ':')
            {
                int start = i++;
                while (i < text.Length && At(i) != '}')
                {
                    if (At(i) == '{' && recursion < 16)
                    {
                        Add(start, i, "string"); Add(i, i + 1, "operator"); i++;
                        Replacement(recursion + 1);
                        start = i;
                    }
                    else i++;
                }
                Add(start, i, "string");
            }
            if (At(i) == '}') { Add(i, i + 1, "operator"); i++; }
        }
    }
}
