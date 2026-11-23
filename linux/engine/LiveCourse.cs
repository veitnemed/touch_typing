using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record LivePhase(string Title, string Route);
internal sealed record LiveMission(string App, int Level, string Id, string Title, LivePhase[] Phases);
internal sealed record LiveGoal(int Phase, LabTask Task, string Target, string Layout, bool VerifyReport = false, string LegacyTarget = null);

internal static partial class LiveCourse
{
    private static readonly Lazy<List<LiveMission>> catalog = new(Build);
    internal static List<LiveMission> Missions => catalog.Value;
    private static List<LiveMission> Build()
    {
        var result = new List<LiveMission>();
        BrowserMissions(result);
        EditorMissions(result);
        TerminalMissions(result);
        DesktopMissions(result);
        return result;
    }

    // The same case has a reproducible starting desk. The user's live desk is never used for rehearsal.
    internal static List<LiveGoal> Compile(LiveMission mission, LabScene initial)
    {
        var trial = initial.Clone();
        var goals = new List<LiveGoal>();
        for (int phase = 0; phase < mission.Phases.Length; phase++)
        {
            foreach (string token in RouteTokens(mission.Phases[phase]))
            {
                if (IsCommandToken(token) && trial.Layout == "RU")
                {
                    var change = trial.WithBinding(Actions["layout"]);
                    string layout = trial.Layout;
                    trial.Apply("layout");
                    goals.Add(new(phase, change with { Title = "Переключите ввод на английский для команды" }, Evidence(change, trial), layout));
                }
                var task = MakeTask(token, trial);
                bool verifyReport = mission.Id == "workday-report" && task.Id == "command/head -n 2 spectrum-report.txt";
                string original = Evidence(task, trial, verifyReport: verifyReport);
                var before = trial.Clone();
                if (IsPickerToken(token))
                {
                    int count = trial.Entries(trial.Active).Length;
                    for (int i = 0; i < count && !AtItem(trial, task.Text); i++) trial.Apply(task.Action);
                    if (!AtItem(trial, task.Text)) throw new InvalidOperationException(mission.Id + ": объект " + task.Text + " отсутствует");
                }
                else if (token.StartsWith("open-") && trial.Active?.App == token[5..]) continue;
                else if ((token == "maximize" && trial.Active?.State == "maximized") || (token == "tile-left" && trial.Active?.State == "left") || (token == "tile-right" && trial.Active?.State == "right")) continue;
                else if (token == "save" && trial.Active?.Body == trial.Active?.Tab?.SavedBody) continue;
                else if (token == "bookmark" && trial.Active.SavedBookmarks.Contains(trial.Active.Url)) continue;
                else if (token == "first-tab" && trial.Active.ActiveTab == 0 || token == "last-tab" && trial.Active.ActiveTab == trial.Active.Tabs.Count - 1) continue;
                else if (!Demonstrate(task, trial)) throw new InvalidOperationException(mission.Id + ": недоступно " + token);
                string proof = Evidence(task, trial, verifyReport: verifyReport);
                if (original == proof) { trial = before; continue; }
                goals.Add(new(phase, task, proof, before.Layout, verifyReport, verifyReport ? Evidence(task, trial) : null));
            }
        }
        if (goals.Count < 20) throw new InvalidOperationException(mission.Id + ": меньше 20 результатов");
        return goals;
    }

    private static IEnumerable<string> RouteTokens(LivePhase phase) =>
        phase.Route.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsPickerToken(string token) => token.StartsWith("pick:", StringComparison.Ordinal) ||
        token.StartsWith("quick:", StringComparison.Ordinal) || token.StartsWith("sym:", StringComparison.Ordinal);
    private static bool IsCommandToken(string token) => token.StartsWith("cmd:", StringComparison.Ordinal);
    private static string DecodeCommand(string token) => token[4..].Replace('~', ' ');
    private static string CommandId(string command) => "command/" + command;

    // Only a coarse prefilter: the compiled list is authoritative for layout and
    // for picker goals that turn into different IDs depending on focus.
    internal static bool CouldPracticeAny(LiveMission mission, HashSet<string> taskIds) =>
        mission.Phases.SelectMany(RouteTokens).Any(token =>
            IsCommandToken(token) ? taskIds.Contains(CommandId(DecodeCommand(token))) || taskIds.Contains("layout") :
            IsPickerToken(token) ? taskIds.Contains("file-next") || taskIds.Contains("link-next") ||
                taskIds.Contains("quick-next") || taskIds.Contains("symbol-next") :
            taskIds.Contains(token));

    internal static int CountWeakGoals(IEnumerable<LiveGoal> goals, HashSet<(string Task, string Layout)> weak) =>
        goals.Count(goal => weak.Contains((goal.Task.Id, goal.Layout)));

    private static bool AtItem(LabScene scene, string target) => scene.Entries(scene.Active).ElementAtOrDefault(scene.Active.Focus is "quick-open" or "symbols" ? scene.Active.OptionIndex : scene.Active.Item)?.Title == target;
    private static readonly Dictionary<string, LabTask> Actions = LabCatalog.Shortcuts.ToDictionary(task => task.Id);
    private static LabTask MakeTask(string token, LabScene scene)
    {
        if (IsPickerToken(token))
        {
            var w = scene.Active;
            string query = token[(token.IndexOf(':') + 1)..].Replace('_', ' ');
            var entry = scene.Entries(w).FirstOrDefault(item => item.Path == query || item.Title == query || item.Path.EndsWith("/" + query, StringComparison.Ordinal));
            if (entry == null) throw new InvalidOperationException("Не найден объект: " + query + " в " + w.Url + w.Folder);
            var picker = Actions[w.Focus == "quick-open" ? "quick-next" : w.Focus == "symbols" ? "symbol-next" : w.App == "files" ? "file-next" : "link-next"];
            return picker with { Title = "Выберите «" + entry.Title + "»", Text = entry.Title };
        }
        if (IsCommandToken(token))
        {
            string value = DecodeCommand(token);
            return new(CommandId(value), "Выполните «" + value + "»", "", value.Length > 12 ? "Ctrl+R — найти команду по фрагменту. Enter — выполнить, стрелка — перейти к правке." : "Введите команду и нажмите Enter.", value, "command");
        }
        var task = scene.WithBinding(Actions[token]);
        if (task.Action == "item-open")
        {
            var item = scene.Entries(scene.Active).ElementAtOrDefault(scene.Active.Item);
            if (item != null) return task with { Title = (item.Kind == "download" ? "Скачайте" : "Откройте") + " «" + item.Title + "»" };
        }
        return task;
    }

    private static bool Demonstrate(LabTask task, LabScene trial)
    {
        if (task.Action == "launch") { trial.Ensure(task.Id[5..]); return true; }
        if (task.Action == "command")
        {
            trial.TerminalInput = task.Text;
            return trial.Text("Enter", "") && trial.Terminal.LastSuccess;
        }
        if (!LabCatalog.Available(task, trial) || !trial.Apply(task.Action)) return false;
        if (trial.Switcher) trial.CommitSwitch();
        return true;
    }

    internal static string Evidence(LabTask task, LabScene scene, string equivalentCommand = null, bool verifyReport = false)
    {
        var w = scene.Active;
        string app = w?.App ?? "", action = task.Action;
        object value = action switch
        {
            "command" or "terminal-submit" => new { command = equivalentCommand ?? scene.Terminal.LastCommand, scene.Terminal.LastSuccess, scene.Terminal.Cwd, scene.Terminal.LastOutput, input = scene.TerminalInput, report = verifyReport ? scene.ReadReportArtifact() : null },
            "terminal-history-up" or "terminal-history-down" or "terminal-erase" or "terminal-kill" or "terminal-yank" or "word-left" or "word-right" or "terminal-home" or "terminal-end" or "terminal-cancel" => new { scene.TerminalInput, w?.Cursor },
            "terminal-clear" => scene.Terminal.History.Count(),
            "overview" => scene.Overview, "desktop" => scene.ShowDesktop, "layout" => scene.Layout,
            "screenshot" => scene.Screenshot, "screenshot-confirm" => scene.Screenshots,
            "open-chrome" or "open-code" or "open-terminal" or "open-files" or "launch" or "switch-next" or "switch-prev" => app,
            "close-window" or "minimize" => scene.Windows.Select(v => new { v.App, v.State, v.Workspace }).ToArray(),
            "maximize" or "restore" or "tile-left" or "tile-right" or "fullscreen" => new { app, w?.State },
            "workspace-next" or "workspace-prev" or "move-next" or "move-prev" => new { scene.Workspace, windows = scene.Windows.Select(v => new { v.App, v.Workspace }).ToArray() },
            "item-next" or "item-prev" => w == null ? null : new { app, w.Folder, w.Url, item = scene.Entries(w).ElementAtOrDefault(w.Item)?.Path },
            "file-parent" or "file-home" or "file-back" or "file-forward" => new { app, w?.Folder },
            "file-cut" or "file-copy" => scene.Clipboard,
            "file-paste" or "trash" or "restore-trash" => w == null ? null : new { w.Folder, items = scene.Entries(w).Select(e => e.Path).ToArray() },
            "item-open" => new { app, w?.Folder, w?.Url, scene.DownloadCount },
            "url" or "select-all" or "select-line" or "occurrence" => new { app, w?.Focus, w?.Url, w?.Cursor, w?.Anchor, matches = w?.Tab?.ExtraMatches.Count },
            "quick-next" or "symbol-next" => new { w?.Focus, item = w == null ? "" : scene.Entries(w).ElementAtOrDefault(w.OptionIndex)?.Path },
            "quick-choose" or "symbol-open" => new { w?.Focus, w?.Url, w?.Cursor },
            "copy" or "terminal-copy" => new { scene.Clipboard },
            "cut" or "paste" or "undo" or "redo" or "comment" or "delete-line" or "move-line-up" or "move-line-down" => new { app, w?.Focus, w?.Url, w?.Input, w?.Body, scene.TerminalInput },
            "replace-field" or "replace-one" => new { w?.Focus, w?.Body, w?.Find },
            "zoom-in" or "zoom-out" or "zoom-reset" => new { w?.Url, w?.Zoom },
            "bookmark" => w?.SavedBookmarks.Order().ToArray(), "bookmarks" => w?.Bookmarks,
            "new-tab" or "close-tab" or "restore-tab" => new { app, w?.ActiveTab, tabs = w?.Tabs.Select(tab => tab.Url).ToArray() },
            "next-tab" or "prev-tab" or "first-tab" or "last-tab" => new { app, w?.Url, w?.ActiveTab },
            "back" or "forward" or "navigate-pasted" => new { w?.Focus, w?.Url, w?.ActiveTab },
            "history" or "downloads" or "bookmark-manager" => new { w?.Focus, w?.Url },
            "panel" or "terminal-toggle" => new { w?.Panel, w?.Focus }, "sidebar" => w?.Sidebar, "split" or "focus-editor" or "focus-second" => new { w?.Split, w?.Group, w?.Url, w?.Focus },
            "save" => new { w?.Url, w?.Tab?.SavedBody },
            _ => new { app, w?.Focus, w?.OptionIndex, w?.Url, w?.Cursor, w?.View, w?.Find, scene.Overview, scene.ScreenshotReady }
        };
        return JsonSerializer.Serialize(value);
    }
}
