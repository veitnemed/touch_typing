using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// All methods which mutate a ticket run under ProfileStore.Update's existing cross-
// process mutex and single atomic XML replacement. One logical completion writes
// both the terminal ticket and the result; neither can be exposed on a failed save.
internal static class TypingReviewLedger
{
    internal static TypingReviewTicket Find(StudioProfile p, string sourceId) =>
        !string.IsNullOrEmpty(sourceId) ? p.TypingReviews.Find(t => t.SourceId == sourceId) : null;

    internal static void Register(StudioProfile p, TypingResult result, string sourceId, string title,
            IEnumerable<ReviewMiss> misses)
    {
        if (result == null || string.IsNullOrEmpty(sourceId)) return;
        result.PracticeAttemptId = sourceId;
        // Prune unstarted/completed tickets with their old history rows. Explicitly
        // opened correction drafts remain resumable even after 500 other sessions.
        var retained = p.Results.Where(row => !string.IsNullOrEmpty(row.PracticeAttemptId))
            .Select(row => row.PracticeAttemptId).ToHashSet(StringComparer.Ordinal);
        p.TypingReviews.RemoveAll(t => (t.Completed || string.IsNullOrEmpty(t.Plan)) && !retained.Contains(t.SourceId));
        if (!TypingReviewPlan.Eligible(result.Count, result.Errors, result.Completed, !string.IsNullOrEmpty(result.ReviewOf)) ||
            Find(p, sourceId) != null) return;
        var m = misses.Where(item => item.Count > 0).ToArray();
        if (m.Length == 0) return; // Do not invent per-key data for an old/aggregate-only result.
        p.TypingReviews.Add(new TypingReviewTicket { SourceId = sourceId, Title = title, Weights = TypingReviewPlan.Save(m) });
    }

    internal static TypingReviewTicket Open(StudioProfile p, string id)
    {
        var t = Find(p, id) ?? throw new ArgumentException("Для этой попытки нет работы над ошибками.");
        if (t.Completed) throw new ArgumentException("Работа над ошибками этой попытки уже завершена.");
        if (string.IsNullOrEmpty(t.Plan))
        {
            var deck = TypingReviewPlan.Build(id, TypingReviewPlan.Load<ReviewMiss[]>(t.Weights));
            t.Plan = TypingReviewPlan.Save(deck);
            t.Revision = 1;
        }
        return t;
    }

    internal static bool Update(StudioProfile p, string id, int revision,
        int position, int errors, double seconds, IEnumerable<ReviewMiss> reviewMisses, bool complete, int total)
    {
        var t = Find(p, id);
        if (t == null || t.Completed || t.Revision != revision) return false;
        if (position < t.Position || position > total || errors < t.Errors || !double.IsFinite(seconds) ||
              seconds < t.Seconds || complete != (position == total)) return false;
        t.Position = position; t.Errors = errors; t.Seconds = seconds; t.Revision++;
        if (!complete)
        {
            t.Mistakes = TypingReviewPlan.Save(reviewMisses);
            return true;
        }
        t.Completed = true;
        t.Plan = t.Mistakes = null; // original error counts remain visible in this source's history
        TypingModeRecords.PreserveHistory(p);
        p.Results.Add(new TypingResult { Lesson = "Работа над ошибками · " + t.Title,
            Activity = "TypingMistakeReview", Task = "mistake-review", ReviewOf = id,
            Date = DateTime.Now, Count = position, Errors = errors, Seconds = seconds, Completed = true });
        if (p.Results.Count > 500) p.Results.RemoveRange(0, p.Results.Count - 500);
        return true;
    }

    internal static object HistoricalResult(StudioProfile p, string id)
    {
        var row = p.Results.LastOrDefault(t => t.PracticeAttemptId == id && !string.IsNullOrEmpty(id));
        if (row == null) throw new ArgumentException("Эта попытка не найдена в истории занятий.");
        var ticket = Find(p, id);
        var counts = string.IsNullOrEmpty(ticket?.Weights) ? Array.Empty<ReviewMiss>() : TypingReviewPlan.Load<ReviewMiss[]>(ticket.Weights);
        return new { state = new { title = row.Lesson, count = row.Count, errors = row.Errors, seconds = (int)row.Seconds,
            cpm = row.Speed, accuracy = row.Accuracy, attemptId = id, isReview = false, isHistory = true,
            reviewAvailable = ticket is { Completed: false }, finished = row.Completed,
            topMistakes = counts.GroupBy(m => m.Value).Select(g => new { key = g.Key, count = g.Sum(m => m.Count) })
                .OrderByDescending(m => m.count).ThenBy(m => m.key, StringComparer.Ordinal).ToArray() }, series = Array.Empty<object>() };
    }

    internal static object[] Drafts(StudioProfile p)
    {
        var drafts = new List<object>();
        foreach (var t in p.TypingReviews.Where(t => !t.Completed && !string.IsNullOrEmpty(t.Plan)))
        {
            try
            {
                // A corrupt corrective draft must not break startup or the rest of the catalog.
                var deck = TypingReviewPlan.ReadDeck(t.Plan);
                if (t.Position < 0 || t.Position >= deck.Units.Length) continue;
                drafts.Add(new { sourceId = t.SourceId, title = t.Title, position = t.Position, total = deck.Units.Length });
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException) { }
        }
        return drafts.ToArray();
    }
}
