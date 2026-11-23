using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LiveStartRecommendation(string App, int? Level, string Reason);

internal static class LiveStartingLevel
{

    private static bool FirstTrySuccess(ShortcutAttempt attempt) => attempt.Correct && attempt.FirstTry;

    internal static IEnumerable<LiveStartRecommendation> Recommend(IEnumerable<ShortcutAttempt> history, string layout)
    {
        var missions = LiveCourse.Missions.ToDictionary(mission => mission.Id);
        var observed = (history ?? [])
            .Where(attempt => (layout is "RU" or "EN") && attempt?.Layout == layout && attempt.Independent && !attempt.Assisted &&
                attempt.Verification == "lab-scene" && !string.IsNullOrEmpty(attempt.Session) &&
                attempt.Mission != null && missions.ContainsKey(attempt.Mission))
            .Select(attempt => (attempt, mission: missions[attempt.Mission]))
            .Where(item => item.attempt.Skill?.StartsWith("lab/" + item.mission.App + "/", System.StringComparison.Ordinal) == true &&
                item.attempt.Skill.Length > item.mission.App.Length + 5)
            .ToLookup(item => (item.mission.App, item.mission.Level), item => item.attempt);

        foreach (string app in LabCatalog.Apps)
        {
            var levels = LiveCourse.Missions.Where(mission => mission.App == app)
                .Select(mission => mission.Level).Distinct().OrderByDescending(level => level).ToArray();
            LiveStartRecommendation recommendation = null;
            foreach (int tier in levels)
            {
                // Keep several whole missions in scope (each usually has 20+ steps).
                // A separate recent floor prevents a strong old run masking new misses.
                var samples = observed[(app, tier)].TakeLast(120).ToArray();
                var passed = samples.Where(FirstTrySuccess).ToArray();
                int recentSuccesses = samples.TakeLast(10).Count(FirstTrySuccess);
                int missionCount = passed.Select(attempt => attempt.Mission).Distinct().Count();
                int sessionCount = passed.Select(attempt => attempt.Session).Distinct().Count();
                if (samples.Length < 10 || recentSuccesses < 9 || passed.Length * 10 < samples.Length * 9 ||
                    missionCount < 3 || sessionCount < 2 ||
                    passed.Select(attempt => attempt.Skill).Distinct().Count() < 4) continue;

                int next = levels.Where(level => level > tier).DefaultIfEmpty(tier).Min();
                string evidence = $"На уровне {tier + 1} ({layout}) — {passed.Length} из {samples.Length} действий с первой попытки без подсказок, в {missionCount} разных миссиях. ";
                recommendation = new(app, next, evidence + (next == tier
                    ? "Можно продолжить на этом уровне."
                    : "Можно попробовать следующий уровень; он ещё не подтверждён."));
                break;
            }

            yield return recommendation ?? new(app, null,
                $"Пока мало самостоятельных результатов в разных миссиях для раскладки {layout}. Можно выбрать любой уровень.");
        }
    }
}
