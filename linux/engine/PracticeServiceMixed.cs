using System;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class PracticeService
{
    private MixedTypingRun mixedTyping;

    private bool StartMixedTyping(JsonElement m)
    {
        string mode = Text(m, "mode");
        if (!MixedTypingRun.Modes.ContainsKey(mode)) return false;
        mixedTyping = MixedTypingRun.Create(mode, Text(m, "layout") is "RU" or "EN" ? Text(m, "layout") : layout,
            Number(m, "level"), Number(m, "seconds"), Number(m, "cycles", 1), Flag(m, "infinite"),
            verifyInputs && m.TryGetProperty("seed", out _) ? new Random(Number(m, "seed")) : Random.Shared, rightAltMode, windows);
        mixedTyping.SetLayoutBindings(forward, backward);
        mixedTyping.SetGuide(profile.RightHandChord, rightAltMode, windows);
        BeginTypingOwnership(profile.ClassicTypingDraft, false);
        typing = null;
        return true;
    }

    private bool ContinueMixedTyping()
    {
        var restored = MixedTypingRun.Restore(profile.ClassicTypingDraft);
        if (restored == null) return false;
        mixedTyping = restored;
        mixedTyping.SetLayoutBindings(forward, backward);
        mixedTyping.SetGuide(profile.RightHandChord, rightAltMode, windows);
        BeginTypingOwnership(profile.ClassicTypingDraft, true);
        typing = null;
        return true;
    }

    private void SuspendMixedTyping()
    {
        var t = mixedTyping;
        if (t == null) return;
        t.Pause();
        if (t.Finished) { FinishMixedTyping(); return; }
        if (t.CanSave) SaveTypingCheckpoint(t.Draft());
    }

    private void FinishMixedTyping()
    {
        var t = mixedTyping;
        if (t == null) return;
        t.Pause();
        if (!t.CanSave) return;
        if (SaveTypingOutcome(t.AttemptId, p =>
        {
            var item = t.MakeResult(DateTime.Now);
            TypingModeRecords.PreserveHistory(p);
            p.Results.Add(item);
            TypingModeRecords.Record(p, item, t.Level, t.Cycles, t.TimeLimit, -1, t.StartLayout, completed: item.Completed);
            if (p.Results.Count > 500) p.Results.RemoveRange(0, p.Results.Count - 500);
            if (item.Completed) TypingReviewLedger.Register(p, item, t.AttemptId, t.Title, ReviewMisses(t.Mistakes));
        })) t.Saved();
    }

    private object MixedInputState(JsonElement m)
    {
        if (mixedTyping == null) throw new InvalidOperationException("Сначала выберите упражнение на ввод.");
        mixedTyping.Physical(Text(m, "key"), Flag(m, "pressed"));
        if (mixedTyping.Finished) FinishMixedTyping();
        return mixedTyping.State(Text(m, "layout"));
    }
}
