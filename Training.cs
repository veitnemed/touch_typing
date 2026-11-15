using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace TouchTypingStudio
{
    public class TrainingOptions
    {
        public int Accuracy = 95;
        public int GuidedRounds = 2;
        public int FadeFrom = 55;
        public int FirstDays = 1;
        public int LaterDays = 3;
        public int SurvivalErrors = 3;
        public bool ReduceMotion;

        internal int Goal { get { return Math.Max(90, Math.Min(100, Accuracy)); } }
        internal int Delay(int checks)
        {
            int first = Math.Max(1, Math.Min(5, FirstDays));
            int later = Math.Max(first + 1, Math.Min(7, LaterDays));
            return checks == 0 ? first : checks == 1 ? later : checks == 2 ? later * 2 + 1 : later * 4 + 2;
        }
    }

    public class Stroke
    {
        public int Position;
        public string Locale;
        public int ElapsedMs;
        public int IntervalMs;
    }

    public class MotorSample
    {
        public string Locale, Sequence;
        public int Correct, Errors, Intervals, Milliseconds;
        public DateTime Date;
        public bool Independent;
        public string Session;
    }

    public class BestRun
    {
        public string Task, Title, Region;
        public int Count, Errors, Milliseconds;
        public bool Independent;
        public DateTime Date;
        public List<int> Trace = new List<int>();
        internal double Accuracy { get { return 100.0 * Count / Math.Max(1, Count + Errors); } }
        internal int Speed { get { return Milliseconds > 0 ? (int)Math.Round(60000.0 * Count / Milliseconds) : 0; } }
    }

    public class Medal
    {
        public string Id;
        public int Xp;
        public DateTime Date;
    }

    public class ChallengeNote
    {
        public DateTime Campaign;
        public string Task;
        public bool Done;
        public string Text;
    }

    internal enum ExerciseKind { Campaign, Weak, Blind, Memory, Mixed, Ghost, Survival, Python, Shortcuts, Boss, Daily, Free, PythonWords, PythonCode }
    internal enum WaypointKind { Lesson, Route, Branch, Exam }
    internal enum WaypointState { Open, Complete }

    internal sealed class Territory
    {
        public readonly string Id, Name, Tag, Color;
        public readonly Layout Language;
        public readonly string[] Lessons;
        public readonly ExerciseKind Branch;
        public readonly RouteKind[] Trial;

        public Territory(string id, string name, string tag, string color, Layout language, string lessons,
            ExerciseKind branch, params RouteKind[] trial)
        {
            Id = id; Name = name; Tag = tag; Color = color; Language = language; Branch = branch; Trial = trial;
            Lessons = lessons.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public IEnumerable<RouteSpec> Routes
        {
            get
            {
                if (Id == "windows") return RouteSession.Routes.Where(x => !x.InCode);
                if (Id == "vscode") return RouteSession.Routes.Where(x => x.InCode);
                return Enumerable.Empty<RouteSpec>();
            }
        }
    }

    internal sealed class Waypoint
    {
        public readonly Territory Region;
        public readonly WaypointKind Kind;
        public readonly Lesson Lesson;
        public readonly RouteSpec Route;
        public readonly string Title;
        public Waypoint Previous;
        public Waypoint(Territory region, WaypointKind kind, string title, Lesson lesson = null, RouteSpec route = null)
        { Region = region; Kind = kind; Title = title; Lesson = lesson; Route = route; }
        public string Id { get { return Lesson != null ? Lesson.Id : Route != null ? Route.Kind.ToString() : Region.Id + "/" + Kind; } }
    }

    internal static class Atlas
    {
        public static readonly Territory[] Regions =
        {
            new Territory("qwerty", "QWERTY", "ЛАТИНИЦА", "#6FE1DD", Layout.English,
                "home-motion home-rest upper-motion lower-motion reach names qwop xc", ExerciseKind.Ghost),
            new Territory("symbols", "Знаки", "ЦИФРЫ И СИМВОЛЫ", "#F7BF84", Layout.English,
                "plus-motion equals-motion backspace-motion digit-motion apostrophe pairs operators", ExerciseKind.Survival),
            new Territory("python", "Python", "КОД И ОТСТУПЫ", "#94AAF9", Layout.English,
                "assign print list-index call dict attributes function compare for assert comprehension", ExerciseKind.Python),
            new Territory("ru", "ЙЦУКЕН", "КИРИЛЛИЦА", "#BBA6FA", Layout.Russian,
                string.Join(" ", Lessons.Russian.Select(l => l.Id)), ExerciseKind.Memory),
            new Territory("windows", "Windows", "СИСТЕМА БЕЗ МЫШИ", "#79C6A4", Layout.Neutral,
                "", ExerciseKind.Shortcuts, RouteKind.Switch, RouteKind.Explorer, RouteKind.NavigateWords),
            new Territory("vscode", "VS Code", "НАВИГАЦИЯ И ПРАВКИ", "#F398AA", Layout.Neutral,
                "", ExerciseKind.Shortcuts, RouteKind.CodeLine, RouteKind.CodeComment, RouteKind.Python)
        };
        // Mixed is a real existing lesson. Put it on its own branch instead of creating a copy of the script.
        public static readonly Waypoint[] Nodes = MakeNodes();

        private static Waypoint[] MakeNodes()
        {
            var nodes = new List<Waypoint>();
            foreach (var region in Regions)
            {
                var core = region.Lessons.Select(Lessons.Find).Select(l => new Waypoint(region, WaypointKind.Lesson, l.Title, l))
                    .Concat(region.Routes.Select(r => new Waypoint(region, WaypointKind.Route, r.Name, null, r))).ToList();
                for (int i = 1; i < core.Count; i++) core[i].Previous = core[i - 1];
                nodes.AddRange(core);
                nodes.Add(new Waypoint(region, WaypointKind.Branch,
                    region.Id == "qwerty" ? "Гонка с собой" : region.Id == "ru" ? "Вспомнить клавиши" :
                    region.Id == "symbols" ? "Запас ошибок" : region.Id == "python" ? "Код и две раскладки" : "Горячие клавиши",
                    region.Id == "python" ? Lessons.Find("mixed") : null) { Previous = core[0] });
                nodes.Add(new Waypoint(region, WaypointKind.Exam, "Экзамен · " + region.Name) { Previous = core.Last() });
            }
            return nodes.ToArray();
        }

        public static Territory Find(string id) { return Regions.FirstOrDefault(x => x.Id == id) ?? Regions[0]; }
        public static Territory ForLesson(Lesson lesson)
        {
            return lesson == null ? null : Nodes.Where(x => x.Lesson == lesson).Select(x => x.Region).FirstOrDefault();
        }
        public static Territory ForRoute(RouteSpec route) { return Find(route.InCode ? "vscode" : "windows"); }
        public static int Number(Lesson lesson)
        {
            var region = ForLesson(lesson);
            return region == null ? 0 : Array.IndexOf(region.Lessons, lesson.Id) + 1;
        }

        public static WaypointState State(Waypoint node, StudioProfile profile)
        {
            return Completed(node, profile) ? WaypointState.Complete : WaypointState.Open;
        }

        public static bool Completed(Waypoint node, StudioProfile profile)
        {
            if (node.Kind == WaypointKind.Exam) return profile.Exams.Contains(node.Region.Id);
            if (node.Lesson != null) return TrainingProgress.Cleared(node.Lesson, profile);
            if (node.Route != null) return profile.CompletedRoutes.Contains(node.Route.PassId(2));
            return node.Kind == WaypointKind.Branch && profile.Awards.Any(x => x.Id == ("branch/" + node.Region.Id));
        }

        public static Waypoint Next(StudioProfile profile, Territory region)
        {
            return Nodes.FirstOrDefault(n => n.Region == region && n.Kind != WaypointKind.Branch && State(n, profile) == WaypointState.Open);
        }

        public static Waypoint NextAfter(Lesson current, StudioProfile profile)
        {
            var waypoint = Nodes.FirstOrDefault(n => n.Kind == WaypointKind.Lesson && n.Lesson == current);
            if (waypoint == null) return null;
            int index = Array.IndexOf(Nodes, waypoint);
            return Nodes.Skip(index + 1).FirstOrDefault(n => n.Region == waypoint.Region &&
                n.Kind == WaypointKind.Lesson && !Completed(n, profile));
        }

        public static RouteSpec ExamRoute(Territory region, StudioProfile profile)
        {
            return region.Trial.Where((route, index) => !profile.Exams.Contains(region.Id + "/" + index))
                .Select(route => region.Routes.First(x => x.Kind == route)).FirstOrDefault();
        }

        public static void PassRouteExam(StudioProfile p, Territory region, RouteSpec route, DateTime now)
        {
            if (route == null || ExamRoute(region, p) != route) return;
            p.Exams.Add(region.Id + "/" + Array.IndexOf(region.Trial, route.Kind));
            if (ExamRoute(region, p) == null && !p.Exams.Contains(region.Id))
            {
                p.Exams.Add(region.Id);
                TrainingProgress.Earn(p, "exam/" + region.Id, 110, now);
            }
        }
    }

    internal sealed class KeyEvidence
    {
        public string Sequence;
        public int Correct, Errors, Intervals, Milliseconds, Independent, IndependentErrors, RetentionChecks;
        public DateTime Last;
        public double Accuracy { get { return 100.0 * Correct / Math.Max(1, Correct + Errors); } }
        public double Latency { get { return Milliseconds / (double)Math.Max(1, Intervals); } }
        // Shrink small/noisy samples towards the overall error rate.
        public double ErrorWeight { get { return (Errors + .4) / (Correct + Errors + 8.0); } }
    }

    internal static class TrainingProgress
    {
        public static void Migrate(StudioProfile p, DateTime now)
        {
            Lessons.Upgrade(p);
            if (p.ProgressionVersion >= 1) return;
            foreach (var lesson in Lessons.All.Where(l => Cleared(l, p)).ToArray())
            {
                Earn(p, "skill/" + lesson.Id, 45, now);
                if (!p.RecallDates.Any(d => d.Lesson == lesson.Id))
                {
                    var played = p.Results.Where(r => r.Lesson == lesson.Title).Select(r => r.Date).DefaultIfEmpty(now).Max();
                    Lessons.ScheduleRecall(p, lesson, played, false, true);
                }
            }
            p.ProgressionVersion = 1;
        }

        public static bool Pass(StudioProfile profile, double accuracy) { return accuracy >= profile.Training.Goal; }
        public static int GuidedGoal(Lesson lesson, StudioProfile p)
        { return Math.Min(Math.Max(1, lesson.Rounds.Length - lesson.CheckRounds), Math.Max(1, Math.Min(4, p.Training.GuidedRounds))); }

        public static int Guided(Lesson l, StudioProfile p)
        { return Enumerable.Range(0, l.Rounds.Length).Count(i => !l.Recall(i) && p.GoodPasses.Contains(l.PassId(i))); }

        public static bool Cleared(Lesson l, StudioProfile p)
        {
            return p.Awards.Any(x => x.Id == "skill/" + l.Id) ||
                Guided(l, p) >= GuidedGoal(l, p) && Enumerable.Range(0, l.Rounds.Length)
                    .Any(i => l.Recall(i) && p.GoodPasses.Contains(l.PassId(i)));
        }

        public static int Variant(Lesson l, StudioProfile p)
        {
            if (Guided(l, p) >= GuidedGoal(l, p) && !Cleared(l, p))
                for (int i = 0; i < l.Rounds.Length; i++)
                    if (l.Recall(i) && !p.GoodPasses.Contains(l.PassId(i))) return i;
            return l.NextRound(p);
        }

        public static void Earn(StudioProfile p, string id, int xp, DateTime now)
        {
            if (!p.Awards.Any(x => x.Id == id)) p.Awards.Add(new Medal { Id = id, Xp = xp, Date = now });
        }

        public static int Xp(StudioProfile p)
        {
            // Previous course passes remain meaningful even if they predate the award system.
            return p.Awards.Sum(x => x.Xp) +
                Lessons.All.Count(l => Cleared(l, p) && !p.Awards.Any(x => x.Id == "skill/" + l.Id)) * 45 +
                RouteSession.Routes.Count(r => p.CompletedRoutes.Contains(r.PassId(2)) && !p.Awards.Any(x => x.Id == "route/" + r.Kind)) * 50;
        }

        public static int Level(StudioProfile profile) { return 1 + Xp(profile) / 125; }
        public static string Locale(Layout l) { return l == Layout.Russian ? "RU" : "EN"; }

        public static void SaveMotor(StudioProfile p, TypingAttempt attempt, bool independent, DateTime when, string session)
        {
            var groups = new Dictionary<string, MotorSample>();
            string prior = null;
            string previousLocale = null;
            Action<string, string, int, int, int> add = (locale, sequence, correct, errors, ms) =>
            {
                string id = locale + sequence;
                MotorSample sample;
                if (!groups.TryGetValue(id, out sample))
                {
                    sample = new MotorSample { Locale = locale, Sequence = sequence, Date = when, Independent = independent, Session = session };
                    groups.Add(id, sample);
                }
                sample.Correct += correct;
                sample.Errors += errors;
                if (correct > 0 && ms > 0) { sample.Intervals++; sample.Milliseconds += ms; }
            };
            for (int i = 0; i <= attempt.Position && i < attempt.Target.Length; i++)
            {
                Stroke stroke = attempt.StrokeAt(i);
                int correct = i < attempt.Position ? 1 : 0, errors = attempt.ErrorsAt(i);
                if (correct == 0 && errors == 0) break;
                string symbol = attempt.Target[i].ToString();
                string locale = stroke == null || string.IsNullOrEmpty(stroke.Locale)
                    ? Locale(CodeText.Language(attempt.Target[i])) : stroke.Locale;
                int interval = stroke != null && stroke.IntervalMs <= 4000 ? stroke.IntervalMs : 0;
                add(locale, symbol, correct, errors, interval);
                if (prior != null && locale == previousLocale && prior != "\n" && symbol != "\n")
                    add(locale, prior + symbol, correct, errors, interval);
                prior = symbol;
                previousLocale = locale;
            }
            p.Motor.AddRange(groups.Values);
            if (independent)
            {
                foreach (var locale in groups.Values.Select(x => x.Locale).Distinct())
                    foreach (var e in Evidence(p, locale, when).Where(e => e.RetentionChecks > 0))
                    {
                        string id = locale + "/" + e.Sequence;
                        if (!p.StableKeys.Contains(id)) p.StableKeys.Add(id);
                    }
            }
            p.Motor.RemoveAll(x => x.Date.Date < when.Date.AddDays(-90));
            if (p.Motor.Count > 12000) p.Motor.RemoveRange(0, p.Motor.Count - 12000);
        }

        public static KeyEvidence[] Evidence(StudioProfile p, string locale, DateTime now, bool pairs = false)
        {
            var recent = p.Motor.Where(x => x.Locale == locale && x.Sequence != null && x.Sequence.Length == (pairs ? 2 : 1) &&
                x.Date <= now && x.Date >= now.AddDays(-45)).GroupBy(x => x.Sequence);
            var result = new List<KeyEvidence>();
            foreach (var group in recent)
            {
                var independent = group.Where(x => x.Independent).ToArray();
                var days = independent.GroupBy(x => x.Date.Date)
                    .Where(g => g.Sum(x => x.Correct) >= 3 && Pass(p, 100.0 * g.Sum(x => x.Correct) /
                        Math.Max(1, g.Sum(x => x.Correct + x.Errors)))).Select(g => g.Key).OrderBy(x => x).ToArray();
                int retained = 0;
                if (days.Length > 0)
                {
                    var from = days[0];
                    foreach (var date in days.Skip(1)) if ((date - from).Days >= p.Training.Delay(0)) { retained++; from = date; }
                }
                result.Add(new KeyEvidence
                {
                    Sequence = group.Key, Correct = group.Sum(x => x.Correct), Errors = group.Sum(x => x.Errors),
                    Intervals = group.Sum(x => x.Intervals), Milliseconds = group.Sum(x => x.Milliseconds),
                    Independent = independent.Sum(x => x.Correct), IndependentErrors = independent.Sum(x => x.Errors),
                    RetentionChecks = retained, Last = group.Max(x => x.Date)
                });
            }
            if (!pairs)
                foreach (var old in p.KeyHistory.Where(k => k.Layout == locale && k.Symbol > 0 && k.Symbol <= char.MaxValue))
                {
                    string symbol = ((char)old.Symbol).ToString();
                    if (!result.Any(x => x.Sequence == symbol))
                        result.Add(new KeyEvidence { Sequence = symbol, Correct = old.Attempts, Errors = old.Errors, Last = old.LastMistake });
                }
            return result.ToArray();
        }

        public static KeyEvidence[] Weak(StudioProfile p, string locale, DateTime now, bool pairs = false)
        {
            var records = Evidence(p, locale, now, pairs).Where(x => Eligible(x.Sequence)).ToArray();
            double latency = records.Where(x => x.Intervals >= 3).Select(x => x.Latency).DefaultIfEmpty(650).Average();
            return records.Where(x => x.Errors > 0 || x.Intervals >= 3 && x.Latency > latency * 1.25)
                .OrderByDescending(x => 3 * x.ErrorWeight + (x.Intervals < 3 ? 0 : .28 * x.Latency / Math.Max(200, latency)) +
                    (x.IndependentErrors > 0 ? .12 : 0) + (x.RetentionChecks == 0 ? .08 : 0))
                .ThenByDescending(x => x.Last).ToArray();
        }

        private static bool Eligible(string sequence) { return !string.IsNullOrWhiteSpace(sequence) && !sequence.Contains(CodeText.Erase); }

        public static string Mastery(KeyEvidence key, StudioProfile profile)
        {
            if (key == null) return "Не проверено";
            if (key.RetentionChecks > 0) return "Устойчиво";
            if (key.Independent >= 6 && Pass(profile, 100.0 * key.Independent / (key.Independent + key.IndependentErrors)))
                return "Закрепляется";
            return "Изучается";
        }

        public static int MasteryPercent(StudioProfile p, Layout layout, bool allKeys, DateTime now)
        {
            var keys = Evidence(p, Locale(layout), now);
            var caps = TypingKeyboard.Rows.SelectMany(row => row)
                .Where(c => c.Base(layout).Length == 1 && (allKeys ? c.Units >= 1 : CodeText.Language(c.Base(layout)[0]) == layout)).ToArray();
            int known = caps.Count(cap => p.StableKeys.Contains(Locale(layout) + "/" + cap.Base(layout)) ||
                keys.Any(k => k.Sequence == cap.Base(layout) && k.RetentionChecks > 0));
            return 100 * known / Math.Max(1, caps.Length);
        }

        public static void OnLesson(StudioProfile p, Lesson lesson, int variant, bool qualified, DateTime now)
        {
            if (!qualified) return;
            string pass = lesson.PassId(variant);
            if (!p.GoodPasses.Contains(pass)) p.GoodPasses.Add(pass);
            if (!Cleared(lesson, p)) return;
            Earn(p, "skill/" + lesson.Id, 45, now);
            Lessons.ScheduleRecall(p, lesson, now, false, true);
        }

        public static void OnRecall(StudioProfile p, Lesson l, bool qualified, DateTime now)
        {
            var date = p.RecallDates.Find(x => x.Lesson == l.Id);
            if (date == null || date.Due.Date > now.Date) return;
            Lessons.ScheduleRecall(p, l, now, true, qualified);
            if (qualified && date.Checks <= 3) Earn(p, "retained/" + l.Id + "/" + date.Checks, 20, now);
        }

        public static string ThemeTitle(StudioProfile p)
        { return p.Theme == "solar" && Level(p) >= 5 ? "Солнечный ветер" : p.Theme == "aurora" && Level(p) >= 2 ? "Аврора" : "Глубокий космос"; }

        public static string[] Palette(StudioProfile p)
        {
            if (ThemeTitle(p) == "Аврора") return new[] { "#091E25", "#102A34", "#85E8CD" };
            if (ThemeTitle(p) == "Солнечный ветер") return new[] { "#211822", "#35232A", "#FFBE8B" };
            return new[] { "#0C1523", "#162339", "#A8DEF2" };
        }

        public static BestRun Best(StudioProfile p, string task, bool independent, bool trace = false)
        {
            return p.Records.LastOrDefault(x => x.Task == task && x.Independent == independent && (!trace || x.Trace.Count == x.Count));
        }

        public static bool SaveRecord(StudioProfile p, TypingResult item, TypingAttempt attempt, bool trace)
        {
            if (!item.Completed || item.Count < 70 || item.Seconds < 2 || item.Activity == ExerciseKind.Free.ToString()) return false;
            string task = item.Task;
            var previous = Best(p, task, item.Independent);
            // Comparable: same exact text and assistance. Improve accuracy, then pace at that accuracy.
            if (previous != null && (item.Errors > previous.Errors ||
                item.Errors == previous.Errors && item.Seconds * 1000 > previous.Milliseconds - 100)) return false;
            var sample = new BestRun
            {
                Task = task, Title = item.Lesson, Region = item.Region, Date = item.Date, Count = item.Count, Errors = item.Errors,
                Milliseconds = (int)(item.Seconds * 1000), Independent = item.Independent
            };
            if (trace) sample.Trace = attempt.Trace();
            p.Records.Add(sample);
            if (p.Records.Count > 600) p.Records.RemoveRange(0, p.Records.Count - 600);
            return previous != null;
        }
    }

    internal sealed class Exercise
    {
        public ExerciseKind Kind;
        public string Title, Text, Region, Instruction, Task;
        public Layout Layout;
        public bool Independent;
        public bool FixedGuidance;
        public int ErrorLimit;
        public BestRun Ghost;
        public Lesson Recall;
        public PythonDrillLevel DrillLevel;
        public string Branch;
        public string Context;

        public bool Stops(TypingAttempt attempt)
        { return ErrorLimit > 0 && !attempt.Finished && attempt.Errors >= ErrorLimit; }

        public int GhostPosition(double seconds)
        {
            if (Ghost == null || Ghost.Trace.Count == 0) return 0;
            int lo = 0, hi = Ghost.Trace.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Ghost.Trace[mid] <= 1000 * seconds) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    internal sealed class PracticeSuggestion
    {
        public ExerciseKind Mode;
        public string Region, Reason;
    }

    internal static class ExerciseBank
    {
        public static PracticeSuggestion Recommend(StudioProfile p, Territory region, DateTime now)
        {
            var due = Lessons.NextDue(p, now);
            if (due != null)
                return new PracticeSuggestion { Mode = ExerciseKind.Daily, Region = Atlas.ForLesson(due).Id,
                    Reason = "Подошёл срок: «" + due.Title + "»." };
            if (region.Trial.Length > 0)
                return new PracticeSuggestion { Mode = ExerciseKind.Shortcuts, Region = region.Id,
                    Reason = "Повторите знакомый маршрут в новом учебном файле." };
            var recent = p.Results.Where(x => x.Region == region.Id && x.Date >= now.AddDays(-14)).Reverse().Take(4).ToArray();
            string locale = TrainingProgress.Locale(region.Language);
            var weak = TrainingProgress.Weak(p, locale, now);
            if (weak.Length > 0 && recent.Take(3).Count(x => !x.Completed || !TrainingProgress.Pass(p, x.Accuracy)) >= 2)
                return new PracticeSuggestion { Mode = ExerciseKind.Weak, Region = region.Id,
                    Reason = "В последних попытках были ошибки. Начните с «" + CodeText.Display(weak[0].Sequence) + "» в короткой серии." };
            var active = Current(p, region);
            if (recent.Count(x => TrainingProgress.Pass(p, x.Accuracy)) >= 2 && recent.All(x => !x.Independent) &&
                TrainingProgress.Guided(active, p) >= TrainingProgress.GuidedGoal(active, p))
                return new PracticeSuggestion { Mode = ExerciseKind.Blind, Region = region.Id,
                    Reason = "С подсказками получается точно. Попробуйте новый фрагмент без схемы." };
            if (recent.Count(x => x.Independent && TrainingProgress.Pass(p, x.Accuracy)) >= 2 &&
                TrainingProgress.MasteryPercent(p, region.Language, false, now) > 0)
                return new PracticeSuggestion { Mode = ExerciseKind.Mixed, Region = region.Id,
                    Reason = "Получаются самостоятельные проходы. Чередуйте знакомые группы, как в обычной работе." };
            return new PracticeSuggestion { Mode = ExerciseKind.Campaign, Region = region.Id,
                Reason = "Продолжите текущую территорию. Ускоряйтесь, когда движения станут точными." };
        }

        public static string Task(string mode, string text)
        {
            // Stable across processes and does not put imported source code in the results history.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in text) { hash ^= c; hash *= 1099511628211UL; }
                return mode + "/" + hash.ToString("x16");
            }
        }

        public static string Name(ExerciseKind kind)
        {
            switch (kind)
            {
                case ExerciseKind.Campaign: return "Кампания";
                case ExerciseKind.Weak: return "Слабые клавиши";
                case ExerciseKind.Blind: return "Без подсказок";
                case ExerciseKind.Memory: return "Память клавиш";
                case ExerciseKind.Mixed: return "Чередование";
                case ExerciseKind.Ghost: return "Гонка с собой";
                case ExerciseKind.Survival: return "Запас ошибок";
                case ExerciseKind.Python: return "Python Lab";
                case ExerciseKind.PythonWords: return "Слова Python";
                case ExerciseKind.PythonCode: return "Конструкции Python";
                case ExerciseKind.Shortcuts: return "Горячие клавиши";
                case ExerciseKind.Boss: return "Экзамен";
                case ExerciseKind.Daily: return "Повтор по памяти";
                default: return "Свой текст";
            }
        }

        public static bool IsRussian(Territory region) { return region.Language == Layout.Russian; }
        public static Lesson Current(StudioProfile p, Territory region)
        {
            var active = Atlas.Next(p, region);
            if (active != null && active.Lesson != null) return active.Lesson;
            return region.Lessons.Select(Lessons.Find).LastOrDefault() ?? Lessons.Find("assign");
        }

        public static Exercise Create(ExerciseKind kind, StudioProfile profile, Territory region, DateTime now, int salt = 0)
        {
            bool ru = IsRussian(region);
            Layout layout = ru ? Layout.Russian : Layout.English;
            var lesson = Current(profile, region);
            var x = new Exercise { Kind = kind, Region = region.Id, Layout = layout, Title = Name(kind) };
            int next = TrainingProgress.Variant(lesson, profile);
            bool ready = TrainingProgress.Cleared(lesson, profile) || TrainingProgress.Guided(lesson, profile) >= TrainingProgress.GuidedGoal(lesson, profile);
            switch (kind)
            {
                case ExerciseKind.Weak:
                    x.Text = WeakDrill(profile, layout, lesson, now, salt, out x.Instruction);
                    if (x.Text == null) return null;
                    break;
                case ExerciseKind.Blind:
                    x.Text = lesson.Rounds[ready ? lesson.Rounds.Length - 1 - salt % lesson.CheckRounds : next];
                    x.Independent = true;
                    x.Instruction = "Наберите видимый текст, вспоминая клавиши без схемы и подсказок пальцев.";
                    break;
                case ExerciseKind.Memory:
                    var symbols = RecallSymbols(lesson, profile, layout, now, salt);
                    x.Text = symbols;
                    x.Independent = true;
                    x.Instruction = "В центре показывается один символ. Вспомните движение, затем нажмите клавишу.";
                    break;
                case ExerciseKind.Mixed:
                    x.Text = Mixed(profile, region, salt);
                    x.Instruction = region.Id == "symbols" ? "Чередуются знаки, движения на основном ряду и короткий код." :
                        "Группы меняются внутри упражнения. При смене языка переключите Windows через Win+Пробел.";
                    break;
                case ExerciseKind.Ghost:
                    // Always compare a subsequent run to the same script with the same help.
                    x.Text = lesson.Rounds[lesson.Rounds.Length > 1 ? 1 : 0];
                    x.Independent = true;
                    x.Task = Task("ghost", x.Text);
                    x.Ghost = TrainingProgress.Best(profile, x.Task, true, true);
                    x.Instruction = x.Ghost == null ? "Первый проход запишет ваш результат. Повторите, чтобы появилось второе движение на шкале." :
                        "Нижняя шкала — ваш предыдущий рекорд на том же тексте. Приоритет — точность.";
                    break;
                case ExerciseKind.Survival:
                    x.Text = ready ? lesson.Rounds[lesson.Rounds.Length - 1 - salt % lesson.CheckRounds] : lesson.Rounds[next];
                    x.ErrorLimit = Math.Max(1, Math.Min(9, profile.Training.SurvivalErrors));
                    x.Independent = ready;
                    x.Instruction = "Ровный набор без гонки. При исчерпании запаса ошибок раунд остановится.";
                    break;
                case ExerciseKind.Python:
                    lesson = Current(profile, Atlas.Find("python"));
                    x.Region = "python";
                    x.Layout = Layout.English;
                    x.Text = lesson.Rounds[(TrainingProgress.Variant(lesson, profile) + salt) % lesson.Rounds.Length];
                    x.Instruction = "Наберите фрагмент с отступами. Затем можно исправить код в отдельном учебном VS Code.";
                    break;
                case ExerciseKind.Boss:
                    if (region.Trial.Length > 0) return null;
                    x.Text = ExamText(region.Id, salt);
                    x.Independent = true;
                    x.Instruction = "Новый фрагмент проверяет несколько навыков области. Зачёт от " + profile.Training.Goal + "%, без требования к скорости.";
                    break;
                case ExerciseKind.Daily:
                    var l = Lessons.NextDue(profile, now);
                    if (l == null) return null;
                    x.Recall = l;
                    x.Region = Atlas.ForLesson(l).Id;
                    x.Layout = l.Language;
                    x.Title += " · " + l.Title;
                    x.Text = Lessons.RecallText(l, profile.RecallDates.Find(d => d.Lesson == l.Id).Checks);
                    x.Independent = true;
                    x.Instruction = "Наберите другой вариант без схемы. Точный проход увеличит интервал до следующей проверки.";
                    break;
                default: return null;
            }
            if (x.Task == null) x.Task = Task(kind.ToString(), x.Text);
            return x;
        }

        private static string WeakDrill(StudioProfile p, Layout l, Lesson lesson, DateTime now, int salt, out string instruction)
        {
            string locale = TrainingProgress.Locale(l);
            var keys = TrainingProgress.Weak(p, locale, now).Where(x => x.Sequence.All(c => TypingKeyboard.Hint(c, l) != null))
                .Take(3).Select(x => x.Sequence).ToList();
            var pairs = TrainingProgress.Weak(p, locale, now, true).Where(x => x.Sequence.All(c => !char.IsWhiteSpace(c)))
                .Take(2).Select(x => x.Sequence).ToArray();
            string region = Atlas.ForLesson(lesson).Id;
            bool needsShorter = p.Results.Where(x => x.Region == region).Reverse().Take(3)
                .Count(x => !x.Completed || !TrainingProgress.Pass(p, x.Accuracy)) >= 2;
            var focus = keys.Concat(pairs).Distinct().Take(needsShorter ? 2 : 5).ToArray();
            instruction = focus.Length == 0 ? "" : "Отдельно, затем в знакомом контексте: " + string.Join(" · ", focus.Select(CodeText.Display));
            if (focus.Length == 0) return null;
            var bank = lesson.Rounds.Skip(1).SelectMany(s => s.Split('\n')).Where(s => s.Length >= 4 && s.Length < 190 &&
                s.Any(c => c != CodeText.Erase)).Distinct().ToArray();
            var chunks = new List<string>();
            var rng = new Random(13 + salt * 17);
            for (int i = 0; i < Math.Max(needsShorter ? 4 : 7, focus.Length * 2); i++)
            {
                string unit = focus[i % focus.Length];
                var candidates = bank.Where(s => s.Contains(unit) && (!needsShorter || s.Length <= 48)).Take(25).ToArray();
                string anchor = lesson.Language == Layout.Russian ? "ао" : "fj";
                string sequence = i < focus.Length ? string.Concat(Enumerable.Repeat(unit, 4)) :
                    candidates.Length > 0 ? candidates[rng.Next(candidates.Length)] : unit + anchor + unit + anchor + unit;
                chunks.Add(sequence);
            }
            return string.Join("\n", chunks);
        }

        private static string RecallSymbols(Lesson l, StudioProfile p, Layout language, DateTime now, int salt)
        {
            var focus = TrainingProgress.Weak(p, TrainingProgress.Locale(language), now).Select(x => x.Sequence[0]).Take(5).ToList();
            focus.AddRange(l.Rounds[1 % l.Rounds.Length].Where(c => !char.IsWhiteSpace(c) && c != CodeText.Erase).Distinct().Take(12));
            var bank = focus.ToArray();
            var rng = new Random(289 + salt);
            return new string(Enumerable.Range(0, 40).Select(i => bank[rng.Next(bank.Length)]).ToArray());
        }

        private static string Mixed(StudioProfile p, Territory region, int salt)
        {
            Lesson en = Current(p, Atlas.Find("qwerty")), ru = Current(p, Atlas.Find("ru"));
            Lesson py = Current(p, Atlas.Find("python")), symbols = Current(p, Atlas.Find("symbols"));
            Lesson[] order = region.Id == "symbols" ? new[] { symbols, en, symbols, py } :
                region.Id == "ru" ? new[] { ru, en, ru, py } : new[] { en, ru, py, ru };
            return string.Join("\n", order.Select((l, i) => Excerpt(l.Rounds[(1 + salt + i) % l.Rounds.Length], 55)));
        }

        private static string Excerpt(string text, int max)
        {
            if (text.Length <= max) return text;
            int end = text.LastIndexOfAny(new[] { ' ', '\n' }, max, Math.Min(max, 20));
            return text.Substring(0, end > 0 ? end : max);
        }

        private static string ExamText(string region, int salt)
        {
            int i = salt % 3;
            if (region == "ru") return new[] {
                "Проверь, пожалуйста, текст в черновике. Я исправил дату и добавил ещё две строки. Если всё в порядке, отправлю письмо после обеда.",
                "Доброе утро! Я обновил общий список задач и отметил, что уже готово. Загляни туда до встречи и напиши, если нужно что-то изменить.",
                "В отчёте осталось проверить три числа. Я сверю их с исходным файлом, уберу лишнюю строку и пришлю свежую версию к вечеру."
            }[i];
            if (region == "python") return new[] {
                "def ready_names(rows):\n    names = []\n    for row in rows:\n        if row['done']:\n            names.append(row['name'])\n    return names\n\nassert ready_names([]) == []",
                "def reading(items, limit=10):\n    values = [item['value'] for item in items]\n    return [value for value in values if value > limit]\n\nassert reading([]) == []",
                "def label(record, prefix='item'):\n    name = record.get('name', '')\n    if not name:\n        return prefix\n    return f'{prefix}: {name}'\n\nassert label({}) == 'item'"
            }[i];
            if (region == "symbols") return new[] {
                "6++⌫6 = 12\nrow[4:7] = {1: 'new', 2: 'old'}\n8 * 7 + 2 // 3 != 55\nkey = ('name', 'value')\nline = \"x_y\"\n(9 - 3) >= 4",
                "8 + 8+⌫ = 16\nrow[1:4] = (0, 2, 9)\nnames = {'x': \"new\", 'y': \"old\"}\n4**2 - 7 != 8\nentry['id'] < 24\nscore >= 1",
                "6+6+⌫ = 12\npath = ('a', 'b')\n5**3 + 16 // 4 > 10\n{'ready': [0, 9], 'old': []}\nentry[\"tag\"] != 'new'\n3 * (8 + 2) == 30"
            }[i];
            return new[] {
                "pack my box with five dozen liquor jugs. check the new draft before you close the file. move each note to the right folder and save your work.",
                "the quick brown fox jumps over the lazy dog. open the old report, fix the wrong date, and share the updated page with your team.",
                "six big devils from japan quickly forgot how to waltz. write a brief note about your work and leave the next task in the shared folder."
            }[i];
        }

        public static Exercise Transfer(StudioProfile p, bool ru)
        {
            string[] messages = ru ? new[] {
                "Привет! Я поправил список задач. Посмотри, пожалуйста, успеваем ли мы к пятнице.",
                "Файл обновлён. Перед отправкой проверь название и последние две строки.",
                "Я смогу ответить после обеда. Если вопрос срочный, напиши мне в чат."
            } : new[] {
                "I updated the draft this morning. Please check the last two lines before sending it.",
                "Open the notes, fix the date, and save a copy in the shared folder. Thanks!",
                "I will review the changes after lunch. Leave a message if you need an answer sooner."
            };
            string region = ru ? "ru" : "qwerty";
            int count = p.Results.Count(r => r.Activity == "Transfer" && r.Region == region);
            string text = messages[count % messages.Length];
            return new Exercise { Kind = ExerciseKind.Free, Title = ru ? "Сообщение на русском" : "Сообщение на английском",
                Text = text, Layout = ru ? Layout.Russian : Layout.English, Region = region, Task = Task("Transfer", text),
                Instruction = "Наберите рабочее сообщение. После занятия попробуйте отправить одно своё тем же способом." };
        }
    }
}
