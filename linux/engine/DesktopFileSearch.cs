using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private const string FileSearchRoot = Project + "/casework";
    private const string FileSearchHandoff = FileSearchRoot + "/handoff";
    private bool desktopFileSearchCase;
    internal bool ReplayLegacyDesktopFileSearch;
    private static readonly (string Topic, string Number, int Count)[] DesktopSearchCases =
        [("incident", "642", 18), ("invoice", "318", 27), ("delivery", "907", 12)];
    private (string Topic, string Number, int Count) SearchCase => DesktopSearchCases[browserCaseVariant];
    internal string FileSearchQuery => SearchCase.Number;
    internal string FileSearchControl => SearchCase.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string ApprovedName => $"{SearchCase.Topic}-{SearchCase.Number}.csv";
    private string DraftName => $"{SearchCase.Topic}-{SearchCase.Number}-draft.csv";
    private string ApprovedPath => FileSearchRoot + "/approved/" + ApprovedName;
    private string DraftPath => FileSearchRoot + "/archive/" + DraftName;
    private string ExternalPath => "/home/student/Downloads/" + ApprovedName;
    private string HandoffPath => FileSearchHandoff + "/" + ApprovedName;
    private string CaseRow(string status, int count) => $"ticket,status,count\n{FileSearchQuery},{status},{count}\n";

    internal void PrepareDesktopFileSearch(bool windows)
    {
        if (windows) PrepareWindowsDesktop();
        desktopFileSearchCase = true;
        foreach (string directory in new[] { FileSearchHandoff, ParentOf(ApprovedPath), ParentOf(DraftPath) })
            Terminal.CreateFolder(directory);
        Terminal.StoreFile(ApprovedPath, CaseRow("approved", SearchCase.Count));
        Terminal.StoreFile(DraftPath, CaseRow("draft", SearchCase.Count - 2));
        // Same filename elsewhere is not a result, and must remain unmodified.
        Terminal.StoreFile(ExternalPath, CaseRow("external", SearchCase.Count - 1));
    }

    internal string DesktopSearchTarget(string token) => desktopFileSearchCase ? token switch
    {
        "@approved-file" => ApprovedPath,
        "@handoff-file" => HandoffPath,
        _ => token,
    } : token;

    internal LabTask DescribeDesktopSearchTask(LabTask task) => !desktopFileSearchCase ? task : task.Id switch
    {
        "file-next" when task.Text == ApprovedName => task with
        {
            Title = "Выберите утверждённый отчёт, не черновик",
            Hint = "Проверьте путь: утверждённая версия хранится в approved, черновик — в archive."
        },
        "file-copy" => task with { Title = "Скопируйте утверждённый отчёт. Оригинал должен остаться" },
        "file-open" when Active?.Folder == FileSearchHandoff => task with { Title = "Откройте копию из папки handoff" },
        "find" => task with { Title = "Откройте поиск в копии и сверьте строку approved" },
        _ => task,
    };

    internal static bool IsFileSearch(LabWindow w) => w?.App == "files" && w.Focus is "file-search" or "file-results";

    private LabEntry[] FileSearchEntries(LabWindow w)
    {
        string query = (w.Focus == "file-search" ? w.Input : w.Query).Trim();
        if (query.Length == 0) return [];
        return ProjectTree(w.Folder, null, recursive: true)
            .Where(entry => entry.Kind == "file" && entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    private bool? ApplyDesktopFileSearch(string action, LabWindow w)
    {
        if (w?.App != "files" || ReplayLegacyDesktopFileSearch) return null;
        if (action == "file-search-open")
        {
            if (w.Focus == "body")
            {
                w.FileSearchReturnPath = Entries(w).ElementAtOrDefault(w.Item)?.Path;
                w.Query = "";
            }
            else if (!IsFileSearch(w)) return false;
            else if (w.Focus == "file-search") w.Query = w.Input;
            w.Focus = "file-search"; w.Input = w.Query; w.Select(0, w.Input.Length);
            w.Item = -1; w.Status = "Введите часть имени файла";
            return true;
        }
        if (!IsFileSearch(w)) return null;
        switch (action)
        {
            case "file-search-close":
                w.Focus = "body";
                w.Item = Array.FindIndex(Entries(w), entry => entry.Path == w.FileSearchReturnPath);
                w.FileSearchReturnPath = null; w.Status = "Поиск закрыт";
                return true;
            case "file-search-to-field":
                if (w.Focus != "file-results") return false;
                w.Focus = "file-search"; w.Input = w.Query; w.Select(0, w.Input.Length);
                w.Item = -1; w.Status = "Уточните имя файла";
                return true;
            case "file-search-to-results" or "item-next" or "item-prev" when w.Focus == "file-search":
                var results = FileSearchEntries(w);
                if (results.Length == 0) { w.Status = "Нет совпадений. Уточните запрос."; return false; }
                w.Query = w.Input; w.Focus = "file-results";
                w.Item = action == "item-prev" ? results.Length - 1 : 0;
                w.Status = "Выберите файл, проверив его путь";
                return true;
        }
        return null;
    }

    internal object DesktopFileSearchEvidence(LabTask task)
    {
        if (!desktopFileSearchCase) return null;
        var w = Active;
        string picked = w == null ? null : Entries(w).ElementAtOrDefault(w.Item)?.Path;
        return task.Action switch
        {
            "open-files" or "win-explorer" or "launch" => new { w?.App, w?.Folder },
            "file-search-open" or "file-search-close" => new { w?.App, w?.Folder, w?.Focus },
            "file-search-query" => new { w?.App, w?.Folder, w?.Focus, w?.Input,
                matches = w == null ? [] : Entries(w).Select(entry => entry.Path).ToArray() },
            "item-next" or "item-prev" => new { w?.App, w?.Folder, w?.Focus, picked },
            "file-copy" => new { w?.App, w?.Folder, picked, fileTransfer, fileCut },
            "file-paste" => new { w?.App, w?.Folder, copy = Terminal.ReadFile(HandoffPath),
                original = Terminal.ReadFile(ApprovedPath), draft = Terminal.ReadFile(DraftPath), decoy = Terminal.ReadFile(ExternalPath) },
            "item-open" => new { w?.App, w?.Focus, folder = w?.App == "files" ? w.Folder : null,
                path = w?.App == "code" && w.Tab != null ? EditorPath(w.Tab) : null, body = w?.App == "code" ? w.Body : null },
            "find" or "file-search-verify" => new { w?.App, w?.Focus, w?.Url, w?.Find, w?.Anchor, w?.Cursor,
                bodyAnchor = w?.Tab?.Position.Anchor, bodyCaret = w?.Tab?.Position.Caret,
                body = w?.App == "code" ? w.Body : null },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void DesktopFileSearchMissions(List<LiveMission> missions)
    {
        foreach (bool windows in new[] { false, true })
        {
            string app = windows ? "windows" : "ubuntu";
            string open = windows ? "win-explorer" : "open-files";
            missions.Add(new(app, 0, app + "-file-search-handoff", "Найти утверждённый отчёт и подготовить передачу",
            [
                new("Найдите отчёт по номеру внутри рабочей папки",
                    open + " pick:project file-open pick:casework file-open file-search-open file-search-query"),
                new("Отличите утверждённую версию и перенесите только копию",
                    "pick:@approved-file file-copy file-search-close pick:handoff file-open file-paste"),
                new("Откройте копию и проверьте отметку перед передачей",
                    "pick:@handoff-file file-open find file-search-verify")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareDesktopFileSearch(windows)));
        }
    }
}
