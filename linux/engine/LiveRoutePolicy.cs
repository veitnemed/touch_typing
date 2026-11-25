using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// Admission and scheduling are separate from individual scenario definitions.
// A catalog item may be practiced on its own without taking over a main card.
internal static class LiveRoutePolicy
{
    internal const string Course = "course";
    internal const string Mission = "mission";
    internal const int DefaultActions = 40;

    internal static string Resolve(string requested, string mission, bool legacyEditorCourse, string app, LiveDraft draft)
    {
        if (draft != null)
            return draft.RouteKind is Course or Mission ? draft.RouteKind : draft.EditorCourse && app == "code" ? Course : "";
        if (requested == Course && string.IsNullOrEmpty(mission) ||
            legacyEditorCourse && app == "code" && string.IsNullOrEmpty(mission)) return Course;
        return requested == Mission && !string.IsNullOrEmpty(mission) ? Mission : "";
    }

    internal static bool EditorSupplement(string id) =>
        id.StartsWith("code-notebook-", StringComparison.Ordinal) ||
        id.StartsWith("code-project-file-", StringComparison.Ordinal);

    private static bool IsCore(LiveMission mission, string environment) => mission.App switch
    {
        "code" => !EditorSupplement(mission.Id),
        "windows" => mission.Id == "windows-desktop-" + mission.Level ||
                     mission.Id == "windows-desktop-return-" + mission.Level,
        "workday" when environment == "windows" => mission.Id == "workday-windows-" + mission.Level ||
                     mission.Id == "workday-windows-return-" + mission.Level,
        // This includes all four original, longer Terminal scenarios per
        // level, before quick warm-ups (terminal-essentials-*) are offered.
        _ => !mission.Curated
    };

    internal static LiveMission Pick(LiveMission[] unused, LiveMission[] rotation, LiveMission previous,
        string environment, IEnumerable<ShortcutAttempt> attempts, HashSet<string> targets = null)
    {
        bool needsCore = previous == null || !IsCore(previous, environment);
        var candidates = unused.Where(m => IsCore(m, environment) == needsCore).ToArray();
        if (candidates.Length == 0)
        {
            // Supplements must not run consecutively if a core still exists,
            // even in a future level with more supplements than core routes.
            candidates = needsCore ? rotation.Where(m => IsCore(m, environment)).ToArray() : unused;
            if (candidates.Length == 0) candidates = unused;
        }
        if (targets?.Count > 0)
        {
            var practice = candidates.Where(m => LiveCourse.CouldPracticeAny(m, targets)).ToArray();
            if (practice.Length > 0) candidates = practice;
        }
        var lastPracticed = attempts.Where(a => a.Correct && a.Mission != null)
            .GroupBy(a => a.Mission).ToDictionary(g => g.Key, g => g.Max(a => a.Date));
        // Stable catalog order is the tie-break for a new learner. Once used,
        // a learner reaches less recently practised routes first.
        return candidates.OrderBy(m => lastPracticed.GetValueOrDefault(m.Id)).First();
    }
}
