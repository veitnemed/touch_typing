using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record ProjectExercise(string Path, string Name, int Tier, string Code);

internal sealed partial class TypingRun
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly ProjectExercise[] Project = JsonSerializer.Deserialize<ProjectExercise[]>(
        typeof(TypingRun).Assembly.GetManifestResourceStream("TouchTyping.Engine.piling-python.json")!, Json);
    private readonly Lesson lesson;
    private readonly int variant;
    private readonly ContinuousTyping continuous;
    private readonly string title, source, tag, recordMaterial;
    private readonly ClassicTypingPlan classic;
    private readonly ContinuousKind kind;
    private readonly int level;
    internal string AttemptId { get; } = Guid.NewGuid().ToString("N");
    internal int CycleCount => classic?.Lengths.Length ?? 0;
    internal int? PlannedUnits => classic?.Total;
    internal bool CompletedPlan => classic?.Finished == true;
    internal IReadOnlyDictionary<char, long> MistakeCounts => classic?.MistakeCounts ?? continuous.MistakeCounts;
    private readonly int timeLimit;
    private bool saved;
    private readonly List<TypingPoint> samples = [];
    private sealed record TypingPoint(int Seconds, int Speed, int Errors);
    internal bool LastCorrect { get; private set; } = true;
    internal TypingAttempt Attempt => classic?.Attempt ?? continuous.Attempt;
    internal bool Finished => CompletedPlan || (timeLimit > 0 && Snapshot.Seconds >= timeLimit);
    private ContinuousTypingSnapshot Snapshot => classic?.Snapshot() ?? continuous.Snapshot();
    internal bool CanSave => !saved && (Snapshot.Count > 0 || Snapshot.Errors > 0);
    internal bool IsRunning => !saved && Attempt.Running && !Finished;
    internal bool HasFiniteDraft => classic != null && !Finished && !saved && (classic.Count != 0 || classic.MistakeCounts.Count > 0);
    internal bool IsSaved => saved;
    internal static object Catalog(StudioProfile profile) => new
    {
        draft = DraftHeader(profile.ClassicTypingDraft) ?? MixedTypingRun.DraftSummary(profile.ClassicTypingDraft),
        actionModes = MixedTypingRun.Modes.Select(p => new { id = p.Key, title = p.Value }).ToArray(),
        lessons = Lessons.All.Select(item => new { id = "lesson:" + item.Id, title = item.Title, language = CodeText.LanguageName(item.Language) }),
        projects = NeutralProject.Exercises.Select(item => new { id = "project:" + item.Name, title = item.Name, source = item.Path }),
        sets = profile.PracticeSets.Where(item => item.Kind != PracticeSetKind.Shortcuts)
            .Select(item => new { id = "set:" + item.Id, title = item.Name, content = item.Content, kind = item.Kind.ToString() }),
        oldShortcuts = profile.PracticeSets.Where(item => item.Kind == PracticeSetKind.Shortcuts)
            .Select(item => new { id = item.Id, title = item.Name, content = item.Content })
    };

    internal TypingRun(string mode, int level, int requestedRound, int seconds, StudioProfile profile, Random random,
        int cycles = 1, bool infinite = false)
    {
        tag = mode;
        recordMaterial = TypingModeRecords.Material(profile, mode);
        timeLimit = Math.Clamp(seconds, 0, 3600);
        this.level = level = Math.Clamp(level, 0, 2);
        kind = ContinuousKind.Custom;
        string[] snippets = null;
        if (mode.StartsWith("lesson:"))
        {
            lesson = Lessons.Find(mode[7..]) ?? throw new ArgumentException("Урок не найден.");
            variant = requestedRound < 0 ? TrainingProgress.Variant(lesson, profile) : Math.Clamp(requestedRound, 0, lesson.Rounds.Length - 1);
            title = lesson.Title;
            source = lesson.Skill;
            snippets = [lesson.Rounds[variant]];
        }
        else
        switch (mode)
        {
            case "Russian": title = "Русские фразы"; kind = ContinuousKind.Russian; break;
            case "English": title = "Английские фразы"; kind = ContinuousKind.English; break;
            case "Symbols": title = "Спецсимволы"; kind = ContinuousKind.Symbols; break;
            case "WordsRU":
            case "WordsEN":
                bool russian = mode == "WordsRU";
                title = russian ? "Русский словарь" : "Английский словарь";
                var words = Enumerable.Range(0, level + 1).SelectMany(index =>
                        PracticeCorpus.Prose(russian ? ContinuousKind.Russian : ContinuousKind.English, index))
                    .SelectMany(block => block(random).ToLowerInvariant().Split(' '))
                    .Select(word => new string(word.Where(char.IsLetter).ToArray()))
                    .Where(word => word.Length >= (level == 2 ? 4 : 2)).Distinct().ToArray();
                snippets = Enumerable.Range(0, 28).Select(_ => string.Join(" ",
                    Enumerable.Range(0, 8 + level * 2).Select(_ => words[random.Next(words.Length)]))).ToArray();
                break;
            case "Project":
                title = "Python · task-list";
                source = "Добавление, поиск и сохранение списка задач";
                snippets = NeutralProject.Exercises.Where(item => item.Tier == level).Select(item => item.Code).ToArray();
                break;
            case "WeakRU":
            case "WeakEN":
                bool ru = mode == "WeakRU";
                var language = ru ? "RU" : "EN";
                title = $"Работа над промахами · {language}";
                var weak = TrainingProgress.Weak(profile, language, DateTime.Now)
                    .Where(item => item.Sequence.All(TypingKeyboard.Supports)).Take(6).ToArray();
                if (weak.Length == 0) throw new ArgumentException("Нужно сохранить тренировку с промахами в этой раскладке.");
                snippets = Enumerable.Range(0, 16).Select(_ => string.Join(" ",
                    Enumerable.Range(0, 16).Select(_ => weak[random.Next(weak.Length)].Sequence))).ToArray();
                break;
            default:
                if (mode.StartsWith("project:"))
                {
                    var item = NeutralProject.Exercises.Concat(Project).FirstOrDefault(block => block.Name == mode[8..])
                        ?? throw new ArgumentException("Фрагмент проекта не найден.");
                    title = item.Name;
                    source = item.Path;
                    snippets = [item.Code];
                }
                else if (mode.StartsWith("set:"))
                {
                    var set = profile.PracticeSets.Find(item => item.Id == mode[4..] && item.Kind != PracticeSetKind.Shortcuts)
                        ?? throw new ArgumentException("Набор не найден.");
                    title = set.Name;
                    snippets = PracticeSetInput.Fragments(set);
                    if (set.Kind == PracticeSetKind.Symbols) kind = ContinuousKind.Symbols;
                }
                else throw new ArgumentException("Выберите упражнение.");
                break;
        }
        var generator = new ContinuousTyping(random, kind, level, profile.Training.Goal, snippets, authored: lesson != null);
        if (infinite) continuous = generator;
        else
        {
            string first = generator.Attempt.Target;
            classic = ClassicTypingPlan.Build(cycles, random, () =>
            {
                if (first == null) return generator.NextStandardBlock();
                string next = first; first = null;
                return next;
            });
        }
        Attempt.Start();
    }

    internal void Enter(string text)
    {
        if (!IsRunning || text.Length != 1) return;
        var character = text[0];
        if (classic != null) LastCorrect = classic.Enter(character, PreferredLayout);
        else LastCorrect = continuous.Enter(character);
        if (Finished) Attempt.Pause();
    }

    internal void Pause() => Attempt.Pause();
    internal void Resume() { if (!saved && !Finished) Attempt.Start(); }

    internal void Save(StudioProfile profile)
    {
        if (!CanSave) return;
        var now = DateTime.Now;
        var score = Snapshot;
        TypingModeRecords.PreserveHistory(profile); // Before motor/typing history can rotate.
        if (continuous != null)
        {
            continuous.SaveProgress(profile, now);
            if (Attempt.Position + Attempt.Errors > 0)
                TrainingProgress.SaveMotor(profile, Attempt, false, now, tag);
        }
        else classic.SaveMotor(profile, now, tag);
        // A short budget that covers only part of a long authored round is practice,
        // not evidence that the entire original motor lesson was completed.
        if (lesson != null)
            TrainingProgress.OnLesson(profile, lesson, variant,
                (classic == null ? continuous.HasPendingProgress : CompletedPlan && classic.Count >= lesson.Rounds[variant].Length) &&
                score.Accuracy >= profile.Training.Goal, now);
        var item = new TypingResult { Lesson = title, Task = tag, Activity = "TypingPractice", Date = now,
            Count = (int)score.Count, Errors = (int)score.Errors, Seconds = score.Seconds,
            Language = CodeText.LanguageOfText(Attempt.Target), Completed = CompletedPlan || (CycleCount == 0 && timeLimit > 0 && Finished) };
        TypingModeRecords.Record(profile, item, level, CycleCount, timeLimit, lesson == null ? -1 : variant, material: recordMaterial);
        profile.Results.Add(item);
        if (profile.Results.Count > 500) profile.Results.RemoveRange(0, profile.Results.Count - 500);
        if (PracticeRecords.CanRecordTyping(score)) PracticeRecords.Record(profile, "linux-typing/" + tag, score.Cpm, score.Accuracy);
    }

    internal void Saved() => saved = true;

    private Layout PreferredLayout => lesson?.Language ?? (kind == ContinuousKind.Russian ? Layout.Russian :
        kind == ContinuousKind.Custom ? Layout.Neutral : Layout.English);

    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    internal string Checkpoint()
    {
        if (!HasFiniteDraft) return null;
        return JsonSerializer.Serialize(new ClassicRunCheckpoint("classic-v1", AttemptId, tag, title, source, level, variant,
            timeLimit, kind.ToString(), classic.Cycle, CycleCount, classic.Count, classic.Total, classic.Checkpoint(), recordMaterial), DraftJson);
    }

    internal static TypingRun Continue(string saved)
    {
        if (string.IsNullOrEmpty(saved) || saved.Length > 2_000_000) throw ClassicTypingPlan.InvalidDraft();
        try
        {
            var d = JsonSerializer.Deserialize<ClassicRunCheckpoint>(saved, DraftJson);
            if (d?.Format != "classic-v1" || !Guid.TryParseExact(d.Id, "N", out _) || string.IsNullOrEmpty(d.Mode) ||
                string.IsNullOrEmpty(d.Title) || d.TimeLimit is < 0 or > 3600 || d.Level is < 0 or > 2)
                throw ClassicTypingPlan.InvalidDraft();
            return new TypingRun(d);
        }
        catch (JsonException) { throw ClassicTypingPlan.InvalidDraft(); }
    }

    private TypingRun(ClassicRunCheckpoint d)
    {
        if (!Enum.TryParse<ContinuousKind>(d.Kind, out kind) || !Enum.IsDefined(kind)) throw ClassicTypingPlan.InvalidDraft();
        tag = d.Mode; title = d.Title; source = d.Source; level = d.Level; AttemptId = d.Id;
        recordMaterial = d.RecordMaterial;
        timeLimit = d.TimeLimit; variant = d.Round;
        lesson = tag.StartsWith("lesson:", StringComparison.Ordinal) ? Lessons.Find(tag[7..]) : null;
        if (lesson != null && (variant < 0 || variant >= lesson.Rounds.Length)) throw ClassicTypingPlan.InvalidDraft();
        classic = ClassicTypingPlan.Restore(d.Plan);
        if (classic.Total != d.Total || classic.Count != d.Done || classic.Cycle != d.Cycle || CycleCount != d.Cycles)
            throw ClassicTypingPlan.InvalidDraft();
        // Explicit continuation is a learner action; time spent with the application closed does not count.
        Attempt.Start();
    }

    internal static object DraftHeader(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 2_000_000) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var d = doc.RootElement;
            if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String || format.GetString() != "classic-v1") return null;
            string Txt(string key) => d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int Num(string key) => d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
            var id = Txt("id"); var mode = Txt("mode"); var title = Txt("title");
            var done = Num("done"); var total = Num("total"); var cycle = Num("cycle"); var cycles = Num("cycles");
            var timeLimit = Num("timeLimit"); var level = Num("level");
            if (!Guid.TryParseExact(id, "N", out _) || string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(title) ||
                cycles is < 1 or > ClassicTypingPlan.MaxCycles || cycle < 1 || cycle > cycles ||
                total < cycles * ClassicTypingPlan.MinUnits || total > cycles * ClassicTypingPlan.MaxUnits ||
                done < 0 || done >= total || timeLimit is < 0 or > 3600 || level is < 0 or > 2) return null;
            return new { id, mode, title, done, total, cycle, cycles, timeLimit, level };
        }
        catch (JsonException) { return null; }
    }

    internal object Result(string layout)
    {
        UpdateTimeline(Snapshot);
        return new { state = State(layout), series = samples.ToArray() };
    }

    private void UpdateTimeline(ContinuousTypingSnapshot score)
    {
        if (score.Count + score.Errors == 0) return;
        int second = (int)score.Seconds;
        var point = new TypingPoint(second, (int)Math.Round(score.Cpm), (int)score.Errors);
        if (samples.Count == 0 || samples[^1].Seconds != second)
            samples.Add(point);
        else
            samples[^1] = point;
    }

    internal object State(string layout)
    {
        var score = Snapshot;
        UpdateTimeline(score);
        var target = Attempt.Target;
        int position = Attempt.Position;
        Layout requested = CodeText.Language(Attempt.Expected);
        if (requested == Layout.Neutral)
            requested = PreferredLayout;
        if (requested == Layout.Neutral || kind == ContinuousKind.Custom && lesson == null)
            requested = CodeText.Language(Attempt.Expected) == Layout.Neutral
                ? (layout == "RU" ? Layout.Russian : Layout.English) : CodeText.Language(Attempt.Expected);
        var hint = TypingKeyboard.Hint(Attempt.Expected, requested);
        return new
        {
            id = AttemptId, mode = tag, level, title, source, before = (classic?.Prefix ?? continuous?.Prefix ?? "") + target[..position], after = target[position..],
            progressDone = classic?.Count ?? continuous?.AcceptedUnits ?? 0L, progressTotal = PlannedUnits,
            progressFraction = classic == null ? 0 : (double)classic.Count / classic.Total,
            progressVisible = classic != null, infinite = classic == null,
            cycle = classic?.Cycle ?? 0, cycles = CycleCount, cycleLengths = classic?.Lengths ?? [],
            // This compatibility alias must not expose fragment-local resets to older clients.
            fragmentProgress = classic == null ? 0 : (double)classic.Count / classic.Total,
            expected = CodeText.Display(Attempt.Expected),
            wantedLayout = hint == null ? layout : CodeText.LanguageName(hint.Layout),
            key = hint == null ? "" : ShortcutCatalog.KeyName(hint.Cap),
            modifier = hint == null || hint.Modifier == LinuxKey.None ? "" : hint.Modifier == LinuxKey.LeftShift ? "LShift" : "RShift",
            finger = hint?.Cap.Finger, written = Attempt.HasRepairs ? Attempt.Written : "",
            count = score.Count, errors = score.Errors, accuracy = Math.Round(score.Accuracy, 1),
            cpm = Math.Round(score.Cpm), wpm = Math.Round(score.Wpm), seconds = (int)score.Seconds, timeLimit,
            topMistakes = score.TopMistakes.Select(item => new { key = CodeText.Display(item.Key), count = item.Value }),
            running = IsRunning, finished = Finished, correct = LastCorrect, weak = continuous?.Focus,
            lesson = lesson?.Id, round = variant + 1, rounds = lesson?.Rounds.Length ?? 0
        };
    }
}
