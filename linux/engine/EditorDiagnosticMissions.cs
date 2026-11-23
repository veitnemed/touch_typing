using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static void EditorDiagnosticMissions(List<LiveMission> list) =>
        list.Add(new("code", 1, "code-diagnostic-test", "Исправить диагностику и запустить один тест",
            [
                new("Откройте диагностику и сравните два исправления", "code-problem-next code-fix-open code-fix-next code-fix-apply"),
                new("Сохраните исправление и проверьте дробную температуру", "save terminal-toggle cmd:python~-m~unittest~" + LabScene.DiagnosticTestSelector)
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareDiagnosticCase()));
}

internal sealed partial class LabScene
{
    private const string DiagnosticRoot = Project + "/diagnostics-lab";
    private const string DiagnosticSource = DiagnosticRoot + "/units.py";
    private const string DiagnosticTests = DiagnosticRoot + "/test_units.py";
    internal const string DiagnosticTestSelector = "test_units.UnitsTests.test_kelvin_to_celsius";
    private const string DiagnosticFunction = "def kelvin_to_celsius(kelvin):\n";
    private const string DiagnosticTestSource = "import unittest\nfrom units import kelvin_to_celsius\n\nclass UnitsTests(unittest.TestCase):\n    def test_kelvin_to_celsius(self):\n        for kelvin, expected in [(273.15, 0), (300.65, 27.5), (250.4, -22.75)]:\n            with self.subTest(kelvin=kelvin):\n                self.assertAlmostEqual(kelvin_to_celsius(kelvin), expected, places=8)\n";
    private const string Fault = "kelvin + 273.15";
    private static readonly string[] DiagnosticFixes = ["round(kelvin - 273.15)", "kelvin - 273.15"];
    private static readonly LabEntry[] DiagnosticChoices =
    [
        new("Вычесть 273,15 и округлить до целого", "round", "quick-fix"),
        new("Вычесть 273,15, сохранив дробную часть", "decimal", "quick-fix")
    ];
    private static readonly Regex DiagnosticReturn = new(
        @"\Adef kelvin_to_celsius\(kelvin\):\n    return (?<value>[^\r\n]+)\n?\z",
        RegexOptions.CultureInvariant);

    // The fixture registration clones and replays with the workspace. F8
    // remains reserved even if the learner deletes a virtual project file.
    internal bool DiagnosticWorkspace => Terminal.HasUnitTest(DiagnosticTestSelector);
    internal int DiagnosticProblemCount => DiagnosticWorkspace && Active is { App: "code" } w &&
        DiagnosticMessage(w).Length > 0 ? 1 : 0;

    internal void PrepareDiagnosticCase()
    {
        Terminal.CreateFolder(DiagnosticRoot);
        Terminal.StoreFile(DiagnosticSource, DiagnosticFunction + "    return " + Fault + "\n");
        Terminal.StoreFile(DiagnosticTests, DiagnosticTestSource);
        originalFiles[DiagnosticSource] = Terminal.ReadFile(DiagnosticSource);
        originalFiles[DiagnosticTests] = DiagnosticTestSource;
        Terminal.InstallUnitTest(DiagnosticTestSelector, RunDiagnosticTest);
        Terminal.Execute("cd ~/project/diagnostics-lab");
        var w = Active;
        w.Tabs.Clear(); w.ClosedTabs.Clear(); w.ActiveTab = 0;
        OpenDocument(DiagnosticTests);
        w.Panel = true; w.Focus = "code-problems";
    }

    private string DiagnosticDocument(LabWindow w) =>
        w.Tabs.FirstOrDefault(tab => EditorPath(tab) == DiagnosticSource)?.Body ?? Terminal.ReadFile(DiagnosticSource) ?? "";

    private static string RunDiagnosticTest(VirtualTerminal terminal)
    {
        if (terminal.Cwd != DiagnosticRoot || terminal.ReadFile(DiagnosticTests) != DiagnosticTestSource)
            throw new ArgumentException("FAILED: тест не найден в исходном учебном файле test_units.py.");
        var expression = DiagnosticReturn.Match(terminal.ReadFile(DiagnosticSource) ?? "").Groups["value"].Value;
        if (expression is not (Fault or "round(kelvin - 273.15)" or "kelvin - 273.15"))
            throw new ArgumentException("FAILED: учебный тест не распознал выражение в сохранённом units.py.");
        var failures = new List<string>();
        foreach (var (kelvin, expected) in new[] { (273.15m, 0m), (300.65m, 27.5m), (250.4m, -22.75m) })
        {
            decimal actual = expression == Fault ? kelvin + 273.15m : kelvin - 273.15m;
            if (expression == DiagnosticFixes[0]) actual = Math.Round(actual, 0, MidpointRounding.ToEven);
            if (actual != expected)
                failures.Add(FormattableString.Invariant(
                    $"{kelvin} K → {actual} °C, ожидалось {expected} °C"));
        }
        if (failures.Count > 0) throw new ArgumentException(
            $"FAILED (failures={failures.Count}): {DiagnosticTestSelector}; " + string.Join("; ", failures) + ".");
        return "УЧЕБНЫЙ ТЕСТ · " + DiagnosticTestSelector + "\nRan 1 test\nOK\n";
    }

    private string DiagnosticMessage(LabWindow w)
    {
        string value = DiagnosticReturn.Match(DiagnosticDocument(w)).Groups["value"].Value;
        return value == Fault ? "units.py:2 · Неверный знак в переводе температуры"
            : value == DiagnosticFixes[0] ? "units.py:2 · Округление теряет дробную часть"
            : value == DiagnosticFixes[1] ? "" : "units.py:2 · Проверьте выражение возврата";
    }

    private LabEntry[] DiagnosticEntries(LabWindow w) =>
        w.Focus == "code-quick-fix" ? DiagnosticChoices :
        DiagnosticMessage(w) is { Length: > 0 } message ? [new(message, DiagnosticSource + ":2", "problem")] : [];

    internal bool DiagnosticActionAvailable(string action)
    {
        var w = Active;
        if (!DiagnosticWorkspace || w?.App != "code") return false;
        if (action == "code-problem-next")
            return w.Focus is "code-problems" or "body" && DiagnosticMessage(w).Length > 0;
        if (action == "code-fix-open")
        {
            if (w.Focus != "body" || EditorPath(w.Tab) != DiagnosticSource || DiagnosticMessage(w).Length == 0) return false;
            var expression = DiagnosticReturn.Match(w.Body).Groups["value"];
            return expression.Success && w.Cursor >= expression.Index && w.Cursor <= expression.Index + expression.Length;
        }
        return w.Focus == "code-quick-fix";
    }

    private bool? ApplyDiagnostics(string action, LabWindow w)
    {
        if (action != "code-problem-next" && !action.StartsWith("code-fix-", StringComparison.Ordinal)) return null;
        if (!DiagnosticActionAvailable(action)) return false;
        switch (action)
        {
            case "code-problem-next":
                OpenDocument(DiagnosticSource);
                var expression = DiagnosticReturn.Match(w.Body).Groups["value"];
                w.Cursor = expression.Success ? expression.Index : DiagnosticFunction.Length;
                w.Status = DiagnosticMessage(w); return true;
            case "code-fix-open":
                w.Focus = "code-quick-fix"; w.OptionIndex = 0; return true;
            case "code-fix-next":
            case "code-fix-prev":
                w.OptionIndex = 1 - w.OptionIndex; return true;
            case "code-fix-cancel":
                w.Focus = "body"; return true;
            case "code-fix-apply":
                w.Focus = "body";
                var result = DiagnosticReturn.Match(w.Body).Groups["value"];
                if (!result.Success || result.Value == DiagnosticFixes[w.OptionIndex]) return false;
                Remember(w);
                w.Body = w.Body[..result.Index] + DiagnosticFixes[w.OptionIndex] + w.Body[(result.Index + result.Length)..];
                w.Tab.ExtraMatches.Clear(); w.Cursor = result.Index;
                w.Status = DiagnosticMessage(w) is { Length: > 0 } problem ? problem :
                    "Исправление применено. Сохраните файл и запустите тест.";
                return true;
            default:
                return null;
        }
    }

    internal object DiagnosticEvidence()
    {
        var w = Active;
        return new { w?.Url, w?.Focus, w?.OptionIndex, caret = w?.Tab?.Position.Caret,
            code = w == null ? null : DiagnosticDocument(w), choices = w == null ? [] : DiagnosticEntries(w) };
    }
}
