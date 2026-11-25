using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
        private TextBlock referenceHelpText;
        private string referenceKeySignature;
        private ProgressBar referenceScenarioProgress;
        private Border referenceHintPanel, referenceKeysFrame;
        private Expander referenceErrorReview;
        private bool referenceHelpOpen;

        // The aid rail does not expose a control peer on its own. The visible guide
        // needs an accessible identity, actual bounds and a readable state.
        private sealed class LiveAidRail : Border
        {
            protected override AutomationPeer OnCreateAutomationPeer() { return new LiveAidRailPeer(this); }
        }

        private sealed class LiveAidRailPeer : FrameworkElementAutomationPeer
        {
            internal LiveAidRailPeer(LiveAidRail owner) : base(owner) { }
            protected override string GetClassNameCore() { return "LiveAidPanel"; }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Group; }
            protected override bool IsControlElementCore() { return true; }
            protected override bool IsContentElementCore() { return true; }
            protected override string GetHelpTextCore()
            {
                var guide = ((LiveAidRail)Owner).Child as LiveKeyboard;
                return guide == null ? base.GetHelpTextCore() : guide.ReferenceAidExtent;
            }

        }

        private FrameworkElement MakeReferenceLesson()
        {
            referenceHeader.Visibility = Visibility.Collapsed;
            SetReferenceLessonCaption(true);
            MinWidth = 600;
            MinHeight = 415;
            referenceHelpOpen = false;
            referenceErrorReview = null;
            referenceKeySignature = null;

            var root = new Grid { Focusable = true, Background = ReferenceHomeSurface() };
            for (int row = 0; row < 6; row++)
                root.RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var prompt = Stack();
            labTask = ReferenceLabel("", 21, ReferenceText, true);
            labTask.TextAlignment = TextAlignment.Center;
            labTask.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(labTask, "LabTask");
            prompt.Children.Add(labTask);
            labKeys = ReferenceLabel("", 17, ReferenceText, true);
            labKeys.TextAlignment = TextAlignment.Center;
            labKeys.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(labKeys, "LabKeys");
            referenceKeysFrame = new Border { Child = labKeys, Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center };
            prompt.Children.Add(referenceKeysFrame);

            var taskBox = ReferenceLessonPanel(prompt, new Thickness(14, 5, 14, 5), 11);
            taskBox.BorderBrush = Ui.Brush("#77658B");
            taskBox.Margin = new Thickness(16, 6, 16, 0);
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

            labDesktop = new LabSceneView { MinHeight = 118, Focusable = true, WitnessPythonEditor = verifyMode &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR") == "1" };
            AutomationProperties.SetAutomationId(labDesktop, "LabDesktop");
            labDesktop.NotebookCommand += ApplyLabNotebookCommand;
            labDesktop.ExplorerCommand += ApplyLabExplorerCommand;
            var sceneFrame = ReferenceLessonPanel(labDesktop, new Thickness(4), 13);
            sceneFrame.Margin = new Thickness(16, 8, 16, 0);
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
            timerLabel = ReferenceLabel("", 13, ReferenceText, true);
            timerLabel.Margin = new Thickness(0, 0, 12, 0);
            timerLabel.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(timerLabel, "LabClock");
            DockPanel.SetDock(timerLabel, Dock.Left);
            progress.Children.Add(timerLabel);
            referenceScenarioProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 12,
                VerticalAlignment = VerticalAlignment.Center, Focusable = false, Foreground = Ui.Brush("#A89BB9"),
                Background = Ui.Brush("#47424D"), Template = ReferenceProgressTemplate() };
            AutomationProperties.SetAutomationId(referenceScenarioProgress, "LabScenarioProgress");
            progress.Children.Add(referenceScenarioProgress);
            footer.Children.Add(progress);
            var footerFrame = ReferenceLessonPanel(footer, new Thickness(13, 6, 6, 6), 13);
            footerFrame.Margin = new Thickness(16, 7, 16, 0);
            footerFrame.MinHeight = 39;
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

            keyboard = new LiveKeyboard { IsHitTestVisible = false, ReferenceAidRail = true };
            AutomationProperties.SetAutomationId(keyboard, "LiveKeyboard");
            labGuide = new LiveAidRail { Child = keyboard, Height = 148, Margin = new Thickness(16, 0, 16, 5) };
            AutomationProperties.SetAutomationId(labGuide, "LiveAidPanel");
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
                labApp == "windows" ? "Windows 10" : labApp == "ubuntu" ? "Ubuntu" : labApp == "workday" ? "Рабочий день" : "Терминал";
        }

        private static Border ReferenceLessonPanel(UIElement child, Thickness inset, double radius)
        {
            return new Border { Child = child, Padding = inset, CornerRadius = new CornerRadius(radius),
                BorderThickness = new Thickness(1), BorderBrush = ReferenceEdge,
                Background = ReferencePanel };
        }

        private static Button ReferenceLessonButton(string glyph, string name, Action action, string id, double width)
        {
            var b = ReferenceButton(action, 10);
            b.Content = ReferenceLabel(glyph, width > 40 ? 25 : 16, ReferenceText, true);
            b.Width = width; b.Height = width > 40 ? 32 : 29;
            b.Margin = new Thickness(6, 0, 0, 0); b.VerticalAlignment = VerticalAlignment.Center;
            b.Background = ReferenceGradient("#3C3642", "#303039", 90);
            b.BorderBrush = Ui.Brush("#56515D"); b.BorderThickness = new Thickness(1);
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
            fill.SetValue(Border.BackgroundProperty, ReferenceGradient("#B0A3BF", "#887993", 90));
            fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            grid.AppendChild(fill);
            template.VisualTree = grid;
            return template;
        }

        private void UpdateReferenceLesson(Data task)
        {
            int count = state.I("scenarioLength"), step = state.I("scenarioStep");
            bool boundedCourse = state.S("routeKind") == "course" && state.I("actionLimit") > 0;
            int total = boundedCourse ? state.I("actionLimit") : count;
            int completed = Math.Max(0, Math.Min(total, state.I(boundedCourse ? "solved" : "scenarioCompletedGoals")));
            referenceScenarioProgress.Maximum = Math.Max(1, total);
            referenceScenarioProgress.Value = completed;
            referenceScenarioProgress.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(referenceScenarioProgress,
                (boundedCourse ? "Прогресс занятия: " : "Прогресс задания: ") + completed + " из " + total);
            UpdateReferenceKeycaps(task, count, step);
            if (referenceHelpOpen) DrawReferenceLessonHelp(task);
        }

        private void UpdateReferenceKeycaps(Data task, int count, int step)
        {
            bool guided = state.B("showGuide", true) || referenceHelpOpen;
            string raw = task.S("keys"), text = task.S("text");
            string shown = !guided ? "" : raw.Length > 0 ? raw.Replace("+", " + ") :
                task.S("id").StartsWith("open-") ? "⊞  →  " + text + "  →  Enter" :
                task.S("id").StartsWith("command/") && text.Length > 12 ? "Ctrl + R  →  Enter" : text;
            string signature = (guided ? "guided:" : "independent:") + shown + "|" + raw + "|" + step + "/" + count;
            if (signature == referenceKeySignature) return; // Input and timer ticks should not rebuild visual children.
            referenceKeySignature = signature;
            labKeys.ToolTip = guided && raw.Length > 0 && DisplayLabGesture(raw) != shown ? DisplayLabGesture(raw) : null;
            AutomationProperties.SetName(labKeys, shown + (count > 0 ? "   ·   шаг " + step + "/" + count : ""));
            referenceKeysFrame.Visibility = guided && shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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
                var chip = new Border { Child = ReferenceLabel(parts[index], 17, ReferenceText, true),
                    Padding = new Thickness(9, 2, 9, 2), CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#8F819D"),
                    Background = ReferenceGradient("#64556E", "#48414F", 70) };
                labKeys.Inlines.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
            }
        }

        private void UpdateReferenceGuideVisibility()
        {
            if (labGuide == null) return;
            bool keys = library.B("liveShowKeys"), hands = library.B("liveShowHands");
            bool allows = keys || hands;
            labGuide.Visibility = allows && !referenceHelpOpen ? Visibility.Visible : Visibility.Collapsed;
            string aidName = keys && hands ? "Клавиатура и положение рук" :
                keys ? "Клавиатура" : hands ? "Положение рук" : "Учебные подсказки скрыты";
            string keyLabels = keyboard == null ? null : keyboard.ReferenceAidDescription;
            AutomationProperties.SetName(labGuide, keys && !string.IsNullOrWhiteSpace(keyLabels) ?
                aidName + ". " + keyLabels : aidName);
            AutomationProperties.SetItemStatus(labGuide, (keys ? "keyboard" : "no-keyboard") +
                ";" + (hands ? "hands" : "no-hands"));
            // Keep the work surface primary. A short but intrinsically wide rail
            // lays out keycaps at native size instead of shrinking a 1050-DIP canvas.
            var lesson = center.Content as FrameworkElement;
            double lessonHeight = lesson != null ? lesson.ActualHeight : ActualHeight;
            labGuide.Height = Math.Max(100, Math.Min(keys ? 180 : 165, lessonHeight * (keys ? .26 : .23)));
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
            UpdateReferenceKeycaps(state.D("task"), state.I("scenarioLength"), state.I("scenarioStep"));
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
            UpdateReferenceKeycaps(state.D("task"), state.I("scenarioLength"), state.I("scenarioStep"));
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
            var outer = new Grid { Background = Ui.Brush("#191820") };
            if (backdrop != null)
            {
                backdrop.IsHitTestVisible = false;
                outer.Children.Add(backdrop);
                outer.Children.Add(new Border { Background = Ui.Brush("#B5100F15") });
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
                Background = Ui.Brush(pausedPreview ? "#413849" : "#21403B"), Margin = new Thickness(0, 0, 17, 0),
                VerticalAlignment = VerticalAlignment.Center };
            var badgeText = ReferenceLabel(pausedPreview ? "Ⅱ" : "✓", 30, Ui.Brush(pausedPreview ? "#D4C8DE" : "#A9D9AE"), true);
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
            if (!pausedPreview && verifyMode && labDesktop != null &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE") == "1")
                AutomationProperties.SetItemType(titleLabel, labDesktop.BrowserPracticeWitness(state.D("scene")));
            if (!pausedPreview && HasNotebookHandoffWitness(state))
                AutomationProperties.SetItemType(titleLabel, NotebookHandoffWitness(state));
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
                (Action)ReturnToLiveCatalog, pausedPreview ? "LabPauseFinish" : "LabChooseClass", 0);
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
            button.Background = role == 2 ? ReferenceGradient("#71617B", "#64556E", 90) :
                role == 1 ? Ui.Brush("#28272E") : Ui.Brush("#3C3642");
            button.BorderBrush = role == 1 ? Ui.Brush("#A89BB9") : ReferenceEdge;
            button.BorderThickness = new Thickness(role == 1 ? 1.5 : 1);
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, title);
            return button;
        }
    }
}
