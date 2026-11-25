using System;

namespace TouchTypingStudio
{
    public class PracticeRecord
    {
        public string Id;
        public double Speed, Accuracy;
    }

    // XML types understood by both the shared engine and the Windows shell.
    // An old TypingResult has no TypingConditions: never silently give it a modern cohort.
    public class TypingRecordConditions
    {
        public int Level, Cycles, TimerSeconds, Round = -1;
        public string Layout, Material;
        public bool Eligible;
    }

    public class TypingModeBest
    {
        public string Mode;
        public TypingRecordConditions Conditions;
        public int Count, Errors;
        public double ActiveSeconds;
    }

    internal static class PracticeRecords
    {
        public const int MinimumCharacters = 180;

        public static bool CanRecordTyping(ContinuousTypingSnapshot result)
        {
            return result.Count >= MinimumCharacters;
        }

        public static void Record(StudioProfile profile, string id, double speed, double accuracy)
        {
            var item = profile.PracticeRecords.Find(record => record.Id == id);
            if (item == null)
            {
                item = new PracticeRecord { Id = id };
                profile.PracticeRecords.Add(item);
            }
            item.Speed = Math.Max(item.Speed, speed);
            item.Accuracy = Math.Max(item.Accuracy, accuracy);
        }
    }
}
