using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed class AnalyticsPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private readonly StudioWindow owner;
        private readonly StudioProfile profile;
        private readonly DateTime now = DateTime.Now;
        private readonly StackPanel content = new StackPanel();
        private Button en, ru, other;
        private Layout locale;
        private bool all;
        private TextBlock keyDetail;

        public AnalyticsPage(StudioWindow owner, StudioProfile snapshot, Layout locale)
        {
            this.owner = owner;
            profile = snapshot;
            this.locale = locale == Layout.Russian ? Layout.Russian : Layout.English;
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, FlightUi.Label("П Р О Ф И Л Ь   /   Т Е Л Е М Е Т Р И Я", FlightUi.Accent(profile)), 4);
            var actions = new DockPanel();
            var buttons = new WrapPanel { Margin = new Thickness(8, 1, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Right);
            Ui.Add(buttons, Ui.QuietButton("Дерево навыков", delegate { owner.ShowAtlas(this.locale == Layout.Russian ? "ru" : "qwerty"); }));
            Ui.Add(buttons, Ui.QuietButton("Темы", owner.ShowTrainingSettings));
            Ui.Add(buttons, Ui.QuietButton("Esc  Назад", owner.ClosePanel));
            actions.Children.Add(buttons);
            actions.Children.Add(Ui.Text("Ваш прогресс", 31, Ui.Ink, true));
            Ui.Add(stack, actions, 7);
            var ranks = new WrapPanel();
            int xp = TrainingProgress.Xp(profile), level = TrainingProgress.Level(profile);
            Metric(ranks, "УРОВЕНЬ ИГРОКА", level + "  ·  " + xp + " XP", (125 - xp % 125) + " XP до следующего");
            Metric(ranks, "QWERTY · БУКВЫ", TrainingProgress.MasteryPercent(profile, Layout.English, false, now) + "%", "устойчиво без подсказок");
            Metric(ranks, "ЙЦУКЕН · БУКВЫ", TrainingProgress.MasteryPercent(profile, Layout.Russian, false, now) + "%", "устойчиво без подсказок");
            Metric(ranks, "РАБОЧИЕ МАРШРУТЫ", RouteSession.Routes.Count(r => profile.CompletedRoutes.Contains(r.PassId(2))) + " / " + RouteSession.Routes.Length,
                "выполнено самостоятельно");
            Ui.Add(stack, ranks, 12);
            var tabs = new WrapPanel();
            en = Ui.Button("EN · английская", delegate { Select(Layout.English); });
            ru = Ui.Button("RU · русская", delegate { Select(Layout.Russian); });
            other = Ui.Button("Смешанные и знаки", delegate { Select(Layout.Neutral); });
            Ui.Add(tabs, en); Ui.Add(tabs, ru); Ui.Add(tabs, other);
            Ui.Add(stack, tabs, 8);
            Ui.Add(stack, content);
            Content = stack;
            InitialFocus = this.locale == Layout.English ? en : ru;
            Select(this.locale);
        }

        private void Metric(WrapPanel panel, string label, string value, string detail)
        {
            var stack = new StackPanel { MinWidth = 185 };
            Ui.Add(stack, FlightUi.Label(label, FlightUi.Accent(profile)), 3);
            Ui.Add(stack, Ui.Text(value, 24, Ui.Ink, true), 3);
            Ui.Add(stack, Ui.Text(detail, 12, Ui.Muted));
            var frame = Ui.Panel(stack, new Thickness(12, 8, 12, 9), Ui.Brush("#182638"));
            frame.Margin = new Thickness(0, 0, 8, 7);
            panel.Children.Add(frame);
        }

        private void Select(Layout selection)
        {
            locale = selection == Layout.Neutral ? locale : selection;
            all = selection == Layout.Neutral;
            content.Children.Clear();
            en.BorderBrush = selection == Layout.English ? FlightUi.Accent(profile) : Ui.Border;
            ru.BorderBrush = selection == Layout.Russian ? FlightUi.Accent(profile) : Ui.Border;
            other.BorderBrush = all ? FlightUi.Accent(profile) : Ui.Border;
            string lang = TrainingProgress.Locale(locale);
            var evidence = TrainingProgress.Evidence(profile, lang, now);
            var weak = TrainingProgress.Weak(profile, lang, now);
            var pairs = TrainingProgress.Weak(profile, lang, now, true);
            var history = profile.Results.Where(r => all ? r.Language != "EN" && r.Language != "RU" : r.Language == lang).ToArray();
            var trend = history.Where(r => r.Completed).Reverse().Take(18).Reverse().ToArray();
            var summary = new StackPanel();
            Ui.Add(summary, Ui.Text("Скорость и точность", 20, Ui.Ink, true), 3);
            if (trend.Length == 0)
                Ui.Add(summary, Ui.Text("После завершённых раундов здесь появится динамика.", 14, Ui.Muted), 6);
            else
            {
                Ui.Add(summary, Ui.Text("Последние " + trend.Length + " проходов по порядку. Светлые точки — без подсказок. Сложность текстов может различаться.", 12, Ui.Muted), 5);
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var speed = new TrendGraph(trend, false) { Height = 126, Margin = new Thickness(0, 0, 7, 0) };
                var accuracy = new TrendGraph(trend, true) { Height = 126, Margin = new Thickness(7, 0, 0, 0) };
                Grid.SetColumn(accuracy, 1);
                grid.Children.Add(speed); grid.Children.Add(accuracy);
                Ui.Add(summary, grid, 4);
                var latest = trend.Last();
                Ui.Add(summary, Ui.Text("Последний: " + latest.Lesson + "  ·  " + latest.Accuracy.ToString("0.0") +
                    "%  ·  " + latest.Speed + " сим/мин" + (latest.FirstMs > 0 ? "  ·  До первого верного нажатия " + latest.FirstMs + " мс" : ""), 13, Ui.Muted));
            }
            Ui.Add(content, Ui.Panel(summary, new Thickness(13), Ui.Brush("#142234")), 12);
            if (!all) AddHeat(evidence, pairs);

            var insight = new StackPanel();
            Ui.Add(insight, Ui.Text("Что тренировать дальше", 20, Ui.Ink, true), 5);
            if (weak.Length > 0)
            {
                Ui.Add(insight, Ui.Text("Трудные движения: " + string.Join("  ·  ", weak.Take(4).Select(Word)) +
                    (pairs.Length == 0 ? "" : "\nПары: " + string.Join("  ·  ", pairs.Take(3).Select(Word))), 14, Ui.Muted), 6);
                Ui.Add(insight, Ui.Button("Отработать слабые клавиши", delegate { owner.OpenExercise(ExerciseKind.Weak, locale == Layout.Russian ? "ru" : "qwerty"); }));
            }
            else
                Ui.Add(insight, Ui.Text("Пока нет устойчиво трудных движений для этой раскладки. Следующий шаг — новый раунд.", 14, Ui.Muted), 6);
            var strong = evidence.Where(x => x.RetentionChecks > 0 && !string.IsNullOrWhiteSpace(x.Sequence)).OrderByDescending(x => x.RetentionChecks)
                .ThenByDescending(x => x.Accuracy).Take(9).ToArray();
            Ui.Add(insight, Ui.Text("Устойчивые движения: " + (strong.Length == 0 ? "ещё не проверены после перерыва." :
                string.Join("  ·  ", strong.Select(x => CodeText.Display(x.Sequence)))) +
                "\nПодсказки: " + history.Count(r => r.Activity != null && !r.Independent) + " проходов с помощью, " +
                history.Count(r => r.Independent) + " самостоятельных. Проходы старой версии не размечены.", 13, Ui.Muted), 8);
            var due = Lessons.NextDue(profile, now);
            if (due != null)
                Ui.Add(insight, Ui.Button("Повторить «" + due.Title + "»", delegate { owner.OpenExercise(ExerciseKind.Daily); }));
            else if (profile.RecallDates.Count > 0)
                Ui.Add(insight, Ui.Text("Ближайший отложенный повтор: " + profile.RecallDates.Min(x => x.Due).ToString("dd.MM") + ".", 13, Ui.Muted));
            Ui.Add(content, Ui.Panel(insight, new Thickness(13), Ui.Brush("#142234")), 12);
            AddGoals();
            AddRecords();
        }

        private static string Word(KeyEvidence k)
        { return CodeText.Display(k.Sequence) + " " + k.Accuracy.ToString("0.#") + "%"; }

        private void AddHeat(KeyEvidence[] evidence, KeyEvidence[] pairs)
        {
            var stack = new StackPanel();
            Ui.Add(stack, Ui.Text("Тепловая карта · " + TrainingProgress.Locale(locale), 20, Ui.Ink, true), 3);
            Ui.Add(stack, Ui.Text("Серый — нет замеров  ·  бирюзовый — меньше ошибок  ·  тёплый — больше ошибок. Нажмите клавишу на схеме или выберите Tab.", 12, Ui.Muted), 7);
            keyDetail = Ui.Text("Выберите клавишу, чтобы посмотреть точность, интервалы и трудные пары.", 13, Ui.Muted);
            var layout = new StackPanel { Width = 760 };
            foreach (var row in TypingKeyboard.Rows)
            {
                var caps = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                foreach (var cap in row)
                    caps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cap.Units, GridUnitType.Star) });
                for (int i = 0; i < row.Length; i++)
                {
                    Cap cap = row[i];
                    string label = cap.Base(locale), shifted = cap.Shifted(locale);
                    var hits = EvidenceFor(evidence, cap, locale);
                    var button = Ui.Button(label, delegate
                    {
                        string text = label == "Пробел" ? label : label + (shifted.Length == 0 || label == shifted ? "" : " / " + shifted);
                        UpdateKey(text, hits, pairs);
                    });
                    button.Height = button.MinHeight = 30;
                    button.Margin = new Thickness(0, 0, 3, 0);
                    button.Padding = new Thickness(3, 0, 3, 0);
                    button.FontFamily = Ui.Mono.FontFamily;
                    button.FontSize = label.Length > 6 ? 11 : 13;
                    double errorRate = hits.Length == 0 ? 0 : hits.Sum(x => x.Errors) / (double)Math.Max(1, hits.Sum(x => x.Correct + x.Errors));
                    button.Background = Ui.Brush(hits.Length == 0 ? "#263447" : errorRate > .12 ? "#844E59" :
                        errorRate >= .05 ? "#735641" : errorRate > .02 ? "#367077" : "#2B5C65");
                    button.BorderBrush = cap.Anchor ? Ui.Brush("#B2D8DC") : Ui.Brush("#4D6271");
                    Ui.Name(button, label + ": " + (hits.Length > 0 ? Math.Round((1 - errorRate) * 100) + "% точности" : "нет замеров"));
                    button.GotKeyboardFocus += delegate { UpdateKey(label, hits, pairs); };
                    Grid.SetColumn(button, i);
                    caps.Children.Add(button);
                }
                Ui.Add(layout, caps);
            }
            Ui.Add(stack, new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left, Child = layout }, 7);
            Ui.Add(stack, keyDetail);
            Ui.Add(content, Ui.Panel(stack, new Thickness(13), Ui.Brush("#152436")), 12);
        }

        private static KeyEvidence[] EvidenceFor(KeyEvidence[] all, Cap cap, Layout l)
        {
            string normal = cap.Base(l), shifted = cap.Shifted(l);
            if (cap.Key == Key.Space) normal = " ";
            if (cap.Key == Key.Enter) normal = "\n";
            if (cap.Key == Key.Back) normal = CodeText.Erase.ToString();
            return all.Where(x => x.Sequence == normal || x.Sequence == shifted).ToArray();
        }

        private void UpdateKey(string label, KeyEvidence[] entries, KeyEvidence[] pairs)
        {
            if (entries.Length == 0) { keyDetail.Text = label + " · данных пока нет."; return; }
            var ready = entries.Select(x => CodeText.Display(x.Sequence) + "  " + x.Accuracy.ToString("0.#") + "% (" +
                x.Correct + " верных, " + x.Errors + " ошибок)  ·  " +
                (x.Intervals == 0 ? "интервал не измерен" : Math.Round(x.Latency) + " мс между нажатиями") + "  ·  " +
                TrainingProgress.Mastery(x, profile));
            var matches = pairs.Where(p => entries.Any(o => p.Sequence.Contains(o.Sequence))).Take(4).ToArray();
            keyDetail.Text = string.Join("\n", ready) + (matches.Length == 0 ? "" : "\nТрудные пары: " + string.Join("  ·  ", matches.Select(Word)));
        }

        private void AddGoals()
        {
            var stack = new StackPanel();
            Ui.Add(stack, Ui.Text("Задания на сегодня", 20, Ui.Ink, true), 5);
            var today = profile.Results.Where(r => r.Date.Date == now.Date && r.Completed && r.Qualified).ToArray();
            int same = today.Where(x => x.Task != null).Select(x => x.Task).Distinct().Count();
            int recall = today.Count(r => r.Independent);
            int routes = profile.RouteResults.Count(r => r.Date.Date == now.Date && !r.Assisted);
            Goal(stack, "Два разных фрагмента с точностью от " + profile.Training.Goal + "%", same, 2,
                delegate { owner.ShowAtlas(locale == Layout.Russian ? "ru" : "python"); });
            Goal(stack, "Один точный раунд без схемы", recall, 1,
                delegate { owner.OpenExercise(ExerciseKind.Memory, locale == Layout.Russian ? "ru" : "qwerty"); });
            Goal(stack, "Один проверенный маршрут в рабочем окне", routes, 1, delegate { owner.OpenShortcuts("windows"); });
            Ui.Add(stack, Ui.Text("Задания обновляются каждый день. Пропущенные дни не влияют на прогресс.", 12, Ui.Muted), 9);
            Ui.Add(stack, Ui.Text("Достижения", 20, Ui.Ink, true), 5);
            Badge(stack, "Первый уровень", Lessons.All.Any(l => TrainingProgress.Cleared(l, profile)));
            Badge(stack, "Два алфавита", profile.Motor.Any(x => x.Correct > 0 && x.Independent && x.Locale == "EN") &&
                profile.Motor.Any(x => x.Correct > 0 && x.Independent && x.Locale == "RU"));
            Badge(stack, "Долгая память", profile.RecallDates.Any(d => d.Checks > 0));
            Badge(stack, "Работа без мыши", RouteSession.Routes.Count(r => profile.CompletedRoutes.Contains(r.PassId(2))) >= 3);
            Badge(stack, "Пройденный экзамен", Atlas.Regions.Any(r => profile.Exams.Contains(r.Id)));
            Badge(stack, "Победа над собой", profile.Awards.Any(m => m.Id.StartsWith("record/", StringComparison.Ordinal)));
            Ui.Add(content, Ui.Panel(stack, new Thickness(13), Ui.Brush("#142234")), 12);
        }

        private static void Goal(StackPanel stack, string text, int done, int total, Action action)
        {
            var button = Ui.QuietButton((done >= total ? "✓  " : "○  ") + text + "  ·  " + Math.Min(done, total) + "/" + total, action);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.Add(stack, button);
        }
        private static void Badge(StackPanel stack, string title, bool has)
        { Ui.Add(stack, Ui.Text((has ? "✓  " : "◇  ") + title, 14, has ? Ui.Brush("#AEE4C7") : Ui.Muted), 4); }

        private void AddRecords()
        {
            var stack = new StackPanel();
            Ui.Add(stack, Ui.Text("История личных рекордов", 20, Ui.Ink, true), 6);
            var records = profile.Records.AsEnumerable().Reverse().Take(16).ToArray();
            if (records.Length == 0) Ui.Add(stack, Ui.Text("Рекорды появятся после точных проходов; сопоставляются одинаковый текст и режим подсказок.", 14, Ui.Muted));
            foreach (var r in records)
                Ui.Add(stack, Ui.Text(r.Date.ToString("dd.MM HH:mm") + "  ·  " + r.Title + "  ·  " + r.Accuracy.ToString("0.0") + "%  ·  " +
                    r.Speed + " сим/мин" + (r.Independent ? "  ·  самостоятельно" : ""), 13, Ui.Muted), 5);
            Ui.Add(content, Ui.Panel(stack, new Thickness(13), Ui.Brush("#142234")), 8);
        }
    }

    internal sealed class TrendGraph : FrameworkElement
    {
        private readonly TypingResult[] runs;
        private readonly bool accuracy;
        public TrendGraph(TypingResult[] runs, bool accuracy)
        {
            this.runs = runs; this.accuracy = accuracy;
            Ui.Name(this, (accuracy ? "Динамика точности" : "Динамика скорости") + ": " + string.Join(", ", runs.Select(r => Format(Value(r)))));
        }
        private double Value(TypingResult item) { return accuracy ? item.Accuracy : item.Speed; }
        private string Format(double value) { return value.ToString(accuracy ? "0.#" : "0") + (accuracy ? "%" : ""); }
        protected override void OnRender(DrawingContext d)
        {
            if (ActualWidth < 90) return;
            double min = accuracy ? Math.Max(0, Math.Min(95, 5 * Math.Floor(runs.Min(r => r.Accuracy) / 5))) : 0;
            double max = accuracy ? 100 : Math.Max(50, 50 * Math.Ceiling(runs.Max(r => (double)r.Speed) / 50));
            double x0 = 42, width = ActualWidth - 53, h = ActualHeight - 39;
            d.DrawRoundedRectangle(Ui.Brush("#101B2B"), null, new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
            Text(d, accuracy ? "ТОЧНОСТЬ" : "СИМВОЛОВ / МИН", new Point(10, 5));
            for (int row = 0; row <= 2; row++)
            {
                double y = 25 + row * h / 2;
                d.DrawLine(new Pen(Ui.Brush("#2F4058"), 1), new Point(x0, y), new Point(x0 + width, y));
                Text(d, Format(max - (max - min) * row / 2), new Point(5, y - 6));
            }
            Point previous = new Point();
            for (int i = 0; i < runs.Length; i++)
            {
                var point = new Point(x0 + (runs.Length == 1 ? width / 2 : width * i / (runs.Length - 1)),
                    25 + h * (1 - (Value(runs[i]) - min) / (max - min)));
                var color = Ui.Brush(accuracy ? "#B9A6F5" : "#67CBBF");
                if (i > 0) d.DrawLine(new Pen(color, 1.7), previous, point);
                d.DrawEllipse(runs[i].Independent ? Ui.Ink : color, null, point, 3.2, 3.2);
                previous = point;
            }
        }
        private void Text(DrawingContext d, string text, Point point)
        {
            d.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Ui.Mono, 10, Ui.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
        }
    }
}
