// Exercises both desktop profiles through the real WPF Live cards and physical SendInput.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int RunDesktopFileSearch(string executable, string output)
    {
        try
        {
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot capture at native Windows DPI");
            app = Process.Start(new ProcessStartInfo(executable, "--qa --live") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "owned WPF Live dashboard", 45000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "Only the disposable trainer receives native input");
            Focus(); TypingProofClient.Fit(app, trainer);
            uint pid; uint thread = GetWindowThreadProcessId(trainer, out pid);
            SetNativeLessonLayout(thread, "00000409", 0x0409);
            foreach (string profile in new[] { "ubuntu", "windows" }) WalkDesktopFileSearch(profile);
            Console.WriteLine("PASS WPF: ordinary scoped file search and report handoff for Ubuntu and Windows 10 profiles");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("desktop-file-failure"); }
            catch (Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally { if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); } }
    }

    private static void WalkDesktopFileSearch(string profile)
    {
        Invoke("LabDesktopProfile-" + profile);
        string card = "HomeClass-" + profile;
        Until(delegate { return Visible(card); }, "the selected desktop card");
        Find(card).SetFocus(); Key("Enter");
        Until(delegate { return Visible("LabTask") && Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза"); },
            "ordinary keyboard activation of the card");
        string original = CourseScenario();
        Check(ReadSolved() == 0 && original.Contains(profile == "windows" ? "Пуск и окна" : "Организовать рабочий стол разработчика"),
            "an ordinary card opens without an intermediate configuration or inherited credit");
        string exact = profile == "windows" ? "Ctrl+E" : "Ctrl+F";
        for (int i = 0; i < 40 && !Name("LabTask").StartsWith("Найдите файл по имени в этой папке", StringComparison.Ordinal); i++)
            DesktopSearchStep(profile);
        Check(CourseScenario() != original && DesktopSearchPrompt() == "keys:" + exact,
            "ordinary continuation reached the scoped search task with its OS binding");
        DesktopSearchStep(profile);
        Until(delegate { return DesktopFilesHelp().StartsWith("Поле поиска в «", StringComparison.Ordinal); }, "focus on the real search field");
        int none = ReadSolved();
        EnterCharacters("000");
        Until(delegate { return DesktopFilesHelp().Contains("«000»") && DesktopFilesHelp().Contains("Найдено: 0."); }, "wrong search reports no rows");
        Check(ReadSolved() == none, "query with no results earns no credit");
        CaptureReferenceFrame(profile + "-files-01-no-matches");
        string query = DesktopSearchPrompt().Substring("text:".Length);
        Chord("Ctrl+A"); DesktopSearchStep(profile);
        Until(delegate { return DesktopFilesHelp().Contains("«" + query + "»") && DesktopFilesHelp().Contains("Найдено: 2."); },
            "the query returns two results in the selected scope");
        string results = DesktopFilesHelp();
        Check(results.Contains("casework") && results.Contains("/approved/") && results.Contains("/archive/") && !results.Contains("/Downloads/"),
            "the result listing shows the version folders and excludes the same filename outside scope");
        CaptureReferenceFrame(profile + "-files-02-results");
        int choose = ReadSolved();
        Key("Down");
        Until(delegate { return DesktopFilesHelp().Contains("Выбран «") && DesktopFilesHelp().Contains("-draft.csv». Первые результаты"); },
            "the first physical Down selected the draft");
        Check(ReadSolved() == choose, "the draft is not the requested approved artifact");
        DesktopSearchStep(profile); // second Down selects the approved file
        string picked = DesktopFilesHelp();
        Check(picked.Contains("Выбран «") && picked.Contains(".csv». Первые результаты") &&
            !picked.Contains("-draft.csv». Первые результаты"), "the selected file is the approved report");
        int saved = ReadSolved();
        bool savedAtLimit = saved == 40; // the ordinary Live card has a forty-action budget
        if (savedAtLimit)
            Until(delegate { return Visible("LabLimitChoice"); }, "the last budgeted key offers a decision");
        Invoke("LabBack");
        Until(delegate { return Visible("LiveResume-" + profile) && Visible(card); }, "the catalog retains the current ordinary attempt");
        Find(card).SetFocus(); Key("Enter");
        Until(delegate { return Visible("LabTask") && ReadSolved() == saved && (savedAtLimit
            ? Visible("LabLimitChoice") : !Visible("LabLimitChoice") && Name("LabPause").StartsWith("Пауза")); },
            "the same card restores the same action and any unmade limit decision");
        Check(DesktopFilesHelp() == picked && DesktopSearchPrompt() == "keys:Ctrl+C",
            "scope, search query, selected result and pending file copy survived returning from the catalog");
        if (savedAtLimit) ContinueDesktopSearchAtLimit();
        Check(ReadSolved() == saved && DesktopFilesHelp() == picked && DesktopSearchPrompt() == "keys:Ctrl+C",
            "entering the restored lesson does not earn a credit or change the selected report");
        bool copied = false;
        for (int i = 0; i < 14 && !Name("LabTask").StartsWith("В строке approved", StringComparison.Ordinal); i++)
        {
            if (Visible("LabLimitChoice")) ContinueDesktopSearchAtLimit();
            DesktopSearchStep(profile);
            if (Name("LabDesktop").Contains("Создана копия:"))
            {
                copied = true;
                CaptureReferenceFrame(profile + "-files-03-copy");
            }
        }
        Check(copied && Name("LabTask").StartsWith("В строке approved", StringComparison.Ordinal) &&
            Name("LabDesktop").Contains("Visual Studio Code"),
            "copy occurred in the file manager and opened in the editor for content verification");
        CaptureReferenceFrame(profile + "-files-04-opened");
        DesktopSearchStep(profile);
        Console.WriteLine("PASS WPF " + profile + ": " + original + " → " + exact + " → scoped versions → reopen → verify exact file");
        if (Visible("LabResult"))
        {
            Check(Visible("LabChooseClass"), "a completed lesson can return to the catalog");
            Invoke("LabChooseClass");
        }
        else Invoke("LabBack");
        Until(delegate { return Visible(card); }, "return to the current desktop card");
    }

    private static string DesktopFilesHelp() { return Find("LabDesktop").Current.HelpText; }

    private static string DesktopSearchPrompt() { return Find("LabTask").Current.ItemStatus; }

    private static void ContinueDesktopSearchAtLimit()
    {
        Find("LabLimitContinue").SetFocus();
        Until(delegate { return Find("LabLimitContinue").Current.HasKeyboardFocus; }, "focus the decision to finish the current mission");
        Key("Enter");
        Until(delegate { return !Visible("LabLimitChoice") && Name("LabPause").StartsWith("Пауза"); },
            "continue the current work, not a new assignment");
    }

    private static void DesktopSearchStep(string profile)
    {
        int before = ReadSolved();
        string prompt = DesktopSearchPrompt();
        if (prompt.StartsWith("text:", StringComparison.Ordinal))
        {
            EnterCharacters(prompt.Substring(5));
            if (!Visible("LabResult") && ReadSolved() == before) Key("Enter");
        }
        else
        {
            Check(prompt.StartsWith("keys:", StringComparison.Ordinal), "missing native prompt: " + prompt);
            string keys = prompt.Substring(5);
            for (int attempt = 0; attempt < (keys == "Down" || keys == "Tab" || keys == "Up" || keys == "Shift+Tab" ? 28 : 1); attempt++)
            {
                Chord(keys);
                if (Visible("LabResult") || ReadSolved() > before) break;
            }
        }
        Until(delegate { return Visible("LabResult") || ReadSolved() == before + 1; }, profile + " " + before + " " + prompt);
    }
}

internal static class DesktopFileSearchMain
{
    [STAThread]
    private static int Main(string[] args)
    { return LiveUiSmoke.RunDesktopFileSearch(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])); }
}
