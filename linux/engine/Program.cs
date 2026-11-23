using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TouchTypingStudio;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static void Main(string[] args)
    {
        Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;
        var service = new PracticeService(args.Contains("--verify"));
        while (Console.ReadLine() is { } line)
        {
            try
            {
                using var message = JsonDocument.Parse(line);
                object value = service.Handle(message.RootElement);
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, value, storageError = service.StorageError }, Json));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or FormatException)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = exception.Message }, Json));
            }
        }
        service.FinishTyping();
        service.FinishLab();
    }
}

internal sealed class PracticeService
{
    private const string DefaultLayoutBinding = "Super+Space";
    private readonly ProfileStore store;
    private StudioProfile profile;
    private bool windows = OperatingSystem.IsWindows();
    private string layout = "EN", forward = DefaultLayoutBinding, backward = "Super+Shift+Space";
    private ShortcutRun run;
    private TypingRun typing;
    private LabRun lab;
    private LiveDraft labCheckpoint;
    private bool labLimitCheckpointAttempted;
    private int persistedLabAttempts;
    private bool labFinishAttempted;
    private string labSaveError;
    private string[] dock, desktopProfile;
    internal string StorageError => store.Error;

    private readonly bool neutralProject;

    internal PracticeService(bool verify)
    {
        neutralProject = !verify || Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_LEGACY_PROJECT") != "1";
        string data = null;
        if (!windows)
        {
            data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(data) || !Path.IsPathFullyQualified(data))
                data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
        store = new ProfileStore(verify, data);
        profile = store.Read();
        TrainingProgress.Migrate(profile, DateTime.Now);
        if (windows) UseStoredWindowsLayoutBindings();
    }

    internal object Handle(JsonElement message)
    {
        bool verifyProfile = false;
        switch (Text(message, "command"))
        {
            case "configure":
                layout = Text(message, "layout") == "RU" ? "RU" : "EN";
                string platform = Text(message, "platform");
                if (platform is "windows" or "linux") windows = platform == "windows";
                if (windows)
                {
                    UseStoredWindowsLayoutBindings();
                    if (message.TryGetProperty("forward", out _)) forward = WindowsLayoutBinding(Text(message, "forward"));
                    if (message.TryGetProperty("backward", out _)) backward = WindowsLayoutBinding(Text(message, "backward"));
                }
                else
                {
                    forward = ValidBinding(Text(message, "forward"));
                    backward = ValidBinding(Text(message, "backward"));
                }
                if (!windows && message.TryGetProperty("desktopBindings", out var bindings) && bindings.ValueKind == JsonValueKind.Object)
                    desktopProfile = LabScene.NormalizeDesktopProfile(LabScene.GnomeProfileActions
                        .Where(id => bindings.TryGetProperty(id, out var entry) && entry.ValueKind == JsonValueKind.String)
                        .Select(id => KeyValuePair.Create(id, bindings.GetProperty(id).GetString())));
                if (message.TryGetProperty("dock", out var pinned) && pinned.ValueKind == JsonValueKind.Array)
                    dock = pinned.EnumerateArray().Take(9).Select(item => item.GetString() ?? "").ToArray();
                return Library();
            case "lab-catalog": return LabCatalog.Catalog(profile.LiveDrafts, profile.ShortcutHistory, layout, neutralProject);
            case "lab-start":
                FinishLab();
                if (lab?.Saved == false) return LabState(startBlocked: true);
                var draft = Flag(message, "continue") ? profile.LiveDrafts.LastOrDefault(item =>
                    item.App == Text(message, "app") && item.Level == Number(message, "level") && item.Guidance == Text(message, "guidance")) : null;
                bool combinedLimits = Flag(message, "combinedLimits");
                lab = new LabRun(Text(message, "app"), Text(message, "mode"), Number(message, "level"),
                    Number(message, "seconds", combinedLimits ? 0 : 180), layout, profile.ShortcutHistory,
                    message.TryGetProperty("seed", out _) ? new Random(Number(message, "seed")) : Random.Shared, actionLimit: Number(message, "actionLimit"), guidance: Text(message, "guidance"), dock: dock, layoutKey: windows || string.IsNullOrEmpty(forward) ? DefaultLayoutBinding : forward, missionId: Text(message, "mission"), draft: draft, softFinish: Flag(message, "softFinish"), combinedLimits: combinedLimits, windowsBindings: windows, desktopProfile: windows ? null : desktopProfile, neutralProject: neutralProject);
                persistedLabAttempts = 0;
                labCheckpoint = draft;
                labLimitCheckpointAttempted = draft?.Progress?.LimitDecisionPending == true;
                labFinishAttempted = false;
                labSaveError = null;
                FinishLabAutomatically();
                return LabState();
            case "lab-native-layout":
                if (windows && lab != null && Text(message, "layout") is "EN" or "RU")
                {
                    layout = Text(message, "layout");
                    lab.ApplyNativeLayout(layout, Flag(message, "takeOver"));
                }
                return LabState();
            case "lab-key":
                if (lab == null) throw new InvalidOperationException("Сначала выберите учебный класс.");
                var labAttempt = lab.Input(Text(message, "key"), Flag(message, "pressed"), Text(message, "text"));
                if (lab.Finished) FinishLabAutomatically();
                else if (lab.AwaitingLimitChoice) CheckLabLimit();
                else if (labAttempt != null) SaveLabCheckpoint();
                return LabState();
            case "lab-state": CheckLabLimit(); FinishLabAutomatically(); return LabState();
            case "lab-hint":
                CheckLabLimit();
                if (lab?.AwaitingLimitChoice != true) lab?.Hint();
                return LabState();
            case "lab-limit-choice":
                if (lab == null || !lab.ChooseLimit(Text(message, "choice"))) return LabState();
                if (lab.Finished) FinishLab();
                else
                {
                    SaveLabCheckpoint();
                    if (labSaveError != null) lab.CancelContinuation();
                }
                return LabState();
            case "lab-pause":
                lab?.Pause();
                if (lab?.Finished == true) FinishLabAutomatically();
                else if (lab?.HasDraft == true && !lab.Saved)
                {
                    if (lab.AwaitingLimitChoice)
                    {
                        lab.HoldForLimit();
                        labLimitCheckpointAttempted = true;
                    }
                    SaveLabCheckpoint();
                }
                return LabState();
            case "lab-resume":
                CheckLabLimit();
                lab?.Resume();
                if (lab != null && !lab.Finished && !lab.Paused && !lab.Saved) labFinishAttempted = false;
                return LabState();
            case "lab-stop": lab?.Stop(); FinishLab(); return LabState();
            case "lab-result":
                CheckLabLimit();
                FinishLabAutomatically();
                if (lab?.LimitFlowInProgress == true || lab?.Finished == true && !lab.Saved) return null;
                return lab?.Result(profile.LabSessions, profile.LabRecords);
            case "library": return Library();
            case "inspect": return Describe(Gesture.Parse(Text(message, "keys")), Text(message, "fingers"));
            case "shortcut-save": SaveShortcut(message); return Library();
            case "shortcut-delete": DeleteShortcut(Text(message, "id")); return Library();
            case "shortcut-hide": HideShortcut(Text(message, "id"), Flag(message, "hide")); return Library();
            case "shortcut-import": ImportShortcuts(Text(message, "id")); return Library();
            case "typing-save-set": SaveSet(message); return Library();
            case "typing-delete-set": DeleteSet(Text(message, "id")); return Library();
            case "settings": return Settings(message);
            case "shortcut-start": return StartShortcut(message);
            case "shortcut-input-mode":
                if (run == null) throw new InvalidOperationException("Сначала начните тренировку.");
                run.SetInputMode(Text(message, "mode"), Number(message, "stepToken", run.StepToken));
                return ShortcutState();
            case "shortcut-key":
                if (run == null) throw new InvalidOperationException("Сначала начните тренировку.");
                string key = Text(message, "key");
                if (key.Length == 0) return ShortcutState();
                var recorded = run.Input(key, Flag(message, "pressed"), Text(message, "text"));
                if (recorded != null) Persist(recorded);
                return ShortcutState();
            case "shortcut-result": return run?.Result();
            case "shortcut-state": return ShortcutState();
            case "shortcut-pause": run?.Pause(); return ShortcutState();
            case "shortcut-resume": run?.Resume(); return ShortcutState();
            case "typing-start":
                FinishTyping();
                typing = new TypingRun(Text(message, "mode"), Number(message, "level"), Number(message, "round", -1),
                    Number(message, "seconds"), profile, Random.Shared);
                return TypingState(message);
            case "typing-key":
                typing?.Enter(Text(message, "text"));
                verifyProfile = typing?.Finished == true;
                break;
            case "typing-state": verifyProfile = typing?.Finished == true; break;
            case "typing-pause": typing?.Pause(); break;
            case "typing-resume": typing?.Resume(); break;
            case "typing-stop": FinishTyping(); break;
            case "typing-result": return typing?.Result(Text(message, "layout"));
            case "ping": return "ok";
            default: throw new ArgumentException("Неизвестная команда.");
        }
        if (verifyProfile) FinishTyping();
        return TypingState(message);
    }

    private object LabState(bool startBlocked = false)
    {
        if (lab == null) return null;
        return new { lab.App, lab.Mode, lab.Level, task = lab.Task.Snapshot(), scene = lab.Scene.Snapshot(), held = lab.Held,
            lab.LastKey, lab.Status, lab.Feedback, lab.Errors, lab.Solved, lab.Block, index = lab.Solved % 20 + 1,
            lab.Seconds, lab.Remaining, lab.TimeLimit, lab.ActionLimit, lab.Guidance, lab.ShowGuide, lab.Finished, lab.Paused, lab.Saved, startBlocked, saveError = labSaveError, needsCapture = true,
            limitDecisionPending = lab.AwaitingLimitChoice, finishingMission = lab.FinishingMission, limitReached = lab.LimitReached,
            reportArtifact = lab.ReportArtifact,
            mission = lab.MissionId, phase = lab.PhaseTitle, objectives = lab.Objectives, scenario = lab.ScenarioTitle, scenarioStep = lab.ScenarioStep, scenarioLength = lab.ScenarioLength,
            guide = !lab.ShowGuide || lab.Task.Keys.Length == 0 ? null : Describe(Gesture.Parse(lab.Task.Keys), "") };
    }

    private void CheckLabLimit()
    {
        if (lab?.AwaitingLimitChoice != true) return;
        lab.HoldForLimit();
        if (labLimitCheckpointAttempted) return;
        labLimitCheckpointAttempted = true;
        SaveLabCheckpoint();
    }

    private void FinishLabAutomatically()
    {
        // Polling keeps the scene available for retry instead of repeatedly blocking on the same failed write.
        if (lab?.Finished == true && !labFinishAttempted) FinishLab();
    }

    internal void FinishLab()
    {
        if (lab == null || lab.Saved) return;
        labFinishAttempted = true;
        lab.Pause();
        lab.HoldForLimit();
        var record = lab.SessionRecord();
        bool hasOldDraft = profile.LiveDrafts.Any(item => item.App == lab.App && item.Level == lab.Level && item.Guidance == lab.Guidance);
        // An elapsed-only timed session has no credits but its previous
        // checkpoint must still be retired if this attempt reached its limit.
        if (!lab.HasDraft && record.Solved + record.Errors == 0 && !lab.LimitAccepted && !(record.Completed && hasOldDraft))
        { lab.MarkSaved(); return; }
        SaveLabCheckpoint(lab.LimitFlowInProgress ? null : record);
        if (store.Error == null) lab.MarkSaved();
    }

    // The profile is atomically replaced before returning a successful acknowledgement.
    // Use the attempt's existing write cadence, plus explicit pause; do not sync every keystroke.
    private void SaveLabCheckpoint(LabSessionRecord record = null)
    {
        var pending = lab.Attempts.Skip(persistedLabAttempts).ToArray();
        var draft = lab.HasDraft ? lab.Draft() : null;
        if (draft != null) draft.Progress.Revision = Guid.NewGuid().ToString("N");
        var previousProfile = profile;
        Save(current =>
        {
            bool SameSlot(LiveDraft item) => item.App == lab.App && item.Level == lab.Level && item.Guidance == lab.Guidance;
            var previousDraft = current.LiveDrafts.LastOrDefault(SameSlot);
            // Two engines can restore the same durable attempt. Only the one
            // whose snapshot still matches may add credits to that attempt.
            if (labCheckpoint != null && (previousDraft == null || (labCheckpoint.Progress != null
                ? previousDraft.Progress?.Revision != labCheckpoint.Progress.Revision || previousDraft.Progress?.Session != labCheckpoint.Progress.Session
                : previousDraft.Progress != null || previousDraft.Mission != labCheckpoint.Mission || previousDraft.Trace != labCheckpoint.Trace)))
                throw new IOException("Занятие уже изменилось в другом окне. Перезапустите тренажёр и продолжите из сохранённой версии.");
            current.LiveDrafts.RemoveAll(item => SameSlot(item));
            if (draft != null) current.LiveDrafts.Add(draft);
            current.ShortcutHistory.AddRange(pending);
            TrimShortcutHistory(current);
            if (record != null && (record.Solved + record.Errors > 0 || lab.LimitAccepted)) LabSessionRecords.Update(current, record);
        });
        labSaveError = store.Error;
        if (labSaveError == null)
        {
            persistedLabAttempts += pending.Length;
            labCheckpoint = draft;
        }
        else profile = previousProfile; // a failed disk write must not expose speculative credits or drafts
    }

    private Skill[] Skills() => ShortcutCatalog.All(profile, layout, forward, backward);

    private object Library()
    {
        var history = profile.ShortcutHistory.ToLookup(item => item.Skill);
        return new
        {
            layout, forward, backward,
            skills = Skills().Select(skill => new { skill, editable = !skill.BuiltIn, score = ShortcutScore.From(history[skill.Id]) }),
            keyboard = TypingKeyboard.Rows.Select(row => row.Select(cap => new
            {
                key = ShortcutCatalog.KeyName(cap), cap.Units, cap.Finger, cap.Anchor,
                en = cap.English, ru = cap.Russian, enShift = cap.EnglishShift, ruShift = cap.RussianShift
            })),
            liveSkills = profile.ShortcutHistory.Where(a => a.Mission != null).GroupBy(a => a.Skill).Select(group =>
            {
                var samples = group.Where(a => a.Layout == layout && a.Independent && !a.Assisted).TakeLast(10).ToArray();
                string id = group.Key[(group.Key.IndexOf('/', 4) + 1)..];
                var successes = samples.Where(a => a.FirstTry).ToArray();
                return new { group.Key, title = LabCatalog.Shortcuts.FirstOrDefault(t => t.Id == id)?.Title ?? id.Replace("command/", ""),
                    count = group.Count(), accuracy = ShortcutScore.AccuracyOf(group), milliseconds = ShortcutScore.From(group).Median,
                    independent = group.Count(a => a.Independent && !a.Assisted && a.Correct), hints = group.Count(a => a.Independent && a.Assisted),
                    confirmed = samples.Length == 10 && successes.Length >= 9 && successes.Select(a => a.Mission).Distinct().Count() >= 3
                        && successes.Select(a => a.Session).Distinct().Count() >= 2 };
            }),
            typing = TypingRun.Catalog(profile),
            typingHistory = profile.Results.Where(item => item.Activity is "LinuxTyping" or "TypingPractice")
                .TakeLast(60).Reverse().Select(item => new
            {
                item.Lesson, item.Date, item.Speed, item.Accuracy, item.Count, item.Errors, item.Seconds
            }),
            profile.ShowKeys, profile.ShowHands, profile.ShowMistakeKey, profile.RightHandChord,
            profile.KeyboardProfile, profile.FnMode
        };
    }

    private object StartShortcut(JsonElement message)
    {
        var ids = message.GetProperty("ids").EnumerateArray().Select(value => value.GetString()).ToHashSet();
        bool study = Flag(message, "studied");
        var studied = profile.ShortcutHistory.Where(item => item.Correct && !item.IsSubstitute).Select(item => item.Skill).ToHashSet();
        Skill[] selection = Skills().Where(skill => !skill.Hidden && ids.Contains(skill.Id) && (!study || studied.Contains(skill.Id))).ToArray();
        if (study && selection.Length < 2)
            throw new ArgumentException("Для случайной серии сначала выполните хотя бы два разных упражнения.");
        int tier = Math.Clamp(Number(message, "level"), 0, 2);
        if (selection.Length > 0) tier = Math.Max(tier, selection.Min(skill => skill.Tier));
        run = new ShortcutRun(selection, tier, Flag(message, "auto"), study, Number(message, "limit", 10), () => profile.ShortcutHistory);
        return ShortcutState();
    }

    private object ShortcutState()
    {
        if (run == null) return null;
        return new
        {
            run.Skill, run.Status, run.Feedback, run.Solved, run.Limit, run.Tier, run.LastKey, run.MistakeKey, run.Held, run.Finished,
            run.NeedsSystem, run.Activity, run.ExitKey, run.PreviewKey, scene = run.SceneRow, run.VerifiedScene, run.Step,
            run.StepToken, run.InputMode, run.TargetKeys, run.InputKeys, run.SubstituteKeys, run.Paused, run.Seconds,
            guide = run.Guide is { } guide ? Describe(guide, run.Skill.Fingers) : null,
            score = ShortcutScore.From(run.Attempts),
            lifetime = ShortcutScore.From(profile.ShortcutHistory.Where(item => item.Skill == run.Skill.Id))
        };
    }

    private object Settings(JsonElement message)
    {
        string savedForward = windows && message.TryGetProperty("forward", out _)
            ? WindowsLayoutBinding(Text(message, "forward")) : null;
        string savedBackward = windows && message.TryGetProperty("backward", out _)
            ? WindowsLayoutBinding(Text(message, "backward")) : null;
        Save(current =>
        {
            if (message.TryGetProperty("keys", out var keys)) current.ShowKeys = keys.GetBoolean();
            if (message.TryGetProperty("hands", out var hands)) current.ShowHands = hands.GetBoolean();
            if (message.TryGetProperty("mistakeKeys", out var mistakeKeys)) current.ShowMistakeKey = mistakeKeys.GetBoolean();
            if (message.TryGetProperty("keyboardProfile", out var keyboardProfile))
            {
                string value = keyboardProfile.GetString();
                if (value is not ("matebook-d16" or "standard")) throw new ArgumentException("Неизвестный профиль клавиатуры.");
                current.KeyboardProfile = value;
            }
            if (message.TryGetProperty("fnMode", out var fnMode))
            {
                string value = fnMode.GetString();
                if (value is not ("hotkeys" or "functions")) throw new ArgumentException("Выберите режим Fn: hotkeys или functions.");
                current.FnMode = value;
            }
            if (message.TryGetProperty("paired", out var paired)) current.RightHandChord = paired.GetBoolean();
            if (savedForward != null) current.WindowsLayoutForward = savedForward;
            if (savedBackward != null) current.WindowsLayoutBackward = savedBackward;
        });
        if (savedForward != null) forward = WindowsLayoutBinding(profile.WindowsLayoutForward);
        if (savedBackward != null) backward = WindowsLayoutBinding(profile.WindowsLayoutBackward);
        return Library();
    }

    private void UseStoredWindowsLayoutBindings()
    {
        forward = WindowsLayoutBinding(profile.WindowsLayoutForward);
        backward = WindowsLayoutBinding(profile.WindowsLayoutBackward);
    }

    private static string WindowsLayoutBinding(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DefaultLayoutBinding;
        if (!Gesture.TryParse(text, out var gesture) || gesture.Chords.Length != 1)
            throw new ArgumentException("Введите одно сочетание для смены раскладки, например Win+Space.");
        return gesture.Label;
    }

    private object Describe(Gesture gesture, string fingers)
    {
        var overrides = ReadFingers(fingers);
        string[][] choices = gesture.Chords.Select(chord => chord.Tokens.SelectMany(token =>
            KeyChord.Families.Contains(token) ? KeyChord.ModifierFamilies.Where(pair => pair.Value == token && pair.Key != "RSuper").Select(pair => pair.Key) : [token])
            .ToArray()).ToArray();
        return new
        {
            keys = gesture.Label,
            steps = gesture.Chords.Select(chord => chord.Tokens.Select(token =>
            {
                string key = ShortcutCatalog.SuggestedSide(token, chord, overrides, profile.RightHandChord);
                int finger = key == "RCtrl" && ShortcutCatalog.PairOnRight(chord, overrides, profile.RightHandChord)
                    ? 6 : ShortcutCatalog.Finger(key);
                return new { token, key, finger = overrides.GetValueOrDefault(key, finger) };
            })),
            choices = choices.SelectMany(choice => choice).Distinct().Select(key => new
                { key, finger = overrides.GetValueOrDefault(key, ShortcutCatalog.Finger(key)), custom = overrides.ContainsKey(key) })
        };
    }

    private void SaveShortcut(JsonElement message)
    {
        string id = Text(message, "id");
        var existing = profile.Shortcuts.Find(item => item.Id == id && id.StartsWith("own/"));
        if (id.Length > 0 && existing == null) throw new ArgumentException("Доступно редактирование личных сочетаний.");
        var title = Bounded(Text(message, "title"), 80, "Введите название до 80 символов.");
        var group = Bounded(Text(message, "group"), 50, "Введите группу до 50 символов.");
        var gesture = Gesture.Parse(Text(message, "keys"));
        string fingers = Text(message, "fingers");
        var overrides = ReadFingers(fingers);
        if (overrides.Count > 36 || overrides.Keys.Any(key => !gesture.Chords.Any(chord =>
            chord.Tokens.Any(token => KeyChord.Fits(token, key)))))
            throw new ArgumentException("Оставьте подсказки для клавиш текущего сочетания.");
        var updated = new SavedShortcut { Id = existing?.Id ?? "own/" + Guid.NewGuid().ToString("N"),
            Title = title, Group = group, Gesture = gesture.Label, Fingers = fingers, Hidden = existing?.Hidden ?? false };
        Save(current =>
        {
            current.Shortcuts.RemoveAll(item => item.Id == updated.Id);
            current.Shortcuts.Add(updated);
        });
    }

    private void DeleteShortcut(string id)
    {
        if (!id.StartsWith("own/")) throw new ArgumentException("Встроенную комбинацию можно скрыть.");
        Save(current => current.Shortcuts.RemoveAll(item => item.Id == id));
    }

    private void HideShortcut(string id, bool hide)
    {
        if (!Skills().Any(item => item.Id == id)) throw new ArgumentException("Сочетание не найдено.");
        Save(current =>
        {
            var item = current.Shortcuts.Find(entry => entry.Id == id);
            if (item != null) item.Hidden = hide;
            else if (hide) current.Shortcuts.Add(new SavedShortcut { Id = id, Hidden = true });
        });
    }

    private void ImportShortcuts(string id)
    {
        var set = profile.PracticeSets.Find(item => item.Id == id && item.Kind == PracticeSetKind.Shortcuts);
        if (set == null) throw new ArgumentException("Набор не найден.");
        var values = (set.Content ?? "").Split('\n').Where(line => line.Trim().Length > 0).Select(line =>
        {
            string[] fields = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[1].Length == 0) throw new ArgumentException("Проверьте строки исходного набора.");
            return new SavedShortcut
            {
                Id = "own/" + Guid.NewGuid().ToString("N"), Gesture = Gesture.Parse(fields[0]).Label,
                Title = fields[1], Group = set.Name, Fingers = ""
            };
        }).ToArray();
        Save(current =>
        {
            foreach (var item in values)
                if (!current.Shortcuts.Any(old => old.Gesture == item.Gesture && old.Title == item.Title && old.Group == item.Group))
                    current.Shortcuts.Add(item);
        });
    }

    private void SaveSet(JsonElement message)
    {
        string id = Text(message, "id");
        var existing = profile.PracticeSets.Find(item => item.Id == id && item.Kind != PracticeSetKind.Shortcuts);
        var set = new PracticeSet
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Name = Bounded(Text(message, "title"), 70, "Введите название до 70 символов."),
            Kind = Flag(message, "symbols") ? PracticeSetKind.Symbols : PracticeSetKind.Text,
            Content = CodeText.Normalize(Bounded(Text(message, "content"), 12000, "Введите текст до 12 000 символов.", false))
        };
        string error = PracticeSetInput.Validate(set);
        if (error != null) throw new ArgumentException(error);
        Save(current =>
        {
            current.PracticeSets.RemoveAll(item => item.Id == set.Id);
            current.PracticeSets.Add(set);
        });
    }

    private void DeleteSet(string id) => Save(current => current.PracticeSets.RemoveAll(item => item.Id == id && item.Kind != PracticeSetKind.Shortcuts));

    private void Persist(ShortcutAttempt attempt) => Save(current =>
    {
        current.ShortcutHistory.Add(attempt);
        TrimShortcutHistory(current);
    });

    private static void TrimShortcutHistory(StudioProfile current)
    {
        if (current.ShortcutHistory.Count > 5000) current.ShortcutHistory.RemoveRange(0, current.ShortcutHistory.Count - 5000);
    }

    internal void FinishTyping()
    {
        typing?.Pause();
        if (typing?.CanSave != true) return;
        Save(typing.Save);
        if (store.Error == null) typing.Saved();
    }

    private object TypingState(JsonElement message) => typing?.State(Text(message, "layout") == "RU" ? "RU" : "EN");

    private void Save(Action<StudioProfile> change)
    {
        profile = store.Update(current =>
        {
            TrainingProgress.Migrate(current, DateTime.Now);
            change(current);
        });
    }

    private static string ValidBinding(string text) => Gesture.TryParse(text, out var gesture) ? gesture.Label : "";
    private static string Text(JsonElement json, string key) => json.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
    private static bool Flag(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.GetBoolean();
    private static int Number(JsonElement json, string key, int fallback = 0) => json.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
    private static string Bounded(string value, int limit, string error, bool trim = true)
    {
        if (trim) value = value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit) throw new ArgumentException(error);
        return value;
    }

    private static Dictionary<string, int> ReadFingers(string value)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string pair in (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = pair.LastIndexOf('=');
            if (split <= 0 || !int.TryParse(pair[(split + 1)..], out int finger) || finger is < 0 or > 9)
                throw new ArgumentException("Проверьте назначение пальцев.");
            result[pair[..split]] = finger;
        }
        return result;
    }
}
