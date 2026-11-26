using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record LiveEvent(string Key, bool Down = false, string Text = "", bool NativeEnglish = false);

internal sealed record LabPoint(int Second, bool Correct, string Key, string Task, int Milliseconds);

internal sealed partial class LabRun
{
    private readonly Func<double> clock;
    private const string TerminalLogArrivalEvent = "@terminal-log-arrival";
    private double? nextLogArrival;
    private readonly Random random;
    private readonly HashSet<string> held = [];
    private readonly HashSet<string> layoutBlockedKeys = [];
    private readonly List<LabPoint> points = [];
    private readonly List<LiveEvent> trace = [];
    private bool restoring, replayLegacyUrl, replayLegacyReport, replayLegacyNavigation;
    private const string NativeLayoutEvent = "@native-layout";
    private const string SimulatedWindowSwitchEvent = "@simulated-window-switch", TerminalHistoryEvent = "@terminal-reverse-search";
    private const string ExactUrlEvent = "@exact-url", ExactQuickOpenEvent = "@exact-quick-open";
    private const string ChromeHistoryEvent = "@chrome-history-filter", ChromeHistoryPointerEvent = "@chrome-history-pointer";
    private const string ChromeTabSearchEvent = "@chrome-tab-search", ChromeTabPointerEvent = "@chrome-tab-pointer";
    private const string StrictReportEvent = "@strict-report";
    private const string ClusterNavigationEvent = "@cluster-navigation";
    private const string ChromeSurfaceEvent = "@chrome-surface-pointer";
    private const string LineEditingEvent = "@line-editing";
    private const string PythonEditingEvent = "@python-editing";
    private const string NotebookWordEraseEvent = "@notebook-word-erase";
    private const string NotebookActionsEvent = "@notebook-actions-v2";
    private const string NotebookPointerEvent = "@notebook-pointer", NotebookKernelStepEvent = "@notebook-kernel-step",
        ExplorerPointerEvent = "@explorer-pointer";
    private bool replayLegacyNotebookWordErase, replayLegacyCsvExplorer;
    private bool replayLegacyExplorerTargetGuard;
    private const string ExplorerTargetGuardEvent = "@explorer-target-guard-v1";
    private const string CsvExplorerScopeEvent = "@csv-explorer-scope";
    private const string StrictTriggersEvent = "@strict-triggers";
    private const string DesktopFileSearchEvent = "@desktop-file-search";
    private const string WindowsRecoveryScopeEvent = "@windows-recovery-scope";
    private bool replayLegacyWindowsRecovery;
    private bool replayLegacyTriggers;
    private bool replayLegacyLineEditing, replayLegacyPythonEditing;
    private readonly bool neutralProject, nativeEnglishInput, restoreOriginalGuide, restoreLegacyEditorReorder;
    private int variant;
    private string initialLayout, attemptLayout;
    private bool initialVirtual;
    internal bool HasDraft => !CompletedRoute && !limitAccepted && (!ReachedLimit || softFinish || FinishingMission) &&
        (Solved > 0 || Errors > 0 || ActiveMilliseconds > 0 ||
        trace.Any(item => item.Key is not (ChromeTabSearchEvent or ChromeHistoryEvent or TerminalHistoryEvent or ExactUrlEvent or ExactQuickOpenEvent or StrictReportEvent or ClusterNavigationEvent or NativeLayoutEvent or LineEditingEvent or PythonEditingEvent or NotebookWordEraseEvent or NotebookActionsEvent or StrictTriggersEvent or CsvExplorerScopeEvent or DesktopFileSearchEvent or WindowsRecoveryScopeEvent or ExplorerTargetGuardEvent)));
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
    private string status = "Нажмите «Продолжить» после передачи клавиш в учебное окно.";
    internal string Status { get => WindowsRecoveryHint() ?? status; private set => status = value; }
    internal string Feedback { get; private set; } = "ready";
    internal string LastKey { get; private set; } = "";
    internal int Solved { get; private set; }
    internal int SimulatedWindowSwitches { get; private set; }
    internal bool CanSimulateWindowSwitch => App == "ubuntu" && mission != null && !Finished && !AwaitingLimitChoice &&
        pending?.Count > 0 && Task == pending[0].Task &&
        Task.Action is ("switch-next" or "switch-prev" or "switch-group-next" or "switch-group-prev") &&
        !Scene.Switcher && !Scene.Overview && !Scene.Screenshot && LabCatalog.Available(Task, Scene);
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
        missionId is "workday-download-handoff" or "workday-project-file-inbox" or "workday-repair-source-link" ||
        missionId?.StartsWith("code-notebook-", StringComparison.Ordinal) == true ||
        missionId?.StartsWith("code-project-file-", StringComparison.Ordinal) == true;
    private bool CompletedRoute => !restoring && mission != null && pending.Count == 0 &&
        (FinishingMission || softFinish && ReachedLimit || RouteKind == LiveRoutePolicy.Mission ||
            IsCourse && LiveCourse.PracticalTerminalMission(mission.Id) ||
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
