using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio : Window
    {
        private const string HomePage = "Главная";
        private const string LivePage = "Live";
        private const string TypingPage = "Печать";
        private const string EditorPage = "Редактор";
        private const string ProgressPage = "Прогресс";

        private static readonly Brush Ink = Ui.Brush("#F4F4F6");
        private static readonly Brush Dim = Ui.Brush("#B6BCC6");
        private static readonly Brush Accent = Ui.Brush("#9CC5E6");
        private static readonly Brush Card = Ui.Brush("#292D36");
        private static readonly Brush Edge = Ui.Brush("#373D48");
        private static readonly Brush Night = Ui.Brush("#232833");
        private static readonly Brush Error = Ui.Brush("#F18991");

        private readonly PracticeEngineClient engine;
        private readonly bool verifyMode;
        private readonly ContentControl center = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        private readonly TextBlock notice = Label("");
        private readonly StackPanel top = Stack(true);
        private readonly DispatcherTimer timer;
        private readonly HashSet<string> pressed = new HashSet<string>();
        private Data library = Data.Empty, state = Data.Empty;
        private HwndSource nativeWindow;
        private string page = LivePage;
        private bool active, paused;
        private bool closeAccepted;
        private string selectedSkill = "terminal-paste", selectedTyping = "Project";
        private string selectedGroup = "";
        private int selectedLiveKind, selectedLiveLevel, selectedMeasure, selectedTypingLevel, selectedTime;

        private FocusTape tape;
        private ProgressBar lessonProgress;
        private LiveKeyboard keyboard;
        private TextBlock timerLabel, sceneLabel, feedback, keyHints, titleLabel;
        private Button keyboardControl, handsControl, mistakeControl, pauseControl, stopControl;
        private string lastTypedKey = "";
        private ComboBox groupSelect, taskSelect, kindSelect, levelSelect, measureSelect;
        private ComboBox typingSelect, typingLevel, typingTime;
        private Button beginControl;
        private Data[] groupSkills = new Data[0];

        public LiveStudio(string initial, bool verify, bool captureHome = false)
        {
            verifyMode = verify;
            engine = new PracticeEngineClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "TouchTyping.Engine.exe"), verify);
            Title = "Код вслепую";
            Width = captureHome ? 1006 : 1280;
            Height = captureHome ? 863 : 750;
            MinWidth = 748;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new LinearGradientBrush(Color.FromRgb(28,34,45), Color.FromRgb(44,49,61), 0);
            WindowState = captureHome ? WindowState.Normal : WindowState.Maximized;
            FontFamily = LiveUiFont.Sans;
            customLiveChrome = !verify || captureHome;
            if (customLiveChrome) ConfigureLiveChrome();
            Foreground = Ink;
            UseLayoutRounding = true;
            Content = BuildShell();
            SourceInitialized += delegate
            {
                nativeWindow = PresentationSource.FromVisual(this) as HwndSource;
                if (nativeWindow != null) nativeWindow.AddHook(NativeInput);
            };
            Activated += delegate { if (!active && (page == LivePage || page == HomePage)) RefreshLayoutIfChanged(); };
            Deactivated += delegate
            {
                if (labActive) { labLimitArmedButton = null; PauseLab(); return; }
                if (active && !paused && (page == TypingPage || !LayoutStep)) PauseSession();
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (closeAccepted) return;
                if (active)
                {
                    bool wasLab = labActive;
                    if (wasLab) PauseLab(); else PauseSession();
                    if (!ConfirmActiveClose()) { e.Cancel = true; return; }
                    if (wasLab)
                    {
                        labSaveClosePending = true;
                        if (!TrySaveLab()) { e.Cancel = true; return; }
                    }
                    var saved = wasLab ? null : page == TypingPage ?
                        Request("typing-stop", "layout", CurrentLayout()) : Request("shortcut-result");
                    if (!wasLab && (!saved.Exists || !string.IsNullOrWhiteSpace(engine.Notice)))
                    {
                        e.Cancel = true;
                        string problem = notice.Text.Length > 0 ? notice.Text : "Не удалось сохранить прогресс. Занятие осталось на паузе.";
                        SetNotice(problem);
                        MessageBox.Show(this, problem, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    StopLabCapture();
                    timer.Stop();
                    pressed.Clear();
                    labActive = active = paused = false;
                    closeAccepted = true;
                }
                else if (page == LivePage && beginControl != null)
                    e.Cancel = MessageBox.Show(this, "Закрыть тренажёр?", Title, MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
            };
            Closed += delegate
            {
                StopLabCapture();
                timer.Stop();
                if (nativeWindow != null && !nativeWindow.IsDisposed) nativeWindow.RemoveHook(NativeInput);
                engine.Dispose();
            };
            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += OnKeyUp;
            PreviewTextInput += OnText;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += delegate { Tick(); };
            var initialLibrary = Request("configure", "platform", "windows", "layout", CurrentLayout());
            if (!initialLibrary.Exists)
            {
                engine.Dispose();
                throw new InvalidOperationException(notice.Text);
            }
            library = initialLibrary;
            ShowPage(string.IsNullOrEmpty(initial) ? LivePage : initial);
        }

        private bool ConfirmActiveClose()
        {
            var dialog = new Window
            {
                Owner = this, Title = "Завершить занятие?", Width = 430, Height = 174,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, Background = Night, Foreground = Ink
            };
            AutomationProperties.SetAutomationId(dialog, "LabExitDialog");
            var panel = Stack();
            panel.Margin = new Thickness(22, 17, 22, 14);
            panel.Children.Add(Label("Сохранить прогресс и закрыть тренажёр?", 16, Ink, true));
            var detail = Label(labActive && (state.B("limitDecisionPending") || state.B("finishingMission")) ?
                "Миссию можно будет продолжить с сохранённого места." :
                "Текущее занятие остановится; результаты сохранятся.", 12, Dim);
            detail.Margin = new Thickness(0, 7, 0, 16);
            panel.Children.Add(detail);
            var actions = Stack(true);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            var cancel = ActionButton("Отмена", delegate { dialog.DialogResult = false; });
            var save = ActionButton("Сохранить и закрыть", delegate { dialog.DialogResult = true; }, true);
            cancel.IsCancel = true;
            AutomationProperties.SetAutomationId(cancel, "LabExitCancel");
            AutomationProperties.SetAutomationId(save, "LabExitSave");
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            panel.Children.Add(actions);
            dialog.Content = panel;
            dialog.Loaded += delegate { cancel.Focus(); };
            return dialog.ShowDialog() == true;
        }

        private UIElement BuildShell()
        {
            var root = new DockPanel();
            var header = BuildReferenceHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            notice.Foreground = Error;
            notice.Margin = new Thickness(24, 4, 24, 3);
            notice.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
            DockPanel.SetDock(notice, Dock.Bottom);
            root.Children.Add(notice);
            root.Children.Add(center);
            return root;
        }

        private void UpdateHeader()
        {
            UpdateReferenceHeader();

        }

        private void SetNotice(string message)
        {
            notice.Text = message;
            notice.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Data Request(string command, params object[] fields)
        {
            try
            {
                var reply = engine.Call(command, fields);
                SetNotice(engine.Notice);
                return reply;
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is InvalidOperationException || error is ArgumentException)) throw;
                SetNotice(error.Message);
                return Data.Empty;
            }
        }

        private string CurrentLayout()
        {
            return (KeyboardLayout(0).ToInt64() & 0x3ff) == 0x19 ? "RU" : "EN";
        }

        private void RefreshLibrary()
        {
            var next = Request("library");
            if (next.Exists) library = next;
        }

        private void RefreshLayoutIfChanged()
        {
            string layout = CurrentLayout();
            if (library.S("layout") == layout) return;
            var next = Request("configure", "platform", "windows", "layout", layout);
            if (!next.Exists) return;
            library = next;
            if (center.Content != null) ShowPage(page);
        }

        private void ShowPage(string next)
        {
            if (active) return;
            referenceHeader.Visibility = Visibility.Visible;
            SetReferenceLessonCaption(false);
            MinWidth = 748;
            MinHeight = 540;
            page = next;
            var updated = Request("configure", "platform", "windows", "layout", CurrentLayout());
            if (updated.Exists) library = updated;
            UpdateHeader();
            switch (page)
            {
                case HomePage: beginControl = null; center.Content = BuildReferenceHome(); break;
                case TypingPage: center.Content = BuildTyping(); break;
                case EditorPage: center.Content = BuildEditor(); break;
                case ProgressPage: center.Content = BuildProgress(); break;
                default: center.Content = BuildLive(); break;
            }
        }

        private void OpenLiveSkill(string id)
        {
            var skill = library.Items("skills").Select(entry => entry.D("skill")).FirstOrDefault(entry => entry.S("id") == id);
            legacyLive = true;
            selectedGroup = skill == null ? "" : skill.S("group");
            selectedSkill = id;
            selectedLiveKind = 0;
            ShowPage(LivePage);
            if (beginControl != null) beginControl.Focus();
        }

        private void OpenTypingSet(string id)
        {
            selectedTyping = id;
            ShowPage(TypingPage);
            if (beginControl != null) beginControl.Focus();
        }

        private FrameworkElement BuildLive()
        {
            if (!legacyLive) return BuildLabHome();
            var root = PagePanel("Live", "Сочетания для работы без мыши");
            root.Children.Add(ActionButton("Классы и рабочий стол", delegate { legacyLive = false; ShowPage(LivePage); }));
            var choices = Stack(true);
            var all = library.Items("skills").Select(item => item.D("skill")).Where(item => !item.B("hidden")).ToArray();
            var groups = all.Select(item => item.S("group")).Distinct().Select(group => new Option(group, group)).ToArray();
            groupSelect = Select("Группа", groups, selectedGroup, 190);
            taskSelect = Select("Задание", new Option[0], "", 248);
            kindSelect = Select("Подбор", new[] { "Одно задание", "Смешанная серия", "Изученное" }, selectedLiveKind, 140);
            levelSelect = Select("Сложность", new[] { "Авто", "Базовый", "Средний", "Продвинутый" }, selectedLiveLevel, 116);
            measureSelect = Select("Число заданий", new[] { "10 заданий", "20 заданий", "40 заданий" }, selectedMeasure, 122);
            choices.Children.Add(groupSelect);
            choices.Children.Add(taskSelect);
            choices.Children.Add(kindSelect);
            choices.Children.Add(levelSelect);
            choices.Children.Add(measureSelect);
            root.Children.Add(choices);

            var keys = Label("", 24, Accent, true);
            keys.Margin = new Thickness(1, 21, 0, 7);
            root.Children.Add(keys);
            var details = Label("", 13, Dim);
            details.MaxWidth = 760;
            details.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(details);
            var actions = Stack(true);
            actions.Margin = new Thickness(0, 14, 0, 0);
            beginControl = ActionButton("Начать · F5", delegate { StartLive(); }, true);
            actions.Children.Add(beginControl);
            var technique = ActionButton("", delegate
            {
                var result = Request("settings", "paired", !library.B("rightHandChord", true));
                if (result.Exists) library = result;
                ((Button)actions.Children[1]).Content = Technique();
            });
            technique.Content = Technique();
            actions.Children.Add(technique);
            root.Children.Add(actions);
            Action describe = delegate
            {
                var chosen = groupSkills.FirstOrDefault(skill => skill.S("id") == Get(taskSelect));
                keys.Text = chosen == null ? "Выберите задание для серии" : "Пробел — СТАРТ";
                details.Text = chosen == null ? "" : chosen.S("title");
                var mode = kindSelect.SelectedIndex;
                taskSelect.IsEnabled = mode == 0;
                beginControl.IsEnabled = mode == 2 ? all.Length > 0 : groupSkills.Length > 0;
                technique.Visibility = chosen != null && chosen.S("id").StartsWith("terminal-", StringComparison.Ordinal)
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            Action rebuild = delegate
            {
                groupSkills = all.Where(item => item.S("group") == Get(groupSelect)).ToArray();
                var options = groupSkills.Select(skill => new Option(skill.S("id"), skill.S("title"))).ToArray();
                taskSelect.ItemsSource = options;
                taskSelect.SelectedIndex = Pick(options, selectedSkill);
                describe();
            };
            groupSelect.SelectionChanged += delegate { rebuild(); };
            taskSelect.SelectionChanged += delegate { describe(); };
            kindSelect.SelectionChanged += delegate { describe(); };
            rebuild();
            return Scroll(root);
        }

        private string Technique()
        { return library.B("rightHandChord", true) ? "Ctrl и Shift справа" : "Ctrl справа, Shift слева"; }

        private FrameworkElement BuildTyping()
        {
            var root = PagePanel("Печать", "Слова, фразы, упражнения и Python из piling-main");
            var items = new List<Option>
            {
                new Option("Project", "Python · piling-main"), new Option("Russian", "Русские фразы"),
                new Option("English", "Английские фразы"), new Option("WordsRU", "Русские слова"),
                new Option("WordsEN", "Английские слова"), new Option("Symbols", "Спецсимволы"),
                new Option("WeakRU", "Промахи · русский"), new Option("WeakEN", "Промахи · английский")
            };
            var catalog = library.D("typing");
            items.AddRange(catalog.Items("projects").Select(item => new Option(item.S("id"), "Python · " + item.S("title"))));
            items.AddRange(catalog.Items("sets").Select(item => new Option(item.S("id"), "Свой текст · " + item.S("title"))));
            items.AddRange(catalog.Items("lessons").Select(item => new Option(item.S("id"), "Урок · " + item.S("title"))));
            var choices = Stack(true);
            typingSelect = Select("Урок", items.ToArray(), selectedTyping, 300);
            typingLevel = Select("Сложность", new[] { "Базовый", "Средний", "Продвинутый" }, selectedTypingLevel, 146);
            typingTime = Select("Время", new[] { "Без таймера", "1 минута", "3 минуты", "5 минут" }, selectedTime, 136);
            choices.Children.Add(typingSelect);
            choices.Children.Add(typingLevel);
            choices.Children.Add(typingTime);
            beginControl = ActionButton("Начать · F5", delegate { StartTyping(); }, true);
            choices.Children.Add(beginControl);
            root.Children.Add(choices);
            return Scroll(root);
        }

        private void StartLive()
        {
            if (!legacyLive) { StartLab(); return; }
            selectedGroup = Get(groupSelect);
            selectedSkill = Get(taskSelect);
            selectedLiveKind = Math.Max(0, kindSelect.SelectedIndex);
            selectedLiveLevel = Math.Max(0, levelSelect.SelectedIndex);
            selectedMeasure = Math.Max(0, measureSelect.SelectedIndex);
            var skills = selectedLiveKind == 2 ? library.Items("skills").Select(entry => entry.D("skill")).Where(entry => !entry.B("hidden"))
                : selectedLiveKind == 1 ? groupSkills.AsEnumerable() : groupSkills.Where(entry => entry.S("id") == selectedSkill);
            var answer = Request("shortcut-start", "ids", skills.Select(entry => entry.S("id")).ToArray(),
                "level", Math.Max(0, selectedLiveLevel - 1), "auto", selectedLiveLevel == 0,
                "studied", selectedLiveKind == 2, "limit", new[] { 10, 20, 40 }[selectedMeasure]);
            if (answer.Exists) StartSession(answer);
        }

        private void StartTyping()
        {
            selectedTyping = Get(typingSelect);
            selectedTypingLevel = Math.Max(0, typingLevel.SelectedIndex);
            selectedTime = Math.Max(0, typingTime.SelectedIndex);
            var answer = Request("typing-start", "mode", selectedTyping, "level", selectedTypingLevel,
                "seconds", new[] { 0, 60, 180, 300 }[selectedTime], "layout", CurrentLayout());
            if (answer.Exists) StartSession(answer);
        }

        private void StartSession(Data data)
        {
            active = true;
            paused = false;
            pressed.Clear();
            UpdateHeader();
            var root = Stack();
            root.Margin = new Thickness(24, ActualHeight < 680 ? 14 : 37, 24, 10);
            root.VerticalAlignment = VerticalAlignment.Top;
            timerLabel = Label("00:00", 17, Ink, true);
            timerLabel.FontFamily = new FontFamily("Courier New");
            root.Children.Add(new Border { Child = timerLabel, CornerRadius = new CornerRadius(7), BorderBrush = Edge,
                BorderThickness = new Thickness(2), Background = Card, Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, ActualHeight < 680 ? 10 : 23) });
            var toolbar = new DockPanel { LastChildFill = false };
            root.Children.Add(toolbar);
            var operations = Stack(true);
            DockPanel.SetDock(operations, Dock.Right);
            toolbar.Children.Add(operations);
            keyboardControl = ActionButton("⌨", delegate { ToggleHint(true); });
            handsControl = ActionButton("👐", delegate { ToggleHint(false); });
            mistakeControl = ActionButton("✕", ToggleMistakeHint);
            pauseControl = ActionButton("Ⅱ", TogglePause);
            foreach (var item in new[] {keyboardControl, handsControl, mistakeControl, pauseControl})
            {
                item.MinWidth = 24;
                item.MinHeight = 19;
                item.Padding = new Thickness(3, 0, 3, 0);
                operations.Children.Add(item);
            }
            titleLabel = Label("", 16, Ink);
            titleLabel.MaxWidth = Math.Max(390, ActualWidth - 330);
            titleLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            titleLabel.TextWrapping = TextWrapping.NoWrap;
            toolbar.Children.Add(titleLabel);
            stopControl = ActionButton("ОСТАНОВИТЬ", FinishSession);
            stopControl.MinHeight = 17;
            stopControl.FontSize = 11;
            stopControl.Padding = new Thickness(5, 0, 5, 0);
            stopControl.Margin = new Thickness(6, 0, 0, 0);
            stopControl.Background = Ui.Brush("#B80924");
            toolbar.Children.Add(stopControl);
            tape = new FocusTape { Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(tape);
            lessonProgress = new ProgressBar { Height = 5, Minimum = 0, Maximum = 1, Background = Ui.Brush("#191D25"),
                Foreground = Ui.Brush("#8296A8"), BorderThickness = new Thickness(0),
                Margin = new Thickness(ActualWidth * .09, 13, ActualWidth * .09, 0) };
            root.Children.Add(lessonProgress);
            feedback = Label("", 13, Dim);
            feedback.TextWrapping = TextWrapping.NoWrap;
            feedback.TextTrimming = TextTrimming.CharacterEllipsis;
            feedback.Margin = new Thickness(1, 7, 0, 0);
            feedback.Height = 21;
            root.Children.Add(feedback);
            sceneLabel = Label("", 13, Ink);
            sceneLabel.FontFamily = Ui.Mono.FontFamily;
            sceneLabel.TextWrapping = TextWrapping.NoWrap;
            sceneLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            sceneLabel.Height = 24;
            root.Children.Add(sceneLabel);
            keyboard = new LiveKeyboard { Margin = new Thickness(0, ActualHeight < 680 ? 12 : 42, 0, 0), IsHitTestVisible = false };
            root.Children.Add(keyboard);
            keyHints = Label("", 12, Dim);
            keyHints.TextAlignment = TextAlignment.Center;
            keyHints.Margin = new Thickness(0, 4, 0, 0);
            root.Children.Add(keyHints);
            center.Content = root;
            root.Focusable = true;
            root.Focus();
            RenderState(data);
            timer.Start();
        }

        private void Tick()
        {
            if (!active || paused) return;
            if (labActive)
            {
                var snapshot = Request("lab-state");
                if (snapshot.Exists) RenderLab(snapshot);
                return;
            }
            if (page == TypingPage)
            {
                var reply = Request("typing-state", "layout", CurrentLayout());
                if (reply.Exists) RenderState(reply);
            }
            else
            {
                var reply = Request("shortcut-state");
                if (reply.Exists) state = reply;
                RenderClock();
            }
        }

        private void RenderClock()
        {
            int elapsed = state.I("seconds");
            timerLabel.Text = (elapsed / 60).ToString("00") + ":" + (elapsed % 60).ToString("00");
        }

        private bool LayoutStep
        {
            get
            {
                string kind = state.D("skill").S("action");
                return kind == "layout" || kind == "layout-back";
            }
        }

        private void RenderState(Data incoming)
        {
            state = incoming;
            if (page == LivePage && state.B("needsSystem") && state.S("inputMode") == "waiting")
            {
                // Alt+F4 is intercepted by this foreground exercise window. Print and Win
                // combinations may be captured by Windows before reaching WPF.
                string mode = state.S("targetKeys") == "Alt+F4" && nativeWindow != null ? "exact" : "substitute";
                var reply = Request("shortcut-input-mode", "mode", mode, "stepToken", state.I("stepToken"));
                if (reply.Exists) state = reply;
                pressed.Clear();
            }
            if (state.B("finished"))
            {
                FinishSession();
                return;
            }
            if (page == LivePage) PaintLive();
            else PaintTyping();
            RenderClock();
            keyboardControl.ToolTip = "Клавиатура · " + DisplayLabGesture(HintKey());
            handsControl.ToolTip = "Руки · " + DisplayLabGesture(HandKey());
            mistakeControl.ToolTip = "Подсвечивать ошибочную клавишу красным";
            keyboardControl.Opacity = library.B("showKeys", true) ? 1 : .55;
            handsControl.Opacity = library.B("showHands", true) ? 1 : .55;
            mistakeControl.Opacity = library.B("showMistakeKey", true) ? 1 : .55;
            pauseControl.Content = paused ? "▶" : "Ⅱ";
            pauseControl.ToolTip = (paused ? "Продолжить" : "Пауза") + " · " + DisplayLabGesture(PauseKey());
            stopControl.ToolTip = "Завершить · " + DisplayLabGesture(EndKey());
            AutomationProperties.SetName(keyboardControl, (string)keyboardControl.ToolTip);
            AutomationProperties.SetName(handsControl, (string)handsControl.ToolTip);
            feedback.Visibility = feedback.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            lessonProgress.Value = page == LivePage ? (double)state.I("solved") / Math.Max(1, state.I("limit")) :
                state.N("fragmentProgress");
        }

        private void PaintTyping()
        {
            titleLabel.Text = "Уроки  ›  " + (state.S("wantedLayout") == "RU" ? "Русская" : "Английская") + " раскладка  ›  " + state.S("title", TypingPage);
            var before = state.S("before");
            var after = state.S("after");
            tape.Update(TapeText(Tail(before, 90)), after.Length == 0 ? "" : TapeText(after.Substring(0, 1)),
                after.Length == 0 ? "" : TapeText(after.Substring(1, Math.Min(after.Length - 1, 90))), !state.B("correct", true));
            feedback.Text = paused ? "Пауза. Нажмите " + DisplayLabGesture(PauseKey()) + " для продолжения." :
                state.S("wantedLayout") != CurrentLayout() ? "Переключите раскладку: " + state.S("wantedLayout") : "";
            sceneLabel.Visibility = Visibility.Collapsed;
            keyHints.Text = "";
            var targets = new List<Data>();
            if (state.S("key").Length > 0) targets.Add(Target(state.S("key"), state.I("finger", -1)));
            string modifier = state.S("modifier");
            if (modifier.Length > 0) targets.Add(Target(modifier, modifier == "LShift" ? 0 : 7));
            keyboard.Update(library.Items("keyboard"), state.S("wantedLayout", CurrentLayout()), targets,
                Enumerable.Empty<string>(), library.B("showMistakeKey", true) && !state.B("correct", true) ? lastTypedKey : "",
                library.B("showKeys", true), library.B("showHands", true), false,
                library.S("keyboardProfile", "matebook-d16"), library.S("fnMode", "hotkeys"));
        }

        private void PaintLive()
        {
            var skill = state.D("skill");
            titleLabel.Text = "Live  ›  " + skill.S("group") + "  ›  " + skill.S("title", LivePage);
            string original = state.S("targetKeys"), effective = state.S("inputKeys", original);
            bool substitute = state.S("inputMode") == "substitute";
            string target = DisplayLabGesture(effective);
            string upcoming = "";
            if (LayoutStep)
            {
                effective = state.S("activity") == "layout-after" ? original : "A";
                target = DisplayLabGesture(effective);
                upcoming = state.S("activity") == "layout-after" ? " → A" : " → " + DisplayLabGesture(original) + " → A";
            }
            else if (skill.S("action") == "reboot") target = "reboot";
            else if (skill.S("character").Length > 0) target = skill.S("character");
            else if (target.Length == 0) target = "Подготовка";
            else
            {
                string[] steps = skill.S("keys").Split(new[] { " → " }, StringSplitOptions.RemoveEmptyEntries);
                if (state.I("step") + 1 < steps.Length) upcoming = " → " + DisplayLabGesture(steps[state.I("step") + 1]);
            }
            tape.Update(state.I("solved") + "/" + state.I("limit"), target, upcoming, state.S("feedback") == "error");
            feedback.Foreground = state.S("feedback") == "error" ? Error : Dim;
            feedback.Text = paused ? "Пауза. Нажмите " + DisplayLabGesture(PauseKey()) + " для продолжения." :
                state.S("status", "").Length > 0 ? state.S("status") : skill.S("note");
            if (!paused && state.Items("held").Any())
                feedback.Text += (feedback.Text.Length > 0 ? "   ·   " : "") + "Нажато " +
                    string.Join(" + ", state.Items("held").Select(item => LiveKeyboard.Title(item.Value())));
            string scene = SceneText(state.D("verifiedScene").Exists && state.S("status").Length > 0 ?
                state.D("verifiedScene") : state.D("scene"));
            if (substitute)
                scene = "Учебная замена: " + DisplayLabGesture(original) + " → " + DisplayLabGesture(effective) + (scene.Length == 0 ? "" : "   ·   " + scene);
            sceneLabel.Text = scene;
            sceneLabel.Visibility = scene.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            var targets = state.D("guide").Items("steps").Skip(state.I("step")).FirstOrDefault();
            var keys = LayoutStep && state.S("activity") != "layout-after" ? new[] { Target("A", 0) } :
                targets == null ? new Data[0] : targets.Items().ToArray();
            if (state.I("tier") == 2 && !LayoutStep && !substitute) keys = new Data[0];
            keyboard.Update(library.Items("keyboard"), CurrentLayout(), keys, state.Items("held").Select(key => key.Value()),
                library.B("showMistakeKey", true) ? state.S("mistakeKey") : "",
                library.B("showKeys", true), library.B("showHands", true), true,
                library.S("keyboardProfile", "matebook-d16"), library.S("fnMode", "hotkeys"));
            keyHints.Text = LayoutStep ? "Проверка по символу с физической клавиши A" : "";
        }

        private static Data Target(string key, int finger)
        { return new Data(new Dictionary<string, object> { { "key", key }, { "finger", finger } }); }

        private static string SceneText(Data scene)
        {
            string action = scene.S("action");
            switch (action)
            {
                case "screenshot":
                case "screenshot-copy":
                    return scene.B("captured") ? (scene.S("clipboard").Length > 0 ? "Снимок в учебном буфере" : "Учебный снимок готов")
                        : "Учебный снимок ещё не сделан";
                case "window-close": return scene.B("window") ? "Учебное окно открыто" : "Учебное окно закрыто";
                case "window-full": return scene.B("fullscreen") ? "Учебное окно во весь экран" : "Учебное окно в обычном режиме";
                case "window-max": return scene.B("maximized") ? "Учебное окно развёрнуто" : "Учебное окно в обычном режиме";
                case "reboot": return "$ " + scene.S("entry") + (scene.B("rebooted") ? "   ·   учебная команда принята" : "");
                case "terminal-open": return scene.B("terminal") ? "Учебный терминал открыт" : "Учебный терминал закрыт";
                case "copy":
                case "paste":
                case "transfer":
                case "cut":
                    return "Терминал: " + scene.S("body") + (scene.S("clipboard").Length > 0 ? "   ·   Учебный буфер: " + scene.S("clipboard") : "");
                default:
                    var tabs = scene.Items("tabs").Select(item => item.Value()).ToArray();
                    var selected = scene.I("active");
                    return tabs.Length == 0 ? "" : string.Join("  /  ", tabs.Select((tab, index) => selected == index ? "[" + tab + "]" : tab));
            }
        }

        private void ToggleHint(bool keys)
        {
            var reply = Request("settings", keys ? "keys" : "hands", !library.B(keys ? "showKeys" : "showHands", true));
            if (reply.Exists)
            {
                library = reply;
                RenderState(state);
            }
        }

        private void ToggleMistakeHint()
        {
            var reply = Request("settings", "mistakeKeys", !library.B("showMistakeKey", true));
            if (reply.Exists)
            {
                library = reply;
                RenderState(state);
            }
        }

        private string EndKey()
        { return page == LivePage ? state.S("exitKey", "Esc") : "Esc"; }

        private string HintKey()
        { return page == LivePage && state.S("inputKeys").Split('+').Contains("F4") ? "F9" : "F4"; }

        private string HandKey()
        { return page == LivePage && state.S("inputKeys").Split('+').Contains("F7") ? "F10" : "F7"; }

        private string PauseKey()
        { return page == LivePage && state.S("inputKeys").Split('+').Contains("F8") ? "F6" : "F8"; }

        private void TogglePause()
        {
            if (!active) return;
            if (!paused) { PauseSession(); return; }
            paused = false;
            pressed.Clear();
            var next = page == LivePage ? Request("shortcut-resume") : Request("typing-resume", "layout", CurrentLayout());
            if (next.Exists) RenderState(next);
        }

        private void PauseSession()
        {
            if (!active || paused) return;
            paused = true;
            pressed.Clear();
            var next = page == LivePage ? Request("shortcut-pause") : Request("typing-pause", "layout", CurrentLayout());
            if (next.Exists) state = next;
            RenderState(state);
        }

        private void FinishSession()
        {
            if (labActive) { FinishLab(); return; }
            if (!active) return;
            active = false;
            paused = false;
            pressed.Clear();
            timer.Stop();
            UpdateHeader();
            Data results;
            if (page == LivePage) results = Request("shortcut-result");
            else
            {
                Request("typing-stop", "layout", CurrentLayout());
                results = Request("typing-result", "layout", CurrentLayout());
            }
            var backdrop = center.Content as FrameworkElement;
            center.Content = null;
            if (results.Exists) center.Content = BuildResult(results, backdrop);
            else ShowPage(page);
        }

        private FrameworkElement BuildResult(Data result, FrameworkElement backdrop)
        {
            bool live = page == LivePage;
            var score = result.D(live ? "score" : "state");
            int seconds = live ? result.I("seconds") : score.I("seconds");
            int count = live ? result.I("solved") : score.I("count");
            double speed = live ? count * 60.0 / Math.Max(1, seconds) : score.N("cpm");
            var series = result.Items("series").ToArray();
            double?[] tempo;
            double[] errors;
            if (live)
            {
                tempo = series.Select(point => point.Exists && point.Number() > 0
                    ? (double?)(60000.0 / point.Number()) : null).ToArray();
                errors = series.Select(point => point.Exists ? 0.0 : 1.0).ToArray();
            }
            else
            {
                tempo = series.Select(point => (double?)point.N("speed")).ToArray();
                int previous = 0;
                errors = series.Select(point =>
                {
                    int current = point.I("errors");
                    int difference = Math.Max(0, current - previous);
                    previous = current;
                    return (double)difference;
                }).ToArray();
            }
            var panel = BuildStaminaResult(backdrop, "Результат", live ? "Сочетания" : score.S("title"),
                seconds, live ? "ВЫПОЛНЕНО" : "НАБРАНО", count.ToString(),
                Format(speed), live ? "действий/м" : "зн/м", Format(score.N("accuracy")) + "%",
                tempo, errors, live ? "Промахи " + Math.Max(0, score.I("count") - score.I("correct")) :
                    "Промахи " + score.I("errors"), "PracticeResult",
                "Выполнено " + count + ". Ошибки " + (live ? Math.Max(0, score.I("count") - score.I("correct")) : score.I("errors")) + ".",
                delegate { ShowPage(page); if (live) StartLive(); else StartTyping(); },
                delegate { ShowPage(page); }, delegate { ShowPage(ProgressPage); });
            return panel;
        }

        private FrameworkElement BuildStaminaResult(FrameworkElement backdrop, string title, string summary,
            int seconds, string countTitle, string count, string speed, string speedUnit, string accuracy,
            double?[] tempo, double[] errors, string detail, string automationId, string helpText,
            Action restart, Action choose, Action progress, string thirdTitle = "Прогресс")
        {
            var root = new Grid { Background = Ui.Brush("#1D2430") };
            if (backdrop != null)
            {
                backdrop.IsHitTestVisible = false;
                root.Children.Add(backdrop);
                root.Children.Add(new Border { Background = Ui.Brush("#BF080B12") });
            }
            var card = Stack();
            card.Background = Ui.Brush("#292E39");
            var heading = Label(title, 17, Ink);
            heading.Margin = new Thickness(16, 12, 16, 9);
            AutomationProperties.SetAutomationId(heading, automationId);
            AutomationProperties.SetHelpText(heading, helpText);
            card.Children.Add(heading);
            card.Children.Add(new Border { Height = 1, Background = Ui.Brush("#3D4656") });
            var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(13, 13, 13, 10) };
            metrics.Children.Add(ResultMetric("time", "ВРЕМЯ", (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"), ""));
            metrics.Children.Add(ResultMetric("count", countTitle, count, liveResultUnit(countTitle)));
            metrics.Children.Add(ResultMetric("speed", "СКОРОСТЬ", speed, speedUnit));
            metrics.Children.Add(ResultMetric("accuracy", "ТОЧНОСТЬ", accuracy, ""));
            card.Children.Add(metrics);
            var chart = new LiveResultChart(tempo, errors) { Margin = new Thickness(18, 1, 18, 2) };
            card.Children.Add(chart);
            var legend = Label("▰  Скорость        ▰  Ошибки", 11, Dim);
            legend.TextAlignment = TextAlignment.Center;
            legend.Margin = new Thickness(0, 1, 0, 8);
            card.Children.Add(legend);
            var caption = Label(summary + (detail.Length > 0 ? "   ·   " + detail : ""), 12, Dim);
            caption.TextAlignment = TextAlignment.Center;
            caption.Margin = new Thickness(12, 0, 12, 11);
            card.Children.Add(caption);
            var footer = Stack(true);
            footer.HorizontalAlignment = HorizontalAlignment.Center;
            footer.Margin = new Thickness(0, 10, 0, 10);
            var finish = ResultButton("Завершить", choose, "#2862DD");
            var repeat = ResultButton("Начать сначала", restart, "#2871E0");
            var review = ResultButton(thirdTitle, progress, "#BC1732");
            if (automationId != "LabPauseResult") beginControl = repeat;
            if (automationId == "LabResult")
            {
                AutomationProperties.SetAutomationId(finish, "LabChooseClass");
                AutomationProperties.SetAutomationId(repeat, "LabRepeat");
            }
            else if (automationId == "LabPauseResult")
            {
                AutomationProperties.SetAutomationId(finish, "LabPauseFinish");
                AutomationProperties.SetAutomationId(repeat, "LabPauseRestart");
                AutomationProperties.SetAutomationId(review, "LabPauseContinue");
            }
            footer.Children.Add(finish);
            footer.Children.Add(repeat);
            footer.Children.Add(review);
            card.Children.Add(new Border { Background = Ui.Brush("#222630"), Child = footer });
            var shell = new Border { Child = card, Width = 664, MaxWidth = 664,
                Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = Ui.Brush("#394353"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Background = Ui.Brush("#292E39") };
            root.Children.Add(shell);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            { if (automationId == "LabPauseResult") review.Focus(); else repeat.Focus(); }));
            return root;
        }

        private static string liveResultUnit(string countTitle)
        { return countTitle == "НАБРАНО" ? "зн" : ""; }

        private static FrameworkElement ResultMetric(string icon, string title, string value, string unit)
        {
            var row = Stack(true);
            row.VerticalAlignment = VerticalAlignment.Center;
            var iconImage = new System.Windows.Controls.Image { Source = ResultIcon(icon), Width = 42, Height = 42,
                Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(iconImage);
            var text = Stack();
            text.Margin = new Thickness(7, 0, 0, 0);
            text.Children.Add(Label(title, 10, Dim, true));
            var metricValue = Label(value, 23, Ink, true);
            AutomationProperties.SetAutomationId(metricValue, "ResultMetric-" + icon);
            AutomationProperties.SetName(metricValue, title + " " + value + (unit.Length == 0 ? "" : " " + unit));
            text.Children.Add(metricValue);
            if (unit.Length > 0) text.Children.Add(Label(unit, 10, Dim));
            row.Children.Add(text);
            return row;
        }

        private static BitmapSource ResultIcon(string name)
        {
            var source = new BitmapImage();
            using (var stream = typeof(LiveStudio).Assembly.GetManifestResourceStream("TouchTyping.Result." + name + ".png"))
            {
                if (stream == null) throw new InvalidOperationException("Отсутствует значок результата: " + name);
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.StreamSource = stream;
                source.EndInit();
            }
            source.Freeze();
            return source;
        }

        private static Button ResultButton(string title, Action handler, string color)
        {
            var button = ActionButton(title, handler);
            button.MinWidth = 160;
            button.MinHeight = 35;
            button.Padding = new Thickness(8, 4, 8, 4);
            button.Margin = new Thickness(3, 0, 3, 0);
            button.Background = Ui.Brush(color);
            button.BorderThickness = new Thickness(0);
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.Medium;
            button.FontFamily = LiveUiFont.ForWeight(FontWeights.Medium);
            return button;
        }

        private static void AddTrend(Panel panel, string title, double?[] points, Brush color)
        {
            var line = Label(title, 13, Dim);
            line.Margin = new Thickness(0, 14, 0, 3);
            panel.Children.Add(line);
            panel.Children.Add(new LiveTrend(points, color));
        }

        private static void AddMistakes(Panel panel, string title, IEnumerable<Data> records)
        {
            string summary = string.Join("  ·  ", records.Select(item => LiveKeyboard.Title(item.S("key")) + " ×" + item.I("count")));
            if (summary.Length == 0) return;
            var mistakes = Label(title + "   " + summary, 13, Dim);
            mistakes.Margin = new Thickness(0, 9, 0, 0);
            panel.Children.Add(mistakes);
        }

        private static void AddMetric(Panel list, string name, string value)
        {
            var card = Stack();
            card.Children.Add(Label(name, 12, Dim));
            card.Children.Add(Label(value, 24, Ink, true));
            list.Children.Add(new Border
            {
                Child = card, MinWidth = 138, Padding = new Thickness(11, 6, 11, 7), Margin = new Thickness(0, 0, 8, 0),
                Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8)
            });
        }

        private static string Format(double value) { return value.ToString("0.#", CultureInfo.CurrentCulture); }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Key input = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!active)
            {
                if (HandleReferenceResultKey(input)) { e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && input >= Key.D1 && input <= Key.D4)
                {
                    e.Handled = true;
                    ShowPage(new[] { LivePage, TypingPage, EditorPage, ProgressPage }[input - Key.D1]);
                }
                else if (Keyboard.Modifiers == ModifierKeys.None && (input == Key.F5 ||
                    (input == Key.Space && !(Keyboard.FocusedElement is ComboBox) &&
                    (!(Keyboard.FocusedElement is Button) || Keyboard.FocusedElement == beginControl))) &&
                    beginControl != null && beginControl.IsEnabled && (page == LivePage || page == TypingPage))
                {
                    e.Handled = true;
                    if (page == LivePage) StartLive(); else StartTyping();
                }
                return;
            }
            string key = PhysicalKey(input);
            if (key.Length == 0) return;
            if (labActive)
            {
                e.Handled = true;
                if (labLimitOverlay != null && ReferenceEquals(center.Content, labLimitOverlay))
                {
                    if (input == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = false; return; }
                    var focused = Keyboard.FocusedElement as Button;
                    if (!e.IsRepeat && input == Key.Tab &&
                        (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
                    {
                        labLimitArmedButton = null;
                        (focused == labLimitContinue ||
                            focused == null && Keyboard.Modifiers == ModifierKeys.Shift
                            ? labLimitFinish : labLimitContinue).Focus();
                    }
                    else if (!e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None &&
                        (input == Key.Enter || input == Key.Space) &&
                        (focused == labLimitContinue || focused == labLimitFinish))
                    {
                        labLimitArmedButton = focused;
                        labLimitArmedKey = input;
                    }
                    return;
                }
                if (paused && !e.IsRepeat)
                {
                    if (labSavePending)
                    {
                        if (Keyboard.Modifiers == ModifierKeys.None &&
                            ((Keyboard.FocusedElement is Button &&
                              AutomationProperties.GetAutomationId((Button)Keyboard.FocusedElement) == "LabRetrySave" &&
                              (key == "Enter" || key == "Space")) || key == LabEndKey())) FinishLab();
                        return;
                    }
                    if (Keyboard.Modifiers == ModifierKeys.None)
                    {
                        var button = Keyboard.FocusedElement as Button;
                        string buttonId = button == null ? "" : AutomationProperties.GetAutomationId(button);
                        if ((key == "Space" || key == "Enter") &&
                            (buttonId == "LabPauseRestart" || buttonId == "LabPauseFinish"))
                        {
                            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            return;
                        }
                        if (key == "Space") { ResumeLab(); return; }
                        if (key == LabPauseKey()) { ResumeLab(); return; }
                        if (key == LabEndKey()) { FinishLab(); return; }
                    }
                    // Tab moves among the visible pause actions; Enter on Continue does nothing.
                    if (input == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
                        e.Handled = false;
                }
                return;
            }
            if (page == LivePage)
            {
                int vk = KeyInterop.VirtualKeyFromKey(input);
                e.Handled = HandleLiveKey(key, true, e.IsRepeat, Translated(vk, 0));
                return;
            }
            if (!IsModifier(key)) lastTypedKey = key;
            if (HandleControls(key, e.IsRepeat)) { e.Handled = true; return; }
            if (paused) { e.Handled = true; return; }
            string value = key == "Enter" ? "\n" : key == "Tab" ? "\t" : key == "Backspace" ? "\b" : null;
            if (value != null)
            {
                e.Handled = true;
                Type(value);
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (!active || page != LivePage) return;
            Key input = e.Key == Key.System ? e.SystemKey : e.Key;
            if (labActive)
            {
                if (labLimitOverlay != null && ReferenceEquals(center.Content, labLimitOverlay))
                {
                    e.Handled = true;
                    if (labLimitArmedButton != null && input == labLimitArmedKey)
                    {
                        var button = labLimitArmedButton;
                        labLimitArmedButton = null;
                        if (Keyboard.FocusedElement == button && Keyboard.Modifiers == ModifierKeys.None)
                            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }
                return;
            }
            string key = PhysicalKey(input);
            if (key.Length > 0) e.Handled = HandleLiveKey(key, false, false, "");
        }

        private void OnText(object sender, TextCompositionEventArgs e)
        {
            if (!active || page != TypingPage) return;
            e.Handled = true;
            if (!paused && !string.IsNullOrEmpty(e.Text) && e.Text.Length == 1 && !char.IsControl(e.Text[0])) Type(e.Text);
        }

        private void Type(string value)
        {
            var answer = Request("typing-key", "text", value, "layout", CurrentLayout());
            if (answer.Exists) RenderState(answer);
        }

        private bool HandleControls(string key, bool repeat)
        {
            if (pressed.Any(IsModifier) || (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) != 0)
                return false;
            if (paused && key == "Space") { if (!repeat) TogglePause(); return true; }
            if (key == EndKey()) { if (!repeat) FinishSession(); return true; }
            if (key == PauseKey()) { if (!repeat) TogglePause(); return true; }
            if (key == HintKey()) { if (!repeat) ToggleHint(true); return true; }
            if (key == HandKey()) { if (!repeat) ToggleHint(false); return true; }
            return false;
        }

        private bool HandleLiveKey(string key, bool down, bool repeat, string value)
        {
            if (down && HandleControls(key, repeat)) return true;
            if (LayoutStep && key != "A") return false;
            if (paused || state.S("inputMode") == "waiting") return true;
            if (down ? repeat || !pressed.Add(key) : !pressed.Remove(key)) return !LayoutStep;
            var reply = Request("shortcut-key", "key", key, "pressed", down, "text", value);
            if (reply.Exists) RenderState(reply);
            return true;
        }

        private IntPtr NativeInput(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x51 && labActive)
                QueueLabNativeLayout(lParam);
            if (message == 0x51 && !active && (page == HomePage || page == LivePage))
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (!active && (page == HomePage || page == LivePage)) RefreshLayoutIfChanged();
                }));
            if (!active || labActive) return IntPtr.Zero;
            long code = lParam.ToInt64();
            if (message == 0x112 && (wParam.ToInt32() & 0xfff0) == 0xf060)
            {
                // A delivered Alt+F4 is exercised inside the scene only.
                handled = true;
                return IntPtr.Zero;
            }
            if (page != LivePage || !IsActive) return IntPtr.Zero;
            if (message != 0x100 && message != 0x101 && message != 0x104 && message != 0x105) return IntPtr.Zero;
            bool down = message == 0x100 || message == 0x104;
            int vk = wParam.ToInt32(), scan = (int)((code >> 16) & 0xff);
            string key = scan == 0 ? PhysicalKey(KeyInterop.KeyFromVirtualKey(vk)) : ScanKey(scan, (code & (1L << 24)) != 0, vk);
            if (key.Length == 0) return IntPtr.Zero;
            handled = HandleLiveKey(key, down, (code & (1L << 30)) != 0, down ? Translated(vk, scan) : "");
            return IntPtr.Zero;
        }

        private static bool IsModifier(string key)
        {
            return key.StartsWith("L", StringComparison.Ordinal) || key.StartsWith("R", StringComparison.Ordinal)
                ? key.Length > 1 && new[] { "Shift", "Super", "Alt", "Ctrl" }.Contains(key.Substring(1)) : false;
        }

        private static string ScanKey(int scan, bool extended, int vk)
        {
            if (scan == 0x1d) return extended ? "RCtrl" : "LCtrl";
            if (scan == 0x38 && vk != 0xa2 && vk != 0xa3) return extended ? "RAlt" : "LAlt";
            if (scan == 0x2a) return "LShift";
            if (scan == 0x36) return "RShift";
            if (scan == 0x5b) return "LSuper";
            if (scan == 0x5c) return "RSuper";
            if (vk == 0x2c) return "Print";
            if (extended && scan == 0x1c) return "NumEnter";
            if (!extended && scan >= 0x47 && scan <= 0x53)
            {
                string pad = "789-456+1230.";
                char digit = pad[scan - 0x47];
                if (char.IsDigit(digit)) return "NumPad" + digit;
                return digit == '+' ? "NumAdd" : digit == '-' ? "NumSubtract" : "NumDecimal";
            }
            if (!extended)
            {
                if (scan >= 0x02 && scan <= 0x0d) return "1234567890-="[scan - 0x02].ToString();
                if (scan >= 0x10 && scan <= 0x1b) return "QWERTYUIOP[]"[scan - 0x10].ToString();
                if (scan >= 0x1e && scan <= 0x29) return "ASDFGHJKL;'\x0060"[scan - 0x1e].ToString();
                if (scan >= 0x2c && scan <= 0x35) return "ZXCVBNM,./"[scan - 0x2c].ToString();
                if (scan == 0x2b) return "\\";
            }
            return PhysicalKey(KeyInterop.KeyFromVirtualKey(vk));
        }

        internal static string PhysicalKey(Key input)
        {
            if (input >= Key.D0 && input <= Key.D9) return (input - Key.D0).ToString();
            if (input >= Key.A && input <= Key.Z || input >= Key.F1 && input <= Key.F24) return input.ToString();
            if (input >= Key.NumPad0 && input <= Key.NumPad9) return "NumPad" + (input - Key.NumPad0);
            switch (input)
            {
                case Key.Escape: return "Esc";
                case Key.Back: return "Backspace";
                case Key.Return: return "Enter";
                case Key.Space: return "Space";
                case Key.LeftShift: return "LShift";
                case Key.RightShift: return "RShift";
                case Key.LeftCtrl: return "LCtrl";
                case Key.RightCtrl: return "RCtrl";
                case Key.LeftAlt: return "LAlt";
                case Key.RightAlt: return "RAlt";
                case Key.LWin: return "LSuper";
                case Key.RWin: return "RSuper";
                case Key.PrintScreen: return "Print";
                case Key.Apps: return "Menu";
                case Key.Pause: return "Pause";
                case Key.Scroll: return "ScrollLock";
                case Key.Add: return "NumAdd";
                case Key.Subtract: return "NumSubtract";
                case Key.Multiply: return "NumMultiply";
                case Key.Divide: return "NumDivide";
                case Key.Decimal: return "NumDecimal";
                case Key.OemMinus: return "-";
                case Key.OemPlus: return "=";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemTilde: return "\x0060";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.Tab:
                case Key.Left:
                case Key.Right:
                case Key.Down:
                case Key.Up:
                case Key.Home:
                case Key.End:
                case Key.Insert:
                case Key.Delete:
                case Key.PageUp:
                case Key.PageDown:
                case Key.CapsLock: return input.ToString();
                default: return "";
            }
        }

        private static string Translated(int vk, int scan)
        {
            if (vk == 13) return "\n";
            if (vk == 8) return "\b";
            if (vk == 9) return "\t";
            var keyboard = new byte[256];
            if (!GetKeyboardState(keyboard)) return "";
            var output = new StringBuilder(8);
            int result = ToUnicodeEx((uint)vk, (uint)scan, keyboard, output, output.Capacity, 4, KeyboardLayout(0));
            return result > 0 && result <= output.Length ? output.ToString(0, result) : "";
        }

        [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")]
        private static extern IntPtr KeyboardLayout(uint thread);
        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState([Out] byte[] state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder output, int size, uint flags, IntPtr layout);

        internal static string DisplayGesture(string gesture)
        {
            return string.Join(" → ", gesture.Split(new[] { " → " }, StringSplitOptions.None)
                .Select(chord => string.Join(" + ", chord.Split('+').Select(LiveKeyboard.Title))));
        }

        private static string TapeText(string value) { return value.Replace("\r", "").Replace("\n", "↵").Replace("\t", "⇥"); }
        private static string Tail(string value, int count) { return value.Length > count ? value.Substring(value.Length - count) : value; }

        private static StackPanel Stack(bool horizontal = false)
        { return new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical }; }

        private static TextBlock Label(string text, double size = 14, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, FontFamily = bold ? LiveUiFont.ForWeight(FontWeights.Medium) : LiveUiFont.Sans, Foreground = color ?? Ink,
                FontWeight = bold ? FontWeights.Medium : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.28 };
        }

        private static Button ActionButton(string text, Action handler, bool primary = false)
        {
            var button = Ui.Button(text, handler, primary);
            button.Background = primary ? Accent : Card;
            button.Foreground = primary ? Night : Ink;
            button.BorderBrush = primary ? Accent : Edge;
            button.MinHeight = 32;
            button.FontSize = 13;
            button.FontWeight = FontWeights.Medium;
            button.FontFamily = LiveUiFont.ForWeight(FontWeights.Medium);
            button.Padding = new Thickness(9, 4, 9, 4);
            button.Margin = new Thickness(0, 0, 6, 0);
            return button;
        }

        private static StackPanel PagePanel(string title, string summary)
        {
            var root = Stack();
            root.Margin = new Thickness(24, 22, 24, 20);
            root.MaxWidth = 1130;
            var heading = Label(title, 32, Ink, true);
            var sub = Label(summary, 14, Dim);
            sub.Margin = new Thickness(0, 3, 0, 16);
            root.Children.Add(heading);
            root.Children.Add(sub);
            return root;
        }

        private static ScrollViewer Scroll(FrameworkElement content)
        { return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; }

        private sealed class Option
        {
            internal readonly string Value, Caption;
            internal Option(string value, string caption) { Value = value; Caption = caption; }
            public override string ToString() { return Caption; }
        }

        private static ComboBox Select(string name, Option[] options, string value, double width)
        {
            var combo = new ComboBox
            {
                Width = width, Height = 34, Margin = new Thickness(0, 0, 7, 0), ItemsSource = options,
                SelectedIndex = Pick(options, value), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Ui.Brush("#E2E4E9"), Foreground = Night, BorderBrush = Edge
            };
            AutomationProperties.SetName(combo, name);
            return combo;
        }

        private static ComboBox Select(string name, string[] options, int index, double width)
        {
            var combo = Select(name, options.Select((item, position) => new Option(position.ToString(), item)).ToArray(), index.ToString(), width);
            return combo;
        }

        private static int Pick(Option[] options, string key)
        {
            if (options.Length == 0) return -1;
            return Math.Max(0, Array.FindIndex(options, option => option.Value == key));
        }

        private static string Get(ComboBox box)
        { return box.SelectedItem is Option ? ((Option)box.SelectedItem).Value : ""; }
    }
}
