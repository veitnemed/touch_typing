using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed class DesktopTrainingWindow : Window
    {
        internal const int PanelHeight = 110;
        private const int StopKey = 0x5400, HintsKey = 0x5401, CopyKey = 0x5402, RecoverKey = 0x5403, SkipKey = 0x5404;
        private const uint CtrlAlt = 0x4003; // CTRL | ALT | MOD_NOREPEAT
        private readonly IntPtr anchor;
        private readonly DesktopCourse course;
        private readonly DesktopTrack track;
        private readonly bool allowAutomation;
        private readonly DesktopStrip view;
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly DispatcherTimer timer;
        private readonly Mutex lease = new Mutex(false, @"Local\TouchTypingStudio.LiveDesktop");
        private bool leased, hints = true;
        private HwndSource source;
        private DesktopDock dock;
        private DesktopInput input;
        private TouchpadSwitch touchpad;
        private string localNotice;
        private readonly System.Collections.Generic.List<string> unavailableKeys = new System.Collections.Generic.List<string>();
        private bool cleaned;
        private int mouseBaseline;

        public int Completed { get { return course.Completed; } }
        public int Rejected { get { return course.Rejected; } }
        public TimeSpan Elapsed { get { return elapsed.Elapsed; } }
        public bool RestoreFailed { get; private set; }
        public string StartError { get; private set; }

        public DesktopTrainingWindow(string root, IntPtr anchor, DesktopTrack track = DesktopTrack.Workday, bool allowAutomation = false)
        {
            this.anchor = anchor;
            this.track = track;
            this.allowAutomation = allowAutomation;
            try { leased = lease.WaitOne(0); }
            catch (AbandonedMutexException) { leased = true; }
            if (!leased)
            {
                lease.Dispose();
                throw new InvalidOperationException("Тренировка Windows уже запущена в другом окне.");
            }
            try { course = new DesktopCourse(root, track); }
            catch { ReleaseLease(); throw; }
            Title = "Windows без мыши — подсказка";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 900;
            Height = PanelHeight;
            UseLayoutRounding = true;
            Background = Ui.Brush("#101826");
            Foreground = Ui.Ink;
            FontFamily = new FontFamily("Segoe UI");
            view = new DesktopStrip(ToggleHints, Copy, Recover, Close, Skip);
            Content = view;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(170) };
            timer.Tick += delegate { Poll(); };
            SourceInitialized += delegate { Start(); };
            Render();
        }

        private void Start()
        {
            source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(Message);
            dock = new DesktopDock(this, source.Handle, anchor, PanelHeight);
            try
            {
                input = new DesktopInput(allowAutomation) { IgnoreWindow = source.Handle };
                Watch();
            }
            catch (Exception error)
            {
                if (!(error is Win32Exception) && !(error is ArgumentException)) throw;
                if (input != null) input.Dispose();
                input = null;
                StartError = error.Message + " Можно повторить запуск или выбрать «Горячие клавиши» на главном экране.";
                return;
            }
            touchpad = new TouchpadSwitch(source.Handle);
            touchpad.StatusChanged += Render;
            Register(StopKey, 0x7B);
            Register(HintsKey, 0x77);
            Register(CopyKey, (uint)'C');
            Register(RecoverKey, 0x78);
            Register(SkipKey, 0x76);
            elapsed.Start();
            timer.Start();
            Render();
        }

        private void Register(int id, uint key)
        {
            if (!Native.RegisterHotKey(source.Handle, id, CtrlAlt, key))
                unavailableKeys.Add("Ctrl+Alt+" + (key >= 0x70 && key <= 0x7B ? "F" + (key - 0x6F) : ((char)key).ToString()));
        }

        private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (dock != null && dock.HandleMessage(message, wParam))
            {
                handled = true;
                return new IntPtr(3); // MA_NOACTIVATE
            }
            if (touchpad != null && touchpad.ProcessMessage(message, wParam))
            {
                handled = true;
                return IntPtr.Zero;
            }
            if (message != 0x0312) return IntPtr.Zero;
            switch (wParam.ToInt32())
            {
                case StopKey: Close(); break;
                case HintsKey: ToggleHints(); break;
                case CopyKey: Copy(); break;
                case RecoverKey: Recover(); break;
                case SkipKey: Skip(); break;
                default: return IntPtr.Zero;
            }
            handled = true;
            return IntPtr.Zero;
        }

        private void Watch()
        {
            input.Watch(course.ObservedShortcuts);
            mouseBaseline = input.MouseClicks;
        }

        public void ActivateMission()
        {
            if (input == null || cleaned) return;
            course.ActivateMission();
            Watch();
            Render();
        }

        private void Skip()
        {
            course.SkipMission();
            localNotice = null;
            ActivateMission();
        }

        private void Poll()
        {
            if (dock != null) dock.KeepVisible();
            if (input == null) return;
            if (input.MouseClicks > mouseBaseline)
            {
                mouseBaseline = input.MouseClicks;
                course.RejectMouseAction();
                localNotice = "Для зачёта выполните текущую команду с клавиатуры.";
            }
            DesktopStroke stroke;
            while (input.TryRead(out stroke))
                if (input.MouseClicks == stroke.MouseClicks)
                {
                    localNotice = null;
                    course.Key(stroke);
                }
            if (course.Poll(Native.GetForegroundWindow()))
            {
                localNotice = null;
                ActivateMission();
                return;
            }
            Render();
        }

        private void ToggleHints()
        {
            hints = !hints;
            Render();
        }

        private void Copy()
        {
            if (course.CopyText == null) return;
            if (course.IsConsoleCommand && !hints) return;
            try
            {
                Clipboard.SetText(course.CopyText);
                localNotice = course.IsConsoleCommand
                    ? "Команда в буфере обмена. Вставьте её в CMD через Ctrl+V."
                    : "Текст в буфере обмена. Вставьте его в рабочее окно через Ctrl+V.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                localNotice = "Буфер обмена занят. Повторите или введите значение вручную.";
            }
            Render();
        }

        private void Recover()
        {
            if (input == null) return;
            if (course.Recover()) { localNotice = null; ActivateMission(); return; }
            localNotice = "Вернитесь к нужному окну и повторите показанный шаг.";
            Render();
        }

        private void Render()
        {
            string device = touchpad == null ? "Тачпад · Ctrl+Alt+F10" : touchpad.Status;
            string notice = localNotice ?? course.Notice;
            if (unavailableKeys.Count != 0)
                device = "Занято " + string.Join(", ", unavailableKeys) + ". Доступны кнопки панели. " + device;
            if (dock != null && !dock.Reserved)
                device += " · Панель поверх окон";
            view.Set(course.MissionStep, course.MissionLength, course.Goal,
                course.WaitingForWindow ? "Перейдите в открывшееся учебное окно" : course.Action,
                course.WaitingForWindow ? "" : course.Shortcut, course.WaitingForWindow ? "" : course.Detail, notice,
                device, hints, course.CopyText != null, course.CopyText,
                track == DesktopTrack.Console ? "Командная строка" : "Рабочий день", course.IsConsoleCommand,
                course.StepSource, course.StepDestination, course.StepOutcome);
            if (dock != null && ActualWidth > 0)
                dock.SetHeight(Math.Max(PanelHeight, view.RequiredHeight(ActualWidth)));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!e.Cancel) CleanUp();
        }

        private void CleanUp()
        {
            if (cleaned) return;
            cleaned = true;
            timer.Stop();
            elapsed.Stop();
            if (input != null) input.Dispose();
            if (touchpad != null)
            {
                touchpad.StatusChanged -= Render;
                touchpad.Dispose();
                RestoreFailed = touchpad.RestoreFailed;
            }
            if (source != null)
            {
                foreach (int id in new[] { StopKey, HintsKey, CopyKey, RecoverKey, SkipKey })
                    Native.UnregisterHotKey(source.Handle, id);
                if (!source.IsDisposed) source.RemoveHook(Message);
            }
            if (dock != null) dock.Dispose();
            try { course.Dispose(); }
            finally { ReleaseLease(); }
        }

        private void ReleaseLease()
        {
            if (leased) lease.ReleaseMutex();
            leased = false;
            lease.Dispose();
        }
    }

    internal sealed class DesktopStrip : Border
    {
        private readonly StackPanel content, keycaps;
        private readonly Grid body;
        private readonly StackPanel instruction;
        private readonly Border flow, example, progressFill;
        private readonly TextBlock routeText, goal, stepCount, action, detail, literal, entryLabel;
        private readonly TextBlock from, to, outcome, notice, device;
        private readonly Button hintButton, copyButton;
        private readonly WrapPanel controls;
        private bool hasFlow, stacked, arranged;
        private int lastStage, lastLength;
        private string previousText, previousSample, previousChord;

        public DesktopStrip(Action hints, Action copy, Action recover, Action stop, Action skip = null)
        {
            Background = Ui.Brush("#20232B");
            BorderBrush = Ui.Brush("#494E59");
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(20, 8, 20, 6);
            SnapsToDevicePixels = true;
            content = new StackPanel { MaxWidth = 1600 };
            Child = content;

            var header = new DockPanel();
            var counter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stepCount = Ui.Text("", 12, Ui.Brush("#C5D0CC"));
            counter.Children.Add(stepCount);
            var track = new Border { Width = 60, Height = 3, CornerRadius = new CornerRadius(1.5),
                Background = Ui.Brush("#454953"), Margin = new Thickness(8, 0, 0, 0) };
            progressFill = new Border { Background = Ui.Brush("#9EDCC0"), CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = progressFill;
            counter.Children.Add(track);
            DockPanel.SetDock(counter, Dock.Right);
            header.Children.Add(counter);
            routeText = Ui.Text("", 13, Ui.Brush("#A5D8BF"), true);
            routeText.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(routeText, Dock.Left);
            header.Children.Add(routeText);
            goal = Ui.Text("", 15, Ui.Brush("#E9E7E3"), true);
            goal.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(goal);
            Ui.Add(content, header, 6);

            body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            instruction = new StackPanel();
            body.Children.Add(instruction);
            var headline = new DockPanel();
            keycaps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 1, 0, 0) };
            DockPanel.SetDock(keycaps, Dock.Right);
            headline.Children.Add(keycaps);
            action = Ui.Text("", 22, Ui.Brush("#FFFDF7"), true);
            headline.Children.Add(action);
            Ui.Add(instruction, headline);
            detail = Ui.Text("", 14, Ui.Brush("#BCC2CB"));
            detail.Margin = new Thickness(0, 2, 0, 0);
            Ui.Add(instruction, detail);

            var entry = new DockPanel();
            entryLabel = Ui.Text("", 12, Ui.Brush("#9FB9A9"), true);
            entryLabel.Margin = new Thickness(0, 2, 10, 0);
            DockPanel.SetDock(entryLabel, Dock.Left);
            entry.Children.Add(entryLabel);
            literal = Ui.Text("", 16, Ui.Brush("#D5F0DF"));
            literal.FontFamily = Ui.Mono.FontFamily;
            entry.Children.Add(literal);
            example = new Border { Child = entry, CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 4, 9, 4),
                Background = Ui.Brush("#171A1E"), BorderBrush = Ui.Brush("#434E49"), BorderThickness = new Thickness(2, 0, 0, 0),
                Margin = new Thickness(0, 5, 0, 0) };
            Ui.Add(instruction, example);

            var fileCard = new StackPanel();
            var transfer = new Grid();
            transfer.ColumnDefinitions.Add(new ColumnDefinition());
            transfer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            transfer.ColumnDefinitions.Add(new ColumnDefinition());
            from = PathBox(transfer, 0, "Источник", "#F0CF9C");
            var arrow = Ui.Text("→", 20, Ui.Brush("#979BA5"));
            arrow.Margin = new Thickness(10, 0, 10, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(arrow, 1);
            transfer.Children.Add(arrow);
            to = PathBox(transfer, 2, "Назначение", "#D4C2EA");
            Ui.Add(fileCard, transfer);
            outcome = Ui.Text("", 14, Ui.Brush("#E6E5E0"));
            outcome.Margin = new Thickness(0, 6, 0, 0);
            Ui.Add(fileCard, outcome);
            flow = new Border { Child = fileCard, Background = Ui.Brush("#2B2F37"), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 7, 12, 8), VerticalAlignment = VerticalAlignment.Top };
            body.Children.Add(flow);
            Ui.Add(content, body);

            notice = Ui.Text("", 13, Ui.Brush("#F4CA8E"), true);
            notice.Margin = new Thickness(0, 4, 0, 0);
            Ui.Add(content, notice);
            var footer = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
            device = Ui.Text("", 11, Ui.Brush("#ACB2BC"));
            device.MaxWidth = 192;
            device.VerticalAlignment = VerticalAlignment.Center;
            device.Margin = new Thickness(7, 0, 0, 0);
            DockPanel.SetDock(device, Dock.Right);
            footer.Children.Add(device);
            var modifiers = Ui.Text("Ctrl + Alt", 12, Ui.Brush("#A7B0BB"), true);
            modifiers.VerticalAlignment = VerticalAlignment.Center;
            var modifiersBox = new Border { Child = modifiers, BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = Ui.Brush("#48505A"), Padding = new Thickness(0, 0, 9, 0),
                Margin = new Thickness(0, 3, 8, 3), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(modifiersBox, Dock.Left);
            footer.Children.Add(modifiersBox);
            controls = new WrapPanel();
            hintButton = Control("Подсказка", "Ctrl+Alt+F8", hints);
            copyButton = Control("Скопировать текст", "Ctrl+Alt+C", copy);
            Control("К другой задаче", "Ctrl+Alt+F7", skip ?? delegate { });
            Control("Новый проект", "Ctrl+Alt+F9", recover);
            Control("Завершить", "Ctrl+Alt+F12", stop);
            footer.Children.Add(controls);
            Ui.Add(content, footer);
        }

        private static TextBlock PathBox(Grid parent, int column, string label, string color)
        {
            var box = new StackPanel();
            box.Children.Add(Ui.Text(label, 12, Ui.Brush(color), true));
            var path = Ui.Text("", 15, Ui.Brush("#F4F0E9"));
            path.FontFamily = Ui.Mono.FontFamily;
            box.Children.Add(path);
            Grid.SetColumn(box, column);
            parent.Children.Add(box);
            return path;
        }

        private Button Control(string label, string shortcut, Action click)
        {
            var button = Ui.QuietButton(label, click);
            button.FontSize = 12;
            button.Foreground = Ui.Brush("#CED1D7");
            button.MinHeight = 21;
            button.Padding = new Thickness(2, 1, 2, 1);
            button.Margin = new Thickness(0, 0, 14, 0);
            Label(button, label, shortcut);
            Ui.Add(controls, button);
            return button;
        }

        private static void Label(Button button, string title, string chord)
        {
            string full = title + " · " + chord;
            if (Equals(button.ToolTip, full)) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var key = Ui.Text(chord.Substring(chord.LastIndexOf('+') + 1), 12, Ui.Brush("#DEE3DE"), true);
            key.FontFamily = Ui.Mono.FontFamily;
            row.Children.Add(new Border { Child = key, Background = Ui.Brush("#383E45"), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 5, 0) });
            row.Children.Add(Ui.Text(title, 12, Ui.Brush("#C1C6CE")));
            button.Content = row;
            Ui.Name(button, full);
            button.ToolTip = full;
        }

        private void SetChord(string shortcut)
        {
            if (previousChord == shortcut) return;
            previousChord = shortcut;
            keycaps.Children.Clear();
            var parts = (shortcut ?? "").Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool main = i == parts.Length - 1;
                var key = Ui.Text(parts[i].Trim(), 16, Ui.Brush(main ? "#12251B" : "#F0EFE7"), true);
                key.FontFamily = Ui.Mono.FontFamily;
                key.HorizontalAlignment = HorizontalAlignment.Center;
                var cap = new Border { Child = key, Background = Ui.Brush(main ? "#AADFC2" : "#444B51"),
                    BorderBrush = Ui.Brush(main ? "#78A78B" : "#30353D"), BorderThickness = new Thickness(0, 0, 0, 2),
                    CornerRadius = new CornerRadius(5), MinWidth = 31, Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0) };
                keycaps.Children.Add(cap);
            }
        }

        public void Set(int stage, int length, string task, string next, string shortcut, string text, string hint,
            string touch, bool visible, bool canCopy, string copyText = null,
            string route = "Рабочий день", bool commandExample = false, string source = null, string dest = null, string result = null)
        {
            Change(routeText, route);
            Change(goal, task);
            Change(stepCount, "Шаг " + stage + " из " + length);
            if (lastStage != stage || lastLength != length)
            {
                progressFill.Width = 60.0 * (stage - 1) / Math.Max(1, length);
                lastStage = stage;
                lastLength = length;
            }

            Change(action, next);
            SetChord(shortcut);
            keycaps.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (previousText != text || previousSample != copyText)
            {
                previousText = text;
                previousSample = copyText;
                string caption = (text ?? "").Replace("\r\n", "\n");
                string sample = (copyText ?? "").Replace("\r\n", "\n");
                if (sample.Length > 0 && caption.EndsWith("\n" + sample, StringComparison.Ordinal))
                    caption = caption.Substring(0, caption.Length - sample.Length).TrimEnd();
                Change(detail, caption);
                detail.Visibility = caption.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                Change(literal, sample);
            }
            Change(entryLabel, commandExample ? "В CMD" : "Введите");
            example.Visibility = !string.IsNullOrEmpty(copyText) && (visible || !commandExample) ? Visibility.Visible : Visibility.Collapsed;
            Change(from, source);
            Change(to, dest);
            Change(outcome, result);
            outcome.Visibility = string.IsNullOrEmpty(result) ? Visibility.Collapsed : Visibility.Visible;
            bool showFlow = !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(dest);
            if (hasFlow != showFlow || !arranged) ArrangeSections(showFlow, stacked);

            // Progress and the next action already signal a successful or newly started step.
            bool showNotice = !string.IsNullOrWhiteSpace(hint) && hint != "Шаг зачтён." &&
                hint != "Начните с указанного сочетания." && hint != "Введите команду в учебной командной строке и нажмите Enter.";
            Change(notice, showNotice ? hint : "");
            notice.Visibility = showNotice ? Visibility.Visible : Visibility.Collapsed;
            Change(device, touch);
            Label(hintButton, visible ? "Скрыть подсказку" : "Вернуть подсказку", "Ctrl+Alt+F8");
            Label(copyButton, commandExample ? "Скопировать команду" : "Скопировать текст", "Ctrl+Alt+C");
            copyButton.Visibility = canCopy && (visible || !commandExample) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ArrangeSections(bool showFlow, bool narrow)
        {
            hasFlow = showFlow;
            stacked = narrow;
            arranged = true;
            flow.Visibility = hasFlow ? Visibility.Visible : Visibility.Collapsed;
            bool beside = hasFlow && !stacked;
            body.ColumnDefinitions[0].Width = new GridLength(beside ? 6 : 1, GridUnitType.Star);
            body.ColumnDefinitions[1].Width = beside ? new GridLength(4, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(flow, beside ? 0 : 1);
            Grid.SetColumn(flow, beside ? 1 : 0);
            flow.Margin = beside ? new Thickness(18, 0, 0, 0) : new Thickness(0, 5, 0, 0);
        }

        public int RequiredHeight(double width)
        {
            double available = Math.Min(content.MaxWidth, Math.Max(1, width - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right));
            bool narrow = available < 710;
            if (narrow != stacked) ArrangeSections(hasFlow, narrow);
            content.Measure(new Size(available, double.PositiveInfinity));
            return (int)Math.Ceiling(content.DesiredSize.Height + Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom);
        }

        private static void Change(TextBlock block, string value)
        {
            if (block.Text != (value ?? "")) block.Text = value ?? "";
        }
    }
}
