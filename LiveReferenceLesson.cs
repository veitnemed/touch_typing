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
        private bool? commandTheoryChoice;
        private Border commandTheoryRail;
        private ScrollViewer commandTheoryScroll;
        private TextBlock commandTheoryShell, commandTheoryTitle, commandTheoryWhy, commandTheoryObserve, commandTheoryToggleText;
        private string commandTheorySignature;
        private Button referenceTheory;
        private static string ArrowName(string token)
        {
            switch (token)
            {
                case "Up": return "Стрелка вверх";
                case "Down": return "Стрелка вниз";
                case "Left": return "Стрелка влево";
                case "Right": return "Стрелка вправо";
                default: return null;
            }
        }
        private static string LessonCap(string token)
        {
            switch (token)
            {
                case "Up": return "↑";
                case "Down": return "↓";
                case "Left": return "←";
                case "Right": return "→";
                default: return token;
            }
        }

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
            // A previous lesson’s explicit toggle must not override the new lesson’s guidance.
            commandTheoryChoice = null;
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
            AutomationProperties.SetAutomationId(referenceHelpText, "LabHelpText");
            Grid.SetRow(referenceHintPanel, 1);
            root.Children.Add(referenceHintPanel);

            labDesktop = new LabSceneView { MinHeight = 118, Focusable = true, WitnessPythonEditor = verifyMode &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR") == "1" };
            AutomationProperties.SetAutomationId(labDesktop, "LabDesktop");
            labDesktop.NotebookCommand += ApplyLabNotebookCommand;
            labDesktop.ExplorerCommand += ApplyLabExplorerCommand;
            labDesktop.ChromeSurfaceCommand += ApplyLabChromeSurfaceCommand;
            labDesktop.BrowserHistoryCommand += ApplyLabBrowserHistoryCommand;
            labDesktop.BrowserTabCommand += ApplyLabBrowserTabCommand;
            var sceneFrame = ReferenceLessonPanel(labDesktop, new Thickness(4), 13);
            var sceneRow = new Grid { Margin = new Thickness(16, 8, 16, 0) };
            sceneRow.ColumnDefinitions.Add(new ColumnDefinition());
            sceneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sceneRow.Children.Add(sceneFrame);
            var card = Stack();
            commandTheoryShell = ReferenceLabel("", 11, ReferenceMuted, true);
            commandTheoryTitle = ReferenceLabel("", 18, ReferenceText, true);
            commandTheoryTitle.Margin = new Thickness(0, 9, 0, 7);
            commandTheoryWhy = ReferenceLabel("", 14, ReferenceText, false);
            var readout = ReferenceLabel("Что проверить", 13, ReferenceMuted, true);
            readout.Margin = new Thickness(0, 16, 0, 5);
            commandTheoryObserve = ReferenceLabel("", 14, ReferenceText, false);
            AutomationProperties.SetAutomationId(commandTheoryTitle, "LabTheoryTitle");
            AutomationProperties.SetAutomationId(commandTheoryWhy, "LabTheoryWhy");
            AutomationProperties.SetAutomationId(commandTheoryObserve, "LabTheoryObserve");
            foreach (var line in new[] {commandTheoryShell, commandTheoryTitle, commandTheoryWhy, readout, commandTheoryObserve})
                card.Children.Add(line);
            commandTheoryScroll = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            commandTheoryRail = ReferenceLessonPanel(commandTheoryScroll, new Thickness(14), 12);
            commandTheoryRail.Margin = new Thickness(8, 0, 0, 0);
            commandTheoryRail.Visibility = Visibility.Collapsed;
            commandTheorySignature = null;
            Grid.SetColumn(commandTheoryRail, 1);
            sceneRow.Children.Add(commandTheoryRail);
            Grid.SetRow(sceneRow, 2);
            root.Children.Add(sceneRow);

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
            referenceTheory = ReferenceLessonButton("Теория", "Теория терминала · " + DisplayLabGesture("F2"),
                ToggleReferenceTerminalTheory, "LabTerminalTheory", 110);
            referenceTheory.Content = commandTheoryToggleText = ReferenceLabel("Теория", 13, ReferenceText, true);
            referenceTheory.Visibility = Visibility.Collapsed;
            labHint = ReferenceLessonButton("?", "Подсказка · F1", OpenReferenceLessonHelp, "LabHint", 26);
            labKeyAid = ReferenceLessonButton("⌨", "Показать или скрыть клавиатуру", delegate { ToggleLabAid(true); }, "LabKeyboardAid", 28);
            labHandAid = ReferenceLessonButton("👐", "Показать или скрыть руки", delegate { ToggleLabAid(false); }, "LabHandAid", 28);
            labMistakeAid = ReferenceLessonButton("✕", "Подсветка ошибочной клавиши", ToggleLabMistake, "LabMistakeHighlight", 26);
            labFinish = ReferenceLessonButton("■", "Результат", FinishLab, "LabFinish", 26);
            int tabIndex = 6;
            foreach (var button in new[] { referenceTheory, labHint, labKeyAid, labHandAid, labMistakeAid, labFinish })
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
                commandTheoryRail.Width = Math.Max(180, Math.Min(268, root.ActualWidth / 4));
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
    }
}
