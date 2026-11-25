using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TouchTypingStudio;

// Serialized source observations and generated plans are deliberately separate from
// historical aggregate key statistics. A symbol and a command have different identities.
internal sealed record ReviewMiss(string Kind, string Value, string Layout, long Count);
internal sealed record ReviewDeck(TypingAtom[] Units, int[] GroupEnds)
{
    internal bool Valid() => Units is { Length: >= TypingReviewPlan.Minimum and <= TypingReviewPlan.Maximum }
        && GroupEnds is { Length: > 0 } && GroupEnds.Length <= Units.Length && GroupEnds[^1] == Units.Length
        && GroupEnds.Select((end, i) => end > (i == 0 ? 0 : GroupEnds[i - 1]) && end <= Units.Length).All(ok => ok)
        && Units.All(unit => unit != null && unit.IsValid());

    // Groups are visual only. A separator is never an extra action, never the
    // next expected unit, and never changes the physical command identity.
    internal string Ribbon(int start, int end, bool separatorAfterEnd = false)
    {
        var buffer = new StringBuilder();
        for (int i = start; i < end; i++)
        {
            if (i > start && Array.BinarySearch(GroupEnds, i) >= 0) buffer.Append("   ");
            buffer.Append(Units[i].Display);
        }
        if (separatorAfterEnd && end > 0 && end < Units.Length && Array.BinarySearch(GroupEnds, end) >= 0) buffer.Append("   ");
        return buffer.ToString();
    }

    internal string GroupLabel(int position)
    {
        int i = Array.FindIndex(GroupEnds, end => end > position);
        return i < 0 ? "" : $"Группа {i + 1}/{GroupEnds.Length} · {position - (i == 0 ? 0 : GroupEnds[i - 1])}/{GroupEnds[i] - (i == 0 ? 0 : GroupEnds[i - 1])}";
    }
}

internal static class TypingReviewPlan
{
    internal const int Minimum = 150, Maximum = 200;
    // Compare the exact source ratio, NOT the rounded presentation. For example
    // 49/50 is exactly 98%; 39/40 is green (97.5%) but still eligible.
    internal static bool Eligible(long correct, long errors, bool completed, bool isReview = false) =>
        completed && !isReview && correct > 0 && errors > 0 && 100m * correct < 98m * ((decimal)correct + errors);

    private sealed record Rank(string Kind, string Value, decimal Weight, ReviewMiss[] Layouts);
    private static bool Valid(ReviewMiss m) => m != null && m.Count > 0 && (m.Layout is "EN" or "RU") &&
        ((m.Kind == "text" && m.Value?.Length == 1) || (m.Kind == "command" && !string.IsNullOrEmpty(m.Value)));
    private static Rank[] RankMisses(IEnumerable<ReviewMiss> input) =>
        input.Where(Valid).GroupBy(x => (x.Kind, x.Value))
            .Select(g => new Rank(g.Key.Kind, g.Key.Value, g.Sum(m => (decimal)m.Count),
                g.OrderBy(x => x.Layout, StringComparer.Ordinal).ToArray()))
            .OrderByDescending(x => x.Weight).ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal).ToArray();

    private static int[] MinimumCounts(Rank[] ranks)
    {
        var counts = new int[ranks.Length];
        int floor = 0;
        for (int i = ranks.Length - 1; i >= 0; i--)
        {
            if (i == ranks.Length - 1 || ranks[i].Weight != ranks[i + 1].Weight) floor++;
            counts[i] = floor;
        }
        return counts;
    }

    internal static ReviewDeck Build(string sourceId, IEnumerable<ReviewMiss> observations)
    {
        var ranks = RankMisses(observations);
        if (ranks.Length == 0) throw new ArgumentException("В этой попытке нет записанных промахов.");
        // Seed is distinct per primary attempt, not per click. Store the generated
        // sequence on first open; a later generator change cannot alter a draft.
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes("typing-review-v1/" + sourceId));
        var rng = new Random(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(seed));
        int length = rng.Next(Minimum, Maximum + 1);

        // If all observed error ranks physically cannot fit with strict precedence
        // in 200 slots, take the largest ranked prefix that can. The full ledger stays intact.
        if (ranks.Length > Maximum) ranks = ranks[..Maximum];
        int[] counts = MinimumCounts(ranks);
        while (counts.Sum() > Maximum) { ranks = ranks[..^1]; counts = MinimumCounts(ranks); }
        length = Math.Max(length, counts.Sum());
        int free = length - counts.Sum();
        decimal total = ranks.Sum(x => x.Weight);
        for (int i = 0; i < counts.Length; i++) counts[i] += (int)decimal.Floor(free * ranks[i].Weight / total);
        // Hand out integer remainders in rank order, never letting a lower
        // frequency tie/overtake a strictly higher one in the finished deck.
        int leftover = length - counts.Sum();
        for (int i = 0; i < leftover; i++) counts[i]++;

        var tokens = new List<TypingAtom>(length);
        for (int i = 0; i < ranks.Length; i++)
        {
            var rank = ranks[i];
            for (int j = 0; j < counts[i]; j++)
            {
                // E.g. '%' can be expected in two layouts. Choose layout from
                // this source's observations; command name remains unlocalized.
                decimal at = (j + .5m) * rank.Weight / counts[i], sum = 0;
                var pick = rank.Layouts.First(m => (sum += m.Count) > at);
                tokens.Add(new TypingAtom(rank.Kind, rank.Value, pick.Layout));
            }
        }
        for (int i = tokens.Count - 1; i > 0; --i)
        {
            int j = rng.Next(i + 1);
            (tokens[i], tokens[j]) = (tokens[j], tokens[i]);
        }
        var groupEnds = new List<int>();
        for (int end = 0; end < length;)
        {
            end = Math.Min(length, end + rng.Next(4, 10));
            groupEnds.Add(end);
        }
        return new(tokens.ToArray(), groupEnds.ToArray());
    }

    internal static ReviewDeck ReadDeck(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > 65_536)
            throw new ArgumentException("Размер сохранённой работы над ошибками неверен.");
        var deck = Load<ReviewDeck>(raw);
        if (!deck.Valid()) throw new ArgumentException("Не удалось подтвердить состав сохранённой работы над ошибками.");
        return deck;
    }
    internal static string Save<T>(T value) => JsonSerializer.Serialize(value, Program.Json);
    internal static T Load<T>(string value) => JsonSerializer.Deserialize<T>(value, Program.Json) ??
        throw new ArgumentException("Не удалось прочитать сохранённую работу над ошибками.");
}
