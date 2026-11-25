using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// Working files are entirely inside the mission's VirtualTerminal. Downloads history is
// an original location, not a second copy of the file and not a host filesystem dialog.
internal sealed partial class LabScene
{
    private string projectFileFamily;
    // Feedback about a rejected action is not a file operation and is not replay state.
    internal string FileProblem { get; private set; }
    private bool RejectFile(LabWindow w, string reason)
    {
        FileProblem = reason;
        if (w != null) w.Status = reason;
        return false;
    }
    private ResearchCase FileCase => ReportCases[browserCaseVariant];
    internal string FileStem => new[] { "weekly-stock", "ready-shipments", "kit-checks" }[browserCaseVariant];
    private string IncomingDirectory => projectFileFamily == "inbox" ? "/home/student/Downloads" : Project;
    private static string ParentOf(string path) => path[..Math.Max(1, path.LastIndexOf('/'))];
    private static string RevealTitle(string path) => "Показать в папке: " + NameOf(path);

    internal void PrepareProjectFiles(string family, bool windows)
    {
        if (windows) PrepareWindowsDesktop();
        projectFileFamily = family;
        if (family == "inbox") PrepareBrowserPractice("download"); else Ensure("code");
        Terminal.CreateFolder(Project + "/archive");
        Terminal.CreateFolder(Project + "/reports");
        string archived = Project + "/archive/" + FileStem + ".csv";
        Terminal.StoreFile(archived, "period,status\nprevious,archived\n");
        originalFiles[archived] = Terminal.ReadFile(archived);
        // A similarly named file makes a wrong selection observable, even if it has the same extension.
        Terminal.StoreFile(IncomingDirectory + "/draft.csv", "state,approved\ndraft,no\n");
        if (family != "inbox")
        {
            string source = Project + "/" + FileCase.Download;
            Terminal.StoreFile(source, FileCase.Fact + "\n"); originalFiles[source] = Terminal.ReadFile(source);
        }
    }

    internal string FileRouteTarget(string value) => projectFileFamily == null ? value : value switch
    {
        "@source" => IncomingDirectory + "/" + FileCase.Download,
        "@reveal" => RevealTitle(FileCase.Download),
        _ => value
    };

    private IEnumerable<LabEntry> ProjectTree(string folder, HashSet<string> expanded, bool recursive = false)
    {
        foreach (string path in Terminal.Entries(folder))
        {
            if (NameOf(path).StartsWith('.')) continue;
            bool directory = Terminal.DirectoryExists(path);
            yield return new(NameOf(path), path, directory ? "folder" : "file");
            if (directory && (recursive || expanded.Contains(path)))
                foreach (var child in ProjectTree(path, expanded, recursive)) yield return child;
        }
    }

    private LabEntry[] DownloadEntries() => downloads.SelectMany(path => projectFileFamily == "inbox"
        ? new[] { new LabEntry(NameOf(path), path, "file"), new LabEntry(RevealTitle(path), path, "reveal") }
        : new[] { new LabEntry(NameOf(path), path, "file") }).ToArray();

    private void SelectPath(LabWindow w, string path) =>
        w.Item = Array.FindIndex(Entries(w), item => item.Path == path);
    private bool RevealDownload(string path, LabWindow browser)
    {
        if (!Terminal.Exists(path)) { browser.Status = "Файл был перемещён или переименован"; return false; }
        Ensure("files");
        var w = Active; w.Focus = "body"; ChangeFolder(w, ParentOf(path)); SelectPath(w, path);
        w.Status = "Выбран " + NameOf(path);
        return w.Item >= 0;
    }

    // Move an entry, not the active editor selection. Copy deliberately does not rebind tabs.
    private void RelinkTabs(string oldPath, string newPath)
    {
        foreach (var w in Windows.Where(win => win.App == "code"))
            foreach (var tab in w.Tabs.Concat(w.ClosedTabs).Where(t => EditorPath(t) == oldPath || EditorPath(t).StartsWith(oldPath + "/", StringComparison.Ordinal)))
            {
                tab.Url = newPath + EditorPath(tab)[oldPath.Length..]; tab.Title = NameOf(tab.Url);
            }
    }

    private static bool LegalLeaf(string name, bool windows)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 ||
            name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl)) return false;
        if (!windows) return true;
        if (name.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || name.EndsWith('.') || name.EndsWith(' ')) return false;
        string head = name.Split('.')[0].ToUpperInvariant();
        return !(head is "CON" or "NUL" or "PRN" or "AUX" ||
            head.Length == 4 && head[3] is >= '1' and <= '9' && (head.StartsWith("COM") || head.StartsWith("LPT")));
    }

    private bool WindowsFileRules => WindowsDesktop || UseWindowsBindings;

    private bool Occupied(string dest, string except = null) =>
        Terminal.Entries(ParentOf(dest)).Any(p => p != except && string.Equals(p, dest,
            WindowsFileRules ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    private bool BeginFileName(LabWindow w)
    {
        var entry = Entries(w).ElementAtOrDefault(w.Item);
        if (entry?.Kind != "file" || !Terminal.Exists(entry.Path)) return false;
        w.RenamePath = entry.Path; w.Focus = "file-name"; w.Input = entry.Title;
        int dot = entry.Title.LastIndexOf('.');
        w.Select(0, dot > 0 ? dot : entry.Title.Length);
        w.Status = "Измените имя; расширение не выделено. Enter — принять, Esc — отменить.";
        return true;
    }

    private bool EndFileName(LabWindow w, bool cancel)
    {
        FileProblem = null;
        string oldPath = w.RenamePath, newPath = oldPath;
        if (w.Focus != "file-name" || oldPath.Length == 0) return false;
        if (!cancel)
        {
            if (!LegalLeaf(w.Input, WindowsFileRules) || System.Text.Encoding.UTF8.GetByteCount(w.Input) > 255)
            return RejectFile(w, "Некорректное имя. Введите имя файла без пути.");
            newPath = ParentOf(oldPath) + "/" + w.Input;
            if (Occupied(newPath, oldPath))
            return RejectFile(w, "Такое имя уже занято. Исходный файл не изменён.");
            if (!Terminal.Exists(oldPath)) return RejectFile(w, "Исходный файл больше не существует");
            if (newPath != oldPath)
            {
                try { Terminal.Transfer(oldPath, newPath, true); }
                catch (ArgumentException) { return RejectFile(w, "Не удалось переименовать файл. Проверьте путь и права."); }
                RelinkTabs(oldPath, newPath);
                if (fileTransfer == oldPath && Clipboard == oldPath) Clipboard = fileTransfer = newPath;
            }
        }
        w.RenamePath = ""; w.Focus = w.App == "code" ? "explorer" : "body"; SelectPath(w, newPath);
        w.Status = cancel ? "Переименование отменено" : "Имя файла: " + NameOf(newPath);
        return true;
    }

    private bool PasteProjectFile(LabWindow w)
    {
        FileProblem = null;
        string source = fileTransfer;
        if (source.Length == 0 || Clipboard != source || !Terminal.Exists(source))
            return RejectFile(null, "В буфере нет доступного файла. Снова выберите и скопируйте или вырежьте его.");
        var selected = Entries(w).ElementAtOrDefault(w.Item);
        string folder = w.App == "code" ? (selected?.Kind == "folder" ? selected.Path :
            selected == null ? EditorProjectRoot : ParentOf(selected.Path)) : w.Folder;
        string dest = folder + "/" + NameOf(source);
        if (dest == source) return RejectFile(null, "Файл уже в этой папке. Выберите другую папку назначения.");
        if (!Terminal.DirectoryExists(folder) || folder == source || folder.StartsWith(source + "/", StringComparison.Ordinal) ||
            Occupied(dest))
        return RejectFile(w, "Нельзя вставить сюда: проверьте папку и совпадение имён. Ничего не заменено.");
        try { Terminal.Transfer(source, dest, fileCut); }
        catch (ArgumentException) { return RejectFile(w, "Не удалось перенести данные. Проверьте путь и права."); }
        if (fileCut) { RelinkTabs(source, dest); fileTransfer = ""; }
        if (w.App == "code") w.ExpandedProjectFolders.Add(folder);
        SelectPath(w, dest); w.Status = (fileCut ? "Перемещён: " : "Создана копия: ") + NameOf(dest);
        return true;
    }

    internal object ProjectFileEvidence(LabTask task)
    {
        if (projectFileFamily == null) return null;
        var w = Active;
        string action = task.Action;
        if (w == null || !(action.StartsWith("file-", StringComparison.Ordinal) || action is "item-open" or "item-next" or "item-prev" or "explorer" or "win-explorer-parent")) return null;
        string name = FileStem + ".csv";
        object Files() => new {
            source = Terminal.ReadFile(IncomingDirectory + "/" + FileCase.Download),
            renamed = Terminal.ReadFile(IncomingDirectory + "/" + name),
            copy = Terminal.ReadFile(Project + "/reports/" + FileCase.Download),
            delivered = Terminal.ReadFile(Project + "/reports/" + name),
            archive = Terminal.ReadFile(Project + "/archive/" + name),
            neighbor = Terminal.ReadFile(IncomingDirectory + "/draft.csv")
        };
        // A learner may inspect a file or visit another folder before returning. An unrelated
        // tab, clipboard or caret must not invalidate a correct action on the requested object.
        var entry = Entries(w).ElementAtOrDefault(w.Item);
        return action switch {
            "explorer" => new { w.App, w.View, w.Focus, w.Sidebar },
            "item-next" or "item-prev" => new { w.App, w.Focus, w.Folder, entry },
            "file-name-start" => new { w.App, w.Focus, w.RenamePath, w.Input, w.Cursor, w.Anchor },
            "file-name-commit" or "file-paste" => new { w.App, w.Focus, entry, files = Files() },
            "file-cut" or "file-copy" => new { w.App, w.Focus, entry, fileCut, fileTransfer },
            "file-parent" or "win-explorer-parent" => new { w.App, w.Focus, w.Folder },
            "item-open" when w.App == "code" && w.Focus == "body" => new { w.App, w.Focus, w.Url, w.Body, files = Files() },
            "item-open" when w.App == "files" => new { w.App, w.Focus, w.Folder, entry },
            "item-open" when w.App == "chrome" => new { w.App, w.Focus, w.Url, entry,
                downloaded = Terminal.ReadFile(IncomingDirectory + "/" + FileCase.Download) },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void ProjectFileMissions(List<LiveMission> list)
    {
        void Add(string app, int level, string id, string name, string family, params LivePhase[] phases) =>
            list.Add(new(app, level, id, name, phases, FinishAfterRoute: true, Curated: true,
                Prepare: s => s.PrepareProjectFiles(family, id.StartsWith("workday-windows-"))));
        foreach (var route in new[] { ("chrome", "chrome-project-file-inbox"), ("workday", "workday-project-file-inbox"), ("workday", "workday-windows-project-file-inbox") })
            Add(route.Item1, route.Item1 == "chrome" ? 0 : 1, route.Item2, "Скачать CSV, переименовать и передать в проект", "inbox",
                new("Найдите первоисточник и скачайте CSV", "url browser-search pick:@target link-open pick:@download link-open"),
                new("Из загрузок Chrome покажите полученный файл в папке", "downloads pick:@reveal link-open"),
                new("Сохраните расширение, поменяйте имя и перенесите файл в reports", "file-name-start file-name-commit file-cut file-route-parent pick:project file-open pick:reports file-open file-paste"),
                new("Откройте отчёт из проекта и проверьте его содержимое", "file-open"));
        Add("code", 0, "code-project-file-move", "Переименовать файл и переместить его по проекту", "move",
            new("Выберите исходный отчёт в проводнике VS Code", "explorer pick:@source"),
            new("Исправьте имя, сохранив расширение CSV", "file-name-start file-name-commit"),
            new("Перенесите сам файл в reports и откройте его там", "file-cut pick:reports file-paste file-open"));
        Add("code", 1, "code-project-file-copy", "Подготовить рабочую копию, сохранив оригинал", "copy",
            new("Найдите и скопируйте исходный отчёт в проводнике VS Code", "explorer pick:@source file-copy"),
            new("Поместите копию в reports, оставив оригинал на месте", "pick:reports file-paste"),
            new("Задайте новое имя только копии и откройте её для сверки", "file-name-start file-name-commit file-open"));
    }
}
