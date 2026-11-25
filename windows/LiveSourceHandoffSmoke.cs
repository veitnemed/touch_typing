// An external observer of the extracted trainer. The engine is reached only
// through the app's native UI and raw keyboard events, never a test RPC.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    private const string SourceTitle = "Выпустить заметку по актуальному бюллетеню";
    private const string DesktopSourceTitle = "Сверить источник и вернуться к нужному черновику";
    private const string LogSourceTitle = "Исправить настройку по журналу проверки";
    // Seed 1 chooses the first bulletin and configuration on both profiles.
    private const string SourceDraft = "dispatch-window-review.md";
    private const string CsvTitle = "dispatch-window-1.5.csv";
    private const string SourceCsv = "key,value,unit\nTR-18,24,min\nCONTROL,12,min\n";
    private const string FinalNote = "# Окно отправки\n\nИсточник:\nhttps://practice.local/workshop/dispatch-window/1.5\n\nСверенная строка CSV:\nTR-18,24,min\n";
    private const string ShortDraft = "dispatch-window-handoff.txt";
    private const string DesktopDraft = "Передача в смену\n\nПроверенное значение: [значение]\nСледующая проверка: 09:00\n";
    private const string CompleteDesktopDraft = "Передача в смену\n\nПроверенное значение: TR-18: 24 min\nСледующая проверка: 09:00\n";
    private const string ConfigName = "stock-import.env";
    private const string OriginalConfig = "delimiter=semicolon\nstandby=true\n";
    private const string FixedConfig = "delimiter=comma\nstandby=true\n";

    private static string SourceHash(string value)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }

    private static string[] SourceEditor(bool result = false)
    {
        string value = Find(result ? "LabResult" : "LabDesktop").Current.ItemType;
        if (result && value.IndexOf(':') >= 0) value = value.Substring(value.IndexOf(':') + 1);
        string[] parts = value.Split(';');
        return parts.Length == 9 ? parts : null;
    }

    private static string[] SourceTab(string[] state, string name)
    {
        if (state == null) return null;
        return state[8].Split('|').Select(row => row.Split(',')).FirstOrDefault(row => row.Length == 4 && row[0] == name);
    }

    private static void ExactSavedSource(string[] state, string name, string body)
    {
        Check(state != null && state[0] == "body" && state[1] == name, "Correct document is active: " + name);
        string expected = SourceHash(body);
        string[] tab = SourceTab(state, name);
        Check(state[4] == expected && state[5] == expected && tab != null && tab[3] == expected,
            "UI buffer, saved tab and virtual disk contain the same exact bytes for " + name);
    }

    private static void SourceFrame(string name)
    {
        CaptureReferenceFrame(name);
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_VIDEO") == "1")
        {
            Console.WriteLine("VIDEO " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + " " + name);
            Wait(2350);
        }
    }

    private static void AfterSourceResult()
    {
        Invoke("LabChooseClass");
        Until(delegate { return Visible("HomeDashboard") && !Visible("LabResult"); }, "normal return to Live");
    }

    private static void CheckSourceBulletin(string environment)
    {
        bool sawCsv = false, sawTransfer = false, wroteNote = false;
        CheckCatalogMission("workday", 1, SourceTitle, delegate(int step, string prompt)
        {
            if (prompt == "result")
            {
                Check(sawCsv && sawTransfer && wroteNote, "Source, transfer and note were observed before completion");
                ExactSavedSource(SourceEditor(true), SourceDraft, FinalNote);
                SourceFrame(environment + "-bulletin-result");
                return;
            }
            if (!sawTransfer && prompt == "keys:Ctrl+V" && SourceEditor() == null)
            {
                sawTransfer = true;
                string desk = Name("LabDesktop");
                Check(desk.Contains("К переносу:") && desk.Contains(CsvTitle),
                    "The active file-manager transfer holds the approved downloaded CSV");
                SourceFrame(environment + "-bulletin-transfer");
            }
            var state = SourceEditor();
            if (!sawCsv && state != null && state[1] == CsvTitle)
            {
                var tab = SourceTab(state, CsvTitle);
                Check(tab != null && tab[1] == SourceHash(SourceCsv) && tab[3] == SourceHash(SourceCsv),
                    "VS Code opened the approved, actual downloaded CSV bytes");
                sawCsv = true;
                SourceFrame(environment + "-bulletin-csv");
            }
            if (prompt == "keys:Ctrl+S")
            {
                Check(state != null && state[1] == SourceDraft && state[4] == SourceHash(FinalNote) && state[4] != state[5],
                    "The actual source URL and CSV record are present in the still-unsaved editor document");
                wroteNote = true;
                SourceFrame(environment + "-bulletin-note");
            }
        });
        AfterSourceResult();
    }

    private static void CheckSourceDesktop(string environment)
    {
        bool returned = false;
        CheckCatalogMission(environment, 1, DesktopSourceTitle, delegate(int step, string prompt)
        {
            if (prompt == "result")
            {
                Check(returned, "The editor was restored with its original selection");
                ExactSavedSource(SourceEditor(true), ShortDraft, CompleteDesktopDraft);
                SourceFrame(environment + "-desktop-handoff-result");
                return;
            }
            if (prompt != "keys:Ctrl+V") return;
            var w = SourceEditor();
            int from = DesktopDraft.IndexOf("[значение]", StringComparison.Ordinal);
            Check(w != null && w[0] == "body" && w[1] == ShortDraft && w[4] == SourceHash(DesktopDraft) &&
                w[2] == from.ToString(CultureInfo.InvariantCulture) && w[3] == (from + "[значение]".Length).ToString(CultureInfo.InvariantCulture),
                "The selected marker and original caret survive the OS-profile window switch");
            Chord("Ctrl+Shift+V");
            Check(ReadSolved() == step, "A different chord cannot claim the exact requested action");
            returned = true;
            SourceFrame(environment + "-desktop-handoff-restored");
        });
        AfterSourceResult();
    }

    private static void CheckSourceLog(string environment)
    {
        bool sawError = false, sawWritten = false, ranCheck = false;
        CheckCatalogMission("workday", 2, LogSourceTitle, delegate(int step, string prompt)
        {
            if (prompt == "result")
            {
                Check(sawError && sawWritten && ranCheck &&
                      Find("LabResult").Current.HelpText.Contains("Цель занятия выполнена."),
                    "The real successful verification completed the log exercise");
                SourceFrame(environment + "-log-result");
                return;
            }
            if (prompt.StartsWith("text:grep ERROR ", StringComparison.Ordinal))
            {
                sawError = true;
                SourceFrame(environment + "-log-inspect");
            }
            if (prompt == "keys:Ctrl+S")
            {
                var w = SourceEditor();
                Check(w != null && w[1] == ConfigName && w[4] == SourceHash(FixedConfig) && w[5] == SourceHash(OriginalConfig),
                    "Only the expected working file is edited; the original remains unsaved until Ctrl+S");
                sawWritten = true;
                SourceFrame(environment + "-log-edit");
            }
            if (sawWritten && (prompt == "keys:Alt+Tab" || prompt == "keys:Alt+Shift+Tab"))
                ExactSavedSource(SourceEditor(), ConfigName, FixedConfig);
            if (prompt == "text:python3 -m unittest test_live_config")
            {
                ranCheck = true;
                SourceFrame(environment + "-log-verify");
            }
        });
        AfterSourceResult();
    }

    internal static int RunSourceHandoffs(string executable, string output)
    {
        try
        {
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot capture at actual display DPI");
            app = Process.Start(new ProcessStartInfo(executable, "--qa --live") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "owned WPF Live dashboard", 45000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "Only the owned Live window receives input");
            Focus(); TypingProofClient.Fit(app, trainer);
            uint owner; uint thread = GetWindowThreadProcessId(trainer, out owner);
            Check(owner == app.Id, "Keyboard layout belongs to the owned process");
            SetNativeLessonLayout(thread, "00000409", 0x0409);

            string subset = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SOURCE_PROFILE");
            foreach (var environment in new[]{"ubuntu", "windows"})
            {
                if (!String.IsNullOrEmpty(subset) && subset != environment) continue;
                Invoke("LabDesktopProfile-" + environment);
                Until(delegate { return Visible("HomeClass-" + environment); }, "selected " + environment + " profile");
                Find("HomeClass-workday").SetFocus();
                Key("Enter");
                Until(delegate { return Visible("LabTask") && Visible("LabDesktop") && !Visible("LabMission"); },
                    "the actual Workday card launches without a second catalogue");
                Check(ReadSolved() == 0, "Each desktop profile starts its own ordinary card route");
                SourceFrame(environment + "-workday-card");
                Invoke("LabBack");
                Until(delegate { return Visible("HomeClass-workday"); }, "return to original card before choosing a mission");
                CheckSourceBulletin(environment);
                CheckSourceDesktop(environment);
                CheckSourceLog(environment);
                Console.WriteLine("PASS WPF source handoffs: " + environment + ", bulletin/report + restored editor + log repair.");
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("source-handoff-failure"); }
            catch (Exception capture) { Console.Error.WriteLine(capture); }
            return 1;
        }
        finally { if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); } }
    }
}

internal static class LiveSourceHandoffMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Pass an extracted TouchTyping.exe and disposable output path."); return 2; }
        return LiveUiSmoke.RunSourceHandoffs(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
    }
}
