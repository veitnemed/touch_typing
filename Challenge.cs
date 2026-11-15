using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
            RouteSpec first = RouteSession.Routes.First(r => r.Kind.ToString() == firstRoute);
            RouteSpec second = RouteSession.Routes.First(r => r.Kind.ToString() == nextRoute);
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

    internal sealed class ChallengePage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        public ChallengePage(StudioWindow owner, ProfileStore store)
        {
            var saved = store.Read();
            var stack = FlightUi.Sheet();
            Background = FlightUi.Backdrop(saved);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            Ui.Add(stack, FlightUi.Label("4 8   H O U R S   C H A L L E N G E", FlightUi.Accent(saved)), 4);
            Ui.Add(stack, Ui.Text("Два дня на новый способ печати", 31, Ui.Ink, true), 6);
            Ui.Add(stack, Ui.Text("Цель: применять десять пальцев в обычной работе. Между блоками отдыхайте; за пропущенный день ничего не сбрасывается.", 15, Ui.Muted), 13);
            var control = new WrapPanel();
            var minutes = new ComboBox { ItemsSource = new[] { 15, 20, 30, 45, 60, 90 }, SelectedItem = saved.ChallengeMinutes,
                MinWidth = 67, Margin = new Thickness(0, 2, 8, 4), Padding = new Thickness(6) };
            Ui.Name(minutes, "Минут в день");
            if (minutes.SelectedIndex < 0) minutes.SelectedItem = 45;
            Ui.Add(control, minutes);
            Ui.Add(control, Ui.Text("минут в день   ", 15, Ui.Muted));
            bool started = saved.ChallengeStart != DateTime.MinValue && saved.ChallengePlan.Count > 0;
            var start = Ui.Button(started ? "Составить новый цикл" : "Начать двухдневный цикл", delegate
            {
                store.Update(p =>
                {
                    p.ChallengeMinutes = (int)minutes.SelectedItem;
                    p.ChallengeStart = DateTime.Now;
                    p.ChallengePlan = ChallengePlanner.Build(p, p.ChallengeMinutes, p.ChallengeStart);
                });
                owner.ShowMode(3);
            }, !started);
            Ui.Add(control, start);
            Ui.Add(control, Ui.Button("Alt+1  К Python", delegate { owner.ShowMode(1); }));
            Ui.Add(stack, control, 10);
            InitialFocus = minutes;
            if (!started)
            {
                var preview = Ui.Text("", 15, Ui.Accent);
                Action update = delegate
                {
                    var plan = ChallengePlanner.Build(saved, (int)minutes.SelectedItem, DateTime.Now);
                    preview.Text = "В первый день: " + string.Join(" → ", plan.Where(x => x.Day == 1).Take(4).Select(x => x.Title)) +
                        "\nВо второй: " + string.Join(" → ", plan.Where(x => x.Day == 2).Take(4).Select(x => x.Title)) + ".";
                };
                minutes.SelectionChanged += delegate { update(); };
                update();
                Ui.Add(stack, Ui.Panel(preview, new Thickness(15), Ui.Brush("#18283B")));
                Content = stack;
                return;
            }

            var a = ChallengePlanner.Benchmark(saved, "base");
            var b = ChallengePlanner.Benchmark(saved, "retest");
            string baseline = a == null ? "Сначала пройдите исходный замер." :
                "Исходный замер: " + a.Accuracy.ToString("0.0") + "% · " + a.Speed + " сим/мин";
            if (b != null) baseline += "    →    Повтор: " + b.Accuracy.ToString("0.0") + "% · " + b.Speed + " сим/мин";
            Ui.Add(stack, Ui.Panel(Ui.Text(baseline, 15, FlightUi.Accent(saved), true), new Thickness(12), Ui.Brush("#17283B")), 14);
            for (int day = 1; day <= 2; day++)
            {
                int pDay = day;
                var date = saved.ChallengeStart.Date.AddDays(day - 1);
                bool available = DateTime.Today >= date;
                var items = saved.ChallengePlan.Where(x => x.Day == pDay).ToArray();
                Ui.Add(stack, FlightUi.Label("Д Е Н Ь   0 " + day + "   /   " + date.ToString("dd.MM") +
                    "   ·   " + items.Count(x => ChallengePlanner.Done(saved, x)) + " / " + items.Length), 4);
                Ui.Add(stack, Ui.Text(day == 1 ? "Постановка и первый перенос" : "Вспомнить и применить", 22, Ui.Ink, true), 7);
                foreach (var task in items)
                {
                    bool done = ChallengePlanner.Done(saved, task);
                    var row = new DockPanel();
                    var action = Ui.Button(task.Outside ? (done ? "Отмечено ✓" : "Отметить") : done ? "Повторить" : "Открыть", delegate
                    {
                        if (!task.Outside) { owner.OpenChallengeTask(task); return; }
                        store.Update(p =>
                        {
                            p.ChallengeNotes.RemoveAll(x => x.Campaign == p.ChallengeStart && x.Task == task.Id);
                            if (!done) p.ChallengeNotes.Add(new ChallengeNote { Campaign = p.ChallengeStart, Task = task.Id, Done = true });
                        });
                        owner.ShowMode(3);
                    });
                    action.IsEnabled = available;
                    action.MinWidth = 98;
                    action.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(action, Dock.Right);
                    row.Children.Add(action);
                    var text = new StackPanel();
                    Ui.Add(text, Ui.Text((done ? "✓  " : "") + task.Title + "  ·  " + task.Minutes + " мин", 16, Ui.Ink, true), 4);
                    Ui.Add(text, Ui.Text(task.Detail, 13, Ui.Muted));
                    row.Children.Add(text);
                    Ui.Add(stack, Ui.Panel(row, new Thickness(12, 7, 12, 7), Ui.Brush("#192637")), 5);
                    if (available && !done && InitialFocus == minutes) InitialFocus = action;
                }
                string noteId = "note/" + pDay;
                var oldNote = saved.Notes.FirstOrDefault(n => n.Day == pDay);
                var currentNote = saved.ChallengeNotes.Find(n => n.Campaign == saved.ChallengeStart && n.Task == noteId);
                var observation = Ui.Editor(currentNote != null ? currentNote.Text : oldNote == null ? "" : oldNote.Text, 14);
                observation.Height = 68;
                observation.TextWrapping = TextWrapping.Wrap;
                observation.MaxLength = 1800;
                observation.IsEnabled = available;
                Ui.Name(observation, "Заметки дня " + pDay + ": взгляды на клавиатуру и трудности");
                Ui.Add(stack, Ui.Text("Число взглядов и где было трудно", 13, Ui.Muted), 4);
                Ui.Add(stack, observation, 5);
                var noteState = Ui.Text("", 12, Ui.Accent);
                var note = Ui.QuietButton("Сохранить заметку", delegate
                {
                    store.Update(p =>
                    {
                        p.ChallengeNotes.RemoveAll(n => n.Campaign == saved.ChallengeStart && n.Task == noteId);
                        p.ChallengeNotes.Add(new ChallengeNote { Campaign = saved.ChallengeStart, Task = noteId, Text = observation.Text });
                    });
                    owner.WarnStorage();
                    noteState.Text = store.Error == null ? "Заметка сохранена." : "Заметка не сохранена.";
                });
                note.IsEnabled = available;
                Ui.Add(stack, note);
                Ui.Add(stack, noteState, 6);
                Ui.Add(stack, Ui.Text("Отдых: " + (saved.ChallengeMinutes >= 60 ? "три перерыва по 5 мин" :
                    ChallengePlanner.Rest(saved.ChallengeMinutes) + " мин") + ". " +
                    (available ? "В конце отметьте взгляды на клавиатуру самостоятельно." : "Задания откроются после смены дня."), 13, Ui.Muted), 13);
            }
            Content = stack;
        }
    }
}
