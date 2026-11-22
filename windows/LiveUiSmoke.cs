using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

// Compile separately. This process drives only the new --qa trainer process it owns.
internal static class LiveUiSmoke
{
    private static Process app;
    private static IntPtr trainer, focusProbe;
    private static AutomationElement window;
    private static int count;

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 0) { Console.Error.WriteLine("Pass the full path to TouchTyping.exe."); return 2; }
        try
        {
            string path = Path.GetFullPath(arguments[0]);
            Console.WriteLine("UI test seed " + Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_SEED"));
            app = Process.Start(new ProcessStartInfo(path, "--qa --live") { WorkingDirectory = Path.GetDirectoryName(path) });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "trainer window", 12000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "The automation root belongs to the new QA process");
            Focus();
            Chord("Ctrl+F5");
            Check(Exists("LabStart") && !Exists("LabClock"), "Modified F5 keeps the class chooser open");
            // Keep key/cursor evidence independent of later window-close assertions.
            Invoke("LabClass-terminal");
            Select("LabMode", 0);
            Select("LabDuration", 4);
            Key("F5");
            Until(delegate { return Exists("LabClock") && Name("LabPause").StartsWith("Пауза"); }, "focused terminal lesson");
            CheckPasteLayouts();
            CheckTerminalCursorRepeat();
            Key("F10");
            Until(delegate { return Exists("LabResult"); }, "focused terminal result");
            Invoke("LabChooseClass");
            Until(delegate { return Exists("LabStart"); }, "class chooser after focused terminal lesson");
            Console.WriteLine("PASS: focused terminal native input assertions");

            string[] apps = { "ubuntu", "chrome", "code", "terminal", "workday" };
            foreach (string name in apps)
                foreach (int mode in new[] { 0, 1 })
                {
                    Invoke("LabClass-" + name);
                    Select("LabMode", mode);
                    Select("LabDuration", 4);
                    Key("F5");
                    Until(delegate { return Exists("LabClock") && Name("LabPause").StartsWith("Пауза"); }, "active " + name + " lesson");
                    Check(Name("LabClock") == "03:00" || Name("LabClock") == "02:59", "A class starts with 180 seconds");
                    Check(Name("LabTask").Length > 0 && Exists("LabDesktop"), "Task and Ubuntu scene are accessible");
                    Key("F8");
                    Until(delegate { return Exists("LabPauseResult"); }, "pause result overlay");
                    Check(Name("LabPauseResult") == "Пауза", "Pause shows a Stamina-style result panel");
                    Check(Exists("ResultMetric-time") && Exists("ResultMetric-count") &&
                        Exists("ResultMetric-speed") && Exists("ResultMetric-accuracy"), "Pause shows four performance metrics");
                    string frozen = Name("LabClock");
                    Chord("Ctrl+F10");
                    Check(Exists("LabClock") && Exists("LabPauseResult"), "Modified F10 keeps the paused lesson open");
                    Wait(1100);
                    Check(Name("LabClock") == frozen, "The clock stops during a pause");
                    Find("LabPauseRestart").SetFocus();
                    Key("Tab");
                    var focused = AutomationElement.FocusedElement;
                    Check(focused != null && focused.Current.ProcessId == app.Id && focused.Current.AutomationId == "LabPauseContinue", "Tab reaches the pause controls");
                    Key("Enter");
                    Check(Exists("LabPauseResult"), "Enter does not resume the paused lesson");
                    Key("Space");
                    Until(delegate { return !Exists("LabPauseResult") && Name("LabPause").StartsWith("Пауза"); }, "resumed lesson");
                    RequestClose();
                    Until(delegate { return ExitDialog() != null; }, "save-and-close confirmation");
                    Check(!app.HasExited && Exists("LabPauseResult"), "Closing pauses the lesson and releases input");
                    InvokeDialog("LabExitCancel");
                    Until(delegate { return ExitDialog() == null && Exists("LabPauseResult"); }, "cancelled close");
                    Key("Space");
                    Until(delegate { return !Exists("LabPauseResult"); }, "resume after cancelled close");
                    CheckFocusLoss();
                    if (name == "ubuntu" && mode == 0)
                    {
                        Key("F8");
                        Until(delegate { return Exists("LabPauseResult"); }, "pause before keyboard restart");
                        Find("LabPauseRestart").SetFocus();
                        Key("Enter");
                        Until(delegate { return !Exists("LabPauseResult") && Exists("LabClock") &&
                            (Name("LabClock") == "03:00" || Name("LabClock") == "02:59"); }, "Enter restarts through the focused pause button");
                    }
                    if (name == "terminal")
                    {
                        CheckPasteLayouts();
                        CheckTerminalCursorRepeat();
                    }
                    VirtualEnglish();
                    Solve(24, false);
                    Key("F10");
                    Until(delegate { return Exists("LabResult"); }, "lesson result");
                    Check(Exists("ResultMetric-time") && Exists("ResultMetric-count") &&
                        Exists("ResultMetric-speed") && Exists("ResultMetric-accuracy"), "Result shows four performance metrics");
                    Check(Find("LabResult").Current.HelpText.Contains("Ошибки"), "Result exposes completion, mistakes and record");
                    if (name == "ubuntu" && mode == 0)
                    {
                        Key("Space");
                        Until(delegate { return !Exists("LabResult") && Exists("LabClock"); }, "Space replays from the result");
                        Key("F10");
                        Until(delegate { return Exists("LabResult"); }, "result after Space replay");
                    }
                    Invoke("LabChooseClass");
                    Until(delegate { return Exists("LabStart"); }, "class chooser after result");
                }
            if (arguments.Contains("--full-session"))
            {
                Invoke("LabClass-terminal");
                Select("LabMode", 1);
                Select("LabDuration", 4);
                Key("F5");
                Until(delegate { return Exists("LabClock"); }, "full timed session");
                VirtualEnglish();
                Solve(int.MaxValue, true);
                Until(delegate { return Exists("LabResult"); }, "three-minute completion", 10000);
                string result = Find("LabResult").Current.HelpText;
                Check(result.Contains("Цель занятия выполнена"), "A complete 180-second session is recorded");
                var match = Regex.Match(result, "Рекорд ([0-9]+)");
                Check(match.Success && int.Parse(match.Groups[1].Value) > 0, "A completed class has a positive record");
            }
            if (Exists("LabResult")) Invoke("LabChooseClass");
            Invoke("LabClass-ubuntu");
            Select("LabDuration", 4);
            Key("F5");
            Until(delegate { return Exists("LabClock"); }, "lesson before save-and-close");
            RequestClose();
            Until(delegate { return ExitDialog() != null; }, "save-and-close dialog");
            InvokeDialog("LabExitSave");
            var closing = Stopwatch.StartNew();
            while (!app.HasExited && closing.ElapsedMilliseconds < 6000) { Wait(80); app.Refresh(); }
            Check(app.HasExited, "Confirmed close exits the trainer");
            Console.WriteLine("PASS: " + count + " native Windows UI assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            try { DumpProcessWindows(); }
            catch (Exception diagnosticError) { Console.Error.WriteLine("Window diagnostics failed: " + diagnosticError.Message); }
            return 1;
        }
        finally
        {
            if (focusProbe != IntPtr.Zero) DestroyWindow(focusProbe);
            if (app != null)
            {
                try { if (!app.HasExited) app.Kill(); }
                catch (InvalidOperationException) { }
                app.Dispose();
            }
        }
    }

    private static void Solve(int attempts, bool full)
    {
        var timeout = Stopwatch.StartNew();
        int initial = ReadSolved();
        for (int i = 0; i < attempts && Exists("LabClock"); i++)
        {
            if (timeout.Elapsed.TotalSeconds > (full ? 195 : 25)) break;
            string chord = Find("LabKeys").Current.HelpText;
            Console.WriteLine("TASK " + Name("LabTask") + " | " + chord + " | " + Name("LabCounter"));
            if (chord.Length == 0)
            {
                string title = Name("LabTask");
                string text = Find("LabTask").Current.HelpText;
                foreach (char character in text)
                {
                    Stroke[] strokes = Character(character);
                    Send(strokes);
                    if (!Exists("LabClock") || Name("LabTask") != title) break;
                }
                if (Exists("LabClock") && Name("LabTask") == title) Key("Enter");
            }
            else
            {
                int before = ReadSolved();
                var strokes = Parse(chord);
                Send(strokes.Select(stroke => stroke.Down()).ToArray());
                if (chord == "Ctrl+Shift+V")
                {
                    Until(delegate { return !Exists("LabClock") || ReadSolved() != before; }, "Ctrl+Shift+V counts on keydown");
                    Check(true, "Ctrl+Shift+V counts while modifiers remain held");
                }
                Send(strokes.Reverse().Select(stroke => stroke.Up()).ToArray());
            }
            Wait(70);
        }
        if (!full)
        {
            Check(Exists("LabClock"), "The short exercise remains active until completion is requested");
            Check(ReadSolved() > initial, "Physical input completes the requested tasks");
        }
    }

    private static int ReadSolved()
    {
        var value = Regex.Match(Find("LabCounter").Current.HelpText, "Выполнено ([0-9]+)");
        return value.Success ? int.Parse(value.Groups[1].Value) : 0;
    }

    private static void VirtualEnglish()
    {
        // The captured Super+Space combination changes only the lesson's own layout.
        Chord("Super+Space");
        if (!Name("LabDesktop").Contains("Раскладка EN")) Chord("Super+Space");
        Check(Name("LabDesktop").Contains("Раскладка EN"), "Virtual text input uses the displayed English layout");
    }

    private static void CheckPasteLayouts()
    {
        uint process;
        uint thread = GetWindowThreadProcessId(trainer, out process);
        Check(process == app.Id, "Layout checks target only the QA trainer thread");
        IntPtr previous = GetKeyboardLayout(thread);
        try
        {
            foreach (string layout in new[] { "00000409", "00000419" })
            {
                IntPtr keyboard = LoadKeyboardLayout(layout, 0);
                if (keyboard == IntPtr.Zero) throw new InvalidOperationException("The native layout is unavailable: " + layout);
                SendMessage(trainer, 0x50, IntPtr.Zero, keyboard);
                Wait(90);
                foreach (string paste in new[] { "RCtrl+RShift+V", "LCtrl+LShift+V" })
                {
                    Chord(paste);
                    Check(Find("LabDesktop").Current.HelpText.Contains("pwd"), "Physical terminal paste on " + layout);
                    Chord("Ctrl+U");
                }
            }
        }
        finally { if (!app.HasExited) SendMessage(trainer, 0x50, IntPtr.Zero, previous); }
    }

    private static void CheckTerminalCursorRepeat()
    {
        VirtualEnglish();
        string initial = Find("LabDesktop").Current.HelpText;
        foreach (var character in "abcd") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial + "abcd"; }, "terminal input fixture");

        // Each pair contains two down events before one physical key-up.
        var left = Parse("Left").Single();
        Send(new[] { left.Down(), left.Down(), left.Up() });
        var delete = Parse("Delete").Single();
        Send(new[] { delete.Down(), delete.Down(), delete.Up() });
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial + "ab"; }, "Left repeat then right-hand Delete repeat");

        Send(new[] { left.Down() });
        Wait(110);
        var ctrl = Parse("LCtrl").Single();
        Send(new[] { ctrl.Down() });
        try
        {
            CheckFocusLoss(delegate { Send(new[] { ctrl.Up() }); });
            Send(new[] { left.Down() });
            Key("Delete");
            Check(Find("LabDesktop").Current.HelpText == initial + "a", "Held pre-pause arrow is ignored after focus regain");
        }
        finally { Send(new[] { left.Up(), ctrl.Up() }); }
        Key("Right");
        Key("Backspace");
        Until(delegate { return Find("LabDesktop").Current.HelpText == initial; }, "fresh editing keys work after focus recovery");
        Check(true, "Native terminal caret, repeated deletion and focus recovery");

        Chord("Ctrl+A");
        Send(Character('z'));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "z"; }, "modifier released in the other window does not stick");
        Chord("Ctrl+U");
        Until(delegate { return Find("LabDesktop").Current.HelpText == ""; }, "fresh Ctrl chord works after external release");
        foreach (var character in "echo qa") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "echo qa"; }, "terminal history fixture");
        Key("Enter");
        foreach (var character in "draft") Send(Character(character));
        Until(delegate { return Find("LabDesktop").Current.HelpText == "draft"; }, "terminal history draft");
        Key("Up");
        Until(delegate { return Find("LabDesktop").Current.HelpText == "echo qa"; }, "Up recalls previous terminal command");
        Key("Down");
        Until(delegate { return Find("LabDesktop").Current.HelpText == "draft"; }, "Down restores the exact terminal draft");
        Chord("Ctrl+U");
        Until(delegate { return Find("LabDesktop").Current.HelpText == ""; }, "terminal history fixture cleanup");
        Check(true, "Native terminal history restores a draft");
    }

    private static void CheckFocusLoss(Action whileAway = null)
    {
        if (focusProbe == IntPtr.Zero)
            focusProbe = CreateWindowEx(0, "STATIC", "TouchTyping UI check focus probe", 0x10cf0000,
                50, 50, 330, 110, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(focusProbe != IntPtr.Zero, "The test owns its focus probe");
        SetForegroundWindow(focusProbe);
        Until(delegate { return Exists("LabPauseResult"); }, "pause on focus loss");
        if (whileAway != null)
        {
            Check(GetForegroundWindow() == focusProbe, "External key release occurs in the separate probe window");
            whileAway();
            Wait(80);
        }
        Focus();
        Check(Exists("LabPauseResult"), "Returning focus keeps the lesson paused");
        Key("F8");
        Until(delegate { return !Exists("LabPauseResult") && Name("LabPause").StartsWith("Пауза"); }, "resume after focus return");
    }

    private static void Focus()
    {
        SetForegroundWindow(trainer);
        Until(delegate { return GetForegroundWindow() == trainer; }, "foreground QA window");
    }
    private static AutomationElement Find(string id)
    {
        var element = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (element == null) throw new InvalidOperationException("Missing UI element " + id);
        if (element.Current.ProcessId != app.Id) throw new InvalidOperationException("UI element belongs to a different process");
        return element;
    }
    private static bool Exists(string id)
    { return window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) != null; }
    private static void RequestClose()
    {
        if (!PostMessage(trainer, 0x10, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("WM_CLOSE could not be queued: Win32 " + Marshal.GetLastWin32Error());
    }
    private static List<IntPtr> ProcessWindows()
    {
        var handles = new List<IntPtr>();
        EnumWindows(delegate(IntPtr handle, IntPtr ignored)
        {
            uint process;
            GetWindowThreadProcessId(handle, out process);
            if (process == (uint)app.Id) handles.Add(handle);
            return true;
        }, IntPtr.Zero);
        return handles;
    }
    private static string WindowTitle(IntPtr handle)
    {
        var text = new StringBuilder(256);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }
    private static AutomationElement ExitDialog()
    {
        // Owned modal windows aren't consistently exposed as children of the UIA desktop.
        foreach (var handle in ProcessWindows())
        {
            if (handle == trainer || !IsWindowVisible(handle) || WindowTitle(handle) != "Завершить занятие?") continue;
            try
            {
                var dialog = AutomationElement.FromHandle(handle);
                if (dialog != null && dialog.Current.ProcessId == app.Id &&
                    dialog.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "LabExitCancel")) != null)
                    return dialog;
            }
            catch (ElementNotAvailableException) { /* closing between enumeration and query */ }
        }
        return null;
    }
    private static void DumpProcessWindows()
    {
        if (app == null) return;
        app.Refresh();
        Console.Error.WriteLine("QA process exited=" + app.HasExited +
            "; trainer=0x" + trainer.ToInt64().ToString("x") +
            "; foreground=0x" + GetForegroundWindow().ToInt64().ToString("x"));
        if (app.HasExited) { Console.Error.WriteLine("QA exit code=" + app.ExitCode); return; }
        foreach (var handle in ProcessWindows())
            Console.Error.WriteLine("QA HWND 0x" + handle.ToInt64().ToString("x") +
                " visible=" + IsWindowVisible(handle) + " enabled=" + IsWindowEnabled(handle) +
                " title=" + WindowTitle(handle));
        if (window != null)
            Console.Error.WriteLine("QA lesson clock=" + Exists("LabClock") + "; paused=" + Exists("LabPauseResult"));
    }
    private static void InvokeDialog(string id)
    {
        var dialog = ExitDialog();
        if (dialog == null) throw new InvalidOperationException("Missing save-and-close dialog");
        var button = dialog.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        if (button == null) throw new InvalidOperationException("Missing dialog action " + id);
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(130);
    }
    private static string Name(string id) { return Find(id).Current.Name; }
    private static void Invoke(string id)
    {
        ((InvokePattern)Find(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(130);
    }
    private static void Select(string id, int index)
    {
        var combo = Find(id);
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        Wait(80);
        var options = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Check(options.Count > index, "Mode options are accessible");
        ((SelectionItemPattern)options[index].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        Wait(80);
    }
    private static void Key(string name) { Chord(name); }
    private static void Chord(string chord)
    {
        var keys = Parse(chord);
        Send(keys.Select(stroke => stroke.Down()).Concat(keys.Reverse().Select(stroke => stroke.Up())).ToArray());
        Wait(110);
    }
    private static Stroke[] Parse(string chord)
    {
        return chord.Split('+').Select(token =>
        {
            switch (token)
            {
                case "Ctrl": case "LCtrl": return new Stroke(0x1d, false);
                case "RCtrl": return new Stroke(0x1d, true);
                case "Shift": case "LShift": return new Stroke(0x2a, false);
                case "RShift": return new Stroke(0x36, false);
                case "Alt": case "LAlt": return new Stroke(0x38, false);
                case "RAlt": return new Stroke(0x38, true);
                case "Super": case "LSuper": return new Stroke(0x5b, true);
                case "RSuper": return new Stroke(0x5c, true);
                case "Print": return new Stroke(0x37, true);
                case "Esc": return new Stroke(1, false);
                case "Enter": return new Stroke(0x1c, false);
                case "Space": return new Stroke(0x39, false);
                case "Tab": return new Stroke(0xf, false);
                case "Backspace": return new Stroke(0xe, false);
                case "Delete": return new Stroke(0x53, true);
                case "Up": return new Stroke(0x48, true);
                case "Down": return new Stroke(0x50, true);
                case "Left": return new Stroke(0x4b, true);
                case "Right": return new Stroke(0x4d, true);
                case "PageUp": return new Stroke(0x49, true);
                case "PageDown": return new Stroke(0x51, true);
                case "Home": return new Stroke(0x47, true);
                case "End": return new Stroke(0x4f, true);
            }
            int number;
            if (token.Length > 1 && token[0] == 'F' && int.TryParse(token.Substring(1), out number))
                return new Stroke(number <= 10 ? 0x3a + number : number == 11 ? 0x57 : 0x58, false);
            string[] rows = { "1234567890-=", "QWERTYUIOP[]", "ASDFGHJKL;'`", "ZXCVBNM,./" };
            int[] starts = { 2, 0x10, 0x1e, 0x2c };
            for (int i = 0; i < rows.Length; i++)
            {
                int position = rows[i].IndexOf(token, StringComparison.Ordinal);
                if (position >= 0 && token.Length == 1) return new Stroke(starts[i] + position, false);
            }
            if (token == "\\") return new Stroke(0x2b, false);
            throw new ArgumentException("Unknown physical key " + token);
        }).ToArray();
    }
    private static Stroke[] Character(char value)
    {
        if (value == '\n') return Parse("Enter").SelectMany(stroke => new[] { stroke.Down(), stroke.Up() }).ToArray();
        if (value == ' ') return Parse("Space").SelectMany(stroke => new[] { stroke.Down(), stroke.Up() }).ToArray();
        const string plain = "`1234567890-=[]\\;',./";
        const string shift = "~!@#$%^&*()_+{}|:\"<>?";
        int index = shift.IndexOf(value);
        bool upper = char.IsUpper(value) || index >= 0;
        string key = index >= 0 ? plain[index].ToString() : char.ToUpperInvariant(value).ToString();
        var strokes = Parse((upper ? "Shift+" : "") + key);
        return strokes.Select(stroke => stroke.Down()).Concat(strokes.Reverse().Select(stroke => stroke.Up())).ToArray();
    }
    private static void Send(Stroke[] strokes)
    {
        if (GetForegroundWindow() != trainer || app.HasExited)
            throw new InvalidOperationException("Input stopped because the QA trainer lost foreground focus.");
        var inputs = strokes.Select(stroke => new Input { Type = 1, Data = new InputData {
            Keyboard = new KeyboardInput { Scan = (ushort)stroke.Scan, Flags = 8u | (stroke.Extended ? 1u : 0u) | (stroke.Released ? 2u : 0u) } } }).ToArray();
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length)
            throw new InvalidOperationException("SendInput could not deliver all test strokes.");
        Wait(60);
    }
    private static void Check(bool condition, string text)
    { if (!condition) throw new InvalidOperationException(text); count++; Console.WriteLine("PASS " + text); }
    private static void Until(Func<bool> condition, string text, int timeout = 6000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeout)
        {
            if (app.HasExited) throw new InvalidOperationException("QA trainer exited while waiting for " + text);
            if (condition()) return;
            Wait(80);
        }
        throw new TimeoutException("Timed out waiting for " + text);
    }
    private static void Wait(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Message message;
            while (PeekMessage(out message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
            Thread.Sleep(10);
        } while (clock.ElapsedMilliseconds < milliseconds);
    }
    private struct Stroke
    {
        internal int Scan;
        internal bool Extended, Released;
        internal Stroke(int scan, bool extended) { Scan = scan; Extended = extended; Released = false; }
        internal Stroke Down() { return new Stroke(Scan, Extended); }
        internal Stroke Up() { var copy = this; copy.Released = true; return copy; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] internal KeyboardInput Keyboard; [FieldOffset(0)] internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { internal IntPtr Window; internal uint Id; internal UIntPtr WParam; internal IntPtr LParam; internal uint Time; internal int X, Y; internal uint Private; }
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string layout, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr ignored);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr ignored);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extra, string type, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
}
