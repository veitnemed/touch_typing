using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TouchTypingStudio;

// A correction reuses the exact mixed-input engine (including physical chords).
// It is NOT a normal run for records, motor history, or generation of another ticket.
internal sealed class TypingReviewSession
{
    internal readonly string SourceId;
    internal readonly ReviewDeck Deck;
    internal readonly MixedTypingRun Run;
    internal int Revision;
    internal int WrittenAt;
    internal double SavedSeconds;
    internal bool FinishedOnDisk, Conflict;
    internal TypingReviewSession(TypingReviewTicket ticket)
    {
        SourceId = ticket.SourceId;
        Deck = TypingReviewPlan.ReadDeck(ticket.Plan);
        if (ticket.Position < 0 || ticket.Position >= Deck.Units.Length || ticket.Errors < 0 ||
            !double.IsFinite(ticket.Seconds) || ticket.Seconds < 0)
            throw new ArgumentException("Сохранённая позиция исправления неверна. Другие упражнения доступны.");
        var errors = string.IsNullOrEmpty(ticket.Mistakes) ? new Dictionary<TypingAtom,int>() :
            TypingReviewPlan.Load<ReviewMiss[]>(ticket.Mistakes).ToDictionary(
                x => new TypingAtom(x.Kind, x.Value, x.Layout), x => checked((int)x.Count));
        Run = MixedTypingRun.FromPlan("mistake-review", "Работа над ошибками · " + ticket.Title, Deck.Units,
            ticket.Position, ticket.Errors, ticket.Seconds, errors);
        Revision = ticket.Revision; WrittenAt = ticket.Position + ticket.Errors;
        SavedSeconds = ticket.Seconds;
    }

    internal static object AttachOffer(object result, string attemptId, StudioProfile profile)
    {
        if (result == null) return null;
        var payload = JsonSerializer.SerializeToNode(result, Program.Json).AsObject();
        var state = payload["state"].AsObject();
        state["attemptId"] = attemptId; state["isReview"] = false;
        state["reviewAvailable"] = TypingReviewLedger.Find(profile, attemptId) is { Completed: false };
        return payload;
    }

    internal object State(string layout)
    {
        var state = JsonSerializer.SerializeToNode(Run.State(layout), Program.Json).AsObject();
        int at = Run.Position;
        state["before"] = Deck.Ribbon(Math.Max(0, at - 18), at, separatorAfterEnd: true);
        state["after"] = Deck.Ribbon(at, Math.Min(Deck.Units.Length, at + 75));
        state["source"] = Deck.GroupLabel(at);
        state["isReview"] = true;
        state["attemptId"] = SourceId;
        state["reviewAvailable"] = false;
        return state;
    }

    internal object Result(string layout) => new { state = State(layout), series = Array.Empty<object>() };
}
