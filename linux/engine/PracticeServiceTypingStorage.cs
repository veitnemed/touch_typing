using System;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class PracticeService
{
    private string expectedTypingDraft, typingConflictNotice;
    private bool ownsTypingDraft, typingOwnershipLost;

    // Both desktop entry points can have an engine open. Keep the exact last
    // checkpoint seen by this attempt, not just its ID: a second process can
    // advance the same ID while an old window is paused.
    private void BeginTypingOwnership(string currentDraft, bool continuing)
    {
        expectedTypingDraft = currentDraft;
        ownsTypingDraft = continuing;
        typingOwnershipLost = false;
        typingConflictNotice = null;
    }

    private static bool TypingDraftHasId(string raw, string id)
    {
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(id)) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("id", out var saved) && saved.ValueKind == JsonValueKind.String &&
                string.Equals(saved.GetString(), id, StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static void RequireRequestedTypingDraft(JsonElement message, string currentDraft)
    {
        string id = Text(message, "id");
        if (id.Length != 0 && !TypingDraftHasId(currentDraft, id))
            throw new InvalidOperationException("Этот черновик изменился или уже завершён. Откройте раздел «Печать» заново.");
    }

    private bool ChangeTypingStorage(bool compareSlot, Action<StudioProfile> apply)
    {
        if (typingOwnershipLost) return false;
        bool replaced = false;
        Save(current =>
        {
            if (compareSlot && !string.Equals(current.ClassicTypingDraft, expectedTypingDraft, StringComparison.Ordinal))
            {
                replaced = true;
                // Abort the underlying atomic update without rewriting even the
                // profile timestamp if another window has already won.
                throw new InvalidOperationException("Черновик изменён в другом окне.");
            }
            apply(current);
        });
        if (!replaced) return store.Error == null;
        typingOwnershipLost = true;
        profile = store.Read(); // no sticky I/O error: retrying Close may dismiss this obsolete view
        typingConflictNotice = "Черновик уже изменён в другом окне. Эта копия его не перезаписала. " +
            "Закройте это окно и продолжите из раздела «Печать».";
        return false;
    }

    private void SaveTypingCheckpoint(string checkpoint)
    {
        // Pause followed by Close (and stdin EOF) is the same checkpoint. In
        // particular it must not resurrect a checkpoint another window finished.
        if (typingOwnershipLost || (ownsTypingDraft && checkpoint == expectedTypingDraft)) return;
        if (ChangeTypingStorage(true, p => p.ClassicTypingDraft = checkpoint))
        {
            expectedTypingDraft = checkpoint;
            ownsTypingDraft = true;
        }
    }

    // True means this view should not try to file another outcome. When
    // superseded there is deliberately no new result, and the first attempt to
    // overwrite reports the conflict. Fresh unrelated attempts may both finish.
    private bool SaveTypingOutcome(string id, Action<StudioProfile> persist)
    {
        bool stored = ChangeTypingStorage(ownsTypingDraft, p =>
        {
            persist(p);
            if (TypingDraftHasId(p.ClassicTypingDraft, id)) p.ClassicTypingDraft = null;
        });
        return stored || typingOwnershipLost;
    }
}
