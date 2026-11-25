using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// A card is a measurement of one completed attempt, not the older independent
// speed and accuracy maxima (which could also come from manually stopped practice).
internal static class TypingModeRecords
{
    private sealed record Card(double Accuracy, double? Seconds, string Band, bool Legacy);

    // Only the course itself can say whether its intended goal was reached.
    // Stamp unfinished runs, too: they may remain in history but cannot be records.
    internal static void Record(StudioProfile profile, TypingResult item, int level, int cycles,
        int seconds, int round = -1, string layout = null, bool? completed = null, string material = null)
    {
        bool eligible = item.Completed && (completed ?? true);
        item.TypingConditions = new TypingRecordConditions
        {
            Level = level, Cycles = cycles, TimerSeconds = seconds, Round = round,
            Layout = CanonicalLayout(layout), Material = material, Eligible = eligible
        };
        if (eligible && Valid(item.Count, item.Errors) && IsMainPractice(item))
            Keep(profile.TypingModeBests, Candidate(item));
    }

    // Do not execute this migration just for a read: the catalog also folds any
    // remaining historical attempts in memory. Before a write preserve the winners
    // so they survive the legacy 500-row history rotation.
    internal static void PreserveHistory(StudioProfile profile)
    {
        foreach (var result in profile.Results)
        {
            if (!Valid(result.Count, result.Errors)) continue;
            if (result.TypingConditions != null)
            {
                if (result.Completed && result.TypingConditions.Eligible && IsMainPractice(result))
                    Keep(profile.TypingModeBests, Candidate(result));
            }
            else if (result.Completed && LegacyMode(result) is string mode)
                Keep(profile.TypingModeBests, Candidate(result, mode));
        }
    }

    private static bool IsMainPractice(TypingResult item) =>
        item.Activity is "TypingPractice" or "LinuxTyping" && !string.IsNullOrEmpty(item.Task);
    private static string LegacyMode(TypingResult result)
    {
        // These records identify a completed mode/lesson but, before this version,
        // they do not record level/cycle count/timer. Never borrow another field's
        // filename or infer a course from its localized title.
        if (IsMainPractice(result)) return result.Task;
        return result.Activity == "Campaign" && result.Task != null &&
            LegacyCampaigns.TryGetValue(result.Task, out string mode) ? mode : null;
    }

    private static readonly Dictionary<string, string> LegacyCampaigns =
        Lessons.All.SelectMany(l => l.Rounds.Select(round => (hash: ExerciseBank.Task(l.Id, round), mode: "lesson:" + l.Id)))
            .GroupBy(p => p.hash, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.mode).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().mode, StringComparer.Ordinal);

    private static TypingModeBest Candidate(TypingResult item, string legacyMode = null) => new()
    {
        Mode = legacyMode ?? item.Task, Count = item.Count, Errors = item.Errors,
        ActiveSeconds = KnownTime(item.Seconds) ? item.Seconds : 0,
        Conditions = item.TypingConditions
    };

    // A renamed custom set remains comparable; an edited or replaced set does not.
    internal static string Material(StudioProfile profile, string mode)
    {
        if (!mode.StartsWith("set:", StringComparison.Ordinal)) return null;
        var set = profile.PracticeSets.FirstOrDefault(s => s.Id == mode[4..]);
        return set == null ? null : ExerciseBank.Task(set.Kind.ToString(), set.Content ?? "");
    }

    private static string CanonicalLayout(string layout) => layout is "RU" or "EN" ? layout : null;
    private static bool KnownTime(double seconds) => double.IsFinite(seconds) && seconds > 0;
    private static bool Valid(int count, int errors) => count > 0 && errors >= 0;

    private static bool Same(TypingRecordConditions a, TypingRecordConditions b) =>
        a == null || b == null ? a == b :
        a.Level == b.Level && a.Cycles == b.Cycles && a.TimerSeconds == b.TimerSeconds &&
        a.Round == b.Round && a.Layout == b.Layout && a.Material == b.Material;

    private static void Keep(List<TypingModeBest> records, TypingModeBest candidate)
    {
        int index = records.FindIndex(old => old.Mode == candidate.Mode && Same(old.Conditions, candidate.Conditions));
        if (index < 0) records.Add(candidate);
        else if (!Valid(records[index].Count, records[index].Errors) || Better(candidate, records[index]))
            records[index] = candidate;
    }

    private static bool Better(TypingModeBest a, TypingModeBest b)
    {
        // Compare exact rational scores. Since counts/errors fit signed 32 bits,
        // count * ((long)otherCount + otherErrors) fits signed 64 bits.
        long left = a.Count * ((long)b.Count + b.Errors), right = b.Count * ((long)a.Count + a.Errors);
        if (left != right) return left > right;
        return KnownTime(a.ActiveSeconds) && (!KnownTime(b.ActiveSeconds) || a.ActiveSeconds < b.ActiveSeconds);
    }

    internal static object ForCards(StudioProfile profile, int level, int cycles, int seconds, int round, string layout)
    {
        var best = new Dictionary<string, TypingModeBest>(StringComparer.Ordinal);
        var legacy = new Dictionary<string, TypingModeBest>(StringComparer.Ordinal);
        string requestedLayout = CanonicalLayout(layout);
        void Consider(TypingModeBest record)
        {
            if (record == null || string.IsNullOrEmpty(record.Mode) || !Valid(record.Count, record.Errors)) return;
            var c = record.Conditions;
            var target = best;
            if (c == null)
            {
                // Mode-only archives are a labelled fallback, not comparable to
                // a known non-default configuration, specific round or custom set revision.
                if (level != 0 || cycles != 1 || seconds != 0 || round >= 0 ||
                    record.Mode.StartsWith("set:", StringComparison.Ordinal)) return;
                target = legacy;
            }
            else if (!c.Eligible || c.Level != level || c.Cycles != cycles || c.TimerSeconds != seconds ||
                     (round >= 0 && c.Round >= 0 && round != c.Round) ||
                     (c.Layout != null && requestedLayout != c.Layout) ||
                     c.Material != Material(profile, record.Mode))
                return;
            if (!target.TryGetValue(record.Mode, out var other) || Better(record, other))
                target[record.Mode] = record;
        }
        foreach (var record in profile.TypingModeBests) Consider(record);
        foreach (var result in profile.Results)
        {
            if (!Valid(result.Count, result.Errors)) continue;
            if (result.TypingConditions != null)
            {
                if (result.Completed && IsMainPractice(result)) Consider(Candidate(result));
            }
            else if (result.Completed && LegacyMode(result) is string mode) Consider(Candidate(result, mode));
        }
        foreach (var pair in legacy) best.TryAdd(pair.Key, pair.Value);
        return best.ToDictionary(pair => pair.Key, pair =>
        {
            var b = pair.Value;
            long total = (long)b.Count + b.Errors, score = b.Count * 1000L;
            return new Card(100.0 * b.Count / total, KnownTime(b.ActiveSeconds) ? b.ActiveSeconds : null,
                score < 900 * total ? "red" : score < 975 * total ? "orange" : "green", b.Conditions == null);
        }, StringComparer.Ordinal);
    }
}
