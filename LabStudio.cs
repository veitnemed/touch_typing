using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private bool legacyLive, labActive;
        private ComboBox labLimitSelect, labMissionSelect;
        private int lastLabLimit;
        private string lastLabMission = "";
        private string[] labMissionIds = new string[0];
        private Button labHint;
        private Button labLimitContinue, labLimitFinish, labLimitArmedButton;
        private Key labLimitArmedKey;
        private TextBlock labLimitError;
        private FrameworkElement labLimitOverlay;
        private string labApp = "ubuntu", labMode = "sandbox";
        private int labLevel;
        private ComboBox labModeSelect, labLevelSelect;
        private LabSceneView labDesktop;
        private Viewbox labGuide;
        private FrameworkElement pausedLabView;
        private LabKeyboardCapture labCapture;
        private TextBlock labTask, labKeys, labCounter, labStatus;
        private Button labPause, labFinish, labKeyAid, labHandAid, labMistakeAid;
        private readonly Dictionary<string, Button> labClassButtons = new Dictionary<string, Button>();

        private FrameworkElement BuildLabHome()
        {
            var root = PagePanel("Live", "Практикум работы с клавиатурой");
            var catalog = Request("lab-catalog");
            var classes = catalog.Items("classes").ToArray();
            var layout = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 15) };
            labClassButtons.Clear();
            var shortcuts = Label("", 13, Dim);
            shortcuts.MaxWidth = 1000;
            var selected = Label("", 18, Ink, true);
            var levels = new[] { "Базовый", "Средний", "Сложный" };
            labMissionSelect = new ComboBox { MinWidth = 270, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left };
            Action describe = delegate
            {
                var item = classes.FirstOrDefault(c => c.S("id") == labApp);
                selected.Text = item == null ? "Выберите класс" : item.S("title");
                shortcuts.Text = item == null ? "" : item.S("description");
                var advice = catalog.Items("recommendations").FirstOrDefault(r => r.S("app") == labApp);
                if (advice != null && !string.IsNullOrWhiteSpace(advice.S("reason")))
                {
                    int suggested = advice.I("level", -1);
                    string title = suggested >= 0 && suggested < levels.Length ? "Рекомендуемый уровень: " + levels[suggested] + ". " : "";
                    shortcuts.Text += "\n" + title + advice.S("reason");
                }
                int level = labLevelSelect == null ? 0 : Math.Max(0, labLevelSelect.SelectedIndex);
                var ids = new List<string> { "" }; var titles = new List<string> { "Подобрать поручение" };
                string guidance = labModeSelect != null && labModeSelect.SelectedIndex == 0 ? "independent" : "guided";
                if (catalog.Items("drafts").Any(d => d.S("app") == labApp && d.I("level") == level && d.S("guidance") == guidance))
                { ids.Add("@resume"); titles.Add("Продолжить с сохранённого места"); }
                foreach (var m in catalog.Items("missions").Where(m => m.S("app") == labApp && m.I("level") == level))
                { ids.Add(m.S("id")); titles.Add(m.S("title")); }
                labMissionIds = ids.ToArray(); labMissionSelect.ItemsSource = titles; labMissionSelect.SelectedIndex = 0;
                foreach (var entry in labClassButtons)
                {
                    entry.Value.Background = entry.Key == labApp ? Ui.Brush("#404A5E") : Card;
                    entry.Value.BorderBrush = entry.Key == labApp ? Accent : Edge;
                }
            };
            foreach (var item in classes)
            {
                string app = item.S("id");
                var content = Stack();
                content.Children.Add(Label(item.S("title"), 21, Ink, true));
                var description = Label(item.S("description"), 13, Dim);
                description.Margin = new Thickness(0, 7, 0, 0);
                content.Children.Add(description);
                var choose = ActionButton("", delegate { labApp = app; describe(); });
                choose.Content = content;
                choose.HorizontalContentAlignment = HorizontalAlignment.Left;
                choose.Padding = new Thickness(20, 17, 20, 17);
                choose.Margin = new Thickness(0, 0, 12, 12);
                choose.MinHeight = 101;
                AutomationProperties.SetName(choose, item.S("title") + ". " + item.S("description"));
                AutomationProperties.SetAutomationId(choose, "LabClass-" + app);
                labClassButtons[app] = choose;
                layout.Children.Add(choose);
            }
            root.Children.Add(layout);
            root.Children.Add(selected);
            shortcuts.Margin = new Thickness(0, 6, 0, 16);
            root.Children.Add(shortcuts);
            var choices = Stack(true);
            labModeSelect = Select("Режим", new[] { "Самостоятельно", "По шагам" }, labMode == "sandbox" ? 1 : 0, 315);
            AutomationProperties.SetAutomationId(labModeSelect, "LabMode");
            labLevelSelect = Select("Сложность", levels, labLevel, 150);
            choices.Children.Add(labModeSelect);
            choices.Children.Add(labLevelSelect);
            labLimitSelect = Select("Длительность", new[] { "Без таймера", "20 действий", "40 действий", "60 действий", "3 минуты", "10 минут", "100 действий" }, lastLabLimit, 155);
            AutomationProperties.SetAutomationId(labLimitSelect, "LabDuration");
            choices.Children.Add(labLimitSelect);
            AutomationProperties.SetAutomationId(labLevelSelect, "LabLevel");
            beginControl = ActionButton("Начать · F5", StartLab, true);
            AutomationProperties.SetAutomationId(beginControl, "LabStart");
            beginControl.IsEnabled = classes.Length > 0;
            choices.Children.Add(beginControl);
            root.Children.Add(choices);
            root.Children.Add(labMissionSelect);
            AutomationProperties.SetAutomationId(labMissionSelect, "LabMission");
            root.Children.Add(Label("Действие — зачтённый шаг упражнения.", 12, Dim));
            labModeSelect.SelectionChanged += delegate { describe(); };
            labLevelSelect.SelectionChanged += delegate { describe(); };
            var hardware = new Expander { Header = "Клавиатура и Fn", Foreground = Dim,
                Margin = new Thickness(0, 12, 0, 3), IsExpanded = false };
            var hardwareControls = Stack(true);
            var keyboardProfileSelect = Select("Клавиатура", new[] { "MateBook D16", "Стандартная" },
                library.S("keyboardProfile", "matebook-d16") == "standard" ? 1 : 0, 185);
            var fnModeSelect = Select("Режим Fn", new[] { "Мультимедиа без Fn", "F1–F12 без Fn" },
                library.S("fnMode", "hotkeys") == "functions" ? 1 : 0, 188);
            keyboardProfileSelect.SelectionChanged += delegate
            {
                var updated = Request("settings", "keyboardProfile", keyboardProfileSelect.SelectedIndex == 1 ? "standard" : "matebook-d16");
                if (updated.Exists) library = updated;
            };
            fnModeSelect.SelectionChanged += delegate
            {
                var updated = Request("settings", "fnMode", fnModeSelect.SelectedIndex == 1 ? "functions" : "hotkeys");
                if (updated.Exists) library = updated;
            };
            hardwareControls.Children.Add(keyboardProfileSelect);
            hardwareControls.Children.Add(fnModeSelect);
            hardware.Content = hardwareControls;
            root.Children.Add(hardware);
            var notes = Label("Сочетания работают в EN и RU", 13, Dim);
            notes.Margin = new Thickness(0, 12, 0, 17);
            root.Children.Add(notes);
            root.Children.Add(ActionButton("Отдельное сочетание", delegate { legacyLive = true; ShowPage(LivePage); }));
            describe();
            return Scroll(root);
        }

        private bool labSavePending, labSaveClosePending, labSaveBackPending, labExplicitFinishPending;

        private void StartLab()
        {
            if (active) return;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                SetNotice("Отпустите модификаторы, затем нажмите «Начать» или F5.");
                return;
            }
            labMode = labModeSelect.SelectedIndex == 1 ? "sandbox" : "drill";
            labLevel = Math.Max(0, labLevelSelect.SelectedIndex);
            int limit = lastLabLimit = labLimitSelect.SelectedIndex;
            int actions = limit >= 1 && limit <= 3 ? 20 * limit : limit == 6 ? 100 : 0;
            int seconds = limit == 4 ? 180 : limit == 5 ? 600 : 0;
            // Opt-in native smoke test exercises the real expiry transition without waiting 3 minutes.
            int testSeconds;
            if (verifyMode && seconds > 0 && int.TryParse(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SOFT_FINISH_SECONDS"), out testSeconds))
                seconds = Math.Max(1, Math.Min(seconds, testSeconds));
            string guidance = labMode == "drill" ? "independent" : "guided";
            string mission = labMissionIds[Math.Max(0, labMissionSelect.SelectedIndex)];
            bool resume = mission == "@resume";
            if (resume) mission = "";
            int seed;
            var initial = verifyMode && int.TryParse(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED"), out seed)
                ? Request("lab-start", "app", labApp, "mode", labMode, "level", labLevel, "seconds", seconds, "actionLimit", actions, "guidance", guidance, "mission", mission, "continue", resume, "softFinish", true, "seed", seed)
                : Request("lab-start", "app", labApp, "mode", labMode, "level", labLevel, "seconds", seconds, "actionLimit", actions, "guidance", guidance, "mission", mission, "continue", resume, "softFinish", true);
            if (!initial.Exists || initial.B("startBlocked")) return;
            labSavePending = labSaveClosePending = labSaveBackPending = labExplicitFinishPending = false;
            lastLabMission = initial.S("mission");
            labActive = active = true;
            paused = true;
            pressed.Clear();
            UpdateHeader();
            center.Content = MakeReferenceLesson();
            ((FrameworkElement)center.Content).Focus();
            RenderLab(initial);
            ResumeLab();
        }

        private void ToggleLabAid(bool keys)
        {
            var next = Request("settings", keys ? "keys" : "hands", !library.B(keys ? "showKeys" : "showHands", true));
            if (next.Exists) { library = next; referenceGuideExplicit = next.B("showKeys", true) || next.B("showHands", true); RenderLab(state); }
        }

        private void ToggleLabMistake()
        {
            var next = Request("settings", "mistakeKeys", !library.B("showMistakeKey", true));
            if (next.Exists) { library = next; RenderLab(state); }
        }

        private string LabPauseKey()
        { return state.D("task").S("keys").Split('+').Contains("F8") ? "F6" : "F8"; }
        private string LabEndKey()
        { return state.D("task").S("keys").Split('+').Contains("F10") ? "F9" : "F10"; }

        private void ResumeLab()
        {
            if (labSavePending || state.B("limitDecisionPending")) return;
            if (!labActive || !IsActive || (!paused && labCapture != null)) return;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                labStatus.Text = "Отпустите модификаторы, затем нажмите «Продолжить».";
                return;
            }
            try
            {
                if (labCapture == null)
                {
                    labCapture = new LabKeyboardCapture(new WindowInteropHelper(this).Handle, Dispatcher, CaptureLabKey);
                    labCapture.Suspend();
                }
                var next = Request("lab-resume");
                if (!next.Exists)
                {
                    if (labLimitOverlay != null) labLimitError.Text = notice.Text;
                    return;
                }
                if (next.B("paused") || next.B("finished"))
                {
                    labCapture.Suspend();
                    RenderLab(next);
                    return;
                }
                RestorePausedLabView();
                labCapture.Resume();
                paused = false;
                pressed.Clear();
                RenderLab(next);
                if (labActive && !paused) timer.Start();
            }
            catch (Win32Exception)
            {
                StopLabCapture();
                paused = true;
                const string problem = "Захват клавиш недоступен. Нажмите «Продолжить», чтобы повторить.";
                if (labLimitOverlay != null) labLimitError.Text = problem;
                else
                {
                    labStatus.Text = problem;
                    labStatus.Foreground = Error;
                }
            }
        }

        private void StopLabCapture()
        {
            if (labCapture != null) labCapture.Dispose();
            labCapture = null;
        }

        private void PauseLab()
        {
            if (labCapture != null) labCapture.Suspend();
            if (labSavePending) return;
            if (!labActive || paused) return;
            paused = true;
            pressed.Clear();
            var next = Request("lab-pause");
            if (next.Exists) RenderLab(next);
            if (!labActive || labSavePending || state.B("limitDecisionPending")) return;
            if (state.B("finishingMission")) { ShowLabLimitOverlay(true); return; }
            var result = Request("lab-result");
            if (!result.Exists) return;
            pausedLabView = center.Content as FrameworkElement;
            center.Content = null;
            center.Content = BuildLabResult(result, pausedLabView, true);
        }

        private void RestorePausedLabView()
        {
            if (pausedLabView == null) return;
            var overlay = center.Content as Grid;
            if (overlay != null) overlay.Children.Remove(pausedLabView);
            center.Content = null;
            pausedLabView.IsHitTestVisible = true;
            center.Content = pausedLabView;
            pausedLabView = null;
            labLimitOverlay = null;
            labLimitArmedButton = null;
            ((FrameworkElement)center.Content).Focus();
        }

        private void ToggleLabPause()
        { if (paused) ResumeLab(); else PauseLab(); }

        private static bool LabEditingRepeat(string key)
        {
            switch (key)
            {
                case "Left": case "Right": case "Up": case "Down":
                case "Home": case "End": case "PageUp": case "PageDown":
                case "Backspace": case "Delete": return true;
                default: return false;
            }
        }

        private void CaptureLabKey(int vk, int scan, bool extended, bool down, bool nativeRepeat, string value)
        {
            if (!labActive || paused || !IsActive) return;
            string key = ScanKey(scan, extended, vk);
            if (key.Length == 0) return;
            bool repeat = down && (nativeRepeat || pressed.Contains(key));
            if (repeat)
            {
                if (!pressed.Contains(key) || !LabEditingRepeat(key)) return;
            }
            else if (down ? !pressed.Add(key) : !pressed.Remove(key)) return;
            if (down && !repeat && !pressed.Any(IsModifier))
            {
                if (key == "F1" && !state.D("task").S("keys").Split('+').Contains("F1"))
                { OpenReferenceLessonHelp(); return; }
                if (key == "Esc" && referenceHelpOpen) { CloseReferenceLessonHelp(); return; }
                if (key == LabPauseKey()) { PauseLab(); return; }
                if (key == LabEndKey()) { FinishLab(); return; }
                if (key == "F4" && !state.D("task").S("keys").Split('+').Contains("F4")) { ToggleLabAid(true); return; }
                if (key == "F7" && !state.D("task").S("keys").Split('+').Contains("F7")) { ToggleLabAid(false); return; }
            }
            var next = Request("lab-key", "key", key, "pressed", down, "text", key == "Print" && down ? labDesktop.CapturePng() : value ?? "");
            if (next.Exists) RenderLab(next);
        }

        private void RenderLab(Data next)
        {
            state = next;
            if (state.B("finished")) { FinishLab(); return; }
            paused = state.B("paused");
            if (state.B("limitDecisionPending"))
            {
                if (labCapture != null) labCapture.Suspend();
                timer.Stop();
                pressed.Clear();
            }
            int remaining = state.I("remaining", 180);
            timerLabel.Visibility = state.I("timeLimit") > 0 || state.I("actionLimit") > 0 ? Visibility.Visible : Visibility.Collapsed;
            timerLabel.Text = state.B("finishingMission") ? "Дорабатываем миссию" :
                state.I("actionLimit") > 0 ? state.I("solved") + "/" + state.I("actionLimit") : (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
            string labContext = state.S("scenario") + "    ·    " + state.S("phase");
            labCounter.ToolTip = labContext;
            AutomationProperties.SetName(labCounter, labContext + " · Шаг " + state.I("scenarioStep") + " из " + state.I("scenarioLength"));
            AutomationProperties.SetHelpText(labCounter, "Выполнено " + state.I("solved"));
            var task = state.D("task");
            labTask.Text = state.B("showGuide", true) ? task.S("title") : state.S("phase");
            labTask.ToolTip = state.S("scenario") + "\n" + string.Join("\n", state.Items("objectives").Select(item => item.Value()));
            labKeys.ToolTip = state.B("showGuide", true) ? task.S("hint") : "";
            AutomationProperties.SetHelpText(labKeys, task.S("keys"));
            AutomationProperties.SetHelpText(labTask, task.S("text"));
            // A WPF TextBlock with empty HelpText falls back to its tooltip.
            // Keep QA input metadata separate from visible/accessibility hints.
            if (verifyMode)
            {
                string keys = task.S("keys"), text = task.S("text");
                AutomationProperties.SetItemStatus(labTask,
                    keys.Length > 0 ? "keys:" + keys : text.Length > 0 ? "text:" + text : "none:");
            }
            labDesktop.Update(state.D("scene"), labApp);
            string status = state.S("status").Replace("LSuper", "⊞").Replace("RSuper", "⊞ прав.").Replace("Super", "⊞");
            string held = string.Join(" + ", state.Items("held").Select(key => LiveKeyboard.Title(key.Value())));
            labStatus.Text = paused ? "Пауза · пробел — продолжить" :
                (status.Length > 0 ? status : "Нажмите сочетание из задания") + (held.Length > 0 ? "   ·   Нажато " + held : "");
            labStatus.Foreground = state.S("feedback") == "error" ? Error : state.S("feedback") == "good" ? Ui.Brush("#A6D8B0") : Dim;
            UpdateReferenceLesson(task);
            SetReferenceLessonAction(labPause, paused ? "Продолжить · пробел" : "Пауза · " + DisplayLabGesture(LabPauseKey()));
            SetReferenceLessonAction(labFinish, "Результат · " + DisplayLabGesture(LabEndKey()));
            labKeyAid.ToolTip = "Показать или скрыть клавиатуру · " + DisplayLabGesture("F4");
            labHandAid.ToolTip = "Показать или скрыть руки · " + DisplayLabGesture("F7");
            var guide = state.D("guide").Items("steps").FirstOrDefault();
            keyboard.Update(library.Items("keyboard"), state.D("scene").S("layout", CurrentLayout()),
                guide == null ? Enumerable.Empty<Data>() : guide.Items(), state.Items("held").Select(key => key.Value()),
                library.B("showMistakeKey", true) && state.S("feedback") == "error" ? state.S("lastKey") : "",
                library.B("showKeys", true), library.B("showHands", true), true,
                library.S("keyboardProfile", "matebook-d16"), library.S("fnMode", "hotkeys"));
            labKeyAid.Opacity = library.B("showKeys", true) ? 1 : .5;
            labHandAid.Opacity = library.B("showHands", true) ? 1 : .5;
            labMistakeAid.Opacity = library.B("showMistakeKey", true) ? 1 : .5;
            UpdateReferenceGuideVisibility();
            if (state.B("limitDecisionPending")) ShowLabLimitOverlay(false);
        }

        private void ShowLabLimitOverlay(bool extensionPause)
        {
            if (labLimitOverlay != null && ReferenceEquals(center.Content, labLimitOverlay))
            {
                labLimitError.Text = state.S("saveError");
                return;
            }
            RestorePausedLabView();
            pausedLabView = center.Content as FrameworkElement;
            center.Content = null;
            var overlay = new Grid { Background = Night };
            if (pausedLabView != null)
            {
                pausedLabView.IsHitTestVisible = false;
                overlay.Children.Add(pausedLabView);
                overlay.Children.Add(new Border { Background = Ui.Brush("#BF080B12") });
            }
            var card = Stack();
            var title = Label(extensionPause ? "Миссия на паузе" :
                (state.I("timeLimit") > 0 && state.I("remaining") == 0 ? "Время вышло" : "Лимит действий достигнут"), 19, Ink, true);
            title.Focusable = true;
            AutomationProperties.SetAutomationId(title, extensionPause ? "LabExtensionPause" : "LabLimitChoice");
            card.Children.Add(title);
            var detail = Label(extensionPause ? "Текущий шаг сохранён. Продолжите с того же места или завершите занятие." :
                "Можно завершить занятие сейчас или доделать текущую миссию. Введённый текст останется на месте.", 13, Dim);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 8, 0, 12);
            card.Children.Add(detail);
            labLimitError = Label(state.S("saveError"), 13, Error);
            labLimitError.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(labLimitError);
            var actions = new WrapPanel();
            labLimitFinish = ActionButton("Завершить", FinishLab);
            AutomationProperties.SetAutomationId(labLimitFinish, "LabLimitFinish");
            labLimitContinue = ActionButton(extensionPause ? "Продолжить" : "Доработать миссию", delegate
            {
                if (Keyboard.Modifiers != ModifierKeys.None) return;
                if (!extensionPause)
                {
                    var reply = Request("lab-limit-choice", "choice", "continue");
                    if (!reply.Exists)
                    {
                        labLimitError.Text = notice.Text;
                        return;
                    }
                    RenderLab(reply);
                    if (reply.B("limitDecisionPending")) return;
                }
                ResumeLab();
            }, true);
            AutomationProperties.SetAutomationId(labLimitContinue, "LabLimitContinue");
            actions.Children.Add(labLimitFinish);
            actions.Children.Add(labLimitContinue);
            card.Children.Add(actions);
            overlay.Children.Add(new Border { Child = card, Padding = new Thickness(22), Width = 470,
                Background = Night, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            labLimitOverlay = overlay;
            center.Content = overlay;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            { if (ReferenceEquals(center.Content, overlay)) title.Focus(); }));
        }

        private string DisplayLabGesture(string raw)
        {
            string shown = DisplayGesture(raw);
            if (library.S("keyboardProfile") != "matebook-d16") return shown;
            if (library.S("fnMode", "hotkeys") == "hotkeys")
                shown = Regex.Replace(shown, @"(?<!\w)F(?:1[0-2]|[1-9])(?!\d)", match => "Fn + " + match.Value);
            if (raw.Split('+').Contains("Print"))
                shown = shown.Replace("PrtSc", library.S("fnMode", "hotkeys") == "hotkeys" ?
                    "F11 / PrtSc" : "Fn + F11 / PrtSc");
            return shown;
        }

        private bool TrySaveLab()
        {
            StopLabCapture();
            timer.Stop();
            paused = true;
            pressed.Clear();
            var saved = Request("lab-stop");
            if (saved.Exists && saved.B("saved") && string.IsNullOrWhiteSpace(engine.Notice))
            {
                labSavePending = false;
                return true;
            }
            labSavePending = true;
            string problem = string.IsNullOrWhiteSpace(notice.Text) ?
                "Не удалось сохранить занятие. Проверьте, доступна ли папка профиля, и повторите." :
                notice.Text;
            ShowLabSaveRetry(problem);
            return false;
        }

        private void ShowLabSaveRetry(string problem)
        {
            RestorePausedLabView();
            pausedLabView = center.Content as FrameworkElement;
            center.Content = null;
            var overlay = new Grid { Background = Ui.Brush("#1D2430") };
            if (pausedLabView != null)
            {
                pausedLabView.IsHitTestVisible = false;
                overlay.Children.Add(pausedLabView);
                overlay.Children.Add(new Border { Background = Ui.Brush("#BF080B12") });
            }
            var card = Stack();
            var heading = Label("Занятие пока не сохранено", 18, Ink, true);
            AutomationProperties.SetAutomationId(heading, "LabSaveError");
            card.Children.Add(heading);
            var detail = Label(problem, 13, Ink);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 8, 0, 12);
            card.Children.Add(detail);
            var retry = ActionButton(labSaveClosePending ? "Повторить сохранение и закрыть" :
                "Повторить сохранение", FinishLab, true);
            AutomationProperties.SetAutomationId(retry, "LabRetrySave");
            card.Children.Add(retry);
            overlay.Children.Add(new Border { Child = card, Padding = new Thickness(24), Width = 470,
                Background = Night, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center });
            center.Content = overlay;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            { if (ReferenceEquals(center.Content, overlay)) retry.Focus(); }));
        }

        private void FinishLab()
        {
            if (!labActive) return;
            if (labSaveBackPending && !labSaveClosePending) { BackFromLab(); return; }
            if (!labSaveClosePending && (!state.B("finished") || labExplicitFinishPending))
            {
                // Let the server freeze its clock and decide whether the cap was
                // reached. The previous UI snapshot can be stale by one tick.
                StopLabCapture();
                timer.Stop();
                paused = true;
                pressed.Clear();
                labExplicitFinishPending = true;
                var next = Request("lab-limit-choice", "choice", "finish");
                if (next.Exists) state = next;
                if (!next.Exists || !next.B("finished") || !next.B("saved") || !string.IsNullOrWhiteSpace(engine.Notice))
                {
                    labSavePending = true;
                    ShowLabSaveRetry(string.IsNullOrWhiteSpace(notice.Text) ?
                        "Не удалось сохранить результат. Повторите сохранение." : notice.Text);
                    return;
                }
                labExplicitFinishPending = labSavePending = false;
            }
            else if (!TrySaveLab()) return;
            if (labSaveClosePending)
            {
                labSaveClosePending = false;
                closeAccepted = true;
                Close();
                return;
            }
            var result = Request("lab-result");
            labActive = active = paused = false;
            pressed.Clear();
            timer.Stop();
            UpdateHeader();
            beginControl = null;
            RestorePausedLabView();
            var backdrop = center.Content as FrameworkElement;
            center.Content = null;
            center.Content = result.Exists ? BuildLabResult(result, backdrop) : BuildLabHome();
        }

        private void BackFromLab()
        {
            if (!state.B("limitDecisionPending") && !state.B("finishingMission")) { FinishLab(); return; }
            labSaveBackPending = true;
            if (!TrySaveLab()) return;
            labSaveBackPending = false;
            labActive = active = paused = false;
            RestorePausedLabView();
            ShowPage(LivePage);
        }

        private void RestartLab()
        {
            if (labActive) FinishLab();
            if (labActive) return; // Retain the paused lesson until its save succeeds.
            center.Content = BuildLabHome();
            int choice = Array.IndexOf(labMissionIds, lastLabMission);
            if (choice >= 0) labMissionSelect.SelectedIndex = choice;
            StartLab();
        }

        private FrameworkElement BuildLabResult(Data result, FrameworkElement backdrop, bool preview = false)
        {
            return MakeReferenceLabResult(result, backdrop, preview);
        }
    }
}
