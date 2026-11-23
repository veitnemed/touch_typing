using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private const string ExactRoot = Project + "/task-list";
    private const string ExactApp = ExactRoot + "/app.py";
    private const string ExactTasks = ExactRoot + "/tasks.py";
    internal const string ExactFileFragment = "ap",
        ExactNeedle = "draft", ExactReplacement = "ready";
    private const string ExactOriginal = "from tasks import add_task\n\nadd_task(\"draft\", \"notes\")\nadd_task(\"draft\", \"report\")\nadd_task(\"draft\", \"archive\")\n";
    private static readonly string ExactFixed = ExactOriginal.Replace("draft\", \"report", "ready\", \"report", StringComparison.Ordinal);
    private static readonly string[] ExactPaths = [ExactApp, ExactTasks, ExactRoot + "/storage.py", ExactRoot + "/test_tasks.py"];
    internal bool ExactEdit { get; private set; }

    internal void PrepareExactEdit()
    {
        if (ExactEdit) return;
        ExactEdit = true;
        Terminal.CreateFolder(ExactRoot);
        string[] bodies = [ExactOriginal, "from storage import write_task\n\ndef add_task(status, title):\n    return write_task(status, title)\n",
            "def write_task(status, title):\n    return {'status': status, 'title': title}\n",
            "from tasks import add_task\n\ndef test_add_task():\n    assert add_task('draft', 'notes')['status'] == 'draft'\n"];
        for (int i = 0; i < ExactPaths.Length; i++)
        {
            Terminal.StoreFile(ExactPaths[i], bodies[i]);
            originalFiles[ExactPaths[i]] = bodies[i];
        }
        Ensure("code");
        Active.Tabs.Clear();
        Active.ClosedTabs.Clear();
        Active.ActiveTab = 0;
        OpenDocument(ExactTasks);
    }

    private static int FuzzyFileScore(string name, string query)
    {
        if (query.Length == 0) return 0;
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        int found = 0;
        for (int i = 0; i < name.Length; i++)
            if (char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(query[found]) && ++found == query.Length)
                return 3 + i;
        return int.MaxValue;
    }

    private LabEntry[] ExactFileEntries(string query) => ProjectDocuments()
        .Select(path => (Path: path, Score: FuzzyFileScore(NameOf(path), query.Trim())))
        .Where(item => item.Score != int.MaxValue)
        .OrderBy(item => item.Score).ThenBy(item => item.Path, StringComparer.Ordinal)
        .Select(item => new LabEntry(NameOf(item.Path), item.Path, "file")).ToArray();

    private static int[] EditorFindOffsets(string body, string query)
    {
        var found = new List<int>();
        if (query.Length == 0) return [];
        for (int from = 0; from <= body.Length - query.Length;)
        {
            int i = body.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) break;
            found.Add(i); from = i + query.Length;
        }
        return found.ToArray();
    }

    private bool? ApplyExactEdit(string action, LabWindow w)
    {
        if (!ExactEdit || w?.App != "code") return null;
        switch (action)
        {
            case "quick-choose":
                if (w.Focus != "quick-open") return false;
                var choice = ExactFileEntries(w.Input).ElementAtOrDefault(w.OptionIndex);
                if (choice == null || w.Input.Trim().Length == 0 || choice.Title.Equals(w.Input.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    w.Status = "Введите часть имени файла";
                    return false;
                }
                OpenDocument(choice.Path);
                return true;
            case "find":
                if (w.Focus != "body") return false;
                w.Focus = "find";
                w.Input = w.Find = "";
                w.Cursor = 0;
                w.Status = "Введите текст для поиска";
                return true;
            case "exact-find-submit":
            case "exact-find-next":
            case "exact-find-prev":
                if (w.Focus != "find" || w.Tab == null) return false;
                string query = w.Input;
                int[] hits = EditorFindOffsets(w.Body, query);
                int previous = w.Find.Equals(query, StringComparison.OrdinalIgnoreCase) &&
                    w.Tab.Position.Caret - w.Tab.Position.Anchor == query.Length
                    ? Array.IndexOf(hits, w.Tab.Position.Anchor) : -1;
                w.Find = query;
                if (hits.Length == 0) { w.Status = "Совпадений нет"; return false; }
                int index = action == "exact-find-submit" ? 0 : previous == -1
                    ? (action == "exact-find-next" ? 0 : hits.Length - 1)
                    : (previous + (action == "exact-find-next" ? 1 : -1) + hits.Length) % hits.Length;
                w.Tab.Position.Anchor = hits[index];
                w.Tab.Position.Caret = hits[index] + query.Length;
                w.Tab.ExtraMatches.Clear();
                w.Status = $"Совпадение {index + 1} из {hits.Length}";
                return true;
            case "exact-call-home":
                if (w.Focus != "body") return false;
                w.Cursor = LineStart(w.Body, w.Cursor);
                return true;
            default:
                return null;
        }
    }

    private bool ExactIntact(LabWindow w) => ExactPaths.All(path =>
    {
        string expected = path == ExactApp ? ExactFixed : originalFiles[path];
        return Terminal.ReadFile(path) == expected &&
            w.Tabs.Where(tab => EditorPath(tab) == path).All(tab => tab.Body == expected);
    });

    internal object ExactEditEvidence(LabTask task)
    {
        if (!ExactEdit || Active?.App != "code") return null;
        var w = Active;
        return task.Id switch
        {
            "quick-choose" => new { w.Focus, w.Url },
            "exact-find-submit" or "exact-find-next" or "exact-find-prev" or "escape" =>
                new { w.Focus, w.Url, w.Find, w.Tab.Position.Anchor, w.Tab.Position.Caret,
                    hits = EditorFindOffsets(w.Body, w.Find) },
            "save" or "code-back" => new { w.Focus, w.Url, w.Cursor, w.Anchor, exact = ExactIntact(w) },
            "exact-call-home" => new { w.Focus, w.Url, w.Cursor, w.Anchor },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void EditorExactEditMissions(List<LiveMission> list) => list.Add(new(
        "code", 0, "code-find-exact-edit", "Найти и исправить нужное место",
        [
            new("Найдите app.py по части имени «ap»", "quick-open quick-choose"),
            new("Введите draft и обойдите три совпадения вперёд и назад", "find exact-find-submit exact-find-next exact-find-next exact-find-next exact-find-prev exact-find-prev escape"),
            new("Замените выделенный статус отчёта на ready и сохраните", "save"),
            new("Перейдите в начало вызова, проверьте определение и вернитесь", "exact-call-home code-definition code-back")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareExactEdit()));
}
