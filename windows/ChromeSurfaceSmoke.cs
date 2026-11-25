using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

// Native pointer only, positioned relative to the rendered 96-DPI Chrome
// viewport. The UI state, not the pointer API returning, determines success.
internal static partial class LiveUiSmoke
{
    [DllImport("user32.dll", EntryPoint="SetCursorPos")]
    private static extern bool SetChromePointer(int x,int y);

    private static void ChromePointerClick(double x, double y)
    {
        if (app.HasExited || GetForegroundWindow()!=trainer || GetDpiForWindow(trainer)!=96)
            throw new InvalidOperationException("Refusing untargeted/non-96-DPI input.");
        var rect=Find("LabDesktop").Current.BoundingRectangle;
        int screenX=(int)Math.Round(rect.Left+x),screenY=(int)Math.Round(rect.Top+y);
        if(!rect.Contains(screenX,screenY) || !SetChromePointer(screenX,screenY))
            throw new InvalidOperationException("No visible target in the owned Chrome scene.");
        var events=new[]{new Input {Type=0,Data=new InputData {Mouse=new MouseInput {Flags=2}}},
                         new Input {Type=0,Data=new InputData {Mouse=new MouseInput {Flags=4}}}};
        if(GetForegroundWindow()!=trainer || SendInput(2,events,Marshal.SizeOf(typeof(Input)))!=2)
            throw new InvalidOperationException("The owned foreground trainer did not receive the click.");
        Wait(160);
    }

    internal static int CheckChromeSurface(string exe,string dst)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("A fresh synthetic profile is required.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4))==IntPtr.Zero) throw new InvalidOperationException("DPI context");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_BROWSER_CLONE","1");
            evidenceDirectory=Directory.CreateDirectory(dst).FullName;
            app=Process.Start(new ProcessStartInfo(exe,"--qa --live") {WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=false});
            Until(delegate {app.Refresh();trainer=app.MainWindowHandle;return trainer!=IntPtr.Zero;}, "owned WPF window",30000);
            window=AutomationElement.FromHandle(trainer);
            Focus();TypingProofClient.Fit(app,trainer);
            uint owner;uint thread=GetWindowThreadProcessId(trainer,out owner);
            Check(owner==(uint)app.Id,"Only the owned trainer will receive native HKL input.");
            SetNativeLessonLayout(thread,"00000409",0x0409);
            Invoke("LabDesktopProfile-ubuntu");
            Find("HomeClass-chrome").SetFocus();Key("Enter");
            Until(delegate {return Visible("LabDesktop") && Name("LabPause").StartsWith("Пауза");},"ordinary Chrome card");
            Check(Find("LabTask").Current.ItemStatus=="keys:Ctrl+L" && ReadSolved()==0,"Ordinary card begins on its physical address shortcut.");
            string original=BrowserWitness("LabDesktop")[1];
            // The fresh WPF layout is 1024x640 at 96 DPI. These points lie in
            // the text of the first two tab labels rather than their close icons.
            ChromePointerClick(355,60);
            Until(delegate{return BrowserWitness("LabDesktop")[2]=="url" && BrowserWitness("LabDesktop")[3]==original;},
                "pointer focused the real address field");Check(ReadSolved()==0,"Address click grants no shortcut credit.");
            CaptureReferenceFrame("chrome-surface-01-address");
            double originalTab=105;
            ChromePointerClick(105,23);
            if(BrowserWitness("LabDesktop")[1]==original) ChromePointerClick(330,23);
            else originalTab=330;
            string other=BrowserWitness("LabDesktop")[1];
            Check(other!=original && ReadSolved()==0,"Real tab selection changes the open document without credit.");
            CaptureReferenceFrame("chrome-surface-02-other-tab");
            ChromePointerClick(originalTab,23);
            Until(delegate {return BrowserWitness("LabDesktop")[1]==original;},"source tab restored");
            ChromePointerClick(355,60);
            string prefix="";
            foreach(char c in "report")
            {
                prefix+=c; Send(Character(c)); string expected=prefix;
                Until(delegate{return BrowserWitness("LabDesktop")[3]==expected;},"physical query prefix " + prefix);
            }
            Key("Enter");
            Until(delegate {return BrowserWitness("LabDesktop")[1].IndexOf("report",StringComparison.OrdinalIgnoreCase)>=0;},"new visited report page");
            int before=ReadSolved();
            CaptureReferenceFrame("chrome-surface-03-enabled-back");
            ChromePointerClick(23,60);
            Until(delegate {return BrowserWitness("LabDesktop")[1]==original && BrowserWitness("LabDesktop")[2]=="body";},
                "visible Back arrow restored the source in this tab");
            ChromePointerClick(23,60);
            Check(BrowserWitness("LabDesktop")[1]==original && ReadSolved()==before,"Disabled Back is a no-op, without credit.");
            CaptureReferenceFrame("chrome-surface-04-restored");
            Chord("Ctrl+L");
            Until(delegate{return ReadSolved()==1 && BrowserWitness("LabDesktop")[2]=="url";},"a physical Ctrl+L earns exactly one step");
            Console.WriteLine("PASS native WPF Chrome: three physical pointer targets, exact outcome, no mouse shortcut credit.");return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            try{if(app!=null&&!app.HasExited&&GetForegroundWindow()==trainer)CaptureReferenceFrame("chrome-surface-failure");}
            catch(Exception detail){Console.Error.WriteLine(detail);}return 1;
        }
        finally{if(app!=null&&!app.HasExited){app.Kill();app.WaitForExit(5000);}}
    }
}
internal static class ChromeSurfaceMain
{
    [STAThread] private static int Main(string[] args)
    {return args.Length==2 ? LiveUiSmoke.CheckChromeSurface(Path.GetFullPath(args[0]),Path.GetFullPath(args[1])):2;}
}
