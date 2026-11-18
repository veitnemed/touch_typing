using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed class WorkbenchResults
    {
        public readonly int Commands, Misses;
        public readonly TimeSpan Duration;
        public readonly string[] CommonMisses;
        public double Accuracy { get { return Commands + Misses == 0 ? 0 : Commands * 100.0 / (Commands + Misses); } }
        public double CommandsPerMinute { get { return Duration.TotalSeconds < 1 ? 0 : Commands / Duration.TotalMinutes; } }

        public WorkbenchResults(int commands, int misses, TimeSpan duration, string[] commonMisses)
        {
            Commands = commands;
            Misses = misses;
            Duration = duration;
            CommonMisses = commonMisses;
        }
    }

    internal sealed class WorkbenchRecords
    {
        public readonly double BestCommandsPerMinute, BestAccuracy;
        public WorkbenchRecords(double bestCommandsPerMinute, double bestAccuracy)
        {
            BestCommandsPerMinute = bestCommandsPerMinute;
            BestAccuracy = bestAccuracy;
        }
    }

    internal sealed class WorkbenchPage : ScrollViewer
    {
        private readonly WorkbenchArea area;
        private readonly WorkbenchSession session;
        private readonly Action back;
        private readonly Action edit;
        private readonly Func<WorkbenchArea, WorkbenchResults, WorkbenchRecords> saveResult;
        private readonly Stopwatch activeTime = new Stopwatch();
        private readonly Dictionary<string, int> difficult = new Dictionary<string, int>();
        private readonly DispatcherTimer nextTimer;
        private readonly Grid panes;
        private readonly Border mission, surface, overlay, workspaceDetail;
        private readonly StackPanel overview, summary = new StackPanel();
        private readonly TextBlock sceneCaption, sceneLocation, contentTitle, contentBody, info, terminal;
        private readonly TextBlock missionArea, missionTitle, missionGoal, missionStep, shortcutHelp, inputLabel, inputMessage;
        private readonly WrapPanel keyCaps, tabs;
        private readonly StackPanel sceneItems;
        private readonly TextBox entry;
        private readonly Button difficultyButton;
        private static readonly string[] DifficultyNames = { "Начальный", "Обычный", "Без подсказок" };
        private int difficulty;
        private bool hints, stopped, stacked;
        private int commands, misses;
        private string localClipboard = "";

        private static readonly Brush Deep = Ui.Brush("#0B1526");
        private static readonly Brush Panel = Ui.Brush("#122037");
        private static readonly Brush Header = Ui.Brush("#1B2B44");
        private static readonly Brush Focused = Ui.Brush("#243D5B");
        private static readonly Brush Blue = Ui.Brush("#9BCFFD");
        private static readonly Brush Mint = Ui.Brush("#A4E8C8");

        public UIElement InitialFocus { get { return surface; } }

        public WorkbenchPage(WorkbenchArea area, IEnumerable<WorkbenchCustomTask> custom, Action back, Action edit = null,
            int difficulty = 1, bool customOnly = false, Func<WorkbenchArea, WorkbenchResults, WorkbenchRecords> saveResult = null)
        {
            this.area = area;
            this.back = back;
            this.edit = edit;
            this.saveResult = saveResult;
            this.difficulty = Math.Max(0, Math.Min(2, difficulty));
            hints = this.difficulty < 2;
            session = new WorkbenchSession(area, custom, this.difficulty, customOnly);
            Background = Deep;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Focusable = false;

            var root = new StackPanel { Margin = new Thickness(24, 19, 24, 25), MaxWidth = 1230 };
            overview = new StackPanel();
            var headerStack = new StackPanel();
            Ui.Add(headerStack, Ui.Text(WorkbenchTasks.AreaTitle(area), 13, Blue, true), 3);
            Ui.Add(headerStack, Ui.Text(area == WorkbenchArea.Shortcuts ? "Горячие клавиши" : "Рабочая среда", 34, Ui.Ink, true), 4);
            Ui.Add(headerStack, Ui.Text("Учебные окна и файлы внутри тренажёра", 14, Ui.Muted), 10);
            Ui.Add(overview, headerStack);

            var bar = new WrapPanel();
            Ui.Add(bar, Ui.QuietButton("‹ К режимам", back));
            Ui.Add(bar, Ui.Button("Подсказка  F1", ToggleHints));
            Ui.Add(bar, Ui.Button("Пропустить  F8", Continue));
            Ui.Add(bar, Ui.Button("Завершить  F12", Stop));
            difficultyButton = Ui.QuietButton("Уровень  ·  " + DifficultyNames[this.difficulty], CycleDifficulty);
            difficultyButton.ToolTip = "Переключить сложность учебных заданий";
            Ui.Add(bar, difficultyButton);
            if (edit != null) Ui.Add(bar, Ui.QuietButton("Свои сочетания", Edit));
            Ui.Add(overview, bar, 11);

            panes = new Grid();
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            panes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var task = new StackPanel();
            missionArea = Ui.Text("", 13, Blue, true);
            missionTitle = Ui.Text("", 23, Ui.Ink, true);
            missionStep = Ui.Text("", 12, Ui.Muted, true);
            missionGoal = Ui.Text("", 17);
            shortcutHelp = Ui.Text("", 13, Ui.Muted);
            keyCaps = new WrapPanel();
            Ui.Add(task, missionArea, 10);
            Ui.Add(task, missionTitle, 11);
            Ui.Add(task, missionStep, 6);
            Ui.Add(task, missionGoal, 18);
            Ui.Add(task, keyCaps, 7);
            Ui.Add(task, shortcutHelp);
            mission = Ui.Panel(task, new Thickness(17), Panel);
            panes.Children.Add(mission);

            var scene = new StackPanel();
            var desktopBar = new DockPanel { Background = Header };
            sceneCaption = Ui.Text("", 14, Ui.Ink, true);
            sceneCaption.Margin = new Thickness(13, 8, 13, 8);
            desktopBar.Children.Add(sceneCaption);
            Ui.Add(scene, desktopBar);
            sceneLocation = Ui.Text("", 13, Blue);
            sceneLocation.FontFamily = Ui.Mono.FontFamily;
            var address = Ui.Panel(sceneLocation, new Thickness(10, 4, 10, 4), Deep);
            address.Margin = new Thickness(12, 11, 12, 9);
            Ui.Add(scene, address);
            tabs = new WrapPanel { Margin = new Thickness(12, 0, 7, 0) };
            Ui.Add(scene, tabs);

            var inside = new DockPanel { Margin = new Thickness(12, 1, 12, 13), LastChildFill = true, MinHeight = 158 };
            sceneItems = new StackPanel { MaxWidth = 180, MinWidth = 105, Margin = new Thickness(0, 1, 10, 0) };
            DockPanel.SetDock(sceneItems, Dock.Left);
            inside.Children.Add(sceneItems);
            var content = new StackPanel();
            contentTitle = Ui.Text("", 21, Ui.Ink, true);
            contentBody = Ui.Text("", 14, Ui.Ink);
            info = Ui.Text("", 13, Mint);
            terminal = Ui.Text("ТЕРМИНАЛ\nPS C:\\Lab\\demo> _", 13, Mint);
            terminal.FontFamily = Ui.Mono.FontFamily;
            workspaceDetail = Ui.Panel(terminal, new Thickness(8, 6, 8, 6), Deep);
            Ui.Add(content, contentTitle, 9);
            Ui.Add(content, contentBody, 8);
            Ui.Add(content, workspaceDetail, 8);
            Ui.Add(content, info);
            inside.Children.Add(Ui.Panel(content, new Thickness(12, 10, 12, 10), Deep));
            Ui.Add(scene, inside);

            var prompt = new StackPanel();
            inputLabel = Ui.Text("", 15, Blue, true);
            inputMessage = Ui.Text("", 13, Ui.Muted);
            entry = Ui.Editor("", 17);
            entry.FontFamily = Ui.Mono.FontFamily;
            entry.AcceptsReturn = false;
            entry.AllowDrop = false;
            entry.MaxLength = 220;
            CommandManager.AddPreviewCanExecuteHandler(entry, delegate(object sender, CanExecuteRoutedEventArgs e)
            {
                if (IsClipboardCommand(e.Command)) { e.CanExecute = true; e.Handled = true; }
            });
            CommandManager.AddPreviewExecutedHandler(entry, delegate(object sender, ExecutedRoutedEventArgs e)
            {
                if (!IsClipboardCommand(e.Command)) return;
                if (e.Command == ApplicationCommands.Paste)
                {
                    int caret = entry.SelectionStart;
                    entry.SelectedText = localClipboard;
                    entry.Select(Math.Min(caret + localClipboard.Length, entry.Text.Length), 0);
                }
                else if (entry.SelectionLength > 0)
                {
                    localClipboard = entry.SelectedText;
                    if (e.Command == ApplicationCommands.Cut) entry.SelectedText = "";
                }
                e.Handled = true;
            });
            Ui.Name(entry, "Поле команды");
            Ui.Add(prompt, inputLabel, 6);
            Ui.Add(prompt, entry, 7);
            Ui.Add(prompt, inputMessage);
            overlay = Ui.Panel(prompt, new Thickness(12, 9, 12, 9), Header);
            overlay.Margin = new Thickness(12, 0, 12, 12);
            Ui.Add(scene, overlay);

            surface = Ui.Panel(scene, new Thickness(0), Panel);
            surface.Focusable = true;
            KeyboardNavigation.SetIsTabStop(surface, true);
            Ui.Name(surface, "Учебное окно. Клавиши обрабатываются в тренажёре.");
            surface.GotKeyboardFocus += delegate { surface.BorderBrush = Blue; activeTime.Start(); };
            surface.LostKeyboardFocus += delegate { surface.BorderBrush = Ui.Border; };
            surface.PreviewMouseDown += delegate
            {
                if (!session.NeedsInput) surface.Focus();
            };
            panes.Children.Add(surface);
            LayoutPanes(false, true);
            Ui.Add(overview, panes, 12);
            Ui.Add(overview, Ui.Text("Esc закрывает поле команды или завершает тренировку.", 12, Ui.Muted), 3);
            Ui.Add(root, overview);
            Ui.Add(root, summary);
            summary.Visibility = Visibility.Collapsed;
            Content = root;

            nextTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
            nextTimer.Tick += delegate { Continue(); };
            Loaded += delegate { Refocus(); };
            Unloaded += delegate { activeTime.Stop(); nextTimer.Stop(); };
            SizeChanged += delegate { LayoutPanes(ActualWidth < 810, false); };
            PreviewKeyDown += HandleKey;
            Render();
        }

        private void LayoutPanes(bool vertical, bool force)
        {
            if (vertical == stacked && !force) return;
            stacked = vertical;
            Grid.SetRow(surface, vertical ? 1 : 0);
            Grid.SetColumn(surface, vertical ? 0 : 1);
            Grid.SetColumnSpan(surface, vertical ? 2 : 1);
            Grid.SetColumnSpan(mission, vertical ? 2 : 1);
            mission.Margin = vertical ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 12, 0);
        }

        private static bool IsClipboardCommand(ICommand command)
        {
            return command == ApplicationCommands.Cut || command == ApplicationCommands.Copy || command == ApplicationCommands.Paste;
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (stopped || e.Handled) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin) return;

            if (session.NeedsInput)
            {
                if (modifiers != ModifierKeys.None) return;
                if (key == Key.Escape)
                {
                    session.CancelInput();
                    Render();
                    Refocus();
                    e.Handled = true;
                }
                else if (key == Key.Return)
                {
                    e.Handled = true;
                    if (!e.IsRepeat && session.Submit(entry.Text)) StepDone();
                    else if (!e.IsRepeat)
                    {
                        CountMiss();
                        inputMessage.Text = "Сверьте введённое значение с заданием и нажмите Enter.";
                    }
                }
                return;
            }
            if (e.IsRepeat)
            {
                if (surface.IsKeyboardFocused) e.Handled = true;
                return;
            }

            var step = session.Step;
            if (step != null && surface.IsKeyboardFocused && session.Press(key, modifiers))
            {
                e.Handled = true;
                if (session.Step != step) StepDone();
                else if (session.NeedsInput)
                {
                    entry.Clear();
                    Render();
                    Refocus();
                }
                else Render();
                return;
            }

            if (modifiers == ModifierKeys.None)
            {
                switch (key)
                {
                    case Key.F1: ToggleHints(); e.Handled = true; return;
                    case Key.F8: Continue(); e.Handled = true; return;
                    case Key.F12: case Key.Escape: Stop(); e.Handled = true; return;
                    case Key.Return:
                        if (step == null) { Continue(); e.Handled = true; return; }
                        break;
                    case Key.Tab: return;
                }
            }
            if (surface.IsKeyboardFocused && step != null)
            {
                if (e.Key != Key.ImeProcessed && e.Key != Key.DeadCharProcessed && e.Key != Key.None) CountMiss();
                e.Handled = true;
            }
        }

        private void StepDone()
        {
            commands++;
            Render();
            Refocus();
            if (session.Step == null) nextTimer.Start();
        }

        private void CountMiss()
        {
            misses++;
            string label = session.Step.Chord.Label;
            int value;
            difficult.TryGetValue(label, out value);
            difficult[label] = value + 1;
        }

        private void ToggleHints()
        {
            hints = !hints;
            Render();
            Refocus();
        }

        private void CycleDifficulty()
        {
            difficulty = (difficulty + 1) % DifficultyNames.Length;
            difficultyButton.Content = "Уровень  ·  " + DifficultyNames[difficulty];
            session.SetDifficulty(difficulty);
            hints = difficulty < 2;
            Render();
            Refocus();
        }

        private void Continue()
        {
            if (stopped) return;
            nextTimer.Stop();
            hints = difficulty < 2;
            session.NextTask();
            Render();
            Refocus();
        }

        private void Edit()
        {
            if (edit != null) edit();
        }

        private void Refocus()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (!IsVisible || stopped) return;
                if (session.NeedsInput) entry.Focus();
                else surface.Focus();
            }));
        }

        private void Render()
        {
            var task = session.Task;
            var step = session.Step;
            var scene = task.Scene;
            missionArea.Text = scene.Area == WorkbenchArea.Shortcuts ? scene.Location : WorkbenchTasks.AreaTitle(scene.Area);
            missionTitle.Text = task.Title;
            missionStep.Text = step == null ? "Задание выполнено" : "Шаг " + (session.StepIndex + 1) + " из " + task.Steps.Length;
            missionStep.Foreground = step == null ? Mint : Ui.Muted;
            missionGoal.Text = step == null ? "Открываем следующее задание…" : step.Goal;
            keyCaps.Children.Clear();
            shortcutHelp.Text = "";
            if (step != null && !session.NeedsInput)
            {
                var chord = step.Chord;
                bool mapped = chord.Label != chord.PracticeLabel;
                if (hints || mapped)
                {
                    foreach (var part in chord.PracticeLabel.Split('+'))
                    {
                        var label = Ui.Text(part, 16, Blue, true);
                        label.FontFamily = Ui.Mono.FontFamily;
                        var cap = Ui.Panel(label, new Thickness(8, 5, 8, 5), Focused);
                        cap.Margin = new Thickness(0, 0, 5, 4);
                        keyCaps.Children.Add(cap);
                    }
                    if (mapped) shortcutHelp.Text = "Системное сочетание " + chord.Label + " заменено в учебном окне.";
                }
                else shortcutHelp.Text = "F1 — показать сочетание.";
            }

            sceneCaption.Text = (scene.Area == WorkbenchArea.Shortcuts ? "" : WorkbenchTasks.AreaTitle(scene.Area) + "  /  ") + scene.Caption;
            sceneLocation.Text = string.IsNullOrEmpty(scene.Location) ? "Новый адрес" : scene.Location;
            contentTitle.Text = scene.Headline;
            contentBody.Text = scene.Body;
            contentBody.FontFamily = scene.Area == WorkbenchArea.Code ? Ui.Mono.FontFamily : FontFamily;
            info.Text = scene.Detail;
            info.Visibility = string.IsNullOrEmpty(scene.Detail) ? Visibility.Collapsed : Visibility.Visible;
            workspaceDetail.Visibility = scene.Terminal ? Visibility.Visible : Visibility.Collapsed;
            sceneItems.Children.Clear();
            tabs.Children.Clear();
            bool hasTabs = scene.Area == WorkbenchArea.Chrome;
            for (int i = 0; i < scene.Items.Count; i++)
            {
                var text = Ui.Text(scene.Items[i], 13, i == scene.Selection ? Ui.Ink : Ui.Muted);
                var node = Ui.Panel(text, new Thickness(8, 4, 8, 4), i == scene.Selection ? Focused : Panel);
                node.Margin = hasTabs ? new Thickness(0, 0, 5, 7) : new Thickness(0, 0, 0, 5);
                if (hasTabs) tabs.Children.Add(node);
                else sceneItems.Children.Add(node);
            }
            tabs.Visibility = hasTabs ? Visibility.Visible : Visibility.Collapsed;
            sceneItems.Visibility = !hasTabs && scene.Sidebar && scene.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            overlay.Visibility = session.NeedsInput ? Visibility.Visible : Visibility.Collapsed;
            if (session.NeedsInput)
            {
                inputLabel.Text = step.EntryPrompt;
                inputMessage.Text = "Enter — применить   ·   Esc — закрыть поле";
            }
            AutomationProperties.SetName(surface, "Учебное окно. " + scene.Headline + ". " + missionGoal.Text);
        }

        private void Stop()
        {
            if (stopped) return;
            stopped = true;
            nextTimer.Stop();
            activeTime.Stop();
            var result = new WorkbenchResults(commands, misses, activeTime.Elapsed,
                difficult.OrderByDescending(pair => pair.Value).Take(3).Select(pair => pair.Key).ToArray());
            WorkbenchRecords record = saveResult == null ? null : saveResult(area, result);
            overview.Visibility = Visibility.Collapsed;
            summary.Children.Clear();
            summary.Visibility = Visibility.Visible;
            Ui.Add(summary, Ui.Text(WorkbenchTasks.AreaTitle(area), 13, Blue, true), 7);
            Ui.Add(summary, Ui.Text("Итоги тренировки", 34, Ui.Ink, true), 18);
            var metrics = new WrapPanel();
            AddMetric(metrics, "Команды", result.Commands.ToString());
            AddMetric(metrics, "Команд в минуту", result.CommandsPerMinute.ToString("F1"));
            AddMetric(metrics, "Точность", result.Accuracy.ToString("F1") + "%");
            AddMetric(metrics, "Длительность", ((int)result.Duration.TotalMinutes).ToString("00") + ":" + result.Duration.Seconds.ToString("00"));
            Ui.Add(summary, metrics, 15);
            if (result.CommonMisses.Length > 0)
                Ui.Add(summary, Ui.Text("Повторить  " + string.Join("  ·  ", result.CommonMisses), 15, Ui.Muted), 9);
            if (record != null && record.BestCommandsPerMinute > 0)
                Ui.Add(summary, Ui.Text("Рекорды режима  " + record.BestCommandsPerMinute.ToString("F1") + " команд/мин  ·  " +
                    record.BestAccuracy.ToString("F1") + "% точность", 15, Mint), 14);
            var finish = Ui.Button("К режимам", back, true);
            finish.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.Add(summary, finish);
            ScrollToTop();
            finish.Focus();
        }

        private static void AddMetric(WrapPanel panel, string label, string value)
        {
            var metric = new StackPanel { MinWidth = 135 };
            Ui.Add(metric, Ui.Text(value, 24, Blue, true), 3);
            Ui.Add(metric, Ui.Text(label, 13, Ui.Muted));
            var card = Ui.Panel(metric, new Thickness(13), Panel);
            card.Margin = new Thickness(0, 0, 10, 7);
            panel.Children.Add(card);
        }
    }
}
