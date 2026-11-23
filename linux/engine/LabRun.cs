using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record LiveEvent(string Key, bool Down = false, string Text = "");

internal sealed record LabPoint(int Second, bool Correct, string Key, string Task, int Milliseconds);

internal sealed class LabRun
{
    private readonly Func<double> clock;
    private readonly Random random;
    private readonly HashSet<string> held = [];
    private readonly List<LabPoint> points = [];
    private readonly List<LiveEvent> trace = [];
    private bool restoring, replayLegacyUrl, replayLegacyReport, replayLegacyNavigation;
    private const string ExactUrlEvent = "@exact-url";
    private const string StrictReportEvent = "@strict-report";
    private const string ClusterNavigationEvent = "@cluster-navigation";
    private int variant;
    private string initialLayout, attemptLayout;
    private bool initialVirtual;
    internal bool HasDraft => !CompletedRoute && !limitAccepted && (!ReachedLimit || softFinish || FinishingMission) &&
        (Solved > 0 || Errors > 0 || ActiveMilliseconds > 0 || trace.Any(item => item.Key is not (ExactUrlEvent or StrictReportEvent or ClusterNavigationEvent)));
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
    internal bool ShowGuide => Guidance == "guided" || HintVisible;
    internal void Hint()
    {
        if (!HintVisible && !ShowGuide) trace.Add(new("@hint"));
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
    private string requestedMission;
    private readonly string[] dockProfile;
    private readonly string layoutBinding;
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
    internal string ScenarioTitle => mission?.Title ?? "";
    internal int ScenarioStep => Math.Min(missionLength, missionLength - pending.Count + 1);
    internal int ScenarioLength => missionLength;
    internal int Seconds => TimeLimit > 0 && !FinishingMission ? Math.Min(Elapsed, TimeLimit) : Elapsed;
    internal string LimitReached => latchedLimit ?? (TimeLimit > 0 && Elapsed >= TimeLimit ? "timer" :
        ActionLimit > 0 && Solved >= ActionLimit ? "actions" : null);
    private bool ReachedLimit => LimitReached != null;
    private bool CompletedRoute => !restoring && mission != null && pending.Count == 0 &&
        (FinishingMission || softFinish && ReachedLimit || finishSingleRoute && mission.FinishAfterRoute);
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
        Random random = null, Func<double> clock = null, int actionLimit = 0, string guidance = "guided", string[] dock = null, string layoutKey = "Super+Space", string missionId = null, LiveDraft draft = null, bool softFinish = false, bool combinedLimits = false)
    {
        if (!LabCatalog.Apps.Contains(app)) throw new ArgumentException("Выберите Ubuntu, Chrome, VS Code или Терминал.");
        if (mode is not ("drill" or "sandbox")) throw new ArgumentException("Выберите тренировку или учебный рабочий стол.");
        var progress = draft?.Progress;
        this.softFinish = softFinish;
        App = app; Mode = progress?.Mode is "drill" or "sandbox" ? progress.Mode : mode; Level = Math.Clamp(level, 0, 2);
        ActionLimit = Math.Max(0, progress?.ActionLimit ?? actionLimit);
        // Old callers preserve action-over-timer precedence. Any saved pair
        // has already been normalized by the client that started the attempt.
        TimeLimit = Math.Max(0, progress?.TimeLimit ?? (ActionLimit > 0 && !combinedLimits ? 0 : seconds));
        if (!string.IsNullOrEmpty(progress?.Session)) Session = progress.Session;
        Guidance = guidance == "independent" ? guidance : "guided";
        this.random = random ?? Random.Shared; this.clock = clock ?? (() => Environment.TickCount64);
        past = history.Where(attempt => attempt.Skill?.StartsWith("lab/" + app + "/", StringComparison.Ordinal) == true).ToArray();
        requestedMission = draft?.Mission ?? missionId;
        // The explicit unlimited project ends once its artifact is verified.
        // Timed and action-limit sessions continue to honor the chosen length.
        finishSingleRoute = !string.IsNullOrEmpty(requestedMission) && TimeLimit == 0 && ActionLimit == 0;
        dockProfile = draft?.Dock ?? dock; layoutBinding = draft?.LayoutKey ?? layoutKey;
        variant = draft?.Variant ?? -1; restoring = replayLegacyNavigation = draft != null;
        Scene = new LabScene(app == "workday" ? "ubuntu" : app, draft?.Layout ?? layout) { LayoutKey = layoutKey, LayoutIsVirtual = draft?.VirtualLayout ?? false };
        if (dock != null) Scene.Dock = dock;
        Next();
        if (draft != null)
        {
            Resume();
            replayLegacyUrl = replayLegacyReport = true;
            foreach (var e in JsonSerializer.Deserialize<LiveEvent[]>(draft.Trace) ?? [])
            {
                if (e.Key.StartsWith("@held/")) { held.Add(e.Key[6..]); superUsed = e.Down; }
                else if (e.Key == "@release") { Pause(); Resume(); }
                else if (e.Key == "@hint") Hint();
                else if (e.Key == ExactUrlEvent) { replayLegacyUrl = false; trace.Add(e); }
                else if (e.Key == StrictReportEvent) { replayLegacyReport = false; trace.Add(e); }
                else if (e.Key == ClusterNavigationEvent) EndLegacyNavigation(e);
                else Input(e.Key, e.Down, e.Text);
            }
            if (replayLegacyUrl) trace.Add(new(ExactUrlEvent));
            if (replayLegacyReport) trace.Add(new(StrictReportEvent));
            if (replayLegacyNavigation) EndLegacyNavigation(new(ClusterNavigationEvent));
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
            restoring = false;
        }
    }

    private void EndLegacyNavigation(LiveEvent marker)
    {
        if (replayLegacyNavigation) Scene.EndLegacyNavigation();
        replayLegacyNavigation = false;
        trace.Add(marker);
    }

    internal LiveDraft Draft() => new() { App = App, Level = Level, Mission = MissionId, Layout = initialLayout, Guidance = Guidance,
        Dock = Scene.Dock, LayoutKey = Scene.LayoutKey, Variant = variant, VirtualLayout = initialVirtual, Trace = JsonSerializer.Serialize(trace),
        Progress = new() { Session = Session, Mode = Mode, TimeLimit = TimeLimit, ActionLimit = ActionLimit, Solved = Solved,
            Errors = Errors, ActiveMilliseconds = ActiveMilliseconds, Series = JsonSerializer.Serialize(points),
            LimitDecisionPending = PendingLimitChoice, LimitReached = LimitReached,
            FinishingMission = FinishingMission } };

    internal void Pause()
    {
        if (activeSince is double since) activeMilliseconds += Math.Max(0, clock() - since);
        if (!Paused && held.Count > 0) trace.Add(new("@release"));
        Scene.Switcher = false; Scene.SwitchTarget = null;
        activeSince = null; Paused = true; held.Clear(); superUsed = false;
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

    internal ShortcutAttempt Input(string key, bool pressed, string text)
    {
        if (Finished || AwaitingLimitChoice || Paused || string.IsNullOrEmpty(key)) return null;
        if (!restoring && !FinishingMission && TimeLimit > 0 && ActionLimit > 0)
        {
            inputAtActiveMilliseconds = ActiveMilliseconds;
            if (inputAtActiveMilliseconds >= TimeLimit * 1000.0) return null;
        }
        if (pressed && held.Contains(key))
        {
            if (!replayLegacyUrl && Scene.CanRepeatEditing(key) && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super")
                && (!held.Any(item => KeyChord.Family(item) == "Ctrl") || key is "Left" or "Right" or "Home" or "End"))
            {
                trace.Add(new(key, true, ""));
                Scene.Text(key, "", held.Any(item => KeyChord.Family(item) == "Shift"),
                    held.Any(item => KeyChord.Family(item) == "Ctrl"));
            }
            return null; // repeat edits can move the caret, never complete a lesson step
        }
        if (!pressed && !held.Contains(key)) return null;
        // Capture before a layout switch or other action can modify the scene.
        attemptLayout = Scene.Layout;
        trace.Add(new(key, pressed, text));
        if (!pressed)
        {
            if (!held.Remove(key)) return null;
            if (KeyChord.Family(key) == "Alt" && Scene.Switcher) { Watch(); Scene.CommitSwitch(); return Observe(key); }
            if (KeyChord.Family(key) == "Super" && !superUsed && held.Count == 0)
                { Watch(); return Perform(LabCatalog.Shortcuts.First(task => task.Id == "overview"), key); }
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
        Watch();
        var actual = Scene.Overview && !commandModifiers && key != "Esc" ? null : LabCatalog.Resolve(key, held, Scene);
        string before = Scene.Overview ? Scene.Search : Scene.Active?.App == "terminal" || Scene.Active?.Focus is "terminal" or "reverse-search" ? Scene.TerminalInput : Scene.Active?.Input ?? "";
        bool submitting = Task.Action == "command" && key == "Enter" && !commandModifiers;
        bool matches = submitting && Scene.Terminal.MatchesCommand(before, Task.Text);
        if (actual != null && !submitting)
        {
            if (actual.Id == "screenshot") Scene.StartCapture(text);
            return Perform(actual, key);
        }
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        bool word = held.Any(item => KeyChord.Family(item) == "Ctrl") && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super");
        bool navigation = key is "Left" or "Right" or "Home" or "End";
        if (commandModifiers && !(word && navigation))
        {
            // Cutting an empty selection in an ordinary field is a valid no-op.
            if (word && key == "X" && Scene.Active?.Focus == "url") return null;
            return Miss(key, WrongKeyMessage(key));
        }
        bool changed = Scene.Text(key, VirtualText(key, text), shift, word);
        var passed = Observe(key, matches && Scene.Terminal.LastSuccess ? Task.Text : null);
        if (passed != null) return passed;
        if (submitting && !Scene.Terminal.LastSuccess) return Miss(key, Scene.Active.Status);
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.VerifyReport) == true)
            return Miss(key, "Команда выполнена, но отчёт не совпадает с заданием. Проверьте весь сохранённый файл и папку «Загрузки».");
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.ArtifactPath != null) == true)
            return Miss(key, "Сохранённая заметка отличается от исходника. Вернитесь в редактор, вставьте скопированный текст целиком и сохраните файл.");
        if (!changed) return Miss(key, "Здесь эта клавиша не выполняет действие.");
        Feedback = "ready"; LastKey = ""; Status = ""; return null;
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
        bool eligible = LabCatalog.Available(actual, Scene);
        bool changed = eligible && Scene.Apply(actual.Action);
        if (changed && mission != null)
        {
            // Alt+D mirrors the browser behavior but does not demonstrate
            // the requested physical Ctrl+L shortcut.
            if (!replayLegacyUrl && actual.Id == "url" && actual.Keys == "Alt+D")
            {
                if (Task.Id == "url") return Miss(key, "Адрес выделен. Это задание проверяет Ctrl+L.");
                return null;
            }
            var passed = Observe(key, acceptExistingUrlSelection: !replayLegacyUrl && actual.Id == "url" && actual.Keys != "Alt+D");
            if (passed != null) return passed;
        }
        if (changed) { Status = ""; LastKey = ""; Feedback = "ready"; return null; }
        return Miss(key, "Действие сейчас недоступно. " + Task.Title + ".");
    }

    private void Watch()
    {
        if (mission == null) return;
        var eligible = Guidance == "independent" ? pending.TakeWhile(goal => goal.Phase == pending[0].Phase) : pending.Take(1);
        watching = eligible.Select(goal => (goal, LiveCourse.Evidence(goal.Task, Scene, verifyReport: goal.VerifyReport && !replayLegacyReport,
            artifactPath: goal.ArtifactPath))).ToList();
    }

    private ShortcutAttempt Observe(string key, string equivalentCommand = null, bool acceptExistingUrlSelection = false)
    {
        if (mission == null || watching == null || Scene.Switcher) return null;
        foreach (var (goal, before) in watching)
        {
            bool strictReport = goal.VerifyReport && !replayLegacyReport;
            string target = goal.VerifyReport && replayLegacyReport ? goal.LegacyTarget : goal.Target;
            bool recheckingArtifact = goal.ArtifactPath != null && goal.Task == Task && equivalentCommand != null;
            if (goal.ArtifactPath != null && !recheckingArtifact) continue;
            if (before == target && !recheckingArtifact && !(acceptExistingUrlSelection && goal.Task.Id == "url")) continue;
            string command = goal.Task == Task ? equivalentCommand : null;
            if (LiveCourse.Evidence(goal.Task, Scene, command, strictReport, goal.ArtifactPath) != target) continue;
            // Published drafts retain earned progress, but a report receipt is only issued
            // when the reconstructed workspace also meets today's verification rules.
            if (goal.VerifyReport && (strictReport || LiveCourse.Evidence(goal.Task, Scene, command, verifyReport: true) == goal.Target))
                verifiedReport = Scene.ReadReportArtifact();
            Task = goal.Task;
            pending.Remove(goal);
            return Pass(key);
        }
        return null;
    }

    private ShortcutAttempt Pass(string key)
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
        var attempt = Record(true, key);
        Solved++; taskInBlock++;
        string result = Task.Title + " — выполнено.";
        if (taskInBlock == 20) { Block++; taskInBlock = 0; }
        LastKey = "";
        Next(); Status = result; Feedback = "good";
        return attempt;
    }

    private ShortcutAttempt Miss(string key, string message)
    {
        hadError = true; Errors++; Feedback = "error"; Status = message; LastKey = key;
        return Record(false, key);
    }

    private ShortcutAttempt Record(bool correct, string key)
    {
        int milliseconds = (int)Math.Clamp(clock() - sinceTask, 1, 60000);
        var attempt = new ShortcutAttempt { Skill = "lab/" + App + "/" + Task.Id, Session = Session, Verification = "lab-scene",
            Date = DateTime.Now, Correct = correct, Milliseconds = milliseconds, MistakeKey = correct ? null : key, InputMode = "exact", Mission = MissionId, Layout = attemptLayout, Assisted = ShowGuide, Independent = Guidance == "independent", FirstTry = correct && !hadError };
        Attempts.Add(attempt); points.Add(new LabPoint(Seconds, correct, key, Task.Id, milliseconds));
        return attempt;
    }

    private void Next()
    {
        var courses = LiveCourse.Missions.Where(m => m.App == App && m.Level == Level).ToArray();
        if (CompletedRoute)
        {
            Stop();
            return; // Keep the verified artifact in the final scene snapshot.
        }
        if (mission == null || pending.Count == 0)
        {
            var last = mission?.Id;
            // Standalone projects are selected from the catalog, not injected
            // into a learner's existing continuous exercise rotation.
            var rotation = courses.Where(m => !m.FinishAfterRoute).ToArray();
            var available = rotation.Where(m => !used.Contains(m.Id)).ToArray();
            if (available.Length == 0) { used.Clear(); available = rotation.Where(m => m.Id != last || rotation.Length == 1).ToArray(); }
            var recent = past.Concat(Attempts).Where(a => a.Mission != null).Select(a => a.Mission).Reverse().Distinct().Take(2).ToHashSet();
            var fresh = available.Where(m => !recent.Contains(m.Id)).ToArray();
            if (fresh.Length > 0) available = fresh;
            initialLayout = Scene.Layout;
            initialVirtual = Scene.LayoutIsVirtual;
            if (string.IsNullOrEmpty(requestedMission))
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
                    pending = LiveCourse.Compile(mission, startingScene);
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
                        var goals = LiveCourse.Compile(item.Mission, startingScene);
                        return (item.Mission, Goals: goals, Score: item.Draw / (1 + LiveCourse.CountWeakGoals(goals, weak)));
                    }).OrderBy(item => item.Score).First();
                    mission = selected.Mission;
                    pending = selected.Goals ?? LiveCourse.Compile(mission, startingScene);
                }
                Scene = startingScene;
            }
            else
            {
                mission = courses.FirstOrDefault(m => m.Id == requestedMission) ?? throw new ArgumentException("Поручение недоступно для выбранного уровня.");
                if (!restoring) variant = random.Next(3);
                Scene = PrepareMissionScene();
                pending = LiveCourse.Compile(mission, Scene);
            }
            requestedMission = null; used.Add(mission.Id); trace.Clear();
            verifiedReport = null;
            if (!restoring)
            {
                trace.Add(new(ExactUrlEvent));
                trace.Add(new(StrictReportEvent));
                trace.Add(new(ClusterNavigationEvent));
            }
            foreach (string key in held) trace.Add(new("@held/" + key, superUsed));
            missionLength = pending.Count;
            Scene.PrepareHistory(pending.Where(goal => goal.Task.Action == "command" && goal.Task.Text.Length > 12).Select(goal => goal.Task.Text));
        }
        Task = pending[0].Task;
        sinceTask = clock(); HintVisible = hadError = false;
    }

    private LabScene PrepareMissionScene()
    {
        var scene = new LabScene(App == "workday" ? "ubuntu" : App, initialLayout)
        { LayoutKey = layoutBinding, LayoutIsVirtual = initialVirtual, ReplayLegacyNavigation = replayLegacyNavigation };
        if (dockProfile != null) scene.Dock = dockProfile;
        scene.PrepareCase(variant, App);
        return scene;
    }

    internal LabSessionRecord SessionRecord() => new() { Session = Session, App = App, Mode = Mode, Level = Level, Date = DateTime.Now,
        Seconds = Seconds, Solved = Solved, Errors = Errors, TimeLimit = TimeLimit, ActionLimit = ActionLimit, Guidance = Guidance, Completed = Completed };

    internal object Result(IEnumerable<LabSessionRecord> sessions, IEnumerable<LabSessionRecord> records = null)
    {
        var matching = sessions.Concat(records ?? []).GroupBy(item => item.Session).Select(group => group.First()).Where(item => item.App == App && item.Mode == Mode && item.Level == Level && item.TimeLimit == TimeLimit && item.ActionLimit == ActionLimit && item.Guidance == Guidance).ToArray();
        var previousBest = matching.Where(item => item.Session != Session && item.Completed)
            .OrderByDescending(item => item.Solved).ThenBy(item => item.Errors).FirstOrDefault();
        int previousRecord = previousBest?.Solved ?? 0;
        bool complete = Completed;
        bool better = complete && (previousBest == null || Solved > previousBest.Solved || Solved == previousBest.Solved && Errors < previousBest.Errors);
        return new { App, Mode, Level, Solved, Errors, Seconds, TimeLimit, ActionLimit, Guidance, completed = complete,
            accuracy = Solved + Errors == 0 ? 0 : Math.Round(100.0 * Solved / (Solved + Errors), 1),
            record = Math.Max(previousRecord, complete ? Solved : 0), previousRecord, previousRecordErrors = previousBest?.Errors ?? 0, newRecord = better,
            series = points, weak = points.Where(point => !point.Correct).GroupBy(point => point.Task).Select(group =>
            { var task = LabCatalog.Shortcuts.FirstOrDefault(item => item.Id == group.Key); return new { id = group.Key, title = task?.Title ?? "Ввод команды", keys = task?.Keys ?? "", errors = group.Count() }; }),
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
