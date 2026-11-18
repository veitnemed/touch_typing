using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TouchTypingStudio
{
    internal static class WorkdayNavigationTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "WorkdayNavigation-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var course = new DesktopCourse(root))
                {
                    var type = typeof(DesktopCourse);
                    var position = type.GetField("index", Fields);
                    var steps = (IList)type.GetField("steps", Fields).GetValue(course);
                    var starts = new HashSet<string>();
                    for (int project = 0; project < 4; project++)
                    {
                        starts.Add(course.Goal);
                        var groups = (SortedDictionary<int, Func<bool>>)type.GetField("missionEntries", Fields).GetValue(course);
                        int oldRound = course.RoundNumber;
                        int previous = -1;
                        foreach (var pair in groups)
                        {
                            // A brand-new project lacks the saved note required by the editor.
                            if (!pair.Value()) continue;
                            if (previous >= 0)
                            {
                                int before = course.Completed;
                                position.SetValue(course, previous);
                                if (!course.SkipMission() || course.StageNumber != pair.Key + 1 ||
                                    course.RoundNumber != oldRound || course.Completed != before)
                                    throw new Exception("Skip did not reach the next independent, available task");
                            }
                            previous = pair.Key;
                        }
                        foreach (object step in steps)
                        {
                            if (step.GetType().GetField("ConsoleTitle").GetValue(step) != null)
                                throw new Exception("Console syntax in the Workday route");
                        }
                        position.SetValue(course, steps.Count - 1);
                        course.SkipMission();
                        if (course.RoundNumber != oldRound + 1 || course.StageNumber != 1)
                            throw new Exception("Last task did not continue into a new project");
                    }
                    if (starts.Count < 3 || Directory.GetDirectories(course.Folder, "cmd-*").Length != 0)
                        throw new Exception("Workday starts or desktop-only isolation failed");

                    // Give a step a foreground binding to reproduce a key arriving before
                    // the first polling tick. Only the verified window and chord may count.
                    object current = steps[0];
                    var stepType = current.GetType();
                    var target = new IntPtr(123);
                    Func<IntPtr, bool> matches = w => w == target;
                    foreach (string field in new[] { "BindWindow", "Source", "Result" })
                        stepType.GetField(field).SetValue(current, matches);
                    stepType.GetField("Accepted").SetValue(current, null);
                    string key = course.Shortcut;
                    DateTime now = DateTime.UtcNow;
                    course.Key(new DesktopStroke { Window = IntPtr.Zero, Timestamp = now, Chord = key });
                    if (!course.WaitingForWindow || course.Poll(IntPtr.Zero)) throw new Exception("Bound an unrelated window");
                    course.Key(new DesktopStroke { Window = target, Timestamp = now, Chord = key });
                    if (!course.Poll(target) || course.Completed != 1) throw new Exception("First valid shortcut was dropped");
                    course.SkipMission();
                    if (course.Completed != 1) throw new Exception("Skipping changed the confirmed score");

                    // When Explorer opens folders in separate windows, later copy/move
                    // steps must use the verified destination window as their source.
                    var explorer = type.GetField("explorer", Fields);
                    explorer.SetValue(course, target);
                    type.GetMethod("EnterExplorerChild", Fields).Invoke(course,
                        new object[] { "Open child", Path.Combine(root, "Inbox") });
                    object openFolder = steps[steps.Count - 1];
                    var source = (Func<IntPtr, bool>)stepType.GetField("Source").GetValue(openFolder);
                    var result = (Func<IntPtr, bool>)stepType.GetField("Result").GetValue(openFolder);
                    var accepted = (Action<IntPtr>)stepType.GetField("Accepted").GetValue(openFolder);
                    var destination = new IntPtr(456);
                    if (source(IntPtr.Zero) || result(IntPtr.Zero) || accepted == null)
                        throw new Exception("Explorer folder entry did not validate its source and bind its destination");
                    accepted(destination);
                    if ((IntPtr)explorer.GetValue(course) != destination)
                        throw new Exception("Explorer continued observing the window left behind by folder navigation");
                }
                using (var console = new DesktopCourse(root, DesktopTrack.Console))
                {
                    console.SkipMission();
                    if (console.RoundNumber != 2 || console.Completed != 0 ||
                        new DesktopItinerary(root).CompletedConsoleMissions != 0) throw new Exception("Skipping completed a console mission");
                }
                Console.WriteLine("PASS: desktop-only Workday, independent task skips, retained score, endless rollover and early first shortcut.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
