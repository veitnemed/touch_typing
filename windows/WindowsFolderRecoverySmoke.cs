// Exercise the stuck Windows desktop lesson in the real packaged WPF window.
// The learner's detour is reproduced with native keyboard input, then visible
// caption/window clicks recover focus without earning the required key.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    private static string FolderRecoveryView()
    {
        return Find("LabDesktop").Current.HelpText;
    }

    private static string FolderRecoveryPrompt()
    {
        return Find("LabTask").Current.ItemStatus;
    }

    private static void FolderRecoveryFrame(string name)
    {
        Wait(180);
        CaptureReferenceFrame("win-folder-" + name);
    }

    private static void FolderRecoverySelect(string item, string key)
    {
        string selected = "Выбран объект «" + item + "».";
        for (int i = 0; i < 16 && !FolderRecoveryView().Contains(selected); i++)
            Key(key);
        Check(FolderRecoveryView().Contains(selected), "The actual Explorer selected " + item + ": " + FolderRecoveryView());
    }

    // Same scene projection as DrawSimulatedWindows10; UIA provides the
    // current physical viewport (not a prerecorded coordinate or image).
    private static void FolderRecoveryClick(double logicalX, double logicalY)
    {
        Check(GetForegroundWindow() == trainer && !app.HasExited, "Only the owned WPF desktop receives mouse input.");
        var viewport = Find("LabDesktop").Current.BoundingRectangle;
        double scale = Math.Min(viewport.Width / 1200, viewport.Height / 520);
        int x = (int)Math.Round(viewport.X + (viewport.Width - 1200 * scale) / 2 + logicalX * scale);
        int y = (int)Math.Round(viewport.Y + (viewport.Height - 520 * scale) / 2 + logicalY * scale);
        Check(viewport.Contains(x, y) && SetFolderRecoveryCursor(x, y), "The click is on the current scene.");
        var mouse = new[] { new Input { Type = 0, Data = new InputData { Mouse = new MouseInput { Flags = 2 } } },
                            new Input { Type = 0, Data = new InputData { Mouse = new MouseInput { Flags = 4 } } } };
        Check(GetForegroundWindow() == trainer && SendInput(2, mouse, Marshal.SizeOf(typeof(Input))) == 2,
            "Native mouse input reached the simulator.");
        Wait(170);
    }

    internal static int RunWindowsFolderRecovery(string executable, string output)
    {
        try
        {
            Check(!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")),
                "Use an isolated directory for all test profiles.");
            Check(SetThreadDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero, "Allow capture at the monitor DPI.");
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            app = Process.Start(new ProcessStartInfo(executable, "--qa --live") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
            });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "own real WPF window from the verified archive", 45000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id, "The observed desktop belongs to the new process.");
            Focus(); TypingProofClient.Fit(app, trainer);
            Invoke("LabDesktopProfile-windows");
            OpenLiveConfiguration("windows");
            Select("LabLevel", 0); Select("LabMode", 1); Select("LabDuration", 0); Select("LabTimer", 0);
            SelectMissionByName("Windows 10: вернуться к папке проекта");
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза") &&
                FolderRecoveryPrompt() == "keys:Super+E"; }, "normal Configure starts the reported mission");
            VirtualEnglish();

            Chord("Super+E");
            Until(delegate { return FolderRecoveryView().Contains(@"Папка «C:\Users\Student».") &&
                FolderRecoveryPrompt() == "keys:Down" && ReadSolved() == 1; }, "Explorer starts at the user folder");
            FolderRecoverySelect("project", "Up");
            Check(ReadSolved() == 1, "An alternate Up path is not the requested Down gesture.");
            Key("Enter");
            Until(delegate { return FolderRecoveryView().Contains(@"Папка «C:\Users\Student\project»."); }, "enter the off-route project");
            FolderRecoverySelect("logs", "Down"); Key("Enter");
            Until(delegate { return FolderRecoveryView().Contains(@"Папка «C:\Users\Student\project\logs»."); }, "reach the reported logs");
            FolderRecoverySelect("app.log", "Down"); Key("Enter");
            Until(delegate { return FolderRecoveryView() == "app.log" &&
                Name("LabTask").Contains("«project»") && FolderRecoveryPrompt() == "keys:Down" &&
                Name("LabStatus").Contains("Проводник не в фокусе") && ReadSolved() == 1; },
                "reported detour and a visible recovery instruction");
            FolderRecoveryFrame("01-detour");

            Key("Down");
            Check(FolderRecoveryView() == "app.log" && ReadSolved() == 1, "The same arrow in the editor cannot falsely pass the task.");

            // The second ordinary window occupies (272,83,825,374).
            // The first Explorer remains exposed to its left.
            FolderRecoveryClick(1043,98);
            Until(delegate { return Name("LabDesktop").Contains("Visual Studio Code: развёрнуто"); }, "square caption control really maximizes Code");
            FolderRecoveryFrame("02-maximized");
            FolderRecoveryClick(1146,15);
            Until(delegate { return Name("LabDesktop").Contains("Visual Studio Code: открыто"); }, "restore caption control returns to the saved extent");
            FolderRecoveryClick(120,56);
            Until(delegate { return FolderRecoveryView().Contains(@"Папка «C:\Users\Student\project\logs».") &&
                Name("LabStatus").Contains("Alt+↑"); }, "exposed Explorer accepts focus and the parent-folder hint");
            Check(ReadSolved() == 1, "Mouse recovery leaves the exercise uncredited.");

            Chord("Alt+Up"); Chord("Alt+Up");
            Until(delegate { return FolderRecoveryView().Contains(@"Папка «C:\Users\Student»."); }, "return to the correct parent");
            FolderRecoverySelect("project", "Down");
            Until(delegate { return FolderRecoveryPrompt() == "keys:Enter" && ReadSolved() == 2; }, "Down now passes despite the still-open Code window");
            Check(Name("LabDesktop").Contains("Visual Studio Code: открыто"), "Recovery did not silently close the unrelated editor.");
            FolderRecoveryFrame("03-project-credited");
            Key("Enter"); Chord("Alt+Up"); Chord("Super+D"); Chord("Super+D");
            Until(delegate { return Visible("LabResult"); }, "original lesson finished without a restart");
            FolderRecoveryFrame("04-completed");
            Console.WriteLine("PASS WPF desktop recovery: native detour, wrong-focus no credit, maximize/restore, exposed Explorer click, exact folder and full completion.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) FolderRecoveryFrame("failure"); }
            catch (Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally
        {
            if (app != null && !app.HasExited)
            { app.Kill(); app.WaitForExit(4000); }
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    private static extern bool SetFolderRecoveryCursor(int x, int y);
}

internal static class WindowsFolderRecoveryMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.RunWindowsFolderRecovery(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])) : 2;
    }
}
