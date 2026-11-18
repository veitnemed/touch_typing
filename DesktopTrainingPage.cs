using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed class DesktopTrainingPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private readonly TextBlock notice;

        public DesktopTrainingPage(StudioWindow owner, StudioProfile profile, Action<DesktopTrack> start,
            int? completed = null, int rejected = 0, TimeSpan elapsed = default(TimeSpan), bool restoreFailed = false,
            DesktopTrack track = DesktopTrack.Workday, string profileRoot = null)
        {
            Background = new LinearGradientBrush(Color.FromRgb(29, 32, 39), Color.FromRgb(41, 46, 56), 24);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var sheet = new StackPanel { MaxWidth = 912, Margin = new Thickness(32, 25, 32, 23) };
            Ui.Add(sheet, Ui.Text("Windows без мыши", 38, Ui.Ink, true), 5);
            Ui.Add(sheet, Ui.Text("Выполняйте поручения в своих рабочих окнах. Задание будет видно внизу экрана.",
                16, Ui.Muted), 22);

            if (completed.HasValue)
            {
                string title = track == DesktopTrack.Console ? "Командная строка" : "Рабочий день";
                var session = new StackPanel();
                Ui.Add(session, Ui.Text(title + "  ·  Итог тренировки", 17, Ui.Ink, true), 4);
                Ui.Add(session, Ui.Text("Подтверждено действий  " + completed.Value + "     Время  " +
                    elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"), 15, Ui.Accent), 3);
                if (completed.Value + rejected > 0)
                {
                    double speed = elapsed.TotalSeconds < 1 ? 0 : completed.Value / elapsed.TotalMinutes;
                    double percent = 100.0 * completed.Value / (completed.Value + rejected);
                    Ui.Add(session, Ui.Text(speed.ToString("0.0") + " действий/мин  ·  " +
                        percent.ToString("0.0") + "% подтверждённых попыток", 14, Ui.Muted), 3);
                }
                if (completed.Value < 12)
                    Ui.Add(session, Ui.Text("Рекорд сохраняется от 12 подтверждённых действий.", 13, Ui.Muted));
                var record = profile.PracticeRecords.Find(item => item.Id == RecordId(track));
                if (record != null)
                    Ui.Add(session, Ui.Text("Лучшая скорость " + record.Speed.ToString("0.0") + " действий/мин  ·  " +
                        "Лучшая точность " + record.Accuracy.ToString("0.0") + "%", 14, Ui.Accent));
                Ui.Add(sheet, Ui.Panel(session, new Thickness(16), Ui.Brush("#30363F")), 15);
            }

            var routes = new StackPanel();
            var workday = Route(routes, "Рабочий день",
                "Передайте файл коллеге, вернитесь к заметке, сравните две вкладки и сохраните правку в редакторе.",
                true, delegate { start(DesktopTrack.Workday); });
            var console = Route(routes, "Командная строка",
                "Когда файлов много, одна команда может выбрать нужные строки и собрать общий отчёт.",
                false, delegate { start(DesktopTrack.Console); }, ConsoleProgress(profileRoot));
            Ui.Add(sheet, routes, 9);
            Ui.Add(sheet, Ui.Text("Tab — выбор     Enter — начать     Ctrl+Alt+F7 — другое задание", 14, Ui.Muted), 18);

            var controls = new StackPanel { Margin = new Thickness(8, 10, 0, 6) };
            AddControl(controls, "Ctrl+Alt+F8", "Скрыть или показать сочетание клавиш и образец команды");
            AddControl(controls, "Ctrl+Alt+C", "Скопировать точный текст шага");
            AddControl(controls, "Ctrl+Alt+F7", "Перейти к другому заданию, сохранив выполненные действия");
            AddControl(controls, "Ctrl+Alt+F9", "Перейти к новому проекту и сохранить учебные файлы");
            AddControl(controls, "Ctrl+Alt+F10", "Выключить или включить тачпад");
            AddControl(controls, "Ctrl+Alt+F12", "Завершить и посмотреть результат");
            var help = new Expander { Header = "Управление во время тренировки", Content = controls,
                Foreground = Ui.Ink, FontSize = 15, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Ui.Name(help, "Управление во время тренировки");
            Ui.Add(sheet, help, 14);
            var back = Ui.QuietButton("К выбору тренировок", owner.ShowHome);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.Add(sheet, back);
            notice = Ui.Text(restoreFailed
                ? "Проверьте тачпад в параметрах Windows. Нажмите Win+R, введите ms-settings:devices-touchpad и нажмите Enter."
                : "", 14, Ui.Brush("#FFD395"));
            Ui.Add(sheet, notice);
            Content = sheet;
            InitialFocus = track == DesktopTrack.Console ? console : workday;
        }

        internal static string RecordId(DesktopTrack track)
        {
            return track == DesktopTrack.Console ? "desktop/console" : "desktop/live";
        }

        private static string ConsoleProgress(string root)
        {
            if (root == null) return null;
            try
            {
                int passed = new DesktopItinerary(root).CompletedConsoleMissions;
                if (passed < 3) return "Поручения  " + passed + " / 3   ·   Дальше — связки команд через |";
                if (passed < 6) return "Поручения  " + passed + " / 6   ·   Дальше — упаковка в ZIP";
                return "Поручений выполнено  " + passed + "   ·   Доступны связки команд и архивы";
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        public void ShowError(string text)
        {
            notice.Text = text;
            notice.BringIntoView();
        }

        private static Button Route(Panel routes, string name, string example, bool workday, Action go,
            string progress = null)
        {
            var card = Ui.Button(name, go);
            card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.VerticalContentAlignment = VerticalAlignment.Stretch;
            card.Background = Ui.Brush(workday ? "#C0DBCC" : "#30353E");
            card.BorderBrush = Ui.Brush(workday ? "#8FAE9D" : "#494F58");
            card.Padding = new Thickness(21, workday ? 22 : 16, 22, workday ? 22 : 16);
            card.Margin = new Thickness(0, 0, 0, 10);
            var content = new DockPanel();
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0) };
            foreach (var key in (workday ? new[] { "Alt", "Tab" } : new[] { ">_" }))
            {
                var glyph = Ui.Text(key, workday ? 19 : 17, Ui.Brush(workday ? "#214236" : "#E8CED6"), true);
                glyph.FontFamily = Ui.Mono.FontFamily;
                glyph.TextAlignment = TextAlignment.Center;
                var cap = Ui.Panel(glyph, new Thickness(10, 5, 10, 6), Ui.Brush(workday ? "#DBEBE1" : "#403B46"));
                cap.CornerRadius = new CornerRadius(6);
                cap.BorderBrush = Ui.Brush(workday ? "#799588" : "#635766");
                cap.BorderThickness = new Thickness(1, 1, 1, 3);
                cap.Margin = new Thickness(2, 0, 2, 0);
                keys.Children.Add(cap);
            }
            DockPanel.SetDock(keys, Dock.Right);
            content.Children.Add(keys);
            var body = new StackPanel();
            if (workday)
                Ui.Add(body, Ui.Text("Проводник   /   Блокнот   /   Chrome   /   VS Code", 12, Ui.Brush("#385B4B"), true), 4);
            Ui.Add(body, Ui.Text(name, workday ? 29 : 21, workday ? Ui.Brush("#182C23") : Ui.Ink, true), 4);
            Ui.Add(body, Ui.Text(example, 15, workday ? Ui.Brush("#2D473B") : Ui.Muted));
            if (!string.IsNullOrEmpty(progress))
            {
                var counter = Ui.Text(progress, 12, Ui.Brush("#D8BFCB"));
                counter.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(counter);
            }
            content.Children.Add(body);
            card.Content = content;
            Ui.Name(card, name);
            Ui.Add(routes, card);
            return card;
        }

        private static void AddControl(Panel stack, string keys, string description)
        {
            var row = new DockPanel();
            var label = Ui.Text(keys, 14, Ui.Brush("#A8E6D0"), true);
            label.FontFamily = Ui.Mono.FontFamily;
            label.Width = 145;
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(Ui.Text(description, 14, Ui.Muted));
            Ui.Add(stack, row, 5);
        }
    }
}
