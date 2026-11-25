using System;
using System.Linq;

namespace TouchTypingStudio;

// A real in-memory test result supplies the location. This is not an execution
// of the user's Python source on the host, and only the intended file diff passes.
internal sealed partial class LabScene
{
    private sealed record TracebackCase(string Name, string Constant, int Line, string Limit)
    {
        internal string Path => Project + "/" + Name;
        internal string Original => Make("0");
        internal string Fixed => Make(Limit);
        private string Make(string value) => "# Worker configuration\n" + string.Concat(
            Enumerable.Repeat("# Settings from the unchanged project template\n", Line - 2)) +
            Constant + " = " + value + "\n# A different setting must not change\nAROUND_LIMIT = 0\n";
    }
    private static readonly TracebackCase[] TracebackCases =
    [
        new("retry_policy.py", "RETRY_LIMIT", 5, "3"),
        new("buffer_policy.py", "BUFFER_LIMIT", 9, "8"),
        new("queue_policy.py", "QUEUE_LIMIT", 7, "4")
    ];
    internal const string TracebackTestCommand = "python -m unittest test_policy";
    private TracebackCase tracebackCase;
    internal bool TracebackPractice => tracebackCase != null;

    internal void PrepareTracebackPractice()
    {
        var fixture = tracebackCase = TracebackCases[browserCaseVariant];
        Terminal.StoreFile(fixture.Path, fixture.Original);
        originalFiles[fixture.Path] = fixture.Original;
        Terminal.StartInsideProject();
        Terminal.InstallUnitTest("test_policy", terminal =>
        {
            if (terminal.ReadFile(fixture.Path) == fixture.Fixed)
                return "Ran 1 test\nOK: " + fixture.Constant + " = " + fixture.Limit + "\n";
            throw new ArgumentException("Traceback: " + fixture.Name + ":" + fixture.Line +
                "\nValueError: " + fixture.Constant + " должен быть " + fixture.Limit +
                "; соседнюю настройку не меняйте.");
        });
        Terminal.Execute(TracebackTestCommand);
        Ensure("code");
        var code = Active;
        code.Tabs.Clear();
        code.ClosedTabs.Clear();
        OpenDocument(fixture.Path);
        code.Focus = "body";
        code.Panel = true;
        code.Status = "Запуск завершился ошибкой. Перейдите к строке, указанной в терминале.";
    }

    internal LabTask DescribeTracebackTask(string token)
    {
        if (!TracebackPractice) return null;
        if (token == "traceback-line")
            return new("traceback-line", "Введите номер строки из traceback и нажмите Enter", "",
                "Внизу имя файла и номер после двоеточия. Нужна именно эта строка, а не соседняя.",
                tracebackCase.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), "traceback-line");
        return token == "save" ? LabCatalog.AllTasks.First(item => item.Id == "save") with {
            Title = "Исправьте " + tracebackCase.Constant + " по сообщению и сохраните файл",
            Hint = "На строке ошибки замените 0 на требуемое значение. Не меняйте соседний параметр.",
            Text = tracebackCase.Limit
        } : null;
    }

    internal bool? DemonstrateTraceback(LabTask task)
    {
        if (!TracebackPractice) return null;
        if (task.Action == "traceback-line")
        {
            Active.Input = task.Text;
            return Text("Enter", "");
        }
        if (task.Action != "save") return null;
        // The rehearsal uses the real editing and saving operations, not a
        // pre-recorded accepted answer or a boolean flag standing in for disk.
        if (!Text("End", "") || !Text("Backspace", "")) return false;
        foreach (char digit in tracebackCase.Limit)
            if (!Text("D" + digit, digit.ToString())) return false;
        if (!Apply("save") || Terminal.ReadFile(tracebackCase.Path) != tracebackCase.Fixed)
            throw new InvalidOperationException("Traceback fixture failed to save target " + tracebackCase.Name +
                " at line " + tracebackCase.Line + ".");
        return true;
    }

    internal object TracebackEvidence(LabTask task)
    {
        if (!TracebackPractice || Active == null) return null;
        if (task.Action == "traceback-line")
            return new { Active.App, Active.Url, Active.Focus, Active.Cursor, Active.Anchor };
        if (task.Action == "save")
            return new { Active.Url, saved = Terminal.ReadFile(tracebackCase.Path), Active.Tab.SavedBody };
        return null;
    }
}

internal static partial class LiveCourse
{
    private static void EditorTracebackMissions(System.Collections.Generic.List<LiveMission> list)
    {
        list.Add(new("code", 1, "code-traceback-line", "Найти строку ошибки и проверить исправление",
            [
                new("В сообщении теста уже указана строка. Откройте её и исправьте только нужный параметр",
                    "goto-line traceback-line save"),
                new("В терминале повторите тот же тест и убедитесь, что ошибка исчезла",
                    "terminal-toggle cmd:python~-m~unittest~test_policy")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareTracebackPractice()));
    }
}
