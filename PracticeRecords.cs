using System;

namespace TouchTypingStudio
{
    public class PracticeRecord
    {
        public string Id;
        public double Speed, Accuracy;
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
