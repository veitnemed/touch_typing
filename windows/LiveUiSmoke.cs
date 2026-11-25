using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Xml;

// Compile separately. This process drives only the new --qa trainer process it owns.
internal static partial class LiveUiSmoke
{
    private static Process app;
    private static IntPtr trainer, focusProbe;
    private static AutomationElement window;
    private static int count;
    private static string evidenceDirectory;
    private static Size? expectedCodeClient;
    private static bool? originalNumLock;

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 0) { Console.Error.WriteLine("Pass the full path to TouchTyping.exe."); return 2; }
        try
        {
            bool liveUxSmall = arguments.Contains("--live-ux-small");
            bool liveUx = liveUxSmall || arguments.Contains("--live-ux");
            bool referenceBrowser = arguments.Contains("--reference-browser");
            bool browserClone = arguments.Contains("--browser-clone");
            string numLockMode = arguments.Contains("--workday-numlock-on") ? "on" :
                arguments.Contains("--workday-numlock-off") ? "off" :
                arguments.Contains("--workday-numlock-dedicated") ? "dedicated" : "";
            bool dailyUse = arguments.Contains("--daily-use");
            bool gnomeProfile = arguments.Contains("--gnome-profile");
            bool debugDay = arguments.Contains("--debug-day");
            bool codeTheme = arguments.Contains("--code-theme");
            bool pythonEditor = arguments.Contains("--python-editor");
            bool notebookMission = arguments.Contains("--notebook-mission");
            bool projectFileMission = arguments.Contains("--project-file-mission");
            bool editorProjectFileMission = arguments.Contains("--editor-project-file-mission");
            bool editorCourse = arguments.Contains("--editor-course");
            bool coachHint = arguments.Contains("--live-coach-hint");
            bool coach = coachHint || arguments.Contains("--live-coach");
            bool nativeLayout = arguments.Contains("--native-layout");
            bool referenceEvidence = notebookMission || projectFileMission || editorProjectFileMission || editorCourse || coach || liveUx || nativeLayout || referenceBrowser || browserClone || numLockMode.Length > 0 || dailyUse || gnomeProfile || debugDay || codeTheme || pythonEditor || arguments.Contains("--reference-evidence");
            if (referenceEvidence && SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Per-monitor-aware evidence capture cannot be enabled.");
            string path = Path.GetFullPath(arguments[0]);
            if (referenceEvidence)
                evidenceDirectory = Directory.CreateDirectory(Path.Combine(
                    Directory.GetParent(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")).FullName,
                    "native-reference-evidence")).FullName;
            if (arguments.Contains("--soft-finish"))
                Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_SOFT_FINISH_SECONDS", "7");
            Console.WriteLine("UI test seed " + Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED"));
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_NAV_TRACE", gnomeProfile ? "workspace" : numLockMode.Length > 0 ? "workday-keypad" :
                referenceEvidence && !nativeLayout && !referenceBrowser && !dailyUse && !debugDay && !codeTheme && !pythonEditor && !browserClone && !notebookMission && !projectFileMission && !editorProjectFileMission && !editorCourse && !coach ? "terminal" : null);
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", browserClone || projectFileMission || editorProjectFileMission || coach ? "1" : null);
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR", pythonEditor || editorCourse ? "1" : null);
            app = Process.Start(new ProcessStartInfo(path, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(path) });
            Until(delegate {
                app.Refresh();
                if (app.HasExited) throw new InvalidOperationException("Trainer exited at startup: " + app.ExitCode);
                trainer = app.MainWindowHandle;
                return trainer != IntPtr.Zero;
            }, "trainer window", 45000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "The automation root belongs to the new QA process");
            Focus();
            if (liveUx)
            {
                if (liveUxSmall) ResizeOwnedEditorClient(1024, 600, true);
                CheckLiveUx(path, liveUxSmall);
                Console.WriteLine("PASS: " + count + " WPF Live direct-catalog and Configure assertions");
                return 0;
            }
            if (notebookMission)
            {
                CheckNotebookMission();
                Console.WriteLine("PASS: " + count + " native WPF notebook checks");
                return 0;
            }
            if (projectFileMission)
            {
                CheckProjectFileMission();
                Console.WriteLine("PASS: " + count + " native WPF download and project file checks");
                return 0;
            }
            if (coach)
            {
                CheckLiveCoach(coachHint);
                Console.WriteLine("PASS: " + count + " native WPF Live coaching assertions");
                return 0;
            }
            if (editorCourse)
            {
                CheckEditorCourse(path);
                Console.WriteLine("PASS: " + count + " native WPF direct VS Code course checks");
                return 0;
            }
            if (editorProjectFileMission)
            {
                CheckEditorProjectFileMission();
                Console.WriteLine("PASS: " + count + " native WPF Explorer project file checks");
                return 0;
            }
            if (pythonEditor)
            {
                CheckPythonEditingMissions();
                Console.WriteLine("PASS: " + count + " native WPF Python editing, search, F12 and F2 checks");
                return 0;
            }
            if (codeTheme)
            {
                CheckCodeTheme();
                Console.WriteLine("PASS: " + count + " native WPF GitHub Dark editor assertions");
                return 0;
            }
            if (gnomeProfile)
            {
                CheckGnomeProfilePractice();
                Console.WriteLine("PASS: " + count + " native WPF GNOME profile assertions (simulated desktop)");
                return 0;
            }
            if (debugDay)
            {
                CheckDebugDay();
                Console.WriteLine("PASS: " + count + " native WPF debug-day assertions");
                return 0;
            }
            if (dailyUse)
            {
                CheckDailyUse();
                Console.WriteLine("PASS: " + count + " native WPF daily-use assertions");
                return 0;
            }
            if (arguments.Contains("--soft-finish"))
            {
                CheckSoftFinish(path);
                Console.WriteLine("PASS: " + count + " native WPF soft finish assertions");
                return 0;
            }
            if (nativeLayout)
            {
                CheckNativeLessonLayouts();
                Console.WriteLine("PASS: " + count + " native WPF layout assertions");
                return 0;
            }
            if (browserClone)
            {
                CheckBrowserClone();
                CheckResearchHandoff();
                Console.WriteLine("PASS: " + count + " WPF browser editing, search, bookmark, clone and report handoff checks");
                return 0;
            }
            if (referenceBrowser)
            {
                CheckReferenceBrowser();
                Console.WriteLine("PASS: " + count + " native WPF reference browser assertions");
                return 0;
            }
            if (arguments.Contains("--workday-source-note"))
            {
                CheckWorkdaySourceNote(numLockMode);
                Console.WriteLine("PASS: " + count + " native WPF Workday source-note assertions");
                return 0;
            }
            Chord("Ctrl+F5");
            Check(Exists("HomeDashboard") && !Exists("LabTask"), "Modified F5 keeps the Live catalog open");
            // Keep key/cursor evidence independent of later window-close assertions.
            OpenLiveConfiguration("terminal");
            Select("LabMode", 0);
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabTask") && Name("LabPause").StartsWith("Пауза"); }, "focused terminal lesson");
            CheckPasteLayouts();
            CheckReferenceTerminalCursor();
            CheckTerminalCursorRepeat();
            Key("F10");
            Until(delegate { return Exists("LabResult"); }, "focused terminal result");
            Invoke("LabChooseClass");
            Until(delegate { return Exists("HomeDashboard"); }, "class chooser after focused terminal lesson");
            Console.WriteLine("PASS: focused terminal native input assertions");

            string[] apps = { "ubuntu", "chrome", "code", "terminal", "workday" };
            foreach (string name in apps)
                foreach (int mode in new[] { 0, 1 })
                {
                    OpenLiveConfiguration(name);
                    Select("LabMode", mode);
                    Select("LabTimer", 1);
                    var started = Stopwatch.StartNew();
                    Key("F5");
                    Until(delegate { return Exists("LabTask") && Name("LabPause").StartsWith("Пауза"); }, "active " + name + " lesson");
                    CheckFresh180sCountdown(started, name + "/" + mode);
                    Check(Visible("LabScenarioProgress") && !Visible("ResultMetric-accuracy"), "Active lesson shows scenario progress, not accuracy");
                    Check(Name("LabTask").Length > 0 && Exists("LabDesktop"), "Task and Ubuntu scene are accessible");
                    if (mode == 0)
                    {
                        CaptureReferenceFrame("class-" + name);
                        RecordLessonChrome(name);
                    }
                    Key("F8");
                    Until(delegate { return Exists("LabPauseResult"); }, "pause result overlay");
                    Check(Name("LabPauseResult") == "Пауза", "Pause shows a Stamina-style result panel");
                    CheckThreeHeadlineMetrics("Pause");
                    Check(!Visible("LabErrorReview"), "Pause has no mistake-review statistics");
                    CheckStored180sCheckpoint(name, mode == 0 ? "independent" : "guided");
                    if (name == "ubuntu" && mode == 0) CaptureReferenceFrame("04-pause");
                    string frozen = Name("LabClock");
                    Chord("Ctrl+F10");
                    Check(Exists("LabTask") && Exists("LabPauseResult"), "Modified F10 keeps the paused lesson open");
                    Wait(1100);
                    Check(Name("LabClock") == frozen, "The clock stops during a pause");
                    Find("LabPauseRestart").SetFocus();
                    Key("Tab");
                    var focused = AutomationElement.FocusedElement;
                    Check(focused != null && focused.Current.ProcessId == app.Id && focused.Current.AutomationId == "LabPauseContinue", "Tab reaches the pause controls");
                    Key("Enter");
                    Check(Exists("LabPauseResult"), "Enter does not resume the paused lesson");
                    Key("Space");
                    Until(delegate { return !Exists("LabPauseResult") && Name("LabPause").StartsWith("Пауза"); }, "resumed lesson");
                    RequestClose();
                    Until(delegate { return ExitDialog() != null; }, "save-and-close confirmation");
                    Check(!app.HasExited && Exists("LabPauseResult"), "Closing pauses the lesson and releases input");
                    InvokeDialog("LabExitCancel");
                    Until(delegate { return ExitDialog() == null && Exists("LabPauseResult"); }, "cancelled close");
                    Key("Space");
                    Until(delegate { return !Exists("LabPauseResult"); }, "resume after cancelled close");
                    CheckFocusLoss();
                    if (name == "ubuntu" && mode == 0)
                    {
                        Key("F8");
                        Until(delegate { return Exists("LabPauseResult"); }, "pause before keyboard restart");
                        Find("LabPauseRestart").SetFocus();
                        var restarted = Stopwatch.StartNew();
                        Key("Enter");
                        Until(delegate { return !Exists("LabPauseResult") && Exists("LabTask"); },
                            "Enter restarts through the focused pause button");
                        CheckFresh180sCountdown(restarted, name + "/" + mode + " restart");
                    }
                    if (name == "terminal")
                    {
                        CheckPasteLayouts();
                        CheckTerminalCursorRepeat();
                    }
                    VirtualEnglish();
                    Solve(24, false);
                    Key("F10");
                    Until(delegate { return Exists("LabResult"); }, "lesson result");
                    CheckThreeHeadlineMetrics("Result");
                    if (name == "ubuntu" && mode == 0)
                    {
                        CaptureReferenceFrame("05-result");
                        CheckReferenceReview();
                        CaptureReferenceFrame("06-result-review");
                        Find("LabErrorReview").SetFocus();
                        Key("Space");
                        Until(delegate { return !Visible("ResultMetric-accuracy"); }, "mistake review closes again");
                    }
                    Check(Find("LabResult").Current.HelpText.Contains("Ошибки"), "Result exposes completion, mistakes and record");
                    if (name == "ubuntu" && mode == 0)
                    {
                        Find("LabRepeat").SetFocus();
                        Key("Space");
                        Until(delegate { return !Exists("LabResult") && Exists("LabTask"); }, "Space replays from the result");
                        Key("F10");
                        Until(delegate { return Exists("LabResult"); }, "result after Space replay");
                    }
                    Invoke("LabChooseClass");
                    Until(delegate { return Exists("HomeDashboard"); }, "class chooser after result");
                }
            if (arguments.Contains("--full-session"))
            {
                OpenLiveConfiguration("terminal");
                Select("LabMode", 1);
                Select("LabTimer", 1);
                Key("F5");
                Until(delegate { return Exists("LabTask"); }, "full timed session");
                VirtualEnglish();
                Solve(int.MaxValue, true);
                Until(delegate { return Exists("LabResult"); }, "three-minute completion", 10000);
                string result = Find("LabResult").Current.HelpText;
                Check(result.Contains("Цель занятия выполнена"), "A complete 180-second session is recorded");
                var match = Regex.Match(result, "Рекорд ([0-9]+)");
                Check(match.Success && int.Parse(match.Groups[1].Value) > 0, "A completed class has a positive record");
            }
            if (Exists("LabResult")) Invoke("LabChooseClass");
            OpenLiveConfiguration("terminal");
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabTask"); }, "lesson before stop storage failure");
            Send(Character('a')); // Record an attempt so Finish must save even with no completed action.
            WithBlockedProfile(delegate
            {
                Key("F10");
                VerifyPendingSave();
                Key("F8");
                VerifyPendingSave();
                Invoke("LabRetrySave");
                VerifyPendingSave();
            });
            Invoke("LabRetrySave");
            Until(delegate { return Exists("LabResult") && !Exists("LabSaveError"); }, "explicit stop retry saves");
            Invoke("LabChooseClass");
            Until(delegate { return Exists("HomeDashboard"); }, "chooser after retried stop");
            OpenLiveConfiguration("ubuntu");
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabTask"); }, "lesson before save-and-close");
            Send(Character('a'));
            RequestClose();
            Until(delegate { return ExitDialog() != null; }, "save-and-close dialog");
            WithBlockedProfile(delegate
            {
                InvokeDialog("LabExitSave");
                VerifyPendingSave();
            });
            Invoke("LabRetrySave");
            var closing = Stopwatch.StartNew();
            while (!app.HasExited && closing.ElapsedMilliseconds < 6000) { Wait(80); app.Refresh(); }
            Check(app.HasExited, "Confirmed close exits the trainer");
            Console.WriteLine("PASS: " + count + " native Windows UI assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            try { DumpProcessWindows(); }
            catch (Exception diagnosticError) { Console.Error.WriteLine("Window diagnostics failed: " + diagnosticError.Message); }
            return 1;
        }
        finally
        {
            if (focusProbe != IntPtr.Zero)
            {
                if (originalNumLock.HasValue && app != null && !app.HasExited)
                {
                    try
                    {
                        SetForegroundWindow(focusProbe);
                        Until(delegate { return GetForegroundWindow() == focusProbe; }, "owned Num Lock restoration probe");
                        SetProbeNumLock(originalNumLock.Value);
                    }
                    catch (Exception error) { Console.Error.WriteLine("Unable to restore runner Num Lock: " + error.Message); }
                }
                DestroyWindow(focusProbe);
            }
            if (app != null)
            {
                try { if (!app.HasExited) app.Kill(); }
                catch (InvalidOperationException) { }
                app.Dispose();
            }
        }
    }

    private static void CheckLiveUx(string executable, bool small)
    {
        Until(delegate { return Visible("HomeDashboard") && Visible("HomeClass-chrome"); }, "single Live catalog opens on startup");
        Check(Visible("ReferenceNav-practice") && Visible("ReferenceNav-learn") &&
            !Exists("ReferenceNav-home") && !Exists("ReferenceNav-progress"), "Only Live and typing are top-level sections");
        Check(!Exists("LabStart") && !Exists("LabMission"), "No duplicate chooser or permanent Start controls");
        CaptureLiveUxFrame("live-ux-01-catalog");
        if (small)
        {
            CheckCompactLiveCatalog();
            foreach (string id in new[] { "ubuntu", "code", "terminal", "workday" })
                CheckSharedSettings(id, "ubuntu");
            Invoke("LabDesktopProfile-windows");
            Until(delegate { return Exists("HomeClass-windows"); }, "actual catalog switches to Windows 10");
            foreach (string id in new[] { "windows", "workday" })
                CheckSharedSettings(id, "windows");
            Invoke("LabDesktopProfile-ubuntu");
            Until(delegate { return Exists("HomeClass-ubuntu"); }, "restore the first catalog for the Chrome resume probe");
        }
        Invoke("LiveConfigure-chrome");
        Until(delegate { return Visible("LiveConfigurePanel") && Visible("LabMission") && Exists("LabStart"); }, "separate Chrome Configure opens");
        Check(!Exists("HomeClass-chrome") && Name("LiveConfigureHeading").Contains("Chrome"), "Configure has no embedded second catalog");
        var missionBounds = Find("LabMission").Current.BoundingRectangle;
        var modeBounds = Find("LabMode").Current.BoundingRectangle;
        var actionBounds = Find("LabDuration").Current.BoundingRectangle;
        var timerBounds = Find("LabTimer").Current.BoundingRectangle;
        Check(missionBounds.Right < modeBounds.Left && Math.Abs(missionBounds.Top - modeBounds.Top) < 3 &&
            actionBounds.Right < timerBounds.Left && Math.Abs(actionBounds.Top - timerBounds.Top) < 3 &&
            IsWithinLiveClient("LabStart"), "The settings form uses both columns and keeps its Start in view");
        var level = Find("LabLevel");
        level.SetFocus();
        Chord("Alt+Down");
        var expander = (ExpandCollapsePattern)level.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Check(expander.Current.ExpandCollapseState == ExpandCollapseState.Expanded, "Physical Alt+Down opens dark custom choices");
        CaptureLiveUxFrame("live-ux-02-configure-open-popup");
        Key("Esc");
        Check(expander.Current.ExpandCollapseState == ExpandCollapseState.Collapsed, "Escape closes the option list");
        if (small)
        {
            foreach (string id in new[] { "LabMission", "LabMode", "LabDuration", "LabTimer" })
            {
                var picker = Find(id);
                picker.SetFocus();
                Until(delegate { return IsWithinLiveClient(id); }, id + " keyboard focus scrolls the control into view");
                Chord("Alt+Down");
                var options = (ExpandCollapsePattern)picker.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                Check(options.Current.ExpandCollapseState == ExpandCollapseState.Expanded, id + " opens with the same native dark popup template");
                if (id == "LabTimer")
                    Check(IsWithinLiveClient("LabStart"), "Opening the timer on the right keeps Start in view on the left");
                CaptureLiveUxFrame("live-ux-small-dropdown-" + id);
                Key("Esc");
                Check(options.Current.ExpandCollapseState == ExpandCollapseState.Collapsed, id + " closes with Escape");
            }
        }
        Invoke("LiveConfigureBack");
        Until(delegate { return FocusedLiveCard("chrome"); }, "Configure returns focus to Chrome");
        Key("Enter");
        Until(delegate { return Visible("LabDesktop") && Exists("LabTask") && ReadSolved() == 0; },
            "Enter on one card loads a real first task");
        Check(!Exists("LabMission") && !Visible("LiveAidPanel"), "First task has no chooser or default keyboard or hands");
        CheckVerticalPrompt();
        CaptureLiveUxFrame("live-ux-03-active-lesson");
        Key("F4");
        Until(delegate { return Visible("LiveAidPanel") && Find("LiveAidPanel").Current.ItemStatus == "keyboard;no-hands"; },
            "Keyboard appears independently without enabling hands");
        Check(Name("LiveAidPanel") == "Клавиатура", "Live keyboard has a real accessibility name");
        CaptureLiveUxFrame("live-ux-03b-keyboard-only");
        if (small)
        {
            CheckAidGeometry(700, 120, 0, 250);
            Key("F7");
            Until(delegate { return Find("LiveAidPanel").Current.ItemStatus == "keyboard;hands"; },
                "Explicit hand preference adds hands without removing keys");
            CheckAidGeometry(630, 108, 90, 250);
            CaptureLiveUxFrame("live-ux-03c-both-aids");
            Key("F4");
            Until(delegate { return Find("LiveAidPanel").Current.ItemStatus == "no-keyboard;hands"; },
                "Hands remain visible when only the keyboard is switched off");
            CheckAidGeometry(0, 0, 80, 250);
            Key("F7");
            Until(delegate { return !Visible("LiveAidPanel"); }, "Disabling both aids restores the whole workspace");
            Key("F4");
            Until(delegate { return Visible("LiveAidPanel") && Find("LiveAidPanel").Current.ItemStatus == "keyboard;no-hands"; },
                "Restore keyboard-only persistence probe");
        }
        if (Find("LabTask").Current.ItemStatus == "keys:Ctrl+L") Chord("Ctrl+L");
        else
        {
            string input = Find("LabTask").Current.ItemStatus;
            Check(input.StartsWith("keys:"), "Supported QA step reports an actual shortcut");
            Chord(input.Substring(5));
        }
        Until(delegate { return ReadSolved() == 1; }, "Actual first step is credited");
        Invoke("LabBack");
        Until(delegate { return FocusedLiveCard("chrome") && Visible("LiveResume-chrome"); }, "Back saves draft and focuses source card");
        RequestClose();
        UntilExit();
        app.Dispose();
        app = Process.Start(new ProcessStartInfo(executable, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(executable) });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "trainer restarts", 12000);
        window = AutomationElement.FromHandle(trainer);
        Focus();
        if (small) ResizeOwnedEditorClient(1024, 600, true);
        Invoke("LiveResume-chrome");
        Until(delegate { return Visible("LabDesktop") && ReadSolved() == 1; }, "explicit Continue resumes the saved first credit");
        Check(Visible("LiveAidPanel") && Find("LiveAidPanel").Current.ItemStatus == "keyboard;no-hands",
            "Only explicitly enabled Live keyboard persisted through restart");
        Key("F10");
        Until(delegate { return Visible("LabResult"); }, "native lesson result");
        CaptureLiveUxFrame("live-ux-04-result");
        Invoke("LabChooseClass");
        Until(delegate { return FocusedLiveCard("chrome"); }, "result exits to focused source card");
        Invoke("ReferenceNav-learn");
        Key("F5");
        Until(delegate
        {
            var stop = window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "ОСТАНОВИТЬ"));
            return stop != null && stop.Current.ProcessId == app.Id && !stop.Current.IsOffscreen;
        }, "actual Typing lesson active before capturing its result");
        CaptureLiveUxFrame("live-ux-04b-active-typing");
        Key("Esc");
        Until(delegate { return Visible("PracticeResult") && Visible("TypingErrorReview"); }, "Typing creates its centered result with error review");
        Check(Visible("ResultMetric-speed") && Visible("ResultMetric-accuracy") && Visible("TypingRepeat"),
            "Typing preserves its own speed, accuracy and repeat action");
        CaptureLiveUxFrame("live-ux-05-typing-result");
        var review = Find("TypingErrorReview");
        review.SetFocus();
        Key("Space");
        var reviewPattern = (ExpandCollapsePattern)review.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Check(reviewPattern.Current.ExpandCollapseState == ExpandCollapseState.Expanded, "Typing error review expands from keyboard");
        CaptureLiveUxFrame("live-ux-06-typing-review");
    }

    private static void CheckSharedSettings(string appId, string profile)
    {
        Find("LiveConfigure-" + appId).SetFocus();
        OpenLiveConfiguration(appId);
        Check(IsWithinLiveClient("LabStart") && IsWithinLiveClient("LabMode") &&
            !Exists("HomeClass-" + appId), "Same full-width Configure shell for " + appId + " under " + profile);
        var mission = Find("LabMission").Current.BoundingRectangle;
        var timer = Find("LabTimer").Current.BoundingRectangle;
        Check(mission.Right < timer.Left && mission.Width > 180 && timer.Width > 80,
            "Exercise and timer occupy distinct columns for " + appId + " under " + profile);
        var timerControl = Find("LabTimer");
        timerControl.SetFocus();
        Chord("Alt+Down");
        var list = (ExpandCollapsePattern)timerControl.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Check(list.Current.ExpandCollapseState == ExpandCollapseState.Expanded && IsWithinLiveClient("LabStart"),
            "Settings timer opens without hiding Start for " + appId + " under " + profile);
        CaptureLiveUxFrame("live-ux-shared-settings-" + profile + "-" + appId);
        Key("Esc");
        Invoke("LiveConfigureBack");
        Until(delegate { return FocusedLiveCard(appId); }, "Configure returns focus to " + appId);
    }

    private static void CheckAidGeometry(int keyWidth, int keyHeight, int handWidth, int sceneHeight)
    {
        string extent = Find("LiveAidPanel").Current.HelpText;
        var match = Regex.Match(extent, @"^keys:(\d+)x(\d+);hands:(\d+)x(\d+)$");
        Check(match.Success, "Aid rail reports actual drawn keybed and hand bounds: " + extent);
        int measuredKeyWidth = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int measuredKeyHeight = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int measuredHandWidth = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        Check((keyWidth == 0 ? measuredKeyWidth == 0 : measuredKeyWidth >= keyWidth && measuredKeyHeight >= keyHeight) &&
            (handWidth == 0 ? measuredHandWidth == 0 : measuredHandWidth >= handWidth),
            "Rendered keyboard and hands have usable native size: " + extent);
        double sceneDips = Find("LabDesktop").Current.BoundingRectangle.Height * 96 / GetDpiForWindow(trainer);
        Check(sceneDips >= sceneHeight, "Aid rail leaves the simulation usable: " + sceneDips.ToString("F0") + " DIPs");
    }

    private static void CheckCompactLiveCatalog()
    {
        var wanted = new[] { "ubuntu", "chrome", "code", "terminal", "workday" };
        Console.WriteLine("1024x600 first-paint full-card count=" + wanted.Count(id => IsWithinLiveClient("HomeClass-" + id)));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Find("HomeClass-ubuntu").SetFocus();
        for (int i = 0; i < 32 && visited.Count != wanted.Length; i++)
        {
            var current = AutomationElement.FocusedElement;
            if (current != null && current.Current.ProcessId == app.Id)
            {
                var id = current.Current.AutomationId;
                if (wanted.Any(name => id == "HomeClass-" + name) && !visited.Contains(id))
                {
                    Until(delegate { return IsWithinLiveClient(id); }, id + " scrolls fully into view by real Tab focus");
                    visited.Add(id);
                    Check(true, id + " is keyboard reachable and entirely in the actual small client");
                }
            }
            if (visited.Count != wanted.Length) Key("Tab");
        }
        Check(visited.Count == wanted.Length, "All five Live cards are reachable at actual 1024x600");
        Find("HomeClass-chrome").SetFocus();
    }

    private static bool IsWithinLiveClient(string id)
    {
        if (!Visible(id)) return false;
        var origin = new CapturePoint();
        CaptureRect client;
        if (!GetClientRect(trainer, out client) || !ClientToScreen(trainer, ref origin)) return false;
        var area = new System.Windows.Rect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        var bounds = Find(id).Current.BoundingRectangle;
        return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 && area.Contains(bounds);
    }

    private static void CaptureLiveUxFrame(string label)
    {
        CaptureRect client;
        var origin = new CapturePoint();
        if (app.HasExited || GetForegroundWindow() != trainer || !GetClientRect(trainer, out client) ||
            !ClientToScreen(trainer, ref origin))
            throw new InvalidOperationException("Refusing capture outside the exact foreground Live window.");
        var native = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        var monitor = System.Windows.Forms.Screen.FromHandle(trainer).Bounds;
        var clipped = Rectangle.Intersect(native, monitor);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            throw new InvalidOperationException("No visible Live client pixels.");
        using (var image = new Bitmap(clipped.Width, clipped.Height))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(clipped.X, clipped.Y, 0, 0, clipped.Size);
            Check(GetForegroundWindow() == trainer, "Live evidence source remains the same window");
            image.Save(Path.Combine(evidenceDirectory, label + ".png"), ImageFormat.Png);
        }
        Console.WriteLine("CAPTURE " + label + " at 1:1, client=" + native + " monitor=" + monitor +
            " captured=" + clipped + " DPI=" + GetDpiForWindow(trainer));
    }

    private static void OpenLiveConfiguration(string appId)
    {
        if (Visible("LiveConfigureBack"))
        {
            Invoke("LiveConfigureBack");
            Until(delegate { return Visible("HomeDashboard"); }, "return from previous configuration");
        }
        Invoke("LiveConfigure-" + appId);
        Until(delegate { return Visible("LiveConfigurePanel") && Exists("LabStart") && Exists("LabMission"); },
            "configured lesson form for " + appId);
    }

    private static bool FocusedLiveCard(string appId)
    {
        var focus = AutomationElement.FocusedElement;
        return focus != null && focus.Current.ProcessId == app.Id &&
            focus.Current.AutomationId == "HomeClass-" + appId;
    }

    private static void UntilExit()
    {
        var timer = Stopwatch.StartNew();
        while (!app.HasExited && timer.ElapsedMilliseconds < 9000) { Wait(90); app.Refresh(); }
        Check(app.HasExited && app.ExitCode == 0, "catalog close exits normally");
    }

    private static void CheckReferenceBrowser()
    {
        Check(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED") == "0",
            "Use the pinned Python documentation fixture (seed 0)");
        OpenLiveConfiguration("chrome");
        Select("LabMode", 1);
        Select("LabLevel", 0);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        SelectMissionByName("Подготовить справочник к чтению");
        Key("F5");
        Until(delegate { return Exists("LabTask") && Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "active Chrome reading mission");
        Check(Find("LabTask").Current.ItemType.Contains("Подготовить справочник к чтению") &&
            Name("LabDesktop").StartsWith("Учебный Chrome") && Name("LabDesktop").Contains("Chrome."),
            "The actual training window is the selected Chrome mission");
        Check(Find("LabTask").Current.ItemStatus == "keys:Ctrl+L" && ReadSolved() == 0,
            "The initial visible task is Ctrl+L with no credit yet");
        Check(Visible("LabScenarioProgress") && !Exists("LabCounter") && !Visible("ResultMetric-accuracy"),
            "Activity shows one progress bar without text counters or accuracy");
        var range = (RangeValuePattern)Find("LabScenarioProgress").GetCurrentPattern(RangeValuePattern.Pattern);
        Check(range.Current.Value == 0 && range.Current.Maximum > 2,
            "A newly opened browser route has no completed goals");
        CheckVerticalPrompt();
        CaptureReferenceFrame("07-chrome-python-docs-before-ctrl-l");
        RecordLessonChrome("browser-reference");
        Chord("Ctrl+L");
        Until(delegate { return ReadSolved() == 1 && Find("LabTask").Current.ItemStatus == "keys:Ctrl+C"; },
            "native Ctrl+L earns exactly one credit and advances to copy");
        Check(range.Current.Value == 1, "A visual progress increment requires one completed goal");
        CaptureReferenceFrame("08-chrome-python-docs-address-selected");
        Chord("Ctrl+C");
        Until(delegate { return ReadSolved() == 2 && Find("LabTask").Current.ItemStatus == "keys:Ctrl+T"; },
            "native Ctrl+C earns one additional credit and advances to new tab");
        Check(true, "Native address selection and clipboard sequence advanced in order");
        Key("F8");
        Until(delegate { return Exists("LabPauseResult"); }, "native Chrome pause");
        CheckThreeHeadlineMetrics("Chrome pause");
        CaptureReferenceFrame("09-chrome-pause");
        Key("Space");
        Until(delegate { return !Exists("LabPauseResult"); }, "native Chrome resume");
        Key("F10");
        Until(delegate { return Exists("LabResult"); }, "native Chrome result");
        CheckThreeHeadlineMetrics("Chrome result");
        string summary = Find("LabResult").Current.HelpText;
        Check(summary.Contains("Выполнено 2.") && summary.Contains("Ошибки 0."),
            "The visible result records exactly two correct actions");
        CaptureReferenceFrame("10-chrome-result");
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_LESSON_CHROME") == "compact")
        {
            Invoke("LabChooseClass");
            Until(delegate { return Exists("HomeDashboard"); }, "catalog after standalone result");
            OpenLiveConfiguration("chrome");
            Key("F5");
            Until(delegate { return Visible("LabBack") && Name("LabPause").StartsWith("Пауза"); }, "active browser with footer Back");
            Chord("Ctrl+L");
            Until(delegate { return ReadSolved() == 1; }, "first browser action before Back");
            Invoke("LabBack");
            Until(delegate { return FocusedLiveCard("chrome") && Visible("LiveResume-chrome") && !Exists("LabTask"); },
                "footer Back saves the draft and returns focus to its Live catalog card");
            Check(true, "Footer Back retains the actual lesson exit action");
        }
    }

    private static void CheckVerticalPrompt()
    {
        var task = Find("LabTask").Current.BoundingRectangle;
        var keys = Find("LabKeys").Current.BoundingRectangle;
        Check(task.Width > 0 && keys.Width > 0 && task.Bottom <= keys.Top + 1 &&
            Math.Abs((task.Left + task.Width / 2) - (keys.Left + keys.Width / 2)) < 5,
            "Keycaps are below the instruction, with the two rows horizontally centered.");
    }

    // Walk a route from the normal catalog with real input. Each credited
    // step must advance once, and a curated mission must reach its result.
    private static void CheckCatalogMission(string area, int level, string title, Action<int, string> inspect)
    {
        OpenLiveConfiguration(area);
        Select("LabMode", 1); Select("LabLevel", level); Select("LabDuration", 0); Select("LabTimer", 0);
        SelectMissionByName(title);
        Key("F5");
        Until(delegate { return Visible("LabTask") && Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "mission opened from the normal catalog: " + title);
        VirtualEnglish();
        for (int step = 0; step < 75; step++)
        {
            if (Visible("LabResult")) return;
            string prompt = Find("LabTask").Current.ItemStatus;
            Check(ReadSolved() == step, "Exactly one credit per route action");
            Console.WriteLine("MISSION " + step + " " + Name("LabTask") + " | " + prompt);
            inspect(step, prompt);
            if (prompt.StartsWith("text:", StringComparison.Ordinal))
            {
                EnterCharacters(prompt.Substring(5));
                if (!Visible("LabResult") && ReadSolved() == step) Key("Enter");
            }
            else
            {
                Check(prompt.StartsWith("keys:", StringComparison.Ordinal), "Mission supplies a real keyboard action");
                string keys = prompt.Substring(5);
                if (Exists("LabKeys"))
                {
                    string prefixText = Find("LabKeys").Current.ItemStatus;
                    if (!String.IsNullOrEmpty(prefixText))
                    {
                        if (area == "code" && title == "Переименовать файл и переместить его по проекту" && keys == "Ctrl+S")
                            Chord("Ctrl+End");
                        EnterCharacters(prefixText);
                    }
                }
                for (int i = 0; i < (keys == "Tab" || keys == "Down" ? 20 : 1); i++)
                {
                    Chord(keys);
                    if (Visible("LabResult") || ReadSolved() > step) break;
                }
            }
            int expected = step + 1;
            Until(delegate { return Visible("LabResult") || ReadSolved() == expected; },
                "native credit for " + prompt + " in " + title);
            if (Visible("LabResult"))
            {
                string result = Find("LabResult").Current.HelpText;
                Check(result.Contains("Выполнено " + expected + "."), "Result records the exact credited step count");
                inspect(expected, "result");
                return;
            }
        }
        throw new InvalidOperationException("Mission did not finish: " + title);
    }

    private static void CheckLiveCoach(bool revealHint)
    {
        OpenLiveConfiguration("chrome");
        var choice = Find("LabMode");
        var selected = ((SelectionPattern)choice.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection();
        Check(selected.Length == 1 && selected[0].Current.Name == "По шагам", "Settings start in the familiar guided mode");
        var popup = (ExpandCollapsePattern)choice.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        popup.Expand();
        Wait(80);
        string[] names = choice.FindAll(TreeScope.Descendants, new PropertyCondition(
            AutomationElement.ControlTypeProperty, ControlType.ListItem)).Cast<AutomationElement>().Select(option => option.Current.Name).ToArray();
        Check(new[] { "Самостоятельно", "По шагам", "По ситуации" }.All(names.Contains),
            "Guided, independent, and adaptive modes all remain selectable");
        popup.Collapse();
        Invoke("LiveConfigureBack");
        Until(delegate { return FocusedLiveCard("chrome"); }, "Configure returns to the original card");
        Find("HomeClass-chrome").SetFocus();
        Key("Enter");
        Until(delegate { return Visible("LabDesktop") && ReadSolved() == 0 && Name("LabPause").StartsWith("Пауза"); },
            "ordinary Chrome card has started the adaptive course");
        VirtualEnglish();
        string before = BrowserWitness("LabDesktop")[1];
        Check(before.StartsWith("https://docs.python.org"), "Chrome course begins with a documentation page");
        Chord("Alt+D");
        Until(delegate { return BrowserWitness("LabDesktop")[2] == "url" && ReadSolved() == 0; },
            "wrong chord can focus the address but cannot get credit");
        Check(Visible("LabKeys"), "Correction still displays the right combination");
        CaptureReferenceFrame("coach-01-visible-hint");
        Chord("Ctrl+L");
        Until(delegate { return ReadSolved() == 1; }, "correct chord earns one credit");

        Action<int> next = delegate(int solved)
        {
            string prompt = Find("LabTask").Current.ItemStatus;
            Check(prompt.StartsWith("keys:"), "The current simulated action uses a physical shortcut");
            string keys = prompt.Substring(5);
            for (int attempt = 0; attempt < (keys == "Down" || keys == "Tab" ? 24 : 1); attempt++)
            {
                Chord(keys);
                if (ReadSolved() > solved) break;
            }
            Until(delegate { return ReadSolved() == solved + 1; }, "one exact credit at " + solved + " for " + keys);
        };

        for (int solved = 1; solved < 22; solved++) next(solved);
        Check(!Visible("LabKeys") && Name("LabTask") == "Выделите адрес страницы" &&
            BrowserWitness("LabDesktop")[1] == before, "A later task states the goal without giving away the shortcut");
        CaptureReferenceFrame("coach-02-blind-recall");
        if (revealHint)
        {
            Key("F1");
            Until(delegate { return Visible("LabKeys") && ReadSolved() == 22; },
                "the explicitly requested F1 hint appears without adding a score");
            CaptureReferenceFrame("coach-02b-requested-hint");
        }
        next(22);
        for (int solved = 23; solved < 26; solved++) next(solved);
        Check(!Visible("LabKeys") && Name("LabTask") == "Выделите адрес страницы" &&
            BrowserWitness("LabDesktop")[1] == "https://www.google.com/" &&
            Find("LabTask").Current.ItemType.StartsWith("Найти нужный раздел справки"),
            "The same skill transfers without a visible key to a new lesson and a different page");
        CaptureReferenceFrame("coach-03-blind-transfer");
        next(26);
        Check(BrowserWitness("LabDesktop")[2] == "url", "The recalled action changed the actual Chrome address focus");
        Invoke("LabBack");
        Until(delegate { return FocusedLiveCard("chrome") && Visible("LiveResume-chrome"); }, "coaching evidence saves with its draft");
        string profile = Path.Combine(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT"),
            "TouchTypingStudio-QA", "progress.xml");
        var xml = new XmlDocument();
        xml.XmlResolver = null;
        xml.Load(profile);
        var earned = xml.SelectNodes("/StudioProfile/ShortcutHistory/ShortcutAttempt[Skill='lab/chrome/url' and Correct='true' and CoachStage]");
        Check(earned.Count == 3, "Only three coached successes followed the real input");
        var receipts = earned.Cast<XmlNode>().ToArray();
        Check(receipts[0]["CoachStage"].InnerText == "practice" && receipts[0]["Assisted"].InnerText == "true",
            "First corrected action is a practice success");
        Check(receipts[1]["CoachStage"].InnerText == "recall" &&
            receipts[1]["Assisted"].InnerText == (revealHint ? "true" : "false"), "Hint choice is stored with the recall attempt");
        Check(receipts[2]["CoachStage"].InnerText == (revealHint ? "recall" : "transfer") &&
            receipts[2]["Assisted"].InnerText == "false" &&
            receipts[1]["CoachContext"].InnerText != receipts[2]["CoachContext"].InnerText,
            "A receipt reserves transfer for a previous independent success in a different context");
    }

    private static void CheckEditorCourse(string executable)
    {
        Find("HomeClass-code").SetFocus();
        Key("Enter");
        Until(delegate { return Visible("LabDesktop") && ReadSolved() == 0 && Name("LabPause").StartsWith("Пауза"); },
            "VS Code card opens its first section directly");
        Check(!Visible("LabMission") && Find("LabTask").Current.ItemType.StartsWith("Составить заметку по проекту") &&
            Name("LabScenarioProgress") == "Прогресс занятия: 0 из 40", "Established editor work starts a 40-action course");
        VirtualEnglish();
        CaptureReferenceFrame("course-01-original");

        Action<int> next = delegate(int solved)
        {
            string prompt = Find("LabTask").Current.ItemStatus;
            Check(prompt.StartsWith("keys:"), "Input instruction at " + solved + " describes a real key chord");
            string keys = prompt.Substring(5);
            string prefix = Exists("LabKeys") ? Find("LabKeys").Current.ItemStatus : "";
            if (prefix.Length > 0) EnterCharacters(prefix);
            for (int attempt = 0; attempt < (keys == "Down" || keys == "Tab" ? 24 : 1); attempt++)
            {
                Chord(keys);
                if (ReadSolved() > solved) break;
            }
            Until(delegate { return ReadSolved() == solved + 1; }, "one exact key credit for " + keys + " at " + solved);
        };

        for (int solved = 0; solved < 33; solved++)
        {
            next(solved);
            if (solved != 26) continue;
            const string fullNote = "def save_tasks(tasks, path):\n    text = json.dumps(tasks, indent=2)\n" +
                "def add_task(tasks, title):\nfrom tasks import add_task, find_task, mark_done\n";
            string expected = NativeFixtureHash(fullNote);
            var note = EditorWitness();
            Check(note[1] == "review-notes.txt" && ExactEditorTab(note, "review-notes.txt", expected, expected),
                "All three real source fragments reached the same saved review note");
            CaptureReferenceFrame("course-01b-three-source-note");
        }
        Check(!Visible("LabResult") && Find("LabTask").Current.ItemType.StartsWith("Проверить расчёт из блокнота") &&
            Name("LabScenarioProgress") == "Прогресс занятия: 33 из 40", "The original route rolls into notebook without ending or resetting progress");
        next(33); next(34);
        Check(Find("LabDesktop").Current.Name.Contains(".ipynb") || Find("LabDesktop").Current.HelpText.Contains(".ipynb"),
            "The learner opened a notebook from the integrated course");
        CaptureReferenceFrame("course-02-notebook-at-35");

        Invoke("LabBack");
        Until(delegate { return Visible("LiveResume-code") && FocusedLiveCard("code"); }, "Save to the original VS Code card");
        RequestClose();
        UntilExit();
        app.Dispose();
        app = Process.Start(new ProcessStartInfo(executable, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(executable) });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "restart extracted Windows application", 12000);
        window = AutomationElement.FromHandle(trainer);
        Focus();
        Invoke("LiveResume-code");
        Until(delegate { return Visible("LabDesktop") && ReadSolved() == 35; }, "the saved notebook resumes at action 35");
        Check(Name("LabScenarioProgress") == "Прогресс занятия: 35 из 40", "The course budget survived restart");
        for (int solved = 35; solved < 40; solved++) next(solved);
        Until(delegate { return Visible("LabLimitChoice"); }, "40 actions show the real limit decision, not an early result");
        Check(!Visible("LabResult") && ReadSolved() == 40, "The user reached 40 credited actions before deciding");
        CaptureReferenceFrame("course-03-action-limit");
        Invoke("LabLimitFinish");
        Until(delegate { return Visible("LabResult"); }, "explicit completion after 40 actions");
        Check(Find("LabResult").Current.HelpText.Contains("Выполнено 40."), "The result includes the whole course");
        CaptureReferenceFrame("course-04-completed");
    }

    private static void CheckNotebookMission()
    {
        bool opened = false;
        var outputs = new List<string>();
        CheckCatalogMission("code", 1, "Исправить формулу в блокноте по заявке", (step, prompt) =>
        {
            if (prompt == "result")
            {
                Check(opened && outputs.Count >= 2, "VS Code showed distinct original and corrected cell outputs");
                CaptureReferenceFrame("notebook-03-result");
                return;
            }
            var scene = Find("LabDesktop").Current;
            string description = scene.Name + " " + scene.HelpText;
            if (!opened && description.Contains(".ipynb") && description.Contains("ячейк"))
            {
                opened = true;
                CaptureReferenceFrame("notebook-01-opened");
            }
            var output = Regex.Match(scene.HelpText, @"Вывод:\s*(-?\d+)\b");
            if (opened && output.Success && !outputs.Contains(output.Groups[1].Value))
            {
                outputs.Add(output.Groups[1].Value);
                CaptureReferenceFrame("notebook-02-output-" + outputs.Count);
            }
        });
    }

    private static void CheckProjectFileMission()
    {
        Invoke("LabDesktopProfile-windows");
        Until(delegate { return Exists("HomeClass-windows"); }, "Windows 10 simulation selected");
        bool rename = false, move = false;
        CheckCatalogMission("workday", 1, "Скачать CSV, переименовать и передать в проект", (step, prompt) =>
        {
            if (prompt == "result")
            {
                var end = BrowserWitness("LabResult");
                Check(rename && move && end[0] == "code" &&
                    end[1].Contains("/project/reports/") && end[1].EndsWith(".csv", StringComparison.Ordinal),
                    "Transferred CSV is opened in the project after the rename");
                CaptureReferenceFrame("project-files-03-result");
                return;
            }
            var scene = BrowserWitness("LabDesktop");
            if (!rename && scene[0] == "files" && scene[2] == "file-name")
            {
                rename = true;
                CaptureReferenceFrame("project-files-01-rename");
            }
            if (!move && scene[6].Contains("/project/reports/") && scene[6].Contains(".csv"))
            {
                move = true;
                CaptureReferenceFrame("project-files-02-moved");
            }
        });
    }

    private static void CheckEditorProjectFileMission()
    {
        bool renamed = false, moved = false;
        CheckCatalogMission("code", 0, "Переименовать файл и переместить его по проекту", (step, prompt) =>
        {
            var witness = BrowserWitness(prompt == "result" ? "LabResult" : "LabDesktop");
            if (prompt == "result")
            {
                Check(renamed && moved && witness[0] == "code" && witness[1].Contains("/project/reports/") &&
                    witness[1].EndsWith(".csv", StringComparison.Ordinal), "VS Code reopens the moved report in reports");
                CaptureReferenceFrame("editor-file-03-result");
            }
            else if (!renamed && witness[0] == "code" && witness[2] == "file-name")
            {
                EnterCharacters("draft"); Key("Enter");
                Until(delegate { return BrowserWitness("LabDesktop")[2] == "file-name"; }, "duplicate name keeps rename open");
                Check(ReadSolved() == step, "An existing filename does not advance the exercise");
                Key("Esc");
                Until(delegate { return BrowserWitness("LabDesktop")[2] == "explorer"; }, "Esc returns to the VS Code tree");
                Check(ReadSolved() == step, "Cancelling rename keeps the current goal");
                Key("F2");
                Until(delegate { return BrowserWitness("LabDesktop")[2] == "file-name"; }, "F2 reopens the original item");
                renamed = true;
                CaptureReferenceFrame("editor-file-01-rename");
            }
            else if (!moved && witness[6].Contains("/project/reports/") && witness[6].Contains(".csv"))
            {
                moved = true;
                CaptureReferenceFrame("editor-file-02-moved");
            }
        });
    }

    private static string[] BrowserWitness(string id)
    {
        var values = Find(id).Current.ItemType.Split('|');
        Check(values.Length == 9, "Actual scene reports browser/clone state");
        return values;
    }

    private static void CheckBrowserClone()
    {
        const string repo = "https://github.com/veitnemed/touch_typing";
        const string decoy = "https://practice.local/tutorials/touch-typing";
        uint owner;
        uint thread = GetWindowThreadProcessId(trainer, out owner);
        Check(owner == (uint)app.Id, "Set language only on the owned WPF lesson");
        SetNativeLessonLayout(thread, "00000409", 0x0409);
        OpenLiveConfiguration("chrome");
        Select("LabMode", 1); Select("LabLevel", 0); Select("LabDuration", 0); Select("LabTimer", 0);
        SelectMissionByName("Найти репозиторий и открыть учебную копию");
        Key("F5");
        Until(delegate { return Visible("LabTask") && Visible("LabDesktop"); }, "new standalone Chrome practice");
        CheckVerticalPrompt();
        Check(BrowserWitness("LabDesktop")[1] == "https://www.google.com/", "Authored Google home is visible");
        CaptureReferenceFrame("browser-clone-01-google");
        bool indexed = false, back = false, bookmarked = false, reopened = false, copied = false, pasted = false, cloned = false;
        string search = null;
        int solved = 0;
        while (!Visible("LabResult") && solved < 35)
        {
            string instruction = Find("LabTask").Current.ItemStatus;
            Check(ReadSolved() == solved, "Each action earns one credit");
            if (instruction.StartsWith("text:", StringComparison.Ordinal))
            {
                if (!indexed)
                {
                    Check(BrowserWitness("LabDesktop")[2] == "url", "Browser has focused its address field");
                    EnterCharacters("abcdefghij"); Key("Left");
                    Stroke heldBackspace = Parse("Backspace").Single();
                    // Three full native keydown messages with no key-up between
                    // them, just as typematic enters the low-level WPF hook.
                    Send(new[] { heldBackspace.Down(), heldBackspace.Down(), heldBackspace.Down(), heldBackspace.Up() });
                    Until(delegate { return BrowserWitness("LabDesktop")[3] == "abcdefj"; }, "the address after held Backspace");
                    Check(ReadSolved() == solved, "Held Backspace deletes 3 characters before the address caret without credit");
                    CaptureReferenceFrame("browser-clone-01a-held-backspace");
                    Chord("Ctrl+A");
                }
                EnterCharacters(instruction.Substring(5)); Key("Enter");
            }
            else
            {
                Check(instruction.StartsWith("keys:", StringComparison.Ordinal), "Lesson exposes physical chord");
                string keys = instruction.Substring(5);
                int tries = keys == "Tab" || keys == "Down" ? 10 : 1;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    Chord(keys);
                    if (Visible("LabResult") || ReadSolved() > solved) break;
                }
            }
            int completed = solved + 1;
            Until(delegate { return Visible("LabResult") || ReadSolved() == completed; }, "credit for " + instruction);
            solved = completed;
            if (Visible("LabResult")) break;
            var scene = BrowserWitness("LabDesktop");
            if (!indexed && scene[1].StartsWith("https://www.google.com/search?q=", StringComparison.Ordinal))
            {
                search = scene[1];
                Check(scene[6].Contains(repo) && scene[6].Contains(decoy), "Query-specific results include the target and distractor");
                indexed = true;
                CaptureReferenceFrame("browser-clone-02-results");
            }
            if (instruction == "keys:Alt+Left")
            {
                Check(scene[1] == search && scene[8] == "0", "Back restores the prior precise query and selected result");
                back = true;
            }
            if (instruction == "keys:Ctrl+D")
            {
                Check(scene[1] == repo && scene[7].Split(';').Contains(repo), "Actual repository is saved to bookmark store");
                bookmarked = true;
                CaptureReferenceFrame("browser-clone-03-bookmarked");
            }
            if (instruction == "keys:Ctrl+Shift+O")
            {
                Check(scene[1] == "chrome://bookmarks" && scene[6].Split(';').Contains(repo), "Bookmark manager lists saved repository");
                CaptureReferenceFrame("browser-clone-04-bookmarks");
            }
            if (bookmarked && instruction == "keys:Enter" && scene[1] == repo)
                reopened = true;
            if (instruction == "keys:Ctrl+C")
            {
                Check(reopened && scene[4] == repo, "Reopened bookmark is copied as the original HTTPS URL");
                copied = true;
            }
            if (instruction == "keys:Ctrl+V")
            {
                Check(copied && scene[0] == "code" && scene[2] == "clone-url" && scene[3] == repo, "Paste gives simulated Git: Clone the actual clipboard URL");
                pasted = true;
                CaptureReferenceFrame("browser-clone-05-clone-url");
            }
            if (scene[5] == "touch_typing" && !cloned)
            {
                Check(pasted, "Explorer displays the local copy only after the URL was pasted");
                cloned = true;
                CaptureReferenceFrame("browser-clone-06-project");
            }
        }
        Check(indexed && back && bookmarked && reopened && copied && pasted && cloned, "All cross-app browser route stages observed");
        var result = BrowserWitness("LabResult");
        Check(result[1] == "/home/student/projects/touch_typing/README.md" && Name("LabResult").Length > 0 &&
            Find("LabResult").Current.HelpText.Contains("Ошибки 0."), "Full route finishes at the virtual clone's README without errors");
        CaptureReferenceFrame("browser-clone-07-result");
    }

    private static void CheckResearchHandoff()
    {
        Invoke("LabChooseClass");
        Until(delegate { return Visible("HomeDashboard"); }, "return to Live from clone result");
        Invoke("LabDesktopProfile-windows");
        Until(delegate { return Exists("HomeClass-windows"); }, "select the Windows 10 simulation");
        OpenLiveConfiguration("workday");
        Select("LabMode", 0); Select("LabLevel", 1); Select("LabDuration", 0); Select("LabTimer", 0);
        SelectMissionByName("Windows 10: перенести проверенный отчёт в проект");
        Key("F5");
        Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, "new Windows Workday");
        bool search = false, report = false, files = false, opened = false, matched = false;
        for (int step = 1; step <= 35 && !Visible("LabResult"); step++)
        {
            string prompt = Find("LabTask").Current.ItemStatus;
            Console.WriteLine("RESEARCH " + step + " " + Name("LabTask") + " | " + prompt);
            if (prompt.StartsWith("text:"))
            {
                string expectedText = prompt.Substring(5);
                EnterCharacters(expectedText);
                var input = BrowserWitness("LabDesktop");
                bool searchInput = (input[0] == "chrome" && input[2] == "url") ||
                    (input[0] == "code" && input[2] == "find");
                Check(searchInput && input[3] == expectedText, "Exact input before Enter in " + input[0] + "/" +
                    input[2] + " (received: " + input[3] + ")");
                Key("Enter");
            }
            else
            {
                Check(prompt.StartsWith("keys:"), "Report prompt exposes a real key chord");
                string keys = prompt.Substring(5);
                for (int i = 0; i < (keys == "Tab" || keys == "Down" ? 15 : 1); i++)
                {
                    Chord(keys);
                    if (Visible("LabResult") || ReadSolved() >= step) break;
                }
            }
            int expected = step;
            Until(delegate { return Visible("LabResult") || ReadSolved() == expected; }, "report handoff action " + prompt);
            if (Visible("LabResult")) break;
            var active = BrowserWitness("LabDesktop");
            if (!search && active[1].StartsWith("https://www.google.com/search?q=", StringComparison.Ordinal))
            { search = true; CaptureReferenceFrame("research-01-results"); }
            if (!report && active[1].StartsWith("https://practice.local/workshop/", StringComparison.Ordinal))
            { report = true; CaptureReferenceFrame("research-02-workshop"); }
            if (!files && active[0] == "files" && active[6].Contains("/Downloads/") && active[6].Contains(".csv"))
            { files = true; CaptureReferenceFrame("research-03-explorer"); }
            if (!opened && active[0] == "code" && active[1].StartsWith("/home/student/project/", StringComparison.Ordinal) && active[1].EndsWith(".csv"))
            { opened = true; CaptureReferenceFrame("research-04-opened-csv"); }
            if (opened && prompt.StartsWith("text:") && active[2] == "find")
            { matched = true; CaptureReferenceFrame("research-05-found-record"); }
        }
        Check(search && report && files && opened && matched && Visible("LabResult") &&
            BrowserWitness("LabResult")[1].StartsWith("/home/student/project/", StringComparison.Ordinal),
            "Report is searched, moved through Windows Explorer, and checked in the destination CSV");
        CaptureReferenceFrame("research-06-result");
    }

    private static bool ProbeNumLock() { return (GetKeyState(0x90) & 1) != 0; }
    private static void SetProbeNumLock(bool enabled)
    {
        Check(GetForegroundWindow() == focusProbe, "Num Lock changes occur only in the owned QA probe.");
        if (ProbeNumLock() != enabled)
        {
            // Toggle the lock with its VK (the keyboard state action), not
            // a raw E0 scan. The navigation stroke under test remains scan-based.
            var input = new[] {
                new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x90 } } },
                new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = 0x90, Flags = 2 } } }
            };
            if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Input))) != input.Length)
                throw new InvalidOperationException("Cannot toggle Num Lock in the owned QA probe.");
            Wait(150);
        }
        Until(delegate { return ProbeNumLock() == enabled; }, "Windows Num Lock state " + enabled);
        Console.WriteLine("NUMLOCK=" + (enabled ? "ON" : "OFF"));
    }

    private static void CheckWorkdaySourceNote(string numLockMode)
    {
        string qaRoot = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT");
        Check(!string.IsNullOrEmpty(qaRoot) && Path.IsPathRooted(qaRoot), "Use an explicit disposable QA profile");
        string profile = Path.Combine(qaRoot, "TouchTypingStudio-QA", "progress.xml");
        Check(!File.Exists(profile), "The Workday project starts with a fresh profile");

        OpenLiveConfiguration("workday");
        // Inspect the suggested physical key in the explicitly guided mode.
        // An independent lesson must never reveal the answer merely via F4.
        Select("LabMode", 1);
        Select("LabLevel", 0);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        string expectedMission = numLockMode.Length > 0 ? "Доставить отчёт и проверить правку" :
            "Перенести методику из браузера в заметку";
        SelectMissionByName(expectedMission);
        Key("F5");
        Until(delegate { return Exists("LabTask") && Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "active standalone Workday source-note project");

        var range = (RangeValuePattern)Find("LabScenarioProgress").GetCurrentPattern(RangeValuePattern.Pattern);
        int total = (int)range.Current.Maximum;
        Check(total >= 20 && total <= 65, "Scenario exposes its full actionable route size");
        Check(Find("LabTask").Current.ItemType.Contains(expectedMission), "The selected project uses the correct route");

        bool foundSource = false, copied = false, pasted = false, saved = false, reopened = false, submitted = false;
        for (int step = 1; step <= total; step++)
        {
            Check(ReadSolved() == step - 1, "No missing or duplicate credit before step " + step);
            var task = Find("LabTask");
            string title = task.Current.Name, instruction = task.Current.ItemStatus;
            Console.WriteLine("WORKDAY TASK " + step + " " + title + " | " + instruction);
            string activeBefore = Name("LabDesktop");
            bool opensSource = title == "Откройте «Методика расчёта спектра»" || title == "Откройте «Как вести список задач»";
            if (opensSource)
                Check(activeBefore.Contains("Chrome."), "The simulated browser is active before opening the source");

            int before = step - 1;
            if (title == "Откройте «notes.txt»" && activeBefore.Contains("Файлы."))
                CaptureReferenceFrame("workday-03-file-manager-before-open");
            if (instruction.StartsWith("keys:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string gesture = instruction.Substring(5);
                bool keypad = gesture == "Alt+Home" && numLockMode.Length > 0;
                if (keypad && numLockMode != "dedicated")
                {
                    CheckFocusLoss(delegate
                    {
                        if (!originalNumLock.HasValue) originalNumLock = ProbeNumLock();
                        SetProbeNumLock(numLockMode == "on");
                    });
                }
                if (keypad)
                {
                    Check(Visible("LabKeys"), "Explicit guided mode shows the Alt+Home cue");
                    Check(!Visible("LiveAidPanel"), "Default aid stays hidden in the Workday run");
                    Key("F4");
                    Until(delegate { return Visible("LiveAidPanel"); }, "F4 reveals the real keyboard rail");
                    CaptureReferenceFrame("workday-pad7-home");
                    var aid = Find("LiveAidPanel").Current;
                    Console.WriteLine("MateBook visible keyboard: " + aid.Name + "; " + aid.ItemStatus + "; " + aid.HelpText);
                    Check(aid.Name.Contains("7 / Home (NumPad7)"), "Visible MateBook key points to 7/Home on the number pad");
                    Chord("Alt+7");
                    Check(ReadSolved() == before, "Top-row Alt+7 does not satisfy Alt+Home");
                }
                var scan = Parse(keypad && numLockMode != "dedicated" ? "Alt+NumPad7" : gesture);
                var input = scan.Select(k => k.Down()).Concat(scan.Reverse().Select(k => k.Up())).ToArray();
                int tries = gesture == "Tab" || gesture == "Down" ? 24 : 1;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    Send(input);
                    Wait(110);
                    if (ReadSolved() > before) break;
                }
                Until(delegate { return ReadSolved() > before; }, "native " + gesture + " credit at step " + step);
                if (keypad)
                {
                    string trace = Find("LabStatus").Current.ItemStatus;
                    Console.WriteLine("WORKDAY KEYPAD NumLock=" + numLockMode + " " + trace);
                    bool dedicated = numLockMode == "dedicated";
                    Check(trace.Contains("scan=47") && trace.Contains("extended=" + dedicated) && trace.Contains("LAlt"),
                        "Workday credit came from the targeted 0x47 scan with actual Alt held");
                    Check(ReadSolved() == before + 1, "Alt+Home earns exactly one credit");
                    CaptureReferenceFrame("workday-keypad-numlock-" + numLockMode);
                    return;
                }
                if (numLockMode.Length > 0) continue;
                if (opensSource) foundSource = true;
                if (gesture == "Ctrl+C")
                {
                    Check(foundSource && activeBefore.Contains("Chrome."), "Ctrl+C copies from the opened browser document");
                    copied = true;
                    CaptureReferenceFrame("workday-01-source-in-browser");
                }
                if (gesture == "Ctrl+V")
                {
                    Check(copied && activeBefore.Contains("Visual Studio Code."), "Ctrl+V pastes from the browser into the editor");
                    pasted = true;
                    CaptureReferenceFrame("workday-02-editor-after-paste");
                }
                if (gesture == "Ctrl+S")
                {
                    Check(pasted && Name("LabDesktop").Contains("Файл «notes.txt» сохранён"), "Ctrl+S saves the pasted note in the project");
                    saved = true;
                }
                if (title == "Откройте «notes.txt»" && activeBefore.Contains("Файлы."))
                {
                    Check(saved && Name("LabDesktop").Contains("Visual Studio Code."), "File manager reopens notes.txt in the editor");
                    reopened = true;
                }
            }
            else if (instruction.StartsWith("text:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string command = instruction.Substring(5);
                Check(activeBefore.Contains("Терминал.") && activeBefore.Contains("Раскладка EN"), "Command target is the active English terminal");
                foreach (char c in command) Send(Character(c));
                Until(delegate { return Find("LabDesktop").Current.HelpText == command; }, "the full command reached the simulated terminal");
                if (step == total)
                {
                    Check(reopened && command == "cat notes.txt", "The final native Enter submits cat notes.txt after save and reopen");
                    CaptureReferenceFrame("workday-04-terminal-before-readback");
                    Key("Enter");
                    Until(delegate { return Exists("LabResult"); }, "completed Workday result");
                    submitted = true;
                }
                else
                {
                    Key("Enter");
                    Until(delegate { return ReadSolved() > before; }, "native command credit at step " + step);
                }
            }
            else throw new InvalidOperationException("Missing native input for Workday: " + title + " [" + instruction + "]");
        }
        Check(numLockMode.Length == 0, "The Workday route must actually reach Alt+Home for the keypad witness");
        Check(foundSource && copied && pasted && saved && reopened && submitted, "Browser, clipboard, editor, files and terminal all performed their part");
        string summary = Find("LabResult").Current.HelpText;
        Check(summary.Contains("Выполнено " + total + ".") && summary.Contains("Ошибки 0.") && summary.Contains("Цель занятия выполнена"),
            "The visible result confirms a completed error-free project");
        var xml = new XmlDocument();
        xml.XmlResolver = null;
        xml.Load(profile);
        var rows = xml.SelectNodes("/StudioProfile/ShortcutHistory/ShortcutAttempt[Mission='workday-source-note']");
        Check(rows.Count == total, "The completed profile has exactly one record per native action");
        var skills = rows.Cast<XmlNode>().Select(row => row.SelectSingleNode("Skill").InnerText).ToArray();
        int copy = Array.IndexOf(skills, "lab/workday/copy");
        int paste = Array.IndexOf(skills, "lab/workday/paste");
        int save = Array.IndexOf(skills, "lab/workday/save");
        int cat = Array.IndexOf(skills, "lab/workday/command/cat notes.txt");
        Check(0 <= copy && copy < paste && paste < save && save < cat && cat == total - 1,
            "The persisted credits prove copy, paste, save and strict cat verification in order");
        Check(xml.SelectSingleNode("/StudioProfile/LiveDrafts/LiveDraft[Mission='workday-source-note']") == null,
            "Completed Workday project leaves no unfinished draft");
    }

    private static void CheckSoftFinish(string path)
    {
        OpenLiveConfiguration("terminal");
        Select("LabMode", 0);
        Check(Exists("LabDuration") && Exists("LabTimer"), "Action limit and timer are independent controls");
        Select("LabDuration", 4); // 100 actions, alongside the timer.
        Select("LabTimer", 1); // QA-only seven-second clock cap.
        Key("F5");
        Until(delegate { return Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, "active terminal lesson");
        Check(Regex.IsMatch(Name("LabClock"), @"^00:0[0-7]$") &&
            Find("LabClock").Current.HelpText.Contains("0 из 100"), "Only the timer is visual; the action cap remains accessible");
        VirtualEnglish();
        Chord("Ctrl+U");
        foreach (var character in "abcde") Send(Character(character));
        Key("Home"); Key("Right"); Key("Right");
        CheckTerminalState("abcde", 2);
        Until(delegate { return Visible("LabLimitChoice"); }, "actual engine deadline", 14000);
        Check(Name("LabLimitChoice") == "Время вышло" && Name("LabClock") == "00:00",
            "Timer wins and both budgets remain visible at the choice");
        Check(!Visible("LabResult"), "Deadline offers a choice, not a result");
        CheckTerminalState("abcde", 2);
        Key("Delete"); Key("F10"); Key("Enter"); Key("Space");
        Check(Visible("LabLimitChoice"), "Background exercise shortcuts do not escape the modal");
        CheckTerminalState("abcde", 2);

        RequestClose();
        Until(delegate { return ExitDialog() != null; }, "save and close dialog");
        InvokeDialog("LabExitSave");
        Check(app.WaitForExit(6000) && app.ExitCode == 0, "Saving the capped draft closes the trainer normally");
        var profile = Path.Combine(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT"),
            "TouchTypingStudio-QA", "progress.xml");
        var xml = new XmlDocument();
        xml.XmlResolver = null;
        xml.Load(profile);
        Check(xml.SelectSingleNode("/StudioProfile/LiveDrafts/LiveDraft[App='terminal' and Progress/LimitDecisionPending='true' and Progress/TimeLimit='7' and Progress/ActionLimit='100' and Progress/LimitReached='timer']") != null &&
            xml.SelectSingleNode("/StudioProfile/LabSessions/LabSessionRecord[App='terminal']") == null,
            "A capped draft survives closing without a premature session result");
        app.Dispose();
        app = Process.Start(new ProcessStartInfo(path, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(path) });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "reopened trainer window", 12000);
        window = AutomationElement.FromHandle(trainer);
        Focus();
        OpenLiveConfiguration("terminal");
        Select("LabMode", 0);
        SelectMissionByName("Продолжить с сохранённого места");
        Check(!Find("LabDuration").Current.IsEnabled && !Find("LabTimer").Current.IsEnabled,
            "A resumed attempt does not offer to overwrite its budgets");
        Key("F5");
        Until(delegate { return Visible("LabLimitChoice"); }, "recovered deadline decision");
        Check(Name("LabLimitChoice") == "Время вышло" && Name("LabClock") == "00:00",
            "Recovered attempt displays its saved pair and trigger, not default selectors");
        CheckTerminalState("abcde", 2);
        Until(delegate { return AutomationElement.FocusedElement.Current.AutomationId == "LabLimitChoice"; }, "limit dialog starts with neither choice selected");
        Key("Enter");
        Check(Visible("LabLimitChoice"), "Enter on neutral focus does not accept a resumed choice");
        Key("Tab");
        Key("Tab");
        Check(AutomationElement.FocusedElement.Current.AutomationId == "LabLimitFinish", "Tab stays inside the limit choice");
        Key("Tab");
        var activation = Parse("Space")[0];
        Send(new[] { activation });
        Wait(550);
        Check(Visible("LabLimitChoice"), "Holding Space does not accept the choice before physical release");
        Send(new[] { activation.Up() });
        Until(delegate { return !Visible("LabLimitChoice") && Name("LabPause").StartsWith("Пауза"); }, "extension regains keyboard");
        CheckTerminalState("abcde", 2);
        Key("Delete");
        CheckTerminalState("abde", 2);
        Key("F8");
        Until(delegate { return Visible("LabExtensionPause"); }, "extension pause visibly exposes both choices");
        Key("Tab"); Key("Space");
        Until(delegate { return !Visible("LabExtensionPause"); }, "extension resumes by keyboard");
        Key("F10");
        Until(delegate { return Visible("LabResult"); }, "explicit finish accepts one result");

        xml.Load(profile);
        Check(xml.SelectNodes("/StudioProfile/LabSessions/LabSessionRecord[App='terminal' and TimeLimit='7' and ActionLimit='100']").Count == 1,
            "One final session persisted across close, continuation, and explicit finish");
        Check(xml.SelectSingleNode("/StudioProfile/LiveDrafts/LiveDraft[App='terminal']") == null,
            "No resumable parallel attempt after explicit finish");
    }

    private static void StartPythonWitness(string mission, int level)
    {
        if (Visible("LabPause") && !Visible("LabResult"))
        {
            Key("F10");
            Until(delegate { return Visible("LabResult"); }, "previous Python lesson ended");
        }
        if (Visible("LabChooseClass"))
        {
            Invoke("LabChooseClass");
            Until(delegate { return Exists("HomeDashboard"); }, "Python lesson catalog");
        }
        OpenLiveConfiguration("code"); Select("LabMode", 0); Select("LabLevel", level);
        Select("LabDuration", 0); Select("LabTimer", 0);
        SelectMissionByName(mission);
        Key("F5");
        Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза") &&
            EditorWitness().Length == 9; }, mission + " active in owned WPF editor");
        VirtualEnglish();
    }

    private static string[] EditorWitness()
    {
        return Find("LabDesktop").Current.ItemType.Split(';');
    }

    private static string[] FinishedEditorWitness(int solved)
    {
        Until(delegate { return Visible("LabResult"); }, "verified Python editing result");
        string result = Find("LabResult").Current.ItemType;
        string exact = solved.ToString(CultureInfo.InvariantCulture) + ":";
        Check(result.StartsWith(exact, StringComparison.Ordinal), "Result records exactly " + solved + " credited steps");
        return result.Substring(exact.Length).Split(';');
    }

    private static void EnterCharacters(string text)
    {
        // SendInput is asynchronous. Pace consecutive Shift-up / Shift-down
        // pairs so the low-level hook observes both on Windows Server.
        foreach (char c in text) { Send(Character(c)); Wait(18); }
    }

    private static void WaitEditorState(int solved, string focus, string file, int anchor, int caret)
    {
        Until(delegate {
            var view = EditorWitness();
            return ReadSolved() == solved && view.Length == 9 && view[0] == focus && view[1] == file &&
                view[2] == anchor.ToString(CultureInfo.InvariantCulture) && view[3] == caret.ToString(CultureInfo.InvariantCulture);
        }, "exact editor state " + solved + "/" + focus + "/" + file + "/" + anchor + "/" + caret);
    }

    private static bool ExactEditorTab(string[] view, string file, string body, string saved)
    {
        return view.Length == 9 && view[8].Split('|').Contains(file + "," + body + "," + saved + "," + saved);
    }

    private static void CheckPythonEditingMissions()
    {
        // Frozen from linux/tests/test_python_editor.py STEPS. Each witness is
        // allowed to differ only after the actual physical input reaches WPF.
        string[][] steps = {
            new[] { "Enter", "fea9d979447f8e96e7bf217f5890d3d9449f17095e8e1f722be0d5c8e4990da7", "160", "160" },
            new[] { "<write>", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "196", "196" },
            new[] { "Left", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "195", "195" },
            new[] { "Ctrl+Enter", "5c32d4f7251980b21d36dfa97be090c5907449a1b1e72fa502e36bf5e387e130", "205", "205" },
            new[] { "Ctrl+Z", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "195", "195" },
            new[] { "Ctrl+Shift+Enter", "de1c6e0e8647aa04a2c29965c3569013f259f1b821f5aad5ac38a652abad6e6a", "160", "160" },
            new[] { "Ctrl+Z", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "195", "195" },
            new[] { "Up", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "151", "151" },
            new[] { "Up", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "130", "130" },
            new[] { "Up", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "118", "118" },
            new[] { "Up", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "81", "81" },
            new[] { "End", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "106", "106" },
            new[] { "Ctrl+Shift+Left", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "106", "98" },
            new[] { "Ctrl+Backspace", "5d941dfab1695be44f1e22b1f3e9b81cd2767634f622c870b365a541c55e5eb0", "98", "98" },
            new[] { "Ctrl+Z", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "106", "98" },
            new[] { "Ctrl+Right", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "106", "106" },
            new[] { "Ctrl+Backspace", "5d941dfab1695be44f1e22b1f3e9b81cd2767634f622c870b365a541c55e5eb0", "98", "98" },
            new[] { "Ctrl+Z", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "106", "106" },
            new[] { "Ctrl+Left", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "98", "98" },
            new[] { "Ctrl+Delete", "5d941dfab1695be44f1e22b1f3e9b81cd2767634f622c870b365a541c55e5eb0", "98", "98" },
            new[] { "Ctrl+Z", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "98", "98" },
            new[] { "Ctrl+Shift+Right", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "98", "106" },
            new[] { "Home", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "70", "70" },
            new[] { "Shift+Down", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "70", "107" },
            new[] { "Shift+Down", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "70", "119" },
            new[] { "Tab", "78a06063a7a66c0e540719b7c732cfa8be0a3e2b9ba71226d49563640cb203b8", "70", "127" },
            new[] { "Shift+Tab", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "70", "119" },
            new[] { "Alt+Up", "2a3faf31805a286551e2a67c9415f8be6b57175a696e5bd7c85e72ea4bf8f782", "48", "97" },
            new[] { "Alt+Down", "d49e26e0d0737352fa7bdedc80e061320e9406d39047f57f04b46db4679e0578", "70", "119" },
            new[] { "Ctrl+Z", "2a3faf31805a286551e2a67c9415f8be6b57175a696e5bd7c85e72ea4bf8f782", "48", "97" },
            new[] { "Ctrl+S", "2a3faf31805a286551e2a67c9415f8be6b57175a696e5bd7c85e72ea4bf8f782", "48", "97" },
        };
        const string initial = "e965a11277e2c78d641695c322f750242528b6b6df0895a9bb957dc86b922ceb";
        StartPythonWitness("Отредактировать Python-функцию без мыши", 1);
        Check(ExactEditorTab(EditorWitness(), "tasks.py", initial, initial), "Untouched tasks.py backing file at first launch");
        for (int i = 0; i < steps.Length; i++)
        {
            int step = i + 1;
            var row = steps[i];
            if (row[0] == "<write>")
            {
                const string statement = "cost = subtotal + fee + tax + marker";
                EnterCharacters(statement.Substring(0, statement.Length - 1));
                Check(ReadSolved() == step - 1, "Incomplete Python statement cannot earn credit");
                EnterCharacters(statement.Substring(statement.Length - 1));
            }
            else Chord(row[0]);
            if (step < steps.Length)
                WaitEditorState(step, "body", "tasks.py",
                    int.Parse(row[2], CultureInfo.InvariantCulture), int.Parse(row[3], CultureInfo.InvariantCulture));
            var view = step == steps.Length ? FinishedEditorWitness(step) : EditorWitness();
            Check(view.Length == 9 && view[1] == "tasks.py" && view[2] == row[2] && view[3] == row[3] &&
                ExactEditorTab(view, "tasks.py", row[1], step == steps.Length ? row[1] : initial),
                "Python editing step " + step + " " + row[0] + " proves exact buffer, selection and backing file");
            if (step == 2 || step == 13 || step == 26 || step == 31)
                CaptureReferenceFrame("python-edit-" + step.ToString("00", CultureInfo.InvariantCulture));
        }

        CheckPythonSearchMission();
        CheckNeutralPythonSymbols();
    }

    private static string NativeFixtureHash(string text)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                .Replace("-", "").ToLowerInvariant();
    }

    private static void CheckPythonSearchMission()
    {
        const string original = "from tasks import add_task\n\nadd_task(\"draft\", \"notes\")\nadd_task(\"draft\", \"report\")\nadd_task(\"draft\", \"archive\")\n";
        var hits = Regex.Matches(original, "draft").Cast<Match>().Select(match => match.Index).ToArray();
        string before = NativeFixtureHash(original);
        string after = NativeFixtureHash(original.Replace("draft\", \"report", "ready\", \"report"));
        StartPythonWitness("Найти и исправить нужное место", 0);
        Check(EditorWitness()[1] == "tasks.py", "Exact search starts outside app.py");
        Chord("Ctrl+P");
        Until(delegate { return ReadSolved() == 1 && EditorWitness()[0] == "quick-open"; }, "Ctrl+P opens real quick file entry");
        EnterCharacters("ap"); Key("Enter");
        WaitEditorState(2, "body", "app.py", 0, 0);
        Check(ExactEditorTab(EditorWitness(), "app.py", before, before), "Fuzzy ap opened exact unmodified app.py");
        Chord("Ctrl+F"); EnterCharacters("absent"); Key("Enter");
        Until(delegate { return ReadSolved() == 3 && Name("LabDesktop").Contains("Совпадений нет"); }, "No credit for query with zero matches");
        for (int i = 0; i < "absent".Length; i++) Key("Backspace");
        EnterCharacters("draft"); Key("Enter");
        WaitEditorState(4, "find", "app.py", hits[0], hits[0] + 5);
        int[] sequence = { 1, 2, 0, 2, 1 };
        for (int i = 0; i < sequence.Length; i++)
        {
            Chord(i < 3 ? "F3" : "Shift+F3");
            int hit = hits[sequence[i]];
            WaitEditorState(5 + i, "find", "app.py", hit, hit + 5);
        }
        CaptureReferenceFrame("python-search-report-hit");
        Key("Esc");
        WaitEditorState(10, "body", "app.py", hits[1], hits[1] + 5);
        EnterCharacters("ready");
        Check(ExactEditorTab(EditorWitness(), "app.py", after, before), "Only the target selection changes; disk stays untouched before save");
        Chord("Ctrl+S");
        Until(delegate { return ReadSolved() == 11 && ExactEditorTab(EditorWitness(), "app.py", after, after); }, "Saved only the report status");
        Key("Home");
        Until(delegate { return ReadSolved() == 12; }, "Home moves to the exact call site");
        Key("F12");
        Until(delegate { return ReadSolved() == 13 && EditorWitness()[1] == "tasks.py"; }, "F12 follows imported add_task");
        Chord("Alt+Left");
        var final = FinishedEditorWitness(14);
        int call = hits[1] - 10; // "add_task(" plus the opening quote.
        Check(final[0] == "body" && final[1] == "app.py" && final[2] == call.ToString(CultureInfo.InvariantCulture) &&
            final[3] == final[2] && ExactEditorTab(final, "app.py", after, after),
            "Returns to the exact report call site and completes the search-edit route");
    }

    private static void CheckNeutralPythonSymbols()
    {
        const string original = "f5f482eb437481c7f209fb5409715ec29f12cce8076227c8ebaddfdade536115";
        StartPythonWitness("Составить заметку по проекту", 0);
        WaitEditorState(0, "body", "app.py", 0, 0);
        Check(ExactEditorTab(EditorWitness(), "app.py", original, original), "Neutral app.py matches the reference fixture");
        for (int i = 0; i < 8; i++) Key("Down");
        Key("Home");
        for (int i = 0; i < 12; i++) Key("Right");
        WaitEditorState(0, "body", "app.py", 204, 204);
        Key("F12");
        WaitEditorState(0, "body", "tasks.py", 179, 179);
        Chord("Alt+Left");
        WaitEditorState(0, "body", "app.py", 204, 204);
        Key("F2");
        Until(delegate { var view = EditorWitness(); return view[0] == "symbol-rename" && view[6] == "find_task"; },
            "F2 targets imported find_task");
        EnterCharacters("fetch_task"); Key("Esc");
        Check(ExactEditorTab(EditorWitness(), "app.py", original, original), "Escape cancels the rename without edits or writes");
        Key("F2"); EnterCharacters("fetch_task"); Key("Enter");
        var result = EditorWitness();
        Check(ExactEditorTab(result, "app.py", "a5ae49896990a9c7ba85228098544a17738003bc75000715c5805766b5dc2555", original) &&
            ExactEditorTab(result, "tasks.py", "0152ab7c4e7deb72d5837e81fb23afeb0baa92967458030c43e82b7ef2e6e813",
                           "570d2abddfda7dd526adb10fbbd47404a8e0608aa5dc4fca8f31bb5b3978750e") &&
            ExactEditorTab(result, "test_tasks.py", "53b19ec67ede50d6f55e651825b1efabd6205e33418a4fad255ef1f6e43c28da",
                           "e038ee1a0b8342652f49b73aca9289b1153429f53f45e417379335af4a9582cf"),
            "F2 updates the import, definition and real calls across three neutral project buffers, without saving");
        CaptureReferenceFrame("python-neutral-rename");
    }

    private static void CheckCodeTheme()
    {
        Check(GetDpiForWindow(trainer) == 96, "Normal Windows laptop scale: 100% / 96 DPI");
        string exactClient = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_CODE_CLIENT");
        if (!string.IsNullOrEmpty(exactClient))
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
                throw new InvalidOperationException("Exact editor sizing requires a disposable hosted runner.");
            var dimensions = exactClient.Split('x');
            int width, height;
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out width) || !int.TryParse(dimensions[1], out height) ||
                width < 640 || width > 3840 || height < 480 || height > 2160)
                throw new InvalidOperationException("Invalid exact editor client dimensions.");
            ResizeOwnedEditorClient(width, height);
            expectedCodeClient = new Size(width, height);
        }
        if (Visible("LabChooseClass"))
        {
            Invoke("LabChooseClass");
            Until(delegate { return Exists("HomeDashboard"); }, "editor chooser");
        }
        OpenLiveConfiguration("code"); Select("LabMode", 0); Select("LabLevel", 0);
        Select("LabDuration", 0); Select("LabTimer", 0);
        SelectMissionByName("Составить заметку по проекту");
        Key("F5");
        Func<AutomationElement.AutomationElementInformation> scene = () => Find("LabDesktop").Current;
        Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза") &&
            scene().HelpText.EndsWith("app.py") && scene().ItemStatus == "editor:body:0"; },
            "real neutral app.py in editor at the first character");
        CaptureReferenceFrame("editor-theme-01-open");
        using (var opening = CaptureEditorViewport("editor-theme-01-open"))
            Check(opening.Width >= 420, "Editor viewport captured at its actual size");

        for (int i = 0; i < 7; i++) Key("Down");
        Key("Home");
        for (int i = 0; i < 4; i++) Key("Right");
        Until(delegate { return scene().ItemStatus == "editor:body:149"; }, "app.py line 8 column 5");
        CaptureReferenceFrame("editor-theme-02-active-line");
        using (var before = CaptureEditorViewport("editor-theme-02-active-line"))
        {
            for (int i = 0; i < 8; i++) Chord("Shift+Right");
            Until(delegate { return scene().ItemStatus == "editor:body:157"; }, "selection endpoint line 8 column 13");
            CaptureReferenceFrame("editor-theme-03-add-task-selected");
            using (var after = CaptureEditorViewport("editor-theme-03-add-task-selected"))
            {
                const int row = 92 + 7 * 20;
                Check(before.Width == after.Width && before.Height == after.Height && after.Height > row + 20,
                    "Editor evidence uses the same viewport at native 1:1 pixels");
                int selectedPixels = 0, activePixels = 0;
                for (int y = row; y < row + 18; y++)
                    for (int x = 0; x < after.Width; x++)
                    {
                        Color pixel = after.GetPixel(x, y);
                        if (NearColor(pixel, 38, 79, 120, 2) && !NearColor(before.GetPixel(x, y), 38, 79, 120, 2)) selectedPixels++;
                        if (NearColor(before.GetPixel(x, y), 23, 27, 34, 2)) activePixels++;
                    }
                Check(activePixels > 200 && selectedPixels > 120,
                    "Native active-line fill and selected add_task range have distinct GitHub Dark colors");
            }
        }
    }

    private static void ResizeOwnedEditorClient(int width, int height, bool fitVisibleClient = false)
    {
        Check(!app.HasExited && window.Current.ProcessId == app.Id && GetForegroundWindow() == trainer,
            "Resize only the owned foreground trainer window");
        ShowWindow(trainer, 9); // SW_RESTORE: outer dimensions are defined for the normal window.
        var screen = System.Windows.Forms.Screen.FromHandle(trainer);
        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        for (int pass = 0; pass < 4; pass++)
        {
            CaptureRect client, outer;
            if (!GetClientRect(trainer, out client) || !GetWindowRect(trainer, out outer))
                throw new InvalidOperationException("Cannot read exact editor client and outer frame.");
            int fullWidth = outer.Right - outer.Left + width - (client.Right - client.Left);
            int fullHeight = outer.Bottom - outer.Top + height - (client.Bottom - client.Top);
            int left = area.X, top = area.Y;
            if (fitVisibleClient)
            {
                var origin = new CapturePoint();
                if (!ClientToScreen(trainer, ref origin) ||
                    !area.Contains(new Rectangle(area.X, area.Y, width, height)))
                    throw new InvalidOperationException("Exact Live client does not fit in the visible work area: " + area);
                left -= origin.X - outer.Left;
                top -= origin.Y - outer.Top;
            }
            else if (!bounds.Contains(new Rectangle(left, top, fullWidth, fullHeight)))
                throw new InvalidOperationException("Exact editor client needs a larger disposable display, refusing to crop or scale.");
            if (!SetWindowPos(trainer, IntPtr.Zero, left, top, fullWidth, fullHeight, 0x0014))
                throw new InvalidOperationException("Could not size owned editor window.");
            Wait(180);
        }
        Focus();
        CaptureRect final;
        Check(GetClientRect(trainer, out final) && final.Right - final.Left == width && final.Bottom - final.Top == height &&
            GetDpiForWindow(trainer) == 96, "Physical editor client is exactly " + width + "x" + height + " @96 DPI");
        if (fitVisibleClient)
        {
            var origin = new CapturePoint();
            Check(ClientToScreen(trainer, ref origin) && area.Contains(new Rectangle(origin.X, origin.Y, width, height)),
                "All physical client pixels visible unscaled: origin=" + origin.X + "," + origin.Y + "; work area=" + area + "; display=" + bounds);
        }
    }

    private static bool NearColor(Color value, int r, int g, int b, int tolerance)
    {
        return Math.Abs(value.R - r) <= tolerance && Math.Abs(value.G - g) <= tolerance &&
            Math.Abs(value.B - b) <= tolerance;
    }

    private static Bitmap CaptureEditorViewport(string name)
    {
        if (app.HasExited || GetForegroundWindow() != trainer)
            throw new InvalidOperationException("Refusing editor crop outside the owned foreground trainer.");
        var scene = Find("LabDesktop").Current.BoundingRectangle;
        var pixels = Rectangle.FromLTRB((int)Math.Ceiling(scene.Left), (int)Math.Ceiling(scene.Top),
            (int)Math.Floor(scene.Right), (int)Math.Floor(scene.Bottom));
        if (pixels.Width < 420 || pixels.Height <= 0 ||
            !System.Windows.Forms.Screen.FromHandle(trainer).Bounds.Contains(pixels))
            throw new InvalidOperationException("The complete native editor crop is not visible.");
        using (var bitmap = new Bitmap(pixels.Width, pixels.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(pixels.X, pixels.Y, 0, 0, pixels.Size);
            if (GetForegroundWindow() != trainer)
                throw new InvalidOperationException("Trainer lost foreground during editor crop.");
            bitmap.Save(Path.Combine(evidenceDirectory, name + "-viewport.png"), ImageFormat.Png);
            Console.WriteLine("CAPTURE " + name + " editor viewport " + pixels.Width + "x" + pixels.Height +
                " physical px, DPI=" + GetDpiForWindow(trainer));
            return new Bitmap(bitmap);
        }
    }

    private static void CheckDebugDay()
    {
        for (int mode = 0; mode < 2; mode++)
        {
            bool guided = mode == 0;
            string mark = guided ? "guided" : "independent";
            string caller = guided ? "billing.py" : "clock_app.py";
            string module = guided ? "factors.py" : "duration.py";
            string report = guided ? "billing-report.md" : "clock-report.md";
            string test = guided ? "test_billing.PriceTests.test_price_in_cents" : "test_clock.DurationTests.test_to_seconds";
            if (Visible("LabChooseClass"))
            {
                Invoke("LabChooseClass");
                Until(delegate { return Exists("HomeDashboard"); }, "debug-day chooser");
            }
            OpenLiveConfiguration("workday"); Select("LabMode", 0); Select("LabLevel", 1);
            Select("LabDuration", 0); Select("LabTimer", 0);
            SelectMissionByName("Разобрать ошибку без мыши · " + (guided ? "с подсказками" : "самостоятельно"));
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, mark + " native debug-day lesson");
            VirtualEnglish();

            Chord("Super+2"); Chord("Ctrl+Shift+F");
            foreach (char c in guided ? "price_in_cents(45)" : "to_seconds(4200)") Send(Character(c));
            Key("Enter"); Key("Enter");
            Until(delegate { return Find("LabDesktop").Current.HelpText.EndsWith(caller); }, "opened correct call site");
            Key("F12");
            Until(delegate { return Find("LabDesktop").Current.HelpText.EndsWith(module); }, "opened imported definition");
            Chord("Super+1"); Chord("Ctrl+F");
            foreach (char c in guided ? "100 cents" : "1000 milliseconds") Send(Character(c));
            Key("Enter"); Key("Esc"); Chord("Super+2");
            Until(delegate { return Find("LabDesktop").Current.HelpText.EndsWith(module); }, "return to exact module");
            Key("Down"); Chord("Ctrl+L");
            foreach (char c in guided ? "    return value * 100" : "    return value / 1000") Send(Character(c));
            Key("Enter"); Chord("Ctrl+S"); Chord("Ctrl+Alt+T");
            foreach (char c in "python3 -m unittest " + test) Send(Character(c));
            Key("Enter");
            CaptureReferenceFrame("debug-" + mark + "-terminal-test");
            Chord("Alt+Print");
            Until(delegate { return Name("LabDesktop").Contains("Снимок активного терминала скопирован"); },
                "trainer received Alt+Print and produced the virtual terminal PNG");
            Chord("Super+2"); Chord("Ctrl+P"); Key("Enter"); Chord("Ctrl+End"); Chord("Ctrl+V");
            Until(delegate { return Find("LabDesktop").Current.HelpText.EndsWith(report) &&
                Name("LabDesktop").Contains("В отчёт вставлен снимок терминала"); }, "screenshot pasted into report");
            CaptureReferenceFrame("debug-" + mark + "-report-with-image");
            Chord("Ctrl+S"); Chord("Ctrl+Alt+T");
            foreach (char c in "cat " + report) Send(Character(c));
            Key("Enter");
            Until(delegate { return Visible("LabResult"); }, "engine accepted saved report and actual PNG");
        }
    }

    private static void CheckDailyUse()
    {
        Action<string, int, string> start = (area, level, mission) =>
        {
            if (Visible("LabPause") && !Visible("LabResult"))
            {
                Key("F10");
                Until(delegate { return Visible("LabResult"); }, "daily-use lesson finished");
            }
            if (Visible("LabChooseClass"))
            {
                Invoke("LabChooseClass");
                Until(delegate { return Exists("HomeDashboard"); }, "daily-use chooser");
            }
            OpenLiveConfiguration(area);
            Select("LabMode", 0);
            Select("LabLevel", level);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            SelectMissionByName(mission);
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, "native lesson " + mission);
            VirtualEnglish();
        };
        Func<AutomationElement.AutomationElementInformation> editor = () => Find("LabDesktop").Current;
        Action<string> frame = label => { Wait(110); CaptureReferenceFrame(label); };
        start("code", 0, "Перейти к определению и вернуться");
        Until(delegate { return editor().HelpText.EndsWith("main.py") && editor().ItemStatus.StartsWith("editor:body:"); }, "original call caret");
        string origin = editor().ItemStatus;
        frame("symbols-01-call");
        Key("F12");
        Until(delegate { return editor().HelpText.EndsWith("signal_ops.py") && editor().ItemStatus == "editor:body:4"; }, "definition in imported module");
        frame("symbols-02-definition");
        Chord("Alt+Left");
        Until(delegate { return editor().HelpText.EndsWith("main.py") && editor().ItemStatus == origin; }, "exact original caret after Alt+Left");
        frame("symbols-03-back");
        Chord("Ctrl+D");
        Until(delegate { return Visible("LabResult"); }, "confirmed restored source selection");

        start("code", 0, "Найти использования и переименовать функцию");
        Chord("Shift+F12");
        Until(delegate { return editor().ItemStatus.StartsWith("editor:symbol-references:") &&
            editor().HelpText.Contains("main.py") && editor().HelpText.Contains("signal_ops.py"); }, "actual references list");
        frame("symbols-04-usages");
        Key("Down"); Key("Enter"); Key("F2");
        Until(delegate { return editor().HelpText == "Переименовать символ: normalize"; }, "rename prefilled source name");
        frame("symbols-05-rename");
        Key("Esc");
        Until(delegate { return editor().HelpText.EndsWith("main.py") && editor().ItemStatus.StartsWith("editor:body:"); }, "cancel returns to call");
        frame("symbols-06-cancel");
        Key("F2");
        foreach (char c in "scale_sample") Send(Character(c));
        Until(delegate { return editor().HelpText == "Переименовать символ: scale_sample"; }, "typed new symbol");
        frame("symbols-07-new-name");
        Key("Enter");
        Until(delegate { return editor().HelpText.EndsWith("main.py") && editor().ItemStatus.StartsWith("editor:body:"); }, "rename returns to edited source");
        frame("symbols-08-renamed-source");
        Key("F12");
        Until(delegate { return Visible("LabResult"); }, "renamed call resolves to renamed definition");
        frame("symbols-09-result");

        start("code", 0, "Найти ошибку по сообщению");
        Until(delegate { return editor().ItemStatus.StartsWith("cursor:"); }, "starts in the virtual terminal");
        frame("search-01-traceback");
        Chord("Alt+Tab"); Chord("Ctrl+Shift+F"); Key("Esc");
        Until(delegate { return !Visible("LabResult") && editor().HelpText.EndsWith("archive.py"); }, "opening and leaving search cannot complete repair");
        Chord("Ctrl+Shift+F");
        foreach (char c in "DEFAULT_TIMEOUT") Send(Character(c));
        Until(delegate { return editor().HelpText == "Поиск по проекту: DEFAULT_TIMEOUT"; }, "exact search query");
        Key("Enter");
        Until(delegate {
            var result = editor();
            return result.ItemStatus.StartsWith("editor:project-results:") &&
                result.HelpText.StartsWith("Поиск «DEFAULT_TIMEOUT»: ") &&
                result.HelpText.Split(new[] { "; " }, StringSplitOptions.None).Length == 3 &&
                result.HelpText.Contains("archive.py:2:1") && result.HelpText.Contains("config.py:2:1") &&
                result.HelpText.Contains("notes.py:1:9");
        }, "exactly three search results");
        frame("search-02-results");
        Key("Down"); Key("Enter");
        Until(delegate { return editor().HelpText.EndsWith("config.py") && editor().ItemStatus == "editor:body:19"; }, "runtime config at matching token");
        frame("search-03-config");
        Key("End"); Key("Backspace"); Send(Character('5'));
        Until(delegate { return !Visible("LabResult") && editor().ItemStatus == "editor:body:38"; }, "modified buffer is not yet saved");
        Chord("Ctrl+S");
        Until(delegate { return Visible("LabResult"); }, "saved runtime config validates");
        frame("search-04-saved");

        start("code", 0, "Правка выделенной строки");
        Until(delegate { return editor().HelpText.EndsWith("line-edit.py") && editor().ItemStatus == "editor:body:19"; }, "initial middle line");
        Chord("Ctrl+L");
        Until(delegate { return editor().ItemStatus == "editor:body:41"; }, "selected middle line including its newline");
        Chord("Ctrl+/");
        Until(delegate { return editor().ItemStatus == "editor:body:43"; }, "comment preserves selection and adds prefix to middle line");
        frame("line-01-comment");
        Chord("Ctrl+Z");
        Until(delegate { return editor().ItemStatus == "editor:body:41"; }, "undo comment recovers exact cursor");
        Chord("Ctrl+Shift+K");
        Until(delegate { return editor().ItemStatus == "editor:body:19"; }, "delete removes selection and returns to boundary");
        frame("line-02-delete");
        Chord("Ctrl+Z");
        Until(delegate { return Visible("LabResult") || editor().ItemStatus == "editor:body:41"; }, "undo deletion");

        start("code", 1, "Исправить диагностику и запустить один тест");
        Until(delegate { return editor().ItemStatus.StartsWith("editor:code-problems:") &&
            editor().HelpText.Contains("Неверный знак") && Name("LabPause").Contains("F6"); }, "Problems panel and alternate pause key F6");
        Key("F6");
        Until(delegate { return Visible("LabPauseResult"); }, "F6 actually pauses rather than invoking a diagnostic");
        Key("Space");
        Until(delegate { return !Visible("LabPauseResult") && editor().ItemStatus.StartsWith("editor:code-problems:"); }, "resume to unchanged Problems panel");
        Key("F8");
        Until(delegate { return !Visible("LabPauseResult") && editor().HelpText.EndsWith("units.py") &&
            editor().ItemStatus == "editor:body:42"; }, "F8 opens exact diagnostic position without pausing");
        Check(Name("LabPause").Contains("F6"), "Alternate pause key stays available after diagnosis navigation");
        Chord("Ctrl+.");
        Until(delegate { return editor().ItemStatus.StartsWith("editor:code-quick-fix:") &&
            editor().HelpText.Contains("округлить") && editor().HelpText.Contains("дробную часть"); }, "two choices in the Quick Fix menu");
        frame("code-diagnostics-choices");
        Key("Down"); Key("Enter"); Chord("Ctrl+S"); Chord("Ctrl+`");
        const string diagnosticTest = "python -m unittest test_units.UnitsTests.test_kelvin_to_celsius";
        foreach (char c in diagnosticTest) Send(Character(c));
        Until(delegate { return editor().HelpText == diagnosticTest; }, "only one precise Python test selected in the simulated embedded terminal");
        Key("Enter");
        Until(delegate { return Visible("LabResult"); }, "tested editor fix completed");

        start("ubuntu", 1, "GNOME: документация и код рядом");
        Chord("Super+Left");
        Until(delegate { return Name("LabDesktop").Contains("chrome=left*"); }, "single Chrome on left half");
        frame("gnome-01-single-left");
        Chord("Super+2");
        Until(delegate { return editor().HelpText.EndsWith("app.py"); }, "code opens app.py");
        origin = editor().ItemStatus;
        Chord("Super+Right");
        Until(delegate { return Name("LabDesktop").Contains("code=right*") && Name("LabDesktop").Contains("chrome=left"); }, "opposite halves");
        frame("gnome-02-two-halves");
        Chord("Super+Right");
        Until(delegate { return Name("LabDesktop").Contains("code=normal*"); }, "repeated shortcut returns to normal");
        Chord("Super+Right"); Chord("Super+Up");
        Until(delegate { return Name("LabDesktop").Contains("code=maximized*"); }, "maximized code");
        frame("gnome-03-maximized");
        Chord("Super+Down");
        Until(delegate { return Name("LabDesktop").Contains("code=normal*"); }, "restored normal size");
        Chord("Super+Right"); Chord("Super+H");
        Until(delegate { return Name("LabDesktop").Contains("code=minimized") && Name("LabDesktop").Contains("chrome=left*"); }, "Chrome stays on desktop");
        frame("gnome-04-minimized");
        var alt = Parse("Alt").Single();
        var tab = Parse("Tab").Single();
        Send(new[] { alt.Down(), tab.Down(), tab.Up() });
        frame("gnome-04a-switcher");
        Send(new[] { alt.Up() });
        Until(delegate { return Name("LabDesktop").Contains("code=right*") && editor().HelpText.EndsWith("app.py") &&
            editor().ItemStatus == origin; }, "exact active editor, tab, caret and restored half");
        frame("gnome-05-restored");
        foreach (char c in "gnome") Send(Character(c));
        Until(delegate { return editor().ItemStatus == "editor:body:7"; }, "native resumed typing moved original caret by five");
        frame("gnome-06-continued-typing");
        Key("Enter");
        Chord("Alt+Tab"); Chord("Super+Left"); Chord("Super+Left"); Chord("Alt+F4");
        Until(delegate { return !Name("LabDesktop").Contains("chrome=") && Name("LabDesktop").Contains("code=right*"); }, "single editor remains");
        Chord("Super+H");
        Until(delegate { return Name("LabDesktop").Contains("code=minimized"); }, "single window minimizes");
        Chord("Alt+Tab");
        Until(delegate { return Name("LabDesktop").Contains("code=right*"); }, "single window restores to its former half");
        Chord("Super+Down"); Chord("Super+Right"); Chord("Ctrl+S");
        Until(delegate { return Visible("LabResult"); }, "GNOME result");
        frame("gnome-07-result");
    }

    private static void CheckGnomeProfilePractice()
    {
        Action<string> start = mission =>
        {
            if (Visible("LabPause") && !Visible("LabResult"))
            {
                Key("F10");
                Until(delegate { return Visible("LabResult"); }, "GNOME lesson stopped");
            }
            if (Visible("LabChooseClass"))
            {
                Invoke("LabChooseClass");
                Until(delegate { return Exists("HomeDashboard"); }, "GNOME chooser");
            }
            OpenLiveConfiguration("ubuntu");
            Select("LabMode", 0);
            Select("LabLevel", 1);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            SelectMissionByName(mission);
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, "native lesson " + mission);
        };
        Func<AutomationElement.AutomationElementInformation> scene = () => Find("LabDesktop").Current;
        Action<string, string, Func<bool>> expectDesktopTransition = (chord, key, expectedScene) =>
        {
            Chord(chord);
            try { Until(expectedScene, chord + " changes the simulated workspace"); }
            finally
            {
                Console.WriteLine("GNOME " + chord + ": " + Find("LabStatus").Current.ItemStatus +
                    "; Scene=" + scene().Name + "; Focus=" + scene().ItemStatus + "; Solved=" + ReadSolved());
            }
            Check(Find("LabStatus").Current.ItemStatus.Contains(" key=" + key + " held="),
                "The physical chord normalized to the canonical " + key);
        };
        start("GNOME: терминал на втором рабочем столе");
        VirtualEnglish();
        Check(scene().Name.Contains("Рабочий стол 1 из 2") && scene().Name.Contains("terminal@1"), "Both windows begin on desk 1");
        expectDesktopTransition("Super+Shift+PageDown", "PageDown", delegate
        {
            return scene().Name.Contains("Рабочий стол 2 из 2") && scene().Name.Contains("code@1") &&
                scene().Name.Contains("terminal@2") && ReadSolved() == 1;
        });
        foreach (char c in "pwd") Send(Character(c));
        Key("Enter");
        // The drill mode deliberately labels the phase instead of exposing
        // the expected action as the visible title. Check the credit and QA
        // target independently of the translated visible copy.
        Until(delegate
        {
            return ReadSolved() == 2 && scene().Name.Contains("Рабочий стол 2 из 2") &&
                Find("LabTask").Current.ItemStatus == "keys:Super+PageUp";
        }, "exact pwd earns the second credit and advances to PageUp on desk 2");
        CaptureReferenceFrame("gnome-workspace-2-terminal");
        expectDesktopTransition("Super+PageUp", "PageUp", delegate
        {
            return scene().Name.Contains("Рабочий стол 1 из 2") && scene().HelpText.EndsWith("app.py") &&
                scene().ItemStatus == "editor:body:2" && ReadSolved() == 3;
        });
        CaptureReferenceFrame("gnome-workspace-1-editor");
        foreach (char c in "gnome") Send(Character(c));
        Until(delegate { return scene().ItemStatus == "editor:body:7"; }, "continued typing in the same file");
        Key("Enter"); Chord("Ctrl+S"); Chord("Super+PageDown"); Chord("Super+PageUp");
        Until(delegate { return Visible("LabResult"); }, "return trips and final result");

        start("GNOME: два окна VS Code");
        Check(scene().HelpText.EndsWith("README.md"), "The first active window has README.md");
        Chord("Alt+Tab");
        Until(delegate { return scene().Name.Contains("chrome=normal*"); }, "general switcher sees a different application");
        Check(ReadSolved() == 0, "A general application switch is not same-app selection credit");
        Chord("Alt+Tab");
        Until(delegate { return scene().HelpText.EndsWith("README.md"); }, "return to the same-app lesson starting window");
        var alt = Parse("Alt").Single();
        var grave = Parse("`").Single();
        Send(new[] { alt.Down(), grave.Down(), grave.Up() });
        Until(delegate { return scene().Name.Contains("Переключатель: README.md; app.py"); }, "same-app switcher lists only its two file identities");
        Check(ReadSolved() == 0, "Selection is not credited before releasing Alt");
        CaptureReferenceFrame("gnome-same-app-switcher");
        Send(new[] { alt.Up() });
        Until(delegate { return scene().HelpText.EndsWith("app.py") && ReadSolved() == 1; }, "selected app.py in its own window");
        Chord("Alt+Shift+`");
        Until(delegate { return Visible("LabResult"); }, "same application reverse cycle");

        start("MateBook: левый Super и переключение RU/EN");
        var initial = scene().Name.Contains(". Раскладка RU.") ? "RU" : "EN";
        // WPF displays the Windows/Super key as ⊞ in prose, unlike the raw engine status.
        foreach (var attempt in new[] {
            new[] { "RSuper", "Это правый ⊞. Найдите левую клавишу ⊞ рядом с пробелом." },
            new[] { "LAlt", "Это Alt. Сдвиньтесь на одну клавишу левее к кнопке ⊞." },
            new[] { "Z", "Это клавиша Z (Я). Левый ⊞ ⊞ находится ниже, левее Alt." }
        })
        {
            Key(attempt[0]);
            try { Until(delegate { return Name("LabStatus") == attempt[1]; }, "distinct feedback for " + attempt[0]); }
            finally { Console.WriteLine("MateBook " + attempt[0] + ": " + Name("LabStatus")); }
            Check(ReadSolved() == 0, "Wrong physical key does not open the lesson overview");
        }
        CaptureReferenceFrame("gnome-matebook-mistake");
        Key("LSuper");
        Until(delegate { return ReadSolved() == 1; }, "only left Super opens and credits GNOME overview");
        Key("Esc");
        Until(delegate { return ReadSolved() == 2; }, "escape goes back to desktop");
        Chord("Super+Space");
        Until(delegate { return ReadSolved() == 3 && scene().Name.Contains(". Раскладка " + (initial == "RU" ? "EN" : "RU") + "."); },
            "virtual layout changes independently of the host");
        Chord("Super+Space");
        Until(delegate { return Visible("LabResult"); }, "return to original virtual layout");
    }

    private static void SelectMissionByName(string title)
    {
        var combos = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
        foreach (AutomationElement combo in combos)
        {
            if (combo.Current.ProcessId != app.Id || combo.Current.AutomationId != "LabMission") continue;
            object pattern;
            if (!combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern)) continue;
            var expand = (ExpandCollapsePattern)pattern;
            expand.Expand();
            Wait(120);
            var isMission = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, title));
            var option = combo.FindFirst(TreeScope.Descendants, isMission);
            if (option != null && option.Current.ProcessId == app.Id)
            {
                ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                if (expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded) expand.Collapse();
                Wait(100);
                return;
            }
            expand.Collapse();
        }
        throw new InvalidOperationException("Could not select the Workday mission: " + title);
    }

    private static void WithBlockedProfile(Action attempt)
    {
        using (var gate = new Mutex(false, @"Local\TouchTypingStudio.QA.Profile"))
        {
            if (!gate.WaitOne(3000)) throw new InvalidOperationException("Could not seize the isolated QA profile gate.");
            try { attempt(); }
            finally { gate.ReleaseMutex(); }
        }
    }

    private static void VerifyPendingSave()
    {
        Until(delegate { return Exists("LabSaveError") && Exists("LabRetrySave"); }, "explicit retry after failed save");
        Check(!app.HasExited && Exists("LabDesktop") && !Exists("LabResult") && !Exists("LabStart"),
            "A failed write keeps the live lesson without a saved result or new chooser");
    }

    private static void Solve(int attempts, bool full)
    {
        var timeout = Stopwatch.StartNew();
        int initial = ReadSolved();
        for (int i = 0; i < attempts && Exists("LabTask"); i++)
        {
            if (timeout.Elapsed.TotalSeconds > (full ? 195 : 25)) break;
            var task = Find("LabTask");
            string instruction = task.Current.ItemStatus;
            string title = task.Current.Name;
            Console.WriteLine("TASK " + title + " | " + instruction + " | " + Find("LabTask").Current.ItemType);
            if (instruction.StartsWith("text:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string text = instruction.Substring(5);
                foreach (char character in text)
                {
                    Stroke[] strokes = Character(character);
                    Send(strokes);
                    if (!Exists("LabTask") || Name("LabTask") != title) break;
                }
                if (Exists("LabTask") && Name("LabTask") == title) Key("Enter");
            }
            else if (instruction.StartsWith("keys:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string chord = instruction.Substring(5);
                int before = ReadSolved();
                var strokes = Parse(chord);
                Send(strokes.Select(stroke => stroke.Down()).ToArray());
                if (chord == "Ctrl+Shift+V")
                {
                    Until(delegate { return !Exists("LabTask") || ReadSolved() != before; }, "Ctrl+Shift+V counts on keydown");
                    Check(true, "Ctrl+Shift+V counts while modifiers remain held");
                }
                Send(strokes.Reverse().Select(stroke => stroke.Up()).ToArray());
            }
            else
                throw new InvalidOperationException("Missing typed QA input for the active task: " + title + " [" + instruction + "]");
            Wait(70);
        }
        if (!full)
        {
            Check(Exists("LabTask"), "The short exercise remains active until completion is requested");
            Check(ReadSolved() > initial, "Physical input completes the requested tasks");
        }
    }

    private static int ReadSolved()
    {
        var value = Regex.Match(Find("LabTask").Current.ItemType, "Выполнено ([0-9]+)");
        return value.Success ? int.Parse(value.Groups[1].Value) : 0;
    }

    private static void VirtualEnglish()
    {
        // The captured Super+Space combination changes only the lesson's own layout.
        Chord("Super+Space");
        if (!Name("LabDesktop").Contains("Раскладка EN")) Chord("Super+Space");
        Check(Name("LabDesktop").Contains("Раскладка EN"), "Virtual text input uses the displayed English layout");
    }

    private static void CheckNativeLessonLayouts()
    {
        uint process;
        uint thread = GetWindowThreadProcessId(trainer, out process);
        Check(process == app.Id && thread != 0, "Layout test targets only the owned trainer UI thread");
        IntPtr previous = GetKeyboardLayout(thread);
        var failures = new List<string>();
        try
        {
            SetNativeLessonLayout(thread, "00000409", 0x0409);
            OpenLiveConfiguration("terminal");
            Select("LabLevel", 1);
            SelectMissionByName("Сопоставить журнал и процессы");
            Select("LabMode", 1);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            Key("F5");
            Until(delegate { return Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
                "active terminal lesson with real EN layout");
            CheckNativeLessonState(thread, "EN", 0x0409, "", 0, failures);
            Send(PhysicalText("J N X ` N Space V 2"));
            CheckNativeLessonState(thread, "EN", 0x0409, "jnx`n v2", 8, failures);
            Chord("LCtrl+U");
            CheckTerminalState("", 0);

            SetNativeLessonLayout(thread, "00000419", 0x0419);
            // These are unchanged physical positions. Only the Windows HKL changes.
            // Do not use Super+Space or make an engine layout request here.
            Send(PhysicalText("J N X ` N"));
            CheckNativeLessonState(thread, "RU", 0x0419, "отчёт", 5, failures);
            Send(PhysicalText("Space"));
            SetNativeLessonLayout(thread, "00000409", 0x0409);
            Send(PhysicalText("V 2"));
            CheckNativeLessonState(thread, "EN", 0x0409, "отчёт v2", 8, failures);
            Key("Left"); Key("Delete");
            CheckTerminalState("отчёт v", 7);
            Chord("RCtrl+U");
            CheckTerminalState("", 0);

            // This terminal route begins with unrestricted rehearsal. Paste
            // and submit once to reach its first explicitly English command.
            Chord("Ctrl+Shift+V");
            Key("Enter");
            Until(delegate { return Find("LabTask").Current.ItemStatus == "text:whoami"; },
                "the explicitly English whoami step is active");
            int before = ReadSolved();
            Key("F4");
            Until(delegate { return Visible("LiveAidPanel"); }, "onscreen keyboard enabled for layout evidence");
            SetNativeLessonLayout(thread, "00000419", 0x0419);
            Send(PhysicalText("W H O A M I"));
            Until(delegate { return Visible("LabStatus") && Name("LabStatus") == "Переключи раскладку на EN"; },
                "English-only text receives a non-error native layout instruction");
            CheckTerminalState("", 0);
            Check(ReadSolved() == before, "RU physical input earned no credit");
            Check(Find("LiveAidPanel").Current.ItemType == "Раскладка клавиатуры: RU", "Keyboard aid follows native RU");
            CaptureLiveUxFrame("native-layout-ru");
            SetNativeLessonLayout(thread, "00000409", 0x0409);
            Send(PhysicalText("W H O A M I"));
            CheckTerminalState("whoami", 6);
            Check(Find("LiveAidPanel").Current.ItemType == "Раскладка клавиатуры: EN", "Keyboard aid returns to native EN");
            CaptureLiveUxFrame("native-layout-en");
            Key("Enter");
            Until(delegate { return ReadSolved() == before + 1; }, "switching the real source resumes English entry");
        }
        finally { if (!app.HasExited) SendMessage(trainer, 0x50, IntPtr.Zero, previous); }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static Stroke[] PhysicalText(string keys)
    {
        return keys.Split(' ').SelectMany(key => Parse(key)
            .SelectMany(stroke => new[] { stroke.Down(), stroke.Up() })).ToArray();
    }

    private static int NativeLanguage(uint thread)
    {
        return (int)(GetKeyboardLayout(thread).ToInt64() & 0xffff);
    }

    private static void SetNativeLessonLayout(uint thread, string layout, int expected)
    {
        IntPtr keyboard = LoadKeyboardLayout(layout, 0);
        if (keyboard == IntPtr.Zero) throw new InvalidOperationException("Windows layout unavailable: " + layout);
        SendMessage(trainer, 0x50, IntPtr.Zero, keyboard); // WM_INPUTLANGCHANGEREQUEST
        Until(delegate { return NativeLanguage(thread) == expected; }, "actual Windows thread layout " + layout);
        Console.WriteLine("Windows thread HKL=0x" + GetKeyboardLayout(thread).ToInt64().ToString("X", CultureInfo.InvariantCulture));
    }

    private static void CheckNativeLessonState(uint thread, string expectedLayout, int language, string text, int cursor,
        List<string> failures)
    {
        CheckTerminalState(text, cursor);
        Check(NativeLanguage(thread) == language, "Windows thread retained requested language during physical input");
        string badge = Name("LabDesktop");
        Console.WriteLine("Observed " + expectedLayout + " with field «" + text + "»: " + badge);
        if (!badge.Contains("Раскладка " + expectedLayout + "."))
            failures.Add("Displayed layout remained stale on Windows " + expectedLayout + " while field contained «" + text + "»: " + badge);
    }

    private static void CheckPasteLayouts()
    {
        uint process;
        uint thread = GetWindowThreadProcessId(trainer, out process);
        Check(process == app.Id, "Layout checks target only the QA trainer thread");
        IntPtr previous = GetKeyboardLayout(thread);
        try
        {
            foreach (string layout in new[] { "00000409", "00000419" })
            {
                IntPtr keyboard = LoadKeyboardLayout(layout, 0);
                if (keyboard == IntPtr.Zero) throw new InvalidOperationException("The native layout is unavailable: " + layout);
                SendMessage(trainer, 0x50, IntPtr.Zero, keyboard);
                Wait(90);
                foreach (string paste in new[] { "RCtrl+RShift+V", "LCtrl+LShift+V" })
                {
                    Chord(paste);
                    Check(Find("LabDesktop").Current.HelpText.Contains("pwd"), "Physical terminal paste on " + layout);
                    Chord("Ctrl+U");
                }
            }
        }
        finally { if (!app.HasExited) SendMessage(trainer, 0x50, IntPtr.Zero, previous); }
    }

    private static int ReadVisibleClock()
    {
        string label = Name("LabClock");
        var match = Regex.Match(label, @"^([0-9]{2}):([0-5][0-9])$");
        if (!match.Success) throw new InvalidOperationException("Expected a visible mm:ss clock, got: " + label);
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 +
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    private static void CheckFresh180sCountdown(Stopwatch launch, string lesson)
    {
        // UI Automation/runner scheduling can observe the newly launched app
        // several seconds after the engine resumed. Bound by measured wall time
        // since the key was sent, with one second for display quantization.
        int remaining = ReadVisibleClock();
        double observedSeconds = launch.Elapsed.TotalSeconds;
        int earliestAllowed = Math.Max(1, 180 - (int)Math.Ceiling(observedSeconds) - 1);
        Check(remaining >= earliestAllowed && remaining <= 180,
            "180s start " + lesson + ": observed remaining " + remaining +
            ", minimum " + earliestAllowed + ", after " +
            observedSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s wall");
        Check(ReadSolved() == 0, "A fresh or restarted class has no inherited credit");
    }

    private static void CheckStored180sCheckpoint(string appId, string guidance)
    {
        var profile = Path.Combine(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT"),
            "TouchTypingStudio-QA", "progress.xml");
        var xml = new XmlDocument();
        xml.XmlResolver = null;
        using (var stream = new FileStream(profile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            xml.Load(stream);
        var draft = xml.SelectSingleNode("/StudioProfile/LiveDrafts/LiveDraft[App='" + appId +
            "' and Guidance='" + guidance + "']/Progress");
        Check(draft != null, "Pause commits an actual timing checkpoint for " + appId + "/" + guidance);
        Check(draft.SelectSingleNode("TimeLimit").InnerText == "180" &&
            draft.SelectSingleNode("ActionLimit").InnerText == "0" &&
            draft.SelectSingleNode("Solved").InnerText == "0" &&
            draft.SelectSingleNode("Errors").InnerText == "0",
            "Persisted fresh profile confirms a 180-second budget and no inherited actions/errors");
        double activeMs = double.Parse(draft.SelectSingleNode("ActiveMilliseconds").InnerText, CultureInfo.InvariantCulture);
        int expected = Math.Max(0, 180 - (int)Math.Floor(activeMs / 1000));
        int actual = ReadVisibleClock();
        Check(actual == expected, "Paused clock equals 180 minus the actual saved active time (" +
            actual + "s remaining, " + activeMs.ToString("0", CultureInfo.InvariantCulture) + "ms active)");
    }

    private static void CheckThreeHeadlineMetrics(string page)
    {
        Check(Visible("ResultMetric-time") && Visible("ResultMetric-count") &&
            Visible("ResultMetric-speed"), page + " shows time, actions and pace");
        Check(!Visible("ResultMetric-accuracy"), page + " does not show headline accuracy");
    }

    private static void CheckReferenceReview()
    {
        var review = Find("LabErrorReview");
        Check(!Visible("ResultMetric-accuracy"), "Accuracy is initially hidden");
        review.SetFocus();
        Key("Space");
        Until(delegate { return Visible("ResultMetric-accuracy"); }, "keyboard reveals optional result accuracy");
        Check(Visible("ResultMetric-accuracy"), "Accuracy exists only in optional post-result review");
    }

    private static void CheckReferenceTerminalCursor()
    {
        VirtualEnglish();
        Chord("Ctrl+U");
        foreach (var character in "abcde") Send(Character(character));
        CheckTerminalState("abcde", 5);
        int rightCount = 0;
        foreach (var key in new[] { "Home", "Right", "Right" })
        {
            // Keep each press real and distinct; fail on the first lost or misrouted transition.
            Key(key);
            CheckTerminalState("abcde", key == "Home" ? 0 : ++rightCount);
        }
        CaptureReferenceFrame("01-terminal-before-delete");
        Key("Delete");
        CheckTerminalState("abde", 2);
        CaptureReferenceFrame("02-terminal-after-delete");
        Key("Backspace");
        CheckTerminalState("ade", 1);
        CaptureReferenceFrame("03-terminal-after-backspace");
        // Terminal Ctrl+U removes the portion to the left of the caret only.
        Key("End");
        Chord("Ctrl+U");
        CheckTerminalState("", 0);
    }

    private static void CheckTerminalState(string text, int cursor)
    {
        string itemStatus = "cursor:" + cursor;
        try
        {
            Until(delegate {
                var scene = Find("LabDesktop").Current;
                return scene.HelpText == text && scene.ItemStatus == itemStatus;
            }, "terminal text '" + text + "' at caret " + cursor);
        }
        catch (TimeoutException)
        {
            var seen = Find("LabDesktop").Current;
            Console.WriteLine("Terminal navigation observed: text='" + seen.HelpText + "', " +
                "state=" + seen.ItemStatus + ", scene=" + seen.Name +
                ", last trace=" + Find("LabStatus").Current.ItemStatus);
            throw;
        }
        Check(true, "Native terminal text '" + text + "' at caret " + cursor);
    }

    private static bool Visible(string id)
    {
        var node = window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty,id));
        return node != null && node.Current.ProcessId == app.Id && !node.Current.IsOffscreen;
    }

    private static void RecordLessonChrome(string fixture)
    {
        if (evidenceDirectory == null) return;
        string mode = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_LESSON_CHROME");
        if (mode != "baseline" && mode != "compact") return;
        CaptureRect client;
        var origin = new CapturePoint();
        Check(GetForegroundWindow() == trainer && GetClientRect(trainer, out client) && ClientToScreen(trainer, ref origin),
            "Geometry comes from the owned foreground lesson window");
        if (!GetClientRect(trainer, out client)) throw new InvalidOperationException("Missing actual client coordinates.");
        var task = Find("LabTask").Current.BoundingRectangle;
        var scene = Find("LabDesktop").Current.BoundingRectangle;
        var back = Find("LabBack").Current.BoundingRectangle;
        var pause = Find("LabPause").Current.BoundingRectangle;
        var aids = Find("LabKeyboardAid").Current.BoundingRectangle;
        var elements = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        bool titleVisible = elements.Cast<AutomationElement>().Any(element => {
            var value = element.Current;
            return value.ProcessId == app.Id && !value.IsOffscreen &&
                value.Name.StartsWith("Практика · ", StringComparison.Ordinal);
        });
        Func<System.Windows.Rect,string> box = rect => string.Format(CultureInfo.InvariantCulture,
            "[{0:0.##},{1:0.##},{2:0.##},{3:0.##}]",rect.X-origin.X,rect.Y-origin.Y,rect.Width,rect.Height);
        string data = string.Format(CultureInfo.InvariantCulture,
            @"{{""app"":""{0}"",""capture"":""{1}"",""platform"":""WPF"",""dpi"":{2},
            ""viewport"":[{3},{4}],""titleMapped"":{5},""task"":{6},""scene"":{7},
            ""footer"":{8},""footerBack"":{9},""footerKeyboardAid"":{10}}}",
            fixture,mode,GetDpiForWindow(trainer),client.Right,client.Bottom,titleVisible ? "true" : "false",
            box(task),box(scene),box(pause),back.Top >= scene.Bottom - 1 ? "true" : "false",
            aids.Top >= scene.Bottom - 1 ? "true" : "false");
        File.WriteAllText(Path.Combine(evidenceDirectory, "lesson-layout-wpf-" + fixture + ".json"), data);
        Console.WriteLine("WPF lesson geometry: " + fixture + " scene " + box(scene));
        Check(Visible("LabTask") && Visible("LabDesktop") && Visible("LabPause") && scene.Height > 0 &&
            task.Bottom <= scene.Top + 1 && scene.Bottom <= pause.Top + 1,
            "The native task and footer do not overlap the work surface");
        if (mode == "compact")
            Check(!titleVisible && Visible("LabBack") && Visible("LabKeyboardAid") &&
                back.Top >= scene.Bottom - 1 && aids.Top >= scene.Bottom - 1,
                "No redundant module title; Back and keyboard controls live below the work surface");
    }

    private static void CaptureReferenceFrame(string name)
    {
        if (evidenceDirectory == null) return;
        if (app.HasExited || GetForegroundWindow() != trainer)
            throw new InvalidOperationException("Refusing screenshot outside the owned foreground trainer.");
        CaptureRect client;
        var topLeft = new CapturePoint();
        if (!GetClientRect(trainer, out client) || !ClientToScreen(trainer, ref topLeft))
            throw new InvalidOperationException("Unable to read actual foreground client geometry.");
        if (client.Right <= 0 || client.Bottom <= 0)
            throw new InvalidOperationException("Actual foreground client has no area.");
        if (expectedCodeClient.HasValue && (client.Right - client.Left != expectedCodeClient.Value.Width ||
            client.Bottom - client.Top != expectedCodeClient.Value.Height || GetDpiForWindow(trainer) != 96))
            throw new InvalidOperationException("The native editor capture no longer matches the requested physical client size and DPI.");
        var display = System.Windows.Forms.Screen.FromHandle(trainer).Bounds;
        var bounds = new Rectangle(topLeft.X, topLeft.Y, client.Right, client.Bottom);
        if (!display.Contains(bounds))
            throw new InvalidOperationException("Foreground client does not fit on its display. No rescaling.");
        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            if (GetForegroundWindow() != trainer)
                throw new InvalidOperationException("Trainer lost foreground during screenshot.");
            bitmap.Save(Path.Combine(evidenceDirectory,name + ".png"),ImageFormat.Png);
        }
        Console.WriteLine("CAPTURE " + name + " " + bounds.Width + "x" + bounds.Height +
            " physical px, DPI=" + GetDpiForWindow(trainer));
    }

    private static void CheckTerminalCursorRepeat()
    {
        VirtualEnglish();
        string initial = Find("LabDesktop").Current.HelpText;
        foreach (var character in "abcd") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial + "abcd"; }, "terminal input fixture");

        // Each pair contains two down events before one physical key-up.
        var left = Parse("Left").Single();
        Send(new[] { left.Down(), left.Down(), left.Up() });
        var delete = Parse("Delete").Single();
        Send(new[] { delete.Down(), delete.Down(), delete.Up() });
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial + "ab"; }, "Left repeat then right-hand Delete repeat");

        Send(new[] { left.Down() });
        Wait(110);
        var ctrl = Parse("LCtrl").Single();
        Send(new[] { ctrl.Down() });
        try
        {
            CheckFocusLoss(ReleaseProbeCtrl);
            Send(new[] { left.Down() });
            Key("Delete");
            Check(Find("LabDesktop").Current.HelpText == initial + "a", "Held pre-pause arrow is ignored after focus regain");
        }
        catch
        {
            try { Focus(); Send(new[] { left.Up(), ctrl.Up() }); }
            catch (Exception cleanupError) { Console.Error.WriteLine("Terminal input cleanup failed: " + cleanupError.Message); }
            throw;
        }
        Send(new[] { left.Up(), ctrl.Up() });
        Key("Right");
        Key("Backspace");
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial; }, "fresh editing keys work after focus recovery");
        Check(true, "Native terminal caret, repeated deletion and focus recovery");

        Chord("Ctrl+A");
        Send(Character('z'));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "z"; }, "modifier released in the other window does not stick");
        Chord("Ctrl+U");
        Until(delegate { return Find("LabDesktop").Current.HelpText == ""; }, "fresh Ctrl chord works after external release");
        foreach (var character in "echo qa") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "echo qa"; }, "terminal history fixture");
        Key("Enter");
        foreach (var character in "draft") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "draft"; }, "terminal history draft");
        Key("Up");
        Until(delegate { return Find("LabDesktop").Current.HelpText == "echo qa"; }, "Up recalls previous terminal command");
        Key("Down");
        Until(delegate { return Find("LabDesktop").Current.HelpText == "draft"; }, "Down restores the exact terminal draft");
        Chord("Ctrl+U");
        Until(delegate { return Find("LabDesktop").Current.HelpText == ""; }, "terminal history fixture cleanup");
        Check(true, "Native terminal history restores a draft");
    }

    private static void CheckFocusLoss(Action whileAway = null)
    {
        if (focusProbe == IntPtr.Zero)
            focusProbe = CreateWindowEx(0, "STATIC", "TouchTyping UI check focus probe", 0x10cf0000,
                50, 50, 330, 110, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(focusProbe != IntPtr.Zero, "The test owns its focus probe");
        SetForegroundWindow(focusProbe);
        Until(delegate { return Exists("LabPauseResult"); }, "pause on focus loss");
        if (whileAway != null)
        {
            Check(GetForegroundWindow() == focusProbe, "External key release occurs in the separate probe window");
            whileAway();
            Wait(80);
        }
        Focus();
        Check(Exists("LabPauseResult"), "Returning focus keeps the lesson paused");
        Key("F8");
        Until(delegate { return !Exists("LabPauseResult") && Name("LabPause").StartsWith("Пауза"); }, "resume after focus return");
    }

    private static void Focus()
    {
        SetForegroundWindow(trainer);
        Until(delegate { return GetForegroundWindow() == trainer; }, "foreground QA window");
    }
    private static AutomationElement Find(string id)
    {
        var element = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (element == null) throw new InvalidOperationException("Missing UI element " + id);
        if (element.Current.ProcessId != app.Id) throw new InvalidOperationException("UI element belongs to a different process");
        return element;
    }
    private static bool Exists(string id)
    { return window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) != null; }
    private static void RequestClose()
    {
        if (!PostMessage(trainer, 0x10, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("WM_CLOSE could not be queued: Win32 " + Marshal.GetLastWin32Error());
    }
    private static List<IntPtr> ProcessWindows()
    {
        var handles = new List<IntPtr>();
        EnumWindows(delegate(IntPtr handle, IntPtr ignored)
        {
            uint process;
            GetWindowThreadProcessId(handle, out process);
            if (process == (uint)app.Id) handles.Add(handle);
            return true;
        }, IntPtr.Zero);
        return handles;
    }
    private static string WindowTitle(IntPtr handle)
    {
        var text = new StringBuilder(256);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }
    private static AutomationElement ExitDialog()
    {
        // Owned modal windows aren't consistently exposed as children of the UIA desktop.
        foreach (var handle in ProcessWindows())
        {
            if (handle == trainer || !IsWindowVisible(handle) || WindowTitle(handle) != "Завершить занятие?") continue;
            try
            {
                var dialog = AutomationElement.FromHandle(handle);
                if (dialog != null && dialog.Current.ProcessId == app.Id &&
                    dialog.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "LabExitCancel")) != null)
                    return dialog;
            }
            catch (ElementNotAvailableException) { /* closing between enumeration and query */ }
        }
        return null;
    }
    private static void DumpProcessWindows()
    {
        if (app == null) return;
        app.Refresh();
        Console.Error.WriteLine("QA process exited=" + app.HasExited +
            "; trainer=0x" + trainer.ToInt64().ToString("x") +
            "; foreground=0x" + GetForegroundWindow().ToInt64().ToString("x"));
        if (app.HasExited) { Console.Error.WriteLine("QA exit code=" + app.ExitCode); return; }
        foreach (var handle in ProcessWindows())
            Console.Error.WriteLine("QA HWND 0x" + handle.ToInt64().ToString("x") +
                " visible=" + IsWindowVisible(handle) + " enabled=" + IsWindowEnabled(handle) +
                " title=" + WindowTitle(handle));
        if (window != null)
            Console.Error.WriteLine("QA lesson clock=" + Exists("LabTask") + "; paused=" + Exists("LabPauseResult"));
    }
    private static void InvokeDialog(string id)
    {
        var dialog = ExitDialog();
        if (dialog == null) throw new InvalidOperationException("Missing save-and-close dialog");
        var button = dialog.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (button == null) throw new InvalidOperationException("Missing dialog action " + id);
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(130);
    }
    private static string Name(string id) { return Find(id).Current.Name; }
    private static void Invoke(string id)
    {
        ((InvokePattern)Find(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(130);
    }
    private static void Select(string id, int index)
    {
        var combo = Find(id);
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Wait(80);
        var options = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Check(options.Count > index, "Mode options are accessible");
        ((SelectionItemPattern)options[index].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Wait(80);
    }
    private static void Key(string name) { Chord(name); }
    private static void Chord(string chord)
    {
        var keys = Parse(chord);
        Send(keys.Select(stroke => stroke.Down()).Concat(keys.Reverse().Select(stroke => stroke.Up())).ToArray());
        Wait(110);
    }
    private static Stroke[] Parse(string chord)
    {
        return chord.Split('+').Select(token =>
        {
            switch (token)
            {
                case "Ctrl": case "LCtrl": return new Stroke(0x1d, false);
                case "RCtrl": return new Stroke(0x1d, true);
                case "Shift": case "LShift": return new Stroke(0x2a, false);
                case "RShift": return new Stroke(0x36, false);
                case "Alt": case "LAlt": return new Stroke(0x38, false);
                case "RAlt": return new Stroke(0x38, true);
                case "Super": case "LSuper": return new Stroke(0x5b, true);
                case "RSuper": return new Stroke(0x5c, true);
                case "Print": return new Stroke(0x37, true);
                case "Esc": return new Stroke(1, false);
                case "Enter": return new Stroke(0x1c, false);
                case "Space": return new Stroke(0x39, false);
                case "Tab": return new Stroke(0xf, false);
                case "Backspace": return new Stroke(0xe, false);
                case "Delete": return new Stroke(0x53, true);
                case "Up": return new Stroke(0x48, true);
                case "Down": return new Stroke(0x50, true);
                case "Left": return new Stroke(0x4b, true);
                case "Right": return new Stroke(0x4d, true);
                case "PageUp": return new Stroke(0x49, true);
                case "PageDown": return new Stroke(0x51, true);
                case "Home": return new Stroke(0x47, true);
                case "NumPad7": return new Stroke(0x47, false);
                case "NumLock": return new Stroke(0x45, true);
                case "End": return new Stroke(0x4f, true);
            }
            int number;
            if (token.Length > 1 && token[0] == 'F' && int.TryParse(token.Substring(1), out number))
                return new Stroke(number <= 10 ? 0x3a + number : number == 11 ? 0x57 : 0x58, false);
            string[] rows = { "1234567890-=", "QWERTYUIOP[]", "ASDFGHJKL;'`", "ZXCVBNM,./" };
            int[] starts = { 2, 0x10, 0x1e, 0x2c };
            for (int i = 0; i < rows.Length; i++)
            {
                int position = rows[i].IndexOf(token, StringComparison.Ordinal);
                if (position >= 0 && token.Length == 1) return new Stroke(starts[i] + position, false);
            }
            if (token == "\\") return new Stroke(0x2b, false);
            throw new ArgumentException("Unknown physical key " + token);
        }).ToArray();
    }
    private static Stroke[] Character(char value)
    {
        if (value == '\n') return Parse("Enter").SelectMany(stroke => new[] { stroke.Down(), stroke.Up() }).ToArray();
        if (value == ' ') return Parse("Space").SelectMany(stroke => new[] { stroke.Down(), stroke.Up() }).ToArray();
        const string plain = "`1234567890-=[]\\;',./";
        const string shift = "~!@#$%^&*()_+{}|:\"<>?";
        int index = shift.IndexOf(value);
        bool upper = char.IsUpper(value) || index >= 0;
        string key = index >= 0 ? plain[index].ToString() : char.ToUpperInvariant(value).ToString();
        var strokes = Parse((upper ? "Shift+" : "") + key);
        return strokes.Select(stroke => stroke.Down()).Concat(strokes.Reverse().Select(stroke => stroke.Up())).ToArray();
    }
    private static void Send(Stroke[] strokes) { SendTo(trainer, strokes); }

    private static void ReleaseProbeCtrl()
    {
        uint owner;
        if (focusProbe == IntPtr.Zero || GetWindowThreadProcessId(focusProbe, out owner) == 0 ||
            owner != (uint)Process.GetCurrentProcess().Id)
            throw new InvalidOperationException("The QA probe is not owned by the smoke runner.");
        SendTo(focusProbe, new[] { Parse("LCtrl").Single().Up() });
    }

    private static void SendTo(IntPtr expectedForeground, Stroke[] strokes)
    {
        if (GetForegroundWindow() != expectedForeground || app.HasExited)
            throw new InvalidOperationException("Input stopped because the expected QA window lost foreground focus.");
        var inputs = strokes.Select(stroke => new Input { Type = 1, Data = new InputData {
            Keyboard = new KeyboardInput { Scan = (ushort)stroke.Scan, Flags = 8u | (stroke.Extended ? 1u : 0u) | (stroke.Released ? 2u : 0u) } } }).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length)
            throw new InvalidOperationException("SendInput could not deliver all test strokes.");
        Wait(60);
    }
    private static void Check(bool condition, string text)
    { if (!condition) throw new InvalidOperationException(text); count++; Console.WriteLine("PASS " + text); }
    private static void Until(Func<bool> condition, string text, int timeout = 6000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeout)
        {
            if (app.HasExited) throw new InvalidOperationException("QA trainer exited while waiting for " + text);
            if (condition()) return;
            Wait(80);
        }
        throw new TimeoutException("Timed out waiting for " + text);
    }
    private static void Wait(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Message message;
            while (PeekMessage(out message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
            Thread.Sleep(10);
        } while (clock.ElapsedMilliseconds < milliseconds);
    }
    private struct Stroke
    {
        internal int Scan;
        internal bool Extended, Released;
        internal Stroke(int scan, bool extended) { Scan = scan; Extended = extended; Released = false; }
        internal Stroke Down() { return new Stroke(Scan, Extended); }
        internal Stroke Up() { var copy = this; copy.Released = true; return copy; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] internal KeyboardInput Keyboard; [FieldOffset(0)] internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { internal IntPtr Window; internal uint Id; internal UIntPtr WParam; internal IntPtr LParam; internal uint Time; internal int X, Y; internal uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct CaptureRect { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CapturePoint { internal int X,Y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out CaptureRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out CaptureRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref CapturePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern short GetKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string layout, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr ignored);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr ignored);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extra, string type, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
}
