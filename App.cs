using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool verify = Array.IndexOf(args, "--qa") >= 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var store = new ProfileStore(verify);
            bool errorPending = false;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
                if (errorPending) return;
                errorPending = true;
                string message = e.Exception.Message;
                // Layout and window creation can disable dispatcher processing. Open the
                // modal dialog after that operation unwinds, and report only the first error.
                app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                {
                    try { MessageBox.Show("Не удалось продолжить.\n" + message, "Код вслепую", MessageBoxButton.OK, MessageBoxImage.Error); }
                    finally { app.Shutdown(1); }
                }));
            };
            int render = Array.IndexOf(args, "--render-checks");
            if (verify && render >= 0 && render + 1 < args.Length)
            {
                RenderChecks.Run(app, store, args[render + 1]);
                return;
            }
            bool workday = Array.IndexOf(args, "--windows") >= 0;
            bool explicitLive = Array.IndexOf(args, "--live") >= 0;
            string engine = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "TouchTyping.Engine.exe");
            bool legacy = workday || Array.IndexOf(args, "--legacy-typing") >= 0 ||
                (verify && Array.IndexOf(args, "--qa-desktop-input") >= 0);
            if (!legacy && (explicitLive || System.IO.File.Exists(engine)))
            {
                try
                {
                    app.Run(new LiveStudio(Array.IndexOf(args, "--typing") >= 0 ? "Печать" : "Live", verify));
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "Код вслепую", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.ExitCode = 1;
                }
                return;
            }
            var window = new StudioWindow(store, workday, Array.IndexOf(args, "--resume") < 0,
                verify && Array.IndexOf(args, "--qa-desktop-input") >= 0);
            app.Run(window);
        }
    }

    internal static class Ui
    {
        private static readonly Dictionary<string, SolidColorBrush> BrushesByHex = new Dictionary<string, SolidColorBrush>();
        public static readonly Brush Ink = Brush("#F1F1F4");
        public static readonly Brush Muted = Brush("#ADB3C0");
        public static readonly Brush Border = Brush("#383E4A");
        public static readonly Brush Card = Brush("#282D38");
        public static readonly Brush Night = Brush("#20232A");
        public static readonly Brush Accent = Brush("#BFD6DD");
        public static readonly Typeface Mono = new Typeface("Cascadia Mono, Consolas");

        public static SolidColorBrush Brush(string color)
        {
            SolidColorBrush b;
            if (BrushesByHex.TryGetValue(color, out b)) return b;
            b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            b.Freeze();
            BrushesByHex[color] = b;
            return b;
        }

        public static TextBlock Text(string text, double size = 15, Brush color = null, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, LineHeight = 1.32 * size,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
        }

        public static Button Button(string text, Action click, bool primary = false)
        {
            var b = new Button
            {
                Content = text, FontSize = 14, FontWeight = FontWeights.SemiBold, MinHeight = 38,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 5),
                Foreground = primary ? Night : Ink, Background = primary ? Accent : Card,
                BorderBrush = primary ? Accent : Border, BorderThickness = new Thickness(1),
                Template = ButtonTemplate(), Cursor = Cursors.Hand
            };
            b.Click += delegate { click(); };
            return b;
        }

        public static Button QuietButton(string text, Action click)
        {
            var b = Button(text, click);
            b.Background = Brushes.Transparent;
            b.BorderBrush = Brushes.Transparent;
            b.MinHeight = 29;
            b.Padding = new Thickness(8, 3, 8, 3);
            b.Margin = new Thickness(0, 0, 6, 0);
            return b;
        }

        private static ControlTemplate ButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "Frame");
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var text = new FrameworkElementFactory(typeof(ContentPresenter));
            text.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            border.AppendChild(text);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, Accent, "Frame"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, Brush("#FFFFFF"), "Frame"));
            focus.Setters.Add(new Setter(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(2), "Frame"));
            template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .46, "Frame"));
            template.Triggers.Add(disabled);
            return template;
        }

        public static Border Panel(UIElement content, Thickness padding, Brush fill = null)
        {
            return new Border
            {
                Child = content, Background = fill ?? Card, BorderBrush = Border, BorderThickness = new Thickness(1),
                Padding = padding, CornerRadius = new CornerRadius(12), SnapsToDevicePixels = true
            };
        }

        public static void Add(Panel p, UIElement control, double after = 0)
        {
            if (after != 0)
            {
                var f = (FrameworkElement)control;
                f.Margin = new Thickness(f.Margin.Left, f.Margin.Top, f.Margin.Right, f.Margin.Bottom + after);
            }
            p.Children.Add(control);
        }

        public static TextBox Editor(string text, double size = 15)
        {
            var input = new TextBox
            {
                Text = text ?? "", Foreground = Ink, Background = Night, BorderBrush = Border, FontSize = size,
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, CaretBrush = Ink, Padding = new Thickness(10),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            return input;
        }

        public static ScrollViewer Scroll(UIElement child)
        {
            return new ScrollViewer { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        }

        public static void Name(DependencyObject control, string name) { AutomationProperties.SetName(control, name); }
    }

    internal sealed class StudioWindow : Window
    {
        private readonly ContentControl body = new ContentControl();
        private readonly TextBlock footer;
        private readonly Button homeButton, setsButton;
        private readonly ProfileStore store;
        private readonly TypingPage typing;
        private readonly WindowsPage windows;
        private DesktopTrainingWindow liveDesktop;
        private readonly bool allowDesktopAutomation;
        private bool closing;
        private string pickedRegion = "python";
        private readonly Random drillRandom = new Random();
        private int exerciseSalt;
        private int currentMode;
        private bool subPage;

        public StudioWindow(ProfileStore store, bool windowsFirst, bool preferPython = true, bool allowDesktopAutomation = false)
        {
            this.store = store;
            this.allowDesktopAutomation = allowDesktopAutomation;
            Title = windowsFirst ? "Windows без мыши" : "Код вслепую";
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#191E28"),
                (Color)ColorConverter.ConvertFromString("#272C37"), 14);
            Foreground = Ui.Ink;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Width = Math.Min(1190, SystemParameters.WorkArea.Width - 30);
            Height = Math.Min(900, SystemParameters.WorkArea.Height - 24);
            MinWidth = Math.Min(680, Width);
            MinHeight = Math.Min(510, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var shell = new DockPanel { Background = Ui.Brush("#20232A") };
            var top = new DockPanel { Background = Ui.Brush("#22252B"), LastChildFill = true };
            var title = Ui.QuietButton("КОД ВСЛЕПУЮ", ShowHome);
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 2,
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (string tint in new[] { "#DDACBA", "#D3B483", "#A9C8B0", "#91BBC7" })
                mark.Children.Add(new Border { Background = Ui.Brush(tint), CornerRadius = new CornerRadius(2), Margin = new Thickness(1) });
            brand.Children.Add(mark);
            brand.Children.Add(Ui.Text("Код вслепую", 17, Ui.Ink, true));
            title.Content = brand;
            Ui.Name(title, "Код вслепую — к выбору тренировки");
            title.Margin = new Thickness(15, 7, 12, 7);
            title.ToolTip = "Выбрать тренировку · F2";
            DockPanel.SetDock(title, Dock.Left);
            top.Children.Add(title);
            var nav = new WrapPanel { Margin = new Thickness(6, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Right };
            homeButton = Ui.QuietButton("Режимы · F2", ShowHome);
            Ui.Add(nav, homeButton);
            setsButton = Ui.QuietButton("Свои наборы", delegate { ShowPracticeSets(); });
            Ui.Add(nav, setsButton);
            top.Children.Add(nav);
            var header = new Border { Child = top, BorderBrush = Ui.Brush("#383D46"), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(header, Dock.Top);
            shell.Children.Add(header);
            footer = Ui.Text("", 12, Ui.Brush("#FFCA82"));
            footer.Margin = new Thickness(22, 2, 22, 5);
            footer.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(footer, Dock.Bottom);
            shell.Children.Add(footer);
            shell.Children.Add(body);
            Content = shell;

            typing = new TypingPage(this, store, preferPython);
            windows = new WindowsPage(this, store);
            if (windowsFirst) ShowDesktopTraining();
            else if (preferPython) ShowHome();
            else ShowMode(1);
            Loaded += delegate { WarnStorage(); };
            Deactivated += delegate { if (body.Content == typing) typing.PauseForFocusLoss(); };
            Activated += delegate
            {
                if (body.Content == windows) windows.RefreshOnOpen();
            };
            Closing += delegate
            {
                closing = true;
                if (liveDesktop != null) liveDesktop.Close();
                typing.Close();
                windows.Close();
                WarnStorage();
            };
            PreviewKeyDown += GlobalKeys;
        }

        public void WarnStorage()
        {
            footer.Text = store.Error ?? "";
            footer.Visibility = store.Error == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ShowMode(int mode)
        {
            if (mode == 0) { ShowHome(); return; }
            currentMode = mode;
            subPage = false;
            if (mode != 1) typing.PauseForFocusLoss();
            bool inTask = windows.HasRoute;
            setsButton.Background = Brushes.Transparent;
            homeButton.Content = "Режимы · F2";
            if (mode == 1)
            {
                body.Content = typing;
                Title = "Код вслепую";
                typing.FocusPractice();
            }
            else if (mode == 2)
            {
                body.Content = windows;
                Title = "Windows без мыши";
                windows.RefreshOnOpen();
            }
            else
            {
                var plan = new ChallengePage(this, store);
                body.Content = plan;
                Title = "48 часов · Код вслепую";
                Dispatcher.BeginInvoke(new Action(delegate { plan.InitialFocus.Focus(); }), DispatcherPriority.Input);
            }
            if (inTask) windows.UpdateCard();
            WarnStorage();
        }

        private void OpenPython()
        {
            OpenTyping(ContinuousKind.Python);
        }

        public void ShowHome()
        {
            currentMode = 0;
            var page = new TrainingHome(this, store.Read());
            OpenPanel(page, page.InitialFocus);
            subPage = false;
            Title = "Код вслепую";
        }

        public void ShowPracticeSets(PracticeSetKind kind = PracticeSetKind.Text)
        {
            currentMode = 0;
            var page = new PracticeSetsPage(this, store, kind);
            OpenPanel(page, page.InitialFocus);
            Title = "Свои наборы · Код вслепую";
        }

        public void ShowDesktopTraining()
        {
            ShowDesktopTraining(null, 0, TimeSpan.Zero, false);
        }

        private void ShowDesktopTraining(int? completed, int rejected, TimeSpan elapsed, bool restoreFailed,
            DesktopTrack track = DesktopTrack.Workday)
        {
            currentMode = 0;
            DesktopTrainingPage page = null;
            page = new DesktopTrainingPage(this, store.Read(), selected => StartDesktopTraining(page, selected),
                completed, rejected, elapsed, restoreFailed, track, store.Root);
            OpenPanel(page, page.InitialFocus);
            Title = "Windows без мыши · Код вслепую";
        }

        private void StartDesktopTraining(DesktopTrainingPage page, DesktopTrack track)
        {
            if (liveDesktop != null) return;
            DesktopTrainingWindow created = null;
            bool launched = false;
            try
            {
                created = new DesktopTrainingWindow(store.Root, new WindowInteropHelper(this).Handle, track, allowDesktopAutomation, page.StartLevel);
                liveDesktop = created;
                created.Closed += delegate
                {
                    liveDesktop = null;
                    if (closing || !launched) return;
                    if (created.Completed >= 12 && created.Elapsed.TotalSeconds >= 1)
                    {
                        double speed = created.Completed / created.Elapsed.TotalMinutes;
                        double accuracy = 100.0 * created.Completed / (created.Completed + created.Rejected);
                        store.Update(p => PracticeRecords.Record(p, DesktopTrainingPage.RecordId(track), speed, accuracy));
                        WarnStorage();
                    }
                    ShowDesktopTraining(created.Completed, created.Rejected, created.Elapsed, created.RestoreFailed, track);
                    WindowState = WindowState.Normal;
                    Activate();
                };
                created.Show();
                if (created.StartError != null)
                {
                    created.Close();
                    page.ShowError(created.StartError);
                    return;
                }
                launched = true;
                WindowState = WindowState.Minimized;
                created.ActivateMission();
            }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException) && !(error is UnauthorizedAccessException) &&
                    !(error is InvalidOperationException) && !(error is System.ComponentModel.Win32Exception)) throw;
                if (created != null) created.Close();
                liveDesktop = null;
                page.ShowError("Не удалось начать тренировку. " + error.Message);
            }
        }

        public void OpenPracticeSet(PracticeSet set)
        {
            if (set.Kind == PracticeSetKind.Shortcuts)
            {
                WorkbenchCustomTask[] custom;
                if (PracticeSetInput.ParseShortcuts(set.Content, out custom) == null)
                    OpenWorkbench(WorkbenchArea.Shortcuts, custom, set);
                return;
            }
            OpenTyping(set.Kind == PracticeSetKind.Symbols ? ContinuousKind.Symbols : ContinuousKind.Custom, set: set);
        }

        public void OpenWorkbench(WorkbenchArea area, IEnumerable<WorkbenchCustomTask> custom = null, PracticeSet set = null)
        {
            currentMode = 0;
            var page = new WorkbenchPage(area, custom, ShowHome, delegate { ShowPracticeSets(PracticeSetKind.Shortcuts); },
                customOnly: set != null, saveResult: (mode, result) => SaveWorkbench(mode, result, set));
            OpenPanel(page, page.InitialFocus);
            subPage = false;
            Title = "Работа без мыши · Код вслепую";
        }

        private WorkbenchRecords SaveWorkbench(WorkbenchArea mode, WorkbenchResults result, PracticeSet set)
        {
            string id = set == null ? "workbench/" + mode : "set/" + set.Id;
            var saved = result.Commands < 12 ? store.Read() : store.Update(p =>
                PracticeRecords.Record(p, id, result.CommandsPerMinute, result.Accuracy));
            WarnStorage();
            var best = saved.PracticeRecords.Find(record => record.Id == id);
            return best == null ? new WorkbenchRecords(0, 0) : new WorkbenchRecords(best.Speed, best.Accuracy);
        }

        private void ShowMenu()
        {
            var panel = new StackPanel { Margin = new Thickness(32, 22, 32, 18), MaxWidth = 650 };
            Ui.Add(panel, Ui.Text("Меню", 24, Ui.Ink, true), 15);
            var history = Ui.Button("Результаты · F7", delegate { ShowMode(1); typing.HandleShortcut(Key.F7); });
            Ui.Add(panel, history);
            Ui.Add(panel, Ui.Button("Свой код · F3", OpenImport));
            Ui.Add(panel, Ui.Button("Настройки обучения", ShowTrainingSettings));
            Ui.Add(panel, Ui.Button("Параметры упражнения · F10", delegate { ShowMode(1); typing.HandleShortcut(Key.F10); }));
            Ui.Add(panel, Ui.Button("Проверка клавиатуры и Python · F8", ShowSetup));
            Ui.Add(panel, Ui.Button("Назад · Esc", ClosePanel));
            OpenPanel(panel, history);
        }

        public void OpenLesson(string id)
        {
            if (body.Content != typing) ShowMode(1);
            typing.Choose(Lessons.Find(id));
        }

        public void OpenImport() { ShowMode(1); typing.HandleShortcut(Key.F3); }

        public void ShowPythonDrills(PythonDrillLevel selected = null)
        {
            var page = new PythonDrillPage(this, store.Read(), selected);
            OpenPanel(page, page.InitialFocus);
        }

        public void OpenPythonDrill(PythonDrillLevel level)
        {
            int codeLevel = Array.IndexOf(PythonDrills.CodeLevels, level);
            if (codeLevel >= 0)
            {
                OpenAdaptivePython(codeLevel);
                return;
            }
            string script = level.Create(drillRandom);
            pickedRegion = "python";
            ShowMode(1);
            typing.ChooseExercise(new Exercise
            {
                Kind = ExerciseKind.PythonWords,
                Title = "Слова Python  ›  " + level.Title,
                Text = script,
                Layout = Layout.English,
                Region = "python",
                Task = ExerciseBank.Task(level.Id, script),
                DrillLevel = level
            });
        }

        private void OpenAdaptivePython(int level)
        {
            OpenTyping(ContinuousKind.Python, level);
        }

        public void OpenTyping(ContinuousKind kind, int? level = null, PracticeSet set = null)
        {
            if (!level.HasValue && set == null && typing.IsContinuous(kind))
            {
                ShowMode(1);
                return;
            }
            var profile = store.Read();
            int choice = level ?? (kind == ContinuousKind.Python ? -1 : 0);
            var fragments = set == null ? null : PracticeSetInput.Fragments(set);
            var course = new ContinuousTyping(drillRandom, kind, choice < 0 ? profile.AdaptivePythonLevel : choice,
                profile.Training.Goal, fragments, choice < 0);
            pickedRegion = "python";
            ShowMode(1);
            typing.ChooseExercise(new Exercise
            {
                Kind = kind == ContinuousKind.Python ? ExerciseKind.PythonCode : ExerciseKind.Free,
                Title = course.Name,
                Text = course.Attempt.Target,
                Layout = course.Layout,
                Region = "python",
                Adaptive = course,
                PracticeSet = set
            });
        }

        internal void ShowTypingResults(ContinuousTypingSnapshot result, ContinuousTyping course, PracticeSet set, Action resume, Action restart)
        {
            string id = set == null ? "typing/" + course.Kind : "set/" + set.Id;
            var saved = !PracticeRecords.CanRecordTyping(result) ? store.Read() : store.Update(p =>
                PracticeRecords.Record(p, id, result.Cpm, result.Accuracy));
            WarnStorage();
            var best = saved.PracticeRecords.Find(record => record.Id == id);
            var page = new PracticeResultsPage(saved, set == null ? course.Name : set.Name, result, best, resume, restart, ShowHome);
            OpenPanel(page, page.InitialFocus);
        }

        public void ShowAtlas(string region = null)
        {
            if (region != null) pickedRegion = region;
            var page = new AtlasPage(this, store.Read(), pickedRegion);
            OpenPanel(page, page.InitialFocus);
        }

        public void ShowAnalytics()
        {
            var page = new AnalyticsPage(this, store.Read(), Atlas.Find(pickedRegion).Language);
            OpenPanel(page, page.InitialFocus);
        }

        public void ShowModes(string region = null, string message = null)
        {
            if (region != null) pickedRegion = region;
            var page = new ModesPage(this, store.Read(), pickedRegion, message);
            OpenPanel(page, page.InitialFocus);
        }

        public void ShowTrainingSettings()
        {
            var page = new TrainingSettingsPage(this, store);
            OpenPanel(page, page.InitialFocus);
        }

        public void OpenWaypoint(Waypoint node)
        {
            pickedRegion = node.Region.Id;
            if (node.Kind == WaypointKind.Exam)
            {
                if (node.Region.Trial.Length == 0) OpenExercise(ExerciseKind.Boss, pickedRegion);
                else
                {
                    var route = Atlas.ExamRoute(node.Region, store.Read());
                    if (route == null) OpenShortcuts(pickedRegion);
                    else StartWindowsRoute(route.Kind.ToString(), node.Region);
                }
            }
            else if (node.Lesson != null)
            {
                ShowMode(1);
                typing.Choose(node.Lesson, true);
            }
            else if (node.Route != null) StartWindowsRoute(node.Route.Kind.ToString());
            else OpenExercise(node.Region.Branch, pickedRegion, true);
        }

        public void OpenExercise(ExerciseKind mode, string region = null, bool branch = false)
        {
            if (region != null) pickedRegion = region;
            if (mode == ExerciseKind.Campaign) { ShowAtlas(); return; }
            if (mode == ExerciseKind.Shortcuts) { OpenShortcuts(pickedRegion); return; }
            if (mode == ExerciseKind.Free) { OpenImport(); return; }
            var p = store.Read();
            var territory = Atlas.Find(pickedRegion);
            if (mode == ExerciseKind.Boss)
            {
                var boss = Atlas.Nodes.First(x => x.Region == territory && x.Kind == WaypointKind.Exam);
                if (territory.Trial.Length > 0) { OpenWaypoint(boss); return; }
            }
            if (territory.Trial.Length > 0 && mode != ExerciseKind.Daily)
                territory = Atlas.Find(mode == ExerciseKind.Python ? "python" : "qwerty");
            var task = ExerciseBank.Create(mode, p, territory, DateTime.Now, exerciseSalt++ % 300);
            if (task == null)
            {
                string reason = mode == ExerciseKind.Daily ? (p.RecallDates.Count == 0 ?
                    "После зачёта первого уровня здесь появится первый повтор." :
                    "Сегодня повторов нет. Следующий: " + p.RecallDates.Min(x => x.Due).ToString("dd.MM") + ".") :
                    "Для этой раскладки пока мало данных. Пройдите обычный раунд, и здесь появятся трудные клавиши.";
                ShowModes(territory.Id, reason);
                return;
            }
            if (branch) task.Branch = pickedRegion;
            ShowMode(1);
            typing.ChooseExercise(task);
        }

        public void OpenTransfer(bool ru)
        {
            ShowMode(1);
            typing.ChooseExercise(ExerciseBank.Transfer(store.Read(), ru));
        }

        internal void OpenChallengeTask(ChallengeTask task)
        {
            var p = store.Read();
            string context = ChallengePlanner.Context(p, task);
            if (task.Route != null) { StartWindowsRoute(task.Route, context: context); return; }
            var selected = ChallengePlanner.ExerciseFor(p, task, DateTime.Now);
            selected.Context = context;
            ShowMode(1);
            typing.ChooseExercise(selected);
        }

        internal void OpenShortcuts(string region)
        {
            var target = Atlas.Find(region).Id == "vscode" ? "vscode" : "windows";
            var page = new ShortcutIndexPage(this, store.Read(), Atlas.Find(target));
            OpenPanel(page, page.InitialFocus);
        }

        public void OpenPanel(UIElement element, UIElement initialFocus)
        {
            typing.PauseForFocusLoss();
            subPage = true;
            body.Content = element;
            homeButton.Content = element is WorkbenchPage ? "Режимы" : "Режимы · F2";
            setsButton.Background = element is PracticeSetsPage ? Ui.Brush("#343841") : Brushes.Transparent;
            Dispatcher.BeginInvoke(new Action(delegate { if (body.Content == element) initialFocus.Focus(); }), DispatcherPriority.Input);
        }

        public void ClosePanel() { ShowMode(currentMode); }

        internal void StartWindowsRoute(string id, Territory exam = null, string context = null)
        {
            ShowMode(2);
            windows.ChooseRoute(id, exam, context);
        }

        private void GlobalKeys(object sender, KeyEventArgs e)
        {
            if (body.Content is WorkbenchPage) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && !(body.Content is TrainingHome))
            {
                ShowHome();
                e.Handled = true;
                return;
            }
            if (subPage && key == Key.Escape)
            {
                ClosePanel();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D4)
            {
                if (key == Key.D1) OpenPython();
                else ShowDesktopTraining();
                e.Handled = true;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && key == Key.F8)
            {
                ShowSetup();
                e.Handled = true;
            }
            if (!e.Handled && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (key == Key.F3 && !(body.Content is PracticeSetsPage)) { ShowPracticeSets(); e.Handled = true; }
                else if (key == Key.F7 && body.Content != windows) { ShowAnalytics(); e.Handled = true; }
                else if (key == Key.F2 && (body.Content == windows || body.Content is ModesPage || body.Content is AnalyticsPage ||
                    body.Content is ChallengePage || body.Content is PythonDrillPage || body.Content is ShortcutIndexPage))
                { ShowAtlas(); e.Handled = true; }
                else if (key == Key.Escape && windows.HasRoute) e.Handled = windows.HandleKey(key);
                else if (body.Content == windows) e.Handled = windows.HandleKey(key);
                else if (body.Content == typing) e.Handled = typing.HandleShortcut(key);
            }
        }

        private void ShowSetup()
        {
            var stack = new StackPanel { Margin = new Thickness(24), MaxWidth = 710 };
            var details = RuntimeTools.Current;
            var snapshot = store.Read();
            bool f2 = snapshot.F2Seen;
            var dpi = VisualTreeHelper.GetDpi(this);
            Ui.Add(stack, Ui.Text("Проверьте клавиатуру", 24, Ui.Ink, true), 12);
            Ui.Add(stack, Ui.Text("Раскладки Windows   " + Native.LayoutStatus(), 15), 7);
            Ui.Add(stack, Ui.Text("Масштаб окна   " + Math.Round(dpi.DpiScaleX * 100) + "%", 15), 7);
            Ui.Add(stack, Ui.Text("VS Code   " + (details.Code != null ? "найден" : "не найден"), 15), 7);
            Ui.Add(stack, Ui.Text("Python   " + (details.Python ?? "не найден"), 14), 7);
            Ui.Add(stack, Ui.Text("Задание Python запускается в отдельном окне VS Code.", 14, Ui.Muted), 10);
            var prompt = Ui.Text(f2 ? "F2 получена." : "Нажмите F2. Если меняется яркость или громкость, попробуйте Fn+F2.", 15, Ui.Accent);
            Ui.Add(stack, prompt, 12);
            var done = Ui.Button("К тренировке · Enter", delegate
            {
                store.Update(p => p.F2Seen = f2);
                ClosePanel();
            }, true);
            Ui.Add(stack, done);
            stack.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.F2)
                {
                    f2 = true;
                    prompt.Text = "F2 получена. Это клавиша переименования в Проводнике.";
                    e.Handled = true;
                }
            };
            OpenPanel(Ui.Scroll(stack), done);
        }
    }
}
