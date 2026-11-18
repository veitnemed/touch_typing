using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TouchTypingStudio
{
    internal sealed class DesktopTrainingPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private readonly TextBlock notice;

        public DesktopTrainingPage(StudioWindow owner, StudioProfile profile, Action<DesktopTrack> start,
            int? completed = null, int rejected = 0, TimeSpan elapsed = default(TimeSpan), bool restoreFailed = false,
            DesktopTrack track = DesktopTrack.Console, string profileRoot = null)
        {
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var sheet = FlightUi.Sheet();
            Ui.Add(sheet, Ui.Text("Windows без мыши", 34, Ui.Ink, true), 6);
            Ui.Add(sheet, Ui.Text("Выберите маршрут. Откроется ваш рабочий стол, а внизу появится первое задание.",
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
                Ui.Add(sheet, Ui.Panel(session, new Thickness(16), Ui.Brush("#19293C")), 15);
            }

            var routes = new UniformGrid { Columns = 2 };
            var console = Route(routes, "Командная строка",
                "Отберите файлы, найдите сбой в журналах или соберите отчёт. В курсе 6 типов поручений и 30 рабочих тем.",
                "CMD  /  Проводник", "#A8E6D0", delegate { start(DesktopTrack.Console); }, ConsoleProgress(profileRoot));
            var workday = Route(routes, "Рабочий день",
                "Создайте проект, сохраните заметку и внесите правки. Первый шаг меняется от проекта к проекту, а созданные файлы пригодятся дальше.",
                "Windows  /  Chrome  /  VS Code  /  CMD", "#A7D6F6", delegate { start(DesktopTrack.Workday); });
            Ui.Add(sheet, routes, 10);
            Ui.Add(sheet, Ui.Text("Tab — выбрать маршрут    Enter — начать    Ctrl+Alt+F12 — завершить", 14, Ui.Muted), 16);

            var controls = new StackPanel { Margin = new Thickness(8, 10, 0, 6) };
            AddControl(controls, "Ctrl+Alt+F8", "Скрыть или показать сочетание клавиш и образец команды");
            AddControl(controls, "Ctrl+Alt+C", "Скопировать точный текст шага");
            AddControl(controls, "Ctrl+Alt+F7", "Пропустить необязательное приложение");
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

        private static Button Route(Panel routes, string name, string example, string apps, string accent, Action go,
            string progress = null)
        {
            var card = Ui.Button(name, go);
            card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.VerticalContentAlignment = VerticalAlignment.Stretch;
            card.Background = Ui.Brush("#19293C");
            card.BorderBrush = Ui.Brush("#394F63");
            card.Padding = new Thickness(18, 15, 18, 16);
            var content = new DockPanel();
            var tag = Ui.Text(apps, 12, Ui.Brush(accent), true);
            tag.FontFamily = Ui.Mono.FontFamily;
            tag.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(tag, Dock.Top);
            content.Children.Add(tag);
            var title = Ui.Text(name, 22, Ui.Ink, true);
            title.Margin = new Thickness(0, 0, 0, 7);
            DockPanel.SetDock(title, Dock.Top);
            content.Children.Add(title);
            if (!string.IsNullOrEmpty(progress))
            {
                var counter = Ui.Text(progress, 13, Ui.Brush(accent));
                counter.Margin = new Thickness(0, 10, 0, 0);
                DockPanel.SetDock(counter, Dock.Bottom);
                content.Children.Add(counter);
            }
            content.Children.Add(Ui.Text(example, 14, Ui.Muted));
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
