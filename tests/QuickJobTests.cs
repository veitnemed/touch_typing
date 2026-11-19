using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TouchTypingStudio
{
    internal static class QuickJobTests
    {
        public static void Run(string root)
        {
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var goals = new HashSet<string>();
            using (var course = new DesktopCourse(root))
            {
                var steps = (IList)typeof(DesktopCourse).GetField("steps", hidden).GetValue(course);
                for (int round = 1; round <= 3; round++)
                {
                    foreach (string desk in Directory.GetDirectories(course.Folder, "Desk-" + round + "-*"))
                    {
                        int kind = int.Parse(Path.GetFileName(desk).Split('-')[2]);
                        if (kind == 1 || kind == 2) CheckDelivery(desk, kind == 1);
                    }
                    for (int slot = 0; slot < 2; slot++)
                    {
                        int start = course.StageNumber - 1, count = course.MissionLength;
                        if (!goals.Add(course.Goal) || count < 2 || count > 6 || !course.WaitingForWindow)
                            throw new Exception("Expected a fresh short independently launchable desktop job");
                        for (int i = start; i < start + count; i++)
                        {
                            object step = steps[i];
                            var t = step.GetType();
                            string chord = (string)t.GetField("Shortcut").GetValue(step);
                            string literal = (string)t.GetField("CopyText").GetValue(step);
                            if (!DesktopInput.IsSupported(chord) || (literal != null && Path.IsPathRooted(literal)))
                                throw new Exception("A quick job requires an unsupported key or full address");
                        }
                        var final = steps[start + count - 1];
                        if (final.GetType().GetField("Result").GetValue(final) == null)
                            throw new Exception("The job credited its final shortcut without checking a real result");
                        course.SkipMission();
                    }
                    if (round < 3) course.Recover();
                }
            }
            Console.WriteLine("PASS: six distinct 2-6 step native jobs, direct launch, no typed paths, durable copy/move results and retries.");
        }

        private static void CheckDelivery(string desk, bool copy)
        {
            string from = Path.Combine(desk, copy ? "Input" : "Approved");
            string to = Path.Combine(desk, copy ? "Send" : "Archive");
            string[] files = Directory.GetFiles(from);
            string[] contents = files.Select(File.ReadAllText).ToArray();
            if (DesktopCourse.DeliveryComplete(to, files, contents, copy)) throw new Exception("Empty outbox passed");
            foreach (string file in files)
            {
                string destination = Path.Combine(to, Path.GetFileName(file));
                if (copy) File.Copy(file, destination); else File.Move(file, destination);
            }
            if (!DesktopCourse.DeliveryComplete(to, files, contents, copy)) throw new Exception("Delivery not recognized");
            string first = Path.Combine(to, Path.GetFileName(files[0]));
            File.AppendAllText(first, "-wrong");
            if (DesktopCourse.DeliveryComplete(to, files, contents, copy)) throw new Exception("Damaged delivery was accepted");
            File.WriteAllText(first, contents[0]);
            if (!DesktopCourse.DeliveryComplete(to, files, contents, copy)) throw new Exception("Corrected delivery rejected");
            if (copy) File.Delete(files[0]); else File.Copy(first, files[0]);
            if (DesktopCourse.DeliveryComplete(to, files, contents, copy)) throw new Exception("Move and copy confused");
        }
    }
}
