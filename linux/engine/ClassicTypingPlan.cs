using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

// An ordinary exercise has a frozen list of short, readable fragments. A cycle is a
// budgeting unit, not a second exercise; pausing or moving to the next fragment
// never allocates new work or resets the denominator.
internal sealed class ClassicTypingPlan
{
    internal const int MinUnits = 350, MaxUnits = 550, MaxCycles = 5;
    private readonly string[][] cycles;
    internal int[] Lengths { get; }
    internal int Total { get; }
    private int cycle, fragment, previousCount, previousErrors;
    private double previousSeconds;
    private readonly List<TypingAttempt> completed = [];
    private readonly Dictionary<char, long> mistakes = [];
    internal IReadOnlyDictionary<char, long> MistakeCounts => mistakes;
    internal int Count => previousCount + Attempt.Position;
    internal int Cycle => cycle + 1;
    internal string Prefix { get; private set; } = "";
    internal TypingAttempt Attempt { get; private set; }
    internal bool Finished => cycle == cycles.Length - 1 && fragment == cycles[cycle].Length - 1 && Attempt.Finished;

    internal ClassicTypingPlan(string[][] material)
    {
        if (material == null || material.Length is < 1 or > MaxCycles) throw InvalidDraft();
        foreach (var segment in material)
        {
            if (segment == null || segment.Length is < 1 or > MaxUnits ||
                segment.Any(s => string.IsNullOrEmpty(s) || s.Length > MaxUnits) ||
                segment.Sum(s => s.Length) is < MinUnits or > MaxUnits)
                throw InvalidDraft();
        }
        cycles = material;
        Lengths = material.Select(b => b.Sum(s => s.Length)).ToArray();
        Total = Lengths.Sum();
        Attempt = new TypingAttempt(cycles[0][0], null);
    }

    internal static ClassicTypingPlan Build(int count, Random random, Func<string> next)
    {
        // Select complete material where possible. Do not drop the unconsumed tail
        // of a long custom passage when the cycle boundary falls inside it.
        var material = new string[Math.Clamp(count, 1, MaxCycles)][];
        string carry = "";
        for (int c = 0; c < material.Length; c++)
        {
            int budget = random.Next(MinUnits + 1, MaxUnits + 1);
            var chunks = new List<string>();
            int size = 0;
            while (size < budget)
            {
                string chunk = carry.Length > 0 ? carry : next();
                carry = "";
                if (string.IsNullOrEmpty(chunk)) throw new ArgumentException("В упражнении нет текста.");
                if (size >= MinUnits + 1 && size + chunk.Length > budget)
                {
                    carry = chunk;
                    break;
                }
                if (size + chunk.Length > MaxUnits)
                {
                    int low = Math.Max(1, MinUnits + 1 - size), high = MaxUnits - size;
                    int cut = Math.Clamp(budget - size, low, high);
                    // Prefer a complete word or line to a split token; a long
                    // identifier without spaces is deliberately split at the cap.
                    int best = -1;
                    for (int i = low; i <= high; i++)
                        if (char.IsWhiteSpace(chunk[i - 1]) && (best < 0 || Math.Abs(i - cut) < Math.Abs(best - cut)))
                            best = i;
                    if (best >= 0) cut = best;
                    carry = chunk[cut..];
                    chunk = chunk[..cut];
                }
                chunks.Add(chunk); size += chunk.Length;
            }
            // StandardBlock inserts a separator between fragments. Do not make the
            // user type an invisible final space/newline just to finish a cycle.
            string last = chunks[^1];
            if (size > MinUnits && char.IsWhiteSpace(last[^1]))
            {
                if (last.Length == 1) chunks.RemoveAt(chunks.Count - 1);
                else chunks[^1] = last[..^1];
            }
            material[c] = chunks.ToArray();
        }
        return new ClassicTypingPlan(material);
    }

    internal bool Enter(char input, Layout preferred)
    {
        if (Finished || !Attempt.Running) return false;
        var expected = Attempt.Expected;
        var wanted = CodeText.Language(expected);
        var hint = TypingKeyboard.Hint(expected, wanted == Layout.Neutral ? preferred : wanted);
        int errors = Attempt.Errors;
        bool accepted = Attempt.Enter(input, TrainingProgress.Locale(hint?.Layout ?? preferred));
        if (Attempt.Errors > errors) mistakes[expected] = mistakes.GetValueOrDefault(expected) + 1;
        if (Attempt.Finished && !Finished)
        {
            Advance();
            Attempt.Start();
        }
        return accepted;
    }

    private void Advance()
    {
        completed.Add(Attempt);
        previousCount += Attempt.Position;
        previousErrors += Attempt.Errors;
        previousSeconds += Attempt.Seconds;
        Prefix += Attempt.Written;
        if (Prefix.Length > 160) Prefix = Prefix[^160..];
        if (++fragment == cycles[cycle].Length) { cycle++; fragment = 0; Prefix = ""; }
        Attempt = new TypingAttempt(cycles[cycle][fragment], null);
    }

    internal ContinuousTypingSnapshot Snapshot()
    {
        long errors = (long)previousErrors + Attempt.Errors;
        return new ContinuousTypingSnapshot(Count, errors, previousSeconds + Attempt.Seconds,
            Count + errors == 0 ? 0 : 100.0 * Count / (Count + errors),
            mistakes.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(5).ToArray());
    }

    internal void SaveMotor(StudioProfile profile, DateTime now, string tag)
    {
        foreach (var part in completed.Append(Attempt))
            if (part.Position > 0 || part.Errors > 0)
                TrainingProgress.SaveMotor(profile, part, false, now, tag);
    }

    internal ClassicPlanCheckpoint Checkpoint() => new(cycles,
        completed.Append(Attempt).Select(t => t.Draft(null, 0)).ToArray());

    internal static ClassicTypingPlan Restore(ClassicPlanCheckpoint data)
    {
        if (data == null) throw InvalidDraft();
        var plan = new ClassicTypingPlan(data.Cycles);
        if (data.Attempts == null || data.Attempts.Length == 0 || data.Attempts.Length > data.Cycles.Sum(c => c.Length)) throw InvalidDraft();
        foreach (var (d, i) in data.Attempts.Select((d, i) => (d, i)))
        {
            string expected = plan.Attempt.Target;
            bool complete = i < data.Attempts.Length - 1;
            // No arbitrary host content or commands are executed by recovery. Reject
            // corrupt coordinates instead of fabricating a different attempt.
            if (d == null || d.Target != expected || d.Errors < 0 || d.Seconds < 0 || !double.IsFinite(d.Seconds) ||
                d.Position < 0 || (complete ? d.Position != expected.Length : d.Position >= expected.Length) ||
                d.Mistakes == null || d.Strokes == null ||
                d.Mistakes.Any(m => m == null || m.Count <= 0 || m.Position < 0 || m.Position >= expected.Length) ||
                d.Mistakes.Sum(m => (long)m.Count) != d.Errors ||
                d.Strokes.Any(s => s == null || s.Position < 0 || s.Position >= expected.Length))
                throw InvalidDraft();
            plan.Attempt = new TypingAttempt(expected, d, allowCompletedDraft: complete);
            foreach (var m in d.Mistakes)
                plan.mistakes[expected[m.Position]] = plan.mistakes.GetValueOrDefault(expected[m.Position]) + m.Count;
            if (complete) plan.Advance();
        }
        return plan;
    }

    internal static ArgumentException InvalidDraft() => new("Не удалось прочитать сохранённое занятие. Можно начать новое.");
}

internal sealed record ClassicPlanCheckpoint(string[][] Cycles, TypingDraft[] Attempts);
internal sealed record ClassicRunCheckpoint(string Format, string Id, string Mode, string Title, string Source,
    int Level, int Round, int TimeLimit, string Kind, int Cycle, int Cycles, int Done, int Total,
    ClassicPlanCheckpoint Plan, string RecordMaterial = null);
