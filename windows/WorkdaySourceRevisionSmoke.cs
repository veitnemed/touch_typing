using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    private const string RevisionScenario = "Исправить только устаревшую ссылку в передаче смены";
    private const string RevisionFile = "передача-смены.md";
    private const string RevisionRoot = "https://practice.local/handbook/";

    private sealed class WorkdayCitationCase
    {
        internal readonly string Slug;
        internal readonly string SectionTitle;
        internal readonly string Body;
        internal string Cover { get { return RevisionRoot + Slug; } }
        internal string Old { get { return Cover + "/archive"; } }
        internal string Target { get { return Cover + "#procedure"; } }
        internal string Original { get { return Body + Old; } }
        internal string Expected { get { return Body + Target; } }
        internal WorkdayCitationCase(string slug, string topic, string sectionTitle, string fact)
        {
            Slug = slug; SectionTitle = sectionTitle;
            Body = "# Передача смены: " + topic + "\n\nСтатус: работа принята.\nОтветственный: вторая смена.\n" +
                "Итог: " + fact + "\n\nИнструкция для следующей смены:\n";
        }
    }

    private static readonly WorkdayCitationCase[] WorkdayCitationCases = {
        new WorkdayCitationCase("backup", "Резервная копия", "Проверить копию",
            "Восстановите один файл в отдельную папку и сравните содержимое."),
        new WorkdayCitationCase("handoff", "Передача проекта", "Проверить пакет",
            "Сверьте файлы с оглавлением и откройте отчёт перед отправкой."),
        new WorkdayCitationCase("accounts", "Учёт задач", "Проверить отчёт",
            "Прежде чем передать сводку, отфильтруйте завершённые задачи и сверьте количество.")
    };

    private static string WorkdayRevisionScenario()
    {
        return Find("LabTask").Current.ItemType.Split(new[] { "    ·    " }, StringSplitOptions.None)[0];
    }

    private static void AttachWorkdayRevision(string executable)
    {
        app = Process.Start(new ProcessStartInfo(executable, "--qa --live") {
            WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
        });
        if (app == null) throw new InvalidOperationException("The Windows trainer could not be started.");
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
            "WPF from the supplied archive", 45000);
        window = AutomationElement.FromHandle(trainer);
        Check(window.Current.ProcessId == app.Id, "Input observes only the owned trainer process.");
        Focus();
        TypingProofClient.Fit(app, trainer);
        uint owner;
        uint thread = GetWindowThreadProcessId(trainer, out owner);
        Check(owner == app.Id && thread != 0, "The keyboard layout change targets only this trainer.");
        SetNativeLessonLayout(thread, "00000409", 0x0409);
        Until(delegate { return Visible("HomeClass-workday"); }, "standard Live catalogue");
    }

    private static void SelectWorkdayRevisionProfile(string environment)
    {
        Invoke("LabDesktopProfile-" + environment);
        Until(delegate { return Visible("HomeClass-" + environment) && Visible("HomeClass-workday"); },
            "standard catalogue with selected " + environment + " simulation");
    }

    private static void OpenWorkdayRevisionCourse(bool rotation)
    {
        OpenLiveConfiguration("workday");
        Select("LabMode", 1);
        Select("LabLevel", 1);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        SelectMissionByName(rotation ? "Последовательная практика" : RevisionScenario);
        Key("F5");
        Until(delegate { return Visible("LabTask") && Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            rotation ? "unlimited normal Workday course" : "Workday note lesson from the standard Configure panel");
    }

    private static void WorkdayRevisionAction(string keys, bool result = false, string next = null)
    {
        string prompt = Find("LabTask").Current.ItemStatus;
        Check(prompt == "keys:" + keys, "Current real lesson prompt: " + prompt + ", expected keys:" + keys);
        int before = ReadSolved();
        for (int i=0; i < (keys == "Tab" ? 25 : 1); i++)
        {
            Chord(keys);
            if (Visible("LabResult") || ReadSolved() > before) break;
        }
        Until(delegate {
            if (result)
                return Visible("LabResult") && Find("LabResult").Current.HelpText.Contains("Выполнено " +
                    (before + 1).ToString(CultureInfo.InvariantCulture) + ".");
            return !Visible("LabResult") && ReadSolved() == before + 1 &&
                (next == null || Find("LabTask").Current.ItemStatus == "keys:" + next);
        }, "exactly one accepted action from " + keys + " at " + before);
        Console.WriteLine("Workday " + (before + 1) + " " + keys);
    }

    private static void StepWorkdayOrdinary()
    {
        var before = ReadSolved();
        var prompt = Find("LabTask").Current.ItemStatus;
        if (prompt.StartsWith("text:", StringComparison.Ordinal))
        {
            EnterCharacters(prompt.Substring(5));
            if (ReadSolved() == before) Key("Enter");
        }
        else
        {
            Check(prompt.StartsWith("keys:", StringComparison.Ordinal), "The normal lesson has a visible action: " + prompt);
            var keys = prompt.Substring(5);
            string prefix = Exists("LabKeys") ? Find("LabKeys").Current.ItemStatus : "";
            if (!String.IsNullOrEmpty(prefix)) EnterCharacters(prefix);
            for (int i=0;i < (keys == "Tab" || keys == "Down" || keys == "Up" || keys == "Shift+Tab" ? 25 : 1);i++)
            {
                Chord(keys);
                if (Visible("LabResult") || ReadSolved() > before) break;
            }
        }
        Until(delegate { return !Visible("LabResult") && ReadSolved() == before + 1; },
            "ordinary Workday " + before + " " + prompt);
    }

    private static void CheckSelectedOldCitation(WorkdayCitationCase lesson)
    {
        string[] e = SourceEditor();
        ExactSavedSource(e, RevisionFile, lesson.Original);
        int lo = int.Parse(e[2], CultureInfo.InvariantCulture), hi = int.Parse(e[3], CultureInfo.InvariantCulture);
        Check(Math.Min(lo, hi) == lesson.Body.Length && Math.Max(lo, hi) == lesson.Original.Length,
            "The prior URL, and none of the approved text, is selected.");
    }

    private static void CloseWorkdayRevision()
    {
        RequestClose(); UntilExit();
        app.Dispose(); app = null;
    }

    internal static int RunWorkdaySourceRevision(string executable, string output, string environment, bool rotation)
    {
        var browserWitness = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE");
        var editorWitness = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR");
        try
        {
            string root = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT");
            if (String.IsNullOrWhiteSpace(root) || Directory.Exists(root) || File.Exists(root))
                throw new InvalidOperationException("Use a new isolated QA profile on an interactive Windows test desktop.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot use native display DPI for evidence.");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", "1");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR", "1");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            AttachWorkdayRevision(executable);
            SelectWorkdayRevisionProfile(environment);

            Find("HomeClass-workday").SetFocus(); Key("Enter");
            Until(delegate { return Visible("LabDesktop") && Visible("LabTask") && !Visible("LabMission"); },
                "one action from the Workday card begins ordinary practice");
            string ordinary = WorkdayRevisionScenario();
            Check(ordinary.Length > 0 && ordinary != RevisionScenario, "The new lesson does not replace the ordinary Workday route.");
            StepWorkdayOrdinary();
            CaptureReferenceFrame("00-workday-normal-card");
            Invoke("LabBack");
            Until(delegate { return Visible("HomeClass-workday") && Visible("LiveResume-workday"); }, "normal draft on the same card");

            OpenWorkdayRevisionCourse(rotation);
            if (rotation)
            {
                string first = WorkdayRevisionScenario();
                string previous = first;
                bool priorBulletin = false;
                while (WorkdayRevisionScenario() != RevisionScenario && ReadSolved() < 550)
                {
                    string view = WorkdayRevisionScenario();
                    if (view != previous)
                        Console.WriteLine("ROTATION " + environment + " @" + ReadSolved() + " " + previous + " -> " + view);
                    previous = view;
                    priorBulletin |= view == "Выпустить заметку по актуальному бюллетеню";
                    StepWorkdayOrdinary();
                }
                Check(first != RevisionScenario && priorBulletin && ReadSolved() > 20,
                    "Ordinary Workday retains its earlier practice and reaches the new lesson as a supplement.");
            }
            Check(WorkdayRevisionScenario() == RevisionScenario, "Actual Workday mission is visible.");
            var initialState = SourceEditor();
            var lesson = WorkdayCitationCases.Single(item => initialState != null && initialState[4] == SourceHash(item.Original));
            CheckSelectedOldCitation(lesson);
            CaptureReferenceFrame("01-exact-old-address");

            WorkdayRevisionAction("Alt+Tab", next:"Ctrl+H");
            WorkdayRevisionAction("Ctrl+H", next:"Tab");
            int historyCount = ReadSolved();
            WorkdayBrowserHistory(lesson);
            WorkdayRevisionAction("Tab", next:"Enter");
            Check(Find("LabDesktop").Current.HelpText.Contains("Выбрано: " + lesson.SectionTitle),
                "The chosen row is the instruction, not the archive or cover.");
            WorkdayRevisionAction("Enter", next:"Ctrl+L");
            Check(BrowserWitness("LabDesktop")[1] == lesson.Target && ReadSolved() == historyCount + 2,
                "History opens the exact previously visited section.");
            CaptureReferenceFrame("03-exact-section");
            WorkdayRevisionAction("Ctrl+L", next:"Ctrl+C");
            WorkdayRevisionAction("Ctrl+C", next:"Alt+Tab");
            Check(BrowserWitness("LabDesktop")[4] == lesson.Target, "The simulated clipboard has the exact fragment URL.");
            WorkdayRevisionAction("Alt+Tab", next:"Ctrl+V");
            CheckSelectedOldCitation(lesson);
            int checkpoint = ReadSolved();
            CaptureReferenceFrame("04-restored-selection");

            Invoke("LabBack");
            Until(delegate { return Visible("HomeClass-workday") && Visible("LiveResume-workday"); },
                "the checkpoint is offered visibly on the original card");
            CloseWorkdayRevision();
            AttachWorkdayRevision(executable);
            SelectWorkdayRevisionProfile(environment);
            Find("LiveResume-workday").SetFocus(); Key("Enter");
            Until(delegate { return Visible("LabDesktop") && Visible("LabTask") && ReadSolved() == checkpoint &&
                Find("LabTask").Current.ItemStatus == "keys:Ctrl+V"; }, "original file selection and pending paste after a new process");
            CheckSelectedOldCitation(lesson);
            WorkdayRevisionAction("Ctrl+V", next:"Ctrl+S");
            string[] edited = SourceEditor();
            Check(edited != null && edited[4] == SourceHash(lesson.Expected) && edited[5] == SourceHash(lesson.Original) &&
                SourceTab(edited, RevisionFile)[3] == SourceHash(lesson.Original),
                "Paste changed exactly one address in memory and the accepted original is still on disk.");
            CaptureReferenceFrame("05-unsaved-one-address");
            int beforeSave = ReadSolved();
            Key("X");
            string corrupted = SourceHash(lesson.Expected + "x");
            Until(delegate { return SourceEditor()[4] == corrupted && ReadSolved() == beforeSave; }, "one extra edit appears but cannot earn credit");
            Chord("Ctrl+S");
            Until(delegate {
                var bad = SourceEditor();
                return ReadSolved() == beforeSave && Find("LabTask").Current.ItemStatus == "keys:Ctrl+S" &&
                    SourceTab(bad, RevisionFile)[3] == corrupted;
            }, "saving a correct URL with an extra edit does not satisfy the task");
            Chord("Ctrl+Z");
            Until(delegate { return SourceEditor()[4] == SourceHash(lesson.Expected); }, "undo removes just the extra edit");
            WorkdayRevisionAction("Ctrl+S", next:"Ctrl+L");
            ExactSavedSource(SourceEditor(), RevisionFile, lesson.Expected);
            CaptureReferenceFrame("06-one-saved-address");
            WorkdayRevisionAction("Ctrl+L", next:"Ctrl+C");
            WorkdayRevisionAction("Ctrl+C", next:"Alt+Tab");
            WorkdayRevisionAction("Alt+Tab", next:"Ctrl+T");
            WorkdayRevisionAction("Ctrl+T", next:"Ctrl+V");
            Check(BrowserWitness("LabDesktop")[1] == "about:blank", "Verification uses a new reader tab.");
            WorkdayRevisionAction("Ctrl+V", next:"Enter");
            Check(BrowserWitness("LabDesktop")[3] == lesson.Target,
                "The pasted address from the saved note is the complete exact section URL.");
            if (!rotation)
            {
                WorkdayRevisionAction("Enter", result:true);
                Check(BrowserWitness("LabResult")[1] == lesson.Target &&
                    Find("LabResult").Current.HelpText.Contains("Цель занятия выполнена."),
                    "A new tab opened the exact section and completed the Workday goal.");
                CaptureReferenceFrame("07-reader-verified");
            }
            else
            {
                WorkdayRevisionAction("Enter");
                Check(WorkdayRevisionScenario() != RevisionScenario, "Ordinary practice moves on after verifying the saved note.");
                CaptureReferenceFrame("07-next-normal-workday-objective");
            }
            Console.WriteLine("PASS WPF " + environment + (rotation ? " through actual course rotation: " : " via normal Configure: ") +
                lesson.Target + "; real input, preserved selection across a new process, identical saved bytes except the URL.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try {
                if (app != null && !app.HasExited && GetForegroundWindow() == trainer)
                    CaptureReferenceFrame("workday-source-revision-failure");
            }
            catch (Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", browserWitness);
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_PYTHON_EDITOR", editorWitness);
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }

    private static void WorkdayBrowserHistory(WorkdayCitationCase lesson)
    {
        var initial = BrowserWitness("LabDesktop");
        int solved = ReadSolved();
        Check(initial[1] == "chrome://history" && initial[2] == "history" && initial[3].Length == 0 && initial[8] == "-1",
            "Ctrl+H actually focused the browser-history search.");
        TypeChromeWithReceipt(lesson.Slug);
        var filtered = BrowserWitness("LabDesktop");
        string[] rows = filtered[6].Split(';');
        Check(rows.Length == 3 && rows.Contains(lesson.Cover) && rows.Contains(lesson.Old) && rows.Contains(lesson.Target) &&
            Find("LabDesktop").Current.HelpText.Contains("Поиск в истории: " + lesson.Slug + ". Записей: 3"),
            "The rendered real field has only the three appropriate history entries.");
        Key("Enter");
        Check(ReadSolved() == solved && BrowserWitness("LabDesktop")[1] == "chrome://history",
            "Enter in the filter is not a fake choice or opening.");
        CaptureReferenceFrame("02-three-history-choices");
    }
}

internal static class WorkdaySourceRevisionMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4 || (args[2] != "windows" && args[2] != "ubuntu") ||
            (args[3] != "rotation" && args[3] != "catalog")) return 2;
        return LiveUiSmoke.RunWorkdaySourceRevision(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]),
            args[2], args[3] == "rotation");
    }
}
