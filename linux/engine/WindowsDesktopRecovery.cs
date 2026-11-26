using System;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private LabWindow WindowsOverlayWindow() => DesktopOverlayWindows.ElementAtOrDefault(DesktopOverlaySelection) is string id
        ? Windows.Find(w => w.Id == id) : null;

    internal object WindowsRecoveryEvidence(LabTask task)
    {
        if (!WindowsDesktop) return null;
        var w = Active;
        return task.Id switch
        {
            "win-explorer" or "win-explorer-parent" or "file-open" =>
                new { Workspace, w?.App, folder = w?.App == "files" ? w.Folder : null,
                    file = w?.App == "code" && w.Tab != null ? EditorPath(w.Tab) : null, w?.Focus },
            "file-next" or "file-prev" =>
                new { Workspace, w?.App, w?.Folder, w?.Focus,
                    path = w == null ? null : Entries(w).ElementAtOrDefault(w.Item)?.Path },
            "win-start" or "win-task-view" or "win-cancel" => new { Workspace, DesktopOverlay },
            "win-overlay-next" or "win-overlay-prev" => new
            {
                Workspace, DesktopOverlay,
                choice = DesktopOverlay == "start" ? WindowsStartApps.ElementAtOrDefault(DesktopOverlaySelection) : WindowsOverlayWindow()?.App
            },
            "win-overlay-open" or "win-left" or "win-right" or "win-maximize" or "win-down" or "win-switch" =>
                new { Workspace, DesktopOverlay, w?.App, w?.State },
            "win-desktop" => new { Workspace, ShowDesktop },
            "win-new-desktop" or "win-close-desktop" or "win-prev-desktop" or "win-next-desktop" => new { Workspace, WorkspaceCount },
            _ => null
        };
    }

    // Pointer activation changes the actual simulation state but never
    // masquerades as a physical shortcut. IDs are stable across draft replay.
    private bool WindowsDesktopPointer(string action, string id)
    {
        if (!WindowsDesktop || Switcher || Overview || Screenshot || DesktopOverlay.Length != 0) return false;
        if (action == "desktop-taskbar")
        {
            if (!WindowsStartApps.Contains(id)) return false;
            var running = Windows.Where(w => w.Workspace == Workspace && w.App == id).MaxBy(w => w.Activation);
            if (running == null) { Open(id); return true; }
            if (running.Id == ActiveWindow && !ShowDesktop && running.State != "minimized") return false;
            Activate(running); Version++; return true;
        }
        if (ShowDesktop) return false;
        var target = Windows.Find(w => w.Id == id && w.Workspace == Workspace && w.State != "minimized");
        if (target == null) return false;
        if (action == "desktop-focus")
        {
            if (target.Id == ActiveWindow) return false;
            Activate(target); Version++; return true;
        }
        if (action is not ("desktop-minimize" or "desktop-maximize" or "desktop-close")) return false;
        Activate(target);
        switch (action)
        {
            case "desktop-minimize":
                target.MinimizedFrom = target.State;
                target.State = "minimized";
                ActiveWindow = null;
                Activate(DesktopWindow());
                break;
            case "desktop-maximize":
                if (target.State == "maximized") target.State = target.UntileTo;
                else { target.UntileTo = target.State; target.State = "maximized"; }
                break;
            case "desktop-close":
                if (target.App == "code" && target.Tabs.Any(tab => tab.Body != tab.SavedBody))
                {
                    target.Status = "В редакторе есть несохранённые изменения. Сохраните файл и повторите закрытие.";
                    Version++; return true;
                }
                return Apply("win-close");
        }
        Version++;
        return true;
    }
}

internal static partial class LiveCourse
{
    // This is intentionally restricted to the foundation desktop routes.
    // Specialised reports and file handoffs continue to verify their stronger
    // evidence (file contents, original copies, and revision-specific paths).
    internal static bool HasWindowsRecoveryScope(string missionId) =>
        missionId is { Length: > 1 } && missionId[^1] is >= '0' and <= '2' &&
        missionId[..^1] is "windows-desktop-" or "windows-desktop-return-" or "workday-windows-" or "workday-windows-return-";

    internal static string WindowsRecoveryProof(LabTask task, LabScene scene) =>
        scene.WindowsRecoveryEvidence(task) is object evidence ? JsonSerializer.Serialize(evidence) : null;
}

internal sealed partial class LabRun
{
    internal string WindowsRecoveryHint()
    {
        if (Paused || Finished || AwaitingLimitChoice || Scene?.WindowsDesktop != true ||
            Task?.Id != "file-next" || Task.Text != "project") return null;
        if (Scene.Overview || Scene.Switcher || Scene.DesktopOverlay.Length != 0) return null;
        if (Scene.ShowDesktop)
            return "Показан рабочий стол. " + (ShowGuide ? "Нажмите Win+D, чтобы вернуть окна." : "Верните учебные окна.");
        var w = Scene.Active;
        if (w?.App != "files")
            return "Проводник не в фокусе. " + (ShowGuide ? "Щёлкните его значок или нажмите Alt+Tab." : "Вернитесь в Проводник.");
        if (w.Focus != "body")
            return ShowGuide ? "Закройте режим поиска или ввода клавишей Esc и вернитесь к списку папок." : "Вернитесь к списку папок.";
        if (w.Folder != "/home/student")
            return "Папка project находится в C:\\Users\\Student. " +
                (ShowGuide ? "Вернитесь туда через Alt+↑." : "Перейдите в домашнюю папку.");
        return null;
    }
}
