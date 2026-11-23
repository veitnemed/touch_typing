using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private TextBlock referenceTaskStep, referenceProgressLabel, referenceHelpText;
        private string referenceKeySignature;
        private ProgressBar referenceScenarioProgress;
        private Border referenceHintPanel, referenceKeysFrame;
        private Expander referenceErrorReview;
        private bool referenceHelpOpen, referenceGuideExplicit;

        private FrameworkElement MakeReferenceLesson()
        {
            referenceHeader.Visibility = Visibility.Collapsed;
            SetReferenceLessonCaption(true);
            MinWidth = 600;
            MinHeight = 415;
            referenceHelpOpen = referenceGuideExplicit = false;
            referenceErrorReview = null;
            referenceKeySignature = null;

            var root = new Grid { Focusable = true, Background = ReferenceGradient("#18132F", "#21183D", 80) };
            for (int row = 0; row < 6; row++)
                root.RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var prompt = new DockPanel();
            var icon = new Border { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(11),
                Background = Ui.Brush("#322951"), Child = ReferenceIcon(labApp, 34), Margin = new Thickness(0, 0, 16, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            prompt.Children.Add(icon);

            labKeys = ReferenceLabel("", 13, ReferenceText, true);
            labKeys.MaxWidth = 218;
            AutomationProperties.SetAutomationId(labKeys, "LabKeys");
            var keysFrame = referenceKeysFrame = ReferenceLessonPanel(labKeys, new Thickness(10, 7, 10, 7), 9);
            keysFrame.Margin = new Thickness(11, 0, 0, 0);
            keysFrame.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(keysFrame, Dock.Right);
            prompt.Children.Add(keysFrame);

            var wording = Stack();
            wording.VerticalAlignment = VerticalAlignment.Center;
            labCounter = referenceTaskStep = ReferenceLabel("", 13, ReferenceMuted, false);
            AutomationProperties.SetAutomationId(labCounter, "LabCounter");
            wording.Children.Add(referenceTaskStep);
            labTask = ReferenceLabel("", 16, ReferenceText, true);
            labTask.Margin = new Thickness(0, 4, 0, 0);
            AutomationProperties.SetAutomationId(labTask, "LabTask");
            wording.Children.Add(labTask);
            prompt.Children.Add(wording);

            var taskBox = ReferenceLessonPanel(prompt, new Thickness(10, 8, 10, 8), 14);
            taskBox.MinHeight = 67;
            taskBox.Margin = new Thickness(16, 9, 16, 0);
            root.Children.Add(taskBox);

            referenceHelpText = ReferenceLabel("", 13, ReferenceMuted, false);
            var hintScroll = new ScrollViewer { Content = referenceHelpText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 67 };
            referenceHintPanel = ReferenceLessonPanel(hintScroll, new Thickness(12, 7, 12, 7), 9);
            referenceHintPanel.Margin = new Thickness(16, 8, 16, 0);
            referenceHintPanel.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(referenceHintPanel, "LabHelpPanel");
            Grid.SetRow(referenceHintPanel, 1);
            root.Children.Add(referenceHintPanel);

            labDesktop = new LabSceneView { MinHeight = 118, WitnessPythonEditor = verifyMode &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR") == "1" };
            AutomationProperties.SetAutomationId(labDesktop, "LabDesktop");
            var sceneFrame = ReferenceLessonPanel(labDesktop, new Thickness(4), 13);
            sceneFrame.Margin = new Thickness(16, 15, 16, 0);
            Grid.SetRow(sceneFrame, 2);
            root.Children.Add(sceneFrame);

            var footer = new DockPanel();
            var actionCluster = Stack(true);
            actionCluster.VerticalAlignment = VerticalAlignment.Center;
            labPause = ReferenceLessonButton("Ⅱ", "Пауза", ToggleLabPause, "LabPause", 48);
            var restart = ReferenceLessonButton("⟳", "Начать сначала", RestartLab, "LabRestart", 48);
            KeyboardNavigation.SetTabIndex(labPause, 4);
            KeyboardNavigation.SetTabIndex(restart, 5);
            actionCluster.Children.Add(labPause);
            actionCluster.Children.Add(restart);
            DockPanel.SetDock(actionCluster, Dock.Right);
            footer.Children.Add(actionCluster);
            var progress = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 16, 0) };
            referenceProgressLabel = ReferenceLabel("", 14, ReferenceMuted, false);
            referenceProgressLabel.Margin = new Thickness(0, 0, 16, 0);
            referenceProgressLabel.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(referenceProgressLabel, Dock.Left);
            progress.Children.Add(referenceProgressLabel);
            timerLabel = ReferenceLabel("", 13, ReferenceText, true);
            timerLabel.Margin = new Thickness(0, 0, 12, 0);
            timerLabel.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(timerLabel, "LabClock");
            DockPanel.SetDock(timerLabel, Dock.Left);
            progress.Children.Add(timerLabel);
            referenceScenarioProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 12,
                VerticalAlignment = VerticalAlignment.Center, Focusable = false, Foreground = Ui.Brush("#805CF2"),
                Background = Ui.Brush("#463C64"), Template = ReferenceProgressTemplate() };
            AutomationProperties.SetAutomationId(referenceScenarioProgress, "LabScenarioProgress");
            progress.Children.Add(referenceScenarioProgress);
            footer.Children.Add(progress);
            var footerFrame = ReferenceLessonPanel(footer, new Thickness(13, 6, 6, 6), 13);
            footerFrame.Margin = new Thickness(16, 10, 16, 0);
            footerFrame.MinHeight = 61;
            Grid.SetRow(footerFrame, 3);
            root.Children.Add(footerFrame);

            var feedbackStrip = new DockPanel { Margin = new Thickness(17, 4, 17, 6), MinHeight = 32 };
            var aids = Stack(true);
            aids.VerticalAlignment = VerticalAlignment.Center;
            var back = ReferenceLessonButton("‹", "Назад", BackFromLab, "LabBack", 24);
            back.Background = Brushes.Transparent;
            KeyboardNavigation.SetTabIndex(back, 0);
            aids.Children.Add(back);
            labHint = ReferenceLessonButton("?", "Подсказка · F1", OpenReferenceLessonHelp, "LabHint", 26);
            labKeyAid = ReferenceLessonButton("⌨", "Показать или скрыть клавиатуру", delegate { ToggleLabAid(true); }, "LabKeyboardAid", 28);
            labHandAid = ReferenceLessonButton("👐", "Показать или скрыть руки", delegate { ToggleLabAid(false); }, "LabHandAid", 28);
            labMistakeAid = ReferenceLessonButton("✕", "Подсветка ошибочной клавиши", ToggleLabMistake, "LabMistakeHighlight", 26);
            labFinish = ReferenceLessonButton("■", "Результат", FinishLab, "LabFinish", 26);
            int tabIndex = 6;
            foreach (var button in new[] { labHint, labKeyAid, labHandAid, labMistakeAid, labFinish })
            {
                button.Height = 27;
                button.Margin = new Thickness(3, 0, 0, 0);
                KeyboardNavigation.SetTabIndex(button, tabIndex++);
                aids.Children.Add(button);
            }
            AddReferenceLessonWindowActions(aids);
            DockPanel.SetDock(aids, Dock.Right);
            feedbackStrip.Children.Add(aids);
            feedback = labStatus = ReferenceLabel("", 12, ReferenceMuted, false);
            AutomationProperties.SetAutomationId(labStatus, "LabStatus");
            AutomationProperties.SetLiveSetting(labStatus, AutomationLiveSetting.Polite);
            labStatus.VerticalAlignment = VerticalAlignment.Center;
            feedbackStrip.Children.Add(labStatus);
            Grid.SetRow(feedbackStrip, 4);
            root.Children.Add(feedbackStrip);

            keyboard = new LiveKeyboard { IsHitTestVisible = false, Width = 1050 };
            labGuide = new Viewbox { Child = keyboard, Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly, MaxHeight = 142, Margin = new Thickness(16, 0, 16, 5) };
            Grid.SetRow(labGuide, 5);
            root.Children.Add(labGuide);
            root.SizeChanged += delegate
            {
                hintScroll.MaxHeight = root.ActualHeight < 500 ? 37 : 67;
                if (ReferenceEquals(center.Content, root)) UpdateReferenceGuideVisibility();
            };
            return root;
        }

        private void SetReferenceLessonCaption(bool compact)
        {
            if (!customLiveChrome) return;
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null) chrome.CaptionHeight = compact ? 25 : 71;
        }

        private void AddReferenceLessonWindowActions(Panel controls)
        {
            if (!customLiveChrome) return;
            var minimize = ReferenceHeaderButton("Свернуть", "minimize", delegate { SystemCommands.MinimizeWindow(this); });
            var maximize = ReferenceHeaderButton("Развернуть или восстановить", "maximize", delegate
            {
                if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
                else SystemCommands.MaximizeWindow(this);
            });
            var close = ReferenceHeaderButton("Закрыть", "close", delegate { SystemCommands.CloseWindow(this); });
            int tabIndex = 1;
            foreach (var button in new[] { minimize, maximize, close })
            {
                button.Width = 23;
                button.Height = 27;
                KeyboardNavigation.SetTabIndex(button, tabIndex++);
                controls.Children.Add(button);
            }
        }

        private string ReferenceAppTitle()
        {
            return labApp == "chrome" ? "Браузер" : labApp == "code" ? "VS Code" :
                labApp == "ubuntu" ? "Ubuntu" : labApp == "workday" ? "Рабочий день" : "Терминал";
        }

        private static Border ReferenceLessonPanel(UIElement child, Thickness inset, double radius)
        {
            return new Border { Child = child, Padding = inset, CornerRadius = new CornerRadius(radius),
                BorderThickness = new Thickness(1), BorderBrush = ReferenceEdge,
                Background = ReferenceGradient("#27204A", "#1D1737", 75) };
        }

        private static Button ReferenceLessonButton(string glyph, string name, Action action, string id, double width)
        {
            var b = ReferenceButton(action, 10);
            b.Content = ReferenceLabel(glyph, width > 40 ? 25 : 16, ReferenceText, true);
            b.Width = width; b.Height = width > 40 ? 45 : 29;
            b.Margin = new Thickness(6, 0, 0, 0); b.VerticalAlignment = VerticalAlignment.Center;
            b.Background = ReferenceGradient("#3D345E", "#30264E", 90);
            b.BorderBrush = Ui.Brush("#4B3E71"); b.BorderThickness = new Thickness(1);
            AutomationProperties.SetAutomationId(b, id);
            SetReferenceLessonAction(b, name);
            return b;
        }

        private static void SetReferenceLessonAction(Button button, string name)
        {
            button.ToolTip = name;
            AutomationProperties.SetName(button, name);
        }

        private static ControlTemplate ReferenceProgressTemplate()
        {
            var template = new ControlTemplate(typeof(ProgressBar));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var track = new FrameworkElementFactory(typeof(Border), "PART_Track");
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            track.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            grid.AppendChild(track);
            var fill = new FrameworkElementFactory(typeof(Border), "PART_Indicator");
            fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            fill.SetValue(Border.BackgroundProperty, ReferenceGradient("#A287FA", "#754EF2", 90));
            fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            grid.AppendChild(fill);
            template.VisualTree = grid;
            return template;
        }

        private void UpdateReferenceLesson(Data task)
        {
            int count = state.I("scenarioLength"), step = state.I("scenarioStep");
            referenceTaskStep.Text = count > 0 ? "Задание " + step + " из " + count : "Задание";
            referenceProgressLabel.Text = count > 0 ? "Шаг " + step + " из " + count : "Выполнено " + state.I("solved");
            referenceScenarioProgress.Maximum = Math.Max(1, count);
            referenceScenarioProgress.Value = Math.Max(0, Math.Min(step, count));
            referenceScenarioProgress.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(referenceScenarioProgress, referenceProgressLabel.Text);
            UpdateReferenceKeycaps(task, count, step);
            if (referenceHelpOpen) DrawReferenceLessonHelp(task);
        }

        private void UpdateReferenceKeycaps(Data task, int count, int step)
        {
            bool guided = state.B("showGuide", true);
            string raw = task.S("keys"), text = task.S("text");
            string shown = !guided ? "" : raw.Length > 0 ? DisplayLabGesture(raw) :
                task.S("id").StartsWith("open-") ? "⊞  →  " + text + "  →  Enter" :
                text.Length > 12 ? "Ctrl+R  ·  найдите фрагмент  →  Enter" : text;
            string signature = (guided ? "guided:" : "independent:") + shown + "|" + raw + "|" + step + "/" + count;
            if (signature == referenceKeySignature) return; // Input and timer ticks should not rebuild visual children.
            referenceKeySignature = signature;
            AutomationProperties.SetName(labKeys, shown + (count > 0 ? "   ·   шаг " + step + "/" + count : ""));
            referenceKeysFrame.Visibility = guided ? Visibility.Visible : Visibility.Collapsed;
            if (!guided || raw.Length == 0)
            {
                labKeys.Text = shown;
                return;
            }
            var parts = shown.Split(new[] { " + " }, StringSplitOptions.None);
            labKeys.Inlines.Clear();
            for (int index = 0; index < parts.Length; index++)
            {
                if (index > 0) labKeys.Inlines.Add(new Run(" + "));
                var chip = ReferenceLessonPanel(ReferenceLabel(parts[index], 13, ReferenceText, true),
                    new Thickness(7, 2, 7, 2), 6);
                labKeys.Inlines.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
            }
        }

        private void UpdateReferenceGuideVisibility()
        {
            if (labGuide == null) return;
            bool allows = library.B("showKeys", true) || library.B("showHands", true);
            bool room = ActualWidth >= 800 && ActualHeight >= 650 && !referenceHelpOpen;
            labGuide.Visibility = allows && !referenceHelpOpen && (room || referenceGuideExplicit) ? Visibility.Visible : Visibility.Collapsed;
            labGuide.MaxHeight = room ? 142 : 86;
        }

        private void OpenReferenceLessonHelp()
        {
            if (!labActive || paused || labSavePending) return;
            if (referenceHelpOpen) { CloseReferenceLessonHelp(); return; }
            referenceHelpOpen = true;
            if (!state.B("showGuide", true))
            {
                var next = Request("lab-hint");
                if (next.Exists) RenderLab(next);
            }
            DrawReferenceLessonHelp(state.D("task"));
            UpdateReferenceGuideVisibility();
        }

        private void DrawReferenceLessonHelp(Data task)
        {
            const string noHelp = "Выполните текущее действие в рабочей области.";
            referenceHelpText.Text = "Подсказка · " + (state.B("showGuide", true) ? task.S("hint", noHelp) : state.S("phase", noHelp)) +
                "\n" + string.Join(" · ", state.Items("objectives").Select(value => value.Value()));
            referenceHintPanel.Visibility = Visibility.Visible;
        }

        private void CloseReferenceLessonHelp()
        {
            referenceHelpOpen = false;
            referenceHintPanel.Visibility = Visibility.Collapsed;
            UpdateReferenceGuideVisibility();
            var lesson = center.Content as FrameworkElement;
            if (lesson != null) lesson.Focus();
        }

        private bool HandleReferenceResultKey(Key key)
        {
            if (referenceErrorReview == null || !referenceErrorReview.IsKeyboardFocusWithin ||
                Keyboard.Modifiers != ModifierKeys.None || (key != Key.Space && key != Key.Enter)) return false;
            referenceErrorReview.IsExpanded = !referenceErrorReview.IsExpanded;
            return true;
        }

        private FrameworkElement MakeReferenceLabResult(Data result, FrameworkElement backdrop, bool pausedPreview)
        {
            var outer = new Grid { Background = Ui.Brush("#17122F") };
            if (backdrop != null)
            {
                backdrop.IsHitTestVisible = false;
                outer.Children.Add(backdrop);
                outer.Children.Add(new Border { Background = Ui.Brush("#D1100B22") });
            }
            bool complete = result.B("completed");
            int count = result.I("solved"), seconds = result.I("seconds"), errors = result.I("errors");
            string title = pausedPreview ? "Пауза" : "Результат";
            string summary = state.S("scenario", ReferenceAppTitle());
            if (string.IsNullOrWhiteSpace(summary)) summary = ReferenceAppTitle();
            var card = Stack();
            KeyboardNavigation.SetTabNavigation(card, pausedPreview ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);

            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 17) };
            var badge = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(15),
                Background = Ui.Brush(pausedPreview ? "#3E335E" : "#21403B"), Margin = new Thickness(0, 0, 17, 0),
                VerticalAlignment = VerticalAlignment.Center };
            var badgeText = ReferenceLabel(pausedPreview ? "Ⅱ" : "✓", 30, Ui.Brush(pausedPreview ? "#C7B4FF" : "#60FA83"), true);
            badgeText.HorizontalAlignment = HorizontalAlignment.Center;
            badgeText.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = badgeText;
            DockPanel.SetDock(badge, Dock.Left);
            heading.Children.Add(badge);
            var headingText = Stack();
            headingText.VerticalAlignment = VerticalAlignment.Center;
            var titleLabel = ReferenceLabel(title, 29, ReferenceText, true);
            AutomationProperties.SetAutomationId(titleLabel, pausedPreview ? "LabPauseResult" : "LabResult");
            if (!pausedPreview && labDesktop != null && labDesktop.WitnessPythonEditor)
            {
                var snapshot = state.D("scene");
                var selected = snapshot.Items("windows").FirstOrDefault(item => item.B("active") && item.S("app") == "code");
                AutomationProperties.SetItemType(titleLabel, result.I("solved").ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + labDesktop.PythonEditorWitness(snapshot, selected));
            }
            AutomationProperties.SetHelpText(titleLabel, "Выполнено " + count + ". Ошибки " + errors + ". Рекорд " +
                result.I("record") + ". " + (pausedPreview ? "Занятие на паузе." : complete ? "Цель занятия выполнена." : "Занятие сохранено."));
            headingText.Children.Add(titleLabel);
            headingText.Children.Add(ReferenceLabel(summary, 16, ReferenceMuted, false));
            heading.Children.Add(headingText);
            card.Children.Add(heading);

            var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -9, 17) };
            metrics.Children.Add(ReferenceResultMetric("time", "Время", (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"), ""));
            metrics.Children.Add(ReferenceResultMetric("count", "Действия", count.ToString(), ""));
            metrics.Children.Add(ReferenceResultMetric("speed", "Темп", Format(count * 60.0 / Math.Max(1, seconds)), "д/мин"));
            card.Children.Add(metrics);

            if (!pausedPreview)
            {
                var details = new Expander { Header = ReferenceLabel("Работа над ошибками", 16, ReferenceText, true),
                    Foreground = ReferenceText, Padding = new Thickness(12), IsExpanded = false };
                AutomationProperties.SetAutomationId(details, "LabErrorReview");
                AutomationProperties.SetName(details, "Работа над ошибками");
                referenceErrorReview = details;
                var expanded = Stack();
                var accuracy = ReferenceLabel("Точность: " + Format(result.N("accuracy")) + "%", 15, ReferenceText, true);
                AutomationProperties.SetAutomationId(accuracy, "ResultMetric-accuracy");
                expanded.Children.Add(accuracy);
                expanded.Children.Add(ReferenceLabel("Ошибки: " + errors + "   ·   Рекорд: " + result.D("record").Value("—"), 14, ReferenceMuted, false));
                foreach (var mistake in result.Items("weak").Take(5))
                    expanded.Children.Add(ReferenceLabel(mistake.S("title", mistake.S("id")) + " ×" + mistake.I("errors"), 13, ReferenceMuted, false));
                expanded.Margin = new Thickness(7, 8, 7, 3);
                // Do not attach hidden review metrics to the running or collapsed automation tree.
                details.Expanded += delegate { details.Content = expanded; };
                details.Collapsed += delegate { details.Content = null; };
                var review = ReferenceLessonPanel(details, new Thickness(4, 6, 4, 6), 12);
                review.MinHeight = 56;
                review.Margin = new Thickness(0, 1, 0, 17);
                card.Children.Add(review);
            }

            var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, 0) };
            var exit = ReferenceResultAction("Завершить", pausedPreview ? (Action)FinishLab :
                delegate { legacyLive = false; ShowPage(LivePage); }, pausedPreview ? "LabPauseFinish" : "LabChooseClass", 0);
            var restart = ReferenceResultAction("Начать сначала", RestartLab, pausedPreview ? "LabPauseRestart" : "LabRepeat", 1);
            var next = ReferenceResultAction(pausedPreview ? "Продолжить" : "К прогрессу",
                pausedPreview ? (Action)ResumeLab : delegate { ShowPage(ProgressPage); }, pausedPreview ? "LabPauseContinue" : "LabProgress", 2);
            if (!pausedPreview) beginControl = restart;
            actions.Children.Add(exit); actions.Children.Add(restart); actions.Children.Add(next);
            card.Children.Add(actions);

            var inner = ReferenceLessonPanel(card, new Thickness(20), 18);
            var scroll = new ScrollViewer { Content = inner, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 614, Margin = new Thickness(16), Focusable = false };
            outer.Children.Add(scroll);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate
            { if (ReferenceEquals(center.Content, outer)) (pausedPreview ? next : restart).Focus(); }));
            return outer;
        }

        private static FrameworkElement ReferenceResultMetric(string icon, string title, string value, string unit)
        {
            var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var image = new Image { Source = ResultIcon(icon), Width = 42, Height = 42,
                Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 9, 0) };
            DockPanel.SetDock(image, Dock.Left);
            row.Children.Add(image);
            var text = Stack();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(ReferenceLabel(title, 13, ReferenceMuted, false));
            var valueText = ReferenceLabel(value, 22, ReferenceText, true);
            AutomationProperties.SetAutomationId(valueText, "ResultMetric-" + icon);
            AutomationProperties.SetName(valueText, title + " " + value + (unit.Length == 0 ? "" : " " + unit));
            text.Children.Add(valueText);
            if (unit.Length > 0) text.Children.Add(ReferenceLabel(unit, 11, ReferenceMuted, false));
            row.Children.Add(text);
            var frame = ReferenceLessonPanel(row, new Thickness(11, 8, 8, 8), 13);
            frame.MinHeight = 78; frame.Margin = new Thickness(0, 0, 9, 0);
            return frame;
        }

        private static Button ReferenceResultAction(string title, Action action, string id, int role)
        {
            var button = ReferenceButton(action, 12);
            var text = ReferenceLabel(title, 15, ReferenceText, true);
            text.TextAlignment = TextAlignment.Center;
            button.Content = text;
            button.Margin = new Thickness(0, 0, 8, 0);
            button.Padding = new Thickness(6, 6, 6, 6);
            button.MinHeight = 51;
            button.Background = role == 2 ? ReferenceGradient("#8B69FC", "#663BE4", 90) :
                role == 1 ? Ui.Brush("#241C3E") : Ui.Brush("#3B3259");
            button.BorderBrush = role == 1 ? Ui.Brush("#B1A2D1") : ReferenceEdge;
            button.BorderThickness = new Thickness(role == 1 ? 1.5 : 1);
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, title);
            return button;
        }
    }
}
