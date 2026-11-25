using System;
using System.IO;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int RunChromeTabSearch(string exe, string output)
    {
        var prior = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE");
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("The Tab Search gate requires an isolated QA profile.");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", "1");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            app = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--qa --qa-home") {
                WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "Windows trainer from the extracted archive", 18000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "Physical input targets only the new WPF session.");
            Focus();
            TypingProofClient.Fit(app, trainer);
            OpenLiveConfiguration("chrome");
            Select("LabMode", 1);
            Select("LabLevel", 1);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            SelectMissionByName("Найти открытый регламент и передать проверенный код");
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && ReadSolved() == 0 && Name("LabPause").StartsWith("Пауза"); },
                "actual Chrome lesson from the normal catalog");
            VirtualEnglish();
            Chord("Ctrl+Shift+A");
            Check(ReadSolved() == 1 && BrowserWitness("LabDesktop")[2] == "tab-search" &&
                Find("LabDesktop").Current.HelpText.Contains("Совпадений: 8"), "Eight real open tabs populate the focused picker.");
            TypeChromeWithReceipt("archive");
            Key("Enter");
            const string topic = "https://practice.local/tab-review/backup/";
            Check(ReadSolved() == 1 && BrowserWitness("LabDesktop")[1] == topic+"archive",
                "Linking the archive actually switches tabs but does not credit the current source.");
            Chord("Ctrl+Shift+A");
            TypeChromeWithReceipt("backup");
            Until(delegate { return Find("LabDesktop").Current.HelpText.Contains("Совпадений: 5"); },
                "The authored topic narrows entries without creating a tab.");
            for (int i = 0; i < 7 && !Find("LabDesktop").Current.HelpText.Contains(
                "Выбрано: Backup · Резервная копия · действующий регламент"); i++) Key("Down");
            Check(Find("LabDesktop").Current.HelpText.Contains(
                "Выбрано: Backup · Резервная копия · действующий регламент"), "Keyboard selection is the actual current regulation.");
            CaptureReferenceFrame("chrome-tabs-01-current-selected");
            Key("Enter");
            Check(ReadSolved() == 2 && BrowserWitness("LabDesktop")[1] == topic+"current", "The same tab reopened at the exact source.");
            Chord("Ctrl+F");
            TypeChromeWithReceipt("control-code");
            Key("Enter");
            Check(ReadSolved()==4 && Name("LabDesktop").Contains("Найдено: control-code"), "The controlled field was found within the active document.");
            CaptureReferenceFrame("chrome-tabs-02-code-located");
            Key("Esc");
            Chord("Ctrl+L");
            Chord("Ctrl+C");
            Chord("Ctrl+5");
            Key("Tab");
            var review = BrowserWitness("LabDesktop");
            Check(ReadSolved() == 9 && review[1] == topic+"handoff" && review[2] == "source-review" &&
                review[4] == topic+"current", "Direct slot and actual field focus preserve the verified source address.");
            TypeChromeWithReceipt("014");
            Key("Enter");
            Check(ReadSolved() == 9 && !Visible("LabSourceReviewCode"), "An archive code cannot produce a receipt.");
            Chord("Ctrl+A");
            TypeChromeWithReceipt("041");
            Key("Enter");
            Until(delegate { return Visible("LabSourceReviewCode") && Name("LabSourceReviewCode") == "Код сверки: 041" &&
                Name("LabSourceReviewAddress") == topic+"current"; }, "Visible result shows exact submitted code and correct source.");
            CaptureReferenceFrame("chrome-tabs-03-verified-receipt");
            Console.WriteLine("PASS Windows Chrome: real filter through open tabs, wrong archive control, source and code rendered in receipt.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try
            {
                if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("chrome-tabs-failure");
            }
            catch (Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", prior);
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }
}

internal static class ChromeTabSearchMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.RunChromeTabSearch(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])) : 2;
    }
}
