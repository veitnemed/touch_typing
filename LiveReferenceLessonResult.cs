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

            if (!pausedPreview && result.D("sourceReview").Exists)
            {
                var source = result.D("sourceReview");
                var receipt = Stack();
                var sourceCode = ReferenceLabel("Код сверки: " + source.S("code"),15,Ui.Brush("#A9D9AE"),true);
                AutomationProperties.SetAutomationId(sourceCode, "LabSourceReviewCode");
                receipt.Children.Add(sourceCode);
                var address = ReferenceLabel(source.S("url"),13,ReferenceMuted,false);
                AutomationProperties.SetAutomationId(address, "LabSourceReviewAddress");
                address.TextWrapping = TextWrapping.Wrap;
                receipt.Children.Add(address);
                receipt.Margin = new Thickness(0,0,0,11);
                card.Children.Add(receipt);
            }

            if (!pausedPreview && result.S("diskCapacityConclusion").Length > 0)
            {
                var verdict = ReferenceLabel(result.S("diskCapacityConclusion"), 14, ReferenceText, false);
                verdict.Margin = new Thickness(0, 0, 0, 11);
                AutomationProperties.SetAutomationId(verdict, "LabDiskCapacityConclusion");
                card.Children.Add(verdict);
            }

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
