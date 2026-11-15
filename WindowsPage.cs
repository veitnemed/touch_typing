using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed class WindowsPage : ScrollViewer
    {
        private readonly StudioWindow owner;
        private readonly ProfileStore store;
        private readonly ListBox list;
        private readonly Button stageButton;
        private readonly Stopwatch routeTimer = new Stopwatch();
        private RouteSpec[] shown = RouteSession.Routes.Where(r => r.Category == "Windows").ToArray();
        private int stageIndex;
        private readonly TextBlock details, info, result;
        private readonly TextBlock routeStatus;
        private readonly Button start, stop, hint, retry, copy;
        private readonly Button nextExam;
        private Territory exam;
        private string examNext;
        private string routeContext;
        private readonly DispatcherTimer poll;
        private readonly Mutex activeRoute = new Mutex(false, @"Local\TouchTypingStudio.ActiveRoute");

        private bool lockOwned, hintsVisible, assisted, savedCardMoved;
        private RouteSession route;
        private RouteInput input;
        private HintCard card;
        private Window first, second;
        private HwndSource hotkeySource;
        private bool hotkeys;
        private string editorStatus = "";
        private IntPtr lastTarget;
        private IntPtr ownerHandle;

        public bool HasRoute { get { return route != null && input != null; } }

        public WindowsPage(StudioWindow owner, ProfileStore store)
        {
            this.owner = owner;
            this.store = store;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var page = new StackPanel { Margin = new Thickness(25, 16, 25, 18), MaxWidth = 1010 };
            Ui.Add(page, Ui.Text("Windows без мыши", 29, Ui.Ink, true), 4);
            Ui.Add(page, Ui.Text("Выберите сценарий. Enter открывает учебное окно.", 15, Ui.Muted), 10);
            var groups = new WrapPanel();
            foreach (string category in new[] { "Windows", "Проводник", "Текст", "VS Code" })
            {
                string group = category;
                Ui.Add(groups, Ui.QuietButton(group, delegate { SelectCategory(group); }));
            }
            Ui.Add(page, groups, 8);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
            list = new ListBox { Background = Ui.Night, Foreground = Ui.Ink, BorderBrush = Ui.Border, FontSize = 16,
                ItemContainerStyle = ListItemStyle(), Margin = new Thickness(0,0,12,0), MinHeight = 242, MaxHeight = 384 };
            Ui.Name(list, "Маршруты Windows");
            grid.Children.Add(list);

            var right = new StackPanel();
            details = Ui.Text("", 18, Ui.Ink, true);
            Ui.Add(right, details, 10);
            stageButton = Ui.Button("", delegate
            {
                stageIndex = (stageIndex + 1) % StageNames.Length;
                UpdatePanel();
            });
            Ui.Add(right, stageButton, 7);
            info = Ui.Text("", 14, Ui.Muted);
            Ui.Add(right, info, 10);
            var actions = new WrapPanel();
            start = Ui.Button("Начать · Enter", Start, true);
            stop = Ui.Button("Остановить · Esc", Cancel);
            hint = Ui.Button("Подсказка · F1", FlipHints);
            retry = Ui.Button("Открыть учебное окно снова · F7", ReopenTarget);
            copy = Ui.Button("Копировать учебный путь · F6", CopyPath);
            Ui.Add(actions, start);
            Ui.Add(actions, stop);
            Ui.Add(actions, hint);
            Ui.Add(actions, retry);
            Ui.Add(actions, copy);
            nextExam = Ui.Button("Далее в экзамене", delegate
            {
                if (exam == null) return;
                if (examNext == null) owner.ShowAtlas(exam.Id);
                else ChooseRoute(examNext, exam);
            }, true);
            Ui.Add(actions, nextExam);
            Ui.Add(right, actions);
            var detailCard = Ui.Panel(right, new Thickness(16));
            Grid.SetColumn(detailCard, 1);
            grid.Children.Add(detailCard);
            Ui.Add(page, grid, 12);

            result = Ui.Text("", 15, Ui.Accent, true);
            AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
            Ui.Add(page, result, 8);
            routeStatus = Ui.Text("", 15, Ui.Ink);
            Ui.Add(page, routeStatus, 8);
            Ui.Add(page, Ui.Text("Ctrl+Alt+H — подсказка в задании   ·   Alt+Tab, Esc — закончить", 13, Ui.Muted), 7);
            Content = page;

            poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(135) };
            poll.Tick += delegate { Poll(); };
            list.SelectionChanged += delegate
            {
                if (!HasRoute && list.SelectedIndex >= 0)
                {
                    ChooseStage();
                    UpdatePanel();
                }
            };
            Populate(0);
            ChooseStage();
            UpdatePanel();
        }

        private static Style ListItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 8, 7)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
            return style;
        }

        private void Populate(int selected)
        {
            var saved = store.Read();
            list.Items.Clear();
            foreach (var item in shown)
            {
                int complete = Enumerable.Range(0, 3).Count(i => saved.CompletedRoutes.Contains(item.PassId(i)));
                list.Items.Add(item.Name + "    " + complete + " / 3");
            }
            list.SelectedIndex = Math.Max(0, Math.Min(selected, shown.Length - 1));
        }

        private RouteSpec Selected { get { return shown[Math.Max(0, list.SelectedIndex)]; } }
        private int Stage { get { return stageIndex; } }
        private static readonly string[] StageNames =
        {
            "1. Показ последовательности", "2. Подсказка по запросу", "3. Самостоятельно"
        };

        private void ChooseStage()
        {
            if (exam != null) { stageIndex = 2; return; }
            var saved = store.Read();
            int firstIncomplete = Enumerable.Range(0, 3).FirstOrDefault(i => !saved.CompletedRoutes.Contains(Selected.PassId(i)));
            if (saved.CompletedRoutes.Contains(Selected.PassId(firstIncomplete))) firstIncomplete = 2;
            stageIndex = firstIncomplete;
        }

        public void ChooseRoute(string id, Territory examRegion = null, string context = null)
        {
            if (HasRoute) return;
            var spec = RouteSession.Routes.FirstOrDefault(r => r.Kind.ToString() == id);
            if (spec == null) return;
            exam = examRegion;
            examNext = id;
            routeContext = context;
            result.Text = "";
            shown = RouteSession.Routes.Where(r => r.Category == spec.Category).ToArray();
            Populate(Array.IndexOf(shown, spec));
            RefreshOnOpen();
        }

        private void SelectCategory(string name)
        {
            if (HasRoute) return;
            exam = null;
            examNext = null;
            routeContext = null;
            shown = RouteSession.Routes.Where(r => r.Category == name).ToArray();
            Populate(0);
            RefreshOnOpen();
        }

        public void RefreshOnOpen()
        {
            if (!HasRoute) { Populate(list.SelectedIndex); ChooseStage(); }
            UpdatePanel();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsVisible || !owner.IsActive) return;
                if (HasRoute) stop.Focus();
                else if (exam != null) (examNext == Selected.Kind.ToString() ? start : nextExam).Focus();
                else
                {
                    list.Focus();
                    var item = list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as ListBoxItem;
                    if (item != null) item.Focus();
                }
            }), DispatcherPriority.Input);
        }

        private void UpdatePanel()
        {
            if (details == null || start == null) return;
            bool running = HasRoute;
            details.Text = running ? route.Goal() : Selected.Goal;
            if (running) info.Text = route.Spec.Kind == RouteKind.Explorer ? "Учебная папка\n" + route.Folder : "";
            else
            {
                string[] stagesHelp =
                {
                    "Карточка показывает действия по порядку.",
                    "Карточка показывает цель. F1 или Ctrl+Alt+H открывает подсказку.",
                    "Карточка показывает цель и результат проверки. Открытая подсказка переведёт попытку в практику."
                };
                var previous = store.Read().RouteResults.LastOrDefault(item => item.Route == Selected.Kind.ToString() &&
                    item.Stage == Stage && !item.Assisted);
                info.Text = stagesHelp[Stage] + (previous == null ? "" : "\nПоследний проход — " + Duration(previous.Seconds));
                if (exam != null) info.Text = "Экзамен · " + exam.Name + "\n" + info.Text;
            }
            stageButton.Content = StageNames[Stage] + "  ↻";
            Ui.Name(stageButton, StageNames[Stage] + ". Нажмите пробел, чтобы сменить стадию.");
            list.IsEnabled = stageButton.IsEnabled = start.IsEnabled = !running;
            list.IsEnabled = stageButton.IsEnabled = !running && exam == null;
            bool advance = !running && exam != null && examNext != Selected.Kind.ToString();
            start.Visibility = running || advance ? Visibility.Collapsed : Visibility.Visible;
            nextExam.Visibility = advance ? Visibility.Visible : Visibility.Collapsed;
            nextExam.Content = examNext == null ? "На карту · Enter" : "Следующий маршрут · Enter";
            stop.IsEnabled = hint.IsEnabled = copy.IsEnabled = running;
            retry.IsEnabled = running && !route.HasSampleWindows && route.Spec.Kind != RouteKind.StartMenu &&
                (route.Spec.Kind != RouteKind.Explorer || route.Step == 0);
            retry.Content = running && route.Spec.Kind == RouteKind.Explorer
                ? "Проводник для разминки · F7" : "Открыть учебное окно снова · F7";
            stop.Visibility = hint.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            retry.Visibility = retry.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            copy.Visibility = running && route.Spec.Kind == RouteKind.Explorer ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Start()
        {
            if (HasRoute) return;
            if (exam != null && examNext != Selected.Kind.ToString())
            {
                if (examNext == null) owner.ShowAtlas(exam.Id);
                else ChooseRoute(examNext, exam);
                return;
            }
            CloseAttempt();
            result.Text = "";
            try
            {
                try { lockOwned = activeRoute.WaitOne(0); }
                catch (AbandonedMutexException) { lockOwned = true; }
                if (!lockOwned) { result.Text = "Задание уже идёт в другом окне тренажёра."; return; }
                route = new RouteSession(Selected, Stage, store.Root);
                ownerHandle = Native.Handle(owner);
                route.FromWindow = ownerHandle;
                hintsVisible = Stage == 0;
                assisted = false;
                savedCardMoved = false;
                editorStatus = "";
                lastTarget = IntPtr.Zero;
                card = new HintCard(store.Read());
                var handle = new WindowInteropHelper(card).EnsureHandle();
                input = new RouteInput(route.Spec.Kind == RouteKind.Layout ? new Func<IntPtr, bool>(route.IsLayoutExerciseWindow) : null)
                    { IgnoreMain = ownerHandle, IgnoreCard = handle };
                RegisterCardKeys();
                UpdateCard();
                card.Show();
                if (double.IsNaN(card.Left) || !card.HasSavedLocation)
                    Native.PlaceCard(Native.Handle(card), true);
                else
                    Native.PlaceCard(Native.Handle(card), false);

                switch (route.Spec.Kind)
                {
                    case RouteKind.Switch:
                    case RouteKind.Snap:
                        ShowSwitchWindows();
                        break;
                    case RouteKind.Explorer: break;
                    default:
                        route.OpenTargets();
                        break;
                }
                routeTimer.Restart();
                poll.Start();
                result.Text = "Задание началось. Карточка остаётся поверх рабочего окна.";
                UpdatePanel();
                if (owner.IsActive) stop.Focus();
                UpdateCard();
            }
            catch (Exception error)
            {
                CloseAttempt();
                result.Text = "Не удалось начать задание. " + error.Message;
                UpdatePanel();
            }
        }

        private void ShowSwitchWindows()
        {
            bool snap = route.Spec.Kind == RouteKind.Snap;
            first = SampleWindow(snap ? route.SnapLabel(false) : "1", snap ? "Это окно должно быть слева." : "Это первое учебное окно.", -105);
            second = SampleWindow(snap ? route.SnapLabel(true) : "2", snap ? "Это окно должно быть справа." : "Это второе учебное окно.", 95);
            first.Show();
            second.Show();
            first.Activate();
            route.FromWindow = Native.Handle(first);
            route.ToWindow = Native.Handle(second);
        }

        private Window SampleWindow(string number, string instruction, int offset)
        {
            var window = new Window { Title = "Учебное окно " + number, Width = 420, Height = 180, Background = Ui.Night,
                FontFamily = new FontFamily("Segoe UI"), Left = owner.Left + owner.ActualWidth / 2 - 210 + offset,
                Top = owner.Top + 120 + offset / 3 };
            var inner = new StackPanel { Margin = new Thickness(18) };
            Ui.Add(inner, Ui.Text(window.Title, 23, Ui.Accent, true), 9);
            Ui.Add(inner, Ui.Text(instruction, 16));
            window.Content = inner;
            return window;
        }

        private void RegisterCardKeys()
        {
            hotkeySource = HwndSource.FromHwnd(ownerHandle);
            if (hotkeySource == null) return;
            hotkeySource.AddHook(CardHotkeys);
            const uint ctrlAlt = 0x4003;
            hotkeys = Native.RegisterHotKey(ownerHandle, 710, ctrlAlt, (uint)'H');
            bool shift = Native.RegisterHotKey(ownerHandle, 711, ctrlAlt, 0x25);
            shift &= Native.RegisterHotKey(ownerHandle, 712, ctrlAlt, 0x27);
            shift &= Native.RegisterHotKey(ownerHandle, 713, ctrlAlt, 0x26);
            shift &= Native.RegisterHotKey(ownerHandle, 714, ctrlAlt, 0x28);
            moveKeys = shift;
        }

        private bool moveKeys;

        private IntPtr CardHotkeys(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x312 || !HasRoute) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (id == 710) FlipHints();
            else if (id >= 711 && id <= 714)
            {
                Native.PlaceCard(Native.Handle(card), false, id == 711 ? -35 : id == 712 ? 35 : 0,
                    id == 713 ? -35 : id == 714 ? 35 : 0);
                savedCardMoved = true;
            }
            else return IntPtr.Zero;
            handled = true;
            return IntPtr.Zero;
        }

        private void CopyPath()
        {
            if (!HasRoute) return;
            try
            {
                Clipboard.SetText(route.Folder);
                result.Text = "Учебный путь скопирован. В Проводнике нажмите Ctrl+L, затем Ctrl+V.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                result.Text = "Буфер обмена занят. Повторите F6 или наберите путь с карточки.";
            }
        }

        private void ReopenTarget()
        {
            if (!HasRoute || !retry.IsEnabled) return;
            try
            {
                if (route.Spec.Kind == RouteKind.Explorer) route.OpenExplorerWarmup();
                else route.OpenTargets();
            }
            catch (Win32Exception error) { result.Text = "Не удалось открыть учебное окно. " + error.Message; }
        }

        private void FlipHints()
        {
            if (!HasRoute) return;
            hintsVisible = !hintsVisible;
            if (hintsVisible && route.Stage == 2) assisted = true;
            UpdateCard();
        }

        internal bool HandleKey(Key key)
        {
            if (key == Key.Enter && list.IsKeyboardFocusWithin && !HasRoute) Start();
            else if (key == Key.Escape && HasRoute) Cancel();
            else if (key == Key.F1 && HasRoute) FlipHints();
            else if (key == Key.F6 && HasRoute) CopyPath();
            else if (key == Key.F7 && HasRoute) ReopenTarget();
            else return false;
            return true;
        }

        private void Poll()
        {
            if (!HasRoute) return;
            if (input.MouseClicks > 0)
            {
                End("Зафиксирован щелчок в рабочем окне. Попытка не засчитана. Нажмите Enter для повтора.");
                return;
            }
            var active = Native.GetForegroundWindow();
            if (route.HasSampleWindows && (!Native.IsWindow(route.FromWindow) || !Native.IsWindow(route.ToWindow)))
            {
                End("Одно из учебных окон закрыто. Начните это задание заново.");
                return;
            }
            try
            {
                while (input.Events.Count > 0) route.Key(input.Events.Dequeue());
                if (active == ownerHandle && !route.HasSampleWindows &&
                    route.Spec.Kind != RouteKind.CloseWindow && route.Spec.Kind != RouteKind.StartMenu)
                {
                    UpdateCard();
                    return;
                }
                route.Validate(active);
                if (active != lastTarget)
                {
                    lastTarget = active;
                    editorStatus = route.EditorStatus(active);
                }
                if (route.Passed)
                {
                    CompleteRoute();
                    return;
                }
                UpdateCard();
            }
            catch (Exception error)
            {
                End("Проверка недоступна. " + error.Message + " Повторите задание.");
            }
        }

        private void CompleteRoute()
        {
            double seconds = routeTimer.Elapsed.TotalSeconds;
            bool practice = assisted || route.ExplorerWarmup;
            var saved = store.Read();
            string key = route.Spec.Kind.ToString();
            int stage = route.Stage;
            string id = route.Spec.PassId(stage);
            var previous = saved.RouteResults.LastOrDefault(item => item.Route == key && item.Stage == stage && !item.Assisted);
            int xp = 0;
            var current = store.Update(p =>
            {
                int before = TrainingProgress.Xp(p);
                if (!practice && !p.CompletedRoutes.Contains(id)) p.CompletedRoutes.Add(id);
                p.RouteResults.Add(new RouteResult { Route = key, Stage = stage, Seconds = seconds, Assisted = practice, Date = DateTime.Now, Context = routeContext });
                if (p.RouteResults.Count > 350) p.RouteResults.RemoveAt(0);
                if (!practice && stage == 2)
                {
                    var territory = Atlas.ForRoute(route.Spec);
                    TrainingProgress.Earn(p, "route/" + key, 50, DateTime.Now);
                    if (territory.Routes.Count(r => p.CompletedRoutes.Contains(r.PassId(2))) >= 3)
                        TrainingProgress.Earn(p, "branch/" + territory.Id, 30, DateTime.Now);
                    if (exam != null) Atlas.PassRouteExam(p, exam, route.Spec, DateTime.Now);
                }
                xp = TrainingProgress.Xp(p) - before;
            });
            string resultText = route.ExplorerWarmup ? "Папка проверена. Для зачёта повторите с Win+E." :
                assisted ? "Результат проверен. Для зачёта пройдите без подсказки." : "Маршрут зачтён.";
            resultText += "  " + Duration(seconds);
            if (previous != null && !practice) resultText += "  ·  предыдущий проход " + Duration(previous.Seconds);
            if (exam != null && !practice)
            {
                var next = Atlas.ExamRoute(exam, current);
                examNext = next == null ? null : next.Kind.ToString();
                resultText += examNext == null ? "  ·  экзамен сдан" : "  ·  следующее задание: " + next.Name;
            }
            if (xp > 0) resultText += "  +" + xp + " XP";
            End(resultText);
        }

        private static string Duration(double seconds)
        {
            return TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
        }

        public void UpdateCard()
        {
            if (!HasRoute) return;
            string current = route.Notice + (editorStatus.Length > 0 ? "\n" + editorStatus : "");
            if (routeStatus.Text != current) routeStatus.Text = current;
            if (route.Spec.Kind == RouteKind.Explorer)
            {
                retry.IsEnabled = route.Step == 0;
                retry.Visibility = retry.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
            string title = route.Spec.Name + "  ·  " + (route.Stage + 1) + "/3";
            string actions = "";
            if (hintsVisible)
            {
                var steps = route.Hints();
                actions = string.Join("\n", steps.Select((item, i) => (i + 1) + ". " + item));
            }
            string address = route.Spec.Kind == RouteKind.Explorer ? route.Folder : "";
            string caption = hotkeys ? "Ctrl+Alt+H — подсказка" : "F1 в тренажёре — подсказка";
            if (moveKeys) caption += "  ·  Ctrl+Alt+стрелки — сдвиг";
            if (route.Spec.Kind == RouteKind.Explorer) caption += "\nF6 в тренажёре — скопировать путь";
            caption += "\nAlt+Tab к тренажёру, Esc — остановить";
            card.Set(title, route.Goal(), actions, address, current, caption);
        }

        private void Cancel() { End("Задание остановлено. Выберите маршрут или повторите его."); }

        private void End(string message)
        {
            int selected = list.SelectedIndex;
            bool passed = route != null && route.Passed && !assisted && !route.ExplorerWarmup;
            CloseAttempt();
            if (passed) Populate(selected);
            result.Text = message;
            routeStatus.Text = "";
            if (!owner.IsActive) ShowOutcome(passed ? "Зачтено" : "Попытка завершена", message);
            UpdatePanel();
            owner.WarnStorage();
            if (owner.IsActive) RefreshOnOpen();
        }

        private DispatcherTimer outcomeTimer;
        private HintCard outcome;

        private void ShowOutcome(string title, string message)
        {
            var saved = store.Read();
            outcome = new HintCard(saved);
            outcome.Set(title, message, "", "", "Alt+Tab — вернуться к списку заданий.", "");
            outcome.Show();
            Native.PlaceCard(Native.Handle(outcome), !outcome.HasSavedLocation);
            outcomeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            outcomeTimer.Tick += delegate { ClearOutcome(); };
            outcomeTimer.Start();
        }

        private void ClearOutcome()
        {
            if (outcomeTimer != null) outcomeTimer.Stop();
            if (outcome != null) outcome.Close();
            outcome = null;
        }

        private void CloseAttempt()
        {
            poll.Stop();
            routeTimer.Stop();
            ClearOutcome();
            if (input != null) input.Dispose();
            input = null;
            if (hotkeySource != null)
            {
                for (int id = 710; id <= 714; id++) Native.UnregisterHotKey(ownerHandle, id);
                hotkeySource.RemoveHook(CardHotkeys);
                hotkeySource = null;
            }
            hotkeys = moveKeys = false;
            if (card != null)
            {
                if (savedCardMoved || card.Moved)
                {
                    double x = card.Left, y = card.Top;
                    store.Update(p => { p.CardX = x; p.CardY = y; });
                }
                card.Close();
                card = null;
            }
            if (first != null && Native.IsWindow(Native.Handle(first))) first.Close();
            if (second != null && Native.IsWindow(Native.Handle(second))) second.Close();
            first = second = null;
            route = null;
            if (lockOwned) { activeRoute.ReleaseMutex(); lockOwned = false; }
        }

        public void Close() { CloseAttempt(); }
    }

    internal sealed class HintCard : Window
    {
        public readonly bool HasSavedLocation;
        public bool Moved { get; private set; }
        private readonly TextBlock title, goal, hints, address, status, keys;
        private Native.PhysicalPoint dragStart, dragLocation;

        public HintCard(StudioProfile profile)
        {
            Title = "Карточка задания · Windows без мыши";
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = Math.Min(425, Math.Max(320, SystemParameters.WorkArea.Width - 15));
            Background = Ui.Night;
            Foreground = Ui.Ink;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            HasSavedLocation = !double.IsNaN(profile.CardX) && !double.IsNaN(profile.CardY);
            if (HasSavedLocation) { Left = profile.CardX; Top = profile.CardY; }
            else { Left = 25; Top = 25; }
            SourceInitialized += delegate
            {
                IntPtr hwnd = Native.Handle(this);
                Native.NoActivate(hwnd);
                HwndSource.FromHwnd(hwnd).AddHook(NoActivation);
            };
            SizeChanged += delegate
            {
                if (IsLoaded) Native.PlaceCard(Native.Handle(this), false);
            };
            var content = new StackPanel();
            title = Ui.Text("", 17, Ui.Accent, true);
            title.Cursor = Cursors.SizeAll;
            title.Background = Brushes.Transparent;
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                dragStart = Native.Cursor();
                dragLocation = Native.Corner(Native.Handle(this));
                title.CaptureMouse();
                e.Handled = true;
            };
            title.MouseMove += delegate
            {
                if (!title.IsMouseCaptured || Mouse.LeftButton != MouseButtonState.Pressed) return;
                var now = Native.Cursor();
                Native.MoveCard(Native.Handle(this), dragLocation.X + now.X - dragStart.X, dragLocation.Y + now.Y - dragStart.Y);
                Moved = true;
            };
            title.MouseLeftButtonUp += delegate { title.ReleaseMouseCapture(); Native.PlaceCard(Native.Handle(this), false); };
            Ui.Add(content, title, 6);
            goal = Ui.Text("", 15, Ui.Ink, true);
            Ui.Add(content, goal, 7);
            address = Ui.Text("", 12, Ui.Muted);
            Ui.Add(content, address, 6);
            hints = Ui.Text("", 13, Ui.Ink);
            Ui.Add(content, hints, 8);
            status = Ui.Text("", 13, Ui.Brush("#C4E9C7"));
            Ui.Add(content, status, 5);
            keys = Ui.Text("", 12, Ui.Muted);
            Ui.Add(content, keys);
            Content = Ui.Panel(content, new Thickness(13), Ui.Night);
        }

        private static IntPtr NoActivation(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message != 0x21) return IntPtr.Zero;
            handled = true;
            return new IntPtr(3);
        }

        public void Set(string heading, string task, string actions, string path, string message, string shortcuts)
        {
            Show(title, heading);
            Show(goal, task);
            Show(hints, actions);
            Show(address, path);
            Show(status, message);
            Show(keys, shortcuts);
        }

        private static void Show(TextBlock text, string value)
        {
            if (text.Text != value) text.Text = value;
            var visible = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            if (text.Visibility != visible) text.Visibility = visible;
        }
    }
}
