using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio;

internal sealed class LabTab(string title, string url, string body)
{
    internal readonly string Id = Guid.NewGuid().ToString("N")[..8];
    internal string Title = title, Url = url, Body = body, SavedBody = body;
    internal readonly List<string> History = [url];
    internal int HistoryIndex;
    internal object Snapshot() => new { Id, Title, Url, Body, SavedBody, modified = Body != SavedBody };
    internal LabTab Clone()
    {
        var copy = new LabTab(Title, Url, Body) { SavedBody = SavedBody, HistoryIndex = HistoryIndex };
        copy.History.Clear(); copy.History.AddRange(History);
        return copy;
    }
}

internal sealed class LabWindow(string app, int number, int workspace)
{
    internal readonly string Id = "window-" + number;
    internal readonly string App = app;
    internal string Title = app switch { "chrome" => "Chrome", "code" => "Visual Studio Code", "terminal" => "Терминал", _ => "Файлы" };
    internal int Workspace = workspace;
    internal string State = "normal", Focus = "body", Input = "", Find = "", Status = "Готово";
    internal bool Sidebar = true, Split, Selected, Bookmarks;
    internal readonly HashSet<string> SavedBookmarks = [];
    internal int ActiveTab, Cursor, Zoom = 100, OptionIndex;
    internal readonly List<LabTab> Tabs = [];
    internal readonly Stack<LabTab> ClosedTabs = [];
    internal readonly Stack<string> Undo = [], Redo = [];
    internal LabTab Tab => Tabs.Count == 0 ? null : Tabs[Math.Clamp(ActiveTab, 0, Tabs.Count - 1)];
    internal string Body { get => Tab?.Body ?? ""; set { if (Tab != null) Tab.Body = value; } }
    internal string Url => Tab?.Url ?? "";
    internal object Snapshot(bool active) => new { Id, App, Title, Workspace, State, active,
        tabs = Tabs.Select(tab => tab.Snapshot()), ActiveTab, Url, Body, Focus, Input, Find, Sidebar, Split, Status, Zoom, Selected, Cursor, OptionIndex, Bookmarks, savedBookmarks = SavedBookmarks.ToArray() };
    internal LabWindow Clone()
    {
        var copy = new LabWindow(App, int.Parse(Id[7..]), Workspace)
        {
            Title = Title, State = State, Focus = Focus, Input = Input, Find = Find, Status = Status,
            Sidebar = Sidebar, Split = Split, Selected = Selected, Bookmarks = Bookmarks,
            ActiveTab = ActiveTab, Cursor = Cursor, Zoom = Zoom, OptionIndex = OptionIndex
        };
        copy.Tabs.AddRange(Tabs.Select(tab => tab.Clone()));
        foreach (var tab in ClosedTabs.Reverse()) copy.ClosedTabs.Push(tab.Clone());
        foreach (var value in Undo.Reverse()) copy.Undo.Push(value);
        foreach (var value in Redo.Reverse()) copy.Redo.Push(value);
        copy.SavedBookmarks.UnionWith(SavedBookmarks);
        return copy;
    }
}

internal sealed class LabScene
{
    internal readonly List<LabWindow> Windows = [];
    internal readonly VirtualTerminal Terminal;
    internal int Workspace, WorkspaceCount = 3, Version, Screenshots;
    internal string Layout, Clipboard = "build: 17 tests passed", ActiveWindow;
    internal bool Overview, Switcher, Screenshot, ScreenshotReady, ScreenshotCopied, ShowDesktop, LayoutIsVirtual;
    internal string Search = "";
    internal string TerminalInput = "";
    private int windowNumber;
    private readonly List<string> commands = [];
    private int commandIndex;
    internal bool CanHistoryUp => commandIndex > 0;
    internal bool CanHistoryDown => commandIndex < commands.Count;
    internal LabWindow Active => Windows.FirstOrDefault(window => window.Id == ActiveWindow);

    internal LabScene(string app, string layout)
    {
        Terminal = new VirtualTerminal();
        Layout = layout == "RU" ? "RU" : "EN";
        Clipboard = app == "terminal" ? "pwd" : "build: 17 tests passed";
        if (app != "ubuntu") Open(app);
    }

    private LabScene(LabScene source)
    {
        Terminal = source.Terminal.Clone();
        Workspace = source.Workspace; WorkspaceCount = source.WorkspaceCount; Version = source.Version;
        Screenshots = source.Screenshots; Layout = source.Layout; Clipboard = source.Clipboard;
        ActiveWindow = source.ActiveWindow; Overview = source.Overview; Switcher = source.Switcher;
        Screenshot = source.Screenshot; ScreenshotReady = source.ScreenshotReady;
        ScreenshotCopied = source.ScreenshotCopied; ShowDesktop = source.ShowDesktop;
        LayoutIsVirtual = source.LayoutIsVirtual; Search = source.Search;
        TerminalInput = source.TerminalInput; windowNumber = source.windowNumber;
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
            window.Tabs.Add(new LabTab("Проект piling-main", "https://github.com/veitnemed/piling-main", "piling-main\nСигналы, спектр и направляющие векторы."));
        }
        else if (app == "code")
        {
            window.Tabs.Add(new LabTab("app.py", "app.py", Terminal.ReadFile("~/project/app.py") ?? ""));
            window.Tabs.Add(new LabTab("README.md", "README.md", Terminal.ReadFile("~/project/README.md") ?? ""));
        }
        else if (app == "files") window.Tabs.Add(new LabTab("Домашняя папка", "/home/student", "project/\nДокументы/\nЗагрузки/"));
        Windows.Add(window); Activate(window); Version++; return window;
    }

    internal void Activate(LabWindow window)
    {
        if (window == null) return;
        ActiveWindow = window.Id; Workspace = window.Workspace; ShowDesktop = Overview = false;
        if (window.State == "minimized") window.State = "normal";
    }

    internal bool Has(string app) => Windows.Any(window => window.App == app);
    internal void Ensure(string app)
    {
        var window = Windows.FirstOrDefault(item => item.App == app);
        if (window == null) Open(app); else Activate(window);
    }

    internal bool Apply(string action)
    {
        var window = Active;
        bool changed = true;
        switch (action)
        {
            case "overview": Overview = !Overview; Search = ""; break;
            case "switch-next": Switch(1); break;
            case "switch-prev": Switch(-1); break;
            case "open-chrome": Ensure("chrome"); break;
            case "open-code": Ensure("code"); break;
            case "open-terminal": Ensure("terminal"); break;
            case "open-files": Ensure("files"); break;
            case "close-window":
                if (window == null) return false;
                Windows.Remove(window); ActiveWindow = null;
                Activate(Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "maximize": if (window == null) return false; window.State = "maximized"; break;
            case "restore": if (window == null) return false; window.State = "normal"; break;
            case "tile-left": if (window == null) return false; window.State = "left"; break;
            case "tile-right": if (window == null) return false; window.State = "right"; break;
            case "minimize":
                if (window == null) return false;
                window.State = "minimized"; ActiveWindow = null;
                Activate(Windows.LastOrDefault(item => item.Workspace == Workspace && item.State != "minimized")); break;
            case "desktop": ShowDesktop = !ShowDesktop; break;
            case "workspace-next": Workspace = (Workspace + 1) % WorkspaceCount; ActiveWindow = null; Activate(Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "workspace-prev": Workspace = (Workspace + WorkspaceCount - 1) % WorkspaceCount; ActiveWindow = null; Activate(Windows.LastOrDefault(item => item.Workspace == Workspace)); break;
            case "move-next": if (window == null) return false; window.Workspace = (Workspace + 1) % WorkspaceCount; Workspace = window.Workspace; break;
            case "move-prev": if (window == null) return false; window.Workspace = (Workspace + WorkspaceCount - 1) % WorkspaceCount; Workspace = window.Workspace; break;
            case "layout": Layout = Layout == "EN" ? "RU" : "EN"; LayoutIsVirtual = true; break;
            case "screenshot": Screenshot = true; ScreenshotReady = false; ScreenshotCopied = false; break;
            case "screenshot-confirm": if (!Screenshot) return false; Screenshot = false; ScreenshotReady = true; Screenshots++; break;
            case "copy-shot": if (!ScreenshotReady) return false; ScreenshotCopied = true; Clipboard = "Учебный снимок #" + Screenshots; break;
            case "escape": Overview = Switcher = Screenshot = ScreenshotReady = false; if (window != null) { window.Focus = "body"; window.Input = ""; } break;
            case "navigate-pasted":
                if (window?.App != "chrome" || window.Focus != "url" || string.IsNullOrWhiteSpace(window.Input)) return false;
                return Text("Enter", "");
            case "terminal-submit":
                if (window == null || window.App != "terminal" && window.Focus != "terminal" || string.IsNullOrWhiteSpace(TerminalInput)) return false;
                return Text("Enter", "") && Terminal.LastSuccess;
            default: changed = ApplyWindow(action, window); break;
        }
        if (changed) Version++;
        return changed;
    }

    private void Switch(int direction)
    {
        var visible = Windows.Where(item => item.Workspace == Workspace).ToArray();
        if (visible.Length == 0) return;
        int index = Array.FindIndex(visible, item => item.Id == ActiveWindow);
        Activate(visible[(index + direction + visible.Length) % visible.Length]); Switcher = true;
    }

    private bool ApplyWindow(string action, LabWindow window)
    {
        if (window == null) return false;
        bool browser = window.App == "chrome", code = window.App == "code", terminal = window.App == "terminal" || window.Focus == "terminal";
        switch (action)
        {
            case "new-tab":
                if (!browser && !code) return false;
                window.Tabs.Add(browser ? new LabTab("Новая вкладка", "about:blank", "") : new LabTab("untitled.py", "untitled.py", ""));
                window.ActiveTab = window.Tabs.Count - 1; window.Cursor = 0; window.Focus = browser ? "url" : "body"; window.Input = ""; window.Selected = false; break;
            case "close-tab":
                if (window.Tabs.Count <= 1) return false;
                window.ClosedTabs.Push(window.Tab); window.Tabs.RemoveAt(window.ActiveTab); window.ActiveTab = Math.Min(window.ActiveTab, window.Tabs.Count - 1);
                window.Focus = "body"; window.Input = ""; window.Selected = false; break;
            case "restore-tab":
                if (!window.ClosedTabs.TryPop(out var restored)) return false;
                window.Tabs.Add(restored); window.ActiveTab = window.Tabs.Count - 1;
                window.Focus = "body"; window.Input = ""; window.Selected = false; break;
            case "next-tab": case "prev-tab":
                if (window.Tabs.Count < 2) return false;
                window.ActiveTab = (window.ActiveTab + (action == "next-tab" ? 1 : window.Tabs.Count - 1)) % window.Tabs.Count;
                window.Cursor = 0; window.Focus = "body"; window.Selected = false; break;
            case "first-tab": window.ActiveTab = 0; window.Focus = "body"; window.Selected = false; break;
            case "last-tab": window.ActiveTab = Math.Max(0, window.Tabs.Count - 1); window.Focus = "body"; window.Selected = false; break;
            case "url": if (!browser) return false; window.Focus = "url"; window.Input = window.Url; window.Selected = true; break;
            case "find": window.Focus = "find"; window.Input = ""; window.Find = ""; break;
            case "find-next": window.Status = "Следующее совпадение «" + window.Find + "»"; break;
            case "refresh": if (!browser) return false; window.Status = "Страница обновлена"; break;
            case "back": case "forward":
                if (!browser || window.Tab == null) return false;
                int destination = window.Tab.HistoryIndex + (action == "back" ? -1 : 1);
                if (destination < 0 || destination >= window.Tab.History.Count) return false;
                window.Tab.HistoryIndex = destination; SetPage(window.Tab, window.Tab.History[destination]); break;
            case "fullscreen": window.State = window.State == "fullscreen" ? "normal" : "fullscreen"; break;
            case "zoom-in": window.Zoom = Math.Min(200, window.Zoom + 10); break;
            case "zoom-out": window.Zoom = Math.Max(50, window.Zoom - 10); break;
            case "zoom-reset": window.Zoom = 100; break;
            case "bookmark": window.SavedBookmarks.Add(window.Url); window.Status = "Закладка сохранена"; break;
            case "bookmarks": window.Bookmarks = !window.Bookmarks; break;
            case "sidebar": if (!code) return false; window.Sidebar = !window.Sidebar; if (!window.Sidebar) window.Focus = "body"; break;
            case "split": if (!code) return false; window.Split = !window.Split; break;
            case "quick-open": if (!code) return false; window.Focus = "quick-open"; window.Input = ""; window.OptionIndex = window.ActiveTab; break;
            case "quick-next": if (!code || window.Focus != "quick-open" || window.Tabs.Count < 2) return false; window.OptionIndex = (window.OptionIndex + 1) % window.Tabs.Count; break;
            case "quick-choose":
                if (!code || window.Focus != "quick-open" || window.Tabs.Count < 2) return false;
                window.ActiveTab = window.OptionIndex; window.Focus = "body"; window.Input = ""; window.Selected = false; break;
            case "palette": if (!code) return false; window.Focus = "palette"; window.Input = ""; window.OptionIndex = 0; break;
            case "palette-next": if (!code || window.Focus != "palette") return false; window.OptionIndex = (window.OptionIndex + 1) % 3; break;
            case "palette-choose":
                if (!code || window.Focus != "palette") return false;
                if (window.OptionIndex == 1) window.Sidebar = !window.Sidebar;
                else if (window.OptionIndex == 2) window.Split = !window.Split;
                else { Remember(window); window.Body = string.Join("\n", window.Body.Split('\n').Select(line => line.TrimEnd())); }
                window.Focus = "body"; window.Input = ""; break;
            case "goto-line": if (!code) return false; window.Focus = "goto-line"; window.Input = ""; break;
            case "explorer": if (!code) return false; window.Sidebar = true; window.Focus = "explorer"; break;
            case "project-search": if (!code) return false; window.Sidebar = true; window.Focus = "project-search"; window.Input = ""; break;
            case "terminal-toggle": if (!code) return false; window.Focus = window.Focus == "terminal" ? "body" : "terminal"; window.Selected = false; window.Cursor = window.Focus == "terminal" ? TerminalInput.Length : 0; break;
            case "save": if (!code || window.Tab == null) return false; window.Tab.SavedBody = window.Body; window.Status = "Файл «" + window.Tab.Title + "» сохранён"; break;
            case "select-all": window.Selected = true; break;
            case "copy": Clipboard = terminal ? string.Join("\n", Terminal.History.TakeLast(6)) : window.Focus == "body" ? window.Body : window.Input; window.Status = "Скопировано в учебный буфер"; break;
            case "cut":
                Clipboard = terminal ? TerminalInput : window.Focus == "body" ? window.Body : window.Input;
                if (terminal) TerminalInput = ""; else if (window.Focus == "body") { Remember(window); window.Body = ""; window.Cursor = 0; } else window.Input = "";
                window.Selected = false; window.Status = "Выделение вырезано"; break;
            case "paste": Insert(window, Clipboard); window.Status = "Вставлено из учебного буфера"; break;
            case "undo": if (!code || !window.Undo.TryPop(out var undo)) return false; window.Redo.Push(window.Body); window.Body = undo; window.Cursor = Math.Min(window.Cursor, window.Body.Length); break;
            case "redo": if (!code || !window.Redo.TryPop(out var redo)) return false; window.Undo.Push(window.Body); window.Body = redo; window.Cursor = Math.Min(window.Cursor, window.Body.Length); break;
            case "comment":
                if (!code) return false; Remember(window);
                window.Body = window.Body.StartsWith("# ") ? window.Body[2..] : "# " + window.Body; break;
            case "delete-line":
                if (!code) return false; Remember(window);
                int begin = window.Body.LastIndexOf('\n', Math.Max(0, window.Cursor - 1)); begin = begin < 0 ? 0 : begin + 1;
                int end = window.Body.IndexOf('\n', Math.Min(window.Cursor, window.Body.Length)); end = end < 0 ? window.Body.Length : end + 1;
                window.Body = window.Body.Remove(begin, end - begin); window.Cursor = begin; break;
            case "terminal-clear": if (!terminal) return false; Terminal.Execute("clear"); break;
            case "terminal-cancel": if (!terminal) return false; TerminalInput = ""; window.Status = "Ввод команды отменён"; break;
            case "terminal-home": if (!terminal) return false; window.Cursor = 0; break;
            case "terminal-end": if (!terminal) return false; window.Cursor = TerminalInput.Length; break;
            case "terminal-kill": if (!terminal) return false; TerminalInput = TerminalInput[..Math.Clamp(window.Cursor, 0, TerminalInput.Length)]; break;
            case "terminal-erase": if (!terminal) return false; TerminalInput = ""; window.Cursor = 0; break;
            case "terminal-history-up": case "terminal-history-down":
                if (!terminal || commands.Count == 0) return false;
                commandIndex = Math.Clamp(commandIndex + (action == "terminal-history-up" ? -1 : 1), 0, commands.Count);
                TerminalInput = commandIndex == commands.Count ? "" : commands[commandIndex]; window.Cursor = TerminalInput.Length; break;
            default: return false;
        }
        return true;
    }

    private static void Remember(LabWindow window) { window.Undo.Push(window.Body); window.Redo.Clear(); }
    internal void Insert(LabWindow window, string value)
    {
        if (window.App == "terminal" || window.Focus == "terminal")
        {
            int cursor = window.Selected ? 0 : Math.Clamp(window.Cursor, 0, TerminalInput.Length);
            TerminalInput = window.Selected ? value : TerminalInput.Insert(cursor, value); window.Cursor = cursor + value.Length;
        }
        else if (window.Focus != "body") { window.Input = window.Selected ? value : window.Input + value; }
        else if (window.App == "code")
        {
            Remember(window); int cursor = window.Selected ? 0 : Math.Clamp(window.Cursor, 0, window.Body.Length);
            window.Body = window.Selected ? value : window.Body.Insert(cursor, value); window.Cursor = cursor + value.Length;
        }
        window.Selected = false; Version++;
    }

    internal bool Text(string key, string text)
    {
        if (Overview)
        {
            if (key == "Enter")
            {
                string name = Search.ToLowerInvariant().Trim();
                string app = name.Contains("chrome") || name.Contains("хром") ? "chrome" : name.Contains("code") || name.Contains("код") ? "code" : name.Contains("terminal") || name.Contains("терминал") ? "terminal" : name.Contains("files") || name.Contains("файл") ? "files" : "";
                if (app.Length == 0) return false;
                Open(app); Search = ""; Overview = false; Version++; return true;
            }
            if (key == "Backspace") Search = Erase(Search, Search.Length, key, out _);
            else if (text.Length > 0 && text.All(c => !char.IsControl(c))) Search += text;
            else return false;
            Version++; return true;
        }
        var window = Active;
        if (window == null) return false;
        bool terminal = window.App == "terminal" || window.Focus == "terminal";
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
                if (!url.Contains("://") && url != "about:blank") url = url.Contains('.') ? "https://" + url : "https://search.local/?q=" + Uri.EscapeDataString(url);
                var tab = window.Tab; tab.History.RemoveRange(tab.HistoryIndex + 1, tab.History.Count - tab.HistoryIndex - 1);
                tab.History.Add(url); tab.HistoryIndex++; SetPage(tab, url); window.Focus = "body"; window.Input = "";
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
            else if (window.Focus == "goto-line") { if (!int.TryParse(window.Input, out int line)) return false; window.Cursor = window.Body.Split('\n').Take(Math.Max(0, line - 1)).Sum(value => value.Length + 1); window.Cursor = Math.Min(window.Cursor, window.Body.Length); window.Focus = "body"; window.Status = "Строка " + line; }
            else if (window.Focus is "find" or "project-search") { window.Find = window.Input; window.Status = window.Body.Contains(window.Find, StringComparison.OrdinalIgnoreCase) ? "Совпадение найдено" : "Совпадений нет"; }
            else if (window.App == "code") Insert(window, "\n");
            else return false;
            Version++; return true;
        }
        if (key is "Backspace" or "Delete")
        {
            if (terminal)
            {
                TerminalInput = Erase(TerminalInput, window.Cursor, key, out window.Cursor);
            }
            else if (window.Focus != "body") { window.Input = window.Selected ? "" : Erase(window.Input, window.Input.Length, key, out _); }
            else if (window.App == "code")
            {
                Remember(window);
                if (window.Selected) { window.Body = ""; window.Cursor = 0; }
                else window.Body = Erase(window.Body, window.Cursor, key, out window.Cursor);
            }
            window.Selected = false; Version++; return true;
        }
        if (key is "Left" or "Right" or "Home" or "End")
        {
            string value = terminal ? TerminalInput : window.Body;
            window.Cursor = key switch { "Left" => PreviousBoundary(value, window.Cursor), "Right" => NextBoundary(value, window.Cursor), "Home" => 0, _ => value.Length };
            window.Selected = false;
            return true;
        }
        if (terminal && key is "Up" or "Down") return Apply(key == "Up" ? "terminal-history-up" : "terminal-history-down");
        if (terminal && key == "Tab")
        {
            string last = TerminalInput.Split(' ').Last();
            string[] options = Terminal.Complete(last);
            if (options.Length == 1) { TerminalInput = TerminalInput[..^last.Length] + options[0]; window.Cursor = TerminalInput.Length; }
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
        tab.Url = url; tab.Title = url.Contains("python.org") ? "Документация Python" : url.Contains("github.com") ? "Проект piling-main" : url == "about:blank" ? "Новая вкладка" : "Учебная страница";
        tab.Body = url == "about:blank" ? "" : tab.Title + "\n" + url + "\nPython и Ubuntu. Работа с клавиатурой.";
    }

    internal object Snapshot() => new { Workspace, WorkspaceCount, Overview, Switcher, Layout, ActiveWindow,
        windows = Windows.Select(window => window.Snapshot(window.Id == ActiveWindow)), terminal = Terminal.Snapshot(), terminalInput = TerminalInput,
        Clipboard, Screenshot, ScreenshotReady, ScreenshotCopied, Screenshots, ShowDesktop, Search, LayoutIsVirtual, Version };
}
