using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal const string SearchNeedle = "DEFAULT_TIMEOUT";
    private const string SearchRoot = Project + "/search-lab";
    private const string SearchArchive = SearchRoot + "/src/archive.py";
    private const string SearchConfig = SearchRoot + "/src/config.py";
    private const string SearchNotes = SearchRoot + "/src/notes.py";
    private const string SearchErrorLog = "/home/student/runtime-error.log";
    private const string SearchOriginal = "# Runtime settings\nDEFAULT_TIMEOUT = 0\nSERVICE = \"preview\"\n";
    private const string SearchFixed = "# Runtime settings\nDEFAULT_TIMEOUT = 5\nSERVICE = \"preview\"\n";
    private string SearchProjectRoot;
    internal bool SearchRepairExercise => SearchProjectRoot == SearchRoot;

    private sealed record ProjectLineHit(string File, int Offset, int Row, int Column, string Line)
    {
        internal LabEntry Entry => new($"{NameOf(File)}:{Row}:{Column} · {Line.Trim()}", File + ":" + Offset, "search-hit");
    }

    internal void PrepareSearchRepair()
    {
        if (SearchRepairExercise) return;
        SearchProjectRoot = SearchRoot;
        Terminal.CreateFolder(SearchRoot + "/src");
        foreach (var file in new[]
        {
            (SearchArchive, "# Historical sample; not imported by the application\nDEFAULT_TIMEOUT = 8\n"),
            (SearchConfig, SearchOriginal),
            (SearchNotes, "HELP = \"DEFAULT_TIMEOUT comes from the runtime config.\"\n")
        })
        {
            Terminal.StoreFile(file.Item1, file.Item2);
            originalFiles[file.Item1] = file.Item2;
        }
        Terminal.StoreFile(SearchErrorLog, "Запуск: src/config.py\nTraceback (most recent call last):\n  File \"src/config.py\", line 2, in <module>\nValueError: DEFAULT_TIMEOUT must be >= 1 (got 0)\n");
        Ensure("code");
        Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0;
        OpenDocument(SearchArchive);
        Ensure("terminal");
        // This is replay of a recorded application error; there is no host
        // Python process or unrestricted shell behind the practice exercise.
        Terminal.Execute("cat " + SearchErrorLog);
        Active.Status = "В последнем запуске появилась ошибка. Найдите строку в рабочем конфиге.";
    }

    private ProjectLineHit[] ProjectMatches(LabWindow w, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var hits = new List<ProjectLineHit>();
        foreach (string path in ProjectDocuments().OrderBy(p => p, StringComparer.Ordinal))
        {
            string body = w.Tabs.FirstOrDefault(t => EditorPath(t) == path)?.Body ?? Terminal.ReadFile(path) ?? "";
            int offset = 0, row = 1;
            foreach (string line in body.Split('\n'))
            {
                int column = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (column >= 0) hits.Add(new(path, offset + column, row, column + 1, line));
                offset += line.Length + 1; row++;
            }
        }
        return [..hits];
    }

    private LabEntry[] ProjectMatchEntries(LabWindow w) => ProjectMatches(w, w.ProjectSearchQuery).Select(hit => hit.Entry).ToArray();
    private string[] ProjectSearchSidebar(LabWindow w) => ProjectMatches(w,
        w.Focus == "project-search" ? w.Input : w.ProjectSearchQuery).Select(hit => hit.Entry.Title).ToArray();

    private bool? ApplyProjectSearch(string action, LabWindow w)
    {
        if (w?.App != "code") return null;
        switch (action)
        {
            case "project-search-submit":
                if (w.Focus != "project-search" || string.IsNullOrWhiteSpace(w.Input)) return false;
                w.ProjectSearchQuery = w.Input.Trim();
                w.Focus = "project-results"; w.OptionIndex = 0;
                w.Status = ProjectMatches(w, w.ProjectSearchQuery).Length == 0 ? "Совпадений нет. Вернитесь к поиску (Ctrl+Shift+F)." : "Стрелки — выбрать строку, Enter — открыть.";
                return true;
            case "project-search-next": case "project-search-prev":
                if (w.Focus != "project-results") return false;
                int length = ProjectMatches(w, w.ProjectSearchQuery).Length;
                if (length < 2) return false;
                w.OptionIndex = (w.OptionIndex + (action == "project-search-next" ? 1 : length - 1)) % length; return true;
            case "project-search-open":
                if (w.Focus != "project-results") return false;
                var hit = ProjectMatches(w, w.ProjectSearchQuery).ElementAtOrDefault(w.OptionIndex);
                if (hit == null) return false;
                w.SymbolBack.Push(new(EditorPath(w.Tab), w.Tab.Position.Anchor, w.Tab.Position.Caret));
                OpenDocument(hit.File); w.Cursor = hit.Offset; w.Status = $"Строка {hit.Row}, столбец {hit.Column}";
                return true;
            case "project-search-repair":
                if (!SearchRepairExercise || w.Focus != "body" || w.Tab?.SavedBody == w.Body) return false;
                // Same operation as normal Ctrl+S, followed by disk evidence
                // from the virtual terminal snapshot; not a simulated success flag.
                if (!ApplyWindow("save", w)) return false;
                if (EditorPath(w.Tab) != SearchConfig) w.Status = "Сверьте файл с путём в ошибке: рабочий конфиг называется config.py.";
                else if (w.Body != SearchFixed) w.Status = "Значение должно быть 5; остальная часть рабочего конфига должна остаться прежней.";
                return true;
            default:
                return null;
        }
    }

    internal void DemonstrateProjectRepair()
    {
        if (!SearchRepairExercise || Active?.Tab == null || EditorPath(Active.Tab) != SearchConfig)
            throw new InvalidOperationException("Файл конфигурации не выбран");
        Active.Body = SearchFixed;
    }

    internal object ProjectSearchEvidence(string action)
    {
        var w = Active;
        return action switch
        {
            "project-search-submit" or "project-search-next" or "project-search-prev" => new
            {
                w?.Focus, query = w?.ProjectSearchQuery?.ToUpperInvariant(), w?.OptionIndex,
                rows = w?.App == "code" ? ProjectMatches(w, w.ProjectSearchQuery) : []
            },
            "project-search-open" => new { w?.Focus, w?.Url, w?.Cursor, w?.Anchor },
            "project-search-repair" => new
            {
                w?.Url, saved = Terminal.ReadFile(SearchConfig),
                archive = Terminal.ReadFile(SearchArchive), notes = Terminal.ReadFile(SearchNotes)
            },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void EditorProjectSearchMissions(List<LiveMission> list)
    {
        list.Add(new("code", 0, "code-search-error", "Найти ошибку по сообщению",
            [
                new("Прочитайте ошибку в терминале и вернитесь в редактор", "switch-next"),
                new("Найдите DEFAULT_TIMEOUT и выберите src/config.py", "project-search project-search-submit project-search-next project-search-open"),
                new("В рабочем конфиге измените значение с 0 на 5 и сохраните файл", "project-search-repair")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareSearchRepair()));
    }
}
