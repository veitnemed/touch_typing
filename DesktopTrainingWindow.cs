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

        public DesktopTrainingWindow(string root, IntPtr anchor)
        {
            this.anchor = anchor;
            try { leased = lease.WaitOne(0); }
            catch (AbandonedMutexException) { leased = true; }
            if (!leased)
            {
                lease.Dispose();
                throw new InvalidOperationException("Тренировка Windows уже запущена в другом окне.");
            }
            try { course = new DesktopCourse(root); }
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
            view = new DesktopStrip(ToggleHints, Copy, Recover, Close);
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
                input = new DesktopInput { IgnoreWindow = source.Handle };
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
                case SkipKey:
                    if (course.SkipOptional()) { localNotice = null; if (input != null) Watch(); }
                    else localNotice = "Этот шаг входит в основное задание.";
                    Render();
                    break;
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
                Watch();
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
                localNotice = "Скопировано. Вставьте в указанное поле через Ctrl+V.";
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
            if (course.Recover()) { localNotice = null; Watch(); }
            else localNotice = "Вернитесь к нужному окну и повторите показанный шаг.";
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
            view.Set(course.StageNumber, course.StageLength, course.Goal, course.Action, course.Shortcut, course.Detail, notice,
                device, hints, course.CopyText != null);
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
            ReleaseLease();
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
        private readonly TextBlock section, goal, action, keys, detail, notice, device;
        private readonly Border keycap;
        private readonly Button hintButton, copyButton;

        public DesktopStrip(Action hints, Action copy, Action recover, Action stop)
        {
            Background = Ui.Brush("#101826");
            BorderBrush = Ui.Brush("#556981");
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(16, 9, 16, 6);
            var shell = new DockPanel();
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(bar, Dock.Top);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            hintButton = Ui.QuietButton("Клавиши", hints);
            hintButton.ToolTip = "Показать или скрыть сочетание · Ctrl+Alt+F8";
            copyButton = Ui.QuietButton("Копировать", copy);
            copyButton.ToolTip = "Копировать значение текущего задания · Ctrl+Alt+C";
            var retry = Ui.QuietButton("Новый проект", recover);
            retry.ToolTip = "Начать следующий учебный проект · Ctrl+Alt+F9";
            var end = Ui.QuietButton("Завершить", stop);
            end.ToolTip = "Завершить тренировку · Ctrl+Alt+F12";
            Ui.Add(buttons, hintButton); Ui.Add(buttons, copyButton); Ui.Add(buttons, retry); Ui.Add(buttons, end);
            bar.Children.Add(buttons);
            section = Ui.Text("WINDOWS БЕЗ МЫШИ", 13, Ui.Brush("#A7D6F6"), true);
            section.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(section);
            shell.Children.Add(bar);

            var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            device = Ui.Text("", 12, Ui.Brush("#A9CDE0"));
            device.TextWrapping = TextWrapping.NoWrap;
            DockPanel.SetDock(device, Dock.Right);
            footer.Children.Add(device);
            notice = Ui.Text("", 12, Ui.Brush("#FFD395"));
            notice.TextTrimming = TextTrimming.CharacterEllipsis;
            notice.TextWrapping = TextWrapping.NoWrap;
            footer.Children.Add(notice);
            shell.Children.Add(footer);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            var task = new StackPanel { Margin = new Thickness(1, 4, 12, 0) };
            Ui.Add(task, Ui.Text("ЗАДАЧА", 11, Ui.Brush("#88AEC5"), true), 3);
            goal = Ui.Text("", 17, Ui.Ink, true);
            goal.MaxHeight = 48;
            Ui.Add(task, goal);
            row.Children.Add(task);
            var step = new DockPanel { LastChildFill = true };
            keys = Ui.Text("", 18, Ui.Brush("#102331"), true);
            keys.FontFamily = Ui.Mono.FontFamily;
            keycap = Ui.Panel(keys, new Thickness(11, 6, 11, 6), Ui.Brush("#A8E6D0"));
            keycap.BorderBrush = Ui.Brush("#CCF4E6");
            keycap.VerticalAlignment = VerticalAlignment.Center;
            keycap.MaxWidth = 300;
            keycap.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(keycap, Dock.Right);
            step.Children.Add(keycap);
            var captions = new StackPanel();
            action = Ui.Text("", 16, Ui.Ink, true);
            action.MaxHeight = 43;
            Ui.Add(captions, action, 2);
            detail = Ui.Text("", 12, Ui.Muted);
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.MaxHeight = 54;
            Ui.Add(captions, detail);
            step.Children.Add(captions);
            var card = Ui.Panel(step, new Thickness(10, 5, 10, 5), Ui.Brush("#1A293C"));
            Grid.SetColumn(card, 1);
            row.Children.Add(card);
            shell.Children.Add(row);
            Child = shell;
        }

        public void Set(int stage, int length, string task, string next, string shortcut, string text, string hint,
            string touch, bool visible, bool canCopy)
        {
            Change(section, "WINDOWS БЕЗ МЫШИ   /   ШАГ " + stage + " ИЗ " + length);
            Change(goal, task);
            Change(action, next);
            Change(keys, shortcut);
            Change(detail, text);
            detail.ToolTip = text;
            Change(notice, hint);
            notice.Foreground = (hint ?? "").StartsWith("Шаг зачтён", StringComparison.Ordinal)
                ? Ui.Brush("#A8E6D0") : Ui.Brush("#FFD395");
            Change(device, touch);
            keycap.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            hintButton.Content = visible ? "Скрыть клавиши" : "Показать клавиши";
            copyButton.IsEnabled = canCopy;
        }

        private static void Change(TextBlock block, string value)
        {
            if (block.Text != (value ?? "")) block.Text = value ?? "";
        }
    }
}
