using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal static partial class LiveUiSmoke
{
    private static string NotebookSummary()
    {
        return Find("LabDesktop").Current.HelpText;
    }

    private static void WorkbookFrame(string name)
    {
        Wait(200);
        CaptureReferenceFrame(name);
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_VIDEO") == "1") Wait(2350);
    }

    // Spy-free physical input. Positions are read from the visible physical
    // scene rectangle plus the displayed editor rails and the known fixture's
    // heading / two one-line code cells. We verify the real effect, not just a click.
    private static void ClickWorkbench(double x, double y)
    {
        if (GetForegroundWindow() != trainer || app.HasExited) throw new InvalidOperationException("Notebook lost foreground.");
        var view = Find("LabDesktop").Current.BoundingRectangle;
        double scale = GetDpiForWindow(trainer) / 96.0;
        int cursorX = (int)Math.Round(view.X + x * scale), cursorY = (int)Math.Round(view.Y + y * scale);
        if (!view.Contains(cursorX,cursorY)) throw new InvalidOperationException("The notebook target is not in the real desktop view.");
        if (!SetNotebookCursor(cursorX,cursorY)) throw new InvalidOperationException("Cannot position the mouse for the proof.");
        var inputs = new[] { new Input { Type=0, Data=new InputData { Mouse=new MouseInput { Flags=2 } } },
                             new Input { Type=0, Data=new InputData { Mouse=new MouseInput { Flags=4 } } } };
        if (GetForegroundWindow() != trainer || SendInput(2,inputs,Marshal.SizeOf(typeof(Input))) != 2)
            throw new InvalidOperationException("Notebook click did not reach the owned foreground.");
        Wait(180);
    }

    internal static int CheckNotebookWorkbenchWindow(string executable, string destination)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("A fresh isolated notebook QA profile is required.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot capture the real notebook viewport at its native DPI.");
            evidenceDirectory = Directory.CreateDirectory(destination).FullName;
            app = Process.Start(new ProcessStartInfo(executable, "--qa --qa-home") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "owned workbook QA window", 18000);
            window = System.Windows.Automation.AutomationElement.FromHandle(trainer);
            Focus(); TypingProofClient.Fit(app, trainer);
            Invoke("LabDesktopProfile-ubuntu");
            OpenLiveConfiguration("code");
            Select("LabMode",1); Select("LabLevel",1); Select("LabDuration",0); Select("LabTimer",0);
            SelectMissionByName("Исправить формулу в блокноте по заявке");
            Key("F5");
            Until(delegate { return Visible("LabTask") && Name("LabPause").StartsWith("Пауза"); }, "workbench fixture through Live catalog");
            VirtualEnglish();
            for (int i=0;i<4 && !NotebookSummary().Contains("Блокнот · ячейка");i++)
            {
                string prompt = Find("LabTask").Current.ItemStatus;
                if (prompt.StartsWith("text:",StringComparison.Ordinal)) EnterCharacters(prompt.Substring(5));
                else if (prompt.StartsWith("keys:",StringComparison.Ordinal))
                {
                    string prefix = Exists("LabKeys") ? Find("LabKeys").Current.ItemStatus : "";
                    if (prefix.Length > 0) EnterCharacters(prefix);
                    Chord(prompt.Substring(5));
                }
                else throw new InvalidOperationException("No actual teaching instruction: " + prompt);
            }
            Until(delegate { return NotebookSummary().Contains("Блокнот · ячейка 1 из 3"); }, "real Markdown, data and formula notebook");
            WorkbookFrame("workbench-wpf-01-open");
            int previous = ReadSolved();
            var desktop = Find("LabDesktop").Current.BoundingRectangle;
            double dipWidth = desktop.Width * 96.0 / GetDpiForWindow(trainer);
            double editorX = 48 + Math.Min(226, Math.Max(116, dipWidth * .20));
            // 85 is the workbench breadcrumb edge, 32 is the notebook toolbar.
            // The first Markdown H1 has 43+17 px source and 14 px inter-cell
            // gap; the next one-line code cell occupies 53+14 px.
            double formulaY = 85 + 32 + 8 + (43+17+14) + (53+14);
            ClickWorkbench(editorX + 34, formulaY + 13);
            Until(delegate { return NotebookSummary().Contains("ячейка 3 из 3") && NotebookSummary().Contains("NameError"); },
                "native left-edge Run opens traceback from the before-state");
            Check(ReadSolved() == previous, "A pointer equivalent did not award the required physical keyboard action.");
            WorkbookFrame("workbench-wpf-02-run-cell");
            Key("Enter"); Chord("Ctrl+A");
            for (int i=0;i<8;i++) Send(Enumerable.Repeat('x',16).SelectMany(Character).ToArray());
            for (int i=0;i<6;i++) Send(string.Concat(Enumerable.Repeat("\n1",5)).SelectMany(Character).ToArray());
            Until(delegate { return NotebookSummary().Contains("редактирование") && NotebookSummary().Contains(new string('x',100)); },
                "native keyboard sends long code into an edited cell");
            WorkbookFrame("workbench-wpf-03-edit-long");
            Key("Esc"); Chord("Ctrl+Enter"); Key("PageDown"); Chord("Shift+PageDown");
            for (int i=0;i<7;i++) Key("PageDown");
            WorkbookFrame("workbench-wpf-04-scrolled-traceback");
            string beforeMenu = NotebookSummary();
            Chord("Shift+F10"); Key("Down"); Key("Down"); Key("Esc");
            Check(NotebookSummary() == beforeMenu, "Closing keyboard context menu does not edit text or move the selected cell.");
            // Use the actual document-wide kernel command exposed by the workbench.
            Chord("Ctrl+Shift+P"); EnterCharacters("Notebook: Restart Kernel"); Key("Enter");
            Until(delegate { return NotebookSummary().Contains("Сохранённый вывод"); }, "kernel reset keeps existing output distinct from live execution");
            WorkbookFrame("workbench-wpf-05-restart");
            Chord("Ctrl+Shift+E");
            Until(delegate { return NotebookSummary().Contains("Проводник"); }, "Explorer focus rather than symbol focus");
            int explorerBefore = ReadSolved();
            ClickWorkbench(editorX - 45, 69);
            Until(delegate { return NotebookSummary().Contains("Новая папка"); }, "real Explorer create-folder control");
            EnterCharacters("reviewed"); Key("Enter");
            Until(delegate { return NotebookSummary().Contains("Выбрано: reviewed"); }, "newly created folder selected by actual path");
            ClickWorkbench(editorX - 45, 69);
            EnterCharacters("reviewed"); Key("Enter");
            Until(delegate { return NotebookSummary().Contains("занято") && NotebookSummary().Contains("Введено: reviewed"); },
                "duplicate name leaves inline input and never overwrites");
            WorkbookFrame("workbench-wpf-06-explorer-conflict");
            Key("Esc"); Chord("Shift+F10");
            for (int i=0;i<6;i++) Key("Down");
            Key("Enter");
            Until(delegate { return NotebookSummary().Contains("Последнее действие с файлом отменено"); },
                "Explorer context undo restores the original project");
            Check(ReadSolved() == explorerBefore, "Pointer equivalents and viewport handling never granted a keyboard-only task.");
            WorkbookFrame("workbench-wpf-07-explorer-undo");
            Console.WriteLine("PASS Windows WPF notebook workbench: official launch route, left Run, real keyboard caret and scroll, palette reset at DPI " + GetDpiForWindow(trainer));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { if (app != null && !app.HasExited) { DumpProcessWindows(); if (GetForegroundWindow() == trainer) WorkbookFrame("workbench-wpf-failure"); } }
            catch (Exception extra) { Console.Error.WriteLine(extra.Message); }
            return 1;
        }
        finally
        {
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }

    [DllImport("user32.dll", EntryPoint="SetCursorPos")]
    private static extern bool SetNotebookCursor(int x,int y);
}

internal static class NotebookWorkbenchMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.CheckNotebookWorkbenchWindow(Path.GetFullPath(args[0]),Path.GetFullPath(args[1])) : 2;
    }
}
