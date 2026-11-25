using System;
using System.Linq;

namespace TouchTypingStudio;

// One introduction, one durable artifact. Every clipboard selection is used
// before the next source replaces it. Exercise assets remain untouched.
internal sealed partial class LabScene
{
    internal const string GuideNoteRevision = "editor-guide-note/v2";
    internal const string GuideNoteName = "review-notes.txt";
    private const string GuideNotePath = Project + "/" + GuideNoteName;
    private string guideExpected;
    internal bool GuideNoteActive => guideExpected != null;

    internal void PrepareGuideNote()
    {
        string Lines(string name, string start, int count)
        {
            var lines = Terminal.ReadFile(Project + "/" + name)?.Split('\n') ?? [];
            var relevant = lines.SkipWhile(line => !line.StartsWith(start, StringComparison.Ordinal)).Take(count).ToArray();
            if (relevant.Length != count)
                throw new InvalidOperationException("Неполный учебный исходник: " + name);
            return string.Join("\n", relevant) + "\n";
        }
        guideExpected =
            Lines("storage.py", "def save_tasks(", 2) +
            Lines("tasks.py", "def add_task(", 1) +
            Lines("app.py", "from tasks import ", 1);
    }

    private bool CanCreateGuideNote(LabWindow w) => GuideNoteActive && Terminal.ReadFile(GuideNotePath) == null &&
        !w.Tabs.Concat(w.ClosedTabs).Any(t => EditorPath(t) == GuideNotePath);

    internal bool GuideNoteVerified()
    {
        if (Terminal.ReadFile(GuideNotePath) != guideExpected) return false;
        var sources = new[] { "storage.py", "tasks.py", "app.py" }.Select(name => Project + "/" + name).ToArray();
        return sources.All(path => originalFiles.TryGetValue(path, out var source) && Terminal.ReadFile(path) == source) &&
            Windows.Where(w => w.App == "code").SelectMany(w => w.Tabs)
                .All(t => EditorPath(t) == GuideNotePath ? t.Body == guideExpected :
                    !sources.Contains(EditorPath(t)) || t.Body == originalFiles[EditorPath(t)]);
    }

    internal object GuideNoteEvidence(LabTask task)
    {
        if (!GuideNoteActive) return null;
        var w = Active;
        return task.Action switch
        {
            "copy" => new { w?.App, w?.Url, w?.Focus, w?.Cursor, w?.Anchor, Clipboard },
            "focus-editor" => new { w?.Split, w?.Group, w?.Url, w?.Focus, noteConfirmed = GuideNoteVerified() },
            "explorer" => new { w?.View, w?.Focus, entry = w == null ? null : Entries(w).ElementAtOrDefault(w.Item)?.Path },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    internal static LiveMission GuideNoteMission(LiveMission baseline) => baseline with
    {
        Prepare = scene => scene.PrepareGuideNote(),
        Phases = [
            new("Открыть функции сохранения задач",
                "quick-open quick:storage.py quick-choose symbols symbol-open select-line select-line copy"),
            new("Начните одну заметку: вставьте сигнатуру сохранения и строку с форматом JSON",
                "new-file paste"),
            new("Найдите в tasks.py сигнатуру добавления задачи. Она дополнит заметку",
                "quick-open quick:tasks.py quick-choose symbols symbol-open select-line copy"),
            new("Вернитесь к той же заметке, добавьте сигнатуру и сохраните. Закройте её, пока сверяете входной файл",
                "prev-tab paste save close-tab"),
            new("На открытой вкладке app.py возьмите строку, которая связывает входной файл с tasks.py",
                "prev-tab select-line copy"),
            new("Восстановите закрытую заметку и добавьте связь между исходными файлами",
                "restore-tab paste save"),
            new("Проверьте заметку: вернитесь к ней из поиска и найдите сохранённый файл в проводнике",
                "find escape project-search focus-editor explorer focus-editor")
        ]
    };
}
