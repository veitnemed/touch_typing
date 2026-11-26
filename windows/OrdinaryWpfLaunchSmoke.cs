// A command-line-free launch of the verified Windows archive; CI's own blank
// local application-data folder is used, and a pre-existing profile is rejected.
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Automation;
using System.Xml;

internal static partial class LiveUiSmoke
{
    private static void AttachOrdinaryWpf(string executable)
    {
        app = Process.Start(new ProcessStartInfo(executable) {
            WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
        });
        Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; },
              "normal launch from the extracted Windows archive", 45000);
        window = AutomationElement.FromHandle(trainer);
        Check(window.Current.ProcessId == app.Id, "only the process we launched is exercised");
        Focus();
        TypingProofClient.Fit(app, trainer);
        Chord("Alt+1");
        Until(delegate { return Visible("HomeDashboard") && Visible("HomeClass-chrome"); },
              "visible ordinary Live catalog");
        Check(Visible("ReferenceNav-practice") && Visible("ReferenceNav-learn") &&
              !Visible("ReferenceNav-home") && !Visible("ReferenceNav-progress"), "the two real top level sections");
    }

    private static void StopOwnedPracticeEngine()
    {
        int enginePid = 0;
        Until(delegate
        {
            using (var lookup = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE " +
                "Name = 'TouchTyping.Engine.exe' AND ParentProcessId = " + app.Id))
            using (var matches = lookup.Get())
                foreach (ManagementObject item in matches)
                    using (item) { enginePid = Convert.ToInt32(item["ProcessId"]); break; }
            return enginePid != 0;
        }, "owned engine instance");
        using (var child = Process.GetProcessById(enginePid))
        {
            child.Kill();
            Check(child.WaitForExit(5000), "instrumented engine exit");
        }
    }

    private static double OrdinaryCourseValue()
    {
        var bar = Find("LabScenarioProgress");
        return ((RangeValuePattern)bar.GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value;
    }

    private static string OrdinaryVisibleKeys()
    {
        if (!Visible("LabKeys")) return "";
        // Read the accessible label of the actual rendered keycap cluster, not
        // ItemStatus, which is populated only for instrumented sessions.
        string caption = Name("LabKeys");
        int end = caption.IndexOf("   ·   шаг ", StringComparison.Ordinal);
        return (end < 0 ? caption : caption.Substring(0, end)).Replace(" ", "");
    }

    internal static int RunOrdinaryWpfLaunch(string executable, string output)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
                throw new InvalidOperationException("This check is for an isolated GitHub Actions Windows user only.");
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "TouchTypingStudio", "progress.xml");
            // The same isolated runner has already launched the ordinary
            // typing test. It may have authentic typing history, but must
            // never have a prepared or old Live checkpoint for this test.
            if (File.Exists(profile))
            {
                var initial = new XmlDocument { XmlResolver = null };
                initial.Load(profile);
                Check(initial.SelectNodes("/StudioProfile/LiveDrafts/LiveDraft").Count == 0 &&
                      initial.SelectNodes("/StudioProfile/LabSessions/LabSessionRecord").Count == 0,
                      "ordinary mode starts without pre-existing Live progress");
            }
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Windows desktop cannot be captured.");
            AttachOrdinaryWpf(executable);
            CaptureReferenceFrame("ordinary-wpf-01-catalog");
            Find("HomeClass-chrome").SetFocus();
            Key("Enter");
            Until(delegate { return Visible("LabDesktop") && OrdinaryCourseValue() == 0; }, "ordinary Chrome course");
            Check(OrdinaryVisibleKeys() == "Ctrl+L", "expected fresh real course standard action, observed: " + OrdinaryVisibleKeys());
            Chord("Ctrl+L");
            Until(delegate { return OrdinaryCourseValue() == 1 && OrdinaryVisibleKeys() == "Ctrl+C"; },
                  "location submitted to the virtual address field");
            CaptureReferenceFrame("ordinary-wpf-02-address");
            Invoke("LabBack");
            Until(delegate { return Visible("HomeClass-chrome") && Visible("LiveResume-chrome"); }, "draft remains on the original card");
            RequestClose(); UntilExit(); app.Dispose(); app = null;
            Check(File.Exists(profile), "the real production profile was written on the isolated runner");

            AttachOrdinaryWpf(executable);
            Check(Visible("LiveResume-chrome"), "same native start offers saved state");
            Find("HomeClass-chrome").SetFocus(); Key("Enter");
            Until(delegate { return Visible("LabDesktop") && OrdinaryCourseValue() == 1; }, "ordinary card resumed the same goal after restart");
            Check(OrdinaryVisibleKeys() == "Ctrl+C", "restart retained the address selection objective: " + OrdinaryVisibleKeys());
            Chord("Ctrl+C");
            Until(delegate { return OrdinaryCourseValue() == 2 && OrdinaryVisibleKeys() == "Ctrl+T"; },
                  "the saved address selection really provides a value");
            CaptureReferenceFrame("ordinary-wpf-03-restored");
            Invoke("LabBack");
            Until(delegate { return Visible("HomeClass-chrome"); }, "return to ordinary route");
            // Intentionally crash only a child of this disposable test's own app.
            // The next user action must re-open the durable draft, not re-run an old write.
            StopOwnedPracticeEngine();
            Find("HomeClass-chrome").SetFocus(); Key("Enter");
            Until(delegate { return Visible("LabDesktop") && OrdinaryCourseValue() == 2; },
                  "desktop menu recovers and resumes the exact saved position");
            Check(OrdinaryVisibleKeys() == "Ctrl+T", "expected next action after engine relaunch");
            Invoke("LabBack");
            Until(delegate { return Visible("HomeClass-chrome"); }, "second return to ordinary route");
            Chord("Alt+2");
            Until(delegate { return Visible("TypingStart"); }, "ordinary typing menu");
            StopOwnedPracticeEngine();
            Invoke("TypingStart");
            Until(delegate { return Visible("TypingCount"); }, "typing start recovers the engine too");
            Key("Esc");
            Until(delegate { return Visible("TypingChoose"); }, "typing result after recovered start");
            Invoke("TypingChoose");
            RequestClose(); UntilExit();
            Console.WriteLine("PASS Windows exact EXE, no launch flags, clean profile -> course -> checkpoint -> reopen -> next task.");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { if (app != null && !app.HasExited && GetForegroundWindow() == trainer) CaptureReferenceFrame("ordinary-wpf-failure"); }
            catch(Exception detail) { Console.Error.WriteLine(detail); }
            return 1;
        }
        finally { if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); } }
    }
}

internal static class OrdinaryWpfLaunchMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.RunOrdinaryWpfLaunch(Path.GetFullPath(args[0]), Path.GetFullPath(args[1])) : 2;
    }
}
