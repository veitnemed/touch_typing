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
internal static class LiveUiSmoke
{
    private static Process app;
    private static IntPtr trainer, focusProbe;
    private static AutomationElement window;
    private static int count;
    private static string evidenceDirectory;

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 0) { Console.Error.WriteLine("Pass the full path to TouchTyping.exe."); return 2; }
        try
        {
            bool referenceBrowser = arguments.Contains("--reference-browser");
            bool dailyUse = arguments.Contains("--daily-use");
            bool gnomeProfile = arguments.Contains("--gnome-profile");
            bool debugDay = arguments.Contains("--debug-day");
            bool referenceEvidence = referenceBrowser || dailyUse || gnomeProfile || debugDay || arguments.Contains("--reference-evidence");
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
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_NAV_TRACE", gnomeProfile ? "workspace" :
                referenceEvidence && !referenceBrowser && !dailyUse && !debugDay ? "terminal" : null);
            app = Process.Start(new ProcessStartInfo(path, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(path) });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "trainer window", 12000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "The automation root belongs to the new QA process");
            Focus();
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
            if (arguments.Contains("--native-layout"))
            {
                CheckNativeLessonLayouts();
                Console.WriteLine("PASS: " + count + " native WPF layout assertions");
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
                CheckWorkdaySourceNote();
                Console.WriteLine("PASS: " + count + " native WPF Workday source-note assertions");
                return 0;
            }
            Chord("Ctrl+F5");
            Check(Exists("LabStart") && !Exists("LabClock"), "Modified F5 keeps the class chooser open");
            // Keep key/cursor evidence independent of later window-close assertions.
            Invoke("LabClass-terminal");
            Select("LabMode", 0);
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabClock") && Name("LabPause").StartsWith("Пауза"); }, "focused terminal lesson");
            CheckPasteLayouts();
            CheckReferenceTerminalCursor();
            CheckTerminalCursorRepeat();
            Key("F10");
            Until(delegate { return Exists("LabResult"); }, "focused terminal result");
            Invoke("LabChooseClass");
            Until(delegate { return Exists("LabStart"); }, "class chooser after focused terminal lesson");
            Console.WriteLine("PASS: focused terminal native input assertions");

            string[] apps = { "ubuntu", "chrome", "code", "terminal", "workday" };
            foreach (string name in apps)
                foreach (int mode in new[] { 0, 1 })
                {
                    Invoke("LabClass-" + name);
                    Select("LabMode", mode);
                    Select("LabTimer", 1);
                    var started = Stopwatch.StartNew();
                    Key("F5");
                    Until(delegate { return Exists("LabClock") && Name("LabPause").StartsWith("Пауза"); }, "active " + name + " lesson");
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
                    Check(Exists("LabClock") && Exists("LabPauseResult"), "Modified F10 keeps the paused lesson open");
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
                        Until(delegate { return !Exists("LabPauseResult") && Exists("LabClock"); },
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
                        Until(delegate { return !Exists("LabResult") && Exists("LabClock"); }, "Space replays from the result");
                        Key("F10");
                        Until(delegate { return Exists("LabResult"); }, "result after Space replay");
                    }
                    Invoke("LabChooseClass");
                    Until(delegate { return Exists("LabStart"); }, "class chooser after result");
                }
            if (arguments.Contains("--full-session"))
            {
                Invoke("LabClass-terminal");
                Select("LabMode", 1);
                Select("LabTimer", 1);
                Key("F5");
                Until(delegate { return Exists("LabClock"); }, "full timed session");
                VirtualEnglish();
                Solve(int.MaxValue, true);
                Until(delegate { return Exists("LabResult"); }, "three-minute completion", 10000);
                string result = Find("LabResult").Current.HelpText;
                Check(result.Contains("Цель занятия выполнена"), "A complete 180-second session is recorded");
                var match = Regex.Match(result, "Рекорд ([0-9]+)");
                Check(match.Success && int.Parse(match.Groups[1].Value) > 0, "A completed class has a positive record");
            }
            if (Exists("LabResult")) Invoke("LabChooseClass");
            Invoke("LabClass-terminal");
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabClock"); }, "lesson before stop storage failure");
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
            Until(delegate { return Exists("LabStart"); }, "chooser after retried stop");
            Invoke("LabClass-ubuntu");
            Select("LabTimer", 1);
            Key("F5");
            Until(delegate { return Exists("LabClock"); }, "lesson before save-and-close");
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
            if (focusProbe != IntPtr.Zero) DestroyWindow(focusProbe);
            if (app != null)
            {
                try { if (!app.HasExited) app.Kill(); }
                catch (InvalidOperationException) { }
                app.Dispose();
            }
        }
    }

    private static void CheckReferenceBrowser()
    {
        Check(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED") == "0",
            "Use the pinned Python documentation fixture (seed 0)");
        Invoke("LabClass-chrome");
        Select("LabMode", 1);
        Select("LabLevel", 0);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        SelectMissionByName("Подготовить справочник к чтению");
        Key("F5");
        Until(delegate { return Exists("LabTask") && Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "active Chrome reading mission");
        Check(Name("LabCounter").Contains("Подготовить справочник к чтению") &&
            Name("LabDesktop").StartsWith("Учебный Chrome") && Name("LabDesktop").Contains("Chrome."),
            "The actual training window is the selected Chrome mission");
        Check(Find("LabTask").Current.ItemStatus == "keys:Ctrl+L" && ReadSolved() == 0,
            "The initial visible task is Ctrl+L with no credit yet");
        Check(Visible("LabScenarioProgress") && !Visible("ResultMetric-accuracy"),
            "Activity shows progress and no accuracy");
        CaptureReferenceFrame("07-chrome-python-docs-before-ctrl-l");
        RecordLessonChrome("browser-reference");
        Chord("Ctrl+L");
        Until(delegate { return ReadSolved() == 1 && Find("LabTask").Current.ItemStatus == "keys:Ctrl+C"; },
            "native Ctrl+L earns exactly one credit and advances to copy");
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
            Until(delegate { return Exists("LabStart"); }, "catalog after standalone result");
            Invoke("LabClass-chrome");
            Key("F5");
            Until(delegate { return Visible("LabBack") && Name("LabPause").StartsWith("Пауза"); }, "active browser with footer Back");
            Invoke("LabBack");
            Until(delegate { return Exists("LabResult") && !Exists("LabClock"); }, "footer Back shows the saved result");
            Check(true, "Footer Back retains the actual lesson exit action");
        }
    }

    private static void CheckWorkdaySourceNote()
    {
        string qaRoot = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT");
        Check(!string.IsNullOrEmpty(qaRoot) && Path.IsPathRooted(qaRoot), "Use an explicit disposable QA profile");
        string profile = Path.Combine(qaRoot, "TouchTypingStudio-QA", "progress.xml");
        Check(!File.Exists(profile), "The Workday project starts with a fresh profile");

        Invoke("LabClass-workday");
        Select("LabMode", 1);
        Select("LabLevel", 0);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        SelectMissionByName("Перенести методику из браузера в заметку");
        Key("F5");
        Until(delegate { return Exists("LabTask") && Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "active standalone Workday source-note project");

        var number = Regex.Match(Name("LabKeys"), @"шаг 1/([0-9]+)");
        Check(number.Success, "The new project shows the full task count");
        int total = int.Parse(number.Groups[1].Value);
        Check(total == 25 || total == 26, "The exact route has 25 steps and at most one initial layout correction");
        Check(Name("LabCounter").Contains("Перенести методику из браузера в заметку"), "The selected project uses the correct route");

        bool foundSource = false, copied = false, pasted = false, saved = false, reopened = false, submitted = false;
        for (int step = 1; step <= total; step++)
        {
            Check(ReadSolved() == step - 1, "No missing or duplicate credit before step " + step);
            var task = Find("LabTask");
            string title = task.Current.Name, instruction = task.Current.ItemStatus;
            Console.WriteLine("WORKDAY TASK " + step + " " + title + " | " + instruction);
            string activeBefore = Name("LabDesktop");
            if (title == "Откройте «Методика расчёта спектра»")
                Check(activeBefore.Contains("Chrome."), "The simulated browser is active before opening the source");

            int before = step - 1;
            if (title == "Откройте «notes.txt»" && activeBefore.Contains("Файлы."))
                CaptureReferenceFrame("workday-03-file-manager-before-open");
            if (instruction.StartsWith("keys:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string gesture = instruction.Substring(5);
                var scan = Parse(gesture);
                var input = scan.Select(k => k.Down()).Concat(scan.Reverse().Select(k => k.Up())).ToArray();
                int tries = gesture == "Tab" || gesture == "Down" ? 24 : 1;
                for (int attempt = 0; attempt < tries; attempt++)
                {
                    Send(input);
                    Wait(110);
                    if (ReadSolved() > before) break;
                }
                Until(delegate { return ReadSolved() > before; }, "native " + gesture + " credit at step " + step);
                if (title == "Откройте «Методика расчёта спектра»") foundSource = true;
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
        Invoke("LabClass-terminal");
        Select("LabMode", 0);
        Check(Exists("LabDuration") && Exists("LabTimer"), "Action limit and timer are independent controls");
        Select("LabDuration", 4); // 100 actions, alongside the timer.
        Select("LabTimer", 1); // QA-only seven-second clock cap.
        Key("F5");
        Until(delegate { return Exists("LabDesktop") && Name("LabPause").StartsWith("Пауза"); }, "active terminal lesson");
        Check(Regex.IsMatch(Name("LabClock"), @"^0/100 · 00:0[0-7]$"), "Both selected budgets have live values");
        VirtualEnglish();
        Chord("Ctrl+U");
        foreach (var character in "abcde") Send(Character(character));
        Key("Home"); Key("Right"); Key("Right");
        CheckTerminalState("abcde", 2);
        Until(delegate { return Visible("LabLimitChoice"); }, "actual engine deadline", 14000);
        Check(Name("LabLimitChoice") == "Время вышло" && Name("LabClock") == "0/100 · 00:00",
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
        Invoke("LabClass-terminal");
        Select("LabMode", 0);
        SelectMissionByName("Продолжить с сохранённого места");
        Check(!Find("LabDuration").Current.IsEnabled && !Find("LabTimer").Current.IsEnabled,
            "A resumed attempt does not offer to overwrite its budgets");
        Key("F5");
        Until(delegate { return Visible("LabLimitChoice"); }, "recovered deadline decision");
        Check(Name("LabLimitChoice") == "Время вышло" && Name("LabClock") == "0/100 · 00:00",
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
                Until(delegate { return Exists("LabStart"); }, "debug-day chooser");
            }
            Invoke("LabClass-workday"); Select("LabMode", 0); Select("LabLevel", 1);
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
                Until(delegate { return Exists("LabStart"); }, "daily-use chooser");
            }
            Invoke("LabClass-" + area);
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
                Until(delegate { return Exists("LabStart"); }, "GNOME chooser");
            }
            Invoke("LabClass-ubuntu");
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
        for (int i = 0; i < attempts && Exists("LabClock"); i++)
        {
            if (timeout.Elapsed.TotalSeconds > (full ? 195 : 25)) break;
            var task = Find("LabTask");
            string instruction = task.Current.ItemStatus;
            string title = task.Current.Name;
            Console.WriteLine("TASK " + title + " | " + instruction + " | " + Name("LabCounter"));
            if (instruction.StartsWith("text:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string text = instruction.Substring(5);
                foreach (char character in text)
                {
                    Stroke[] strokes = Character(character);
                    Send(strokes);
                    if (!Exists("LabClock") || Name("LabTask") != title) break;
                }
                if (Exists("LabClock") && Name("LabTask") == title) Key("Enter");
            }
            else if (instruction.StartsWith("keys:", StringComparison.Ordinal) && instruction.Length > 5)
            {
                string chord = instruction.Substring(5);
                int before = ReadSolved();
                var strokes = Parse(chord);
                Send(strokes.Select(stroke => stroke.Down()).ToArray());
                if (chord == "Ctrl+Shift+V")
                {
                    Until(delegate { return !Exists("LabClock") || ReadSolved() != before; }, "Ctrl+Shift+V counts on keydown");
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
            Check(Exists("LabClock"), "The short exercise remains active until completion is requested");
            Check(ReadSolved() > initial, "Physical input completes the requested tasks");
        }
    }

    private static int ReadSolved()
    {
        var value = Regex.Match(Find("LabCounter").Current.HelpText, "Выполнено ([0-9]+)");
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
            Invoke("LabClass-terminal");
            Select("LabMode", 0);
            Select("LabDuration", 0);
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
            Console.Error.WriteLine("QA lesson clock=" + Exists("LabClock") + "; paused=" + Exists("LabPauseResult"));
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
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out CaptureRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref CapturePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
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
