using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int CheckEditorGitRelease(string executable,string output)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")) ||
                SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("An isolated interactive WPF profile is required.");
            evidenceDirectory=Directory.CreateDirectory(output).FullName;
            app=Process.Start(new ProcessStartInfo(executable,"--qa --qa-home") {
                WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false
            });
            Until(delegate {app.Refresh();trainer=app.MainWindowHandle;return trainer!=IntPtr.Zero;},
                "owned WPF window",30000);
            window=AutomationElement.FromHandle(trainer);
            Focus();TypingProofClient.Fit(app,trainer);
            Invoke("LabDesktopProfile-windows");
            StartPythonWitness("Собрать инструкцию дежурному из двух документов и передать через Git",1);
            bool sawStaged=false,sawUnstaged=false,sawCommitted=false;
            int n=0;
            while (!Visible("LabResult") && n<70)
            {
                int old=ReadSolved(); n=old;
                var objective=Find("LabTask").Current;
                string instruction=objective.ItemStatus, git=Find("LabDesktop").Current.ItemStatus;
                if (git.StartsWith("Git ·") && git.Contains("индекс: notes.md;")) {
                    sawUnstaged=true;
                    if (git.EndsWith("scm-menu")) CaptureReferenceFrame("editor-git-before-unstage");
                }
                if (instruction.StartsWith("keys:"))
                {
                    string keys=instruction.Substring(5);
                    if (keys=="Ctrl+Enter") {
                        Check(git.Contains("индекс: handoff.md;") && git.Contains("рабочие изменения: notes.md;"),
                            "the staged result and unrelated working notes are visibly separate");
                        sawStaged=true;
                        EnterCharacters("Deliver shift handoff");
                        CaptureReferenceFrame("editor-git-staged");
                    }
                    for (int t=0;t<(keys=="Down"?24:1);t++) {
                        Chord(keys);
                        if (Visible("LabResult") || ReadSolved()>old) break;
                    }
                }
                else if (instruction.StartsWith("text:")) { EnterCharacters(instruction.Substring(5));Key("Enter"); }
                else throw new InvalidOperationException("Missing actionable UI instruction: "+instruction);
                Until(delegate { return Visible("LabResult") || ReadSolved()==old+1; }, "one keyboard transition: "+instruction+" from "+old);
                if (!Visible("LabResult")) {
                    git=Find("LabDesktop").Current.ItemStatus;
                    if (git.Contains("последний локальный коммит: Deliver shift handoff;") && git.Contains("индекс: ;") && git.Contains("рабочие изменения: notes.md;"))
                    {
                        if (!sawCommitted) CaptureReferenceFrame("editor-git-committed");
                        sawCommitted=true;
                    }
                }
                n=old+1;
            }
            Check(n>=30 && n<70 && Visible("LabResult") && sawUnstaged && sawStaged && sawCommitted,
                "one whole published handoff, shell status, clean index, private scratch retained");
            Check(Find("LabResult").Current.ItemType.StartsWith(n.ToString()+":"),"one completed route receipt");
            CaptureReferenceFrame("editor-git-result");
            Console.WriteLine("PASS WPF native W10 exercise profile: editor file selection, terminal Git verification, "+n+" actual keyboard steps.");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);
            try {if (app!=null && !app.HasExited && GetForegroundWindow()==trainer) CaptureReferenceFrame("editor-git-failure");} catch(Exception capture){Console.Error.WriteLine(capture);}
            return 1;
        }
        finally{ if (app!=null && !app.HasExited) { app.Kill();app.WaitForExit(4000); } }
    }
}
internal static class EditorGitReleaseMain
{
    [STAThread] private static int Main(string[] args) { return args.Length==2 ? LiveUiSmoke.CheckEditorGitRelease(args[0],args[1]) : 2; }
}
