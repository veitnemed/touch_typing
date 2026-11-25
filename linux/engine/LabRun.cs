using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record LiveEvent(string Key, bool Down = false, string Text = "", bool NativeEnglish = false);

internal sealed record LabPoint(int Second, bool Correct, string Key, string Task, int Milliseconds);

internal sealed class LabRun
{
    private readonly Func<double> clock;
    private readonly Random random;
    private readonly HashSet<string> held = [];
    private readonly HashSet<string> layoutBlockedKeys = [];
    private readonly List<LabPoint> points = [];
    private readonly List<LiveEvent> trace = [];
    private bool restoring, replayLegacyUrl, replayLegacyReport, replayLegacyNavigation;
    private const string NativeLayoutEvent = "@native-layout";
    private const string ExactUrlEvent = "@exact-url", ExactQuickOpenEvent = "@exact-quick-open";
    private const string StrictReportEvent = "@strict-report";
    private const string ClusterNavigationEvent = "@cluster-navigation";
    private const string LineEditingEvent = "@line-editing";
    private const string PythonEditingEvent = "@python-editing";
    private const string NotebookWordEraseEvent = "@notebook-word-erase";
    private const string NotebookActionsEvent = "@notebook-actions-v2";
    private const string NotebookPointerEvent = "@notebook-pointer", NotebookKernelStepEvent = "@notebook-kernel-step",
        ExplorerPointerEvent = "@explorer-pointer";
    private bool replayLegacyNotebookWordErase, replayLegacyCsvExplorer;
    private const string CsvExplorerScopeEvent = "@csv-explorer-scope";
    private const string StrictTriggersEvent = "@strict-triggers";
    private bool replayLegacyTriggers;
    private bool replayLegacyLineEditing, replayLegacyPythonEditing;
    private readonly bool neutralProject, nativeEnglishInput, restoreOriginalGuide;
    private int variant;
    private string initialLayout, attemptLayout;
    private bool initialVirtual;
    internal bool HasDraft => !CompletedRoute && !limitAccepted && (!ReachedLimit || softFinish || FinishingMission) &&
        (Solved > 0 || Errors > 0 || ActiveMilliseconds > 0 ||
        trace.Any(item => item.Key is not (ExactUrlEvent or ExactQuickOpenEvent or StrictReportEvent or ClusterNavigationEvent or NativeLayoutEvent or LineEditingEvent or PythonEditingEvent or NotebookWordEraseEvent or NotebookActionsEvent or StrictTriggersEvent or CsvExplorerScopeEvent)));
    private readonly HashSet<string> used = [];
    private readonly ShortcutAttempt[] past;
    private double activeMilliseconds, sinceTask, inputAtActiveMilliseconds;
    private string latchedLimit;
    private double? activeSince;
    private bool stopped, superUsed, finishSingleRoute, limitAccepted, explicitFinish;
    private readonly bool softFinish;
    internal bool FinishingMission { get; private set; }
    private int taskInBlock;
    internal readonly string Session = Guid.NewGuid().ToString("N");
    internal readonly string App, Mode;
    internal readonly int Level, TimeLimit, ActionLimit;
    internal readonly string Guidance;
    internal bool HintVisible { get; private set; }
    private bool hadError;
    private LiveCoaching coach = LiveCoaching.None;
    private string attemptCoachContext;
    internal bool AdaptiveGuide { get; }
    internal string CoachStage => coach.Stage;
    internal string CoachReason => coach.Reason;
    internal string CoachPrompt => coach.Blind && !ShowGuide ? Task.Title : "";
    internal bool ShowGuide => HintVisible || Guidance == "guided" && !coach.Blind;
    internal void Hint()
    {
        if (!HintVisible && (!ShowGuide || restoring)) trace.Add(new("@hint"));
        HintVisible = true;
    }
    private double ActiveMilliseconds => activeMilliseconds + (activeSince is double since ? Math.Max(0, clock() - since) : 0);
    private int Elapsed => (int)Math.Min(ActiveMilliseconds / 1000, int.MaxValue);
    internal LabScene Scene { get; private set; }
    private LabReportArtifact verifiedReport;
    internal LabReportArtifact ReportArtifact => verifiedReport != null && verifiedReport == Scene.ReadReportArtifact() ? verifiedReport : null;
    private LiveMission mission;
    private List<LiveGoal> pending;
    private List<(LiveGoal Goal, string Before)> watching;
    private string requestedMission, switchEvidenceId, switchReleaseKey;
    private readonly bool windowsBindings;
    private readonly string[] dockProfile, desktopProfile;
    private readonly string layoutBinding;
    internal string RouteKind { get; }
    internal bool IsCourse => RouteKind == LiveRoutePolicy.Course;
    internal bool EditorCourse => IsCourse && App == "code";
    internal string MissionId => mission?.Id;
    internal string PhaseTitle => mission == null || pending.Count == 0 ? "" : mission.Phases[pending[0].Phase].Title;
    private int missionLength;
    internal string[] Objectives => mission == null || pending.Count == 0 ? [] : mission.Phases.Skip(pending[0].Phase).Select(phase => phase.Title).ToArray();
    internal readonly List<ShortcutAttempt> Attempts = [];
    internal LabTask Task { get; private set; }
    internal bool Paused { get; private set; } = true;
    internal bool Saved { get; private set; }
    internal string Status { get; private set; } = "Нажмите «Продолжить» после передачи клавиш в учебное окно.";
    internal string Feedback { get; private set; } = "ready";
    internal string LastKey { get; private set; } = "";
    internal int Solved { get; private set; }
    internal int Errors { get; private set; }
    internal int Block { get; private set; } = 1;
    internal string EnvironmentProfile { get; }
    private string SceneApp => App == "workday" ? (EnvironmentProfile == "windows" ? "windows" : "ubuntu") : App;
    internal string ScenarioTitle => mission?.Title ?? "";
    internal int ScenarioStep => Math.Min(missionLength, missionLength - pending.Count + 1);
    internal int ScenarioLength => missionLength;
    internal int ScenarioCompletedGoals => Math.Max(0, missionLength - (pending?.Count ?? 0));
    internal int Seconds => TimeLimit > 0 && !FinishingMission ? Math.Min(Elapsed, TimeLimit) : Elapsed;
    internal string LimitReached => latchedLimit ?? (TimeLimit > 0 && Elapsed >= TimeLimit ? "timer" :
        ActionLimit > 0 && Solved >= ActionLimit ? "actions" : null);
    private bool ReachedLimit => LimitReached != null;
    private static bool IsEssentials(string missionId) => missionId?.StartsWith("terminal-essentials-", StringComparison.Ordinal) == true;
    private static bool JoinsTimedRotation(string missionId) => missionId?.StartsWith("chrome-", StringComparison.Ordinal) == true ||
        missionId is "workday-download-handoff" or "workday-project-file-inbox" ||
        missionId?.StartsWith("code-notebook-", StringComparison.Ordinal) == true ||
        missionId?.StartsWith("code-project-file-", StringComparison.Ordinal) == true;
    private bool CompletedRoute => !restoring && mission != null && pending.Count == 0 &&
        (FinishingMission || softFinish && ReachedLimit || RouteKind == LiveRoutePolicy.Mission ||
            !IsCourse && (finishSingleRoute || mission.Curated && (EnvironmentProfile != "windows" && !IsEssentials(mission.Id) && !JoinsTimedRotation(mission.Id) || TimeLimit == 0 && ActionLimit == 0))
                && mission.FinishAfterRoute);
    private bool PendingLimitChoice => softFinish && !restoring && ReachedLimit &&
        !limitAccepted && !FinishingMission && !CompletedRoute;
    internal bool AwaitingLimitChoice => !stopped && PendingLimitChoice;
    internal bool LimitFlowInProgress => !Completed && (FinishingMission || PendingLimitChoice);
    internal bool LimitAccepted => limitAccepted;
    private bool Completed => limitAccepted || CompletedRoute || !softFinish && !FinishingMission && ReachedLimit;
    internal bool Finished => stopped || !restoring && Completed;
    internal int Remaining => TimeLimit == 0 ? 0 : Math.Max(0, TimeLimit - Seconds);
    internal string[] Held => held.ToArray();

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
        EnvironmentProfile = app == "windows" ? "windows" : app == "workday" ? LabScene.DesktopEnvironment(draft?.DesktopEnvironment ?? environment) : "ubuntu";
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
            "workday-debug-independent" => "independent",
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
            ExactUrlEvent or ExactQuickOpenEvent or StrictReportEvent or ClusterNavigationEvent or LineEditingEvent or PythonEditingEvent or NotebookWordEraseEvent or NotebookActionsEvent or StrictTriggersEvent or CsvExplorerScopeEvent);
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
                else if (e.Key == ExplorerPointerEvent)
                {
                    int cut = e.Text.IndexOf(':');
                    if (cut > 0) ExplorerPointer(e.Text[..cut], e.Text[(cut + 1)..]);
                }
                else if (e.Key == NativeLayoutEvent) ApplyNativeLayout(e.Text, true);
                else if (e.Key == ExactUrlEvent) { replayLegacyUrl = false; trace.Add(e); }
                else if (e.Key == ExactQuickOpenEvent) { Scene.ReplayLegacyQuickOpen = false; trace.Add(e); }
                else if (e.Key == StrictReportEvent) { replayLegacyReport = false; trace.Add(e); }
                else if (e.Key == ClusterNavigationEvent) EndLegacyNavigation(e);
                else if (e.Key == LineEditingEvent) EndLegacyLineEditing(e);
                else if (e.Key == PythonEditingEvent) EndLegacyPythonEditing(e);
                else if (e.Key == NotebookActionsEvent) { Scene.ReplayLegacyNotebookActions = false; trace.Add(e); }
                else if (e.Key == NotebookWordEraseEvent) { replayLegacyNotebookWordErase = false; trace.Add(e); }
                else if (e.Key == StrictTriggersEvent) { replayLegacyTriggers = false; trace.Add(e); }
                else if (e.Key == CsvExplorerScopeEvent) { replayLegacyCsvExplorer = false; trace.Add(e); }
                else Input(e.Key, e.Down, e.Text, e.NativeEnglish ? "EN" : "");
            }
            if (replayLegacyUrl) trace.Add(new(ExactUrlEvent));
            if (Scene.ReplayLegacyQuickOpen)
            { Scene.ReplayLegacyQuickOpen = false; trace.Add(new(ExactQuickOpenEvent)); }
            if (replayLegacyReport) trace.Add(new(StrictReportEvent));
            if (replayLegacyNavigation) EndLegacyNavigation(new(ClusterNavigationEvent));
            if (replayLegacyLineEditing) EndLegacyLineEditing(new(LineEditingEvent));
            if (replayLegacyPythonEditing) EndLegacyPythonEditing(new(PythonEditingEvent));
            if (replayLegacyTriggers) { replayLegacyTriggers = false; trace.Add(new(StrictTriggersEvent)); }
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

    private void EndLegacyNavigation(LiveEvent marker)
    {
        if (replayLegacyNavigation) Scene.EndLegacyNavigation();
        replayLegacyNavigation = false;
        trace.Add(marker);
    }

    private void EndLegacyLineEditing(LiveEvent marker)
    {
        bool changed = replayLegacyLineEditing;
        replayLegacyLineEditing = Scene.ReplayLegacyLineEditing = false;
        if (changed && App == "code") LiveCourse.RebaseEditorLineGoals(pending, Scene);
        trace.Add(marker);
    }

    private void EndLegacyPythonEditing(LiveEvent marker)
    {
        bool changed = replayLegacyPythonEditing;
        replayLegacyPythonEditing = Scene.ReplayLegacyPythonEditing = false;
        if (changed && App == "code") LiveCourse.RebaseEditorLineGoals(pending, Scene);
        trace.Add(marker);
    }

    internal LiveDraft Draft() => new() { App = App, DesktopEnvironment = EnvironmentProfile, Level = Level, Mission = MissionId, Layout = initialLayout, Guidance = Guidance,
        Dock = Scene.Dock, ProjectFixture = neutralProject ? NeutralProject.FixtureId : null, DesktopProfile = Scene.GnomePractice.Length == 0 ? null : Scene.DesktopProfile, LayoutKey = Scene.LayoutKey, Variant = variant, VirtualLayout = initialVirtual, NativeEnglishInput = nativeEnglishInput, EditorCourse = EditorCourse, RouteKind = RouteKind,
        AdaptiveGuide = AdaptiveGuide, CoachStage = CoachStage, CoachReason = CoachReason,
        MissionRevision = Scene.GuideNoteActive ? LabScene.GuideNoteRevision : null,
        Trace = JsonSerializer.Serialize(trace),
        Progress = new() { Session = Session, Mode = Mode, TimeLimit = TimeLimit, ActionLimit = ActionLimit, Solved = Solved,
            Errors = Errors, ActiveMilliseconds = ActiveMilliseconds, Series = JsonSerializer.Serialize(points),
            LimitDecisionPending = PendingLimitChoice, LimitReached = LimitReached,
            FinishingMission = FinishingMission } };

    internal void Pause()
    {
        if (activeSince is double since) activeMilliseconds += Math.Max(0, clock() - since);
        if (!Paused && held.Count > 0) trace.Add(new("@release"));
        Scene.Switcher = false; Scene.SwitchTarget = null; Scene.SwitchChoices = []; switchReleaseKey = switchEvidenceId = null;
        activeSince = null; Paused = true; held.Clear(); layoutBlockedKeys.Clear(); superUsed = false;
        Scene.ResetNotebookChord();
    }
    internal void Resume()
    {
        if (Finished || AwaitingLimitChoice) return;
        if (!Paused) return;
        Paused = false; held.Clear(); activeSince = clock(); sinceTask = clock();
        Feedback = "ready"; Status = "Клавиши принимаются. Выполните задание.";
    }
    internal void HoldForLimit()
    {
        if (!PendingLimitChoice) return;
        latchedLimit ??= LimitReached;
        Pause();
        // Polling and focus handling may notice a deadline after it passed.
        // Time waiting for the choice is not time spent in the mission.
        if (latchedLimit == "timer") activeMilliseconds = Math.Min(activeMilliseconds, TimeLimit * 1000.0);
    }
    internal bool ChooseLimit(string choice)
    {
        if (choice == "finish")
        {
            // Retry the same accepted action after a failed atomic write.
            // Navigation/close never enters this branch.
            if (explicitFinish) return !Saved;
            if (Finished || !softFinish && !FinishingMission) return false;
            Pause(); // Freeze the clock before checking a cap the UI may not have observed yet.
            HoldForLimit();
            limitAccepted = PendingLimitChoice || FinishingMission;
            explicitFinish = true;
            stopped = true;
            return true;
        }
        HoldForLimit();
        if (choice != "continue" || !AwaitingLimitChoice) return false;
        // Reacquire the owner's keyboard before counting active time again.
        FinishingMission = true;
        return true;
    }
    internal void CancelContinuation() => FinishingMission = false;
    internal void Stop() { Pause(); stopped = true; }
    internal void MarkSaved() => Saved = true;

    internal void ApplyNativeLayout(string value, bool takeOver)
    {
        if (value is not ("EN" or "RU") || !restoring && Finished ||
            Scene.LayoutIsVirtual && !takeOver ||
            value == Scene.Layout && !Scene.LayoutIsVirtual) return;
        Scene.Layout = value;
        Scene.LayoutIsVirtual = false;
        trace.Add(new(NativeLayoutEvent, Text: value));
    }

    internal bool NotebookPointer(string action, int index, int offset)
    {
        if (Finished || AwaitingLimitChoice || Paused || !Scene.NotebookPointer(action, index, offset)) return false;
        // Mouse/UI Automation and keyboard shortcuts alter the same document.
        // Only physical keyboard Input() can earn an exact-shortcut credit.
        trace.Add(new(NotebookPointerEvent, Text: action + ":" + index + ":" + offset));
        return true;
    }

    internal bool ExplorerPointer(string action, string path)
    {
        if (Finished || AwaitingLimitChoice || Paused) return false;
        int before = Scene.Version;
        if (!Scene.ExplorerPointer(action, path) && before == Scene.Version) return false;
        trace.Add(new(ExplorerPointerEvent, Text: action + ":" + path));
        return true;
    }

    internal void TickNotebook()
    {
        if (!Finished && !AwaitingLimitChoice && !Paused && Scene.TickNotebook())
            trace.Add(new(NotebookKernelStepEvent));
    }

    internal ShortcutAttempt Input(string key, bool pressed, string text, string nativeLayout = "")
    {
        if (Finished || AwaitingLimitChoice || Paused || string.IsNullOrEmpty(key)) return null;
        if (!restoring && !FinishingMission && TimeLimit > 0 && ActionLimit > 0)
        {
            inputAtActiveMilliseconds = ActiveMilliseconds;
            if (inputAtActiveMilliseconds >= TimeLimit * 1000.0) return null;
        }
        if (!pressed && layoutBlockedKeys.Remove(key)) return null;
        bool targetedText = pressed && (nativeLayout is "EN" or "RU") && RequiresEnglishText(key, text);
        if (pressed && (layoutBlockedKeys.Contains(key) || targetedText && nativeLayout == "RU"))
        {
            layoutBlockedKeys.Add(key);
            LastKey = "";
            Feedback = "layout";
            Status = "Переключи раскладку на EN";
            return null;
        }
        if (pressed && held.Contains(key))
        {
            if (!replayLegacyUrl && Scene.CanRepeatEditing(key) && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super")
                && (!held.Any(item => KeyChord.Family(item) == "Ctrl") || key is "Left" or "Right" or "Home" or "End" ||
                    CanEraseNotebookWord(key, held.Any(item => KeyChord.Family(item) == "Shift"))))
            {
                trace.Add(new(key, true, ""));
                Scene.Text(key, "", held.Any(item => KeyChord.Family(item) == "Shift"),
                    held.Any(item => KeyChord.Family(item) == "Ctrl"));
            }
            return null; // repeat edits can move the caret, never complete a lesson step
        }
        if (!pressed && !held.Contains(key)) return null;
        // Capture before a layout switch or other action can modify the scene.
        bool useNativeEnglish = targetedText && nativeLayout == "EN";
        attemptLayout = useNativeEnglish ? "EN" : Scene.Layout;
        trace.Add(new(key, pressed, text, useNativeEnglish));
        if (!pressed)
        {
            if (!held.Remove(key)) return null;
            if (Scene.Switcher && (key == switchReleaseKey || switchReleaseKey == null && KeyChord.Family(key) == "Alt"))
            {
                Watch(); Scene.CommitSwitch();
                var credited = switchEvidenceId; switchEvidenceId = switchReleaseKey = null;
                return Observe(key, actualTaskId: credited);
            }
            if (Task.Id == "gnome-left-super" && held.Count == 0)
            {
                if (key == "LSuper" && !superUsed)
                {
                    Watch();
                    return Perform(LabCatalog.Shortcuts.First(task => task.Id == "gnome-left-super"), key);
                }
                if (KeyChord.Family(key) is "Alt" or "Super")
                    return Miss(key, key == "RSuper" ? "Это правый Super. Найдите левую клавишу ⊞ рядом с пробелом."
                        : "Это Alt. Сдвиньтесь на одну клавишу левее к кнопке ⊞.");
            }
            if (KeyChord.Family(key) == "Super" && !superUsed && held.Count == 0)
                { Watch(); return Perform(Scene.WindowsDesktop ? LabCatalog.WindowsStartTask : LabCatalog.Shortcuts.First(task => task.Id == "overview"), key); }
            return null;
        }
        if (!held.Add(key)) return null;
        if (KeyChord.IsModifier(key))
        {
            if (KeyChord.Family(key) == "Super") superUsed = false;
            return null;
        }
        if (held.Any(item => KeyChord.Family(item) == "Super")) superUsed = true;
        bool commandModifiers = held.Any(item => KeyChord.Family(item) is "Ctrl" or "Alt" or "Super");
        if (Task.Id == "gnome-left-super" && key == "Z")
            return Miss(key, "Это клавиша Z (Я). Левый Super ⊞ находится ниже, левее Alt.");
        Watch();
        // Keep the physical key in held and trace for release, repeats and
        // saved-draft replay. Interpret its secondary legend only for a
        // navigation goal eligible in the current scene.
        var keypadGoal = replayLegacyTriggers ? null : watching?.Select(item => item.Goal.Task).FirstOrDefault(goal =>
            Gesture.TryParse(goal.Keys, out var gesture) && gesture.Chords.Length == 1 &&
            gesture.Chords[0].AcceptsNavigationKeypad(key, held));
        if (keypadGoal != null && Gesture.TryParse(keypadGoal.Keys, out var keypadGesture))
        {
            key = keypadGesture.Chords[0].Trigger;
            text = "";
        }
        // Lock toggles affect the physical keyboard, not progress in a lesson.
        if (key is "NumLock" or "CapsLock" or "ScrollLock") return null;
        if (Scene.OnNotebook && LiveCourse.ResolveNotebook(key, held, Scene) is LabTask notebook) return Perform(notebook, key);
        var pythonEdit = Scene.OnNotebook ? null : LiveCourse.ResolvePythonEdit(key, held, Scene);
        if (pythonEdit != null) return Perform(pythonEdit, key);
        var actual = Scene.Overview && !commandModifiers && key != "Esc" ? null : LabCatalog.Resolve(key, held, Scene);
        if (Scene.OnNotebook && actual != null && (!LabScene.NotebookGlobalAction(actual.Action) ||
            Scene.ReplayLegacyNotebookActions && actual.Action is "palette" or "code-definition" or "code-back" or "restore-tab")) actual = null;
        string before = Scene.Overview ? Scene.Search : Scene.Active?.App == "terminal" || Scene.Active?.Focus is "terminal" or "reverse-search" ? Scene.TerminalInput : Scene.Active?.Input ?? "";
        bool submitting = Task.Action == "command" && key == "Enter" && !commandModifiers;
        bool matches = submitting && Scene.Terminal.MatchesCommand(before, Task.Text);
        if (actual == null && Scene.WindowsDesktop && (Scene.DesktopOverlay.Length > 0 || Scene.ShowDesktop))
            return Miss(key, WrongKeyMessage(key));
        if (actual != null && !submitting)
        {
            if (actual.Id is "screenshot" or "debug-window-shot") Scene.StartCapture(text);
            return Perform(actual, key);
        }
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        bool word = held.Any(item => KeyChord.Family(item) == "Ctrl") && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super");
        bool navigation = key is "Left" or "Right" or "Home" or "End";
        bool notebookWordDelete = CanEraseNotebookWord(key, shift);
        if (commandModifiers && !(word && (navigation || notebookWordDelete)))
        {
            // Cutting an empty selection in an ordinary field is a valid no-op.
            if (word && key == "X" && Scene.Active?.Focus == "url") return null;
            return Miss(key, WrongKeyMessage(key));
        }
        bool changed = Scene.Text(key, useNativeEnglish ? text : VirtualText(key, text), shift, word);
        bool typedEdit = changed && Task.Action is ("python-edit-write" or "notebook-type") &&
            key is not ("Left" or "Right" or "Up" or "Down" or "Home" or "End" or "Enter" or "Backspace" or "Delete") &&
            !string.IsNullOrEmpty(text) && !text.Any(char.IsControl);
        var passed = Observe(key, matches && Scene.Terminal.LastSuccess ? Task.Text : null, actualTaskId:
            matches && Scene.Terminal.LastSuccess || typedEdit || changed && Task.Action is not ("python-edit-write" or "notebook-type") && Task.Keys.Length == 0 && key == "Enter" && !submitting ? Task.Id : null);
        if (passed != null) return passed;
        if (submitting && !Scene.Terminal.LastSuccess) return Miss(key, Scene.Active.Status);
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.VerifyReport) == true)
            return Miss(key, "Команда выполнена, но отчёт не совпадает с заданием. Проверьте весь сохранённый файл и папку «Загрузки».");
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.ArtifactPath != null) == true)
            return Miss(key, "Сохранённая заметка отличается от исходника. Вернитесь в редактор, вставьте скопированный текст целиком и сохраните файл.");
        if (!changed) return Miss(key, key == "Enter" && Scene.Active?.Focus == "quick-open"
            ? Scene.Active.Status : "Здесь эта клавиша не выполняет действие.");
        Feedback = "ready"; LastKey = ""; Status = ""; return null;
    }

    private bool CanEraseNotebookWord(string key, bool shift) =>
        !replayLegacyNotebookWordErase && Scene.OnNotebook && Scene.Active.Tab.Notebook.Editing &&
        !shift && (key is "Backspace" or "Delete");

    // The virtual keyboard/switching mission and the actual input source are
    // different things. Require EN for known English text goals only; ordinary
    // shortcuts, edits, virtual layout switching and free Unicode input still work.
    private bool RequiresEnglishText(string key, string text)
    {
        if (KeyChord.IsModifier(key) ||
            string.IsNullOrEmpty(text) || text.Any(char.IsControl) ||
            held.Any(item => KeyChord.Family(item) is "Ctrl" or "Alt" or "Super"))
            return false;
        var window = Scene.Active;
        if (Scene.Overview) return Task.Action == "launch" && Task.Text.All(char.IsAscii);
        if (window == null || Scene.Switcher) return false;
        return Task.Action switch
        {
            "command" => window.App == "terminal" || window.Focus is "terminal" or "reverse-search",
            "navigate-pasted" => Scene.BrowserPracticeActive && Task.Text.Length > 0 && window.Focus == "url",
            "file-name-commit" => window.Focus == "file-name",
            "code-clone-command" => Scene.BrowserPracticeActive && window.Focus == "palette",
            "palette-choose" => Scene.AdvancedNotebook && window.Focus == "palette" && Task.Text.Length > 0,
            "notebook-type" => Scene.OnNotebook && window.Tab.Notebook.Editing,
            "python-edit-write" or "gnome-write" or "debug-rewrite" => window.App == "code" && window.Focus == "body",
            "debug-doc-submit" => window.App == "chrome" && window.Focus == "find",
            "project-search-submit" => window.Focus == "project-search",
            "project-search-repair" => window.App == "code" && window.Focus == "body",
            "code-rename-confirm" => window.Focus == "symbol-rename",
            "exact-find-submit" or "research-find" => window.Focus == "find",
            "quick-choose" => (Scene.NotebookCourse || Scene.ExactEdit) && window.Focus == "quick-open" && Task.Text.Length > 0,
            "save" => (Scene.NotebookCourse || Scene.ExactEdit || Scene.CompletingMovedReport) && window.App == "code" && window.Focus == "body" && Task.Text.Length > 0,
            _ => false
        };
    }

    private string WrongKeyMessage(string key) =>
        Task.Keys == "F11" && key == "Print"
            ? "Получена клавиша PrtSc. Для F11 на MateBook попробуйте Fn+F11 или переключите Fn Lock."
            : !ShowGuide || Task.Keys.Length == 0 ? "Клавиша «" + key + "» здесь не выполняет действие." : "Нажата «" + key + "». Подсказка: " + Task.Keys + ".";

    private string VirtualText(string key, string fallback)
    {
        if (!Scene.LayoutIsVirtual) return fallback;
        var cap = TypingKeyboard.Rows.SelectMany(row => row).FirstOrDefault(item => ShortcutCatalog.KeyName(item) == key);
        if (key == "Space") return " ";
        if (cap == null) return fallback;
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        string value = shift ? cap.Shifted(Scene.Layout == "RU" ? Layout.Russian : Layout.English) : cap.Base(Scene.Layout == "RU" ? Layout.Russian : Layout.English);
        return value.Length == 1 ? value : fallback;
    }

    private ShortcutAttempt Perform(LabTask actual, string key)
    {

        if (actual.Id == "screenshot" && ShowGuide && Task.Keys == "F11") return Miss(key, WrongKeyMessage(key));
        bool pythonEdit = actual.Action.StartsWith("python-edit-", StringComparison.Ordinal);
        bool attempted = !pythonEdit && LabCatalog.Available(actual, Scene);
        bool changed = pythonEdit ? Scene.PerformPythonEdit(actual.Action) : attempted && Scene.Apply(actual.Action);
        if (changed && mission != null)
        {
            if (Scene.Switcher && (actual.Id is "switch-next" or "switch-prev" or "switch-group-next" or "switch-group-prev" or "win-switch"))
            {
                switchEvidenceId = actual.Id;
                switchReleaseKey = new[] { "Super", "Alt", "Ctrl" }
                    .Select(family => held.FirstOrDefault(keyName => KeyChord.Family(keyName) == family))
                    .FirstOrDefault(keyName => keyName != null);
            }
            // Alt+D mirrors the browser behavior but does not demonstrate
            // the requested physical Ctrl+L shortcut.
            if (!replayLegacyTriggers && actual.Id is ("next-tab" or "prev-tab") && actual.Keys is ("Ctrl+PageUp" or "Ctrl+PageDown"))
            {
                if (Task.Id == actual.Id) return Miss(key, "Вкладка изменилась. Это задание проверяет " + Task.Keys + ".");
                return null;
            }
            if (!replayLegacyUrl && actual.Id == "url" && actual.Keys == "Alt+D")
            {
                if (Task.Id == "url") return Miss(key, "Адрес выделен. Это задание проверяет Ctrl+L.");
                return null;
            }
            var passed = Observe(key, acceptExistingUrlSelection: !replayLegacyUrl && actual.Id == "url" && actual.Keys != "Alt+D", actualTaskId: actual.Id);
            if (passed != null) return passed;
        }
        if (!changed && attempted && actual.Action == "file-paste" && Task.Text == "!conflict" && Scene.NotebookConflictExpected())
        {
            // Only a real new paste can verify the refusal; an unrelated
            // unchanged state or an old error message cannot earn the goal.
            var conflict = Observe(key, actualTaskId: actual.Id, renewedCsvConflict: true);
            if (conflict != null) return conflict;
        }
        if (changed && actual.Id == "focus-editor" && Task.Id == "focus-editor" && Scene.GuideNoteActive && !Scene.GuideNoteVerified())
            return Miss(key, "Заметка не совпала с тремя фрагментами. Сверьте review-notes.txt и сохраните его, не меняя исходники.");
        if (changed) { Status = ""; LastKey = ""; Feedback = "ready"; return null; }
        if (actual.Action is "file-name-commit" or "file-paste" or "file-undo" && Scene.FileProblem is string reason)
            return Miss(key, reason);
        return Miss(key, "Действие сейчас недоступно. " + Task.Title + ".");
    }

    private void Watch()
    {
        if (mission == null) return;
        if (AdaptiveGuide) attemptCoachContext = CoachContext();
        var eligible = Guidance == "independent" ? pending.TakeWhile(goal => goal.Phase == pending[0].Phase) : pending.Take(1);
        watching = eligible.Select(goal => (goal, ObserveEvidence(goal, null, goal.VerifyReport && !replayLegacyReport))).ToList();
    }

    private bool UsesCsvExplorerScope(LiveGoal goal) => !replayLegacyCsvExplorer && goal.CsvExplorerTarget != null;
    private string ObserveEvidence(LiveGoal goal, string command, bool strictReport) => UsesCsvExplorerScope(goal)
        ? LiveCourse.CsvExplorerProof(goal.Task, Scene)
        : LiveCourse.Evidence(goal.Task, Scene, command, strictReport, goal.ArtifactPath);

    private ShortcutAttempt Observe(string key, string equivalentCommand = null, bool acceptExistingUrlSelection = false,
        string actualTaskId = null, bool renewedCsvConflict = false)
    {
        if (mission == null || watching == null || Scene.Switcher) return null;
        foreach (var (goal, before) in watching)
        {
            bool csvExplorerScope = UsesCsvExplorerScope(goal);
            bool alternatePicker = csvExplorerScope && Guidance == "independent" &&
                goal.Task.Id == "file-next" && actualTaskId == "file-prev";
            if (mission.Curated && actualTaskId != goal.Task.Id && !alternatePicker) continue;
            bool strictReport = goal.VerifyReport && !replayLegacyReport;
            string target = csvExplorerScope ? goal.CsvExplorerTarget :
                goal.VerifyReport && replayLegacyReport ? goal.LegacyTarget : goal.Target;
            bool recheckingArtifact = goal.ArtifactPath != null && goal.Task == Task && equivalentCommand != null;
            if (goal.ArtifactPath != null && !recheckingArtifact) continue;
            bool recheckingConflict = csvExplorerScope && renewedCsvConflict && goal.Task.Text == "!conflict";
            if (before == target && !(Scene.NotebookCourse && actualTaskId == goal.Task.Id && goal.Task.Action is "notebook-run" or "save") &&
                !recheckingArtifact && !recheckingConflict && !(acceptExistingUrlSelection && goal.Task.Id == "url")) continue;
            string command = goal.Task == Task ? equivalentCommand : null;
            if (ObserveEvidence(goal, command, strictReport) != target) continue;
            // Published drafts retain earned progress, but a report receipt is only issued
            // when the reconstructed workspace also meets today's verification rules.
            if (goal.VerifyReport && (strictReport || LiveCourse.Evidence(goal.Task, Scene, command, verifyReport: true) == goal.Target))
                verifiedReport = Scene.ReadReportArtifact();
            Task = goal.Task;
            pending.Remove(goal);
            return Pass(key, alternatePicker ? actualTaskId : null);
        }
        return null;
    }

    private ShortcutAttempt Pass(string key, string actualSkill = null)
    {
        if (!restoring && ActionLimit > 0 && Solved == ActionLimit - 1 && latchedLimit == null)
        {
            latchedLimit = "actions";
            // If an admitted key consumes the last action, processing that key
            // may finish after the timer boundary. Its receipt won, not CPU time.
            if (TimeLimit > 0 && !FinishingMission)
            {
                activeMilliseconds = inputAtActiveMilliseconds;
                activeSince = null;
            }
        }
        var attempt = Record(true, key, actualSkill);
        Solved++; taskInBlock++;
        string result = Task.Action == "command" ? Task.Text switch
        {
            "pwd" => "Текущая папка: " + Scene.Terminal.Cwd,
            "cd" => "Вы перешли в домашнюю папку: " + Scene.Terminal.Cwd,
            "ls" => "В выводе видно содержимое папки: " + Scene.Terminal.Cwd,
            "whoami" => "Имя пользователя показано в выводе терминала.",
            "id" => "Вывод показывает идентификатор пользователя и его группы.",
            "uname" => "Вывод показывает имя операционной системы.",
            "ps" => "Вывод показывает процессы симуляции и их PID.",
            "history" => "В выводе видны команды этой сессии.",
            "clear" => "Вывод очищен. Файлы и текущая папка не изменились.",
            "reboot" => "Перезапущена только учебная система. Текущая папка: " + Scene.Terminal.Cwd,
            _ => Task.Title + " — выполнено."
        } : Task.Title + " — выполнено.";
        if (taskInBlock == 20) { Block++; taskInBlock = 0; }
        LastKey = "";
        Next(); Status = result; Feedback = "good";
        return attempt;
    }

    private ShortcutAttempt Miss(string key, string message)
    {
        hadError = true; Errors++; Feedback = "error"; Status = message; LastKey = key;
        var attempt = Record(false, key);
        if (AdaptiveGuide && !restoring && !coach.Blind) UpdateCoaching();
        return attempt;
    }

    private ShortcutAttempt Record(bool correct, string key, string actualSkill = null)
    {
        int milliseconds = (int)Math.Clamp(clock() - sinceTask, 1, 60000);
        string skill = actualSkill ?? Task.Id;
        var attempt = new ShortcutAttempt { Skill = "lab/" + App + "/" + skill, Session = Session, Verification = "lab-scene",
            Date = DateTime.Now, Correct = correct, Milliseconds = milliseconds, MistakeKey = correct ? null : key, InputMode = "exact", Mission = MissionId, Layout = attemptLayout,
            Assisted = ShowGuide, Independent = Guidance == "independent" || coach.Blind, FirstTry = correct && !hadError,
            CoachStage = CoachStage.Length == 0 ? null : CoachStage, CoachContext = CoachStage.Length == 0 ? null : attemptCoachContext };
        Attempts.Add(attempt); points.Add(new LabPoint(Seconds, correct, key, skill, milliseconds));
        return attempt;
    }

    private void Next()
    {
        var courses = (neutralProject ? NeutralProject.MissionCatalog : LiveCourse.Missions)
            .Where(m => m.App == App && m.Level == Level && (LabScene.MissionEnvironment(m) is null || LabScene.MissionEnvironment(m) == EnvironmentProfile)).ToArray();
        if (CompletedRoute)
        {
            Stop();
            return; // Keep the verified artifact in the final scene snapshot.
        }
        if (mission == null || pending.Count == 0)
        {
            var last = mission?.Id;
            // Keep standalone projects out of other modes. Chrome research cases are part
            // of the same rotation as the older browser routes.
            bool essentials = IsEssentials(last);
            var rotation = IsCourse ? courses : courses.Where(m => essentials
                ? IsEssentials(m.Id)
                : !m.FinishAfterRoute || (EnvironmentProfile == "windows" || JoinsTimedRotation(m.Id)) && m.Curated).ToArray();
            var available = rotation.Where(m => !used.Contains(m.Id)).ToArray();
            if (available.Length == 0) { used.Clear(); available = rotation.Where(m => m.Id != last || rotation.Length == 1).ToArray(); }
            var recent = past.Concat(Attempts).Where(a => a.Mission != null).Select(a => a.Mission).Reverse().Distinct().Take(2).ToHashSet();
            var fresh = available.Where(m => !recent.Contains(m.Id)).ToArray();
            if (!IsCourse && fresh.Length > 0) available = fresh;
            initialLayout = Scene.Layout;
            initialVirtual = Scene.LayoutIsVirtual;
            if (IsCourse && string.IsNullOrEmpty(requestedMission))
            {
                var history = past.Concat(Attempts);
                // Alternate a weak-skill opportunity with the least recently
                // seen lesson. Keep the established-vs-supplement balance.
                var target = AdaptiveGuide && used.Count % 2 == 1
                    ? LiveCoach.Outstanding(App, initialLayout, history) : null;
                mission = LiveRoutePolicy.Pick(available, rotation, mission, EnvironmentProfile, history, target);
                variant = random.Next(3);
                Scene = PrepareMissionScene();
                mission.Prepare?.Invoke(Scene);
                pending = LiveCourse.Compile(mission, Scene, prepared: true, nativeEnglishInput: nativeEnglishInput);
            }
            else if (string.IsNullOrEmpty(requestedMission))
            {
                // Keep the original seeded order: one draw per eligible mission, then the case variant.
                var draws = available.Select(m => (Mission: m, Draw: random.NextDouble())).ToArray();
                if (!restoring) variant = random.Next(3);
                var startingScene = PrepareMissionScene();
                string skillPrefix = "lab/" + App + "/";
                // Old unlabeled mistakes remain in the journal but cannot target an input layout.
                var weak = past.Concat(Attempts).TakeLast(60).Where(a => !a.Correct && a.Layout is "EN" or "RU")
                    .Select(a => (a.Skill[skillPrefix.Length..], a.Layout)).ToHashSet();
                if (weak.Count == 0 || draws.Length == 1)
                {
                    mission = draws.OrderBy(item => item.Draw).First().Mission;
                    pending = LiveCourse.Compile(mission, startingScene, nativeEnglishInput: nativeEnglishInput);
                }
                else
                {
                    // At most one isolated compile per eligible route at mission selection.
                    // Reuse the winner's exact scene variant and goals for the actual lesson.
                    var weakTasks = weak.Select(hit => hit.Item1).ToHashSet(StringComparer.Ordinal);
                    var selected = draws.Select(item =>
                    {
                        if (!LiveCourse.CouldPracticeAny(item.Mission, weakTasks))
                            return (item.Mission, Goals: (List<LiveGoal>)null, Score: item.Draw);
                        var goals = LiveCourse.Compile(item.Mission, startingScene, nativeEnglishInput: nativeEnglishInput);
                        return (item.Mission, Goals: goals, Score: item.Draw / (1 + LiveCourse.CountWeakGoals(goals, weak)));
                    }).OrderBy(item => item.Score).First();
                    mission = selected.Mission;
                    pending = selected.Goals ?? LiveCourse.Compile(mission, startingScene, nativeEnglishInput: nativeEnglishInput);
                }
                mission.Prepare?.Invoke(startingScene);
                Scene = startingScene;
            }
            else
            {
                mission = courses.FirstOrDefault(m => m.Id == requestedMission) ?? throw new ArgumentException("Поручение недоступно для выбранного уровня.");
                if (restoring && restoreOriginalGuide && mission.Id == "code-guide")
                    mission = NeutralProject.LegacyMission(LiveCourse.Missions.Single(m => m.Id == "code-guide"));
                if (!restoring) variant = random.Next(3);
                Scene = PrepareMissionScene();
                mission.Prepare?.Invoke(Scene);
                pending = LiveCourse.Compile(mission, Scene, prepared: true, nativeEnglishInput: nativeEnglishInput);
            }
            requestedMission = null; used.Add(mission.Id); trace.Clear();
            verifiedReport = null;
            if (!restoring)
            {
                trace.Add(new(ExactUrlEvent));
                trace.Add(new(ExactQuickOpenEvent));
                trace.Add(new(StrictReportEvent));
                trace.Add(new(ClusterNavigationEvent));
                trace.Add(new(LineEditingEvent));
                trace.Add(new(PythonEditingEvent));
                trace.Add(new(NotebookActionsEvent));
                trace.Add(new(StrictTriggersEvent));
                if (Scene.NotebookCourse) trace.Add(new(NotebookWordEraseEvent));
                if (Scene.CsvNotebook) trace.Add(new(CsvExplorerScopeEvent));
            }
            foreach (string key in held) trace.Add(new("@held/" + key, superUsed));
            missionLength = pending.Count;
            Scene.PrepareHistory(pending.Where(goal => goal.Task.Action == "command" && goal.Task.Text.Length > 12).Select(goal => goal.Task.Text));
        }
        Task = pending[0].Task;
        sinceTask = clock(); HintVisible = hadError = false;
        if (!restoring) UpdateCoaching();
    }

    private string CoachContext() =>
        string.Join("|", Scene.Active?.App ?? "", Scene.Active?.Focus ?? "", Scene.Active?.Url ?? "", Scene.Terminal.Cwd);

    private void UpdateCoaching()
    {
        coach = AdaptiveGuide && Guidance == "guided"
            ? LiveCoach.Decide(Task, App, Scene.Layout, MissionId, CoachContext(), past.Concat(Attempts))
            : LiveCoaching.None;
    }

    private LabScene PrepareMissionScene()
    {
        var scene = new LabScene(SceneApp, initialLayout, neutralProject)
        { LayoutKey = layoutBinding, LayoutIsVirtual = initialVirtual, ReplayLegacyNavigation = replayLegacyNavigation,
            ReplayLegacyLineEditing = replayLegacyLineEditing, ReplayLegacyPythonEditing = replayLegacyPythonEditing, UseWindowsBindings = windowsBindings };
        if (dockProfile != null) scene.Dock = dockProfile;
        scene.DesktopProfile = desktopProfile;
        scene.PrepareCase(variant, App);
        return scene;
    }

    internal LabSessionRecord SessionRecord() => new() { Session = Session, App = App, DesktopEnvironment = EnvironmentProfile, Mode = Mode, Level = Level, Date = DateTime.Now,
        Seconds = Seconds, Solved = Solved, Errors = Errors, TimeLimit = TimeLimit, ActionLimit = ActionLimit, Guidance = Guidance, Completed = Completed };

    internal object Result(IEnumerable<LabSessionRecord> sessions, IEnumerable<LabSessionRecord> records = null)
    {
        var matching = sessions.Concat(records ?? []).GroupBy(item => item.Session).Select(group => group.First()).Where(item => item.App == App && LabScene.DesktopEnvironment(item.DesktopEnvironment) == EnvironmentProfile && item.Mode == Mode && item.Level == Level && item.TimeLimit == TimeLimit && item.ActionLimit == ActionLimit && item.Guidance == Guidance).ToArray();
        var previousBest = matching.Where(item => item.Session != Session && item.Completed)
            .OrderByDescending(item => item.Solved).ThenBy(item => item.Errors).FirstOrDefault();
        int previousRecord = previousBest?.Solved ?? 0;
        bool complete = Completed;
        bool better = complete && (previousBest == null || Solved > previousBest.Solved || Solved == previousBest.Solved && Errors < previousBest.Errors);
        return new { App, Mode, Level, Solved, Errors, Seconds, TimeLimit, ActionLimit, Guidance, completed = complete,
            accuracy = Solved + Errors == 0 ? 0 : Math.Round(100.0 * Solved / (Solved + Errors), 1),
            record = Math.Max(previousRecord, complete ? Solved : 0), previousRecord, previousRecordErrors = previousBest?.Errors ?? 0, newRecord = better,
            series = points, weak = points.Where(point => !point.Correct).GroupBy(point => point.Task).Select(group =>
            { var task = LabCatalog.AllTasks.Concat(LiveCourse.NotebookActions).FirstOrDefault(item => item.Id == group.Key); return new { id = group.Key, title = task?.Title ?? (group.Key == "notebook-type" ? "Редактирование ячейки" : "Ввод команды"), keys = task?.Keys ?? "", errors = group.Count() }; }),
            history = matching.OrderByDescending(item => item.Date).Take(15).Select(item => new
            { item.Session, item.App, item.Mode, item.Level, item.Date, item.Seconds, item.Solved, item.Errors, item.Completed }) };
    }
}

internal static class LabSessionRecords
{
    internal static void Update(StudioProfile profile, LabSessionRecord record)
    {
        profile.LabSessions.RemoveAll(item => item.Session == record.Session);
        profile.LabSessions.Add(record);
        // Read legacy history before pruning so its best completed run survives migration.
        profile.LabRecords = profile.LabRecords.Concat(profile.LabSessions)
            .Where(item => item.Completed && ((item.ActionLimit > 0 && item.Solved >= item.ActionLimit) || (item.TimeLimit > 0 && item.Seconds >= item.TimeLimit)) && LabCatalog.Apps.Contains(item.App) &&
                item.Mode is "drill" or "sandbox" && item.Level is >= 0 and <= 2)
            .GroupBy(item => new { item.App, item.Mode, item.Level, item.Guidance, item.TimeLimit, item.ActionLimit })
            .Select(group => group.OrderByDescending(item => item.Solved).ThenBy(item => item.Errors)
                .ThenBy(item => item.Date).First()).ToList();
        if (profile.LabSessions.Count > 500)
            profile.LabSessions.RemoveRange(0, profile.LabSessions.Count - 500);
    }
}
