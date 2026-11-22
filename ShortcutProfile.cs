using System;

namespace TouchTypingStudio
{
    public class SavedShortcut
    {
        public string Id, Title, Group, Gesture;
        public bool Hidden;
        public string Fingers;
    }

    public class LabSessionRecord
    {
        public string Session, App, Mode;
        public int Level, Seconds, Solved, Errors;
        public DateTime Date;
        public bool Completed;
    }

    public class ShortcutAttempt
    {
        public string Skill, Session, Verification, MistakeKey;
        public string InputMode;
        public DateTime Date;
        public bool Correct;
        public int Milliseconds;

        internal bool IsSubstitute { get { return InputMode == "substitute"; } }
    }
}
