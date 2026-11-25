// Optional out-of-process observer of a normal packaged application. No --qa,
// application IPC, injected profile, clipboard, or Unicode packet is used.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Xml;

internal static partial class LiveUiSmoke
{
    private const string WorkNoteMode = "lesson:work-note-bilingual";
    private const string WorkNoteTitle = "Заметка коллеге · RU / EN";
    // The first authored case is part of the acceptance contract. The observer
    // uses the untouched application UI for every position, not an engine API.
    private const string WorkNoteText = "Тема: передача отчёта № 204\n"
        + "Привет! Таблицу за 07.12 я положила в reports/weekly.csv. В ней 28 строк, две проверки пока не закрыты. На листе итогов зафиксирована сумма 1 240,50 руб.\n"
        + "Открой задачу QA-204 и перед встречей в 16:30 проверь две последние строки. Если суммы не совпадут, напиши на ops@example.test с темой QA-204.\n"
        + "Status: ready for review; don't replace the source file.";

    private static bool WorkNoteHeaderFocused()
    {
        for (AutomationElement node = AutomationElement.FocusedElement;
             node != null && node.Current.ProcessId == app.Id;
             node = TreeWalker.RawViewWalker.GetParent(node))
            if (node.Current.AutomationId == "TypingMore") return true;
        return false;
    }

    private static void EnterWorkNoteFromCatalog()
    {
        var more = (ExpandCollapsePattern)Find("TypingMore").GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Check(more.Current.ExpandCollapseState == ExpandCollapseState.Collapsed, "fresh supplementary catalog is collapsed");
        for (int tries = 0; tries < 42 && !WorkNoteHeaderFocused(); tries++) Key("Tab");
        Check(WorkNoteHeaderFocused(), "keyboard focus reached the supplementary catalog heading");
        Key("Space");
        Until(delegate { return more.Current.ExpandCollapseState == ExpandCollapseState.Expanded; },
              "Space expands the focused supplementary catalog instead of starting another lesson");
        string id = "TypingRow-" + WorkNoteMode;
        Until(delegate { return Exists(id); }, "the authored note's normal catalog card");
        var card = Find(id);
        object scroll;
        if (card.TryGetCurrentPattern(ScrollItemPattern.Pattern, out scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        card.SetFocus();
        Until(delegate { return Visible(id) && Find(id).Current.HasKeyboardFocus; }, "visible focused note card");
        Check(Name(id).Contains(WorkNoteTitle), "the accessible card has the expected author title");
        CaptureReferenceFrame("work-note-wpf-01-catalog");
        Key("Space");
        Until(delegate { return Visible("TypingTape") && Visible("TypingCount"); }, "normal note practice window");
        Check(Name("TypingLayoutLabel").Contains(WorkNoteTitle) && Name("TypingCount") == "0/381",
              "correct ordinary note and bounded counter; not the default prose exercise");
    }

    // Resolve only direct, main-section strokes in the HWND's actual HKL.
    // Use the Windows keyboard table, independently of the product's hints.
    // Never emit Unicode packets or click into an OS-level input panel.
    private static Stroke[] WorkNoteStroke(char glyph, IntPtr layout)
    {
        if (glyph == '\n') return new[] { new Stroke(0x1c, false), new Stroke(0x1c, false).Up() };
        short key = VkKeyScanExW(glyph, layout);
        if (key == -1) return null;
        uint vk = (uint)(key & 0xff), modifiers = (uint)((key >> 8) & 0xff);
        if (modifiers > 1) return null; // the sample needs no AltGr, dead keys or IME
        uint scan = MapVirtualKeyExW(vk, 4, layout);
        if (scan == 0 || scan > 0xff) return null;
        var state = new byte[256];
        if (modifiers != 0) state[0x10] = state[0xa0] = 0x80; // VK_SHIFT, VK_LSHIFT
        var text = new StringBuilder(5);
        if (ToUnicodeEx(vk, scan, state, text, text.Capacity, 4, layout) != 1 || text[0] != glyph) return null;
        var character = new Stroke((int)scan, false);
        if (modifiers == 0) return new[] { character, character.Up() };
        var shift = new Stroke(0x2a, false);
        return new[] { shift, character, character.Up(), shift.Up() };
    }

    private static void WorkNoteLayout(uint thread, bool ru)
    {
        if (GetForegroundWindow() != trainer) throw new InvalidOperationException("Owned note window lost keyboard focus.");
        int wanted = ru ? 0x0419 : 0x0409;
        if (NativeLanguage(thread) != wanted) SetNativeLessonLayout(thread, ru ? "00000419" : "00000409", wanted);
    }

    private static void CheckWorkNoteTape(int start, int length)
    {
        string ribbon = Name("TypingTape");
        // The production accessibility peer describes the selected glyph, a
        // separator and the following ribbon. The terminal glyph may have no tail.
        if (string.IsNullOrEmpty(ribbon) || (ribbon.Length > 1 && ribbon[1] != ' '))
            throw new InvalidOperationException("Unexpected native typing ribbon at " + start + ": " + ribbon);
        var actual = ribbon[0] + (ribbon.Length > 1 ? ribbon.Substring(2) : "");
        var expected = WorkNoteText.Substring(start, length).Replace("\n", "↵");
        if (!actual.StartsWith(expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Native typing ribbon mismatch at " + start + ": expected [" + expected + "] in [" + actual + "].");
    }

    private static void CheckWorkNoteSaved(string profile)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(profile); // read-only, after the ordinary process closed
        var attempts = document.SelectNodes("/StudioProfile/Results/TypingResult[Task='" + WorkNoteMode + "']");
        Check(attempts.Count == 1, "real ordinary profile has exactly one saved note outcome");
        var result = attempts[0];
        Check(result.SelectSingleNode("Completed").InnerText == "true" &&
              result.SelectSingleNode("Count").InnerText == "381" &&
              result.SelectSingleNode("Errors").InnerText == "1" &&
              result.SelectSingleNode("Activity").InnerText == "TypingPractice",
              "saved result is the completed 381-glyph note with exactly one earlier wrong glyph");
    }

    internal static int RunOrdinaryWorkNote(string executable, string output)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_DISPOSABLE_USER") != "1")
                throw new InvalidOperationException("Use the isolated Windows runner or an explicitly disposable Windows user.");
            string ordinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchTypingStudio");
            if (Directory.Exists(ordinary)) throw new InvalidOperationException("Ordinary profile already exists; it will not be opened or cleared.");
            if (WorkNoteText.Length != 381) throw new InvalidOperationException("Review the first authored case and observer together.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero) throw new InvalidOperationException("Cannot capture at native monitor DPI.");
            if ((GetKeyState(0x14) & 1) != 0) throw new InvalidOperationException("Start the disposable image with Caps Lock off.");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            OpenTypingNormally(executable); // existing observer: exact EXE, no arguments, normal Alt+2 catalog
            uint owner;
            uint thread = GetWindowThreadProcessId(trainer, out owner);
            Check(thread != 0 && owner == (uint)app.Id && NativeLanguage(thread) == 0x0409,
                  "owned HWND with the native English-US keyboard layout");
            EnterWorkNoteFromCatalog();
            CheckWorkNoteTape(0, 1);
            var wrong = WorkNoteStroke('T', GetKeyboardLayout(thread));
            if (wrong == null) throw new InvalidOperationException("Runner lacks the requested EN-US T stroke.");
            Send(wrong); // the note starts with Cyrillic Т, not Latin T
            Check(Name("TypingCount") == "0/381", "Latin T did not advance the Cyrillic Т position");
            CheckWorkNoteTape(0, 1);

            bool ru = false;
            int changes = 0;
            var landmarks = new Dictionary<int, string> {
                { 0, "work-note-wpf-02-cyrillic" },
                { WorkNoteText.IndexOf("reports/weekly.csv", StringComparison.Ordinal), "work-note-wpf-03-path" },
                { WorkNoteText.IndexOf("@example.test", StringComparison.Ordinal), "work-note-wpf-04-email" }
            };
            for (int position = 0; position < WorkNoteText.Length;)
            {
                IntPtr layout = GetKeyboardLayout(thread);
                if (WorkNoteStroke(WorkNoteText[position], layout) == null)
                {
                    ru = !ru;
                    WorkNoteLayout(thread, ru);
                    layout = GetKeyboardLayout(thread);
                    changes++;
                }
                if (NativeLanguage(thread) != (ru ? 0x0419 : 0x0409))
                    throw new InvalidOperationException("Keyboard layout changed outside the probe at position " + position);
                string landmark;
                if (landmarks.TryGetValue(position, out landmark))
                {
                    string expectedLayout = ru ? "Русская" : "Английская";
                    Check(Name("TypingLayoutLabel").Contains(expectedLayout), "native note ribbon at " + position + " identifies " + expectedLayout);
                    CaptureReferenceFrame(landmark);
                }
                int before = position;
                var strokes = new List<Stroke>();
                // Do not mix the final full stop in a batch or let a checkpoint
                // get skipped merely because the paint occurred after the batch.
                while (position < WorkNoteText.Length && position - before < 10)
                {
                    if (position > before && (landmarks.ContainsKey(position) || position == WorkNoteText.Length - 1)) break;
                    var next = WorkNoteStroke(WorkNoteText[position], layout);
                    if (next == null) break;
                    strokes.AddRange(next);
                    position++;
                }
                if (position == before) throw new InvalidOperationException("No direct native RU/EN stroke at " + before + ": " + WorkNoteText[before]);
                CheckWorkNoteTape(before, position - before);
                Send(strokes.ToArray());
                if (position < WorkNoteText.Length)
                {
                    int target = position;
                    Until(delegate { return Exists("TypingCount") && Name("TypingCount") == target + "/381"; },
                          "exact note counter " + target + "/381, layout " + (ru ? "RU" : "EN"));
                }
            }
            Check(changes >= 6, "native RU/EN switches across independent letter blocks: " + changes);
            Until(delegate { return Visible("PracticeResult"); }, "final full stop opened actual result on its own");
            Check(Find("PracticeResult").Current.HelpText.Contains("381") &&
                  Find("PracticeResult").Current.HelpText.Contains("Ошибки 1"), "rendered note result contains 381 accepted glyphs and one miss");
            CaptureReferenceFrame("work-note-wpf-05-finished");
            WorkNoteLayout(thread, false);
            RequestClose(); UntilExit();
            Check(app.ExitCode == 0, "normal close with no unfinished lesson");
            CheckWorkNoteSaved(Path.Combine(ordinary, "progress.xml"));
            Console.WriteLine("PASS ordinary WPF bilingual note, real Win32 scan codes and HKLs; not physical keyboard hardware.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL WPF bilingual note: " + ex);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("work-note-wpf-failure"); }
            catch (Exception details) { Console.Error.WriteLine(details.Message); }
            return 1;
        }
        finally
        {
            if (app != null)
            {
                if (!app.HasExited) { app.Kill(); app.WaitForExit(4000); } // only the owned disposable application
                app.Dispose();
            }
        }
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanExW(char glyph, IntPtr layout);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyExW(uint code, uint mode, IntPtr layout);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out] StringBuilder result, int count, uint flags, IntPtr layout);
}

internal static class TypingWorkNoteMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2) return LiveUiSmoke.RunOrdinaryWorkNote(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
        Console.Error.WriteLine("Usage: TypingWorkNoteSmoke.exe TouchTyping.exe evidence-folder");
        return 2;
    }
}
