using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

internal static partial class LiveUiSmoke
{
    private static void CheckHistoryPalette(string name)
    {
        int page = 0, card = 0, ring = 0, toolbar = 0;
        using (var picture = new Bitmap(Path.Combine(evidenceDirectory, name + ".png")))
        {
            for (int y = 0; y < picture.Height; y += 2)
                for (int x = 0; x < picture.Width; x += 2)
                {
                    var pixel = picture.GetPixel(x,y);
                    if (pixel.R == 0x25 && pixel.G == 0x26 && pixel.B == 0x29) page++;
                    if (pixel.R == 0x2d && pixel.G == 0x2e && pixel.B == 0x31) card++;
                    if (Math.Abs(pixel.R-0xa7)<4 && Math.Abs(pixel.G-0xc6)<4 && Math.Abs(pixel.B-0xfa)<4) ring++;
                    if (pixel.R == 0x3d && pixel.G == 0x3d && pixel.B == 0x3d) toolbar++;
                }
        }
        Check(page > 1000 && card > 500 && ring > 25 && toolbar > 1000,
            "Native Chrome history canvas has the dark page, separate result card, focussed search outline and distinct toolbar.");
    }

    private static void CheckHistoryAdvance()
    {
        string task = Find("LabTask").Current.ItemStatus;
        int before = ReadSolved();
        if (task.StartsWith("text:",StringComparison.Ordinal))
        {
            EnterCharacters(task.Substring(5));
            Key("Enter");
        }
        else
        {
            Check(task.StartsWith("keys:",StringComparison.Ordinal),"The visible task identifies a real input: " + task);
            string keys = task.Substring(5);
            int attempts = keys == "Tab" || keys == "Shift+Tab" ? 32 : 1;
            for (int i=0;i<attempts;i++)
            {
                Chord(keys);
                if (ReadSolved() > before) break;
            }
        }
        Until(delegate { return ReadSolved() == before+1; }, "credit that follows actual browser action " + task);
    }

    internal static int CheckChromeHistoryView(string executable,string destination)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("A disposable Chrome history profile is required.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot capture native WPF history at actual DPI.");
            evidenceDirectory = Directory.CreateDirectory(destination).FullName;
            app = Process.Start(new ProcessStartInfo(executable,"--qa --qa-home") {
                WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "owned Chrome QA window",18000);
            window = System.Windows.Automation.AutomationElement.FromHandle(trainer);
            Focus(); TypingProofClient.Fit(app,trainer);
            Invoke("LabDesktopProfile-ubuntu");
            uint owner;
            uint thread = GetWindowThreadProcessId(trainer,out owner);
            Check(owner == (uint)app.Id,"Language switch is scoped to the owned test application.");
            SetNativeLessonLayout(thread,"00000409",0x0409);
            OpenLiveConfiguration("chrome");
            Select("LabMode",0); Select("LabLevel",2); Select("LabDuration",0); Select("LabTimer",0);
            SelectMissionByName("Восстановить рабочий источник из истории");
            Key("F5");
            Until(delegate { return Visible("LabTask") && Name("LabPause").StartsWith("Пауза"); },
                "Chrome through the standard Live catalog and configuration.");
            for (int count=0; count<12 && Find("LabTask").Current.ItemStatus!="keys:Ctrl+H"; count++) CheckHistoryAdvance();
            Check(Find("LabTask").Current.ItemStatus == "keys:Ctrl+H","The learner reached the actual History step.");
            Chord("Ctrl+Shift+B");
            CaptureReferenceFrame("chrome-wpf-01-dark-chrome");
            Chord("Ctrl+H");
            Until(delegate { return BrowserWitness("LabDesktop")[2]=="history" &&
                Find("LabDesktop").Current.HelpText.Contains("Поиск в истории"); }, "History visibly exposes its focusable search field.");
            int before = ReadSolved();
            EnterCharacters("Handbook Backup");
            var filtered = BrowserWitness("LabDesktop");
            Check(filtered[1]=="chrome://history" && filtered[2]=="history" && filtered[3]=="Handbook Backup" &&
                filtered[6].Contains("https://practice.local/handbook/backup#procedure") && filtered[8]=="-1",
                "Actual typed history query filters the local visited ledger and focus stays in the search box.");
            Check(ReadSolved()==before,"Searching never awards the physical shortcut for choosing a result.");
            CaptureReferenceFrame("chrome-wpf-02-history-filtered");
            CheckHistoryPalette("chrome-wpf-02-history-filtered");
            for(int count=0;count<5 && BrowserWitness("LabDesktop")[1]=="chrome://history";count++) CheckHistoryAdvance();
            Check(BrowserWitness("LabDesktop")[1]=="https://practice.local/handbook/backup#procedure",
                "Real Tab and Enter restore the lesson's exact source section.");
            CaptureReferenceFrame("chrome-wpf-03-restored-source");
            Console.WriteLine("PASS native WPF Chrome history, real keyboard and dark visuals at DPI " + GetDpiForWindow(trainer));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { if (app!=null && !app.HasExited && GetForegroundWindow()==trainer) CaptureReferenceFrame("chrome-wpf-failure"); }
            catch(Exception extra) { Console.Error.WriteLine(extra.Message); }
            return 1;
        }
        finally
        {
            if (app!=null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }
}

internal static class ChromeHistoryViewMain
{
    [STAThread] public static int Main(string[] args)
    { return args.Length==2 ? LiveUiSmoke.CheckChromeHistoryView(args[0],args[1]) : 2; }
}
