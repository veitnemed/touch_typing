using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int CheckNativeTraceback(string executable, string output)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")) ||
                SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("An isolated interactive WPF profile is required.");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            app = Process.Start(new ProcessStartInfo(executable, "--qa --qa-home") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "owned VS Code WPF window", 30000);
            window = AutomationElement.FromHandle(trainer);
            Focus(); TypingProofClient.Fit(app, trainer);
            Invoke("LabDesktopProfile-ubuntu");
            StartPythonWitness("Найти строку ошибки и проверить исправление", 1);
            string file = EditorWitness()[1], setting, limit;
            int row;
            switch (file)
            {
                case "retry_policy.py": setting = "RETRY_LIMIT"; row = 5; limit = "3"; break;
                case "buffer_policy.py": setting = "BUFFER_LIMIT"; row = 9; limit = "8"; break;
                case "queue_policy.py": setting = "QUEUE_LIMIT"; row = 7; limit = "4"; break;
                default: throw new InvalidOperationException("Unrecognized simulated source: " + file);
            }
            var prefix = new StringBuilder("# Worker configuration\n");
            for (int i = 0; i < row - 2; i++) prefix.Append("# Settings from the unchanged project template\n");
            string rest = "\n# A different setting must not change\nAROUND_LIMIT = 0\n";
            string before = NativeFixtureHash(prefix.ToString() + setting + " = 0" + rest);
            string after = NativeFixtureHash(prefix.ToString() + setting + " = " + limit + rest);
            Check(ExactEditorTab(EditorWitness(), file, before, before), "The source really contains the buggy setting before the revision");
            CaptureReferenceFrame("editor-traceback-01-visible-error");

            Chord("Ctrl+G");
            Until(delegate { return ReadSolved() == 1 && EditorWitness()[0] == "goto-line"; }, "physical Ctrl+G opens the line field");
            EnterCharacters((row + 1).ToString(CultureInfo.InvariantCulture)); Key("Enter");
            Until(delegate { return EditorWitness()[0] == "body"; }, "different line really opens");
            Check(ReadSolved() == 1 && EditorWitness()[3] != prefix.Length.ToString(CultureInfo.InvariantCulture),
                "Landing on the next line is not credited");
            Chord("Ctrl+G");
            Until(delegate { return EditorWitness()[0] == "goto-line"; }, "reopen line entry without extra credit");
            EnterCharacters(row.ToString(CultureInfo.InvariantCulture)); Key("Enter");
            WaitEditorState(2, "body", file, prefix.Length, prefix.Length);
            CaptureReferenceFrame("editor-traceback-02-exact-line");
            Key("End"); Key("Backspace"); EnterCharacters(limit);
            Until(delegate { return ExactEditorTab(EditorWitness(), file, after, before); }, "precise unsaved change, saved file unchanged");
            Chord("Ctrl+S");
            Until(delegate { return ReadSolved() == 3 && ExactEditorTab(EditorWitness(), file, after, after); },
                "the fixed source alone was written");
            Chord("Ctrl+`");
            Until(delegate { return ReadSolved() == 4 && EditorWitness()[0] == "terminal"; }, "switch to the actual integrated terminal");
            const string test = "python -m unittest test_policy";
            EnterCharacters(test);
            Until(delegate { return Find("LabDesktop").Current.HelpText == test; }, "exact command typed into the focused terminal");
            Key("Enter");
            Check(ExactEditorTab(FinishedEditorWitness(5), file, after, after), "only a passing test on the saved source finishes the route");
            CaptureReferenceFrame("editor-traceback-03-real-test-result");
            Console.WriteLine("PASS native WPF " + file + ":" + row + ", exact save, specific real test and five purposeful actions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("editor-traceback-failure"); }
            catch (Exception capture) { Console.Error.WriteLine(capture); }
            return 1;
        }
        finally { if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); } }
    }
}

internal static class EditorTracebackMain
{
    [STAThread]
    private static int Main(string[] args) { return args.Length == 2 ? LiveUiSmoke.CheckNativeTraceback(args[0], args[1]) : 2; }
}
