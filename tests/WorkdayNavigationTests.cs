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
                CheckHandoff(Path.Combine(root, "handoff-course"));
                ExplorerPathTests.Run(Path.Combine(root, "file-path"));
                QuickJobTests.Run(Path.Combine(root, "desk-jobs"));
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

                    position.SetValue(course, steps.Count - 1);
                    type.GetMethod("OpenForStep", Fields).Invoke(course, new object[] {
                        openFolder, "test editor", Path.Combine(root, "missing-app.exe"), "", matches, null });
                    course.ActivateMission();
                    int credit = course.Completed;
                    int stage = course.StageNumber;
                    if (!course.CanReopenWindow || !course.Notice.Contains("test editor"))
                        throw new Exception("An unavailable application left no recovery on the current step");
                    course.ReopenWindow();
                    if (!course.CanReopenWindow || course.StageNumber != stage || course.Completed != credit)
                        throw new Exception("Retrying a failed launch changed progress");
                    if (course.Poll(target) || course.WaitingForWindow || course.CanReopenWindow)
                        throw new Exception("Retry remained active after the correct window opened");
                }
                using (var console = new DesktopCourse(root, DesktopTrack.Console))
                {
                    console.SkipMission();
                    if (console.RoundNumber != 2 || console.Completed != 0 ||
                        new DesktopItinerary(root).CompletedConsoleMissions != 0) throw new Exception("Skipping completed a console mission");
                }
                Console.WriteLine("PASS: linked ticket handoff, file-dialog retries, independent Workday starts, skips and endless rollover.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void CheckHandoff(string root)
        {
            string decks = Path.Combine(root, "DesktopPractice", "decks");
            Directory.CreateDirectory(decks);
            foreach (string deck in new[] { "day", "day-with-browser" })
                File.WriteAllText(Path.Combine(decks, deck + ".txt"), "0 2");
            using (var course = new DesktopCourse(root))
            {
                var courseType = typeof(DesktopCourse);
                var steps = (IList)courseType.GetField("steps", Fields).GetValue(course);
                var stepType = steps[0].GetType();
                var shortcut = stepType.GetField("Shortcut");
                var outcome = stepType.GetField("Result");
                string[] order = { "Ctrl+O", "Enter", "Ctrl+End", "Shift+Home", "Ctrl+C", "Ctrl+O", "Enter", "Ctrl+End", "Ctrl+V", "Ctrl+S" };
                course.SkipMission();
                course.SkipMission();
                int start = course.StageNumber - 1;
                if (!course.Goal.StartsWith("Передайте номер заявки") || !course.WaitingForWindow || course.MissionLength != order.Length)
                    throw new Exception("The prepared handoff was unavailable after the two short jobs");
                for (int i = 0; i < order.Length; i++)
                    if ((string)shortcut.GetValue(steps[start + i]) != order[i] || !DesktopInput.IsSupported(order[i]))
                        throw new Exception("Broken ticket workflow at " + i);
                string folder = Path.Combine(course.Folder, "Inbox-1");
                string source = Directory.GetFiles(folder, "ticket-*.txt")[0], card = Directory.GetFiles(folder, "handoff-*.txt")[0];
                string[] lines = File.ReadAllLines(source);
                if ((string)stepType.GetField("CopyText").GetValue(steps[start + 1]) != source ||
                    stepType.GetField("CopyText").GetValue(steps[start + 6]) != null || lines.Length != 3 ||
                    !lines[2].EndsWith(Path.GetFileName(course.Folder).Substring(11), StringComparison.OrdinalIgnoreCase) ||
                    !File.ReadAllText(card).EndsWith("Reference: ") || outcome.GetValue(steps[start + 4]) == null || outcome.GetValue(steps[start + 9]) == null)
                    throw new Exception("Incoming reference, clipboard preservation or final card check is missing");

                var accepted = stepType.GetField("Accepted");
                var sourceCheck = stepType.GetField("Source");
                var confirmed = new IntPtr(78);
                int dialogs = 0;
                for (int enter = 1; enter < steps.Count; enter++)
                {
                    if ((string)shortcut.GetValue(steps[enter]) == "Win+R")
                        throw new Exception("Workday required entering a launch command in Run");
                    if (!(bool)stepType.GetField("AcceptNoteDialog").GetValue(steps[enter])) continue;
                    dialogs++;
                    if ((string)shortcut.GetValue(steps[enter - 1]) == "Ctrl+Shift+S" &&
                        stepType.GetField("Launch").GetValue(steps[enter - 1]) == null)
                        throw new Exception("The prepared draft did not open in its own project folder");
                    ((Action<IntPtr>)accepted.GetValue(steps[enter - 1]))(confirmed);
                    var allowed = (Func<IntPtr, bool>)sourceCheck.GetValue(steps[enter]);
                    if (!allowed(confirmed) || allowed(IntPtr.Zero)) throw new Exception("Lost Enter from verified dialog after it closed");
                    var nextDialog = new IntPtr(123);
                    stepType.GetField("NoteDialog").SetValue(steps[enter], nextDialog);
                    if (!allowed(nextDialog) || allowed(confirmed)) throw new Exception("Replacement dialog did not supersede dismissed window");
                }
                if (dialogs != 3) throw new Exception("Both Open dialogs and the project's Save As dialog must support retry");
                string template = File.ReadAllText(card);
                string expected = template.Replace("\r\n", "\n") + lines[2];
                var sameText = courseType.GetMethod("SameText", BindingFlags.NonPublic | BindingFlags.Static);
                Func<bool> signed = () => (bool)sameText.Invoke(null, new object[] { card, expected });
                if (signed()) throw new Exception("An empty handoff was accepted");
                File.WriteAllText(card, template + lines[2] + "-wrong");
                if (signed()) throw new Exception("A mistyped reference was accepted");
                File.WriteAllText(card, template + lines[2]);
                if (!signed()) throw new Exception("A corrected reference was rejected");
            }
        }
    }
}
