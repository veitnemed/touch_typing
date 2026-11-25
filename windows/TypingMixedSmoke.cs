// Standalone probe for the installed/extracted WPF UI, on a fresh isolated QA
// profile. Observes only public UI Automation and the real foreground HWND.
// All learner actions enter as physical Win32 scan codes; no engine API calls.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    private static int MixedCount()
    {
        return int.Parse(Name("TypingCount").Split('/')[0], CultureInfo.InvariantCulture);
    }
    private static string MixedAtom()
    {
        string tape = Name("TypingTape");
        if (string.IsNullOrEmpty(tape)) throw new InvalidOperationException("Missing native exercise ribbon.");
        if (tape[0] != '⟦') return tape.Substring(0, 1);
        Match match = Regex.Match(tape, "^⟦([^⟧]+)⟧");
        if (!match.Success) throw new InvalidOperationException("Broken command on native ribbon: " + tape);
        return match.Value;
    }
    private static void MixedLayout(uint thread, bool ru)
    {
        if (NativeLanguage(thread) != (ru ? 0x419 : 0x409))
            SetNativeLessonLayout(thread, ru ? "00000419" : "00000409", ru ? 0x0419 : 0x0409);
    }
    private static string MixedLayoutTitle()
    {
        string title = Name("TypingLayoutLabel");
        return title.Contains("Русская") ? "RU" : title.Contains("Английская") ? "EN" : "";
    }
    private static void MixedChoose(string mode)
    {
        if (Exists("TypingTape"))
        {
            Key("Esc"); Until(delegate { return Visible("TypingChoose"); }, "result window after Esc");
            Invoke("TypingChoose");
        }
        string card = "TypingRow-" + mode;
        Until(delegate { return Exists(card); }, card);
        var item = Find(card);
        object scroll;
        if (item.TryGetCurrentPattern(ScrollItemPattern.Pattern, out scroll))
            ((ScrollItemPattern)scroll).ScrollIntoView();
        item.SetFocus(); Key("Space");
        Until(delegate { return Visible("TypingTape") && Visible("TypingCount"); }, mode + " started via card + Space");
        Check(MixedCount() == 0, mode + " starts at zero");
    }
    private static void MixedStep(uint thread, string atom, string group)
    {
        int before = MixedCount();
        if (atom[0] == '⟦') Chord(atom.Substring(1, atom.Length-2));
        else
        {
            // Known complete physical row mapping. Do not inject Unicode/paste:
            // keycode 0x2f = V physically, producing Latin v or Cyrillic м via HKL.
            char glyph = atom[0];
            bool ru = group == "RU";
            MixedLayout(thread, ru);
            const string lower = "ёйцукенгшщзхъфывапролджэячсмитьбю";
            const string scans = "`QWERTYUIOP[]ASDFGHJKL;'ZXCVBNM,.";
            const string signsRu = ")!\"№;%:?*(", signsEn = ")!@#$%^&*(";
            int letter = lower.IndexOf(char.ToLowerInvariant(glyph));
            int sign = (ru ? signsRu : signsEn).IndexOf(glyph);
            if (letter >= 0) Chord((char.IsUpper(glyph) ? "Shift+" : "") + scans[letter]);
            else if (sign >= 0) Chord("Shift+" + sign.ToString(CultureInfo.InvariantCulture));
            else Send(Character(glyph));
        }
        Until(delegate { return MixedCount() == before+1; }, "one action for " + atom + " in " + group);
    }
    private static void MixedLandmark(string title)
    {
        Wait(180);
        CaptureReferenceFrame(title);
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_VIDEO") == "1") Wait(2300);
    }
    internal static int RunTypingMixed(string executable, string target)
    {
        IntPtr previous = IntPtr.Zero;
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("A dedicated QA profile is mandatory.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot enable physical client capture.");
            evidenceDirectory = Directory.CreateDirectory(target).FullName;
            app = Process.Start(new ProcessStartInfo(executable, "--qa --qa-home") {
                WorkingDirectory=Path.GetDirectoryName(executable), UseShellExecute=false });
            Until(delegate { app.Refresh(); trainer=app.MainWindowHandle; return trainer != IntPtr.Zero; }, "owned WPF home", 18000);
            window = AutomationElement.FromHandle(trainer);
            uint pid, thread = GetWindowThreadProcessId(trainer, out pid);
            Check(window.Current.ProcessId == app.Id && pid == app.Id && thread != 0, "owned foreground UI thread");
            Focus(); Until(delegate { return Visible("HomeDashboard"); }, "fresh WPF home");
            TypingProofClient.Fit(app, trainer);
            previous = GetKeyboardLayout(thread); originalNumLock = ProbeNumLock();
            MixedLayout(thread, false);
            Chord("Alt+2");
            Until(delegate { return Visible("TypingStart"); }, "catalog through Alt+2");
            foreach (string mode in new[] {"Digits","TopRow","Hotkeys","WordsDigits","WordsDigitsHotkeys"})
                Check(Exists("TypingRow-"+mode), "visible catalog route: " + mode);
            MixedLandmark("wpf-09-mode-list");

            MixedChoose("WordsDigitsHotkeys");
            var combinations = new HashSet<string>();
            for (int i=0; i<65 && combinations.Count<4; i++)
            {
                string atom = MixedAtom(), layout = MixedLayoutTitle();
                Check(layout=="RU" || layout=="EN", "visible exercise layout in the header");
                bool newPair = combinations.Add((atom[0]=='⟦' ? "command/" : "text/")+layout);
                if (newPair && atom[0]=='⟦') MixedLandmark("wpf-10-mixed-chord-"+layout);
                if (newPair && atom[0]!='⟦' && layout=="RU") MixedLandmark("wpf-11-mixed-russian");
                MixedStep(thread, atom, layout);
            }
            Check(combinations.Count==4, "Cyrillic and Latin compositions, digits and chords alternate without skipping the next unit");

            MixedChoose("Hotkeys");
            bool ruV=false, enV=false, f2=false;
            for (int i=0; i<14; i++)
            {
                string atom = MixedAtom(), layout = MixedLayoutTitle();
                if (atom=="⟦Ctrl+V⟧")
                {
                    // Deliberately use opposite OS layout for the EN target; the
                    // trainer must retain the exercise alphabet and the English chord.
                    MixedLayout(thread, true);
                    string caps = Find("TypingKeyboard").Current.HelpText;
                    Check(caps.IndexOf("Ctrl",StringComparison.OrdinalIgnoreCase)>=0 &&
                        (layout=="RU" ? caps.IndexOf("м (V)",StringComparison.OrdinalIgnoreCase)>=0 :
                         caps.EndsWith(" + v",StringComparison.OrdinalIgnoreCase)),
                        "English chord and corresponding highlighted physical key on " + layout + ": " + caps);
                    int before=MixedCount();
                    Chord("RCtrl+B");
                    Check(MixedCount()==before && MixedAtom()=="⟦Ctrl+V⟧", "RCtrl+B / И is not V / М");
                    if(layout=="RU" && !ruV) MixedLandmark("wpf-12-ru-ctrl-v-not-b");
                    Chord("RCtrl+V");
                    Until(delegate { return MixedCount()==before+1; }, "physical right Ctrl+V on Russian HKL");
                    ruV |= layout=="RU"; enV |= layout=="EN";
                }
                else { f2 |= atom=="⟦F2⟧"; MixedStep(thread, atom, layout); }
            }
            Check(ruV && enV && f2, "Ctrl+V is displayed/accepted in both exercise layouts; physical F2 also works");

            MixedChoose("Digits");
            int digit = int.Parse(MixedAtom(), CultureInfo.InvariantCulture);
            int[] keypad = {0x52,0x4f,0x50,0x51,0x4b,0x4c,0x4d,0x47,0x48,0x49};
            var k = new Stroke(keypad[digit], false);
            SetProbeNumLock(false); Send(new[] {k.Down(),k.Up()});
            Check(MixedCount()==0, "NumLock off: keypad navigation is not a typed digit");
            SetProbeNumLock(true); Send(new[] {k.Down(),k.Up()});
            Until(delegate { return MixedCount()==1; }, "NumLock on: same physical switch produces the expected digit");
            foreach (bool ru in new[] {true,false})
            {
                MixedLayout(thread,ru); MixedChoose("TopRow");
                string layout=ru?"RU":"EN", expected=ru?"№":"#";
                Check(MixedLayoutTitle()==layout, "top-row exercise begins in active HKL " + layout);
                for (int i=0; i<20 && MixedAtom()!=expected; i++) MixedStep(thread,MixedAtom(),layout);
                Check(MixedAtom()==expected, "visible Shift+3 target " + expected);
                int before=MixedCount(); MixedLayout(thread,!ru);
                var shift = Parse("LShift").Single(); var three = Parse("3").Single();
                Send(new[] {shift.Down()});
                try
                {
                    Until(delegate { return Find("TypingKeyboard").Current.HelpText.Contains(expected+" (3)"); },
                          "held Shift shows " + expected + " on the actual cap for physical 3");
                    Send(new[] {three.Down(),three.Up()});
                }
                finally { Send(new[] {shift.Up()}); }
                Check(MixedCount()==before && MixedAtom()==expected, "wrong layout's same Shift+3 scan is not the requested character");
                MixedLayout(thread,ru); MixedStep(thread,expected,layout);
                MixedLandmark("wpf-13-shift3-"+layout);
            }
            MixedLayout(thread,false);
            Console.WriteLine("PASS " + count + " real WPF mixed checks; isolated profile, real HKLs, SendInput scan codes (no claim of physical laptop test).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (app!=null && !app.HasExited)
            {
                try { DumpProcessWindows(); if (GetForegroundWindow()==trainer) CaptureReferenceFrame("wpf-mixed-failure"); }
                catch(Exception extra) { Console.Error.WriteLine(extra.Message); }
            }
            return 1;
        }
        finally
        {
            if (app!=null && !app.HasExited)
            {
                if (GetForegroundWindow()==trainer)
                {
                    try { if(originalNumLock.HasValue) SetProbeNumLock(originalNumLock.Value); }
                    catch(Exception cleanup) { Console.Error.WriteLine(cleanup.Message); }
                }
                if (previous!=IntPtr.Zero) SendMessage(trainer,0x50,IntPtr.Zero,previous);
                try { RequestClose(); Until(delegate { return ExitDialog()!=null; }, "close confirmation",3000); InvokeDialog("LabExitSave"); app.WaitForExit(4000); }
                catch(Exception cleanup) { Console.Error.WriteLine(cleanup.Message); }
                if (!app.HasExited) { app.Kill(); app.WaitForExit(4000); }
            }
        }
    }
}
internal static class MixedMain
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length!=2) return 2;
        return LiveUiSmoke.RunTypingMixed(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
    }
}
