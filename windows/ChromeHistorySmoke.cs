using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    private static void TypeChromeWithReceipt(string value)
    {
        var initial = BrowserWitness("LabDesktop");
        string visible = initial[3], prefix = "";
        int retries = 1;
        foreach (char c in value)
        {
            string expected = prefix + c;
            while (true)
            {
                Send(Character(c));
                var elapsed = Stopwatch.StartNew();
                bool observed = false;
                while (elapsed.ElapsedMilliseconds < 1300)
                {
                    var now = BrowserWitness("LabDesktop");
                    if (now[1] != initial[1] || now[2] != initial[2])
                        throw new InvalidOperationException("Chrome lost its focused field while typing.");
                    if (now[3] == expected) { observed = true; break; }
                    if (now[3] != visible)
                        throw new InvalidOperationException("Unexpected visible prefix: " + now[3]);
                    Wait(40);
                }
                if (observed) break;
                if (retries-- == 0) throw new TimeoutException("The native window did not receive " + c);
                Console.WriteLine("RETRY physical key " + c + " after the field showed no change.");
            }
            visible = prefix = expected;
        }
    }

    private static void AdvanceChromeHistoryStep()
    {
        int previous = ReadSolved();
        string prompt = Find("LabTask").Current.ItemStatus;
        Check(prompt.StartsWith("keys:"), "The current Chrome step requires a physical keyboard action.");
        string keys = prompt.Substring(5);
        for (int attempt = 0; attempt < (keys == "Tab" ? 25 : 1); attempt++)
        {
            Chord(keys);
            if (ReadSolved() > previous) break;
        }
        Until(delegate { return ReadSolved() == previous + 1; }, "credit once for " + keys + " at " + previous);
    }

    internal static int RunChromeHistoryProof(string exe, string output)
    {
        var original = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE");
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("Use a clean isolated QA profile.");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", "1");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            SetThreadDpiAwarenessContext(new IntPtr(-4));
            app = Process.Start(new ProcessStartInfo(exe, "--qa --qa-home") {
                WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "Windows trainer from the extracted archive", 18000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "Native input targets only the new WPF process.");
            Focus();
            TypingProofClient.Fit(app, trainer);
            OpenLiveConfiguration("chrome");
            Select("LabMode", 1);
            Select("LabLevel", 2);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            SelectMissionByName("Восстановить рабочий источник из истории");
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && ReadSolved() == 0 && Name("LabPause").StartsWith("Пауза"); },
                "physical launch of the Chrome history exercise");
            VirtualEnglish();
            Chord("Ctrl+L");
            Check(ReadSolved() == 1 && BrowserWitness("LabDesktop")[2] == "url", "Ctrl+L selects the actual address.");
            // Ctrl+L begins with a selected URL; the first key replaces it.
            TypeChromeWithReceipt("Handbook Backup");
            Key("Enter");
            Until(delegate { return ReadSolved() == 2 && BrowserWitness("LabDesktop")[1].Contains("Handbook%20Backup"); },
                "same real search results accept uppercase without replacing visible text");
            CaptureReferenceFrame("chrome-history-00-search-results");
            while (ReadSolved() < 9 && Find("LabTask").Current.ItemStatus != "keys:Ctrl+H")
                AdvanceChromeHistoryStep();
            Check(Find("LabTask").Current.ItemStatus == "keys:Ctrl+H", "Reached the browser history step.");
            Chord("Ctrl+H");
            var active = BrowserWitness("LabDesktop");
            int before = ReadSolved();
            Check(active[1] == "chrome://history" && active[2] == "history" && active[3] == "" &&
                active[8] == "-1", "Ctrl+H focused the visible search, not the document body.");
            Check(Find("LabDesktop").Current.HelpText.Contains("Поиск в истории:"),
                "The rendered history field has a real accessible label.");
            TypeChromeWithReceipt("Handbook Backup");
            var filtered = BrowserWitness("LabDesktop");
            Check(filtered[6].Split(';').Length == 3 && filtered[6].Contains("/handbook/backup#procedure") &&
                !filtered[6].Contains("task-list") && ReadSolved() == before,
                "Three genuine visits match; editing/filtering alone does not score.");
            Check(Find("LabDesktop").Current.HelpText.Contains("Поиск в истории: Handbook Backup. Записей: 3"),
                "The rendered accessibility surface exposes the typed query and filtered count.");
            CaptureReferenceFrame("chrome-history-01-filtered");
            Key("Enter");
            Check(ReadSolved() == before && BrowserWitness("LabDesktop")[1] == "chrome://history",
                "Enter on the search field does not fake the opening step.");
            const string historyHint = "Tab выбирает запись, Enter открывает её.";
            Until(delegate { return Name("LabDesktop").Contains(historyHint); },
                "Enter on the query displays the selection hint in the real browser surface");
            CaptureReferenceFrame("chrome-history-01b-selection-hint");
            AdvanceChromeHistoryStep();
            Check(!Name("LabDesktop").Contains(historyHint), "Selecting a result clears the field-only hint.");
            Check(Find("LabDesktop").Current.HelpText.Contains("Выбрано: Проверить копию"),
                "The rendered row selection is the actual expected procedure.");
            AdvanceChromeHistoryStep();
            Check(BrowserWitness("LabDesktop")[1] == "https://practice.local/handbook/backup#procedure",
                "Tab and Enter open the exact previously visited instruction.");
            Check(!Name("LabDesktop").Contains(historyHint),
                "The source page no longer displays a History-only instruction.");
            CaptureReferenceFrame("chrome-history-02-recovered");
            Console.WriteLine("PASS Windows Chrome: typed real history query, observed filter, restored exact page.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try {
                if (app != null && !app.HasExited && GetForegroundWindow() == trainer)
                    CaptureReferenceFrame("chrome-history-failure");
            } catch (Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE", original);
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }
}

internal static class ChromeHistoryMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.RunChromeHistoryProof(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])) : 2;
    }
}
