// Exercise actual ordinary Live card activation using an isolated Windows-hosted profile.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

internal static partial class LiveUiSmoke
{
    internal static int RunLiveCourseCards(string executable, string output)
    {
        try
        {
            evidenceDirectory = Directory.CreateDirectory(output).FullName;
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot capture at native Windows DPI");
            app = Process.Start(new ProcessStartInfo(executable,"--qa --live") {
                WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false
            });
            Until(delegate { app.Refresh(); trainer=app.MainWindowHandle; return trainer != IntPtr.Zero; },
                "owned WPF Live dashboard",45000);
            window = AutomationElement.FromHandle(trainer);
            Check(window.Current.ProcessId == app.Id,"Only the owned course window receives input");
            Focus(); TypingProofClient.Fit(app,trainer);
            uint pid; uint thread=GetWindowThreadProcessId(trainer,out pid);
            SetNativeLessonLayout(thread,"00000409",0x0409);
            string[][] groups = {
                new[] {"ubuntu","chrome","code","terminal","ubuntu","workday"},
                new[] {"windows","windows","terminal","workday"}
            };
            foreach (var group in groups)
            {
                string environment=group[0];
                Invoke("LabDesktopProfile-"+environment);
                Until(delegate {return Visible("HomeClass-"+environment);},"desktop profile "+environment);
                for(int card=1;card<group.Length;card++)
                {
                    string mode=group[card];
                    Find("HomeClass-"+mode).SetFocus();
                    Key("Enter");
                    Until(delegate {return Visible("LabTask")&&Visible("LabDesktop")&&Name("LabPause").StartsWith("Пауза");},
                        "ordinary "+environment+"/"+mode+" card");
                    Check(ReadSolved()==0,"Ordinary card starts clean for "+environment+"/"+mode);
                    string initial=CourseScenario();
                    Check(initial.Length>0,"The lesson reports the actual current objective");
                    if (mode=="terminal")
                    {
                        Until(delegate {return Visible("LabTheoryTitle") && Visible("LabTerminalTheory");},"discoverable command theory");
                        var console=Find("LabDesktop").Current.BoundingRectangle;
                        var copy=Find("LabTheoryTitle").Current.BoundingRectangle;
                        Check(console.Right<=copy.Left && console.Width>350,"Sidebar to the right does not overlap input");
                        Check(Name("LabTheoryWhy").Contains(environment=="windows"?"Select-String":"grep"),"First shell command explained");
                        Key("F2");
                        Until(delegate {return !Visible("LabTheoryTitle");},"hide explanation");
                        Check(ReadSolved()==0,"Closing theory does not submit the exercise");
                        Key("F2");
                        Until(delegate {return Visible("LabTheoryTitle");},"reopen explanation");
                    }
                    else if(mode=="chrome") Check(!Visible("LabTerminalTheory") && !Visible("LabTheoryTitle"),"No command sidebar in browser");
                    CaptureReferenceFrame("course-"+environment+"-"+mode+"-start");
                    bool shortTerminal = mode=="terminal";
                    for(int cap=0;cap<100 && !Visible("LabResult") && (ReadSolved()<20||CourseScenario()==initial);cap++)
                    {
                        int before=ReadSolved();
                        string prompt=Find("LabTask").Current.ItemStatus;
                        if (shortTerminal && before==1)
                            Check(Name("LabTheoryWhy").Contains(environment=="windows"?"-Wait":"-f"),"Theory changes with current command");
                        if (shortTerminal && before==2)
                            Check(!Visible("LabTerminalTheory") && !Visible("LabTheoryTitle"),"No command sidebar on interrupt shortcut");
                        if (shortTerminal && before==3)
                            Check(Name("LabTheoryTitle").Contains("одной попытки"),"Returned prompt has lookup theory");
                        if (prompt.StartsWith("text:",StringComparison.Ordinal))
                        {
                            EnterCharacters(prompt.Substring(5));
                            if (!Visible("LabResult") && ReadSolved()==before) Key("Enter");
                        }
                        else
                        {
                            Check(prompt.StartsWith("keys:",StringComparison.Ordinal),"A credited step expects a real typed action: "+prompt);
                            string keys=prompt.Substring(5);
                            if (shortTerminal && keys=="Ctrl+C")
                                Until(delegate {return Find("LabDesktop").Current.ItemStatus.Contains("новая ошибка");},
                                      "actual virtual log event before interrupting its viewer");
                            string prefix=Exists("LabKeys") ? Find("LabKeys").Current.ItemStatus : "";
                            if(!String.IsNullOrEmpty(prefix)) EnterCharacters(prefix);
                            for(int attempt=0;attempt<(keys=="Tab"||keys=="Down"||keys=="Up"||keys=="Shift+Tab"?25:1);attempt++)
                            {
                                Chord(keys);
                                if(Visible("LabResult")||ReadSolved()>before) break;
                            }
                        }
                        Until(delegate {return Visible("LabResult") || ReadSolved()==before+1;},
                              environment+"/"+mode+" "+before+" "+prompt);
                        if (!shortTerminal) Check(!Visible("LabResult"),"Ordinary practice didn't terminate at the end of its first activity");
                    }
                    if (shortTerminal)
                    {
                        Check(Visible("LabResult") && Find("LabResult").Current.HelpText.Contains("Выполнено 4."),
                              "The bounded journal incident completes four actions without unrelated additions");
                        CaptureReferenceFrame("course-"+environment+"-"+mode+"-completed");
                        Console.WriteLine("COURSE " + environment + "/" + mode + ": " + initial + " | single practical journal completed.");
                        Invoke("LabChooseClass");
                        Until(delegate {return Visible("HomeClass-"+mode);},"terminal card restores after finite result");
                        continue;
                    }
                    int earned=ReadSolved();
                    string next=CourseScenario();
                    Check(earned>=20 && next!=initial,"At least twenty successful actions and a natural mission transition");
                    Console.WriteLine("COURSE " + environment + "/" + mode + ": " + initial + " | " + next + " | " + earned);
                    CaptureReferenceFrame("course-"+environment+"-"+mode+"-continuation");
                    Invoke("LabBack");
                    Until(delegate {return Visible("HomeClass-"+mode);},"card restored after save");
                    Check(Visible("LiveResume-"+mode),"Returning from ordinary practice offers its draft");
                    Find("HomeClass-"+mode).SetFocus();
                    Key("Enter");
                    Until(delegate {return Visible("LabTask") && Name("LabPause").StartsWith("Пауза");}, "card reopens its exact draft");
                    Check(ReadSolved()==earned && CourseScenario()==next,"Ordinary activation restores the same course and count");
                    Invoke("LabBack");
                    Until(delegate {return Visible("HomeClass-"+mode);},"saved again to the same card");
                }
            }
            foreach (string desktop in new[] {"ubuntu", "windows"})
                CheckDiskCapacityMission(desktop);
            Console.WriteLine("PASS real WPF course cards, both shell profiles and ordinary mission boundaries.");
            return 0;
        }
        catch(Exception e)
        {
            Console.Error.WriteLine(e);
            try { if(app!=null && !app.HasExited && GetForegroundWindow()==trainer) CaptureReferenceFrame("course-failure"); }
            catch(Exception detail){Console.Error.WriteLine(detail);}
            return 1;
        }
        finally { if(app!=null && !app.HasExited){app.Kill();app.WaitForExit(4000);} }
    }

    private static void CheckDiskCapacityMission(string desktop)
    {
        bool ps = desktop == "windows";
        Invoke("LabDesktopProfile-" + desktop);
        Until(delegate {return Visible("HomeClass-" + desktop);}, "disk exercise profile "+desktop);
        OpenLiveConfiguration("terminal");
        Select("LabMode", 1);
        Select("LabLevel", 0);
        Select("LabDuration", 0);
        Select("LabTimer", 0);
        SelectMissionByName("Не сохранился архив: проверить место");
        Key("F5");
        Until(delegate {return Visible("LabTask") && Visible("LabTheoryObserve") && Name("LabPause").StartsWith("Пауза");},
            "visible disk capacity explanation");
        Check(Name("LabTheoryObserve").Contains(ps ? "Free (GB)" : "Avail"), "The real table column is taught for this shell");
        string expected = Find("LabTask").Current.ItemStatus;
        Check(expected.StartsWith(ps ? "text:Get-PSDrive " : "text:df -h /"), "Visible working task uses the destination disk");
        EnterCharacters(ps ? "Get-PSDrive C" : "df -h .");
        Key("Enter");
        Check(ReadSolved()==0, "System disk is not disk of storage incident");
        EnterCharacters(expected.Substring(5));
        Key("Enter");
        Until(delegate {return ReadSolved()==1 && Name("LabTask").Contains("Хватит места");}, "actual decision step after volume query");
        Check(!Visible("LabTerminalTheory"), "Decision is not a shell command");
        Key("2");
        Check(ReadSolved()==1 && !Visible("LabResult"), "A wrong answer does not score");
        Key("1");
        Until(delegate {return Visible("LabDiskCapacityConclusion");}, "visible verified conclusion");
        Check(Name("LabDiskCapacityConclusion").Contains("Места не хватает"), "Seed zero disk case needs more space");
        CaptureReferenceFrame("disk-space-" + desktop);
        Invoke("LabChooseClass");
        Until(delegate {return Visible("HomeClass-terminal");}, "return to Live after capacity result");
    }

    private static string CourseScenario()
    {
        string type=Find("LabTask").Current.ItemType;
        return type.Split(new[]{"    ·    "},StringSplitOptions.None)[0];
    }
}

internal static class LiveCourseEntryMain
{
    [STAThread]
    private static int Main(string[] args)
    { return LiveUiSmoke.RunLiveCourseCards(Path.GetFullPath(args[0]),Path.GetFullPath(args[1])); }
}
