using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record ShortcutScore(int Count, int Correct, double Accuracy, int Median, int Deviation, int Streak,
    double? Change, int Level, int VerifiedResults, int SubstituteCount, int SubstituteCorrect, int SubstituteVerifiedResults)
{
    internal static ShortcutScore From(IEnumerable<ShortcutAttempt> source)
    {
        var all = source.ToArray();
        var samples = all.Where(sample => !sample.IsSubstitute).ToArray();
        var substitutes = all.Where(sample => sample.IsSubstitute).ToArray();
        var times = samples.Where(sample => sample.Correct).Select(sample => sample.Milliseconds).Where(time => time > 0).ToArray();
        var recent = samples.TakeLast(12).ToArray();
        int correct = samples.Count(sample => sample.Correct);
        int streak = samples.Reverse().TakeWhile(sample => sample.Correct).Count();
        int window = Math.Min(10, samples.Length / 2);
        double? change = window < 3 ? null
            : Math.Round(AccuracyOf(samples.TakeLast(window)) - AccuracyOf(samples.Take(window)), 1);
        int level = recent.Length >= 8 && AccuracyOf(recent) >= 90 ? 2 : recent.Length >= 4 && AccuracyOf(recent) >= 75 ? 1 : 0;
        double mean = times.Length == 0 ? 0 : times.Average();
        var ordered = times.Order().ToArray();
        int median = ordered.Length == 0 ? 0 : (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2;
        return new ShortcutScore(samples.Length, correct, AccuracyOf(samples), median,
            times.Length < 2 ? 0 : (int)Math.Round(Math.Sqrt(times.Average(value => Math.Pow(value - mean, 2)))),
            streak, change, level, samples.Count(sample => sample.Correct && sample.Verification != "keys"),
            substitutes.Length, substitutes.Count(sample => sample.Correct),
            substitutes.Count(sample => sample.Correct && sample.Verification != "keys"));
    }

    internal static double AccuracyOf(IEnumerable<ShortcutAttempt> samples)
    {
        var values = samples as ShortcutAttempt[] ?? samples.ToArray();
        return values.Length == 0 ? 0 : Math.Round(100.0 * values.Count(sample => sample.Correct) / values.Length, 1);
    }
}

internal sealed class SkillPicker(Random random, Skill[] skills, int initialTier, bool adaptive, bool studyOnly)
{
    private readonly List<Skill> route = [];
    private readonly Queue<Skill> repeat = new();
    private Skill previous;
    private readonly HashSet<string> recentMisses = [];
    private int sinceRepeat;

    internal int Tier { get; private set; } = initialTier;

    internal void Update(Skill skill, bool hadErrors, IReadOnlyList<ShortcutAttempt> attempts)
    {
        if (hadErrors && skills.Length > 1 && recentMisses.Add(skill.Id)) repeat.Enqueue(skill);
        if (!adaptive) return;
        var exact = attempts.Where(attempt => !attempt.IsSubstitute).ToArray();
        var recent = exact.TakeLast(8).ToArray();
        if (recent.Length == 8 && ShortcutScore.AccuracyOf(recent) >= 87.5)
            Tier = Math.Max(Tier, Math.Min(2, 1 + exact.Length / 16));
    }

    internal Skill Next(IEnumerable<ShortcutAttempt> past)
    {
        if (skills.Length == 1) return skills[0];
        if (sinceRepeat >= 2 && repeat.TryPeek(out var weak) && weak.Id != previous?.Id)
        {
            repeat.Dequeue();
            recentMisses.Remove(weak.Id);
            sinceRepeat = 0;
            return previous = weak;
        }

        var history = past.Where(attempt => !attempt.IsSubstitute).GroupBy(attempt => attempt.Skill)
            .ToDictionary(group => group.Key, group => group.TakeLast(8).ToArray());
        var eligible = skills.Where(skill => skill.Tier <= Tier &&
            (!studyOnly || history.GetValueOrDefault(skill.Id)?.Any(attempt => attempt.Correct) == true)).ToArray();
        if (eligible.Length == 0) eligible = skills.Where(skill => skill.Tier <= Tier).ToArray();
        if (eligible.Length == 0) eligible = skills.Where(skill => skill.Tier == skills.Min(candidate => candidate.Tier)).ToArray();

        if (route.Count == 0)
            route.AddRange(eligible.OrderBy(skill =>
            {
                var old = history.GetValueOrDefault(skill.Id) ?? [];
                // Recent misses come up sooner, but each eligible skill gets a turn.
                return random.NextDouble() - .8 * (1 - ShortcutScore.AccuracyOf(old) / 100) * (old.Length > 0 ? 1 : .35);
            }));
        int index = route.Count > 1 && route[0].Id == previous?.Id ? 1 : 0;
        previous = route[index];
        route.RemoveAt(index);
        sinceRepeat++;
        return previous;
    }
}
