using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record ProjectExercise(string Path, string Name, int Tier, string Code);

internal sealed class TypingRun
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ProjectExercise[] Project = JsonSerializer.Deserialize<ProjectExercise[]>(
        typeof(TypingRun).Assembly.GetManifestResourceStream("TouchTyping.Engine.piling-python.json")!, Json);
    private readonly Lesson lesson;
    private readonly int variant;
    private readonly ContinuousTyping continuous;
    private readonly string title, source, tag;
    private readonly TypingAttempt attempt;
    private readonly int timeLimit;
    private bool saved;
    private readonly List<TypingPoint> samples = [];
    private sealed record TypingPoint(int Seconds, int Speed, int Errors);
    internal bool LastCorrect { get; private set; } = true;
    internal TypingAttempt Attempt => continuous?.Attempt ?? attempt;
    internal bool Finished => (lesson != null && Attempt.Finished) || (timeLimit > 0 && Snapshot.Seconds >= timeLimit);
    private ContinuousTypingSnapshot Snapshot => continuous?.Snapshot() ?? new ContinuousTypingSnapshot(Attempt.Position,
        Attempt.Errors, Attempt.Seconds, Attempt.Accuracy, []);
    internal bool CanSave => !saved && (Snapshot.Count > 0 || Snapshot.Errors > 0);
    internal bool IsRunning => Attempt.Running && !Finished;
    internal static object Catalog(StudioProfile profile) => new
    {
        lessons = Lessons.All.Select(item => new { id = "lesson:" + item.Id, title = item.Title, language = CodeText.LanguageName(item.Language) }),
        projects = Project.Select(item => new { id = "project:" + item.Name, title = item.Name, source = item.Path }),
        sets = profile.PracticeSets.Where(item => item.Kind != PracticeSetKind.Shortcuts)
            .Select(item => new { id = "set:" + item.Id, title = item.Name, content = item.Content, kind = item.Kind.ToString() }),
        oldShortcuts = profile.PracticeSets.Where(item => item.Kind == PracticeSetKind.Shortcuts)
            .Select(item => new { id = item.Id, title = item.Name, content = item.Content })
    };

    internal TypingRun(string mode, int level, int requestedRound, int seconds, StudioProfile profile, Random random)
    {
        tag = mode;
        timeLimit = Math.Clamp(seconds, 0, 3600);
        if (mode.StartsWith("lesson:"))
        {
            lesson = Lessons.Find(mode[7..]) ?? throw new ArgumentException("Урок не найден.");
            variant = requestedRound < 0 ? TrainingProgress.Variant(lesson, profile) : Math.Clamp(requestedRound, 0, lesson.Rounds.Length - 1);
            title = lesson.Title;
            source = lesson.Skill;
            attempt = new TypingAttempt(lesson.Rounds[variant], null);
            Attempt.Start();
            return;
        }

        level = Math.Clamp(level, 0, 2);
        var kind = ContinuousKind.Custom;
        string[] snippets = null;
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
                title = "Python · piling-main";
                source = "Сигналы, спектр и направляющие векторы";
                snippets = Project.Where(item => item.Tier == level).Select(item => item.Code).ToArray();
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
                    var item = Project.SingleOrDefault(block => block.Name == mode[8..])
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
        continuous = new ContinuousTyping(random, kind, level, profile.Training.Goal, snippets);
        Attempt.Start();
    }

    internal void Enter(string text)
    {
        if (!IsRunning || text.Length != 1) return;
        var character = text[0];
        if (continuous != null) LastCorrect = continuous.Enter(character);
        else LastCorrect = Attempt.Enter(character, TrainingProgress.Locale(lesson.Language));
        if (Finished) Attempt.Pause();
    }

    internal void Pause() => Attempt.Pause();
    internal void Resume() { if (!Finished) Attempt.Start(); }

    internal void Save(StudioProfile profile)
    {
        if (!CanSave) return;
        var now = DateTime.Now;
        var score = Snapshot;
        if (continuous != null)
        {
            continuous.SaveProgress(profile, now);
            if (Attempt.Position + Attempt.Errors > 0)
                TrainingProgress.SaveMotor(profile, Attempt, false, now, tag);
        }
        else
        {
            TrainingProgress.SaveMotor(profile, Attempt, false, now, tag);
            TrainingProgress.OnLesson(profile, lesson, variant, Attempt.Finished && score.Accuracy >= profile.Training.Goal, now);
        }
        profile.Results.Add(new TypingResult { Lesson = title, Task = tag, Activity = "TypingPractice", Date = now,
            Count = (int)score.Count, Errors = (int)score.Errors, Seconds = score.Seconds,
            Language = CodeText.LanguageOfText(Attempt.Target), Completed = Finished });
        if (profile.Results.Count > 500) profile.Results.RemoveRange(0, profile.Results.Count - 500);
        if (PracticeRecords.CanRecordTyping(score)) PracticeRecords.Record(profile, "linux-typing/" + tag, score.Cpm, score.Accuracy);
    }

    internal void Saved() => saved = true;

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
            requested = continuous?.Layout ?? lesson.Language;
        if (requested == Layout.Neutral || continuous?.Kind == ContinuousKind.Custom)
            requested = CodeText.Language(Attempt.Expected) == Layout.Neutral
                ? (layout == "RU" ? Layout.Russian : Layout.English) : CodeText.Language(Attempt.Expected);
        var hint = TypingKeyboard.Hint(Attempt.Expected, requested);
        return new
        {
            title, source, before = (continuous?.Prefix ?? "") + target[..position], after = target[position..],
            fragmentProgress = target.Length == 0 ? 0 : (double)position / target.Length,
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
