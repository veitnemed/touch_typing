using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// Authored offline fixtures. Every fact is visible in an actual page, CSV or
// log. None of these lessons reach the host filesystem, browser or Python.
internal sealed record BulletinCase(string Slug, string Query, string Title, string Key, int Before, int After,
    string Unit, string Previous, string Current)
{
    private string Site => "https://practice.local/workshop/" + Slug;
    internal string OldPage => Site + "/" + Previous;
    internal string NewPage => Site + "/" + Current;
    internal string BriefPage => Site + "/approved";
    internal string OldDownload => Slug + "-" + Previous + ".csv";
    internal string NewDownload => Slug + "-" + Current + ".csv";
    internal string VersionTitle(bool latest) => Title + " · " + (latest ? Current : Previous);
    internal string Row(bool latest) => $"{Key},{(latest ? After : Before)},{Unit}";
    private string OtherRow => $"CONTROL,12,{Unit}";
    internal string Csv(bool latest) => "key,value,unit\n" + Row(latest) + "\n" + OtherRow + "\n";
    internal string Body(bool latest) => (latest ? "Действующая редакция" : "Архивная редакция") + " · " +
        (latest ? Current : Previous) + "\nЗапись " + Key + " в таблице сверки:\n" +
        Row(latest) + "\n" + OtherRow;
    internal string Brief => Key + ": " + After + " " + Unit;
    internal string Report => Slug + "-review.md";
    internal string ReportTemplate => "# " + Title + "\n\nИсточник:\n[источник]\n\nСверенная строка CSV:\n";
    internal string ReportComplete(string row) => ReportTemplate.Replace("[источник]", NewPage, StringComparison.Ordinal) + row;
    internal string QuickNote => Slug + "-handoff.txt";
    internal string QuickTemplate => "Передача в смену\n\nПроверенное значение: [значение]\nСледующая проверка: 09:00\n";
    internal string QuickComplete => QuickTemplate.Replace("[значение]", Brief, StringComparison.Ordinal);
}

internal sealed record ConfigurationCase(string Slug, string Key, string Actual, string Required, string Explanation)
{
    internal string Config => Slug + ".env";
    internal string Log => Slug + "-validator.log";
    internal string Archived => Slug + "-example.env";
    internal string Broken => Key + "=" + Actual + "\n" + "standby=true\n";
    internal string Line => Key + "=" + Required + "\n";
    internal string Fixed => Line + "standby=true\n";
    internal string LogBody => "INFO проверен профиль " + Config + "\n" +
        "WARN резервный профиль " + Archived + " пропущен\n" +
        "ERROR " + Config + ": " + Explanation + " (" + Actual + "). Рабочая строка:\n" + Line +
        "INFO резервный пример оставить без изменений\n";
}

internal sealed partial class LabScene
{
    private static readonly BulletinCase[] BulletinCases =
    [
        new("dispatch-window", "dispatch window bulletin", "Окно отправки", "TR-18", 15, 24, "min", "1.4", "1.5"),
        new("receiving-limit", "receiving limit bulletin", "Лимит приёмки", "RX-09", 32, 36, "boxes", "2.1", "2.2"),
        new("audit-retention", "audit retention bulletin", "Хранение журналов", "LG-04", 7, 14, "days", "3.6", "3.7"),
    ];
    private static readonly ConfigurationCase[] ConfigurationCases =
    [
        new("stock-import", "delimiter", "semicolon", "comma", "разделитель не совпадает с CSV поставщика"),
        new("upload-gateway", "timeout", "15", "35", "задержка меньше минимального времени выгрузки"),
        new("dispatch-review", "dataset", "draft.csv", "approved.csv", "черновик не подходит для расчёта смены"),
    ];

    private BulletinCase sourceBulletin;
    private ConfigurationCase sourceConfiguration;
    private string sourceHandoff;
    private bool currentSourceRead, archivedSourceRead;
    internal bool ContextHandoffActive => sourceHandoff != null;
    internal bool BulletinWorkflow => sourceHandoff is "browser" or "report";
    private bool ReportHandoff => sourceHandoff == "report";
    private bool DesktopHandoff => sourceHandoff == "desktop";
    private string SourceReportFile => Project + "/" + sourceBulletin.Report;
    private string QuickNoteFile => Project + "/" + sourceBulletin.QuickNote;
    private string ReportCsvFile => Project + "/reports/" + sourceBulletin.NewDownload;
    private string DownloadPath => "/home/student/Downloads/" + sourceBulletin.NewDownload;
    private string ArchivedCsv => Project + "/archive/" + sourceBulletin.OldDownload;
    private string ConfigFile => Project + "/" + sourceConfiguration.Config;
    private string ConfigArchive => Project + "/archive/" + sourceConfiguration.Archived;
    private string ConfigLog => Project + "/logs/" + sourceConfiguration.Log;
    internal string SourceReadNeedle => sourceConfiguration == null ? sourceBulletin?.Key : sourceConfiguration.Key + "=";
    internal string SourceLogCommand => "grep ERROR logs/" + sourceConfiguration.Log;
    private const string ConfigTest = "test_live_config";
    internal const string SourceTestCommand = "python3 -m unittest " + ConfigTest;

    private void RestoreSourceFixture(LabScene source)
    {
        sourceBulletin = source.sourceBulletin; sourceConfiguration = source.sourceConfiguration;
        sourceHandoff = source.sourceHandoff;
        citationCopiedFromSavedNote = source.citationCopiedFromSavedNote;
        currentSourceRead = source.currentSourceRead; archivedSourceRead = source.archivedSourceRead;
    }

    private void StoreSourceFile(string path, string data)
    {
        Terminal.StoreFile(path, data);
        originalFiles[path] = data;
    }

    private LabWindow SeedSourceEditor(string path, string selection = null)
    {
        var editor = Open("code");
        string data = Terminal.ReadFile(path);
        editor.Tabs.Clear();
        editor.Tabs.Add(new LabTab(NameOf(path), path, data));
        editor.Title = "VS Code: " + NameOf(path); editor.Focus = "body"; editor.ActiveTab = 0;
        if (selection != null)
        {
            int from = data.IndexOf(selection, StringComparison.Ordinal);
            editor.Select(from, from + selection.Length);
        }
        return editor;
    }

    internal void PrepareBulletin(bool windows, bool report)
    {
        sourceBulletin = BulletinCases[browserCaseVariant];
        sourceHandoff = report ? "report" : "browser";
        if (windows) PrepareWindowsDesktop();
        if (report)
        {
            Terminal.CreateFolder(Project + "/reports");
            Terminal.CreateFolder(Project + "/archive");
            StoreSourceFile(ArchivedCsv, sourceBulletin.Csv(false));
            StoreSourceFile(SourceReportFile, sourceBulletin.ReportTemplate);
            SeedSourceEditor(SourceReportFile, "[источник]");
        }
        PrepareBrowserPractice();
        BrowserPracticeQuery = sourceBulletin.Query;
    }

    internal void PrepareDesktopHandoff(bool windows)
    {
        sourceBulletin = BulletinCases[browserCaseVariant];
        sourceHandoff = "desktop";
        if (windows) PrepareWindowsDesktop();
        else
        {
            GnomeWindows = true; GnomePractice = "same-app";
            Workspace = 0; WorkspaceCount = 2;
        }
        StoreSourceFile(QuickNoteFile, sourceBulletin.QuickTemplate);
        string scratch = Project + "/черновик.txt";
        StoreSourceFile(scratch, "Старое значение не подтверждено. Не изменять.\n");
        var target = SeedSourceEditor(QuickNoteFile, "[значение]");
        SeedSourceEditor(scratch);
        // MRU order: target immediately follows the source in Snap Assist.
        Activate(target);
        PrepareBrowserPractice();
        Navigate(Active, sourceBulletin.BriefPage);
    }

    internal void PrepareConfigurationHandoff(bool windows)
    {
        sourceConfiguration = ConfigurationCases[browserCaseVariant];
        sourceHandoff = "configuration";
        if (windows) PrepareWindowsDesktop();
        Terminal.CreateFolder(Project + "/archive");
        Terminal.CreateFolder(Project + "/logs");
        StoreSourceFile(ConfigFile, sourceConfiguration.Broken);
        StoreSourceFile(ConfigArchive, sourceConfiguration.Broken);
        StoreSourceFile(ConfigLog, sourceConfiguration.LogBody);
        var config = sourceConfiguration;
        Terminal.InstallUnitTest(ConfigTest, runner =>
        {
            string main = runner.ReadFile(Project + "/" + config.Config);
            string sample = runner.ReadFile(Project + "/archive/" + config.Archived);
            if (runner.Cwd != Project || main != config.Fixed || sample != config.Broken)
                throw new ArgumentException("Проверка не прошла: сравните рабочую настройку с журналом, сохраните её и не меняйте архив.");
            return "Профиль " + config.Config + ": " + config.Line + "Архивный пример не изменён.\nRan 2 tests\nOK\n";
        });
        SeedSourceEditor(ConfigFile);
        Ensure("terminal");
    }

    private static (string Title, string Body)? BulletinPage(string url)
    {
        foreach (var b in BulletinCases)
        {
            if (url == b.NewPage) return (b.VersionTitle(true), b.Body(true));
            if (url == b.OldPage) return (b.VersionTitle(false), b.Body(false));
            if (url == b.BriefPage) return ("Подтверждено · " + b.Title, b.Brief);
        }
        return ArchivedHandbookPage(url);
    }

    private LabEntry[] BulletinLinks(string url)
    {
        if (!BulletinWorkflow) return [];
        var b = sourceBulletin;
        if (url == b.NewPage)
            return [new(b.VersionTitle(false) + " · архив", b.OldPage, "page"),
                new(b.NewDownload, b.NewDownload, "download")];
        if (url == b.OldPage)
            return [new(b.VersionTitle(true) + " · актуально", b.NewPage, "page"),
                new(b.OldDownload, b.OldDownload, "download")];
        return [];
    }

    private LabEntry[] BulletinSearch(string[] terms)
    {
        if (!BulletinWorkflow || terms.Length == 0) return [];
        var b = sourceBulletin;
        var termsInSource = b.Query + " " + b.Title;
        if (!terms.All(term => termsInSource.Contains(term, StringComparison.OrdinalIgnoreCase))) return [];
        var current = new LabEntry(b.VersionTitle(true) + " · актуально", b.NewPage, "page");
        var archived = new LabEntry(b.VersionTitle(false) + " · архив", b.OldPage, "page");
        return browserCaseVariant == 1 ? [current, archived] : [archived, current];
    }

    private string BulletinDownload(string name) => !BulletinWorkflow ? null :
        name == sourceBulletin.NewDownload ? sourceBulletin.Csv(true) :
        name == sourceBulletin.OldDownload ? sourceBulletin.Csv(false) : null;

    private void RememberSourceMatch(LabWindow w, int start)
    {
        if (!BulletinWorkflow || w.App != "chrome") return;
        bool latest = w.Url == sourceBulletin.NewPage;
        if (!(latest || w.Url == sourceBulletin.OldPage) || w.Find != sourceBulletin.Key ||
            !w.Body.AsSpan(start).StartsWith(sourceBulletin.Row(latest), StringComparison.Ordinal)) return;
        if (latest) currentSourceRead = true; else archivedSourceRead = true;
    }

    internal string SourceRouteTarget(string value) => !ContextHandoffActive ? value : value switch
    {
        "@bulletin-current" => sourceBulletin.NewPage,
        "@bulletin-old" => sourceBulletin.OldPage,
        "@bulletin-csv" => sourceBulletin.NewDownload,
        "@bulletin-reveal" => RevealTitle(sourceBulletin.NewDownload),
        "@quick-note" => QuickNoteFile,
        "@config-log" => ConfigLog,
        _ => value
    };

    internal LabTask DescribeSourceTask(LabTask task)
    {
        if (!ContextHandoffActive) return task;
        if (SourceRevision) return DescribeRevision(task);
        if (BulletinWorkflow && task.Action == "find-next")
            return task with { Title = "Перейдите от упоминания к строке таблицы «" + sourceBulletin.Key + "»" };
        if (BulletinWorkflow && task.Action == "bookmark")
            return task with { Title = "Сохраните действующую редакцию в закладки" };
        if (BulletinWorkflow && task.Action == "close-tab")
            return task with { Title = "Закройте вкладку: следующий вход в документ будет из закладки" };
        if (ReportHandoff && (task.Action is "win-switch" or "switch-next"))
            return task with { Title = Active?.App == "chrome" ? "Перейдите к подготовленной заметке" :
                "Вернитесь к действующему бюллетеню, чтобы скачать его CSV" };
        if (ReportHandoff && task.Action == "save")
            return task with { Title = "Сохраните заметку со ссылкой и сверенной строкой" };
        if (DesktopHandoff && task.Action == "switch-group-next")
            return task with { Title = "Верните окно «" + sourceBulletin.QuickNote + "» из группы VS Code" };
        if (DesktopHandoff && task.Action == "win-overlay-next" && DesktopOverlay == "task-view")
            return task with { Title = "Выберите «" + sourceBulletin.QuickNote + "» в представлении задач" };
        if (sourceConfiguration != null && task.Action == "save")
            return task with { Title = "Сохраните исправленный рабочий профиль, не меняя пример" };
        return task;
    }

    internal string SourceReadTitle => Active?.App == "chrome"
        ? "Найдите ключ «" + SourceReadNeedle + "» в бюллетене"
        : sourceConfiguration == null ? "Найдите ключ «" + SourceReadNeedle + "» в полученном CSV" :
            "Найдите готовую строку «" + SourceReadNeedle + "» в журнале";

    private bool ReportMatchesSource()
    {
        string csv = Terminal.ReadFile(ReportCsvFile);
        if (csv == null) return false;
        var rows = csv.Split('\n').Where(line => line.StartsWith(sourceBulletin.Key + ",", StringComparison.Ordinal)).Take(2).ToArray();
        return rows.Length == 1 && rows[0] == sourceBulletin.Row(true) &&
            Terminal.ReadFile(SourceReportFile) == sourceBulletin.ReportComplete(rows[0] + "\n");
    }

    // Return only the observable data relevant to an action. A detour into
    // another tab must not make a legitimate handoff impossible.
    internal object SourceHandoffEvidence(LabTask task, string equivalentCommand)
    {
        if (!ContextHandoffActive) return null;
        if (SourceRevision && RevisionEvidence(task) is object proof) return proof;
        var w = Active;
        var tab = w?.Tab;
        object Selection() => w == null ? null : new { w.Focus, w.Url, w.Find, w.Input, w.Anchor, w.Cursor,
            excerpt = tab == null ? "" : Slice(w, FieldText(w)), bodyAnchor = tab?.Position.Anchor,
            bodyCursor = tab?.Position.Caret, currentSourceRead, archivedSourceRead };
        object SelectedEntry() => w == null ? null : Entries(w).ElementAtOrDefault(w.Focus == "quick-open" ? w.OptionIndex : w.Item);
        object Document() => new { w?.Url, w?.Focus, w?.Body, w?.Cursor, w?.Anchor };
        object Topology(bool editorState = false) => new
        {
            Workspace, WorkspaceCount, ActiveWindow, DesktopOverlay, DesktopOverlaySelection, DesktopOverlayWindows,
            windows = Windows.Select(view => new { view.Id, view.State, view.Workspace, view.MinimizedFrom }).ToArray(),
            active = editorState ? Document() : null
        };
        string command = equivalentCommand ?? Terminal.LastCommand;
        string action = task.Action;
        return action switch
        {
            "layout" or "win-layout" => Layout,
            "item-next" or "item-prev" or "quick-next" => new { w?.Focus, w?.Url, w?.Folder, entry = SelectedEntry() },
            "item-open" when BulletinWorkflow =>
                new { w?.App, w?.Focus, w?.Url, w?.Folder, entry = SelectedEntry(),
                    download = Terminal.ReadFile(DownloadPath), currentSourceRead, archivedSourceRead },
            "item-open" => new { w?.App, w?.Focus, w?.Folder, entry = SelectedEntry(), doc = w?.App == "code" ? Document() : null },
            "file-paste" when ReportHandoff => new { w?.Folder, entry = SelectedEntry(), moved = Terminal.ReadFile(ReportCsvFile),
                old = Terminal.ReadFile(ArchivedCsv), leftover = Terminal.ReadFile(DownloadPath) },
            "file-cut" or "file-copy" => new { Clipboard, fileCut, w?.Folder, entry = SelectedEntry() },
            "file-parent" or "win-explorer-parent" or "file-back" or "file-home" => new { w?.App, w?.Folder },
            "new-tab" or "next-tab" or "prev-tab" or "close-tab" or "restore-tab" or "first-tab" or "last-tab" =>
                new { w?.Focus, w?.Url, w?.ActiveTab, tabs = w?.Tabs.Select(t => t.Url).ToArray() },
            "bookmark" => BulletinWorkflow && w?.App == "chrome" && w.SavedBookmarks.Contains(sourceBulletin.NewPage),
            "history" or "bookmark-manager" or "downloads" or "navigate-pasted" => new { w?.Url, w?.Focus },
            "back" or "forward" or "quick-choose" or "document-end" or "paste" => Document(),
            "find-next" or "research-find" => new { w?.Url, w?.Focus, w?.Find,
                from = tab?.Position.Anchor, to = tab?.Position.Caret, currentSourceRead, archivedSourceRead },
            "select-all" or "select-line" or "find" or "escape" => Selection(),
            "url" => new { w?.App, w?.Url, w?.Focus, w?.Cursor, w?.Anchor },
            "copy" => new { Clipboard, w?.App, w?.Url, w?.Focus },
            "save" when ReportHandoff => new { w?.Url, final = Terminal.ReadFile(SourceReportFile), correct = ReportMatchesSource(),
                currentSourceRead, archivedSourceRead, bookmarked = Windows.Any(win => win.App == "chrome" && win.SavedBookmarks.Contains(sourceBulletin.NewPage)),
                archived = Terminal.ReadFile(ArchivedCsv), remaining = Terminal.ReadFile(DownloadPath) },
            "save" when DesktopHandoff => new { w?.Url, body = Terminal.ReadFile(QuickNoteFile),
                scratch = Terminal.ReadFile(Project + "/черновик.txt") },
            "save" when sourceConfiguration != null => new { w?.Url, body = Terminal.ReadFile(ConfigFile), archive = Terminal.ReadFile(ConfigArchive) },
            "command" => new { command, Terminal.Cwd, Terminal.LastSuccess, Terminal.LastOutput,
                checkedConfig = sourceConfiguration != null && command == SourceTestCommand ? Terminal.ReadFile(ConfigFile) : null },
            "open-files" or "win-explorer" or "quick-open" => new { w?.App, w?.Focus, w?.Folder },
            "open-chrome" or "open-code" or "open-terminal" or "launch" or "switch-next" or "switch-prev" or "win-switch" or
                "switch-group-next" or "switch-group-prev" or "win-overlay-open" => Topology(editorState: true),
            "tile-left" or "tile-right" or "maximize" or "restore" or "minimize" or "move-next" or "workspace-prev" or "workspace-next" or
                "win-left" or "win-right" or "win-down" or "win-task-view" or "win-overlay-next" or "win-overlay-prev" or
                "win-new-desktop" or "win-prev-desktop" or "win-next-desktop" => Topology(),
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void SourceHandoffMissions(List<LiveMission> list)
    {
        void Add(string app, int level, string id, string title, Action<LabScene> prepare, params LivePhase[] phases) =>
            list.Add(new(app, level, id, title, phases, FinishAfterRoute: true, Curated: true, Prepare: prepare));

        LivePhase[] CompareVersions() =>
        [
            new("Найдите действующий бюллетень, проверьте строку и сохраните источник",
                "url browser-search pick:@bulletin-current link-open find research-find find-next escape bookmark"),
            new("Откройте архивную редакцию и сравните ту же строку",
                "pick:@bulletin-old link-open find find-next escape"),
            new("Верните действующую версию из истории, чтобы не взять архивные данные",
                "history pick:@bulletin-current link-open")
        ];

        Add("chrome", 1, "chrome-compare-bulletin", "Сравнить редакции бюллетеня и сверить CSV",
            s => s.PrepareBulletin(false, false), [.. CompareVersions(),
                new("Скачайте файл именно действующей редакции, откройте его из загрузок и проверьте строку",
                    "pick:@bulletin-csv link-open downloads pick:@bulletin-csv link-open find research-find escape")]);

        foreach (bool windows in new[] { false, true })
        {
            string profile = windows ? "windows-" : "";
            string change = windows ? "win-switch" : "switch-next";
            string explorer = windows ? "win-explorer" : "open-files";
            Add("workday", 1, "workday-" + profile + "verified-bulletin", "Выпустить заметку по актуальному бюллетеню",
                s => s.PrepareBulletin(windows, true), [.. CompareVersions(),
                    new("Закройте проверенную вкладку, перенесите её ссылку в заметку и вернитесь к источникам",
                        "url copy escape close-tab " + change + " paste " + change),
                    new("Откройте актуальную редакцию из закладки, скачайте её CSV и поместите в папку отчётов",
                        "bookmark-manager pick:@bulletin-current link-open pick:@bulletin-csv link-open downloads pick:@bulletin-reveal link-open file-cut file-route-parent pick:project file-open pick:reports file-open file-paste"),
                    new("Откройте файл по новому пути, перенесите строку из CSV и сохраните заметку",
                        "file-open find research-find escape select-line copy next-tab code-document-end paste save")]);

            Add("workday", 2, "workday-" + profile + "log-config-repair", "Исправить настройку по журналу проверки",
                s => s.PrepareConfigurationHandoff(windows),
                new("Прочитайте ошибку и отличите рабочий профиль от архивного примера",
                    "cmd:cd~/home/student/project source-log-command"),
                new("Найдите рекомендованную строку в журнале и перенесите её в рабочий профиль",
                    change + " quick-open quick:@config-log quick-choose find research-find escape select-line copy next-tab select-line paste save"),
                new("Проверьте сохранённое исправление и нетронутый архив",
                    change + " source-config-test"));

            string desktop = windows ? "windows" : "ubuntu";
            Add(desktop, 1, windows ? "windows-desktop-source-handoff" : "ubuntu-source-window-handoff",
                "Сверить источник и вернуться к нужному черновику", s => s.PrepareDesktopHandoff(windows),
                windows ? new("Разместите источник слева, а правильный черновик справа через Snap Assist", "win-left win-overlay-open")
                        : new("Разместите источник слева, а черновик редактора справа", "tile-left open-code tile-right"),
                windows ? new("Освободите экран для источника, скопируйте значение и верните окно через представление задач",
                            "win-down win-down select-all copy win-task-view win-overlay-next win-overlay-open")
                        : new("Скопируйте значение и уберите источник на соседний стол; вернитесь через другое окно редактора",
                            "minimize select-all copy move-next workspace-prev switch-group-next"),
                new("Продолжите на прежней каретке, сохраните значение и проверьте файл в проводнике",
                    "paste save " + (windows ? "win-new-desktop " : "") + explorer + " pick:project file-open pick:@quick-note file-open"));
        }
        SourceRevisionMissions(list);
    }
}
