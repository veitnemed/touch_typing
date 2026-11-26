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
            DrawReferenceTerminalTheory();
            if (referenceHelpOpen) DrawReferenceLessonHelp(task);
        }

        private void UpdateReferenceKeycaps(Data task, int count, int step)
        {
            bool guided = state.B("showGuide", true) || referenceHelpOpen;
            string raw = task.S("keys"), text = task.S("text");
            var steps = raw.Split(new[] { " → " }, StringSplitOptions.None);
            string shown = !guided ? "" : raw.Length > 0 ? string.Join(" → ", steps.Select(chord =>
                string.Join(" + ", chord.Split('+').Select(LessonCap)))) :
                task.S("id").StartsWith("open-") ? "⊞  →  " + text + "  →  Enter" :
                task.S("id") == "command/delivery-read-match" ? (state.S("desktopProfile") == "windows" ? "Get-Content" : "cat") + " [CSV]" :
                task.S("id") == "command/filter-incident" ? "grep [ID] app.log" :
                task.S("id") == "command/powershell-incident" ? "Select-String [ID] sync.log" :
                text;
            string signature = (guided ? "guided:" : "independent:") + shown + "|" + raw + "|" + step + "/" + count;
            if (signature == referenceKeySignature) return; // Input and timer ticks should not rebuild visual children.
            referenceKeySignature = signature;
            labKeys.ToolTip = guided && raw.Length > 0 && DisplayLabGesture(raw) != shown ? DisplayLabGesture(raw) : null;
            string accessible = raw.Length == 0 || !guided ? shown : string.Join(" → ", steps.Select(chord =>
                string.Join(" + ", chord.Split('+').Select(token => ArrowName(token) ?? token))));
            AutomationProperties.SetName(labKeys, accessible + (count > 0 ? "   ·   шаг " + step + "/" + count : ""));
            referenceKeysFrame.Visibility = guided && shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!guided || raw.Length == 0)
            {
                labKeys.Text = shown;
                return;
            }
            labKeys.Inlines.Clear();
            for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            {
                if (stepIndex > 0) labKeys.Inlines.Add(new Run(" → "));
                string[] keys = steps[stepIndex].Split('+');
                for (int i = 0; i < keys.Length; i++)
                {
                    if (i > 0) labKeys.Inlines.Add(new Run(" + "));
                    var chip = new Border { Child = ReferenceLabel(LessonCap(keys[i]), 17, ReferenceText, true),
                        ToolTip = ArrowName(keys[i]), Padding = new Thickness(9, 2, 9, 2), CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#8F819D"),
                        Background = ReferenceGradient("#64556E", "#48414F", 70) };
                    labKeys.Inlines.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
                }
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

        private void ToggleReferenceTerminalTheory()
        {
            if (!state.D("commandTheory").Exists || !labActive || paused || labSavePending || state.B("limitDecisionPending")) return;
            commandTheoryChoice = commandTheoryRail.Visibility != Visibility.Visible;
            DrawReferenceTerminalTheory();
            labDesktop.Focus();
        }

        private void DrawReferenceTerminalTheory()
        {
            var card = state.D("commandTheory");
            bool expanded = commandTheoryChoice ?? state.B("showGuide", true);
            referenceTheory.Visibility = card.Exists ? Visibility.Visible : Visibility.Collapsed;
            commandTheoryRail.Visibility = card.Exists && expanded ? Visibility.Visible : Visibility.Collapsed;
            if (!card.Exists) { commandTheorySignature = null; return; }
            var signature = card.S("shell") + "|" + card.S("title") + "|" + card.S("why") + "|" + card.S("observe") + "|" + expanded;
            if (signature == commandTheorySignature) return;
            commandTheorySignature = signature;
            commandTheoryShell.Text = "ТЕОРИЯ   /   " + card.S("shell");
            commandTheoryTitle.Text = card.S("title");
            commandTheoryWhy.Text = card.S("why");
            commandTheoryObserve.Text = card.S("observe");
            commandTheoryToggleText.Text = expanded ? "Скрыть теорию" : "Теория";
            SetReferenceLessonAction(referenceTheory, commandTheoryToggleText.Text + " · " + DisplayLabGesture("F2"));
            commandTheoryScroll.ScrollToTop();
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
                "\n" + string.Join(" · ", state.Items("objectives").Select(value => value.Value())) +
                (state.D("guide").S("note").Length == 0 ? "" : "\n" + state.D("guide").S("note"));
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
    }
}
