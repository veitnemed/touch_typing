using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio
{
    public class ChallengeTask
    {
        public string Id, Title, Detail, Region, Route, Activity;
        public int Day, Minutes, Variant;
        public bool Outside;
    }

    internal static class ChallengePlanner
    {
        public static string Context(StudioProfile p, ChallengeTask task)
        { return p.ChallengeStart.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "/" + task.Id; }

        public static List<ChallengeTask> Build(StudioProfile p, int minutes, DateTime now)
        {
            minutes = Math.Max(15, Math.Min(90, minutes));
            var ru = Atlas.Find("ru");
            var en = Atlas.Find("qwerty");
            var weakEn = TrainingProgress.Weak(p, "EN", now).FirstOrDefault();
            var weakRu = TrainingProgress.Weak(p, "RU", now).FirstOrDefault();
            string enName = weakEn == null ? "закрепите домашний ряд и текущую группу" : "потренируйте «" + CodeText.Display(weakEn.Sequence) + "» в контексте";
            string ruName = weakRu == null ? "начните с текущего уровня ЙЦУКЕН" : "отработайте «" + CodeText.Display(weakRu.Sequence) + "» на ЙЦУКЕН";
            var route = Atlas.Next(p, Atlas.Find("windows"));
            var firstRoute = route == null || route.Route == null ? RouteKind.NavigateWords.ToString() : route.Route.Kind.ToString();
            var editor = Atlas.Next(p, Atlas.Find("vscode"));
            var nextRoute = editor == null || editor.Route == null ? RouteKind.CodeLine.ToString() : editor.Route.Kind.ToString();
            RouteSpec first = RouteCatalog.Routes.First(r => r.Kind.ToString() == firstRoute);
            RouteSpec second = RouteCatalog.Routes.First(r => r.Kind.ToString() == nextRoute);
            bool advanced = TrainingProgress.Cleared(Lessons.Find("assign"), p);
            int baseVariant = advanced ? 11 : 3;
            var plan = new List<ChallengeTask>
            {
                Item(1, "base", "Исходный замер: Python", "Наберите фрагмент в удобном темпе с правильной постановкой пальцев. Заметьте, сколько раз посмотрели вниз.", "python", "benchmark", baseVariant),
                Item(1, "moves", "Движения EN", enName + ". Работайте короткими сериями с паузой между раундами.", en.Id, weakEn == null ? "practice" : "Weak"),
                Item(1, "ru", "Движения RU", ruName + ". Ищите выступы А/О пальцами.", ru.Id, weakRu == null ? "practice" : "Weak"),
                new ChallengeTask { Day = 1, Id = "window", Title = first.Name, Detail = first.Goal + " Освойте маршрут в настоящем окне.",
                    Region = "windows", Route = firstRoute },
                new ChallengeTask { Day = 1, Id = "outside-first", Title = "Перенос: короткое сообщение", Detail =
                    "В обычном приложении напишите короткое сообщение на русском. Затем наберите две строки Python в VS Code. Если замедлились, сделайте паузу вместо взгляда на клавиши.", Outside = true },
                Item(2, "delay", "Проверка после перерыва", "Вспомните клавиши без разминки. Если подошёл срок повтора уровня, откроется его вариант по памяти.", ru.Id, "recall"),
                Item(2, "mix", "EN ↔ RU ↔ Python", "Чередуйте знакомые движения и переключайте раскладку в пределах одного раунда.", "python", "Mixed"),
                new ChallengeTask { Day = 2, Id = "editor", Title = second.Name, Detail = second.Goal +
                    " Если техника знакома, выберите стадию «Самостоятельно».", Region = "vscode", Route = nextRoute },
                Item(2, "retest", "Повтор исходного замера", "Тот же фрагмент и одинаковые подсказки. Сравните точность, время и число взглядов вниз.", "python", "benchmark", baseVariant),
                new ChallengeTask { Day = 2, Id = "outside-second", Title = "Пять минут обычной работы без мыши", Detail =
                    "Откройте свой файл в VS Code, измените короткий фрагмент и сохраните. Переключите окно и раскладку для ответа в чате. Отметьте, где вернулись к старой привычке.", Outside = true }
            };
            int rest = Rest(minutes);
            foreach (int day in new[] { 1, 2 })
            {
                var tasks = plan.Where(x => x.Day == day).ToArray();
                int total = minutes - rest;
                for (int i = 0; i < tasks.Length; i++)
                    tasks[i].Minutes = total / tasks.Length + (i < total % tasks.Length ? 1 : 0);
            }
            return plan;
        }

        public static int Rest(int minutes) { return minutes >= 60 ? 15 : minutes >= 30 ? 5 : 2; }

        private static ChallengeTask Item(int day, string id, string title, string detail, string region, string activity, int variant = 0)
        { return new ChallengeTask { Day = day, Id = id, Title = title, Detail = detail, Region = region, Activity = activity, Variant = variant }; }

        public static Exercise ExerciseFor(StudioProfile p, ChallengeTask task, DateTime now)
        {
            var region = Atlas.Find(task.Region);
            if (task.Activity == "recall")
                return ExerciseBank.Create(ExerciseKind.Daily, p, region, now) ??
                    ExerciseBank.Create(ExerciseKind.Memory, p, region, now);
            ExerciseKind mode;
            if (Enum.TryParse(task.Activity, out mode))
            {
                var chosen = ExerciseBank.Create(mode, p, region, now);
                if (chosen != null) return chosen;
            }
            var level = task.Activity == "benchmark" ? Lessons.Find("assign") : ExerciseBank.Current(p, region);
            int variant = task.Activity == "benchmark" ? task.Variant : TrainingProgress.Variant(level, p);
            string text = level.Rounds[variant];
            return new Exercise { Kind = ExerciseKind.Campaign, Title = task.Title, Text = text, Region = task.Region,
                Layout = level.Language, Task = ExerciseBank.Task(task.Activity, text), Instruction = task.Detail,
                FixedGuidance = task.Activity == "benchmark" };
        }

        public static bool Done(StudioProfile p, ChallengeTask task)
        {
            string context = Context(p, task);
            if (task.Outside) return p.ChallengeNotes.Any(x => x.Campaign == p.ChallengeStart && x.Task == task.Id && x.Done);
            if (task.Route != null) return p.RouteResults.Any(x => x.Context == context && !x.Assisted);
            return p.Results.Any(x => x.Context == context && x.Completed);
        }

        public static TypingResult Benchmark(StudioProfile p, string task)
        {
            var target = p.ChallengePlan.Find(x => x.Id == task);
            return target == null ? null : p.Results.LastOrDefault(x => x.Context == Context(p, target) && x.Completed);
        }
    }
}
