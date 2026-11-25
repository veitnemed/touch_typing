using System;

namespace TouchTypingStudio
{
    public class SavedShortcut
    {
        public string Id, Title, Group, Gesture;
        public bool Hidden;
        public string Fingers;
    }

    public class LiveDraft
    {
        public string App, Mission, Layout, Guidance, Trace, LayoutKey, ProjectFixture, DesktopEnvironment;
        public string[] Dock, DesktopProfile;
        public int Level, Variant;
        public bool VirtualLayout;
        // Older drafts continue replaying their original virtual prerequisite goals.
        public bool NativeEnglishInput;
        // Opt-in card route. Old profiles continue their selected individual mission.
        public bool EditorCourse;
        public string RouteKind;
        // Only version a scenario when its already-recorded keystrokes would change meaning.
        public string MissionRevision;
        public bool AdaptiveGuide;
        public string CoachStage, CoachReason;
        // Absent in older drafts, including explicit unlimited sessions.
        public LiveDraftProgress Progress;
    }

    public class LiveDraftProgress
    {
        public string Session, Mode, Series, Revision, LimitReached;
        public int TimeLimit, ActionLimit, Solved, Errors, SimulatedWindowSwitches;
        public double ActiveMilliseconds;
        public bool LimitDecisionPending, FinishingMission;
    }

    public class LabSessionRecord
    {
        public string Session, App, Mode, DesktopEnvironment;
        public int Level, Seconds, Solved, Errors;
        public int TimeLimit = 180, ActionLimit;
        public string Guidance = "guided";
        public DateTime Date;
        public bool Completed;
    }

    public class ShortcutAttempt
    {
        public string Skill, Session, Verification, MistakeKey;
        public string InputMode, Mission, CoachStage, CoachContext;
        // Filled only for finished practical journal cases; old history remains valid.
        public int TerminalCase;
        public bool ShouldSerializeTerminalCase() { return TerminalCase > 0; }
        // Simulated input layout just before the action; absent on older records.
        public string Layout;
        public bool Assisted, Independent, FirstTry;
        public DateTime Date;
        public bool Correct;
        public int Milliseconds;

        internal bool IsSubstitute { get { return InputMode == "substitute"; } }
    }
}
