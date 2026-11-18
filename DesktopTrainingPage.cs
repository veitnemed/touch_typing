using System;
using System.Windows;
using System.Windows.Controls;

namespace TouchTypingStudio
{
    internal sealed class DesktopTrainingPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private readonly TextBlock notice;

        public DesktopTrainingPage(StudioWindow owner, StudioProfile profile, Action start,
            int? completed = null, int rejected = 0, TimeSpan elapsed = default(TimeSpan), bool restoreFailed = false)
        {
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, Ui.Text("Windows без мыши", 34, Ui.Ink, true), 7);
            if (completed.HasValue)
            {
                Ui.Add(stack, Ui.Text("Выполнено действий: " + completed.Value + "  ·  Время " +
                    elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"), 19, Ui.Accent, true), 13);
                if (completed.Value + rejected > 0)
                {
                    double speed = elapsed.TotalSeconds < 1 ? 0 : completed.Value / elapsed.TotalMinutes;
                    double percent = 100.0 * completed.Value / (completed.Value + rejected);
                    Ui.Add(stack, Ui.Text(speed.ToString("0.0") + " действий/мин  ·  " +
                        percent.ToString("0.0") + "% подтверждённых попыток", 15, Ui.Muted), 6);
                    var record = profile.PracticeRecords.Find(item => item.Id == "desktop/live");
                    if (record != null)
                        Ui.Add(stack, Ui.Text("Лучшая скорость " + record.Speed.ToString("0.0") + " действий/мин  ·  " +
                            "Лучшая точность " + record.Accuracy.ToString("0.0") + "%", 14, Ui.Accent), 10);
                    if (completed.Value < 12)
                        Ui.Add(stack, Ui.Text("Для рекорда нужно 12 подтверждённых действий.", 13, Ui.Muted), 8);
                }
            }
            var intro = new StackPanel();
            Ui.Add(intro, Ui.Text("Настоящий рабочий стол", 21, Ui.Ink, true), 7);
            Ui.Add(intro, Ui.Text("Откроется панель внизу. Выполняйте её команды в Windows. " +
                "Задания идут цепочкой и используют файлы из учебной папки.", 15, Ui.Muted), 12);
            Ui.Add(intro, Ui.Text("В цепочке есть рабочий стол и Пуск, Проводник, Блокнот, Chrome, VS Code и системные окна.", 14, Ui.Ink));
            Ui.Add(stack, Ui.Panel(intro, new Thickness(16), Ui.Brush("#19293C")), 13);
            AddControl(stack, "Ctrl+Alt+F8", "Показать или скрыть сочетание");
            AddControl(stack, "Ctrl+Alt+F10", "Переключить тачпад");
            AddControl(stack, "Ctrl+Alt+C", "Скопировать нужный путь или текст");
            AddControl(stack, "Ctrl+Alt+F9", "Начать следующий проект и сохранить предыдущие файлы");
            AddControl(stack, "Ctrl+Alt+F7", "Пропустить необязательное приложение");
            AddControl(stack, "Ctrl+Alt+F12", "Завершить тренировку");
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var launch = Ui.Button(completed.HasValue ? "Новый сеанс" : "Начать тренировку", start, true);
            Ui.Add(actions, launch);
            Ui.Add(actions, Ui.Button("К режимам", owner.ShowHome));
            Ui.Add(stack, actions, 8);
            notice = Ui.Text(restoreFailed
                ? "Проверьте тачпад в «Параметрах». С клавиатуры откройте Win+R, введите ms-settings:devices-touchpad и нажмите Enter."
                : "", 14, Ui.Brush("#FFD395"));
            Ui.Add(stack, notice);
            Content = stack;
            InitialFocus = launch;
        }

        public void ShowError(string text) { notice.Text = text; }

        private static void AddControl(Panel stack, string keys, string description)
        {
            var row = new DockPanel();
            var label = Ui.Text(keys, 14, Ui.Brush("#A8E6D0"), true);
            label.FontFamily = Ui.Mono.FontFamily;
            label.Width = 161;
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(Ui.Text(description, 14, Ui.Muted));
            Ui.Add(stack, row, 5);
        }
    }
}
