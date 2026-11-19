using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace TouchTypingStudio
{
    internal static class WorkdayEditorTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "WorkdayEditor-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var course = new DesktopCourse(root))
                {
                    if (RuntimeTools.Current.Code != null)
                    {
                        string previous = "";
                        for (int round = 1; round <= 2; round++)
                        {
                            while (course.RoundNumber == round && !course.Goal.Contains("VS Code")) course.SkipMission();
                            if (course.RoundNumber != round || course.Completed != 0 || course.Shortcut != "Ctrl+P" || previous == course.Goal)
                                throw new Exception("Editor practice repeated, was hidden or awarded unearned points");
                            previous = course.Goal;
                            if (course.Goal.Contains("Python"))
                            {
                                var steps = (IList)typeof(DesktopCourse).GetField("steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(course);
                                string[] keys = { "Ctrl+P", "Enter", "Ctrl+Home", "Ctrl+D", "Ctrl+Shift+L", "Ctrl+S" };
                                for (int n = 0; n < keys.Length; n++)
                                {
                                    var step = steps[course.StageNumber - 1 + n];
                                    if ((string)step.GetType().GetField("Shortcut").GetValue(step) != keys[n] ||
                                        !DesktopInput.IsSupported(keys[n]) || step.GetType().GetField("Result").GetValue(step) == null)
                                        throw new Exception("Python rename missed a keyboard action or verification");
                                }
                            }
                            if (round == 1) course.Recover();
                        }
                    }
                    var prepare = typeof(DesktopCourse).GetMethod("PrepareCodeNote", BindingFlags.Instance | BindingFlags.NonPublic);
                    string project = Path.Combine(course.Folder, "test-001-01234567");
                    string original = Path.Combine(project, "review-test.txt");
                    string template = "QA / Windows\nStatus: ready";
                    object[] args = { original, template, null };
                    string separate = (string)prepare.Invoke(course, args);
                    if (Directory.Exists(project) || separate == original || !File.Exists(separate) ||
                        File.ReadAllText(separate).Replace("\r\n", "\n") != template || (string)args[2] != "QA / Windows\nStatus: reviewed")
                        throw new Exception("Cold start did not stage the review note in an independent workspace");
                    Directory.CreateDirectory(project);
                    File.WriteAllText(original, "My project\r\nStatus: ready");
                    if ((string)prepare.Invoke(course, args) != original || (string)args[2] != "My project\nStatus: reviewed")
                        throw new Exception("Existing reviewable handoff lost the learner's first line");
                    using (File.Open(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        if ((string)prepare.Invoke(course, args) != separate) throw new Exception("Locked handoff blocked the editor");
                    File.WriteAllText(original, "Unfinished note");
                    if ((string)prepare.Invoke(course, args) != separate || File.ReadAllText(original) != "Unfinished note")
                        throw new Exception("An unfinished handoff was overwritten");
                }
                Console.WriteLine("PASS: editor reachable after skips, independent note, existing and locked handoffs.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
