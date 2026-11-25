using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabRun
{
    private void Next()
    {
        var courses = (neutralProject ? NeutralProject.MissionCatalog : LiveCourse.Missions)
            .Where(m => m.App == App && (m.Level == Level || App == "terminal" && (IsCourse || EnvironmentProfile == "windows") &&
                LiveCourse.PracticalTerminalMission(m.Id)) &&
                (LabScene.MissionEnvironment(m) is null || LabScene.MissionEnvironment(m) == EnvironmentProfile)).ToArray();
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
            var rotation = App == "terminal" && (IsCourse || EnvironmentProfile == "windows")
                ? courses.Where(m => LiveCourse.PracticalTerminalMission(m.Id)).ToArray()
                : IsCourse ? courses : courses.Where(m => essentials
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
                mission = LiveRoutePolicy.Pick(available, mission, EnvironmentProfile, history, target);
                variant = random.Next(3);
                if (LiveCourse.PracticalTerminalMission(mission.Id))
                {
                    var finalSkill = mission.Id == LiveCourse.PracticalPowerShellId ?
                        "lab/terminal/command/powershell-incident" : "lab/terminal/command/filter-incident";
                    int previousCase = history.Where(attempt => attempt.Correct && attempt.TerminalCase > 0 &&
                        attempt.Mission == mission.Id && attempt.Skill == finalSkill)
                        .OrderByDescending(attempt => attempt.Date).FirstOrDefault()?.TerminalCase ?? 0;
                    if (variant == previousCase - 1) variant = (variant + 1) % 3;
                }
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
                trace.Add(new(TerminalHistoryEvent));
                trace.Add(new(ChromeHistoryEvent));
                trace.Add(new(ChromeTabSearchEvent));
                trace.Add(new(StrictReportEvent));
                trace.Add(new(ClusterNavigationEvent));
                trace.Add(new(LineEditingEvent));
                trace.Add(new(PythonEditingEvent));
                trace.Add(new(NotebookActionsEvent));
                trace.Add(new(StrictTriggersEvent));
                trace.Add(new(DesktopFileSearchEvent));
                if (Scene.NotebookCourse) trace.Add(new(NotebookWordEraseEvent));
                if (Scene.CsvNotebook) trace.Add(new(CsvExplorerScopeEvent));
            }
            foreach (string key in held) trace.Add(new("@held/" + key, superUsed));
            missionLength = pending.Count;
            if (!Scene.PracticalTerminal && !Scene.DiskSpaceLesson)
                Scene.PrepareHistory(pending.Where(goal => goal.Task.Action == "command" && goal.Task.Text.Length > 12).Select(goal => goal.Task.Text));
        }
        Task = pending[0].Task;
        sinceTask = clock(); HintVisible = hadError = false;
        if (!restoring) UpdateCoaching();
    }

    private string CoachContext() =>
        string.Join("|", Scene.Active?.App ?? "", Scene.Active?.Focus ?? "", Scene.Active?.Url ?? "", Scene.Terminal.Cwd) +
        (Scene.PracticalTerminal ? "|" + EnvironmentProfile + "-journal-" + variant : "");

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
        return new { App, Mode, Level, Solved, Errors, Seconds, TimeLimit, ActionLimit, Guidance,
            SimulatedWindowSwitches, completed = complete,
            sourceReview = Scene.TabReviewReceipt(), diskCapacityConclusion = Scene.DiskCapacityConclusion,
            accuracy = Solved + Errors == 0 ? 0 : Math.Round(100.0 * Solved / (Solved + Errors), 1),
            record = Math.Max(previousRecord, complete ? Solved : 0), previousRecord, previousRecordErrors = previousBest?.Errors ?? 0, newRecord = better,
            series = points, weak = points.Where(point => !point.Correct).GroupBy(point => point.Task).Select(group =>
            { var task = LabCatalog.AllTasks.Concat(LiveCourse.NotebookActions).FirstOrDefault(item => item.Id == group.Key); return new { id = group.Key, title = task?.Title ?? (group.Key == "notebook-type" ? "Редактирование ячейки" : "Ввод команды"), keys = task?.Keys ?? "", errors = group.Count() }; }),
            history = matching.OrderByDescending(item => item.Date).Take(15).Select(item => new
            { item.Session, item.App, item.Mode, item.Level, item.Date, item.Seconds, item.Solved, item.Errors, item.Completed }) };
    }
}
