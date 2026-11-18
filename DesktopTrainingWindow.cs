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
        internal const int PanelHeight = 166;
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
        private bool hotkeyConflict;
        private bool cleaned;
        private int mouseBaseline;

        public int Completed { get { return course.Completed; } }
        public int Rejected { get { return course.Rejected; } }
        public TimeSpan Elapsed { get { return elapsed.Elapsed; } }
        public bool RestoreFailed { get; private set; }

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
                localNotice = "Windows не разрешила отслеживать сочетания. " + error.Message;
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
            if (!Native.RegisterHotKey(source.Handle, id, CtrlAlt, key)) hotkeyConflict = true;
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
            if (course.SkipOptional()) { localNotice = null; ActivateMission(); return; }
            localNotice = "Этот шаг входит в основной маршрут.";
            Render();
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
            try
            {
                Clipboard.SetText(course.CopyText);
                localNotice = "Скопировано. Вставьте текст в рабочее окно через Ctrl+V.";
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
            if (string.IsNullOrEmpty(notice) && hotkeyConflict)
                notice = "Часть сочетаний панели занята другой программой. Используйте кнопки панели.";
            if (string.IsNullOrEmpty(notice) && dock != null && !dock.Reserved)
                notice = "Панель поверх окон. Windows не выделила для неё отдельное место.";
            view.Set(course.MissionStep, course.MissionLength, course.Goal, course.Action, course.Shortcut, course.Detail, notice,
                device, hints, course.CopyText != null, course.CopyText, course.RoundNumber, course.CanSkipOptional,
                track == DesktopTrack.Console ? "Командная строка" : "Рабочий день", course.IsConsoleCommand);
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
        private readonly TextBlock stepCount, goal, action, keys, detail, notice, device, literal;
        private readonly Border keycap, example;
        private readonly Button hintButton, copyButton, skipButton;
        private readonly Grid progress;
        private readonly ColumnDefinition filled, remaining;
        private readonly WrapPanel controls;
        private readonly StackPanel content;
        private string previousDetail, previousExample;

        public DesktopStrip(Action hints, Action copy, Action recover, Action stop, Action skip = null)
        {
            Background = Ui.Brush("#131D29");
            BorderBrush = Ui.Brush("#475B6C");
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(18, 9, 18, 6);
            content = new StackPanel();

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            stepCount = Ui.Text("", 13, Ui.Brush("#A8E6D0"), true);
            stepCount.FontFamily = Ui.Mono.FontFamily;
            stepCount.Margin = new Thickness(14, 0, 0, 0);
            DockPanel.SetDock(stepCount, Dock.Right);
            header.Children.Add(stepCount);
            goal = Ui.Text("", 15, Ui.Brush("#AAC5DB"), true);
            header.Children.Add(goal);
            Ui.Add(content, header);

            progress = new Grid { Height = 2, Background = Ui.Brush("#344758"), Margin = new Thickness(0, 0, 0, 6) };
            filled = new ColumnDefinition();
            remaining = new ColumnDefinition();
            progress.ColumnDefinitions.Add(filled);
            progress.ColumnDefinitions.Add(remaining);
            progress.Children.Add(new Border { Background = Ui.Brush("#83CFB8") });
            Ui.Add(content, progress);

            var instruction = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            keys = Ui.Text("", 19, Ui.Brush("#112C24"), true);
            keys.FontFamily = Ui.Mono.FontFamily;
            keycap = Ui.Panel(keys, new Thickness(10, 4, 10, 4), Ui.Brush("#A8E6D0"));
            keycap.CornerRadius = new CornerRadius(5);
            keycap.BorderBrush = Ui.Brush("#C8F3E4");
            keycap.VerticalAlignment = VerticalAlignment.Top;
            keycap.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(keycap, Dock.Right);
            instruction.Children.Add(keycap);
            var words = new StackPanel();
            action = Ui.Text("", 21, Ui.Ink, true);
            Ui.Add(words, action);
            detail = Ui.Text("", 14, Ui.Muted);
            Ui.Add(words, detail);
            instruction.Children.Add(words);
            Ui.Add(content, instruction);

            literal = Ui.Text("", 16, Ui.Brush("#C0EFE2"));
            literal.FontFamily = Ui.Mono.FontFamily;
            Ui.Name(literal, "Текст для ввода");
            var entry = new DockPanel();
            var enter = Ui.Text("Введите", 13, Ui.Brush("#90A9AF"));
            enter.Margin = new Thickness(0, 2, 12, 0);
            DockPanel.SetDock(enter, Dock.Left);
            entry.Children.Add(enter);
            entry.Children.Add(literal);
            example = Ui.Panel(entry, new Thickness(9, 4, 9, 4), Ui.Brush("#09141D"));
            example.CornerRadius = new CornerRadius(5);
            example.BorderBrush = Ui.Brush("#374D58");
            example.Margin = new Thickness(0, 3, 0, 3);
            Ui.Add(content, example);

            var tools = new DockPanel { LastChildFill = true };
            device = Ui.Text("", 12, Ui.Brush("#A9CDE0"));
            device.MaxWidth = 240;
            device.VerticalAlignment = VerticalAlignment.Center;
            device.Margin = new Thickness(12, 2, 0, 2);
            DockPanel.SetDock(device, Dock.Right);
            tools.Children.Add(device);
            controls = new WrapPanel();
            hintButton = Control("Скрыть клавиши", "Ctrl+Alt+F8", hints);
            copyButton = Control("Копировать текст", "Ctrl+Alt+C", copy);
            skipButton = Control("Пропустить", "Ctrl+Alt+F7", skip ?? delegate { });
            Control("Новый проект", "Ctrl+Alt+F9", recover);
            Control("Завершить", "Ctrl+Alt+F12", stop);
            tools.Children.Add(controls);
            Ui.Add(content, tools);
            notice = Ui.Text("", 13, Ui.Brush("#FFD395"));
            Ui.Add(content, notice);
            Child = content;
        }

        private Button Control(string title, string chord, Action action)
        {
            var button = Ui.QuietButton(title, action);
            button.FontSize = 12;
            button.MinHeight = 23;
            button.Padding = new Thickness(3, 2, 3, 2);
            button.Margin = new Thickness(0, 1, 13, 0);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.ToolTip = title + " · " + chord;
            if (title == "Копировать текст" || title == "Завершить") button.Content = title + " · " + chord;
            Ui.Name(button, title + " · " + chord);
            Ui.Add(controls, button);
            return button;
        }

        public void Set(int stage, int length, string task, string next, string shortcut, string text, string hint,
            string touch, bool visible, bool canCopy, string copyText = null, int round = 1, bool canSkip = false,
            string route = "Рабочий день", bool commandExample = false)
        {
            Change(stepCount, stage + " / " + length);
            Change(goal, route + "  /  Проект " + round + "  ·  " + task);
            if (filled.Width.Value != stage - 1 || remaining.Width.Value != length - stage + 1)
            {
                filled.Width = new GridLength(Math.Max(0, stage - 1), GridUnitType.Star);
                remaining.Width = new GridLength(Math.Max(0, length - stage + 1), GridUnitType.Star);
            }
            Change(action, next);
            Change(keys, shortcut);
            keycap.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (text != previousDetail || copyText != previousExample)
            {
                previousDetail = text;
                previousExample = copyText;
                string caption = (text ?? "").Replace("\r\n", "\n");
                string sample = (copyText ?? "").Replace("\r\n", "\n");
                if (sample.Length > 0 && caption.EndsWith("\n" + sample, StringComparison.Ordinal))
                    caption = caption.Substring(0, caption.Length - sample.Length).TrimEnd();
                Change(detail, caption);
                detail.Visibility = caption.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                Change(literal, sample);
            }
            // Payloads such as paths must stay available in recall mode. Console learners
            // can recall the command from the mission and reveal it with the same hotkey.
            bool showExample = !string.IsNullOrEmpty(copyText) && (visible || !commandExample);
            example.Visibility = showExample ? Visibility.Visible : Visibility.Collapsed;
            Change(notice, hint);
            notice.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
            notice.Foreground = (hint ?? "").StartsWith("Шаг зачтён", StringComparison.Ordinal)
                ? Ui.Brush("#A8E6D0") : Ui.Brush("#FFD395");
            Change(device, touch);
            string toggleLabel = (visible ? "Скрыть" : "Показать") + " подсказку · Ctrl+Alt+F8";
            if (!Equals(hintButton.Content, toggleLabel))
            {
                hintButton.Content = toggleLabel;
                Ui.Name(hintButton, toggleLabel);
                hintButton.ToolTip = toggleLabel;
            }
            copyButton.Visibility = canCopy ? Visibility.Visible : Visibility.Collapsed;
            skipButton.Visibility = canSkip ? Visibility.Visible : Visibility.Collapsed;
        }

        public int RequiredHeight(double width)
        {
            double available = Math.Max(1, width - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right);
            content.Measure(new Size(available, double.PositiveInfinity));
            return (int)Math.Ceiling(content.DesiredSize.Height + Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom);
        }

        private static void Change(TextBlock block, string value)
        {
            if (block.Text != (value ?? "")) block.Text = value ?? "";
        }
    }
}
