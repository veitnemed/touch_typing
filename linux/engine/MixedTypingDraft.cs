using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class MixedTypingRun
{
    private sealed record SavedMiss(TypingAtom Atom, int Count);
    private sealed record Stored(string Format, string Id, string Mode, string Title, string Layout, int Level,
        int TimeLimit, int Cycle, int Cycles, int Done, int? Total, int Offset, string Carry,
        int Errors, double Seconds, int[] Lengths, TypingAtom[] Units, SavedMiss[] Mistakes);
    internal string Draft() => JsonSerializer.Serialize(new Stored("mixed-v1", AttemptId, Mode, Title, StartLayout,
        Level, TimeLimit, cycleIndex + 1, Cycles, Position, PlannedUnits, absoluteOffset, carry,
        Errors, Seconds, IsInfinite ? [plan.Count] : lengths, plan.ToArray(),
        mistakes.Select(kv => new SavedMiss(kv.Key, kv.Value)).ToArray()), Program.Json);

    private static Stored ReadDraft(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 900_000) return null;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String || format.GetString() != "mixed-v1") return null;
            var d = root.Deserialize<Stored>(Program.Json);
            if (d == null || d.Mode == null || !Modes.ContainsKey(d.Mode) || !Guid.TryParseExact(d.Id, "N", out _) ||
                d.Layout is not ("RU" or "EN") || d.Level is < 0 or > 2 || d.TimeLimit is < 0 or > 3600 ||
                d.Cycles is < 0 or > 5 || d.Lengths?.Length != Math.Max(1, d.Cycles) ||
                d.Lengths.Any(n => n is < 1 or > 550) || d.Units?.Length != d.Lengths.Sum() ||
                d.Units.Distinct().Any(a => a == null || !a.IsValid()) || d.Offset < 0 || d.Done < d.Offset ||
                d.Done - d.Offset >= d.Units.Length || d.Cycle < 1 || d.Carry?.Length > 300 ||
                d.Errors < 0 || !double.IsFinite(d.Seconds) || d.Seconds < 0 ||
                d.TimeLimit > 0 && d.Seconds >= d.TimeLimit || d.Mistakes == null ||
                d.Mistakes.Any(m => m?.Atom == null || !m.Atom.IsValid() || m.Count <= 0) ||
                d.Mistakes.Select(m => m.Atom).Distinct().Count() != d.Mistakes.Length ||
                d.Mistakes.Sum(m => (long)m.Count) != d.Errors ||
                (d.Cycles == 0 ? d.Total != null : d.Offset != 0 || d.Cycle > d.Cycles || d.Total != d.Units.Length)) return null;
            return d;
        }
        catch (JsonException) { return null; }
    }

    internal static bool NeedsSystemCapture(string json) => ReadDraft(json)?.Mode == SystemMode;

    internal static string DraftId(string json) => ReadDraft(json)?.Id;

    internal static object DraftSummary(string json)
    {
        var d = ReadDraft(json);
        return d == null ? null : new { d.Id, d.Mode, title = Modes[d.Mode], d.Level, d.TimeLimit, d.Cycle,
            d.Cycles, d.Done, d.Total, layout = d.Layout, progressDone = d.Done, progressTotal = d.Total };
    }

    internal static MixedTypingRun Restore(string json)
    {
        var d = ReadDraft(json);
        if (d == null) return null;
        var cycles = new List<IReadOnlyList<TypingAtom>>();
        int offset = 0;
        foreach (int len in d.Lengths) { cycles.Add(d.Units[offset..(offset + len)]); offset += len; }
        var run = new MixedTypingRun(d.Mode, Modes[d.Mode], d.Layout, d.Level, d.TimeLimit,
            d.Cycles == 0, cycles, Random.Shared, d.Done - d.Offset, d.Errors, d.Seconds,
            d.Mistakes.ToDictionary(m => m.Atom, m => m.Count), d.Id);
        if (run.IsInfinite)
        {
            run.cycleIndex = d.Cycle - 1; run.absoluteOffset = d.Offset; run.Position = d.Done;
            run.carry = d.Carry ?? "";
        }
        return run;
    }
}
