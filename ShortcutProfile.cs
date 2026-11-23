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
        public string App, Mission, Layout, Guidance, Trace, LayoutKey;
        public string[] Dock;
        public int Level, Variant;
        public bool VirtualLayout;
        // Absent in older drafts, including explicit unlimited sessions.
        public LiveDraftProgress Progress;
    }

    public class LiveDraftProgress
    {
        public string Session, Mode, Series, Revision;
        public int TimeLimit, ActionLimit, Solved, Errors;
        public double ActiveMilliseconds;
        public bool LimitDecisionPending, FinishingMission;
    }

    public class LabSessionRecord
    {
        public string Session, App, Mode;
        public int Level, Seconds, Solved, Errors;
        public int TimeLimit = 180, ActionLimit;
        public string Guidance = "guided";
        public DateTime Date;
        public bool Completed;
    }

    public class ShortcutAttempt
    {
        public string Skill, Session, Verification, MistakeKey;
        public string InputMode, Mission;
        // Simulated input layout just before the action; absent on older records.
        public string Layout;
        public bool Assisted, Independent, FirstTry;
        public DateTime Date;
        public bool Correct;
        public int Milliseconds;

        internal bool IsSubstitute { get { return InputMode == "substitute"; } }
    }
}
