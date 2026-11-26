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
        private static readonly int[] LabActionLimits = { 0, 20, 40, 60, 100 };
        private static readonly int[] LabTimeLimits = { 0, 180, 600 };
        private int lastLabLimit, lastLabTimer;
        private string lastLabMission = "";
        private string lastLabRouteKind = "";
        private int lastLabRuntimeActions, lastLabRuntimeSeconds;
        private bool lastLabAdaptiveGuide;
        private Button labHint;
        private Button labLimitContinue, labLimitFinish, labLimitArmedButton;
        private Key labLimitArmedKey;
        private TextBlock labLimitError;
        private FrameworkElement labLimitOverlay;
        private string labApp = "ubuntu", labMode = "sandbox";
        private int labLevel;
        private string labDesktopProfile = "ubuntu";
        private Button labDesktopUbuntuChoice, labDesktopWindowsChoice;

        private string LessonDesktopProfile
        { get { return labApp == "windows" || labApp == "workday" || labApp == "terminal" ? labDesktopProfile : "ubuntu"; } }

        private void HydrateDesktopProfile(Data settings)
        {
            if (!settings.Exists || !string.IsNullOrWhiteSpace(engine.Notice)) return;
            string id = settings.S("desktopProfile");
            if ((id != "ubuntu" && id != "windows") || id == labDesktopProfile) return;
            if (labApp == "ubuntu" || labApp == "windows") labApp = id;
            labDesktopProfile = id;
            lastLabMission = "";
            lastLabRouteKind = "";
        }

        private FrameworkElement BuildDesktopProfileChoice(Action redraw)
        {
            var group = Stack();
            group.Margin = new Thickness(0, 0, 0, 14);
            var title = ReferenceLabel("Система для тренировки", 17, ReferenceText, true);
            AutomationProperties.SetAutomationId(title, "LabDesktopProfileHeading");
            group.Children.Add(title);
            var row = new WrapPanel { Margin = new Thickness(0, 5, 0, 1) };
            labDesktopUbuntuChoice = DesktopProfileButton("Ubuntu", "ubuntu", redraw);
            labDesktopWindowsChoice = DesktopProfileButton("Windows 10", "windows", redraw);
            row.Children.Add(labDesktopUbuntuChoice);
            row.Children.Add(labDesktopWindowsChoice);
            group.Children.Add(row);
            const string detail = "Выбор системы меняет сочетания и задания, но не настройки компьютера.";
            var hint = ReferenceLabel(detail, 13, ReferenceMuted, false);
            group.ToolTip = detail;
            AutomationProperties.SetHelpText(title, detail);
            hint.TextWrapping = TextWrapping.Wrap;
            group.Children.Add(hint);
            return group;
        }

        private Button DesktopProfileButton(string label, string id, Action redraw)
        {
            bool selected = labDesktopProfile == id;
            Button button = null;
            button = ReferenceButton(delegate
            {
                if (labDesktopProfile == id) return;
                var updated = Request("settings", "desktopProfile", id);
                if (!updated.Exists || !string.IsNullOrWhiteSpace(engine.Notice) || updated.S("desktopProfile") != id)
                {
                    if (string.IsNullOrWhiteSpace(notice.Text))
                        SetNotice("Не удалось сохранить выбор системы. Повторите действие.");
                    button.Focus();
                    return;
                }
                library = updated;
                HydrateDesktopProfile(updated);
                redraw();
                var next = id == "windows" ? labDesktopWindowsChoice : labDesktopUbuntuChoice;
                if (next != null) next.Focus();
            }, 8);
            button.Content = label;
            button.Foreground = ReferenceText;
            button.Background = selected ? ReferencePrimary : ReferenceField;
            button.BorderBrush = selected ? ReferenceAccent : ReferenceEdge;
            button.BorderThickness = new Thickness(1);
            button.Padding = new Thickness(12, 6, 12, 6);
            button.Margin = new Thickness(0, 0, 10, 5);
            button.MinWidth = 130;
            AutomationProperties.SetAutomationId(button, "LabDesktopProfile-" + id);
            AutomationProperties.SetName(button, label + (selected ? ", выбрано" : ", выбрать для практики"));
            button.ToolTip = "Тренировать сочетания и рабочий стол " + label + " в симуляции";
            return button;
        }

        private static void ConfigureDesktopDensity(FrameworkElement element, bool compact)
        {
            var group = (StackPanel)element;
            var title = (TextBlock)group.Children[0];
            var row = (WrapPanel)group.Children[1];
            title.FontSize = compact ? 13 : 17;
            title.LineHeight = title.FontSize * 1.26;
            row.Margin = new Thickness(0, compact ? 3 : 5, 0, compact ? 0 : 1);
            foreach (Button button in row.Children)
            {
                button.MinWidth = compact ? 105 : 130;
                button.Margin = new Thickness(0, 0, 8, compact ? 0 : 5);
                button.Padding = new Thickness(12, compact ? 4 : 6, 12, compact ? 4 : 6);
            }
            group.Children[2].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool UsesSelectedDesktop(Data item)
        {
            string target = item.S("desktopProfile");
            return target.Length == 0 || target == labDesktopProfile;
        }
        private LabSceneView labDesktop;
        private Border labGuide;
        private FrameworkElement pausedLabView;
        private LabKeyboardCapture labCapture;
        private readonly string labQaNavigationTrace = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_NAV_TRACE");
        private int labNativeLayoutGeneration;
        private string labNativeLayout;
        private TextBlock labTask, labKeys, labStatus;
        private Button labPause, labFinish, labKeyAid, labHandAid, labMistakeAid;

        private readonly Dictionary<string, Button> liveCatalogButtons = new Dictionary<string, Button>();
        private string liveReturnApp;

        private Data PreferredLiveDraft(Data catalog, string app)
        {
            string environment = app == "windows" || app == "workday" || app == "terminal" ? labDesktopProfile : "ubuntu";
            var drafts = catalog.Items("drafts").Where(draft => draft.S("app") == app &&
                draft.S("desktopProfile", "ubuntu") == environment);
            // The profile keeps checkpoint insertion order. Do not hide a
            // saved independent/advanced practice just because the app restarted.
            return drafts.LastOrDefault();
        }

        private bool HasCompatibleLiveDraft(Data catalog, string app)
        {
            return PreferredLiveDraft(catalog, app) != null;
        }

        private void LaunchLiveCard(string app, bool resume)
        {
            if (active || !EnsureMenuEngine()) return;
            var catalog = Request("lab-catalog", "desktopProfile", labDesktopProfile);
            if (!catalog.Exists) return;
            labApp = liveReturnApp = app;
            var draft = PreferredLiveDraft(catalog, app);
            if (draft != null)
            {
                labLevel = draft.I("level");
                labMode = draft.S("guidance") == "independent" ? "drill" : "sandbox";
                StartLabAttempt("", true);
                return;
            }
            if (resume) { SetNotice("Сохранённое занятие больше недоступно."); return; }
            labLevel = 0;
            labMode = "sandbox";
            StartLabAttempt("", false, "course", true, adaptiveGuide: true);
        }

        private void ReturnToLiveCatalog()
        {
            legacyLive = false;
            ShowPage(LivePage);
            Button card;
            if (liveReturnApp != null && liveCatalogButtons.TryGetValue(liveReturnApp, out card))
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { card.BringIntoView(); card.Focus(); }));
        }

        private bool labSavePending, labSaveClosePending, labSaveBackPending, labExplicitFinishPending;

        private void StartLabAttempt(string mission, bool resume, string routeKind = "",
            bool defaultBudget = false, int? actionOverride = null, int? timeOverride = null, bool adaptiveGuide = false)
        {
            if (active || !EnsureMenuEngine()) return;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                SetNotice("Отпустите модификаторы, затем запустите упражнение.");
                return;
            }
            page = LivePage;
            int actions = actionOverride ?? LabActionLimits[lastLabLimit];
            int seconds = timeOverride ?? LabTimeLimits[lastLabTimer];
            int testSeconds;
            if (verifyMode && seconds > 0 && int.TryParse(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SOFT_FINISH_SECONDS"), out testSeconds))
                seconds = Math.Max(1, Math.Min(seconds, testSeconds));
            string guidance = labMode == "drill" ? "independent" : "guided";
            var fields = new List<object> {
                "desktopProfile", labDesktopProfile, "app", labApp, "mode", labMode, "level", labLevel,
                "guidance", guidance, "mission", mission, "continue", resume, "preserveDraft", !resume,
                "softFinish", true, "combinedLimits", true, "nativeEnglishInput", true
            };
            if (routeKind.Length > 0) fields.AddRange(new object[] { "routeKind", routeKind });
            if (adaptiveGuide) fields.AddRange(new object[] { "adaptiveGuide", true });
            if (!defaultBudget) fields.AddRange(new object[] { "seconds", seconds, "actionLimit", actions });
            int seed;
            if (verifyMode && int.TryParse(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED"), out seed))
                fields.AddRange(new object[] { "seed", seed });
            var initial = Request("lab-start", fields.ToArray());
            if (!initial.Exists || initial.B("startBlocked"))
            {
                if (string.IsNullOrWhiteSpace(notice.Text))
                    SetNotice(initial.S("saveError", "Не удалось открыть занятие. Повторите запуск."));
                return;
            }
            labSavePending = labSaveClosePending = labSaveBackPending = labExplicitFinishPending = false;
            lastLabMission = initial.S("mission");
            lastLabRouteKind = initial.S("routeKind");
            lastLabRuntimeActions = initial.I("actionLimit");
            lastLabRuntimeSeconds = initial.I("timeLimit");
            lastLabAdaptiveGuide = initial.B("adaptiveGuide");
            labActive = active = true;
            paused = true;
            pressed.Clear();
            UpdateHeader();
            center.Content = MakeReferenceLesson();
            ((FrameworkElement)center.Content).Focus();
            labNativeLayoutGeneration++;
            labNativeLayout = KnownNativeLessonLayout(KeyboardLayout(0));
            // A restarted process can use a different Windows layout than
            // the draft. Preserve a deliberate virtual layout in that case.
            initial = SyncLabNativeLayout(labNativeLayout, false) ?? initial;
            RenderLab(initial);
            ResumeLab();
        }

        private void ToggleLabAid(bool keys)
        {
            var next = Request("settings", keys ? "liveKeys" : "liveHands", !library.B(keys ? "liveShowKeys" : "liveShowHands"));
            if (next.Exists && string.IsNullOrWhiteSpace(engine.Notice))
            {
                library = next;
                RenderLab(state);
            }
        }

        private void ToggleLabMistake()
        {
            var next = Request("settings", "mistakeKeys", !library.B("showMistakeKey", true));
            if (next.Exists) { library = next; RenderLab(state); }
        }

        private string LabPauseKey()
        { return state.D("scene").B("diagnosticWorkspace") || state.D("task").S("keys").Split('+').Contains("F8") ? "F6" : "F8"; }
        private string LabEndKey()
        { return state.D("task").S("keys").Split('+').Contains("F10") ? "F9" : "F10"; }

        private static string KnownNativeLessonLayout(IntPtr hkl)
        {
            switch (hkl.ToInt64() & 0x3ff)
            {
                case 0x09: return "EN";
                case 0x19: return "RU";
                default: return null;
            }
        }

        private Data SyncLabNativeLayout(string layout, bool takeOver)
        {
            if (layout == null) return null;
            var next = Request("lab-native-layout", "layout", layout, "takeOver", takeOver);
            return next.Exists ? next : null;
        }

        private void QueueLabNativeLayout(IntPtr hkl)
        {
            var layout = KnownNativeLessonLayout(hkl);
            if (layout == null) return;
            int generation = labNativeLayoutGeneration;
            // Keep delivery ordered with low-level scan-code callbacks, while
            // leaving WM_INPUTLANGCHANGE itself free of engine calls.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (!labActive || labNativeLayoutGeneration != generation) return;
                ObserveLabNativeLayout(layout);
            }));
        }

        private void ObserveLabNativeLayout(string layout)
        {
            if (layout == null || layout == labNativeLayout) return;
            var next = SyncLabNativeLayout(layout, true);
            if (next == null) return;
            labNativeLayout = layout;
            if (!labSavePending && !next.B("finished")) RenderLab(next);
        }

        private void ResumeLab()
        {
            if (labSavePending || state.B("limitDecisionPending")) return;
            if (!labActive || !IsActive || (!paused && labCapture != null)) return;
            ObserveLabNativeLayout(KnownNativeLessonLayout(KeyboardLayout(0)));
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

        private void ApplyLabNotebookCommand(string command, int cell, int offset)
        {
            if (!labActive || paused || !IsActive || referenceHelpOpen || state.B("limitDecisionPending")) return;
            var next = Request("lab-notebook-action", "action", command, "cell", cell, "offset", offset);
            if (next.Exists) RenderLab(next);
        }

        private void ApplyLabExplorerCommand(string command, string path)
        {
            if (!labActive || paused || !IsActive || referenceHelpOpen || state.B("limitDecisionPending")) return;
            var next = Request("lab-explorer-action", "action", command, "path", path);
            if (next.Exists) RenderLab(next);
        }

        private void ApplyLabChromeSurfaceCommand(string action, string owner, int slot)
        {
            if (!labActive || paused || !IsActive || referenceHelpOpen || state.B("limitDecisionPending")) return;
            var next = Request("lab-browser-surface-action", "action", action, "owner", owner, "slot", slot);
            if (next.Exists) RenderLab(next);
        }

        private void ApplyLabBrowserHistoryCommand(string action, string path)
        {
            if (!labActive || paused || !IsActive || referenceHelpOpen || state.B("limitDecisionPending")) return;
            var next = Request("lab-browser-history-focus", "action", action, "path", path);
            if (next.Exists) RenderLab(next);
        }

        private void ApplyLabBrowserTabCommand(string action, string index)
        {
            if (!labActive || paused || !IsActive || referenceHelpOpen || state.B("limitDecisionPending")) return;
            var next = Request("lab-browser-tab-search", "action", action, "index", index);
            if (next.Exists) RenderLab(next);
        }

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

        private void CaptureLabKey(int vk, int scan, bool extended, bool down, bool nativeRepeat, string value, IntPtr inputLayout)
        {
            if (!labActive || paused || !IsActive) return;
            string key = ScanKey(scan, extended, vk);
            // Only targeted QA runs expose these physical navigation keys, never typed text.
            if (verifyMode && down &&
                (labQaNavigationTrace == "workspace" && (vk == 0x21 || vk == 0x22) ||
                 labQaNavigationTrace == "terminal" && (vk == 0x24 || vk == 0x27) ||
                 labQaNavigationTrace == "workday-keypad" && scan == 0x47))
            {
                var cachedModifiers = new[] { "LSuper", "RSuper", "LShift", "RShift", "LCtrl", "RCtrl", "LAlt", "RAlt" };
                var label = "VK=" + vk.ToString("X2") + " scan=" + scan.ToString("X2") +
                    " extended=" + extended + " key=" + key +
                    " held=" + string.Join("+", cachedModifiers.Where(pressed.Contains));
                AutomationProperties.SetItemStatus(labStatus, label);
            }
            if (key.Length == 0) return;
            bool repeat = down && (nativeRepeat || pressed.Contains(key));
            if (repeat)
            {
                if (!pressed.Contains(key) || !LabEditingRepeat(key)) return;
            }
            else if (down ? !pressed.Add(key) : !pressed.Remove(key)) return;
            if (down && !repeat && !pressed.Any(IsModifier))
            {
                if (key == "F2" && state.D("commandTheory").Exists && !state.D("task").S("keys").Split('+').Contains("F2"))
                { ToggleReferenceTerminalTheory(); return; }
                if (key == "F1" && !state.D("task").S("keys").Split('+').Contains("F1"))
                { OpenReferenceLessonHelp(); return; }
                if (key == "Esc" && referenceHelpOpen) { CloseReferenceLessonHelp(); return; }
                if (key == LabPauseKey()) { PauseLab(); return; }
                if (key == LabEndKey()) { FinishLab(); return; }
                if (key == "F4" && !state.D("task").S("keys").Split('+').Contains("F4")) { ToggleLabAid(true); return; }
                if (key == "F7" && !state.D("task").S("keys").Split('+').Contains("F7")) { ToggleLabAid(false); return; }
            }
            if (down && !pressed.Any(k => IsModifier(k) && k != "LShift" && k != "RShift") &&
                labDesktop.NotebookViewportKey(key, pressed.Contains("LShift") || pressed.Contains("RShift"))) return;
            var next = Request("lab-key", "key", key, "pressed", down, "nativeLayout", KnownNativeLessonLayout(inputLayout) ?? "",
                "text", key == "Print" && down
                ? labDesktop.CapturePng(state.D("scene").B("debugDay") && (pressed.Contains("LAlt") || pressed.Contains("RAlt"))) : value ?? "");
            if (next.Exists) RenderLab(next);
        }

        private void RenderLab(Data next)
        {
            // A displayed hint belongs to that step. Do not let an open
            // help surface expose the answer to a later recall exercise.
            bool completedPrevious = referenceHelpOpen && state != null && state.Exists && next.I("solved") > state.I("solved");
            state = next;
            if (state.B("finished")) { FinishLab(); return; }
            if (completedPrevious) CloseReferenceLessonHelp();
            paused = state.B("paused");
            if (state.B("limitDecisionPending"))
            {
                if (labCapture != null) labCapture.Suspend();
                timer.Stop();
                pressed.Clear();
            }
            int remaining = state.I("remaining"), seconds = state.I("timeLimit");
            timerLabel.Visibility = seconds > 0 || state.B("finishingMission") ? Visibility.Visible : Visibility.Collapsed;
            timerLabel.Text = state.B("finishingMission") ? "Дорабатываем миссию" :
                (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
            timerLabel.ToolTip = state.B("finishingMission") ? "Ограничение снято до конца текущей миссии." :
                "Оставшееся время";
            int actionLimit = state.I("actionLimit");
            AutomationProperties.SetHelpText(timerLabel, actionLimit > 0 ?
                state.I("solved") + " из " + actionLimit + " действий" : "Оставшееся время");
            if (verifyMode)
                AutomationProperties.SetItemType(labTask, state.S("scenario") + "    ·    " + state.S("phase") +
                    "\nВыполнено " + state.I("solved"));
            var task = state.D("task");
            bool guided = state.B("showGuide", true);
            string coachPrompt = state.S("coachPrompt"), coachReason = state.S("coachReason");
            labTask.Text = guided ? task.S("title") : coachPrompt.Length > 0 ? coachPrompt : state.S("phase");
            string objectives = coachPrompt.Length > 0 ? coachReason :
                string.Join("\n", state.Items("objectives").Select(item => item.Value())) +
                (coachReason.Length > 0 ? "\n" + coachReason : "");
            labTask.ToolTip = (state.S("desktopProfile", LessonDesktopProfile) == "windows" ? "Windows 10" : "Ubuntu") + " · " + state.S("scenario") +
                "\n" + objectives;
            labKeys.ToolTip = guided ? task.S("hint") : "";
            AutomationProperties.SetHelpText(labKeys, guided ? task.S("keys") : "");
            AutomationProperties.SetHelpText(labTask, guided ? task.S("text") : "");
            // A WPF TextBlock with empty HelpText falls back to its tooltip.
            // Keep QA input metadata separate from visible/accessibility hints.
            if (verifyMode)
            {
                string keys = task.S("keys"), text = task.S("text");
                // Many navigation tasks carry the target row's title in Text;
                // it describes the destination and must not be typed.
                string id = task.S("id");
                AutomationProperties.SetItemStatus(labKeys,
                    keys.Length > 0 && (id == "quick-choose" || id == "save") ? text : "");
                AutomationProperties.SetItemStatus(labTask,
                    keys.Length > 0 ? "keys:" + keys : text.Length > 0 ? "text:" + text : "none:");
            }
            labDesktop.Update(state.D("scene"), labApp);
            if (HasNotebookHandoffWitness(state))
                AutomationProperties.SetItemType(labDesktop, NotebookHandoffWitness(state));
            string status = state.S("status").Replace("LSuper", "⊞").Replace("RSuper", "⊞ прав.").Replace("Super", "⊞");
            string held = string.Join(" + ", state.Items("held").Select(key => LiveKeyboard.Title(key.Value())));
            labStatus.Text = paused ? "Пауза · пробел — продолжить" :
                (status.Length > 0 ? status : "Нажмите сочетание из задания") + (held.Length > 0 ? "   ·   Нажато " + held : "");
            labStatus.Foreground = state.S("feedback") == "error" ? Error : state.S("feedback") == "good" ? Ui.Brush("#A6D8B0") : Dim;
            UpdateReferenceLesson(task);
            SetReferenceLessonAction(labPause, paused ? "Продолжить · пробел" : "Пауза · " + DisplayLabGesture(LabPauseKey()));
            var pauseGlyph = labPause.Content as TextBlock;
            if (pauseGlyph != null)
            {
                bool showAlternate = !paused && state.D("scene").B("diagnosticWorkspace");
                pauseGlyph.Text = paused ? "▶" : showAlternate ? "Ⅱ F6" : "Ⅱ";
                pauseGlyph.FontSize = showAlternate ? 15 : 25;
            }
            SetReferenceLessonAction(labFinish, "Результат · " + DisplayLabGesture(LabEndKey()));
            labKeyAid.ToolTip = "Показать или скрыть клавиатуру · " + DisplayLabGesture("F4");
            labHandAid.ToolTip = "Показать или скрыть руки · " + DisplayLabGesture("F7");
            var guide = state.D("guide").Items("steps").FirstOrDefault();
            var exerciseLayout = state.D("scene").S("layout") == "RU" ? "RU" : "EN";
            var guideTargets = guide == null ? Enumerable.Empty<Data>() : guide.Items();
            keyboard.Update(library.Items("keyboard"), exerciseLayout,
                guideTargets, state.Items("held").Select(key => key.Value()),
                library.B("showMistakeKey", true) && state.S("feedback") == "error" ? state.S("lastKey") : "",
                library.B("liveShowKeys"), library.B("liveShowHands"), true,
                library.S("keyboardProfile", "matebook-d16"), library.S("fnMode", "hotkeys"));
            AutomationProperties.SetItemType(labGuide, "Раскладка клавиатуры: " + exerciseLayout);
            labKeyAid.Opacity = library.B("liveShowKeys") ? 1 : .5;
            labHandAid.Opacity = library.B("liveShowHands") ? 1 : .5;
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
            // The engine latches which limit actually won (a late UI timer
            // repaint must not turn an actions-first outcome into a timeout).
            string cause = state.S("limitReached");
            bool timerReached = cause == "timer" || cause.Length == 0 && state.I("timeLimit") > 0 && state.I("remaining") == 0;
            var title = Label(extensionPause ? "Миссия на паузе" :
                timerReached ? "Время вышло" : "Лимит действий достигнут", 19, Ink, true);
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
            if (result.Exists) center.Content = BuildLabResult(result, backdrop);
            else ReturnToLiveCatalog();
        }

        private void BackFromLab()
        {
            labSaveBackPending = true;
            if (!TrySaveLab()) return;
            labSaveBackPending = false;
            labActive = active = paused = false;
            RestorePausedLabView();
            ReturnToLiveCatalog();
        }

        private void RestartLab()
        {
            if (labActive) FinishLab();
            if (labActive) return; // Retain the paused lesson until its save succeeds.
            StartLabAttempt(lastLabRouteKind == "course" ? "" : lastLabMission, false, lastLabRouteKind,
                actionOverride: lastLabRuntimeActions, timeOverride: lastLabRuntimeSeconds, adaptiveGuide: lastLabAdaptiveGuide);
        }

        private FrameworkElement BuildLabResult(Data result, FrameworkElement backdrop, bool preview = false)
        {
            return MakeReferenceLabResult(result, backdrop, preview);
        }
    }
}
