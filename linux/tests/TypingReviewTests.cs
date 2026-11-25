using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static class TypingReviewTests
{
    private static int passed;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        passed++;
    }

    public static void Main()
    {
        Check(TypingReviewPlan.Eligible(39, 1, true), "97.5% is eligible");
        Check(TypingReviewPlan.Eligible(48999, 1001, true), "97.998% is eligible before display rounding");
        Check(!TypingReviewPlan.Eligible(49, 1, true), "98% strictly excluded");
        Check(!TypingReviewPlan.Eligible(99, 1, true), "99% excluded");
        Check(!TypingReviewPlan.Eligible(100, 0, true), "100% excluded");
        Check(!TypingReviewPlan.Eligible(1, 10, false), "abandoned source excluded");
        Check(!TypingReviewPlan.Eligible(1, 10, true, true), "no chain");

        ReviewMiss[] weights = [
            new("text","ж","RU",5), new("text","7","EN",1), new("text","7","RU",2),
            new("text","%","EN",1), new("command","Ctrl+V","RU",2)];
        var singleLengths = new HashSet<int>();
        var singleGroups = new HashSet<string>();
        for (int seed = 0; seed < 300; seed++)
        {
            var deck = TypingReviewPlan.Build("source/" + seed, weights);
            Check(deck.Units.Length is >= 150 and <= 200, "deck size bounds");
            Check(deck.GroupEnds[^1] == deck.Units.Length && deck.GroupEnds[0] > 0 &&
                deck.GroupEnds.Zip(deck.GroupEnds.Skip(1)).All(p => p.Second > p.First), "group partition");
            var counts = deck.Units.GroupBy(t => t.Value).ToDictionary(g => g.Key, g => g.Count());
            Check(counts.Count == 4 && counts["ж"] > counts["7"] && counts["7"] > counts["Ctrl+V"] &&
                counts["Ctrl+V"] > counts["%"], "strict frequencies in final shuffled deck");
            Check(deck.Units.Any(t => t.Value == "Ctrl+V" && t.Command && t.Layout == "RU"), "canonical physical command");
            Check(deck.Units.Where(t => t.Value == "7").Select(t => t.Layout).Distinct().Count() == 2, "one symbol, both observed layouts");
            Check(TypingReviewPlan.Save(deck) == TypingReviewPlan.Save(TypingReviewPlan.Build("source/" + seed, weights.Reverse())),
                "source identity reproduces the same material independently of enumeration order");
            var one = TypingReviewPlan.Build("source/" + seed, [new("text","ж","RU",90)]);
            Check(one.Units.All(t => t.Value == "ж"), "group separators are not stray scored characters");
            singleLengths.Add(one.Units.Length); singleGroups.Add(string.Join(",", one.GroupEnds));
        }
        Check(singleLengths.Count > 40 && singleGroups.Count > 250, "single-symbol attempts vary in length and grouping");

        var p = new StudioProfile();
        var source = new TypingResult { Completed = true, Count = 39, Errors = 1, Lesson = "mixed" };
        p.Results.Add(source);
        TypingReviewLedger.Register(p, source, "s", "lesson", weights);
        TypingReviewLedger.Register(p, source, "s", "lesson", weights);
        Check(p.TypingReviews.Count == 1 && source.PracticeAttemptId == "s", "one ticket per completed source");
        var t = TypingReviewLedger.Open(p, "s");
        string plan = t.Plan; int rev = t.Revision;
        var duplicate = TypingReviewLedger.Open(p, "s");
        Check(duplicate.Plan == plan && duplicate.Revision == rev, "double open is idempotent");
        var units = TypingReviewPlan.Load<ReviewDeck>(plan).Units;
        Check(TypingReviewLedger.Update(p,"s",rev,4,1,2,[],false,units.Length), "save draft");
        Check(!TypingReviewLedger.Update(p,"s",rev,1,0,1,[],false,units.Length), "stale process cannot overwrite draft");
        Check(t.Position == 4 && t.Errors == 1, "winning progress preserved");
        Check(TypingReviewLedger.Update(p,"s",t.Revision,units.Length,3,10,[],true,units.Length), "complete");
        Check(!TypingReviewLedger.Update(p,"s",t.Revision,units.Length,3,10,[],true,units.Length), "no second finish");
        Check(p.Results.Count(r => r.ReviewOf == "s") == 1 && !string.IsNullOrEmpty(source.PracticeAttemptId), "distinct result");
        bool rejected = false;
        try { TypingReviewLedger.Open(p,"s"); } catch (ArgumentException) { rejected = true; }
        Check(rejected && TypingReviewLedger.Drafts(p).Length == 0, "completed review cannot restart");
        Console.WriteLine("PASS typing review: " + passed + " planner, threshold, source and ledger checks.");
    }
}
