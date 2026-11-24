using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TouchTypingStudio;

internal sealed class FieldPosition
{
    internal int Caret, Anchor;
    internal FieldPosition Clone() => new() { Caret = Caret, Anchor = Anchor };
}

internal sealed record EditRevision(string Text, int Cursor, int Anchor);

internal sealed class LabTab(string title, string url, string body)
{
    internal string Id = Guid.NewGuid().ToString("N")[..8];
    internal string Title = title, Url = url, Body = body, SavedBody = body, Origin;
    internal readonly List<string> History = [url];
    internal int HistoryIndex;
    internal readonly Dictionary<int, int> PracticeHistoryItems = [];
    internal FieldPosition Position = new();
    internal readonly List<int> ExtraMatches = [];
    internal readonly Stack<EditRevision> Undo = [], Redo = [];
    internal object Snapshot(bool editorDetail = false) => new { Id, Title, Url, Body, SavedBody, modified = Body != SavedBody, syntax = editorDetail ? PythonHighlight.For(Body, Url) : [] };
    internal LabTab Clone()
    {
        var copy = new LabTab(Title, Url, Body) { Id = Id, Origin = Origin, SavedBody = SavedBody, HistoryIndex = HistoryIndex, Position = Position.Clone() };
        foreach (var item in Undo.Reverse()) copy.Undo.Push(item);
        foreach (var item in Redo.Reverse()) copy.Redo.Push(item);
        copy.ExtraMatches.AddRange(ExtraMatches);
        copy.History.Clear(); copy.History.AddRange(History);
        foreach (var item in PracticeHistoryItems) copy.PracticeHistoryItems.Add(item.Key, item.Value);
        return copy;
    }
}

internal sealed class LabWindow(string app, int number, int workspace)
{
    internal readonly string Id = "window-" + number;
    internal readonly string App = app;
    internal string Title = app switch { "chrome" => "Chrome", "code" => "Visual Studio Code", "terminal" => "Терминал", _ => "Файлы" };
    internal int Workspace = workspace;
    internal int SearchIndex, UntitledCount, Group, Activation;
    internal string ShellInput = "";
    internal string OtherTab, ImagePreview;
    internal string View = "explorer", Query = "", ProjectSearchQuery = "";
    internal string Folder = "/home/student";
    internal int Item = -1;
    internal readonly Stack<EditorJump> SymbolBack = [];
    internal SymbolLocation[] SymbolReferences = [];
    internal SymbolBinding PendingSymbol;
    internal readonly Stack<string> FolderBack = [], FolderForward = [];
    internal string State = "normal", Focus = "body", Find = "", Status = "Готово";
    internal string MinimizedFrom = "normal", UntileTo = "normal";
    internal int NormalSlot => int.Parse(Id.AsSpan(7)) - 1;
    internal bool Sidebar = true, Split, Panel, Bookmarks;
    private readonly Dictionary<string, FieldPosition> positions = [];
    private readonly Dictionary<string, string> fields = [];
    internal string Input { get => fields.GetValueOrDefault(Focus, ""); set => fields[Focus] = value; }
    private FieldPosition Position
    {
        get
        {
            if (Focus == "body" && Tab != null) return Tab.Position;
            if (!positions.TryGetValue(Focus, out var position)) positions[Focus] = position = new();
            return position;
        }
    }
    internal int Cursor { get => Position.Caret; set { Position.Caret = Position.Anchor = value; } }
    internal int Anchor => Position.Anchor;
    internal int Start => Math.Min(Cursor, Anchor);
    internal int End => Math.Max(Cursor, Anchor);
    internal void Select(int anchor, int caret) { Position.Anchor = anchor; Position.Caret = caret; }
    internal bool Selected
    {
        get => Anchor != Cursor;
        set { if (value) Select(0, Focus == "body" ? Body.Length : Input.Length); else Cursor = Cursor; }
    }
    internal void RepairLegacyFieldPositions(Func<string, int, int> snap)
    {
        foreach (var field in positions)
        {
            if (App == "terminal" || field.Key == "terminal")
            {
                field.Value.Caret = snap(ShellInput, field.Value.Caret);
                field.Value.Anchor = snap(ShellInput, field.Value.Anchor);
            }
            else if (fields.TryGetValue(field.Key, out var text))
            {
                field.Value.Caret = snap(text, field.Value.Caret);
                field.Value.Anchor = snap(text, field.Value.Anchor);
            }
        }
    }
    internal readonly HashSet<string> SavedBookmarks = [];
    internal int ActiveTab, Zoom = 100, OptionIndex;
    internal readonly List<LabTab> Tabs = [];
    internal readonly Stack<LabTab> ClosedTabs = [];
    internal Stack<EditRevision> Undo => Tab.Undo;
    internal Stack<EditRevision> Redo => Tab.Redo;
    internal LabTab Tab => Tabs.Count == 0 ? null : Tabs[Math.Clamp(ActiveTab, 0, Tabs.Count - 1)];
    internal string Body { get => Tab?.Body ?? ""; set { if (Tab != null) Tab.Body = value; } }
    internal string Url => Tab?.Url ?? "";
    internal object Snapshot(bool active, LabEntry[] entries, IEnumerable<string> projectFiles, IEnumerable<string> changedFiles, IEnumerable<string> searchResults, string projectName, string projectRelative, string windowsPath = null, bool browserPractice = false) => new { windowsPath, browserPractice, syntax = App == "code" ? PythonHighlight.For(Body, Url) : [], projectFiles, changedFiles, searchResults, projectName, projectRelative, ProjectSearchQuery, Id, App, Title, Workspace, State, NormalSlot, restoreState = MinimizedFrom, active, Folder, Item, Group, ImagePreview, otherTab = Tabs.Find(tab => tab.Id == OtherTab)?.Snapshot(App == "code"), View, Query, matches = Tab?.ExtraMatches.ToArray() ?? [], entries,
        tabs = Tabs.Select(tab => tab.Snapshot()), ActiveTab, Url, Body, Focus, Input, Find, Sidebar, Split, Status, Zoom, Selected, Cursor, Anchor, Panel, bodyCursor = Tab?.Position.Caret ?? 0, bodyAnchor = Tab?.Position.Anchor ?? 0, OptionIndex, Bookmarks, savedBookmarks = SavedBookmarks.ToArray() };
    internal LabWindow Clone()
    {
        var copy = new LabWindow(App, int.Parse(Id[7..]), Workspace)
        {
            Activation = Activation, ShellInput = ShellInput, UntitledCount = UntitledCount, ImagePreview = ImagePreview, OtherTab = OtherTab, Group = Group, Folder = Folder, Item = Item, View = View, Query = Query, ProjectSearchQuery = ProjectSearchQuery, SearchIndex = SearchIndex, Title = Title, State = State, Focus = Focus, Input = Input, Find = Find, Status = Status,
            Sidebar = Sidebar, Split = Split, Panel = Panel, Bookmarks = Bookmarks,
            MinimizedFrom = MinimizedFrom, UntileTo = UntileTo,
            ActiveTab = ActiveTab, Zoom = Zoom, OptionIndex = OptionIndex
        };
        foreach (var jump in SymbolBack.Reverse()) copy.SymbolBack.Push(jump);
        copy.SymbolReferences = [.. SymbolReferences];
        copy.PendingSymbol = PendingSymbol;
        foreach (string path in FolderBack.Reverse()) copy.FolderBack.Push(path);
        foreach (string path in FolderForward.Reverse()) copy.FolderForward.Push(path);
        copy.Tabs.AddRange(Tabs.Select(tab => tab.Clone()));
        foreach (var tab in ClosedTabs.Reverse()) copy.ClosedTabs.Push(tab.Clone());
        foreach (var item in fields) copy.fields[item.Key] = item.Value;
        foreach (var item in positions) copy.positions[item.Key] = item.Value.Clone();
        copy.SavedBookmarks.UnionWith(SavedBookmarks);
        return copy;
    }
}

internal sealed partial class LabScene
{
    internal readonly List<LabWindow> Windows = [];
    internal readonly VirtualTerminal Terminal;
    internal int Workspace, WorkspaceCount = 3, Version, Screenshots;
    internal string[] Dock = ["chrome", "code", "terminal", "files"];
    internal string LayoutKey = "Super+Space", SwitchTarget;
    internal string Layout, Clipboard = "build: 17 tests passed", ActiveWindow;
    internal bool Overview, Switcher, Screenshot, ScreenshotReady, ScreenshotCopied, ShowDesktop, LayoutIsVirtual;
    internal bool ReplayLegacyNavigation, ReplayLegacyLineEditing, UseWindowsBindings;
    internal bool GnomeWindows;
    internal const string GnomeNoteText = "gnome";
    private string GnomeCodeBody => NeutralWorkspace ? "# \nfrom tasks import add_task\n" : "# \nfrom src.spectrum import fft\n";

    internal void PrepareGnomeWindowMission()
    {
        GnomeWindows = true;
        const string file = "/home/student/project/app.py";
        Terminal.StoreFile(file, GnomeCodeBody);
        originalFiles[file] = GnomeCodeBody;
        Open("chrome"); // Python documentation is the first tab by default.
    }
    internal string Search = "";
    internal string TerminalInput { get => Active?.ShellInput ?? ""; set { if (Active != null) Active.ShellInput = value; } }
    private int windowNumber;
    private readonly List<string> commands = [];
    private int commandIndex;
    private string historyDraft = "", killedText = "";
    internal bool CanHistoryUp => commandIndex > 0;
    internal bool CanHistoryDown => commandIndex < commands.Count;
    internal LabWindow Active => Windows.FirstOrDefault(window => window.Id == ActiveWindow);

    internal bool NeutralWorkspace { get; }
    internal LabScene(string app, string layout, bool neutralProject = false)
    {
        NeutralWorkspace = neutralProject;
        Terminal = new VirtualTerminal(NeutralWorkspace);
        Terminal.CreateFolder("~/Pictures/Screenshots");
        Terminal.CreateFolder("~/.Trash");
        if (NeutralWorkspace)
        {
            foreach (var exercise in NeutralProject.Exercises)
                Terminal.StoreFile("~/project/" + exercise.Path, exercise.Code);
            if (app != "code")
                Terminal.StoreFile("~/project/notes.txt", "add\nfind\nsave\nfind\n");
            Terminal.InstallUnitTest(NeutralProject.TestSelector, NeutralProject.RunTests);
        }
        else
        {
            Terminal.CreateFolder("~/project/src");
            foreach (var group in TypingRun.Project.GroupBy(item => item.Path))
                Terminal.StoreFile("~/project/" + group.Key, string.Join("\n\n", group.Select(item => item.Code)) + "\n");
        }
        foreach (var path in ProjectDocuments()) originalFiles[path] = Terminal.ReadFile(path);
        Layout = layout == "RU" ? "RU" : "EN";
        Clipboard = app == "terminal" ? "pwd" : "build: 17 tests passed";
        if (app == "windows") { WindowsDesktop = true; WorkspaceCount = 1; }
        else if (app != "ubuntu") Open(app);
    }

    private LabScene(LabScene source)
    {
        NeutralWorkspace = source.NeutralWorkspace;
        BrowserPracticeActive = source.BrowserPracticeActive; RepoCloned = source.RepoCloned; BrowserPracticeQuery = source.BrowserPracticeQuery;
        Terminal = source.Terminal.Clone(); captureImage = source.captureImage;
        CloneDebugDay(source);
        symbolWorkspace = source.symbolWorkspace;
        SearchProjectRoot = source.SearchProjectRoot; ExactEdit = source.ExactEdit;
        foreach (var file in source.originalFiles) originalFiles[file.Key] = file.Value;
        Dock = [.. source.Dock]; LayoutKey = source.LayoutKey; SwitchTarget = source.SwitchTarget;
        Workspace = source.Workspace; WorkspaceCount = source.WorkspaceCount; Version = source.Version;
        Screenshots = source.Screenshots; Layout = source.Layout; Clipboard = source.Clipboard;
        ActiveWindow = source.ActiveWindow; Overview = source.Overview; Switcher = source.Switcher;
        Screenshot = source.Screenshot; ScreenshotReady = source.ScreenshotReady;
        ScreenshotCopied = source.ScreenshotCopied; ShowDesktop = source.ShowDesktop;
        UseWindowsBindings = source.UseWindowsBindings;
        WindowsDesktop = source.WindowsDesktop; DesktopOverlay = source.DesktopOverlay;
        DesktopOverlaySelection = source.DesktopOverlaySelection; DesktopOverlayWindows = [.. source.DesktopOverlayWindows];
        LayoutIsVirtual = source.LayoutIsVirtual; ReplayLegacyNavigation = source.ReplayLegacyNavigation; Search = source.Search;
        GnomeWindows = source.GnomeWindows; GnomePractice = source.GnomePractice; DesktopProfile = [.. source.DesktopProfile]; SwitchChoices = [.. source.SwitchChoices];
        ReplayLegacyLineEditing = source.ReplayLegacyLineEditing;
        ReplayLegacyPythonEditing = source.ReplayLegacyPythonEditing; PythonEditingExercise = source.PythonEditingExercise;
        windowNumber = source.windowNumber;
        historyDraft = source.historyDraft; killedText = source.killedText;
        fileTransfer = source.fileTransfer; fileCut = source.fileCut; trashSequence = source.trashSequence;
        foreach (var item in source.trash) trash[item.Key] = item.Value;
        recentUrls.AddRange(source.recentUrls); downloads.AddRange(source.downloads);
        commands.AddRange(source.commands); commandIndex = source.commandIndex;
        Windows.AddRange(source.Windows.Select(window => window.Clone()));
    }

    internal LabScene Clone() => new(this);

    internal LabWindow Open(string app)
    {
        var window = new LabWindow(app, ++windowNumber, Workspace);
        if (app == "chrome")
        {
            window.Tabs.Add(new LabTab("Документация Python", "https://docs.python.org/3/", "Python\nДокументация языка Python.\nФункции, модули и примеры."));
            window.Tabs.Add(NeutralWorkspace
                ? new LabTab("Учебный проект task-list", NeutralProject.ProjectPage, NeutralProject.ProjectBody)
                : new LabTab("Проект piling-main", "https://github.com/veitnemed/piling-main", "piling-main\nСигналы, спектр и направляющие векторы."));
        }
        else if (app == "code")
        {
            window.Tabs.Add(new LabTab("app.py", "app.py", Terminal.ReadFile("~/project/app.py") ?? ""));
            if (!NeutralWorkspace || GnomeWindows)
                window.Tabs.Add(new LabTab("README.md", "README.md", Terminal.ReadFile("~/project/README.md") ??
                    "# task-list\nДобавьте задачу, найдите по названию и сохраните список в JSON.\n"));
            if (GnomeWindows) window.Cursor = 2; // after "# " in the actual first tab
        }
        else if (app == "files")
        {
            if (WindowsDesktop) window.Title = "Проводник";
            window.Tabs.Add(new LabTab("Домашняя папка", "/home/student", "project/\nДокументы/\nЗагрузки/"));
        }
        if (app == "chrome" && recentUrls.Count == 0)
            recentUrls.AddRange(window.Tabs.Select(tab => tab.Url));
        Windows.Add(window); Activate(window); Version++; return window;
    }

    internal void Activate(LabWindow window)
    {
        if (window == null) return;
        window.Activation = Windows.Max(w => w.Activation) + 1;
        ActiveWindow = window.Id; Workspace = window.Workspace; ShowDesktop = Overview = false;
        if (window.State == "minimized") window.State = GnomeWindows || WindowsDesktop ? window.MinimizedFrom : "normal";
    }

    internal bool Has(string app) => Windows.Any(window => window.App == app);
    internal void Ensure(string app)
    {
        var window = Windows.FirstOrDefault(item => item.App == app);
        if (window == null) Open(app); else Activate(window);
    }

    internal bool Apply(string action)
    {
        try { return ApplyCore(action); }
        catch (ArgumentException error) { if (Active != null) Active.Status = error.Message; return false; }
    }

    private bool ApplyCore(string action)
    {
        if (action.StartsWith("win-", StringComparison.Ordinal)) return ApplyWindowsAction(action);
        var window = Active;
        if (ApplyDebugDayAction(action, window) is bool debug) return debug;
        bool changed = true;
        switch (action)
        {
            case "overview": Overview = !Overview; Search = ""; break;
            case "switch-next": Switch(1); break;
            case "switch-prev": Switch(-1); break;
            case "switch-group-next": Switch(1, sameApp: true); break;
            case "switch-group-prev": Switch(-1, sameApp: true); break;
            case "gnome-left-super": if (GnomePractice != "matebook") return false; Overview = !Overview; Search = ""; break;
            case "open-chrome": Ensure("chrome"); break;
            case "open-code": Ensure("code"); break;
            case "open-terminal": Ensure("terminal"); break;
            case "open-files": Ensure("files"); break;
            case "close-window":
                if (window == null) return false;
                Windows.Remove(window); ActiveWindow = null;
                Activate(GnomeWindows ? Windows.Where(item => item.Workspace == Workspace && item.State != "minimized").MaxBy(item => item.Activation)
                    : Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "maximize": if (window == null) return false; window.State = "maximized"; break;
            case "restore": if (window == null) return false; window.State = window.UntileTo = "normal"; break;
            case "tile-left": return TileWindow(window, "left");
            case "tile-right": return TileWindow(window, "right");
            case "minimize":
                if (window == null) return false;
                if (GnomeWindows) window.MinimizedFrom = window.State;
                window.State = "minimized"; ActiveWindow = null;
                Activate(Windows.Where(item => item.Workspace == Workspace && item.State != "minimized").MaxBy(item => item.Activation)); break;
            case "desktop": ShowDesktop = !ShowDesktop; break;
            case "workspace-next" when GnomePractice == "workspaces": return GoGnomeWorkspace(1);
            case "workspace-prev" when GnomePractice == "workspaces": return GoGnomeWorkspace(-1);
            case "move-next" when GnomePractice == "workspaces": return window != null && GoGnomeWorkspace(1, window);
            case "move-prev" when GnomePractice == "workspaces": return window != null && GoGnomeWorkspace(-1, window);
            case "workspace-next": Workspace = (Workspace + 1) % WorkspaceCount; ActiveWindow = null; Activate(Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "workspace-prev": Workspace = (Workspace + WorkspaceCount - 1) % WorkspaceCount; ActiveWindow = null; Activate(Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "move-next": if (window == null) return false; window.Workspace = (Workspace + 1) % WorkspaceCount; Workspace = window.Workspace; break;
            case "move-prev": if (window == null) return false; window.Workspace = (Workspace + WorkspaceCount - 1) % WorkspaceCount; Workspace = window.Workspace; break;
            case "layout": Layout = Layout == "EN" ? "RU" : "EN"; LayoutIsVirtual = true; break;
            case "screenshot": Screenshot = true; ScreenshotReady = false; ScreenshotCopied = false; break;
            case "screenshot-confirm": if (!Screenshot) return false; Screenshot = false; ScreenshotReady = true; Screenshots++; ScreenshotCopied = true;
                Capture(); break;
            case "copy-shot": if (!ScreenshotReady) return false; ScreenshotCopied = true; Clipboard = "Снимок-" + Screenshots + ".png"; break;
            case "escape":
                if (window?.Focus == "reverse-search") return Text("Esc", "");
                Overview = Switcher = Screenshot = ScreenshotReady = false;
                if (window != null)
                {
                    window.ImagePreview = null;
                    if (window.Focus != "body") window.Input = "";
                    window.Focus = "body"; // restores the active tab's independent caret/selection
                }
                break;
            case "navigate-pasted":
                if (window?.App != "chrome" || window.Focus != "url" || string.IsNullOrWhiteSpace(window.Input)) return false;
                return Text("Enter", "");
            case "terminal-submit":
                if (window == null || window.App != "terminal" && window.Focus != "terminal" || string.IsNullOrWhiteSpace(TerminalInput)) return false;
                return Text("Enter", "") && Terminal.LastSuccess;
            default: changed = ApplyBrowserPractice(action, window) ?? ApplyExactEdit(action, window) ?? ApplyProjectSearch(action, window) ?? ApplyDiagnostics(action, window) ?? ApplySymbols(action, window) ?? ApplyWorkspace(action, window) ?? ApplyWindow(action, window); break;
        }
        if (changed) Version++;
        return changed;
    }

    private bool TileWindow(LabWindow window, string side)
    {
        if (window == null) return false;
        if (GnomeWindows && window.State == side)
            window.State = window.UntileTo;
        else
        {
            if (GnomeWindows && window.State is not ("left" or "right"))
                window.UntileTo = window.State == "maximized" ? "maximized" : "normal";
            window.State = side;
        }
        Version++;
        return true;
    }

    private void Switch(int direction, bool sameApp = false)
    {
        string app = Active?.App;
        var visible = Windows.Where(item => item.Workspace == Workspace && (!sameApp || item.App == app))
            .OrderByDescending(item => item.Activation).ToArray();
        if (visible.Length == 0) return;
        int index = Array.FindIndex(visible, item => item.Id == (Switcher ? SwitchTarget : ActiveWindow));
        SwitchTarget = visible[(index + direction + visible.Length) % visible.Length].Id; Switcher = true;
        SwitchChoices = visible.Select(item => item.Id).ToArray();
    }

    internal void CommitSwitch()
    {
        if (Switcher) Activate(Windows.Find(item => item.Id == SwitchTarget));
        SwitchTarget = null; Switcher = false; SwitchChoices = [];
    }

    internal LabTask WithBinding(LabTask task)
    {
        if (task.Id == "code-back" && UseWindowsBindings) return task with { Keys = "Alt+Left" };
        if (task.Id == "layout") return task with { Keys = LayoutKey };
        task = GnomeProfileBinding(task);
        if (task.Id is "open-chrome" or "open-code" or "open-files")
        {
            string app = task.Id[5..]; int slot = Array.IndexOf(Dock, app);
            if (slot >= 0 && slot < 9) return task with { Keys = "Super+" + (slot + 1) };
            return task with { Keys = "", Text = Layout == "RU" ? app switch { "chrome" => "хром", "code" => "код", _ => "файлы" } : app, Hint = "Откройте обзор клавишей ⊞, введите название и нажмите Enter.", Action = "launch" };
        }
        return task;
    }

    private bool ApplyWindow(string action, LabWindow window)
    {
        if (window == null) return false;
        bool browser = window.App == "chrome", code = window.App == "code", terminal = window.App == "terminal" || window.Focus == "terminal";
        switch (action)
        {
            case "new-tab":
                if (!browser && !code) return false;
                string origin = window.Tab?.Id;
                string name = code ? "notes-" + (++window.UntitledCount) + ".py" : "";
                window.Tabs.Add(browser ? new LabTab("Новая вкладка", "about:blank", "") : new LabTab(name, name, "") { Origin = origin });
                window.ActiveTab = window.Tabs.Count - 1; window.Focus = browser ? "url" : "body"; window.Input = ""; window.Cursor = 0; break;
            case "close-tab":
                if (window.Tabs.Count <= 1) return false;
                var closing = window.Tab;
                window.ClosedTabs.Push(closing); window.Tabs.RemoveAt(window.ActiveTab);
                int parent = window.Tabs.FindIndex(t => t.Id == closing.Origin);
                window.ActiveTab = parent >= 0 ? parent : Math.Min(window.ActiveTab, window.Tabs.Count - 1);
                if (closing.Id == window.OtherTab) { window.Split = false; window.Group = 0; window.OtherTab = null; }
                window.Focus = "body"; break;
            case "restore-tab":
                if (!window.ClosedTabs.TryPop(out var restored)) return false;
                window.Tabs.Add(restored); window.ActiveTab = window.Tabs.Count - 1;
                window.Focus = "body"; break;
            case "next-tab": case "prev-tab":
                if (window.Tabs.Count < 2) return false;
                window.ActiveTab = (window.ActiveTab + (action == "next-tab" ? 1 : window.Tabs.Count - 1)) % window.Tabs.Count;
                window.Focus = "body"; break;
            case "first-tab": window.ActiveTab = 0; window.Focus = "body"; break;
            case "last-tab": window.ActiveTab = Math.Max(0, window.Tabs.Count - 1); window.Focus = "body"; break;
            case "url": if (!browser) return false; window.Focus = "url"; window.Input = window.Url; window.Selected = true; break;
            case "find":
                string needle = window.Focus == "body" && window.Selected ? Slice(window, window.Body).TrimEnd('\n') : window.Find;
                window.Focus = "find"; window.Input = window.Find = needle; window.Select(0, needle.Length);
                if (needle.Length > 0) FindMatch(window, false); break;
            case "find-next": return FindMatch(window, true);
            case "refresh": if (!browser) return false; window.Status = "Страница обновлена"; break;
            case "back": case "forward":
                if (!browser || window.Tab == null) return false;
                int destination = window.Tab.HistoryIndex + (action == "back" ? -1 : 1);
                if (destination < 0 || destination >= window.Tab.History.Count) return false;
                if (BrowserPracticeActive) window.Tab.PracticeHistoryItems[window.Tab.HistoryIndex] = window.Item;
                window.Tab.HistoryIndex = destination; SetPage(window.Tab, window.Tab.History[destination]);
                if (BrowserPracticeActive)
                {
                    window.Item = window.Tab.PracticeHistoryItems.GetValueOrDefault(destination, -1);
                    window.Focus = "body";
                }
                break;
            case "fullscreen": window.State = window.State == "fullscreen" ? "normal" : "fullscreen"; break;
            case "zoom-in": window.Zoom = Math.Min(200, window.Zoom + 10); break;
            case "zoom-out": window.Zoom = Math.Max(50, window.Zoom - 10); break;
            case "zoom-reset": window.Zoom = 100; break;
            case "bookmark": window.SavedBookmarks.Add(window.Url); window.Status = BrowserPracticeActive
                    ? "Сохранено в закладки: «" + window.Tab.Title + "». Открыть закладки: Ctrl+Shift+O."
                    : "Закладка сохранена"; break;
            case "bookmarks": window.Bookmarks = !window.Bookmarks; break;
            case "sidebar": if (!code) return false; window.Sidebar = !window.Sidebar; if (!window.Sidebar) window.Focus = "body"; break;
            case "split":
                if (!code) return false;
                window.OtherTab = window.Tab.Id; window.Split = true; window.Group = 1; window.Focus = "body"; break;
            case "quick-open":
                if (!code) return false; window.Focus = "quick-open"; window.Cursor = 0; window.Input = ""; window.OptionIndex = 0; break;
            case "quick-next":
                var options = Entries(window);
                if (options.Length == 0) return false;
                window.OptionIndex = (window.OptionIndex + 1) % options.Length; break;
            case "quick-choose":
                var chosen = Entries(window).ElementAtOrDefault(window.OptionIndex);
                if (chosen == null) return false;
                OpenDocument(chosen.Path); break;
            case "palette": if (!code) return false; window.Focus = "palette"; window.Cursor = 0; window.Input = ""; window.OptionIndex = 0; break;
            case "palette-next": if (!code || window.Focus != "palette") return false; window.OptionIndex = (window.OptionIndex + 1) % 3; break;
            case "palette-choose":
                if (!code || window.Focus != "palette") return false;
                if (window.OptionIndex == 1) window.Sidebar = !window.Sidebar;
                else if (window.OptionIndex == 2) window.Split = !window.Split;
                else { Remember(window); window.Body = string.Join("\n", window.Body.Split('\n').Select(line => line.TrimEnd())); }
                window.Focus = "body"; window.Input = ""; break;
            case "goto-line": if (!code) return false; window.Focus = "goto-line"; window.Input = ""; window.Cursor = 0; break;
            case "explorer": if (!code) return false; window.View = "explorer"; window.Sidebar = true; window.Focus = "explorer"; break;
            case "project-search": if (!code) return false; window.View = "project-search"; window.Sidebar = true; window.Focus = "project-search"; window.Input = ""; window.Cursor = 0; window.OptionIndex = 0; break;
            case "panel":
                window.Panel = !window.Panel;
                if (!window.Panel && window.Focus == "terminal") window.Focus = "body";
                break;
            case "extensions": window.View = "extensions"; window.Sidebar = true; window.Focus = "extensions"; break;
            case "terminal-toggle":
                window.Panel = window.Focus != "terminal";
                window.Focus = window.Panel ? "terminal" : "body"; break;
            case "select-line":
                int first = LineStart(window.Body, window.Start);
                int last = LineEnd(window.Body, window.End);
                window.Select(first, Math.Min(window.Body.Length, last + 1)); break;
            case "save": if (!code || window.Tab == null) return false; Terminal.StoreFile(EditorPath(window.Tab), window.Body); window.Tab.SavedBody = window.Body; window.Status = "Файл «" + window.Tab.Title + "» сохранён"; break;
            case "select-all": window.Selected = true; break;
            case "terminal-copy":
                if (!window.Selected) return false;
                Clipboard = Slice(window, TerminalInput); break;
            case "copy": case "cut":
                var content = FieldText(window);
                int start = Math.Clamp(window.Start, 0, content.Length), end = Math.Clamp(window.End, start, content.Length);
                if (start == end && code && window.Focus == "body")
                {
                    start = LineStart(content, start); end = Math.Min(content.Length, LineEnd(content, end) + 1);
                }
                if (start == end) return false;
                Clipboard = content[start..end];
                if (action == "cut") { window.Select(start, end); Insert(window, ""); }
                break;
            case "paste": Insert(window, Clipboard); break;
            case "undo": case "redo":
                var from = action == "undo" ? window.Undo : window.Redo;
                var to = action == "undo" ? window.Redo : window.Undo;
                if (!from.TryPop(out var revision)) return false;
                to.Push(Revision(window)); window.Body = revision.Text;
                window.Tab.ExtraMatches.Clear(); window.Select(revision.Anchor, revision.Cursor); break;
            case "comment":
                if (!code) return false;
                if (!ReplayLegacyLineEditing) return ToggleLineComment(window);
                int lineStart = LineStart(window.Body, window.Cursor);
                bool commented = window.Body.AsSpan(lineStart).StartsWith("# ");
                window.Select(lineStart, lineStart + (commented ? 2 : 0));
                Insert(window, commented ? "" : "# "); break;
            case "delete-line":
                if (!code) return false;
                if (!ReplayLegacyLineEditing) return DeleteLines(window);
                Remember(window);
                int begin = LineStart(window.Body, window.Cursor);
                int lineEnd = window.Body.IndexOf('\n', Math.Min(window.Cursor, window.Body.Length)); lineEnd = lineEnd < 0 ? window.Body.Length : lineEnd + 1;
                window.Body = window.Body.Remove(begin, lineEnd - begin); window.Cursor = begin; break;
            case "terminal-clear": if (!terminal) return false; Terminal.Execute("clear"); break;
            case "terminal-cancel": if (!terminal) return false; TerminalInput = ""; window.Cursor = 0; window.Status = "Ввод команды отменён"; break;
            case "terminal-home": if (!terminal) return false; window.Cursor = 0; break;
            case "terminal-end": if (!terminal) return false; window.Cursor = TerminalInput.Length; break;
            case "terminal-kill": if (!terminal) return false; killedText = TerminalInput[window.Cursor..]; TerminalInput = TerminalInput[..window.Cursor]; break;
            case "terminal-yank": Insert(window, killedText); break;
            case "terminal-erase": if (!terminal) return false; killedText = TerminalInput[..window.Cursor]; TerminalInput = TerminalInput[window.Cursor..]; window.Cursor = 0; break;
            case "terminal-history-up": case "terminal-history-down":
                if (!terminal || commands.Count == 0) return false;
                if (commandIndex == commands.Count) historyDraft = TerminalInput;
                commandIndex = Math.Clamp(commandIndex + (action == "terminal-history-up" ? -1 : 1), 0, commands.Count);
                TerminalInput = commandIndex == commands.Count ? historyDraft : commands[commandIndex]; window.Cursor = TerminalInput.Length; break;
            default: return false;
        }
        return true;
    }

    private static EditRevision Revision(LabWindow window) => new(window.Body, window.Cursor, window.Anchor);
    private static void Remember(LabWindow window) { window.Undo.Push(Revision(window)); window.Redo.Clear(); }
    private static int LineStart(string value, int at) => at <= 0 || value.Length == 0 ? 0 : value.LastIndexOf('\n', Math.Min(value.Length, at) - 1) + 1;
    private static int LineEnd(string value, int at) => value.IndexOf('\n', Math.Clamp(at, 0, value.Length)) is int n && n >= 0 ? n : value.Length;
    // A nonempty selection covers [start, end). Ctrl+L deliberately includes
    // the line break, making end the following line's start, not its content.
    private static (int First, int Last) AffectedLines(LabWindow w)
    {
        string body = w.Body;
        int start = Math.Clamp(w.Start, 0, body.Length), end = Math.Clamp(w.End, start, body.Length);
        return (LineStart(body, start), LineStart(body, end > start ? end - 1 : start));
    }

    private static bool ToggleLineComment(LabWindow w)
    {
        string source = w.Body;
        var (first, last) = AffectedLines(w);
        var lines = new List<(int Start, int Marker, int End)>();
        for (int begin = first; ;)
        {
            int end = LineEnd(source, begin), marker = begin;
            while (marker < end && source[marker] is ' ' or '\t') marker++;
            lines.Add((begin, marker, end));
            if (begin >= last) break;
            begin = end + 1;
        }
        var meaningful = lines.Where(line => line.Marker < line.End).ToArray();
        if (meaningful.Length == 0) return false;
        bool strip = meaningful.All(line => source[line.Marker] == '#');
        var edits = meaningful.Select(line => (Offset: line.Marker,
            Remove: strip ? (line.Marker + 1 < line.End && source[line.Marker + 1] == ' ' ? 2 : 1) : 0)).ToArray();
        int Map(int position, bool leftAffinity)
        {
            int delta = 0;
            foreach (var edit in edits)
            {
                if (position < edit.Offset || position == edit.Offset && leftAffinity) break;
                if (strip && position < edit.Offset + edit.Remove) return edit.Offset + delta;
                delta += strip ? -edit.Remove : 2;
            }
            return position + delta;
        }
        int anchor = Map(w.Anchor, w.Selected && w.Anchor == w.Start);
        int cursor = Map(w.Cursor, w.Selected && w.Cursor == w.Start);
        Remember(w); // Snapshot the real selection, before staging any edit.
        foreach (var edit in edits.Reverse())
            source = strip ? source.Remove(edit.Offset, edit.Remove) : source.Insert(edit.Offset, "# ");
        w.Body = source; w.Tab.ExtraMatches.Clear(); w.Select(anchor, cursor);
        return true;
    }

    private static bool DeleteLines(LabWindow w)
    {
        string source = w.Body;
        var (first, last) = AffectedLines(w);
        int column = Math.Clamp(w.Start, first, LineEnd(source, first)) - first;
        int end = LineEnd(source, last);
        int from = first, to = end < source.Length ? end + 1 : end;
        if (end == source.Length && first > 0) from--; // Delete the last line's preceding LF.
        if (from == to) return false;
        string result = source.Remove(from, to - from);
        int destination = end < source.Length ? first : LineStart(result, from);
        int target = Math.Min(destination + column, LineEnd(result, destination));
        if (target < result.Length)
        {
            int[] starts = StringInfo.ParseCombiningCharacters(result);
            target = starts[ElementAtOrBefore(starts, result.Length, target)];
        }
        Remember(w);
        w.Body = result; w.Tab.ExtraMatches.Clear(); w.Cursor = target;
        return true;
    }
    private static bool IsTerminal(LabWindow w) => w.App == "terminal" || w.Focus == "terminal";
    private string FieldText(LabWindow w) => IsTerminal(w) ? TerminalInput : w.Focus == "body" ? w.Body : w.Input;
    private static string Slice(LabWindow w, string value) => value[Math.Clamp(w.Start, 0, value.Length)..Math.Clamp(w.End, 0, value.Length)];
    internal void Insert(LabWindow w, string value)
    {
        var source = FieldText(w);
        int start = Math.Clamp(w.Start, 0, source.Length), end = Math.Clamp(w.End, start, source.Length);
        string updated = source[..start] + value + source[end..];
        if (IsTerminal(w)) TerminalInput = updated;
        else if (w.Focus != "body") w.Input = updated;
        else if (w.App == "code")
        {
            Remember(w);
            foreach (int match in w.Tab.ExtraMatches.OrderDescending())
                if (match >= end && match + end - start <= updated.Length + end - start - value.Length)
                {
                    int shifted = match + value.Length - (end - start);
                    updated = updated[..shifted] + value + updated[(shifted + end - start)..];
                }
            w.Tab.ExtraMatches.Clear(); w.Body = updated;
        }
        else return;
        int caret = start + value.Length;
        w.Cursor = ReplayLegacyNavigation ? caret : InsertionBoundary(updated, caret);
        Version++;
    }

    internal bool CanRepeatEditing(string key)
    {
        var window = Active;
        if (window == null || Overview || Switcher || Screenshot || DesktopOverlay.Length > 0 || ShowDesktop && WindowsDesktop || window.Focus == "reverse-search") return false;
        bool editor = window.App == "code" && window.Focus == "body";
        if (!editor && !IsTerminal(window)) return false;
        return key is "Left" or "Right" or "Home" or "End" or "Delete" or "Backspace"
            || editor && key is "Up" or "Down";
    }

    internal bool Text(string key, string text, bool extend = false, bool word = false)
    {
        if (Overview)
        {
            if (key == "Enter")
            {
                string name = Search.ToLowerInvariant().Trim();
                string app = name.Contains("chrome") || name.Contains("хром") ? "chrome" : name.Contains("code") || name.Contains("код") ? "code" : name.Contains("terminal") || name.Contains("терминал") ? "terminal" : name.Contains("files") || name.Contains("файл") ? "files" : "";
                if (app.Length == 0) return false;
                Ensure(app); Search = ""; Overview = false; Version++; return true;
            }
            if (key == "Backspace") Search = Erase(Search, Search.Length, key, out _);
            else if (text.Length > 0 && text.All(c => !char.IsControl(c))) Search += text;
            else return false;
            Version++; return true;
        }
        var window = Active;
        if (window == null) return false;
        bool terminal = window.App == "terminal" || window.Focus == "terminal";
        if (window.Focus == "reverse-search")
        {
            if (key is "Enter" or "Esc" or "Left" or "Right")
            {
                window.Focus = window.App == "terminal" ? "body" : "terminal"; window.Cursor = TerminalInput.Length;
                return key == "Esc" || Text(key, "");
            }
            if (key == "Backspace" && window.Query.Length > 0) window.Query = window.Query[..PreviousBoundary(window.Query, window.Query.Length)];
            else if (!string.IsNullOrEmpty(text) && !text.Any(char.IsControl)) window.Query += text;
            else return false;
            window.SearchIndex = commands.Count;
            return ReverseSearch(window);
        }
        if (key == "Enter")
        {
            if (terminal)
            {
                string command = TerminalInput;
                var result = Terminal.Execute(command);
                if (command.Length > 0) commands.Add(command);
                commandIndex = commands.Count; TerminalInput = ""; window.Cursor = 0;
                window.Status = result.Success ? "Команда выполнена" : "Проверьте команду";
            }
            else if (window.Focus == "url")
            {
                string url = window.Input.Trim(); if (url.Length == 0) return false;
                if (BrowserPracticeActive) url = PracticeAddress(url);
                else if (!url.Contains("://") && url != "about:blank") url = url.Contains('.') ? "https://" + url : "https://search.local/?q=" + Uri.EscapeDataString(url);
                var tab = window.Tab; tab.Position = new(); tab.History.RemoveRange(tab.HistoryIndex + 1, tab.History.Count - tab.HistoryIndex - 1);
                if (BrowserPracticeActive)
                {
                    foreach (int entry in tab.PracticeHistoryItems.Keys.Where(index => index > tab.HistoryIndex).ToArray())
                        tab.PracticeHistoryItems.Remove(entry);
                    tab.PracticeHistoryItems[tab.HistoryIndex] = window.Item;
                }
                tab.History.Add(url); tab.HistoryIndex++; SetPage(tab, url); recentUrls.Remove(url); recentUrls.Insert(0, url); window.Item = -1; window.Focus = "body"; window.Input = "";
            }
            else if (window.Focus == "quick-open")
            {
                string name = window.Input.Trim();
                int found = window.Tabs.FindIndex(tab => tab.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) window.ActiveTab = found;
                else window.Tabs.Add(new LabTab(name, name, Terminal.ReadFile("~/project/" + name) ?? ""));
                if (found < 0) window.ActiveTab = window.Tabs.Count - 1;
                window.Focus = "body"; window.Input = ""; window.Cursor = 0;
            }
            else if (window.Focus == "palette")
            {
                if (window.Input.Equals("Format Document", StringComparison.OrdinalIgnoreCase))
                { Remember(window); window.Body = string.Join("\n", window.Body.Split('\n').Select(line => line.TrimEnd())); window.Status = "Документ отформатирован"; }
                else if (window.Input.Equals("Toggle Sidebar", StringComparison.OrdinalIgnoreCase)) window.Sidebar = !window.Sidebar;
                else if (window.Input.Equals("Split Editor", StringComparison.OrdinalIgnoreCase)) window.Split = true;
                else { window.Status = "Команда не найдена"; return false; }
                window.Input = ""; window.Focus = "body";
            }
            else if (window.Focus == "goto-line") { if (!int.TryParse(window.Input, out int line)) return false; window.Focus = "body"; window.Cursor = window.Body.Split('\n').Take(Math.Max(0, line - 1)).Sum(value => value.Length + 1); window.Cursor = Math.Min(window.Cursor, window.Body.Length); window.Focus = "body"; window.Status = "Строка " + line; }
            else if (window.Focus is "find" or "project-search") { window.Find = window.Input; FindMatch(window, false); }
            else if (window.App == "code" && window.Focus == "body") Insert(window, EditorNewLine(window, window.Start));
            else return false;
            Version++; return true;
        }
        if (key is "Backspace" or "Delete")
        {
            if (!terminal && window.Focus == "body" && window.App != "code") return false;
            string value = FieldText(window);
            if (!window.Selected)
            {
                int caret = Math.Clamp(window.Cursor, 0, value.Length);
                int edge = key == "Backspace" ? PreviousBoundary(value, caret) : NextBoundary(value, caret);
                if (edge == caret) return true;
                window.Select(caret, edge);
            }
            Insert(window, ""); return true;
        }
        if (key is "Left" or "Right" or "Home" or "End" || window.App == "code" && window.Focus == "body" && key is "Up" or "Down")
        {
            string value = FieldText(window);
            int pos = Math.Clamp(window.Cursor, 0, value.Length);
            int target = pos;
            switch (key)
            {
                case "Left": case "Right":
                    bool left = key == "Left";
                    if (window.Selected && !extend && !word) target = left ? window.Start : window.End;
                    else if (word) target = ReplayLegacyNavigation ? LegacyWordBoundary(value, pos, left) :
                        !ReplayLegacyPythonEditing && IsEditorBody(window) ? EditorWordEdge(value, pos, left) : WordBoundary(value, pos, left);
                    else target = left ? PreviousBoundary(value, pos) : NextBoundary(value, pos);
                    break;
                case "Home": target = word ? 0 : LineStart(value, pos); break;
                case "End": target = word ? value.Length : LineEnd(value, pos); break;
                default:
                    target = VerticalBoundary(value, pos, up: key == "Up", legacy: ReplayLegacyNavigation); break;
            }
            window.Tab?.ExtraMatches.Clear();
            window.Select(extend ? window.Anchor : target, target); Version++; return true;
        }
        if (terminal && key is "Up" or "Down") return Apply(key == "Up" ? "terminal-history-up" : "terminal-history-down");
        if (terminal && key == "Tab")
        {
            int caret = Math.Clamp(window.Cursor, 0, TerminalInput.Length);
            string last = TerminalInput[..caret].Split(' ').Last();
            string[] options = Terminal.Complete(last);
            if (options.Length == 1) { window.Select(caret - last.Length, caret); Insert(window, options[0]); }
            else window.Status = string.Join("  ", options);
            return true;
        }
        if (text.Length == 0 || text.Any(char.IsControl)) return false;
        if (terminal || window.App == "code" || window.Focus != "body") { Insert(window, text); return true; }
        return false;
    }

    private static int PreviousBoundary(string value, int cursor) => StringInfo.ParseCombiningCharacters(value)
        .Where(index => index < cursor).DefaultIfEmpty(0).Last();

    private static int NextBoundary(string value, int cursor) => StringInfo.ParseCombiningCharacters(value)
        .FirstOrDefault(index => index > cursor, value.Length);

    // Keep published cursor positions in UTF-16 for the two renderers, but
    // navigate text elements so one command cannot split a visual character.
    private static int ElementAtOrBefore(int[] starts, int length, int offset)
    {
        if (offset == length) return starts.Length; // sentinel: end of text
        int found = Array.BinarySearch(starts, offset);
        return found < 0 ? Math.Max(0, (~found) - 1) : found;
    }

    private static int InsertionBoundary(string value, int caret)
    {
        if (caret == 0 || caret == value.Length) return caret;
        // Between two ASCII characters, only CRLF is one grapheme. A
        // non-ASCII Prepend character can bind even to an ASCII suffix.
        if (value[caret] < 128 && value[caret - 1] < 128 &&
            (value[caret] != '\n' || value[caret - 1] != '\r')) return caret;
        int offset = 0;
        while (offset < caret) offset += StringInfo.GetNextTextElementLength(value.AsSpan(offset));
        return offset;
    }

    private static int WordBoundary(string value, int caret, bool left)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(value);
        int element = ElementAtOrBefore(starts, value.Length, caret);
        bool IsWord(int index) =>
            Rune.TryGetRuneAt(value, starts[index], out Rune rune) && Rune.IsLetterOrDigit(rune);
        if (left)
        {
            while (element > 0 && !IsWord(element - 1)) element--;
            while (element > 0 && IsWord(element - 1)) element--;
        }
        else
        {
            while (element < starts.Length && IsWord(element)) element++;
            while (element < starts.Length && !IsWord(element)) element++;
        }
        return element == starts.Length ? value.Length : starts[element];
    }

    // Reproduce already-saved edits made under the former code-unit rules.
    private static int LegacyWordBoundary(string value, int offset, bool left)
    {
        if (left)
        {
            while (offset > 0 && !char.IsLetterOrDigit(value[offset - 1])) offset--;
            while (offset > 0 && char.IsLetterOrDigit(value[offset - 1])) offset--;
        }
        else
        {
            while (offset < value.Length && char.IsLetterOrDigit(value[offset])) offset++;
            while (offset < value.Length && !char.IsLetterOrDigit(value[offset])) offset++;
        }
        return offset;
    }

    private static int VerticalBoundary(string value, int caret, bool up, bool legacy)
    {
        int start = LineStart(value, caret), end = LineEnd(value, caret);
        int adjacent = up ? LineStart(value, Math.Max(0, start - 1)) : Math.Min(value.Length, end + 1);
        if (legacy) return Math.Min(LineEnd(value, adjacent), adjacent + caret - start);
        int[] starts = StringInfo.ParseCombiningCharacters(value);
        int Index(int offset) => ElementAtOrBefore(starts, value.Length, offset);
        int column = Index(caret) - Index(start);
        int target = Math.Min(Index(LineEnd(value, adjacent)), Index(adjacent) + column);
        return target == starts.Length ? value.Length : starts[target];
    }

    internal void EndLegacyNavigation()
    {
        ReplayLegacyNavigation = false;
        // Former vertical moves and joining insertions could leave a caret
        // inside a cluster. Keep already-edited text, snap old endpoints.
        static int Snap(string text, int offset)
        {
            int safe = Math.Clamp(offset, 0, text.Length);
            if (safe == 0 || safe == text.Length) return safe;
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            return starts[ElementAtOrBefore(starts, text.Length, safe)];
        }
        static void Repair(Stack<EditRevision> stack)
        {
            var states = stack.Reverse().Select(revision => revision with {
                Cursor = Snap(revision.Text, revision.Cursor), Anchor = Snap(revision.Text, revision.Anchor)
            }).ToArray();
            stack.Clear();
            foreach (var state in states) stack.Push(state);
        }
        foreach (var window in Windows) window.RepairLegacyFieldPositions(Snap);
        foreach (var tab in Windows.Where(window => window.App is "code" or "chrome")
                                   .SelectMany(window => window.Tabs.Concat(window.ClosedTabs)))
        {
            tab.Position.Caret = Snap(tab.Body, tab.Position.Caret);
            tab.Position.Anchor = Snap(tab.Body, tab.Position.Anchor);
            // An Undo after upgrade must not resurrect the invalid coordinates.
            Repair(tab.Undo); Repair(tab.Redo);
        }
    }

    private static string Erase(string value, int cursor, string key, out int nextCursor)
    {
        cursor = Math.Clamp(cursor, 0, value.Length);
        int start = key == "Backspace" ? PreviousBoundary(value, cursor) : cursor;
        int end = key == "Backspace" ? cursor : NextBoundary(value, cursor);
        nextCursor = start;
        return end > start ? value.Remove(start, end - start) : value;
    }

    private static void SetPage(LabTab tab, string url)
    {
        tab.Url = url;
        (tab.Title, tab.Body) = BrowserPracticePage(url) ?? (url switch
        {
            "about:blank" => ("Новая вкладка", ""),
            "chrome://downloads" => ("Загрузки", ""),
            "chrome://history" => ("История", ""),
            "chrome://bookmarks" => ("Закладки", ""),
            "https://docs.python.org/3/" => ("Документация Python", "Python\nДокументация языка Python.\nФункции, модули и примеры."),
            NeutralProject.ProjectPage => ("Учебный проект task-list", NeutralProject.ProjectBody),
            NeutralProject.GuidePage => ("Как вести список задач", NeutralProject.GuideBody),
            "https://github.com/veitnemed/piling-main" => ("Проект piling-main", "piling-main\nСигналы, спектр и направляющие векторы."),
            "https://practice.local/spectrum" => ("Методика расчёта спектра", "Спектр сигнала\nПрямое преобразование возвращает комплексный спектр.\nЦентрированный спектр позволяет сопоставить частоты.\nМатериалы и отчёт доступны ниже."),
            _ => ("Учебная страница", url)
        });
        tab.Position = new();
    }

    internal object Snapshot() => new { Workspace, WorkspaceCount, Overview, Switcher, SwitchTarget, switchChoices = GnomePractice == "same-app" || WindowsDesktop ? SwitchChoices : [],
        desktopStyle = WindowsDesktop ? "win10" : "ubuntu", DesktopOverlay, DesktopOverlaySelection, DesktopOverlayWindows,
        desktopStartApps = WindowsDesktop ? WindowsStartApps : [], Dock, Layout, ActiveWindow, GnomeWindows, GnomePractice, DebugDay,
        windows = Windows.Select(WindowSnapshot), terminal = Terminal.Snapshot(), terminalInput = TerminalInput,
        Clipboard, clipboardMime = ClipboardImage == null ? "text/plain" : "image/png", Screenshot, ScreenshotReady, ScreenshotCopied, Screenshots, ShowDesktop, Search, LayoutIsVirtual, Version, DiagnosticWorkspace, DiagnosticProblemCount };
}
