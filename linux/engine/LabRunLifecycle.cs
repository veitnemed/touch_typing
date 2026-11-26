using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabRun
{
    private void EndLegacyChromeHistory(LiveEvent marker)
    {
        Scene.FinishLegacyChromeHistory();
        trace.Add(marker);
    }

    private void EndLegacyTerminalSearch(LiveEvent marker)
    {
        Scene.ReplayLegacyTerminalSearch = false;
        trace.Add(marker);
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
        MissionRevision = Scene.GuideNoteActive ? LabScene.GuideNoteRevision : Scene.SafeReorder ? LabScene.SafeReorderRevision : null,
        Trace = JsonSerializer.Serialize(trace),
        Progress = new() { Session = Session, Mode = Mode, TimeLimit = TimeLimit, ActionLimit = ActionLimit, Solved = Solved,
            Errors = Errors, SimulatedWindowSwitches = SimulatedWindowSwitches, ActiveMilliseconds = ActiveMilliseconds,
            Series = JsonSerializer.Serialize(points),
            LimitDecisionPending = PendingLimitChoice, LimitReached = LimitReached,
            FinishingMission = FinishingMission } };

    internal void Pause()
    {
        nextLogArrival = null;
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

    internal bool SimulateWindowSwitch()
    {
        if (!CanSimulateWindowSwitch || held.Count != 0) return false;
        var goal = pending[0];
        var trial = Scene.Clone();
        if (!trial.Apply(goal.Task.Action) || !trial.Switcher) return false;
        trial.CommitSwitch();
        // Demonstrate an actual change in the same desktop, not skip the step
        // from a stale or incorrect window.
        if (LiveCourse.Evidence(goal.Task, trial) != goal.Target ||
            LiveCourse.Evidence(goal.Task, Scene) == goal.Target) return false;
        string noCredit = ShowGuide ? goal.Task.Keys + " не зачтено." : "Сочетание не проверялось.";
        Scene = trial;
        watching = null;
        switchEvidenceId = switchReleaseKey = null;
        trace.Add(new(SimulatedWindowSwitchEvent));
        pending.RemoveAt(0);
        SimulatedWindowSwitches++;
        // No Pass/Record: this is a demonstration of the resulting window,
        // not a physical shortcut or an attempt against the action budget.
        Next();
        LastKey = "";
        Feedback = "simulation";
        Status = "Учебное окно переключено. " + noCredit;
        return true;
    }

    internal bool BrowserHistoryPointer(string action, string path)
    {
        if (Finished || AwaitingLimitChoice || Paused || !Scene.BrowserHistoryPointer(action, path)) return false;
        trace.Add(new(ChromeHistoryPointerEvent, Text: action + ":" + path));
        return true;
    }

    internal bool BrowserTabPointer(string action, string index)
    {
        if (Finished || AwaitingLimitChoice || Paused || !Scene.BrowserTabPointer(action, index)) return false;
        trace.Add(new(ChromeTabPointerEvent, Text: action + ":" + index));
        return true;
    }

    internal bool ChromeSurfacePointer(string owner, string action, int slot)
    {
        if (Finished || AwaitingLimitChoice || Paused || !Scene.ChromeSurfacePointer(owner, action, slot)) return false;
        trace.Add(new(ChromeSurfaceEvent, Text: owner + ":" + action + ":" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return true;
    }

    internal void TickTerminal()
    {
        if (Finished || AwaitingLimitChoice || Paused || !Scene.Terminal.FollowingLog)
        { nextLogArrival = null; return; }
        double now = clock();
        nextLogArrival ??= now + Scene.Terminal.LogInterval;
        if (now < nextLogArrival) return;
        AppendTerminalLog();
        nextLogArrival = now + Scene.Terminal.LogInterval;
    }

    private void AppendTerminalLog()
    {
        if (Scene.Terminal.AppendLogFeedLine()) trace.Add(new(TerminalLogArrivalEvent));
    }

    internal void TickNotebook()
    {
        if (!Finished && !AwaitingLimitChoice && !Paused && Scene.TickNotebook())
            trace.Add(new(NotebookKernelStepEvent));
    }
}
