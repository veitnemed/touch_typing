using System;
using System.Linq;
using TouchTypingStudio;

static void Require(bool ok, string why)
{
    if (!ok) throw new Exception(why);
}

string body = "def café(v):\r\n    \"\"\"# doc🙂\r\n    next\"\"\"\r\n    # ведущий🙂\r\n    value = f\"{{set}} # {len(v):>{2}}\"  # inline🙂\r\n    greeting = r'# inside'\r\n    total = .5 + 12e-3 + 0x_2a\r\n    return value";
var spans = PythonHighlight.For(body, "task-list/app.py");
bool Has(string kind, string token) => spans.Any(x => x.Kind == kind && body[x.Start..x.End] == token);
Require(Has("keyword", "def") && Has("keyword", "return"), "keywords");
Require(Has("function", "café") && Has("function", "len"), "function and replacement");
Require(Has("comment", "# ведущий🙂") && Has("comment", "# inline🙂"), "comments");
Require(!Has("comment", "# inside") && Has("string", "r'# inside'"), "literal hash");
Require(Has("string", "\"\"\"# doc🙂\r\n    next\"\"\""), "multiline quotes");
Require(Has("string", "f\"{{set}} # ") && Has("operator", "{") && Has("number", "2"), "f-string escapes and nested format");
Require(Has("number", ".5") && Has("number", "12e-3") && Has("number", "0x_2a"), "Python numeric forms");
Require(Has("variable", "greeting") && Has("operator", "+"), "assignment and operator");
Require(spans.Zip(spans.Skip(1)).All(pair => pair.First.End <= pair.Second.Start)
        && spans.All(x => x.Start >= 0 && x.End <= body.Length && x.Start < x.End
             && !char.IsLowSurrogate(body[x.Start]) && (x.End == body.Length || !char.IsLowSurrogate(body[x.End]))),
        "sorted exclusive UTF-16 intervals");
Require(PythonHighlight.For(body, "notes.md").Length == 0, "do not color plain report");
string edited = body.Replace("12e-3", "'# live edit'").Replace("# inline", "# changed");
var editedSpans = PythonHighlight.For(edited, "task-list/app.py");
Require(editedSpans.Any(x => x.Kind == "string" && edited[x.Start..x.End] == "'# live edit'")
        && editedSpans.Any(x => x.Kind == "comment" && edited[x.Start..x.End] == "# changed🙂"), "retokenize editor changes");
foreach (string unfinished in new[] { "x", "f\"{", "'\\", "f\"{x!q", "f\"{x:{", "\"a\n", "🙂", "𝐱=1" })
    Require(PythonHighlight.For(unfinished, "a.py").All(x => x.End <= unfinished.Length), "incomplete editor state: " + unfinished);
Console.WriteLine($"PASS {spans.Length} Python UTF-16 intervals: CRLF, escaped/nested f-strings, Unicode, numbers and live edits");
