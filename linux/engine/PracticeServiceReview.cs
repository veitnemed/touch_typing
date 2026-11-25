using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class PracticeService
{
    private TypingReviewSession review;

    private void PersistReview(bool pause = false, bool force = false)
    {
        var r = review;
        if (r == null || r.FinishedOnDisk) return;
        if (r.Conflict) throw new InvalidOperationException("Эту работу уже продолжили в другом окне. Вернитесь к занятиям и откройте сохранённую попытку.");
        if (pause) r.Run.Pause(); // clears pressed modifiers; time closed/on pause is not practice time
        int position = r.Run.Position, errors = r.Run.Errors;
        double seconds = r.Run.Seconds;
        int units = position + errors;
        if (!r.Run.Completed && !force && units - r.WrittenAt < 12 &&
            (units == r.WrittenAt || seconds - r.SavedSeconds < 3.0)) return;
        if (units == r.WrittenAt && seconds == r.SavedSeconds && !r.Run.Completed) return;
        bool applied = false;
        Save(p => applied = TypingReviewLedger.Update(p, r.SourceId, r.Revision, position,
            errors, seconds, ReviewMisses(r.Run.Mistakes), r.Run.Completed, r.Deck.Units.Length));
        // ProfileStore may return the mutated in-memory object after an I/O failure.
        // Never mark a finished result or advance the local revision until disk succeeded.
        if (store.Error != null) throw new InvalidOperationException(store.Error);
        if (!applied)
        {
            r.Run.Pause();
            r.Conflict = true;
            throw new InvalidOperationException("Сохранение уже обновлено другим окном. Откройте «Печать» и продолжите актуальную попытку.");
        }
        r.Revision++;
        r.WrittenAt = units;
        r.SavedSeconds = seconds;
        r.FinishedOnDisk = r.Run.Completed;
    }

    private static IEnumerable<ReviewMiss> ReviewMisses(IReadOnlyDictionary<TypingAtom, int> entries) =>
        entries.Select(e => new ReviewMiss(e.Key.Kind, e.Key.Value, e.Key.Layout, e.Value));

    private bool TryReviewCommand(JsonElement m, out object response)
    {
        response = null;
        string command = Text(m, "command"), lang = Text(m, "layout") == "RU" ? "RU" : "EN";
        if (command == "typing-history-result")
        {
            profile = store.Read(); // including completions made by a second open window
            if (store.Error != null) throw new InvalidOperationException(store.Error);
            response = TypingReviewLedger.HistoricalResult(profile, Text(m, "sourceId"));
            return true;
        }
        if (command == "typing-review-start")
        {
            string id = Text(m, "sourceId");
            if (review?.SourceId == id && !review.Conflict && !review.FinishedOnDisk)
            {
                review.Run.Resume();
                response = review.State(lang);
                return true;
            }
            // Do not destroy a main/older review draft on a malformed or repeated request.
            var current = store.Read();
            if (store.Error != null) throw new InvalidOperationException(store.Error);
            var available = TypingReviewLedger.Find(current, id);
            if (available == null || available.Completed) throw new ArgumentException("Для этой попытки нет незавершённой работы над ошибками.");
            if (review != null && !review.Conflict) PersistReview(pause: true, force: true);
            if (review == null && (typing != null || mixedTyping != null))
            {
                SuspendTyping();
                if (typingConflictNotice != null) throw new InvalidOperationException(typingConflictNotice);
            }
            if (store.Error != null) throw new InvalidOperationException(store.Error);
            TypingReviewSession opened = null;
            Save(p => opened = new TypingReviewSession(TypingReviewLedger.Open(p, id)));
            if (store.Error != null) throw new InvalidOperationException(store.Error);
            review = opened;
            review.Run.SetLayoutBindings(forward, backward);
            typing = null;
            mixedTyping = null;
            response = review.State(lang);
            return true;
        }
        if (review == null)
        {
            if (command != "typing-result") return false;
            var result = mixedTyping?.Result(lang) ?? typing?.Result(lang);
            response = TypingReviewSession.AttachOffer(result, mixedTyping?.AttemptId ?? typing?.AttemptId, profile);
            return true;
        }
        switch (command)
        {
            case "typing-start": case "typing-continue":
                if (!review.Conflict) PersistReview(pause: true, force: true);
                review = null;
                return false; // main run creation is kept in its original code path
            case "typing-physical":
                review.Run.Physical(Text(m, "key"), Flag(m, "pressed")); PersistReview(); break;
            case "typing-key": review.Run.Enter(Text(m, "text")); PersistReview(); break;
            case "typing-result":
                PersistReview(pause: true, force: true);
                response = review.Result(lang);
                return true;
            case "typing-state": PersistReview(); break;
            case "typing-stop": case "typing-suspend": case "typing-pause":
                PersistReview(pause: true, force: true); break;
            case "typing-resume": review.Run.Resume(); break;
            default: return false;
        }
        response = review.State(lang);
        return true;
    }
}

internal sealed partial class TypingRun
{
    // For old ordinary modes, preserve the actual expected text, never the erroneous input.
    // Authored navigation tokens become physical gestures rather than literal control characters.
    private IEnumerable<ReviewMiss> ReviewMisses()
    {
        foreach (var hit in MistakeCounts)
        {
            char c = hit.Key;
            string command = c switch { CodeText.Erase or '\b' => "Backspace", '\n' or '\r' => "Enter", '\t' => "Tab", _ => null };
            Layout lang = CodeText.Language(c);
            if (lang == Layout.Neutral) lang = PreferredLayout;
            string layout = lang == Layout.Russian ? "RU" : "EN";
            if (command != null) yield return new ReviewMiss("command", command, layout, hit.Value);
            else if (TypingKeyboard.Supports(c)) yield return new ReviewMiss("text", c.ToString(), layout, hit.Value);
        }
    }
}
