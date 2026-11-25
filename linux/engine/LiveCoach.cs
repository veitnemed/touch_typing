using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LiveCoaching(string Stage, string Reason)
{
    internal static readonly LiveCoaching None = new("", "");
    internal bool Blind => Stage is "recall" or "transfer";
}

// History is evidence: an assisted success never stands in for remembering,
// and a remembered action in the same workspace never counts as transfer.
internal static class LiveCoach
{
    private const int InterveningActions = 2;
    internal const string Practice = "practice", Recall = "recall", Transfer = "transfer";

    private static string Skill(string app, string id) => "lab/" + app + "/" + id;

    private static IReadOnlyDictionary<string, ShortcutAttempt[]> BySkill(IEnumerable<ShortcutAttempt> attempts, string layout) =>
        attempts.Where(a => a.Skill != null && a.Layout == layout && a.InputMode == "exact")
                .GroupBy(a => a.Skill).ToDictionary(g => g.Key, g => g.ToArray());

    private static int NewestUnresolvedMiss(ShortcutAttempt[] events)
    {
        for (int i = events.Length - 1; i >= 0; i--)
        {
            var sample = events[i];
            if (sample.CoachStage == Transfer && IndependentlySucceeded(sample)) return -1;
            if (!sample.Correct) return i;
        }
        return -1;
    }

    private static bool IndependentlySucceeded(ShortcutAttempt sample) =>
        sample.Correct && sample.FirstTry && !sample.Assisted && sample.Independent;

    internal static HashSet<string> Outstanding(string app, string layout, IEnumerable<ShortcutAttempt> attempts)
    {
        string prefix = Skill(app, "");
        return BySkill(attempts, layout).Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal) &&
            NewestUnresolvedMiss(entry.Value) >= 0).Select(entry => entry.Key[prefix.Length..]).ToHashSet();
    }

    internal static LiveCoaching Decide(LabTask task, string app, string layout, string mission, string context,
        IEnumerable<ShortcutAttempt> attempts)
    {
        // A command may also be recalled by meaning, e.g. "show this folder"
        // → pwd. Leave explicit data and long copied commands as instructions.
        string answer = task.Keys.Length != 0 ? task.Keys :
            task.Id.StartsWith("command/", StringComparison.Ordinal) ? task.Text : "";
        if (answer.Length == 0 || task.Title.Replace(" ", "").Contains(answer.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            return LiveCoaching.None; // an answer cannot be hidden if it is the instruction itself
        string skill = Skill(app, task.Id);
        var history = attempts.Where(a => a.Layout == layout && a.InputMode == "exact").ToArray();
        var atSkill = history.Where(a => a.Skill == skill).ToArray();
        int miss = NewestUnresolvedMiss(atSkill);
        if (miss < 0 || task.Id == "layout") return LiveCoaching.None;
        var changes = atSkill.Skip(miss + 1).ToArray();
        var demonstrated = changes.FirstOrDefault(a => a.CoachStage == Practice && a.Correct && a.Assisted);
        if (demonstrated == null)
            return new(Practice, "Это действие недавно не получилось. Повторите его с подсказкой.");
        var recalled = changes.FirstOrDefault(a => a.CoachStage == Recall && IndependentlySucceeded(a));
        var checkpoint = recalled ?? demonstrated;
        int index = Array.IndexOf(history, checkpoint);
        int spacing = history.Skip(index + 1).Count(a => a.Correct && a.Skill != skill);
        if (spacing < InterveningActions) return LiveCoaching.None;
        if (recalled == null)
            return new(Recall, "Вы уже сделали это с подсказкой. Вспомните действие по рабочей цели.");
        if (recalled.Mission != mission && !string.IsNullOrEmpty(recalled.CoachContext) && recalled.CoachContext != context)
            return new(Transfer, "Проверьте знакомый навык в другом поручении и рабочем состоянии.");
        return LiveCoaching.None;
    }
}
