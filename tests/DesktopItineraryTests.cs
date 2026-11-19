using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TouchTypingStudio
{
    internal static class DesktopItineraryTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "TypingDeck-" + Guid.NewGuid().ToString("N"));
            try
            {
                var months = new HashSet<string>(StringComparer.Ordinal);
                string lastTopic = null;
                for (int session = 0; session < 90; session++)
                {
                    var deck = new DesktopItinerary(root);
                    DesktopBriefing task = deck.NextBriefing();
                    if (task.Slug == lastTopic || !months.Add(task.Slug))
                        throw new Exception("A briefing was repeated during the month or at a month boundary");
                    if (task.Keyword.Length < 3 || task.Project.Length < 8 || !task.Topic.All(char.IsLetter))
                        throw new Exception("Incomplete briefing: " + task.Slug);
                    lastTopic = task.Slug;
                    if (months.Count == 30) months.Clear();
                }
                VerifyDeck(root, 6, d => d.NextConsoleScenario());
                VerifyDeck(root, 3, d => d.NextExplorerScenario());
                VerifyDeck(root, 6, d => d.NextQuickJob());
                VerifyDeck(root, 4, d => d.NextWorkdayOrder(true));
                VerifyDeck(root, 3, d => d.NextWorkdayOrder(false));
                File.WriteAllText(Path.Combine(root, "DesktopPractice", "decks", "console.txt"), "4 2 2");
                int restored = new DesktopItinerary(root).NextConsoleScenario();
                if (restored == 4 || restored < 0 || restored > 5) throw new Exception("Invalid recovered itinerary");
                var first = new DesktopItinerary(root);
                if (first.CompletedConsoleMissions != 0) throw new Exception("Drawing a mission must not grant completion");
                first.RecordConsoleMission();
                if (new DesktopItinerary(root).CompletedConsoleMissions != 1) throw new Exception("Course progress lost on restart");
                Console.WriteLine("PASS: 90 briefing draws across restarts, shuffled starts, missions and saved-deck recovery.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void VerifyDeck(string root, int count, Func<DesktopItinerary, int> draw)
        {
            int previous = -1;
            for (int cycle = 0; cycle < 12; cycle++)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    int number = draw(new DesktopItinerary(root));
                    if (number < 0 || number >= count || !seen.Add(number) || number == previous)
                        throw new Exception("Skill omitted or repeated within cycle");
                    previous = number;
                }
            }
        }
    }
}
