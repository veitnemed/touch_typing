using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal bool WindowsDesktop;
    internal string DesktopOverlay = "";
    internal int DesktopOverlaySelection;
    internal string[] DesktopOverlayWindows = [];
    internal static readonly string[] WindowsStartApps = ["chrome", "code", "terminal", "files"];

    internal static string DesktopEnvironment(string value) => value == "windows" ? "windows" : "ubuntu";
    internal static string MissionEnvironment(LiveMission mission) =>
        mission.App == "windows" || LiveCourse.TerminalEnvironment(mission.Id) == "windows" ||
            mission.Id.StartsWith("workday-windows-", StringComparison.Ordinal) ? "windows"
        : mission.App is "ubuntu" or "workday" || LiveCourse.TerminalEnvironment(mission.Id) == "ubuntu" ? "ubuntu" : null;

    internal void PrepareWindowsDesktop()
    {
        WindowsDesktop = true;
        Workspace = 0;
        WorkspaceCount = 1;
    }

    private void ClearWindowsOverlay()
    {
        DesktopOverlay = "";
        DesktopOverlaySelection = 0;
        DesktopOverlayWindows = [];
    }

    private LabWindow DesktopWindow() => Windows.Where(item => item.Workspace == Workspace && item.State != "minimized").MaxBy(item => item.Activation);

    internal object WindowsDesktopEvidence() => new
    {
        Workspace, WorkspaceCount, ActiveWindow, ShowDesktop, DesktopOverlay, DesktopOverlaySelection,
        windows = Windows.Select(w => new { w.Id, w.App, w.Workspace, w.State, w.MinimizedFrom, w.Folder, w.Item }).ToArray()
    };

    internal bool WindowsActionAvailable(string action)
    {
        var window = Active;
        if (!WindowsDesktop) return false;
        return action switch
        {
            "win-start" or "win-explorer" or "win-task-view" or "win-desktop" or "win-layout" => true,
            "win-cancel" => DesktopOverlay.Length > 0 || Switcher,
            "win-overlay-next" or "win-overlay-prev" => DesktopOverlay switch
            {
                "start" => WindowsStartApps.Length > 1,
                "snap" or "task-view" => DesktopOverlayWindows.Length > 1,
                _ => false
            },
            "win-overlay-open" => DesktopOverlay == "start" || (DesktopOverlay is "snap" or "task-view" && DesktopOverlayWindows.Length > 0),
            "win-switch" => DesktopOverlay.Length == 0 && Windows.Count(w => w.Workspace == Workspace) > 1,
            "win-explorer-parent" => DesktopOverlay.Length == 0 && window?.App == "files" && window.Folder != "/home/student",
            "win-new-desktop" => WorkspaceCount < 9,
            "win-close-desktop" => WorkspaceCount > 1,
            "win-prev-desktop" => Workspace > 0,
            "win-next-desktop" => Workspace + 1 < WorkspaceCount,
            "win-maximize" => DesktopOverlay.Length == 0 && window != null && !ShowDesktop && window.State != "maximized",
            "win-down" or "win-close" => DesktopOverlay.Length == 0 && window != null && !ShowDesktop,
            "win-left" => DesktopOverlay.Length == 0 && window != null && !ShowDesktop && window.State != "snapped-left",
            "win-right" => DesktopOverlay.Length == 0 && window != null && !ShowDesktop && window.State != "snapped-right",
            _ => false
        };
    }

    private bool ApplyWindowsAction(string action)
    {
        if (!WindowsActionAvailable(action)) return false;
        var window = Active;
        switch (action)
        {
            case "win-start":
                bool start = DesktopOverlay != "start";
                ClearWindowsOverlay();
                if (start) DesktopOverlay = "start";
                break;
            case "win-task-view":
                bool taskView = DesktopOverlay != "task-view";
                ClearWindowsOverlay();
                if (taskView)
                {
                    DesktopOverlay = "task-view";
                    DesktopOverlayWindows = Windows.Where(w => w.Workspace == Workspace)
                        .OrderByDescending(w => w.Activation).Select(w => w.Id).ToArray();
                }
                break;
            case "win-cancel": ClearWindowsOverlay(); Switcher = false; SwitchTarget = null; SwitchChoices = []; break;
            case "win-explorer": ClearWindowsOverlay(); Open("files"); break;
            case "win-explorer-parent": return Apply("file-parent");
            case "win-overlay-next":
            case "win-overlay-prev":
                int count = DesktopOverlay == "start" ? WindowsStartApps.Length : DesktopOverlayWindows.Length;
                DesktopOverlaySelection = (DesktopOverlaySelection + (action == "win-overlay-next" ? 1 : count - 1)) % count;
                break;
            case "win-overlay-open":
                if (DesktopOverlay == "start")
                {
                    string app = WindowsStartApps[DesktopOverlaySelection];
                    ClearWindowsOverlay();
                    var local = Windows.Where(w => w.App == app && w.Workspace == Workspace).MaxBy(w => w.Activation);
                    if (local == null) Open(app); else Activate(local);
                }
                else
                {
                    var other = Windows.Find(w => w.Id == DesktopOverlayWindows[DesktopOverlaySelection]);
                    if (DesktopOverlay == "snap")
                        other.State = window.State == "snapped-left" ? "snapped-right" : "snapped-left";
                    ClearWindowsOverlay();
                    Activate(other);
                }
                break;
            case "win-desktop": ClearWindowsOverlay(); ShowDesktop = !ShowDesktop; break;
            case "win-layout": Layout = Layout == "EN" ? "RU" : "EN"; LayoutIsVirtual = true; break;
            case "win-switch": Switch(1); break;
            case "win-maximize": window.State = "maximized"; break;
            case "win-close":
                Windows.Remove(window);
                ActiveWindow = null;
                Activate(DesktopWindow());
                break;
            case "win-down":
                if (window.State != "normal") window.State = "normal";
                else
                {
                    window.MinimizedFrom = window.State;
                    window.State = "minimized";
                    ActiveWindow = null;
                    Activate(DesktopWindow());
                }
                break;
            case "win-left":
            case "win-right":
                window.State = action == "win-left" ? "snapped-left" : "snapped-right";
                DesktopOverlayWindows = Windows.Where(w => w.Id != window.Id && w.Workspace == Workspace && w.State != "minimized")
                    .OrderByDescending(w => w.Activation).Select(w => w.Id).ToArray();
                if (DesktopOverlayWindows.Length > 0) DesktopOverlay = "snap";
                break;
            case "win-close-desktop":
                ClearWindowsOverlay();
                int closing = Workspace;
                int destination = Math.Max(0, closing - 1);
                foreach (var candidate in Windows)
                    if (candidate.Workspace == closing) candidate.Workspace = destination;
                    else if (candidate.Workspace > closing) candidate.Workspace--;
                WorkspaceCount--; Workspace = destination;
                ActiveWindow = null; ShowDesktop = false;
                Activate(DesktopWindow());
                break;
            case "win-prev-desktop":
            case "win-next-desktop":
            case "win-new-desktop":
                ClearWindowsOverlay();
                if (action == "win-new-desktop") Workspace = WorkspaceCount++;
                else Workspace += action == "win-next-desktop" ? 1 : -1;
                ActiveWindow = null; ShowDesktop = false;
                Activate(DesktopWindow());
                break;
        }
        Version++;
        return true;
    }

    internal static string WindowsPath(string folder)
    {
        const string home = "/home/student";
        return folder == home || folder.StartsWith(home + "/", StringComparison.Ordinal)
            ? @"C:\Users\Student" + folder[home.Length..].Replace('/', '\\')
            : @"C:\Учебный_диск" + folder.Replace('/', '\\');
    }
}

internal static partial class LiveCourse
{
    private static void WindowsDesktopMissions(List<LiveMission> list)
    {
        LivePhase open = new("Откройте материалы и Проводник", "win-start win-overlay-open win-explorer");
        LivePhase arrange = new("Разместите окна рядом", "win-left win-overlay-open win-maximize win-down");
        LivePhase focus = new("Переключите окна и временно покажите рабочий стол", "win-switch win-desktop win-desktop");
        LivePhase desks = new("Создайте отдельный рабочий стол и вернитесь к материалам", "win-task-view win-new-desktop win-explorer win-prev-desktop win-next-desktop win-prev-desktop");
        LivePhase appChoice = new("Найдите редактор в меню «Пуск»", "win-start win-overlay-next win-overlay-open");
        LivePhase finishDesk = new("Верните окна со второго рабочего стола", "win-next-desktop win-close-desktop");
        LivePhase explorer = new("Найдите папку проекта и вернитесь в домашнюю папку", "win-explorer pick:project file-open win-explorer-parent");
        for (int level = 0; level < 3; level++)
        {
            // The initial workspace is reproducible independently of the actual host.
            list.Add(new("windows", level, "windows-desktop-" + level, level switch
            {
                0 => "Windows 10: Пуск и окна", 1 => "Windows 10: Проводник и рабочие столы", _ => "Windows 10: окна и рабочие столы"
            }, level switch { 0 => [open, arrange, focus], 1 => [open, arrange, focus, desks, explorer],
                    _ => [open, arrange, focus, desks, explorer, appChoice, finishDesk] },
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareWindowsDesktop()));
            list.Add(new("workday", level, "workday-windows-" + level, "Windows 10: организуйте рабочее место",
                level switch { 0 => [open, arrange, explorer, focus], 1 => [open, arrange, appChoice, focus, desks, explorer],
                    _ => [open, arrange, focus, desks, explorer, appChoice, finishDesk] }, FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareWindowsDesktop()));
            var returnToProject = new LivePhase("Проверьте папку проекта после возвращения",
                "win-explorer pick:project file-open win-explorer-parent");
            var keepExplorer = new LivePhase("Спрячьте и верните открытый Проводник",
                "win-desktop win-desktop");
            var secondary = level switch
            {
                0 => new[] { returnToProject, keepExplorer },
                1 => new[] { returnToProject, desks, keepExplorer },
                _ => new[] { returnToProject, desks, appChoice, finishDesk, keepExplorer }
            };
            list.Add(new("windows", level, "windows-desktop-return-" + level,
                "Windows 10: вернуться к папке проекта", secondary,
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareWindowsDesktop()));
            list.Add(new("workday", level, "workday-windows-return-" + level,
                "Windows 10: проверить папку и сохранить рабочий контекст", secondary,
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareWindowsDesktop()));
        }
        BrowserFileHandoff(list, windows: true);
    }
}
