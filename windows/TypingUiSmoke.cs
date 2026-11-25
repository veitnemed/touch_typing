// Companion entry point for the ordinary packaged EXE (no --qa or --typing).
// Shares the real SendInput, HWND ownership, UIA and foreground screenshot checks with LiveUiSmoke.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int RunTypingSession(string application, string directory)
    {
        try
        {
            string ordinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchTypingStudio");
            if (Directory.Exists(ordinary)) throw new InvalidOperationException("Use a fresh disposable Windows user, not an existing learner profile.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero) throw new InvalidOperationException("Cannot capture at native Windows DPI.");
            evidenceDirectory = Directory.CreateDirectory(directory).FullName;
            OpenTypingNormally(application);
            Key("F5");
            Until(delegate { return Visible("TypingSessionBar") && Exists("TypingTape"); }, "default finite bar plus visible ribbon");
            int total = TypingNumbers()[1];
            Check(TypingNumbers()[0] == 0 && total >= 350 && total <= 550, "ordinary home → Печать defaults to one finite 350–550 character lesson");
            CaptureReferenceFrame("01-classic-default");
            ReturnToTypingCards(); VolumeAndWords(1);
            Key("F5");
            Until(delegate { return Exists("TypingTape"); }, "two-cycle word practice");
            var help = Find("TypingCount").Current.HelpText;
            var info = Regex.Match(help, @"\. (\d+) действий");
            Check(info.Success, "accessible cycle scope: " + help);
            int first = int.Parse(info.Groups[1].Value, CultureInfo.InvariantCulture);
            total = TypingNumbers()[1];
            Check(first >= 350 && first <= 550 && total-first >= 350 && total-first <= 550, "both cycle lengths set at start");
            Key("1"); Check(TypingNumbers()[0] == 0, "wrong character did not advance accepted progress");
            ReachTyping(first); Check(TypingNumbers()[1] == total, "whole-session denominator at cycle boundary");
            CaptureReferenceFrame("02-cycle-boundary");
            ReachTyping(first + 1); CaptureReferenceFrame("03-next-character");
            Key("F8"); Key("1");
            Check(TypingNumbers()[0] == first + 1, "pausing does not advance or reset cursor");
            Key("Space");
            string next = Name("TypingTape");
            CloseTypingNormally(); OpenTypingNormally(application);
            Until(delegate { return Visible("TypingContinue"); }, "saved draft on real restart");
            Invoke("TypingContinue");
            Until(delegate { return Exists("TypingTape"); }, "reopened lesson");
            Check(TypingNumbers()[0] == first+1 && TypingNumbers()[1] == total && Name("TypingTape") == next,
                  "restarted ordinary profile retained the exact ribbon and overall progress");
            ReachTyping(first + 2); CaptureReferenceFrame("04-restored-and-continued");
            ReturnToTypingCards(); VolumeAndWords(5); Key("F5");
            Until(delegate { return Exists("TypingCount") && Name("TypingCount") == "Набрано: 0"; }, "explicit infinite mode");
            char upcoming = Name("TypingTape")[0];
            Send(Character(upcoming));
            Until(delegate { return Name("TypingCount") == "Набрано: 1"; }, "infinite accepted counter");
            Check(!Exists("TypingSessionBar") || !Visible("TypingSessionBar"), "infinite removes bar instead of resetting its percentage");
            CaptureReferenceFrame("05-infinite-no-bar");
            CloseTypingNormally();
            Console.WriteLine("PASS " + count + " real WPF checks. Input: synthetic Win32 scan codes, not physical hardware.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            if (app != null && !app.HasExited)
            {
                try { DumpProcessWindows(); if (GetForegroundWindow() == trainer) CaptureReferenceFrame("failure"); }
                catch (Exception details) { Console.Error.WriteLine(details.Message); }
            }
            return 1;
        }
        finally { if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); } }
    }

    private static void OpenTypingNormally(string path)
    {
        app = Process.Start(new ProcessStartInfo(path) { WorkingDirectory=Path.GetDirectoryName(path), UseShellExecute=false });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "ordinary packaged window", 18000);
        window = AutomationElement.FromHandle(trainer);
        Check(window.Current.ProcessId == app.Id, "owned ordinary WPF process");
        Focus(); Until(delegate { return Visible("HomeDashboard"); }, "default ordinary home");
        TypingProofClient.Fit(app, trainer);
        uint pid; uint thread = GetWindowThreadProcessId(trainer, out pid);
        SetNativeLessonLayout(thread, "00000409", 0x0409);
        Chord("Alt+2");
        Until(delegate { return Visible("TypingStart"); }, "ordinary Печать catalog");
    }
    private static void CloseTypingNormally()
    {
        RequestClose(); Until(delegate { return ExitDialog() != null; }, "save-and-close dialog");
        InvokeDialog("LabExitSave");
        if (!app.WaitForExit(10000) || app.ExitCode != 0) throw new InvalidOperationException("Application did not save and exit normally.");
        app.Dispose(); app = null;
    }
    private static int[] TypingNumbers()
    { return Name("TypingCount").Split('/').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(); }
    private static void ReachTyping(int target)
    {
        while (TypingNumbers()[0] < target)
        {
            int start = TypingNumbers()[0];
            string tape = Name("TypingTape");
            if (tape.Length < 2 || tape[1] != ' ') throw new InvalidOperationException("No current character on the ribbon.");
            // The peer publishes current token + a separating space + remaining visible text.
            string shown = tape[0] + tape.Substring(2);
            string batch = shown.Substring(0, Math.Min(10, Math.Min(target - start, shown.Length)));
            Check(batch.All(c => c == ' ' || ('a' <= c && c <= 'z')), "actual English words on ribbon");
            Send(batch.SelectMany(Character).ToArray());
            Until(delegate { return TypingNumbers()[0] == start + batch.Length; }, "scan codes accepted " + batch.Length + " letters/spaces");
        }
    }
    private static void ReturnToTypingCards()
    {
        Key("Esc"); Until(delegate { return Visible("TypingChoose"); }, "result window");
        Invoke("TypingChoose"); Until(delegate { return Visible("TypingStart"); }, "Печать after result");
    }
    private static void VolumeAndWords(int index)
    {
        var settings = (ExpandCollapsePattern)Find("TypingParameters").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        if (settings.Current.ExpandCollapseState != ExpandCollapseState.Expanded) settings.Expand();
        Select("TypingCycles", index);
        var combo = Find("TypingMode");
        var dropdown = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        dropdown.Expand();
        AutomationElement pick = null;
        Until(delegate {
            pick = combo.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty,"Английские слова")));
            return pick != null;
        }, "Английские слова option");
        ((SelectionItemPattern)pick.GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); dropdown.Collapse();
    }
}
internal static class TypingMain
{
    [STAThread]
    private static int Main(string[] args) { return LiveUiSmoke.RunTypingSession(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])); }
}
