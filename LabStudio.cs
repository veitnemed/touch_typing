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
            var root = PagePanel("Live", "Три минуты практики горячих клавиш в учебных приложениях");
            var catalog = Request("lab-catalog");
            var classes = catalog.Items("classes").ToArray();
            var layout = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 15) };
            labClassButtons.Clear();
            var shortcuts = Label("", 13, Dim);
            shortcuts.MaxWidth = 1000;
            var selected = Label("", 18, Ink, true);
            Action describe = delegate
            {
                var item = classes.FirstOrDefault(c => c.S("id") == labApp);
                selected.Text = item == null ? "Выберите класс" : item.S("title");
                shortcuts.Text = item == null ? "" : string.Join("   ·   ", item.Items("shortcuts").Select(s => DisplayGesture(s.S("keys"))).Where(s => s.Length > 0));
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
            labModeSelect = Select("Режим", new[] { "Сочетания · 3 минуты", "Задачи на рабочем столе · 3 минуты" }, labMode == "sandbox" ? 1 : 0, 315);
            AutomationProperties.SetAutomationId(labModeSelect, "LabMode");
            labLevelSelect = Select("Сложность", new[] { "Базовый", "Средний", "Сложный" }, labLevel, 150);
            choices.Children.Add(labModeSelect);
            choices.Children.Add(labLevelSelect);
            AutomationProperties.SetAutomationId(labLevelSelect, "LabLevel");
            beginControl = ActionButton("Начать · F5", StartLab, true);
            AutomationProperties.SetAutomationId(beginControl, "LabStart");
            beginControl.IsEnabled = classes.Length > 0;
            choices.Children.Add(beginControl);
            root.Children.Add(choices);
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
            var notes = Label("20 связанных действий в блоке · EN и RU · 3 минуты", 13, Dim);
            notes.Margin = new Thickness(0, 12, 0, 17);
            root.Children.Add(notes);
            root.Children.Add(ActionButton("Отдельное сочетание", delegate { legacyLive = true; ShowPage(LivePage); }));
            describe();
            return Scroll(root);
        }

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
            int seed;
            var initial = verifyMode && int.TryParse(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED"), out seed)
                ? Request("lab-start", "app", labApp, "mode", labMode, "level", labLevel, "seconds", 180, "seed", seed)
                : Request("lab-start", "app", labApp, "mode", labMode, "level", labLevel, "seconds", 180);
            if (!initial.Exists) return;
            labActive = active = true;
            paused = true;
            pressed.Clear();
            UpdateHeader();
            var root = new Grid { Margin = new Thickness(13, 8, 13, 7), Focusable = true };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel();
            labPause = ActionButton("Продолжить · F8", ToggleLabPause);
            labFinish = ActionButton("Результат · F10", FinishLab);
            AutomationProperties.SetAutomationId(labPause, "LabPause");
            AutomationProperties.SetAutomationId(labFinish, "LabFinish");
            var controls = Stack(true);
            controls.Children.Add(labPause);
            controls.Children.Add(labFinish);
            DockPanel.SetDock(controls, Dock.Right);
            header.Children.Add(controls);
            timerLabel = Label("03:00", 24, Ink, true);
            AutomationProperties.SetAutomationId(timerLabel, "LabClock");
            timerLabel.FontFamily = new FontFamily("Consolas");
            timerLabel.Margin = new Thickness(0, 0, 17, 0);
            DockPanel.SetDock(timerLabel, Dock.Left);
            header.Children.Add(timerLabel);
            labCounter = Label("", 12, Dim);
            AutomationProperties.SetAutomationId(labCounter, "LabCounter");
            labCounter.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(labCounter);
            root.Children.Add(header);
            var prompt = Stack();
            prompt.Margin = new Thickness(0, 7, 0, 7);
            labTask = Label("", 20, Ink, true);
            AutomationProperties.SetAutomationId(labTask, "LabTask");
            labTask.TextWrapping = TextWrapping.NoWrap;
            labTask.TextTrimming = TextTrimming.CharacterEllipsis;
            labKeys = Label("", 17, Accent);
            AutomationProperties.SetAutomationId(labKeys, "LabKeys");
            labKeys.Margin = new Thickness(0, 4, 0, 0);
            prompt.Children.Add(labTask);
            prompt.Children.Add(labKeys);
            Grid.SetRow(prompt, 1);
            root.Children.Add(prompt);
            labDesktop = new LabSceneView();
            AutomationProperties.SetAutomationId(labDesktop, "LabDesktop");
            Grid.SetRow(labDesktop, 2);
            root.Children.Add(labDesktop);
            var input = new DockPanel { Margin = new Thickness(0, 4, 0, 1) };
            var aids = Stack(true);
            labKeyAid = ActionButton("⌨", delegate { ToggleLabAid(true); });
            labHandAid = ActionButton("👐", delegate { ToggleLabAid(false); });
            labMistakeAid = ActionButton("✕", ToggleLabMistake);
            labKeyAid.ToolTip = "Показать или скрыть клавиатуру · F4";
            labHandAid.ToolTip = "Показать или скрыть руки · F7";
            labMistakeAid.ToolTip = "Подсвечивать ошибочную клавишу красным";
            AutomationProperties.SetAutomationId(labMistakeAid, "LabMistakeHighlight");
            aids.Children.Add(labKeyAid);
            aids.Children.Add(labHandAid);
            aids.Children.Add(labMistakeAid);
            DockPanel.SetDock(aids, Dock.Right);
            input.Children.Add(aids);
            feedback = labStatus = Label("", 12, Dim);
            AutomationProperties.SetAutomationId(feedback, "LabStatus");
            feedback.TextWrapping = TextWrapping.NoWrap;
            feedback.TextTrimming = TextTrimming.CharacterEllipsis;
            feedback.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
            input.Children.Add(labStatus);
            Grid.SetRow(input, 3);
            root.Children.Add(input);
            keyboard = new LiveKeyboard { IsHitTestVisible = false, Width = 1050 };
            labGuide = new Viewbox { Child = keyboard, Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly, MaxHeight = ActualHeight < 680 ? 128 : 147 };
            Grid.SetRow(labGuide, 4);
            root.Children.Add(labGuide);
            center.Content = root;
            root.Focus();
            RenderLab(initial);
            ResumeLab();
        }

        private void ToggleLabAid(bool keys)
        {
            var next = Request("settings", keys ? "keys" : "hands", !library.B(keys ? "showKeys" : "showHands", true));
            if (next.Exists) { library = next; RenderLab(state); }
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
            if (!labActive || !IsActive || (!paused && labCapture != null)) return;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                labStatus.Text = "Отпустите модификаторы, затем нажмите «Продолжить».";
                return;
            }
            RestorePausedLabView();
            StopLabCapture();
            try
            {
                labCapture = new LabKeyboardCapture(new WindowInteropHelper(this).Handle, Dispatcher, CaptureLabKey);
                var next = Request("lab-resume");
                if (!next.Exists) { StopLabCapture(); return; }
                paused = false;
                pressed.Clear();
                RenderLab(next);
                timer.Start();
            }
            catch (Win32Exception)
            {
                StopLabCapture();
                paused = true;
                labStatus.Text = "Захват клавиш недоступен. Нажмите «Продолжить», чтобы повторить.";
                labStatus.Foreground = Error;
            }
        }

        private void StopLabCapture()
        {
            if (labCapture != null) labCapture.Dispose();
            labCapture = null;
        }

        private void PauseLab()
        {
            StopLabCapture();
            if (!labActive || paused) return;
            paused = true;
            pressed.Clear();
            var next = Request("lab-pause");
            if (next.Exists) RenderLab(next);
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
        }

        private void ToggleLabPause()
        { if (paused) ResumeLab(); else PauseLab(); }

        private void CaptureLabKey(int vk, int scan, bool extended, bool down, string value)
        {
            if (!labActive || paused || !IsActive) return;
            string key = ScanKey(scan, extended, vk);
            if (key.Length == 0) return;
            bool repeat = value == null;
            if (down && repeat) return;
            if (down ? !pressed.Add(key) : !pressed.Remove(key)) return;
            if (down && !pressed.Any(IsModifier))
            {
                if (key == LabPauseKey()) { PauseLab(); return; }
                if (key == LabEndKey()) { FinishLab(); return; }
                if (key == "F4" && !state.D("task").S("keys").Split('+').Contains("F4")) { ToggleLabAid(true); return; }
                if (key == "F7" && !state.D("task").S("keys").Split('+').Contains("F7")) { ToggleLabAid(false); return; }
            }
            var next = Request("lab-key", "key", key, "pressed", down, "text", value ?? "");
            if (next.Exists) RenderLab(next);
        }

        private void RenderLab(Data next)
        {
            state = next;
            if (state.B("finished")) { FinishLab(); return; }
            paused = state.B("paused");
            int remaining = state.I("remaining", 180);
            timerLabel.Text = (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
            labCounter.Text = "Выполнено " + state.I("solved") + "    ·    Блок " + state.I("block", 1) + "    ·    Задание " + state.I("index", 1) + "/20";
            var task = state.D("task");
            labTask.Text = task.S("title", "Учебный рабочий стол");
            labTask.ToolTip = state.S("scenario");
            labKeys.Text = (task.S("keys").Length > 0 ? DisplayLabGesture(task.S("keys")) : task.S("text")) +
                (state.I("scenarioLength") > 0 ? "   ·   шаг " + state.I("scenarioStep") + "/" + state.I("scenarioLength") : "");
            labKeys.ToolTip = task.S("hint");
            AutomationProperties.SetHelpText(labKeys, task.S("keys"));
            AutomationProperties.SetHelpText(labTask, task.S("text"));
            labDesktop.Update(state.D("scene"), labApp);
            string status = state.S("status").Replace("LSuper", "⊞").Replace("RSuper", "⊞ прав.").Replace("Super", "⊞");
            string held = string.Join(" + ", state.Items("held").Select(key => LiveKeyboard.Title(key.Value())));
            labStatus.Text = paused ? "Пауза · пробел — продолжить" :
                (status.Length > 0 ? status : "Нажмите сочетание из задания") + (held.Length > 0 ? "   ·   Нажато " + held : "");
            labStatus.Foreground = state.S("feedback") == "error" ? Error : state.S("feedback") == "good" ? Ui.Brush("#A6D8B0") : Dim;
            labPause.Content = paused ? "Продолжить · пробел" : "Пауза · " + DisplayLabGesture(LabPauseKey());
            labFinish.Content = "Результат · " + DisplayLabGesture(LabEndKey());
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
            labGuide.Visibility = library.B("showKeys", true) || library.B("showHands", true)
                ? Visibility.Visible : Visibility.Collapsed;
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

        private void FinishLab()
        {
            if (!labActive) return;
            StopLabCapture();
            Request("lab-stop");
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

        private FrameworkElement BuildLabResult(Data result, FrameworkElement backdrop, bool preview = false)
        {
            int seconds = result.I("seconds");
            var series = result.Items("series").ToArray();
            var counts = series.Where(point => point.B("correct")).GroupBy(point => point.I("second"))
                .ToDictionary(group => group.Key, group => group.Count());
            var errorCounts = series.Where(point => !point.B("correct")).GroupBy(point => point.I("second"))
                .ToDictionary(group => group.Key, group => group.Count());
            int duration = Math.Max(1, seconds);
            var tempo = Enumerable.Range(0, duration + 1).Select(second =>
            {
                int start = Math.Max(0, second - 4);
                int count = Enumerable.Range(start, second - start + 1)
                    .Sum(index => counts.ContainsKey(index) ? counts[index] : 0);
                return (double?)(count * 60.0 / (second - start + 1));
            }).ToArray();
            var errors = Enumerable.Range(0, duration + 1)
                .Select(second => (double)(errorCounts.ContainsKey(second) ? errorCounts[second] : 0)).ToArray();
            string summary = labApp == "ubuntu" ? "Ubuntu" : labApp == "chrome" ? "Chrome" :
                labApp == "code" ? "VS Code" : "Терминал";
            string detail = "Ошибки " + result.I("errors") + "   ·   Рекорд " + result.D("record").Value("—") +
                (result.B("newRecord") ? "   ·   Новый рекорд" : "");
            return BuildStaminaResult(backdrop, preview ? "Пауза" : "Результат", summary, seconds, "ДЕЙСТВИЙ",
                result.I("solved").ToString(),
                Format(result.I("solved") * 60.0 / Math.Max(1, seconds)), "действий/м",
                Format(result.N("accuracy")) + "%", tempo, errors, detail, preview ? "LabPauseResult" : "LabResult",
                "Выполнено " + result.I("solved") + ". Ошибки " + result.I("errors") + ". Рекорд " +
                    result.I("record") + ". " + (result.B("completed") ?
                        "Полный трёхминутный подход." : "Занятие завершено раньше."),
                preview ? (Action)delegate { FinishLab(); center.Content = BuildLabHome(); StartLab(); } :
                    delegate { center.Content = BuildLabHome(); StartLab(); },
                preview ? (Action)FinishLab : delegate { legacyLive = false; ShowPage(LivePage); },
                preview ? (Action)ResumeLab : delegate { ShowPage(ProgressPage); },
                preview ? "Продолжить" : "Прогресс");
        }
    }
}
