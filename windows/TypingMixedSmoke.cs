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
    private static void MixedGlyph(char glyph, bool ru)
    {
        // Use the keyboard positions, not Unicode injection or clipboard text.
        // At Windows HKL 0409/0419 the same positions yield different glyphs.
        const string lower = "ёйцукенгшщзхъфывапролджэячсмитьбю";
        const string scans = "`QWERTYUIOP[]ASDFGHJKL;'ZXCVBNM,.";
        const string signsRu = ")!\"№;%:?*(", signsEn = ")!@#$%^&*(";
        int letter = lower.IndexOf(char.ToLowerInvariant(glyph));
        int sign = (ru ? signsRu : signsEn).IndexOf(glyph);
        if (letter >= 0) Chord((char.IsUpper(glyph) ? "Shift+" : "") + scans[letter]);
        else if (sign >= 0) Chord("Shift+" + sign.ToString(CultureInfo.InvariantCulture));
        else Send(Character(glyph));
    }
    private static void MixedStep(uint thread, string atom, string group)
    {
        int before = MixedCount();
        if (atom[0] == '⟦') Chord(atom.Substring(1, atom.Length-2).Replace("Win+", "Super+"));
        else
        {
            bool ru = group == "RU";
            MixedLayout(thread, ru);
            MixedGlyph(atom[0], ru);
        }
        try { Until(delegate { return MixedCount() == before+1; }, "one action for " + atom + " in " + group); }
        catch (TimeoutException e)
        {
            throw new InvalidOperationException("Mixed scan at HKL 0x" + NativeLanguage(thread).ToString("X4") +
                ": expected " + atom + ", before " + before + ", current " + MixedCount() +
                ", next " + MixedAtom(), e);
        }
    }
    private static void MixedLandmark(string title)
    {
        Wait(180);
        CaptureReferenceFrame(title);
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_VIDEO") == "1") Wait(2300);
    }
    private static bool MixedPaused()
    {
        var texts = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        return texts.Cast<AutomationElement>().Any(t => !t.Current.IsOffscreen &&
            t.Current.Name.StartsWith("Пауза.", StringComparison.Ordinal));
    }
    private static void FocusMixedNumLockProbe()
    {
        if (focusProbe == IntPtr.Zero)
            focusProbe = CreateWindowEx(0, "STATIC", "TouchTyping mixed Num Lock probe", 0x10cf0000,
                50, 50, 330, 110, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        uint owner;
        Check(focusProbe != IntPtr.Zero && GetWindowThreadProcessId(focusProbe, out owner) != 0 &&
            owner == (uint)Process.GetCurrentProcess().Id, "test controls only its own Num Lock probe");
        SetForegroundWindow(focusProbe);
        Until(delegate { return GetForegroundWindow() == focusProbe; }, "owned Num Lock probe");
        Wait(100);
    }
    private static void MixedNumLock(bool enabled)
    {
        int before = MixedCount();
        FocusMixedNumLockProbe();
        Until(MixedPaused, "the typing lesson paused when the owned probe took focus");
        if (!originalNumLock.HasValue) originalNumLock = ProbeNumLock();
        SetProbeNumLock(enabled);
        Focus();
        Check(MixedPaused() && MixedCount()==before, "returning from Num Lock probe preserves position and pause");
        Key("F8");
        Until(delegate { return !MixedPaused(); }, "resume typing after Num Lock probe");
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
            previous = GetKeyboardLayout(thread);
            MixedLayout(thread, false);
            Chord("Alt+2");
            Until(delegate { return Visible("TypingStart"); }, "catalog through Alt+2");
            foreach (string mode in new[] {"Digits","TopRow","Hotkeys","WordsDigits","WordsDigitsHotkeys"})
                Check(Exists("TypingRow-"+mode), "visible catalog route: " + mode);
            MixedLandmark("wpf-09-mode-list");

            MixedChoose("WordsDigitsHotkeys");
            Check(!Visible("TypingKeyboard"), "Fresh typing aids leave the work area clear");
            Key("F4");
            Until(delegate { return Visible("TypingKeyboard"); }, "keyboard aid enabled explicitly");
            var combinations = new HashSet<string>();
            for (int i=0; i<65 && combinations.Count<4; i++)
            {
                string atom = MixedAtom(), layout = MixedLayoutTitle();
                Check(layout=="RU" || layout=="EN", "visible exercise layout in the header");
                bool newPair = combinations.Add((atom[0]=='⟦' ? "command/" : "text/")+layout);
                if (newPair && atom[0]=='⟦') MixedLandmark("wpf-10-mixed-chord-"+layout);
                if (newPair && atom[0]!='⟦' && layout=="RU")
                {
                    // The very first character after the accepted physical shortcut.
                    // Wrong language must not pass just because the scan position fits.
                    int before = MixedCount();
                    MixedLayout(thread, false);
                    MixedGlyph(atom[0], true);
                    Check(MixedCount()==before && MixedAtom()==atom,
                        "wrong-layout glyph after chord does not advance the Russian exercise");
                    MixedStep(thread, atom, layout);
                    Check(MixedCount()==before+1, "first composed Russian character after chord is accepted once");
                    MixedLandmark("wpf-11-mixed-russian");
                }
                else MixedStep(thread, atom, layout);
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

            MixedLayout(thread, false);
            foreach (var pair in new[] { new[] {"LAlt+Right", "RAlt+Right"}, new[] {"RAlt+D", "LAlt+D"} })
            {
                string atom = "⟦" + pair[0] + "⟧";
                Check(MixedAtom() == atom, "default course includes explicit " + pair[0]);
                int before = MixedCount();
                Chord(pair[1]);
                Check(MixedCount() == before && MixedAtom() == atom, "opposite-side Alt cannot score " + pair[0]);
                MixedStep(thread, atom, MixedLayoutTitle());
            }
            var everyday = new HashSet<string>(StringComparer.Ordinal);
            for (int i=0; i<11; i++)
            {
                string atom = MixedAtom();
                everyday.Add(atom.Trim('⟦','⟧'));
                MixedStep(thread, atom, MixedLayoutTitle());
            }
            Check(everyday.SetEquals(new[] {"Ctrl+F","Ctrl+Y","Ctrl+T","Ctrl+W","Ctrl+Shift+T",
                "Ctrl+Left","Ctrl+Right","Ctrl+Backspace","Ctrl+Delete","Shift+Home","Shift+End"}),
                "ordinary Hotkeys card delivers the 11 everyday extensions");
            foreach (string alt in new[] {"Alt+Home", "Alt+Right"})
            {
                Check(MixedAtom() == "⟦" + alt + "⟧", "ordinary Hotkeys includes " + alt);
                MixedStep(thread, MixedAtom(), MixedLayoutTitle());
            }

            MixedChoose("Digits");
            int digit = int.Parse(MixedAtom(), CultureInfo.InvariantCulture);
            int[] keypad = {0x52,0x4f,0x50,0x51,0x4b,0x4c,0x4d,0x47,0x48,0x49};
            var k = new Stroke(keypad[digit], false);
            MixedNumLock(false); Send(new[] {k.Down(),k.Up()});
            Check(MixedCount()==0, "NumLock off: keypad navigation is not a typed digit");
            MixedNumLock(true); Send(new[] {k.Down(),k.Up()});
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
            MixedChoose("SystemHotkeys");
            MixedLandmark("wpf-system-typing-start");
            var captured = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 11; i++)
            {
                string atom = MixedAtom();
                Check(atom.StartsWith("⟦", StringComparison.Ordinal), "system mode has only physical chords");
                captured.Add(atom.Trim('⟦', '⟧'));
                if (atom == "⟦Win+Alt+1⟧")
                {
                    int before = MixedCount();
                    Chord("Alt+1"); Chord("Super+1"); Wait(100);
                    Check(MixedCount() == before && GetForegroundWindow() == trainer,
                        "partial taskbar gestures neither score nor leave the protected foreground");
                }
                MixedStep(thread, atom, MixedLayoutTitle());
                Check(GetForegroundWindow() == trainer, "system gesture remains inside the trainer: " + atom);
            }
            Check(captured.Contains("Win+E") && captured.Contains("Win+Alt+1") && captured.Contains("Win+I") && captured.Contains("Alt+Tab") && !captured.Contains("Win+H"),
                "native Windows typing recognizes Win+E and Alt+Tab and does not assign speech hotkey");
            int beforeFocus = MixedCount();
            FocusMixedNumLockProbe();
            Until(MixedPaused, "system Typing pauses when the owned probe gets focus");
            Focus();
            Check(MixedPaused() && MixedCount() == beforeFocus, "system Typing does not auto-resume after focus loss");
            Key("F8");
            Until(delegate { return !MixedPaused(); }, "explicit F8 resumes system Typing");
            MixedStep(thread, MixedAtom(), MixedLayoutTitle());
            MixedLandmark("wpf-system-typing-credited");
            while (MixedCount() < 43) MixedStep(thread, MixedAtom(), MixedLayoutTitle());
            string finalSystem = MixedAtom().Trim('⟦', '⟧').Replace("Win+", "Super+");
            Chord(finalSystem);
            Until(delegate { return Visible("TypingStartReview"); }, "system errors offer a corrective exercise");
            Invoke("TypingStartReview");
            Until(delegate { return Visible("TypingCount"); }, "captured correction opened");
            Check(MixedCount() == 0 && MixedAtom() == "⟦Win+Alt+1⟧", "only the missed gesture is in the generated exercise");
            MixedStep(thread, MixedAtom(), MixedLayoutTitle());
            FocusMixedNumLockProbe(); Until(MixedPaused, "captured correction pauses when focus leaves");
            Focus(); Key("F8"); Until(delegate { return !MixedPaused(); }, "correction reacquires capture on resume");
            MixedStep(thread, MixedAtom(), MixedLayoutTitle());
            Check(MixedCount() == 2 && GetForegroundWindow() == trainer, "capture does not leak taskbar action");
            Key("Esc");
            Until(delegate { return Visible("TypingChoose"); }, "captured corrective exercise can be suspended");
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
                if (originalNumLock.HasValue && (GetForegroundWindow()==trainer || GetForegroundWindow()==focusProbe))
                {
                    try { FocusMixedNumLockProbe(); SetProbeNumLock(originalNumLock.Value); Focus(); }
                    catch(Exception cleanup) { Console.Error.WriteLine("Num Lock restore: " + cleanup.Message); }
                }
                if (previous!=IntPtr.Zero) SendMessage(trainer,0x50,IntPtr.Zero,previous);
                try { RequestClose(); Until(delegate { return ExitDialog()!=null; }, "close confirmation",3000); InvokeDialog("LabExitSave"); app.WaitForExit(4000); }
                catch(Exception cleanup) { Console.Error.WriteLine(cleanup.Message); }
                if (!app.HasExited) { app.Kill(); app.WaitForExit(4000); }
            }
            if (focusProbe != IntPtr.Zero) DestroyWindow(focusProbe);
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
