using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabRun
{
    internal LabRun(string app, string mode, int level, int seconds, string layout, IEnumerable<ShortcutAttempt> history,
        Random random = null, Func<double> clock = null, int actionLimit = 0, string guidance = "guided", string[] dock = null, string layoutKey = "Super+Space", string missionId = null, LiveDraft draft = null, bool softFinish = false, bool combinedLimits = false, bool windowsBindings = false, string[] desktopProfile = null, bool neutralProject = false, string environment = "ubuntu", bool nativeEnglishInput = false, bool editorCourse = false, string routeKind = "", bool adaptiveGuide = false)
    {
        if (!LabCatalog.Apps.Contains(app)) throw new ArgumentException("Выберите Ubuntu, Chrome, VS Code или Терминал.");
        if (mode is not ("drill" or "sandbox")) throw new ArgumentException("Выберите тренировку или учебный рабочий стол.");
        var progress = draft?.Progress;
        this.neutralProject = draft == null ? neutralProject : draft.ProjectFixture == NeutralProject.FixtureId;
        this.nativeEnglishInput = draft?.NativeEnglishInput ?? nativeEnglishInput;
        restoreOriginalGuide = this.neutralProject && draft?.Mission == "code-guide" &&
            draft.MissionRevision != LabScene.GuideNoteRevision;
        RouteKind = LiveRoutePolicy.Resolve(routeKind, missionId, editorCourse, app, draft);
        AdaptiveGuide = RouteKind is LiveRoutePolicy.Course or LiveRoutePolicy.Mission && (draft?.AdaptiveGuide ?? adaptiveGuide);
        this.softFinish = softFinish;
        this.windowsBindings = windowsBindings;
        EnvironmentProfile = app == "windows" ? "windows" : app is "workday" or "terminal" ? LabScene.DesktopEnvironment(draft?.DesktopEnvironment ?? environment) : "ubuntu";
        App = app; Mode = progress?.Mode is "drill" or "sandbox" ? progress.Mode : mode; Level = Math.Clamp(level, 0, 2);
        ActionLimit = Math.Max(0, progress?.ActionLimit ?? actionLimit);
        // Old callers preserve action-over-timer precedence. Any saved pair
        // has already been normalized by the client that started the attempt.
        TimeLimit = Math.Max(0, progress?.TimeLimit ?? (ActionLimit > 0 && !combinedLimits ? 0 : seconds));
        if (!string.IsNullOrEmpty(progress?.Session)) Session = progress.Session;
        // Forced guidance on isolated debug exercises must not mutate a
        // rotating course merely because it was resumed during that scene.
        Guidance = (IsCourse ? null : draft?.Mission ?? missionId) switch
        {
            "workday-debug-guided" => "guided",
            "workday-debug-independent" or LiveCourse.BashDiskRepeatId or LiveCourse.PowerShellDiskRepeatId => "independent",
            _ => guidance == "independent" ? guidance : "guided"
        };
        this.random = random ?? Random.Shared; this.clock = clock ?? (() => Environment.TickCount64);
        past = history.Where(attempt => attempt.Skill?.StartsWith("lab/" + app + "/", StringComparison.Ordinal) == true).ToArray();
        requestedMission = draft?.Mission ?? missionId;
        if (IsCourse && draft != null)
            used.UnionWith(past.Where(a => a.Session == Session && a.Correct && a.Mission != null).Select(a => a.Mission));
        // Preserve what older clients meant by an explicit unbounded curated
        // task. New clients explicitly distinguish a course from a mission.
        finishSingleRoute = !string.IsNullOrEmpty(requestedMission) && TimeLimit == 0 && ActionLimit == 0;
        dockProfile = draft?.Dock ?? dock; layoutBinding = draft?.LayoutKey ?? layoutKey;
        this.desktopProfile = draft?.DesktopProfile ?? desktopProfile ?? [];
        variant = draft?.Variant ?? -1; restoring = replayLegacyNavigation = draft != null;
        var savedEvents = draft == null ? Array.Empty<LiveEvent>() : JsonSerializer.Deserialize<LiveEvent[]>(draft.Trace) ?? [];
        // New traces begin with format markers. Old ones acquire the line
        // marker only after the historic keystrokes have been replayed.
        var initialFormat = savedEvents.TakeWhile(e => e.Key is
            ChromeTabSearchEvent or ChromeHistoryEvent or TerminalHistoryEvent or ExactUrlEvent or ExactQuickOpenEvent or StrictReportEvent or ClusterNavigationEvent or LineEditingEvent or PythonEditingEvent or NotebookWordEraseEvent or NotebookActionsEvent or StrictTriggersEvent or CsvExplorerScopeEvent or DesktopFileSearchEvent);
        replayLegacyNotebookWordErase = draft != null && !initialFormat.Any(e => e.Key == NotebookWordEraseEvent);
        replayLegacyTriggers = replayLegacyCsvExplorer = draft != null;
        replayLegacyLineEditing = draft != null && !initialFormat.Any(e => e.Key == LineEditingEvent);
        replayLegacyPythonEditing = draft != null && !initialFormat.Any(e => e.Key == PythonEditingEvent);
        Scene = new LabScene(SceneApp, draft?.Layout ?? layout, this.neutralProject) { LayoutKey = layoutKey, LayoutIsVirtual = draft?.VirtualLayout ?? false };
        bool legacyNotebookActions = draft != null && !initialFormat.Any(e => e.Key == NotebookActionsEvent);
        if (dock != null) Scene.Dock = dock;
        Next();
        // Next creates the mission's prepared scene. Only historical input,
        // not the route definitions used by its compiler, has legacy semantics.
        Scene.ReplayLegacyNotebookActions = legacyNotebookActions;
        Scene.ReplayLegacyQuickOpen = draft != null && !initialFormat.Any(e => e.Key == ExactQuickOpenEvent);
        Scene.ReplayLegacyTerminalSearch = draft != null && !initialFormat.Any(e => e.Key == TerminalHistoryEvent);
        Scene.ReplayLegacyChromeHistory = draft != null && !initialFormat.Any(e => e.Key == ChromeHistoryEvent);
        Scene.ReplayLegacyDesktopFileSearch = draft != null && !initialFormat.Any(e => e.Key == DesktopFileSearchEvent);
        if (draft != null)
        {
            Resume();
            replayLegacyUrl = replayLegacyReport = true;
            foreach (var e in savedEvents)
            {
                if (e.Key.StartsWith("@held/")) { held.Add(e.Key[6..]); superUsed = e.Down; }
                else if (e.Key == "@release") { Pause(); Resume(); }
                else if (e.Key == "@hint") Hint();
                else if (e.Key == NotebookPointerEvent)
                {
                    var parts = e.Text.Split(':');
                    if (parts.Length is 2 or 3 && int.TryParse(parts[1], out int index))
                        NotebookPointer(parts[0], index, parts.Length == 3 && int.TryParse(parts[2], out int offset) ? offset : -1);
                }
                else if (e.Key == NotebookKernelStepEvent) TickNotebook();
                else if (e.Key == TerminalLogArrivalEvent) AppendTerminalLog();
                else if (e.Key == SimulatedWindowSwitchEvent) SimulateWindowSwitch();
                else if (e.Key == ChromeSurfaceEvent)
                {
                    string[] args = e.Text.Split(':');
                    if (args.Length == 3 && int.TryParse(args[2], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var slot)) ChromeSurfacePointer(args[0],args[1],slot);
                }
                else if (e.Key == ExplorerPointerEvent)
                {
                    int cut = e.Text.IndexOf(':');
                    if (cut > 0) ExplorerPointer(e.Text[..cut], e.Text[(cut + 1)..]);
                }
                else if (e.Key == NativeLayoutEvent) ApplyNativeLayout(e.Text, true);
                else if (e.Key == ExactUrlEvent) { replayLegacyUrl = false; trace.Add(e); }
                else if (e.Key == ExactQuickOpenEvent) { Scene.ReplayLegacyQuickOpen = false; trace.Add(e); }
                else if (e.Key == TerminalHistoryEvent) EndLegacyTerminalSearch(e);
                else if (e.Key == ChromeHistoryEvent) EndLegacyChromeHistory(e);
                else if (e.Key == ChromeTabSearchEvent) { Scene.ReplayLegacyTabSearch = false; trace.Add(e); }
                else if (e.Key == ChromeTabPointerEvent)
                {
                    int cut = e.Text.IndexOf(':');
                    if (cut > 0) BrowserTabPointer(e.Text[..cut], e.Text[(cut + 1)..]);
                }
                else if (e.Key == ChromeHistoryPointerEvent)
                {
                    int cut = e.Text.IndexOf(':');
                    if (cut > 0) BrowserHistoryPointer(e.Text[..cut], e.Text[(cut + 1)..]);
                }
                else if (e.Key == StrictReportEvent) { replayLegacyReport = false; trace.Add(e); }
                else if (e.Key == ClusterNavigationEvent) EndLegacyNavigation(e);
                else if (e.Key == LineEditingEvent) EndLegacyLineEditing(e);
                else if (e.Key == PythonEditingEvent) EndLegacyPythonEditing(e);
                else if (e.Key == NotebookActionsEvent) { Scene.ReplayLegacyNotebookActions = false; trace.Add(e); }
                else if (e.Key == NotebookWordEraseEvent) { replayLegacyNotebookWordErase = false; trace.Add(e); }
                else if (e.Key == StrictTriggersEvent) { replayLegacyTriggers = false; trace.Add(e); }
                else if (e.Key == DesktopFileSearchEvent) { Scene.ReplayLegacyDesktopFileSearch = false; trace.Add(e); }
                else if (e.Key == CsvExplorerScopeEvent) { replayLegacyCsvExplorer = false; trace.Add(e); }
                else Input(e.Key, e.Down, e.Text, e.NativeEnglish ? "EN" : "");
            }
            if (replayLegacyUrl) trace.Add(new(ExactUrlEvent));
            if (Scene.ReplayLegacyTerminalSearch) EndLegacyTerminalSearch(new(TerminalHistoryEvent));
            if (Scene.ReplayLegacyChromeHistory) EndLegacyChromeHistory(new(ChromeHistoryEvent));
            if (Scene.ReplayLegacyTabSearch) { Scene.ReplayLegacyTabSearch = false; trace.Add(new(ChromeTabSearchEvent)); }
            if (Scene.ReplayLegacyQuickOpen)
            { Scene.ReplayLegacyQuickOpen = false; trace.Add(new(ExactQuickOpenEvent)); }
            if (replayLegacyReport) trace.Add(new(StrictReportEvent));
            if (replayLegacyNavigation) EndLegacyNavigation(new(ClusterNavigationEvent));
            if (replayLegacyLineEditing) EndLegacyLineEditing(new(LineEditingEvent));
            if (replayLegacyPythonEditing) EndLegacyPythonEditing(new(PythonEditingEvent));
            if (replayLegacyTriggers) { replayLegacyTriggers = false; trace.Add(new(StrictTriggersEvent)); }
            if (Scene.ReplayLegacyDesktopFileSearch) { Scene.ReplayLegacyDesktopFileSearch = false; trace.Add(new(DesktopFileSearchEvent)); }
            // Do not earn different credits for already-recorded keys. A resumed CSV
            // lesson starts using object-scoped evidence only for subsequent input.
            if (replayLegacyCsvExplorer && Scene.CsvNotebook) trace.Add(new(CsvExplorerScopeEvent));
            replayLegacyCsvExplorer = false;
            // Earlier notebook drafts recorded rejected Ctrl+Delete/Backspace
            // as key events. Do not turn them into edits while replaying.
            if (replayLegacyNotebookWordErase && Scene.NotebookCourse) trace.Add(new(NotebookWordEraseEvent));
            replayLegacyNotebookWordErase = false;
            if (Scene.ReplayLegacyNotebookActions)
            { Scene.ReplayLegacyNotebookActions = false; trace.Add(new(NotebookActionsEvent)); }
            replayLegacyUrl = replayLegacyReport = false;
            Pause();
            // Trace describes the current mission, not the whole attempt. Its
            // replay reconstructs the scene without generating any new credit.
            Solved = Math.Max(0, progress?.Solved ?? 0);
            SimulatedWindowSwitches = Math.Max(0, progress?.SimulatedWindowSwitches ?? SimulatedWindowSwitches);
            Errors = Math.Max(0, progress?.Errors ?? 0);
            taskInBlock = Solved % 20;
            Block = 1 + Solved / 20;
            activeMilliseconds = progress != null && double.IsFinite(progress.ActiveMilliseconds)
                ? Math.Clamp(progress.ActiveMilliseconds, 0, (double)int.MaxValue * 1000) : 0;
            points.Clear();
            if (progress?.Series != null)
                points.AddRange(JsonSerializer.Deserialize<LabPoint[]>(progress.Series) ?? []);
            Attempts.Clear(); LastKey = ""; Feedback = "ready";
            FinishingMission = progress?.FinishingMission == true;
            latchedLimit = progress?.LimitReached is "timer" or "actions" ? progress.LimitReached : null;
            if (AdaptiveGuide && draft.CoachStage is LiveCoach.Practice or LiveCoach.Recall or LiveCoach.Transfer)
                coach = new(draft.CoachStage, draft.CoachReason ?? "");
            restoring = false;
        }
    }
}
