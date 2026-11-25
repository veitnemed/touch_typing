using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace TouchTypingStudio
{
    public class StudioProfile
    {
        public string LabDesktopEnvironment;
        public bool Configured;
        public bool F2Seen;
        public bool ShowKeys = true;
        public bool ShowHands = true;
        public bool LiveShowKeys;
        public bool LiveShowHands;
        public bool ShowMistakeKey = true;
        public string KeyboardProfile = "matebook-d16";
        public string FnMode = "hotkeys";
        public bool RightHandChord = true;
        public string WindowsLayoutForward, WindowsLayoutBackward;
        public string CustomText;
        public string CustomName;
        public string SelectedLesson = "home-motion";
        public string PythonLesson;
        public int AdaptivePythonLevel;
        public List<PracticeSet> PracticeSets = new List<PracticeSet>();
        public List<PracticeRecord> PracticeRecords = new List<PracticeRecord>();
        public List<TypingModeBest> TypingModeBests = new List<TypingModeBest>();
        public List<SavedShortcut> Shortcuts = new List<SavedShortcut>();
        public List<ShortcutAttempt> ShortcutHistory = new List<ShortcutAttempt>();
        public List<LiveDraft> LiveDrafts = new List<LiveDraft>();
        public List<LabSessionRecord> LabSessions = new List<LabSessionRecord>();
        public List<LabSessionRecord> LabRecords = new List<LabSessionRecord>();
        // Versioned payload owned by the packaged practice engine; older XML profiles simply lack it.
        public string ClassicTypingDraft;
        public int TypingCourse;
        public double CardX = double.NaN;
        public double CardY = double.NaN;
        public TypingDraft Draft;
        public List<TypingDraft> Drafts = new List<TypingDraft>();
        public List<string> GoodPasses = new List<string>();
        public List<string> CompletedRoutes = new List<string>();
        public List<TypingResult> Results = new List<TypingResult>();
        public List<KeyMistakes> KeyHistory = new List<KeyMistakes>();
        public List<DayNote> Notes = new List<DayNote>();
        public List<RecallDate> RecallDates = new List<RecallDate>();
        public List<RouteResult> RouteResults = new List<RouteResult>();
        public TrainingOptions Training = new TrainingOptions();
        public List<MotorSample> Motor = new List<MotorSample>();
        public List<BestRun> Records = new List<BestRun>();
        public List<Medal> Awards = new List<Medal>();
        public List<string> StableKeys = new List<string>();
        public List<string> Exams = new List<string>();
        public int ProgressionVersion;
        public string Theme;
        public DateTime ChallengeStart;
        public int ChallengeMinutes = 45;
        public List<ChallengeTask> ChallengePlan = new List<ChallengeTask>();
        public List<ChallengeNote> ChallengeNotes = new List<ChallengeNote>();

        public TypingDraft FindDraft(string id)
        {
            return Drafts.Find(d => d.Lesson == id) ?? (Draft != null && Draft.Lesson == id ? Draft : null);
        }

        public void SaveDraft(string id, TypingDraft draft)
        {
            if (Draft != null && Draft.Lesson == id) Draft = null;
            Drafts.RemoveAll(d => d.Lesson == id);
            if (draft != null) Drafts.Add(draft);
        }
    }

    public class RecallDate
    {
        public string Lesson;
        public DateTime Due;
        public int Checks;
    }

    public class RouteResult
    {
        public string Route;
        public int Stage;
        public double Seconds;
        public bool Assisted;
        public DateTime Date;
        public string Context;
    }

    public class DayNote
    {
        public int Day;
        public string Text;
    }

    public class KeyMistakes
    {
        public string Layout;
        public int Symbol;
        public int Attempts;
        public int Errors;
        public DateTime LastMistake;
    }

    public class TypingResult
    {
        public string Lesson;
        public string Language;
        public DateTime Date;
        public int Count;
        public int Errors;
        public double Seconds;
        public string Weak;
        public string Activity;
        public string Task;
        public string Region;
        public bool Independent;
        public bool Completed = true;
        public bool Qualified;
        public int FirstMs;
        public string Context;
        public TypingRecordConditions TypingConditions;
        [XmlIgnore] public double Accuracy { get { return Count + Errors == 0 ? 0 : (100.0 * Count) / (Count + Errors); } }
        [XmlIgnore] public int Speed { get { return Seconds <= 0 ? 0 : (int)Math.Round(60 * Count / Seconds); } }
    }

    public class TypingDraft
    {
        public string Lesson;
        public string Target;
        public int Variant;
        public int Position;
        public int Errors;
        public double Seconds;
        public List<Mistake> Mistakes = new List<Mistake>();
        public List<Stroke> Strokes = new List<Stroke>();
        public bool HintsUsed;
        public int FirstMs;
    }

    public class Mistake
    {
        public int Position;
        public int Count;
    }

    internal sealed class ProfileStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(StudioProfile));
        private readonly string path;
        private readonly Mutex gate;
        public readonly string Root;
        public string Error { get; private set; }

        public ProfileStore(bool verification, string dataRoot = null)
        {
            string qaRoot = verification ? Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT") : null;
            if (!string.IsNullOrEmpty(qaRoot))
            {
                bool windowsAbsolute = qaRoot.StartsWith(@"\\", StringComparison.Ordinal) || qaRoot.StartsWith("//", StringComparison.Ordinal) ||
                    (qaRoot.Length >= 3 && qaRoot[1] == ':' && (qaRoot[2] == '\\' || qaRoot[2] == '/'));
                if (!Path.IsPathRooted(qaRoot) || (Path.DirectorySeparatorChar == '\\' && !windowsAbsolute))
                    throw new ArgumentException("Папка профиля проверки должна иметь абсолютный путь.");
                dataRoot = Path.GetFullPath(qaRoot);
            }
            Root = Path.Combine(dataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                verification ? "TouchTypingStudio-QA" : "TouchTypingStudio");
            path = Path.Combine(Root, "progress.xml");
            gate = new Mutex(false, verification ? @"Local\TouchTypingStudio.QA.Profile" : @"Local\TouchTypingStudio.Profile");
        }

        public StudioProfile Read()
        {
            try
            {
                Error = null;
                if (!File.Exists(path)) return new StudioProfile();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return (StudioProfile)Serializer.Deserialize(stream);
            }
            catch (Exception e)
            {
                if (!IsStorageError(e)) throw;
                Error = "Не удалось прочитать профиль. " + e.Message;
            }
            return new StudioProfile();
        }

        // Also allows the two entry shortcuts to update the same profile independently.
        public StudioProfile Update(Action<StudioProfile> update)
        {
            bool locked = false;
            StudioProfile current = null;
            try
            {
                try { locked = gate.WaitOne(1000); }
                catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new IOException("Профиль занят. Повторите действие.");
                current = Read();
                if (Error != null)
                {
                    // Keep the original file for recovery instead of overwriting it.
                    return current;
                }
                update(current);
                Directory.CreateDirectory(Root);
                var temporary = path + ".writing";
                using (var stream = File.Create(temporary)) Serializer.Serialize(stream, current);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception e)
            {
                if (!IsStorageError(e)) throw;
                Error = "Не удалось сохранить прогресс. " + e.Message;
            }
            finally { if (locked) gate.ReleaseMutex(); }
            if (current != null) return current;
            // A fallback read must not turn a failed update into a successful acknowledgement.
            string updateError = Error;
            current = Read();
            if (updateError != null) Error = updateError;
            return current;
        }

        private static bool IsStorageError(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException;
        }
    }

    internal enum Layout { Neutral, English, Russian, Other }

    internal sealed class LessonStage
    {
        public readonly int Start;
        public readonly string Caption;

        public LessonStage(int start, string caption) { Start = start; Caption = caption; }
    }

    internal sealed class Lesson
    {
        public const int CurrentCourse = 5;
        public readonly string Id, Title, Skill, Source, FocusKeys;
        public readonly Layout Language;
        public readonly string[] Rounds;
        public int Number, CourseSize;
        public int CheckRounds = 4;
        public LessonStage[] Opening = new LessonStage[0];
        public int PreviousIntroRounds; // 4.x kept each introductory sequence in a separate round.

        public Lesson(string id, string title, string skill, string source, string focusKeys, Layout language, params string[] rounds)
        {
            Id = id;
            Title = title;
            Skill = skill;
            Source = source;
            FocusKeys = focusKeys;
            Language = language;
            Rounds = rounds;
        }

        public bool Recall(int variant) { return variant >= Rounds.Length - CheckRounds; }
        public string OpeningPhase(int variant, int position)
        {
            if (variant != 0) return "";
            for (int i = Opening.Length - 1; i >= 0; i--)
                if (Opening[i].Start <= position) return Opening[i].Caption;
            return "";
        }

        public string Phase(int variant, int position)
        {
            if (Recall(variant)) return "Самостоятельный набор";
            string opening = OpeningPhase(variant, position);
            if (opening.Length > 0) return opening;
            return variant >= 14 ? "Смешанная практика" : "Набор фрагмента";
        }
        public string PassId(int variant) { return "course" + CurrentCourse + "/" + Id + "/" + variant; }
        public int NextRound(StudioProfile profile)
        {
            for (int i = 0; i < Rounds.Length; i++)
                if (!profile.GoodPasses.Contains(PassId(i))) return i;
            return 0;
        }

        public int GoodRounds(StudioProfile profile)
        {
            return Enumerable.Range(0, Rounds.Length).Count(i => profile.GoodPasses.Contains(PassId(i)));
        }
    }

    internal sealed class TypingAttempt
    {
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Dictionary<int, int> mistakes = new Dictionary<int, int>();
        private readonly Dictionary<int, Stroke> strokes = new Dictionary<int, Stroke>();
        private readonly StringBuilder editedText;
        private double previousSeconds;
        private int lastMs = -1, startedAt;

        public readonly string Target;
        public int Position { get; private set; }
        public int Errors { get; private set; }
        public bool Running { get { return clock.IsRunning; } }
        public bool Finished { get { return Position == Target.Length; } }
        public char Expected { get { return Finished ? '\0' : Target[Position]; } }
        public bool HasRepairs { get { return editedText != null; } }
        public string Written { get { return editedText == null ? Target.Substring(0, Position) : editedText.ToString(); } }
        public char LastWritten { get { return editedText == null || editedText.Length == 0 ? '\0' : editedText[editedText.Length - 1]; } }
        public double Seconds { get { return previousSeconds + clock.Elapsed.TotalSeconds; } }
        public double Accuracy { get { return Position + Errors == 0 ? 0 : (100.0 * Position) / (Position + Errors); } }
        public int FirstMs { get; private set; }
        public bool HintsUsed { get; private set; }

        public TypingAttempt(string target, TypingDraft draft, bool allowCompletedDraft = false)
        {
            Target = target;
            if (target.IndexOf(CodeText.Erase) >= 0) editedText = new StringBuilder();
            if (draft == null || draft.Target != target || draft.Position < 0 || draft.Position > target.Length ||
                (!allowCompletedDraft && draft.Position == target.Length)) return;
            Position = draft.Position;
            Errors = draft.Errors;
            previousSeconds = draft.Seconds;
            HintsUsed = draft.HintsUsed;
            FirstMs = draft.FirstMs;
            if (editedText != null) AppendThrough(0, Position);
            foreach (var error in draft.Mistakes)
                if (error.Position >= 0 && error.Position < Target.Length) mistakes[error.Position] = error.Count;
            foreach (var stroke in draft.Strokes)
                if (stroke.Position >= 0 && stroke.Position <= Position) strokes[stroke.Position] = stroke;
        }

        public void Start()
        {
            if (Finished || Running) return;
            lastMs = -1; // Ignore the pause in the next interval.
            startedAt = (int)(Seconds * 1000);
            clock.Start();
        }
        public void Pause() { clock.Stop(); }
        public void ShowGuidance() { HintsUsed = true; }

        public bool Enter(char value, string locale = null)
        {
            if (!Running || Finished) return false;
            // The visible ⌫ in a lesson is an action, not a character the user can type.
            if (value == CodeText.Erase) return false;
            if (value == '\b' && Expected == CodeText.Erase) value = CodeText.Erase;
            int count = value == Expected ? 1 : value == '\t' && CanIndent() ? 4 : 0;
            int elapsed = (int)(Seconds * 1000);
            if (count > 0)
            {
                if (Position == 0) FirstMs = Math.Max(1, elapsed - startedAt);
                int interval = lastMs < 0 ? 0 : Math.Max(1, elapsed - lastMs);
                for (int offset = 0; offset < count; offset++)
                    strokes[Position + offset] = new Stroke { Position = Position + offset, Locale = locale,
                        ElapsedMs = elapsed, IntervalMs = offset == 0 ? interval : 0 };
                lastMs = elapsed;
                if (editedText != null) AppendThrough(Position, Position + count);
                Position += count;
                if (Finished) Pause();
                return true;
            }
            Errors++;
            if (!strokes.ContainsKey(Position)) strokes[Position] = new Stroke { Position = Position, Locale = locale };
            int times;
            mistakes.TryGetValue(Position, out times);
            mistakes[Position] = times + 1;
            return false;
        }

        public Stroke StrokeAt(int position)
        {
            Stroke stroke;
            return strokes.TryGetValue(position, out stroke) ? stroke : null;
        }

        public int ErrorsAt(int position)
        {
            int count;
            return mistakes.TryGetValue(position, out count) ? count : 0;
        }

        public List<int> Trace()
        {
            return Enumerable.Range(0, Position).Where(strokes.ContainsKey).Select(i => strokes[i].ElapsedMs).ToList();
        }

        private void AppendThrough(int start, int stop)
        {
            for (int i = start; i < stop; i++)
            {
                if (Target[i] != CodeText.Erase) editedText.Append(Target[i]);
                else if (editedText.Length > 0) editedText.Length--;
            }
        }

        public bool CanIndent()
        {
            if (Position + 4 > Target.Length || string.CompareOrdinal(Target, Position, "    ", 0, 4) != 0)
                return false;
            for (int i = Position - 1; i >= 0 && Target[i] != '\n'; i--)
                if (Target[i] != ' ') return false;
            return true;
        }

        public string ReviewText()
        {
            var lines = new SortedSet<int>();
            foreach (int i in mistakes.Keys)
                lines.Add(i == 0 ? 0 : Target.LastIndexOf('\n', i - 1) + 1);
            var snippets = new List<string>();
            foreach (int start in lines.Take(8))
            {
                int end = Target.IndexOf('\n', start);
                string line = Target.Substring(start, (end >= 0 ? end : Target.Length) - start);
                snippets.Add(line.Length > 0 ? line : "\n");
            }
            return string.Join("\n", snippets);
        }

        public string WeakKeys()
        {
            return string.Join("   ", mistakes.GroupBy(e => Target[e.Key])
                .Select(g => new { Key = g.Key, Count = g.Sum(e => e.Value) }).OrderByDescending(g => g.Count)
                .Take(5).Select(x => CodeText.Display(x.Key) + " ×" + x.Count));
        }

        public string WeakPairs()
        {
            return string.Join("   ", mistakes.Where(e => e.Key > 0)
                .GroupBy(e => Target.Substring(e.Key - 1, 2))
                .Select(g => new { Pair = g.Key, Count = g.Sum(x => x.Value) }).OrderByDescending(g => g.Count)
                .Take(3).Select(x => CodeText.Display(x.Pair) + " ×" + x.Count));
        }

        public TypingDraft Draft(string lesson, int round)
        {
            return new TypingDraft
            {
                Lesson = lesson, Variant = round, Target = Target, Position = Position, Errors = Errors,
                Seconds = Seconds, Mistakes = mistakes.Select(x => new Mistake { Position = x.Key, Count = x.Value }).ToList(),
                Strokes = strokes.Values.ToList(), HintsUsed = HintsUsed, FirstMs = FirstMs
            };
        }
    }

    internal static class CodeText
    {
        public const char Erase = '\u232B';
        public static string VisibleScript(string target) { return target.Replace(Erase.ToString(), " ⌫ "); }
        public static string Normalize(string text)
        {
            text = (text ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
            if (!text.Contains("\t")) return text;
            var output = new StringBuilder();
            int column = 0;
            foreach (char c in text)
            {
                if (c == '\t')
                {
                    int spaces = 4 - column % 4;
                    output.Append(' ', spaces);
                    column += spaces;
                }
                else
                {
                    output.Append(c);
                    column = c == '\n' ? 0 : column + 1;
                }
            }
            return output.ToString();
        }

        public static Layout Language(char value)
        {
            if ((value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z')) return Layout.English;
            if ((value >= 'а' && value <= 'я') || (value >= 'А' && value <= 'Я') || value == 'ё' || value == 'Ё' || value == '№')
                return Layout.Russian;
            return Layout.Neutral;
        }

        public static string LanguageName(Layout value)
        {
            return value == Layout.English ? "EN" : value == Layout.Russian ? "RU" : value == Layout.Neutral ? "EN / RU" : "Другая";
        }

        public static string LanguageOfText(string text)
        {
            bool en = text.Any(c => Language(c) == Layout.English);
            bool ru = text.Any(c => Language(c) == Layout.Russian);
            return ru ? (en ? "EN / RU" : "RU") : "EN";
        }

        public static string Display(char c)
        {
            switch (c)
            {
                case ' ': return "·";
                case '\n': return "↵";
                case '\t': return "⇥";
                case '\b': return "⌫";
                case '\0': return "";
                default: return c.ToString();
            }
        }

        public static string Display(string text) { return string.Concat(text.Select(Display)); }

        public static string ReadImport(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 262144) throw new IOException("Файл больше 256 КБ. Вставьте нужный фрагмент.");
            if (!new[] { ".py", ".txt" }.Contains(info.Extension, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Выберите файл .py или .txt.");
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                    return reader.ReadToEnd();
            }
            catch (DecoderFallbackException) { return File.ReadAllText(path, Encoding.GetEncoding(1251)); }
        }
    }
}
