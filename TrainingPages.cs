using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed class ModesPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private static readonly ExerciseKind[] Order =
        {
            ExerciseKind.Campaign, ExerciseKind.Daily, ExerciseKind.Weak, ExerciseKind.Memory, ExerciseKind.Blind, ExerciseKind.Mixed,
            ExerciseKind.Ghost, ExerciseKind.Survival, ExerciseKind.Python, ExerciseKind.Shortcuts, ExerciseKind.Boss, ExerciseKind.Free
        };
        private static readonly string[] Details =
        {
            "Двигайтесь по карте. После точных проходов появится самостоятельный раунд.",
            "Вспомните один из освоенных уровней после перерыва. Точный набор увеличит интервал.",
            "Отдельно отработайте самые неточные или медленные движения, затем наберите знакомые фрагменты.",
            "Видно по одному символу. Вспомните, где клавиша, и проверьте себя нажатием.",
            "Докажите себе, что расположение клавиш помнится. Текст виден целиком, схема скрыта.",
            "Меняйте движение и раскладку внутри одного раунда: знакомые группы чередуются.",
            "Сравните свой темп с лучшим точным проходом на том же тексте.",
            "Начните с запаса ошибок. Когда он закончится, раунд остановится.",
            "Наберите Python с отступами, затем исправьте код в учебном окне VS Code.",
            "Выберите настоящее действие в Windows или VS Code. Проверяется его результат.",
            "Проверьте территорию без подсказок: новый фрагмент или серия рабочих действий.",
            "Вставьте текст, откройте файл .py или .txt и выберите строки для набора."
        };

        public ModesPage(StudioWindow owner, StudioProfile profile, string region, string message)
        {
            var territory = Atlas.Find(region);
            var stack = FlightUi.Sheet();
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            Ui.Add(stack, FlightUi.Label("Т Р Е Н И Р О В К И", FlightUi.Accent(profile)), 4);
            Ui.Add(stack, Ui.Text("Выберите, что отработать", 31, Ui.Ink, true), 6);
            if (!string.IsNullOrEmpty(message))
                Ui.Add(stack, Ui.Panel(Ui.Text(message, 15, Ui.Accent), new Thickness(12)), 10);
            var regions = new WrapPanel();
            foreach (var scope in Atlas.Regions)
            {
                var tag = Ui.Button(scope.Name, delegate { owner.ShowModes(scope.Id); }, scope == territory);
                tag.MinHeight = 32;
                Ui.Add(regions, tag);
                if (scope == territory) InitialFocus = tag;
            }
            Ui.Add(stack, regions, 12);
            var suggestion = ExerciseBank.Recommend(profile, territory, DateTime.Now);
            var recommendation = new DockPanel();
            var launch = Ui.Button(ExerciseBank.Name(suggestion.Mode) + "  ›",
                delegate { owner.OpenExercise(suggestion.Mode, suggestion.Region); }, true);
            launch.Margin = new Thickness(9, 0, 0, 0);
            DockPanel.SetDock(launch, Dock.Right);
            recommendation.Children.Add(launch);
            var why = new StackPanel();
            Ui.Add(why, FlightUi.Label("Р Е К О М Е Н Д А Ц И Я", FlightUi.Accent(profile)), 3);
            Ui.Add(why, Ui.Text(suggestion.Reason, 14, Ui.Muted));
            recommendation.Children.Add(why);
            Ui.Add(stack, Ui.Panel(recommendation, new Thickness(12, 8, 12, 8), Ui.Brush("#172D42")), 10);
            var daily = Lessons.NextDue(profile, DateTime.Now);
            var items = new UniformGrid { Columns = 2 };
            for (int i = 0; i < Order.Length; i++)
            {
                var mode = Order[i];
                string detail = Details[i];
                if (mode == ExerciseKind.Daily) detail += daily == null ? " На сегодня всё." : " Сегодня: " + daily.Title + ".";
                var card = new StackPanel();
                var open = Ui.QuietButton(ExerciseBank.Name(mode) + "  ›", delegate { owner.OpenExercise(mode, territory.Id); });
                open.FontSize = 17;
                open.HorizontalAlignment = HorizontalAlignment.Left;
                open.Padding = new Thickness(4, 2, 4, 2);
                open.Foreground = FlightUi.Accent(profile);
                Ui.Add(card, open, 3);
                Ui.Add(card, Ui.Text(detail, 13, Ui.Muted));
                var block = Ui.Panel(card, new Thickness(12, 8, 12, 12), Ui.Brush("#182535"));
                block.Margin = new Thickness(0, 0, 9, 8);
                items.Children.Add(block);
            }
            Ui.Add(stack, items, 9);
            var links = new WrapPanel();
            Ui.Add(links, Ui.Button("Сообщения на русском", delegate { owner.OpenTransfer(true); }));
            Ui.Add(links, Ui.Button("Сообщения на английском", delegate { owner.OpenTransfer(false); }));
            Ui.Add(links, Ui.Button("Продолжить текущий уровень", delegate { owner.OpenLesson(ExerciseBank.Current(profile, territory).Id); }));
            Ui.Add(links, Ui.Button("Настройки обучения", owner.ShowTrainingSettings));
            Ui.Add(links, Ui.Button("Esc  Назад", owner.ClosePanel));
            Ui.Add(stack, links);
            Content = stack;
        }
    }

    internal sealed class ShortcutIndexPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        public ShortcutIndexPage(StudioWindow owner, StudioProfile profile, Territory region)
        {
            var stack = FlightUi.Sheet();
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            Ui.Add(stack, FlightUi.Label("П Р А К Т И К А   /   Р А Б О Ч И Е   О К Н А", FlightUi.Accent(profile)), 4);
            Ui.Add(stack, Ui.Text("Горячие клавиши", 32, Ui.Ink, true), 6);
            Ui.Add(stack, Ui.Text("Учебные файлы открываются отдельно. Сначала виден порядок действий, затем задача проверяется без подсказок.", 15, Ui.Muted), 10);
            var tabs = new WrapPanel();
            var win = Ui.Button("Windows и Проводник", delegate { owner.OpenShortcuts("windows"); }, region.Id == "windows");
            var code = Ui.Button("VS Code", delegate { owner.OpenShortcuts("vscode"); }, region.Id == "vscode");
            InitialFocus = region.Id == "windows" ? win : code;
            Ui.Add(tabs, win); Ui.Add(tabs, code);
            Ui.Add(tabs, Ui.Button("К режимам", delegate { owner.ShowModes(region.Id); }));
            Ui.Add(stack, tabs, 8);
            foreach (var route in region.Routes)
            {
                int stages = Enumerable.Range(0, 3).Count(i => profile.CompletedRoutes.Contains(route.PassId(i)));
                var row = new DockPanel();
                var count = FlightUi.Label(stages + " / 3  ·  " + route.Category);
                count.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                var open = Ui.QuietButton(route.Name + "  ›", delegate { owner.StartWindowsRoute(route.Kind.ToString()); });
                open.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(open);
                Ui.Add(stack, Ui.Panel(row, new Thickness(8, 3, 10, 3), Ui.Brush("#172536")), 5);
            }
            Content = stack;
        }
    }

    internal sealed class TrainingSettingsPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        public TrainingSettingsPage(StudioWindow owner, ProfileStore store)
        {
            var saved = store.Read();
            var options = saved.Training;
            var stack = FlightUi.Sheet();
            Background = FlightUi.Backdrop(saved);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            Ui.Add(stack, FlightUi.Label("П А Р А М Е Т Р Ы", FlightUi.Accent(saved)), 4);
            Ui.Add(stack, Ui.Text("Настройки обучения", 30, Ui.Ink, true), 8);
            Ui.Add(stack, Ui.Text("Пороги настраиваются под ваш темп. Уже открытые уровни и награды сохраняются. Даты уже назначенных повторов останутся прежними.", 14, Ui.Muted), 17);
            var goal = Field(stack, "Точность зачёта, % (90–100)", options.Goal);
            var guided = Field(stack, "Разных раундов с подсказками до самостоятельного (1–4, где хватает вариантов)", options.GuidedRounds);
            var fade = Field(stack, "Скрывать руки и подсветку после этой доли тренировочного раунда, % (20–100; 100 — в конце)", options.FadeFrom);
            var first = Field(stack, "Первый повтор через столько дней (1–5)", options.FirstDays);
            var later = Field(stack, "Следующий интервал после успеха, дней (2–7; больше первого)", options.LaterDays);
            var survival = Field(stack, "Запас ошибок в испытании (1–9)", options.SurvivalErrors);
            var motion = new CheckBox { Content = "Снизить анимацию на карте", IsChecked = options.ReduceMotion, Foreground = Ui.Ink, Margin = new Thickness(0, 3, 0, 11) };
            Ui.Add(stack, motion);
            Ui.Add(stack, Ui.Text("Темы оформления", 17, Ui.Ink, true), 4);
            var colors = new WrapPanel();
            var themeInfo = Ui.Text("Сейчас: " + TrainingProgress.ThemeTitle(saved), 13, Ui.Muted);
            string[] themeNames = { "Глубокий космос", "Аврора · уровень 2", "Солнечный ветер · уровень 5" };
            string[] codes = { "space", "aurora", "solar" };
            int[] needs = { 1, 2, 5 };
            string pendingTheme = saved.Theme;
            for (int i = 0; i < themeNames.Length; i++)
            {
                string code = codes[i], label = themeNames[i];
                var b = Ui.Button(label, delegate { pendingTheme = code; themeInfo.Text = "Будет применена: " + label; });
                b.IsEnabled = TrainingProgress.Level(saved) >= needs[i];
                Ui.Add(colors, b);
            }
            Ui.Add(stack, colors);
            Ui.Add(stack, themeInfo, 10);
            var message = Ui.Text("", 14, Ui.Brush("#FFD395"));
            Ui.Add(stack, message, 6);
            var bar = new WrapPanel();
            Ui.Add(bar, Ui.Button("Сохранить", delegate
            {
                int a, g, f, d, e, s;
                if (!Number(goal, 90, 100, out a) || !Number(guided, 1, 4, out g) || !Number(fade, 20, 100, out f) ||
                    !Number(first, 1, 5, out d) || !Number(later, 2, 7, out e) || !Number(survival, 1, 9, out s) || e <= d)
                {
                    message.Text = "Проверьте диапазоны. Второй интервал должен быть больше первого.";
                    return;
                }
                store.Update(p =>
                {
                    // Materialize already-earned milestones before changing optional thresholds.
                    foreach (var course in Lessons.All.Where(l => TrainingProgress.Cleared(l, p)).ToArray())
                        TrainingProgress.Earn(p, "skill/" + course.Id, 45, DateTime.Now);
                    p.Training = new TrainingOptions { Accuracy = a, GuidedRounds = g, FadeFrom = f, FirstDays = d,
                        LaterDays = e, SurvivalErrors = s, ReduceMotion = motion.IsChecked == true };
                    p.Theme = pendingTheme;
                });
                owner.WarnStorage();
                if (store.Error == null) owner.ShowModes(null, "Настройки сохранены.");
            }, true));
            Ui.Add(bar, Ui.Button("Esc  Назад", owner.ClosePanel));
            Ui.Add(stack, bar);
            Content = stack;
            InitialFocus = goal;
        }

        private static TextBox Field(StackPanel parent, string label, int number)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var box = Ui.Editor(number.ToString(), 15);
            box.Width = 70;
            box.AcceptsReturn = false;
            box.MaxLength = 3;
            box.Padding = new Thickness(7, 3, 7, 3);
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(box);
            Ui.Name(box, label);
            var description = Ui.Text(label, 14, Ui.Muted);
            description.VerticalAlignment = VerticalAlignment.Center;
            description.Margin = new Thickness(0, 0, 7, 0);
            row.Children.Add(description);
            Ui.Add(parent, row);
            return box;
        }
        private static bool Number(TextBox box, int min, int max, out int value)
        { return int.TryParse(box.Text, out value) && value >= min && value <= max; }
    }
}
