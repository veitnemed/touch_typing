using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal sealed record DebugDayFixture(string Folder, string Caller, string Module, string Function, int Sample,
    char Operator, int Fault, int Correct, decimal Expected, string Document, string Selector, string Report)
{
    internal string Root => "/home/student/project/" + Folder;
    internal string Source => Root + "/src/" + Module;
    internal string Call => Function + "(" + Sample.ToString(CultureInfo.InvariantCulture) + ")";
    internal string Title => "Справка: " + Document;
    internal string RevisedLine => "    return value " + Operator + " " + Correct.ToString(CultureInfo.InvariantCulture);
    internal string Code(int factor) => "def " + Function + "(value):\n    return value " + Operator + " " + factor.ToString(CultureInfo.InvariantCulture) + "\n";
    internal string Test => "python3 -m unittest " + Selector;
    internal string ReportPath => Root + "/" + Report;
    internal string PicturePath => Root + "/evidence.png";
    internal const string ImageLink = "![Результат профильного теста](evidence.png)";
    internal string OriginalReport => "# Отчёт об ошибке\n\nТест: " + Selector + "\n\n";
}

internal sealed partial class LabScene
{
    private DebugDayFixture debugDay;
    internal bool DebugDay => debugDay != null;
    internal string DebugRewrite => debugDay?.RevisedLine;
    internal string DebugDocumentation => debugDay?.Document;
    internal string DebugCallQuery => debugDay?.Call;
    internal string DebugTestCommand => debugDay?.Test;
    internal string DebugReportName => debugDay?.Report;
    internal string DebugCaptureImage => debugClipboard == Clipboard ? Terminal.ReadFile(debugDay?.PicturePath ?? "~/__no_debug_image") : null;
    private string debugClipboard;
    private string debugCaptureSource;
    private bool debugFixtureCapture; // used only by the compiler's separate trial clone

    internal void CloneDebugDay(LabScene source)
    {
        debugDay = source.debugDay; debugClipboard = source.debugClipboard;
        debugCaptureSource = source.debugCaptureSource;
    }

    internal IEnumerable<string> DebugProjectDocuments() => new[] { debugDay.ReportPath }.Concat(Terminal.Entries(debugDay.Root + "/src")
        .Where(path => !Terminal.DirectoryExists(path)));
    internal string DebugRelative(string path) => path[(debugDay.Root.Length + 1)..];

    internal void PrepareDebugDay(bool independent)
    {
        debugDay = independent
            ? new("debug-clock", "clock_app.py", "duration.py", "to_seconds", 4200, '/', 10, 1000, 4.2m,
                  "1000 milliseconds", "test_clock.DurationTests.test_to_seconds", "clock-report.md")
            : new("debug-billing", "billing.py", "factors.py", "price_in_cents", 45, '*', 1000, 100, 4500m,
                  "100 cents", "test_billing.PriceTests.test_price_in_cents", "billing-report.md");
        var fixture = debugDay;
        Terminal.CreateFolder(fixture.Root + "/src");
        string stem = fixture.Module[..^3];
        Terminal.StoreFile(fixture.Root + "/src/" + fixture.Caller,
            "from " + stem + " import " + fixture.Function + "\n\nresult = " + fixture.Call + "\n");
        Terminal.StoreFile(fixture.Source, fixture.Code(fixture.Fault));
        Terminal.StoreFile(fixture.Root + "/src/decoy.py", fixture.Code(fixture.Correct));
        Terminal.StoreFile(fixture.ReportPath, fixture.OriginalReport);
        foreach (var path in DebugProjectDocuments()) originalFiles[path] = Terminal.ReadFile(path);

        // Standard command syntax, but isolated virtual file + numeric fixture
        // only. No process or Python interpreter runs on the real host.
        Terminal.InstallUnitTest(fixture.Selector, terminal =>
        {
            string actualCode = terminal.ReadFile(fixture.Source) ?? "";
            var match = Regex.Match(actualCode, @"\Adef " + Regex.Escape(fixture.Function) +
                @"\(value\):\n[ \t]+return[ \t]+value[ \t]*" + Regex.Escape(fixture.Operator.ToString()) +
                @"[ \t]*(?<value>[0-9]+)[ \t]*\n?\z");
            if (!match.Success || !int.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int coefficient) || coefficient == 0)
                throw new ArgumentException("AssertionError: недоступно определение " + fixture.Function);
            decimal actual = fixture.Operator == '*' ? (decimal)fixture.Sample * coefficient : (decimal)fixture.Sample / coefficient;
            if (actual != fixture.Expected)
                throw new ArgumentException("AssertionError: " + fixture.Call + ": expected " +
                    fixture.Expected.ToString(CultureInfo.InvariantCulture) + ", got " + actual.ToString(CultureInfo.InvariantCulture));
            return "Ran 1 test · " + fixture.Call + " = " + actual.ToString(CultureInfo.InvariantCulture) + "\nOK";
        });
        Terminal.Execute("cd " + fixture.Root);
        Terminal.Execute(fixture.Test); // real failed result computed from the seeded buggy source

        Open("chrome");
        var browser = Active;
        Navigate(browser, "https://practice.local/debug/" + fixture.Folder);
        browser.Tab.Title = fixture.Title;
        browser.Tab.Body = fixture.Title + "\n\n" +
            (independent ? "There are 1000 milliseconds in one second. Divide by 1000." :
                "One currency unit is 100 cents. Multiply by 100.");
        browser.Tab.SavedBody = browser.Tab.Body;
        browser.State = "left";
        Open("code");
        Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0;
        OpenDocument(fixture.ReportPath);
        Active.State = "right";
        Open("terminal");
    }

    internal void PrepareCompilerDebugCapture() => debugFixtureCapture = true;

    internal bool DebugTestCurrent => DebugDay && Terminal.LastSuccess && Terminal.LastCommand == debugDay.Test &&
        Terminal.ReadFile(debugDay.Source) == debugDay.Code(debugDay.Correct);

    internal bool? ApplyDebugDayAction(string action, LabWindow active)
    {
        if (!DebugDay) return null;
        switch (action)
        {
            case "debug-next-line":
                return active?.App == "code" && active.Focus == "body" && active.Url == debugDay.Source && Text("Down", "");
            case "debug-report-end":
                return active?.App == "code" && active.Focus == "body" && active.Url == debugDay.ReportPath && Text("End", "", word: true);
            case "debug-window-shot":
                if (active?.App != "terminal" || !DebugTestCurrent) return false;
                string supplied = captureImage;
                captureImage = null;
                // The compiler runs its own detached trial. A learner or
                // protocol client must provide image bytes; no fallback here.
                if (debugFixtureCapture) supplied = DeskThumbnail();
                if (!ValidDebugPng(supplied)) return false;
                debugClipboard = Clipboard = "debug-window.png";
                debugCaptureSource = Terminal.ReadFile(debugDay.Source);
                Terminal.StoreFile(debugDay.PicturePath, supplied);
                active.Status = "Снимок активного терминала скопирован";
                Screenshots++;
                return true;
            case "paste" when Clipboard == debugClipboard:
                if (active?.App != "code" || active.Focus != "body" || active.Url != debugDay.ReportPath ||
                    active.Body.Contains(DebugDayFixture.ImageLink, StringComparison.Ordinal) ||
                    debugCaptureSource != Terminal.ReadFile(debugDay.Source) ||
                    !ValidDebugPng(Terminal.ReadFile(debugDay.PicturePath))) return false;
                Insert(active, DebugDayFixture.ImageLink + "\n");
                active.Status = "В отчёт вставлен снимок терминала";
                return true;
            default: return null;
        }
    }

    private static bool ValidDebugPng(string tagged)
    {
        const string png = "data:image/png;base64,";
        if (tagged is not { Length: < 1400050 } || !tagged.StartsWith(png, StringComparison.Ordinal)) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(tagged[png.Length..]); }
        catch (FormatException) { return false; }
        if (bytes.Length < 64 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) || !bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8)) return false;
        int w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)), h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        return w is >= 16 and <= 1600 && h is >= 16 and <= 1600;
    }

    internal bool DebugReportValid => DebugDay &&
        debugCaptureSource == debugDay.Code(debugDay.Correct) && debugCaptureSource == Terminal.ReadFile(debugDay.Source) &&
        Terminal.ReadFile(debugDay.ReportPath) == debugDay.OriginalReport + DebugDayFixture.ImageLink + "\n" &&
        ValidDebugPng(Terminal.ReadFile(debugDay.PicturePath));

    internal object DebugEvidence(string action)
    {
        var w = Active;
        return action switch
        {
            "debug-doc-submit" => new { w?.Url, w?.Find, caret = w?.Tab?.Position.Caret, anchor = w?.Tab?.Position.Anchor },
            "debug-next-line" or "debug-rewrite" or "debug-report-end" => new { w?.Url, w?.Body, w?.Cursor, w?.Anchor },
            "debug-window-shot" => new { terminal = w?.App == "terminal", debugClipboard, ready = ValidDebugPng(DebugCaptureImage), debugCaptureSource },
            _ => null
        };
    }

    internal string DebugReportArtifact(string relative) => relative == debugDay?.Report ? debugDay.ReportPath : null;
}

internal static partial class LiveCourse
{
    private static void WorkdayDebugMissions(List<LiveMission> list)
    {
        foreach (bool independent in new[] { false, true })
        {
            string variant = independent ? "independent" : "guided";
            string title = independent ? "Самостоятельно" : "С подсказками";
            const string prefix = "Разобрать ошибку без мыши";
            var phases = new[]
            {
                new LivePhase("По сообщению теста найдите вызов и его определение",
                    "open-code project-search debug-project-query project-search-open code-definition"),
                new LivePhase("Сверьтесь со справкой Chrome, исправьте и сохраните исходник",
                    "open-chrome find debug-doc-submit escape open-code debug-next-line select-line debug-rewrite save"),
                new LivePhase("Запустите нужный тест и снимите окно терминала",
                    "open-terminal debug-unit debug-window-shot"),
                new LivePhase("Приложите снимок к отчёту и сохраните его",
                    "open-code quick-open quick:" + (independent ? "clock-report.md" : "billing-report.md") +
                    " quick-choose debug-report-end paste save"),
                new LivePhase("Проверьте сохранённый отчёт", "open-terminal debug-report-check"),
            };
            list.Add(new LiveMission("workday", 1, "workday-debug-" + variant, prefix + " · " + title.ToLowerInvariant(), phases,
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareDebugDay(independent)));
        }
    }
}
