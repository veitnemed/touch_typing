using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio
{
    internal enum ContinuousKind { Python, Russian, English, Symbols, Custom }

    internal sealed class ContinuousTypingSnapshot
    {
        public readonly long Count, Errors;
        public readonly double Seconds, Accuracy;
        public readonly KeyValuePair<char, long>[] TopMistakes;
        public double Cpm { get { return Seconds > 0 ? 60.0 * Count / Seconds : 0; } }
        public double Wpm { get { return Cpm / 5.0; } }

        public ContinuousTypingSnapshot(long count, long errors, double seconds, double accuracy,
            KeyValuePair<char, long>[] topMistakes)
        {
            Count = count;
            Errors = errors;
            Seconds = seconds;
            Accuracy = accuracy;
            TopMistakes = topMistakes;
        }
    }

    internal class ContinuousTyping
    {
        private static readonly string[] PythonLevels =
        {
            "Вызовы и присваивания", "Коллекции и индексы", "Функции и условия",
            "Выражения и исправления", "Вложенные конструкции"
        };
        private static readonly string[] GeneralLevels = { "Базовый", "Средний", "Сложный" };
        private static readonly string[] CustomLevel = { "Свой набор" };
        private readonly Random random;
        private readonly int goal;
        private readonly string[] custom;
        private Func<Random, string>[] blocks;
        private sealed class Miss
        {
            public int Count;
            public string Fragment;
        }

        private readonly Dictionary<string, Miss> mistakes = new Dictionary<string, Miss>();
        private readonly Dictionary<char, long> totalMistakes = new Dictionary<char, long>();
        private readonly HashSet<int> recordedMisses = new HashSet<int>();
        private readonly List<TypingAttempt> pending = new List<TypingAttempt>();
        private readonly List<int> remainingBlocks = new List<int>();
        private long typed, errors, erasures;
        private double completedSeconds;
        private int goodBlocks;
        private string lastStandard;

        public ContinuousKind Kind { get; private set; }
        public bool AutoAdvance { get; private set; }
        public int Level { get; private set; }
        public TypingAttempt Attempt { get; private set; }
        public string Prefix { get; private set; }
        public string Focus { get; private set; }
        public int Goal { get { return goal; } }
        public long Inputs { get { return typed + errors + Attempt.Position + Attempt.Errors; } }
        public double Accuracy { get { return Inputs == 0 ? 0 : 100.0 * (typed + Attempt.Position) / Inputs; } }
        public string Weakest { get { return Focus ?? NextWeakness(); } }
        public bool HasPendingProgress { get { return pending.Count > 0; } }
        internal long AcceptedUnits { get { return typed + Attempt.Position; } }
        internal IReadOnlyDictionary<char, long> MistakeCounts { get { return totalMistakes; } }
        internal string NextStandardBlock() { return StandardBlock(); }
        public string Name
        {
            get
            {
                switch (Kind)
                {
                    case ContinuousKind.Python: return "Python";
                    case ContinuousKind.Russian: return "Русский текст";
                    case ContinuousKind.English: return "Английский текст";
                    case ContinuousKind.Symbols: return "Спецсимволы";
                    default: return "Свой текст";
                }
            }
        }
        public Layout Layout
        {
            get
            {
                if (Kind == ContinuousKind.Russian) return Layout.Russian;
                return Kind == ContinuousKind.Custom ? Layout.Neutral : Layout.English;
            }
        }
        public string Heading { get { return Name + "  ›  " + LevelNames(Kind)[Level]; } }

        public static string[] Levels(ContinuousKind kind) { return (string[])LevelNames(kind).Clone(); }

        private static string[] LevelNames(ContinuousKind kind)
        {
            if (kind == ContinuousKind.Python) return PythonLevels;
            return kind == ContinuousKind.Custom ? CustomLevel : GeneralLevels;
        }

        public ContinuousTyping(Random random, ContinuousKind kind, int level, int goal, IEnumerable<string> custom = null,
            bool autoAdvance = false, bool authored = false)
        {
            if (random == null) throw new ArgumentNullException("random");
            this.random = random;
            Kind = kind;
            AutoAdvance = autoAdvance && kind == ContinuousKind.Python;
            Level = Math.Max(0, Math.Min(level, LevelNames(kind).Length - 1));
            this.goal = goal;
            this.custom = (custom ?? Enumerable.Empty<string>()).Select(CodeText.Normalize)
                .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct().ToArray();
            string validation = authored ? null : ValidateSnippets(kind, this.custom);
            if (validation != null) throw new ArgumentException(validation, "custom");
            Prefix = "";
            SelectBlocks();
            BeginFragment(StandardBlock(), false);
        }

        public static string ValidateSnippets(ContinuousKind kind, IEnumerable<string> snippets)
        {
            var normalized = (snippets ?? Enumerable.Empty<string>()).Select(CodeText.Normalize)
                .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (kind == ContinuousKind.Custom && normalized.Length == 0)
                return "Введите хотя бы одну строку для тренировки.";
            foreach (string line in normalized)
                foreach (char value in line)
                    if (!TypingKeyboard.Supports(value))
                        return "Символ «" + CodeText.Display(value) + "» отсутствует в раскладках EN и RU.";
            return null;
        }

        public ContinuousTyping Restart()
        {
            return new ContinuousTyping(random, Kind, Level, goal, custom, AutoAdvance);
        }

        public ContinuousTypingSnapshot Snapshot()
        {
            var topMistakes = totalMistakes.OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key).Take(5).ToArray();
            return new ContinuousTypingSnapshot(typed + Attempt.Position - erasures,
                errors + Attempt.Errors, completedSeconds + Attempt.Seconds, Accuracy, topMistakes);
        }

        public bool Enter(char input)
        {
            int position = Attempt.Position;
            int errorCount = Attempt.Errors;
            char expected = Attempt.Expected;
            Layout expectedLanguage = CodeText.Language(expected);
            var keyHint = TypingKeyboard.Hint(expected, expectedLanguage == Layout.Neutral ? Layout : expectedLanguage);
            Layout keyLayout = keyHint == null ? Layout : keyHint.Layout;
            bool accepted = Attempt.Enter(input, TrainingProgress.Locale(keyLayout));
            Layout inputLanguage = CodeText.Language(input);
            bool wrongLayout = Kind != ContinuousKind.Custom && inputLanguage != Layout.Neutral && inputLanguage != keyLayout;
            if (accepted && expected == CodeText.Erase) erasures++;
            if (Attempt.Errors > errorCount)
            {
                long missed;
                totalMistakes.TryGetValue(expected, out missed);
                totalMistakes[expected] = missed + 1;
                if (!wrongLayout && recordedMisses.Add(position)) RecordMiss(Attempt.Target, position);
            }
            if (Attempt.Finished) NextFragment();
            return accepted;
        }

        private void RecordMiss(string text, int position)
        {
            char key = text[position];
            if (key == ' ') return;
            string symbol = key.ToString();
            string focus = symbol;
            if (position > 0 && !char.IsWhiteSpace(text[position - 1]) && text[position - 1] != CodeText.Erase && key != '\n')
                focus = text[position - 1] + symbol;
            Miss miss;
            if (!mistakes.TryGetValue(focus, out miss))
            {
                miss = new Miss();
                mistakes.Add(focus, miss);
            }
            miss.Count++;
            miss.Fragment = text;
        }

        private string NextWeakness()
        {
            return mistakes.OrderByDescending(m => m.Value.Count).Select(m => m.Key).FirstOrDefault();
        }

        private void NextFragment()
        {
            var completed = Attempt;
            pending.Add(completed);
            if (pending.Count > 128) pending.RemoveAt(0);
            typed += completed.Position;
            errors += completed.Errors;
            completedSeconds += completed.Seconds;
            Prefix += completed.Written;
            if (Prefix.Length > 160) Prefix = Prefix.Substring(Prefix.Length - 160);

            bool wasFocused = Focus != null;
            if (!wasFocused)
                goodBlocks = completed.Accuracy >= goal ? goodBlocks + 1 : 0;
            else if (completed.Accuracy < goal)
                goodBlocks = 0;

            string weakness = wasFocused ? null : NextWeakness();
            if (weakness != null)
            {
                string example = mistakes[weakness].Fragment;
                mistakes.Remove(weakness);
                Focus = weakness;
                BeginFragment(FocusedBlock(weakness, example), true);
                return;
            }

            if (AutoAdvance && goodBlocks >= 3 && Level < LevelNames(Kind).Length - 1)
            {
                Level++;
                goodBlocks = 0;
                SelectBlocks();
            }

            Focus = null;
            BeginFragment(StandardBlock(), true);
        }

        private void SelectBlocks()
        {
            switch (Kind)
            {
                case ContinuousKind.Python:
                    blocks = PracticeCorpus.Python(Level);
                    break;
                case ContinuousKind.Russian:
                case ContinuousKind.English:
                    blocks = PracticeCorpus.Prose(Kind, Level);
                    break;
                case ContinuousKind.Symbols:
                    blocks = PracticeCorpus.SymbolBlocks(Level, custom);
                    break;
                default:
                    blocks = PracticeCorpus.AsBlocks(custom);
                    break;
            }
            remainingBlocks.Clear();
        }

        private string StandardBlock()
        {
            if (remainingBlocks.Count == 0)
                remainingBlocks.AddRange(Enumerable.Range(0, blocks.Length));
            int choice = random.Next(remainingBlocks.Count);
            string result = FinishLine(blocks[remainingBlocks[choice]](random));
            for (int attempt = 0; result == lastStandard && attempt < 3; attempt++)
            {
                if (remainingBlocks.Count > 1)
                    choice = (choice + 1) % remainingBlocks.Count;
                result = FinishLine(blocks[remainingBlocks[choice]](random));
            }
            remainingBlocks.RemoveAt(choice);
            lastStandard = result;
            return result;
        }

        private string FocusedBlock(string focus, string previous)
        {
            // Try fresh variants first. The just-finished fragment always contains the missed sequence.
            string best = previous;
            int bestScore = 0;
            int variations = Kind == ContinuousKind.Python ? 4 : Kind == ContinuousKind.Symbols ? 16 : 1;
            foreach (var block in blocks)
            {
                for (int sample = 0; sample < variations; sample++)
                {
                    string candidate = FinishLine(block(random));
                    if (candidate == previous) continue;
                    int count = Count(candidate, focus);
                    if (count == 0) continue;
                    if (count > bestScore)
                    {
                        best = candidate;
                        bestScore = count;
                    }
                }
            }
            return best;
        }

        private string FinishLine(string text)
        {
            if (Kind == ContinuousKind.Python || text.IndexOf('\n') >= 0)
                return text.EndsWith("\n", StringComparison.Ordinal) ? text : text + "\n";
            return text.EndsWith(" ", StringComparison.Ordinal) ? text : text + " ";
        }

        private static int Count(string text, string focus)
        {
            int total = 0;
            for (int next = 0; next < text.Length;)
            {
                int index = text.IndexOf(focus, next, StringComparison.Ordinal);
                if (index < 0) break;
                total++;
                next = index + focus.Length;
            }
            return total;
        }

        private void BeginFragment(string text, bool start)
        {
            recordedMisses.Clear();
            Attempt = new TypingAttempt(text, null);
            if (start) Attempt.Start();
        }

        public void SaveProgress(StudioProfile profile, DateTime now)
        {
            if (!HasPendingProgress) return;
            var result = new TypingResult
            {
                Lesson = Name,
                Activity = "Continuous" + Kind, Date = now, Task = "continuous-" + Kind,
                Count = pending.Sum(segment => segment.Position),
                Errors = pending.Sum(segment => segment.Errors),
                Seconds = pending.Sum(segment => segment.Seconds)
            };
            result.Language = CodeText.LanguageOfText(string.Concat(pending.Select(segment => segment.Target)));
            result.Region = Kind == ContinuousKind.Python ? "python" :
                Kind == ContinuousKind.Symbols ? "symbols" : result.Language == "RU" ? "ru" : "qwerty";
            result.Qualified = result.Accuracy >= goal;
            if (Kind == ContinuousKind.Python) profile.AdaptivePythonLevel = Level;
            profile.Results.Add(result);
            if (profile.Results.Count > 500) profile.Results.RemoveRange(0, profile.Results.Count - 500);
            foreach (var segment in pending)
                TrainingProgress.SaveMotor(profile, segment, false, now, result.Task);
        }

        public void ProgressSaved() { pending.Clear(); }
    }

    // Allows profiles and earlier adaptive Python call sites to keep their five-step progression.
    internal sealed class AdaptivePython : ContinuousTyping
    {
        public PythonDrillLevel Drill { get { return PythonDrills.CodeLevels[Level]; } }

        public AdaptivePython(Random random, int level, int goal)
            : base(random, ContinuousKind.Python, level, goal, autoAdvance: true)
        {
        }
    }
}
