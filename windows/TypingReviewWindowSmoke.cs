// Compile with LiveUiSmoke.cs. Reuses its foreground/process-ownership guards and scan-code input.
// The application is the EXE from the ZIP, not code linked into this test process.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Automation;
using System.Xml;
using System.Web.Script.Serialization;

internal static partial class LiveUiSmoke
{
    private static string ReviewFile { get { return Path.Combine(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT"), "TouchTypingStudio-QA", "progress.xml"); } }
    private static XmlDocument ReviewProfile()
    {
        var doc = new XmlDocument { XmlResolver = null };
        using (var fs = new FileStream(ReviewFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            doc.Load(fs);
        return doc;
    }

    // Only author a reusable custom exercise; do not seed results, errors or positions.
    private static string AuthorReviewSet(string appPath, string text)
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(appPath), "engine", "TouchTyping.Engine.exe"), "--verify");
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        using (var engine = Process.Start(start))
        {
            var data = new JavaScriptSerializer().Serialize(new { command = "typing-save-set", title = "Practice: correction", content = text });
            engine.StandardInput.WriteLine(data);
            engine.StandardInput.Close();
            string answer = engine.StandardOutput.ReadToEnd();
            if (!engine.WaitForExit(12000) || engine.ExitCode != 0 || !answer.Contains("\"ok\":true"))
                throw new InvalidOperationException("Could not author the isolated practice set: " + answer.Substring(0, Math.Min(200, answer.Length)));
        }
        return ReviewProfile().SelectSingleNode("/StudioProfile/PracticeSets/PracticeSet[Name='Practice: correction']/Id").InnerText;
    }

    private static void OpenReviewApp(string path)
    {
        app = Process.Start(new ProcessStartInfo(path, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(path) });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "WPF window from release ZIP", 15000);
        window = AutomationElement.FromHandle(trainer);
        Check(window.Current.ProcessId == app.Id, "All upcoming input is scoped to the owned QA window");
        Focus();
        uint owner;
        var thread = GetWindowThreadProcessId(trainer, out owner);
        if (owner != (uint)app.Id) throw new InvalidOperationException("Layout target is not the QA application.");
        SetNativeLessonLayout(thread, "00000409", 0x0409);
        Invoke("ReferenceNav-learn");
        Until(delegate { return Visible("TypingStart"); }, "ordinary typing catalog");
    }

    // Batch native scan codes from known practice material; observe every batch at
    // the real visible counter. If focus moves off the EXE, Send aborts immediately.
    private static void EnterReviewedText(string value, int startingAt, int endingAt, int total)
    {
        for (int i = startingAt; i < endingAt;)
        {
            int count = Math.Min(10, endingAt - i);
            Send(value.Substring(i, count).SelectMany(Character).ToArray());
            i += count;
            int wanted = i;
            if (i != total)
                Until(delegate { return Exists("TypingCount") && Name("TypingCount") == wanted + "/" + total; }, "visible progress " + wanted + "/" + total);
        }
    }

    internal static int VerifyCorrectionWindow(string[] args)
    {
        try
        {
            if (args.Length != 2 || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new ArgumentException("An isolated QA root, extracted executable path, output folder are required.");
            string path = Path.GetFullPath(args[0]);
            if (File.Exists(ReviewFile)) throw new ArgumentException("Use a new QA profile.");
            evidenceDirectory = Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName;
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot configure truthful per-monitor screenshots.");
            // Nongenerated, 390 characters, all observable in the practice surface.
            string text = string.Concat(Enumerable.Repeat("save notes. ", 40)).Substring(0, 390);
            string set = AuthorReviewSet(path, text);
            OpenReviewApp(path);
            ((ExpandCollapsePattern)Find("TypingMore").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            Invoke("TypingRow-set:" + set);
            Until(delegate { return Exists("TypingCount") && Name("TypingCount") == "0/390"; }, "known 390-character primary attempt");
            // Exactly ten errors on the first requested s: 390/(390+10) = 97.5%.
            for (int n = 0; n < 10; n++) Key("X");
            Check(Name("TypingCount") == "0/390", "errors do not advance the primary position");
            EnterReviewedText(text, 0, 390, 390);
            Until(delegate { return Visible("PracticeResult") && Visible("TypingStartReview"); }, "97.5 percent review offer", 20000);
            CaptureLiveUxFrame("review-wpf-01-source-result");
            Find("TypingStartReview").SetFocus();
            Key("Enter");
            Until(delegate { return Exists("TypingCount") && Name("TypingCount").StartsWith("0/"); }, "actual corrective exercise");
            int total = int.Parse(Name("TypingCount").Split('/')[1]);
            Check(total >= 150 && total <= 200, "Correction requests 150–200 actual actions");
            string id = ReviewProfile().SelectSingleNode("/StudioProfile/TypingReviews/TypingReviewTicket/SourceId").InnerText;
            CaptureLiveUxFrame("review-wpf-02-started");
            string corrective = new string('s', total);
            Key("X");
            EnterReviewedText(corrective, 0, 12, total);
            RequestClose();
            Until(delegate { return ExitDialog() != null; }, "save-and-close dialog");
            InvokeDialog("LabExitSave");
            UntilExit(); app.Dispose();
            Check(ReviewProfile().SelectSingleNode("/StudioProfile/TypingReviews/TypingReviewTicket/Position").InnerText == "12",
                "Clean WPF process exit persisted twelve review units");
            OpenReviewApp(path);
            var continuation = Find("TypingContinueReview_" + id);
            Check(continuation.Current.Name.Contains("12/" + total), "Catalog names the exact prior review position");
            CaptureLiveUxFrame("review-wpf-03-continue");
            continuation.SetFocus(); Key("Space");
            Until(delegate { return Exists("TypingCount") && Name("TypingCount") == "12/" + total; }, "same session reopened");
            EnterReviewedText(corrective, 12, total, total);
            Until(delegate { return Visible("PracticeResult"); }, "normal final result", 15000);
            Check(!Visible("TypingStartReview") && !Visible("TypingRepeat"), "No duplicate or chained review on its result");
            CaptureLiveUxFrame("review-wpf-04-done");
            Invoke("TypingProgress");
            var original = Find("TypingHistoryResult_" + id);
            ((InvokePattern)original.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Until(delegate { return Visible("PracticeResult"); }, "reopened historical source result");
            Check(!Visible("TypingStartReview"), "Source history also prevents reopening a completed drill");
            var doc = ReviewProfile();
            var matches = doc.SelectNodes("/StudioProfile/Results/TypingResult[ReviewOf='" + id + "' and Completed='true']");
            Check(matches.Count == 1 && matches[0].SelectSingleNode("Errors").InnerText == "1", "One completed corrective result with its own error");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL WPF corrective exercise: " + e);
            try { DumpProcessWindows(); if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureLiveUxFrame("review-failure"); } catch (Exception) { }
            return 1;
        }
        finally
        {
            if (app != null) { if (!app.HasExited) app.Kill(); app.Dispose(); }
        }
    }
}

internal static class TypingReviewWindowEntry
{
    [STAThread]
    private static int Main(string[] args) { return LiveUiSmoke.VerifyCorrectionWindow(args); }
}
