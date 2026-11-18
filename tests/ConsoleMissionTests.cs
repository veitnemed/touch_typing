using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TouchTypingStudio
{
    internal static class ConsoleMissionTests
    {
        public static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "Typing console QA " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            int commands = 0;
            int completions = 0;
            int inspections = 0;
            var brief = new DesktopBriefing("qa", "Учебная смена", "wheel", "RETRY", "studio");
            try
            {
                for (int recipe = 0; recipe < 6; recipe++)
                {
                    string initial = Directory.CreateDirectory(Path.Combine(root, "first-" + recipe)).FullName;
                    var first = new ConsoleMission(initial, recipe, brief, 261 + recipe, 0);
                    if (first.Commands.Any(p => p.Command.Contains("count.txt") || p.Command.Contains("handoff.zip")))
                        throw new InvalidOperationException("Beginner challenge exceeded its scope.");
                    string folder = Directory.CreateDirectory(Path.Combine(root, "advanced-" + recipe)).FullName;
                    var advanced = new ConsoleMission(folder, recipe, brief, 419 + recipe, 6);
                    if (advanced.Commands.Count != first.Commands.Count + 2)
                        throw new InvalidOperationException("Advanced console skills did not unlock.");
                    foreach (var step in advanced.Commands)
                    {
                        if (new[] { step.SourceLabel, step.DestinationLabel, step.Outcome }.Any(string.IsNullOrWhiteSpace))
                            throw new InvalidOperationException("Missing learner context: " + step.Action);
                        if (step.Done()) throw new InvalidOperationException("Premature completion: " + step.Command);
                        if (step.Command.Contains("handoff.zip"))
                        {
                            string path = Path.Combine(folder, "handoff.zip");
                            File.WriteAllText(path, "not an archive", Encoding.ASCII);
                            if (step.Done()) throw new InvalidOperationException("Invalid archive passed.");
                            File.Delete(path);
                        }
                        string command = step.Command;
                        if (step.ReadCommand != null)
                        {
                            string[] visible = Execute(folder, step.ReadCommand).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (visible.Length < 2 || step.Done() || string.IsNullOrWhiteSpace(step.TaskHint))
                                throw new InvalidOperationException("Inspection must display useful input and leave the outcome pending.");
                            string incorrect;
                            command = DecideFromOutput(recipe, visible, out incorrect);
                            Execute(folder, incorrect);
                            if (step.Done()) throw new InvalidOperationException("Wrong decision accepted: " + incorrect);
                            commands += 2;
                            inspections++;
                        }
                        if (step.CompletionPrefix != null)
                        {
                            int argument = step.CompletionPrefix.LastIndexOf(' ') + 1;
                            string stem = step.CompletionPrefix.Substring(argument);
                            string[] matches = Directory.GetFileSystemEntries(folder, stem + "*");
                            if (matches.Length != 1 || Path.GetFileName(matches[0]) != step.CompletionName)
                                throw new InvalidOperationException("Tab cannot uniquely complete " + step.CompletionPrefix);
                            command = step.CompletionPrefix.Substring(0, argument) + Path.GetFileName(matches[0]);
                            completions++;
                        }
                        Execute(folder, command);
                        if (!step.Done()) throw new InvalidOperationException("Missing result: " + step.Command);
                        if (command.StartsWith("copy /y *.", StringComparison.Ordinal))
                        {
                            string extra = Path.Combine(folder, "handoff", "scratch.txt");
                            File.WriteAllText(extra, "not part of the handoff");
                            if (step.Done()) throw new InvalidOperationException("Unrequested file included in a batch handoff.");
                            File.Move(extra, Path.Combine(folder, "scratch.txt"));
                            if (!step.Done()) throw new InvalidOperationException("Corrected batch handoff rejected.");
                        }
                        commands++;
                    }
                    if (recipe == 0 && File.Exists(Path.Combine(Environment.SystemDirectory, "tar.exe")))
                    {
                        File.Move(Path.Combine(folder, "handoff.zip"), Path.Combine(folder, "previous.zip"));
                        Execute(folder, ConsoleMission.ArchiveCommand(false));
                        if (!advanced.Commands.Last().Done()) throw new InvalidOperationException("Legacy .NET ZIP contents rejected.");
                        commands++;
                    }
                    string report = Path.Combine(folder, "handoff", advanced.Delivery);
                    File.AppendAllText(report, "tampered content");
                    if (advanced.Commands.Last().Done())
                        throw new InvalidOperationException("Out-of-date archive credited.");
                    string midway = Directory.CreateDirectory(Path.Combine(root, "midway-" + recipe)).FullName;
                    var intermediate = new ConsoleMission(midway, recipe, brief, 731 + recipe, 3);
                    if (intermediate.Commands.Count != first.Commands.Count + 1 ||
                        !intermediate.Commands.Last().Command.Contains("count.txt"))
                        throw new InvalidOperationException("Intermediate console skills did not unlock.");
                }
                if (completions != 2) throw new InvalidOperationException("Expected two filename completion prompts.");
                if (inspections != 6) throw new InvalidOperationException("Each recipe needs an inspection and a choice.");
                CheckCompletionSequence(root);
                CheckInspectionSequence(root);
                Console.WriteLine("PASS: " + commands + " commands executed in real cmd.exe across six console missions, including ZIP handoffs.");
                Console.WriteLine("PASS: six read-only inspections, answers derived from actual console output, wrong file/rows rejected, correction accepted.");
                Console.WriteLine("PASS: two unique CMD completions, scoped Tab/Enter order, mouse reset and required output.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void CheckCompletionSequence(string root)
        {
            // Use the production course's actual staged prompts. Replace the foreground/result
            // probes to exercise the keyboard state machine without opening a learner window.
            const BindingFlags privateField = BindingFlags.Instance | BindingFlags.NonPublic;
            IntPtr expected = new IntPtr(17);
            bool resultReady = false;
            for (int round = 0; round < 6; round++)
            using (var course = new DesktopCourse(root, DesktopTrack.Console))
            {
                var steps = (IList)typeof(DesktopCourse).GetField("steps", privateField).GetValue(course);
                for (int i = 0; i < steps.Count; i++)
                {
                    var item = steps[i];
                    var fields = item.GetType();
                    if (fields.GetField("CompletionPrefix").GetValue(item) == null) continue;
                    typeof(DesktopCourse).GetField("index", privateField).SetValue(course, i);
                    fields.GetField("Source").SetValue(item, new Func<IntPtr, bool>(hwnd => hwnd == expected));
                    fields.GetField("Result").SetValue(item, new Func<IntPtr, bool>(hwnd => resultReady && hwnd == expected));
                    Action<string, IntPtr> key = (name, hwnd) => course.Key(new DesktopStroke {
                        Chord = name, Window = hwnd, Timestamp = DateTime.UtcNow });
                    key("Enter", expected);
                    key("Tab", IntPtr.Zero);
                    if (course.Shortcut != "Tab" || course.Poll(expected))
                        throw new InvalidOperationException("Wrong-window Tab or premature Enter passed.");
                    key("Tab", expected);
                    if (course.Shortcut != "Enter" || course.Poll(expected) || course.Completed != 0)
                        throw new InvalidOperationException("Completion without execution passed.");
                    course.RejectMouseAction();
                    if (course.Shortcut != "Tab") throw new InvalidOperationException("Mouse did not reset completion.");
                    key("Tab", expected);
                    key("Enter", expected);
                    if (course.Poll(expected)) throw new InvalidOperationException("Missing command output passed.");
                    resultReady = true;
                    key("Enter", expected);
                    if (!course.Poll(expected) || course.Completed != 1)
                        throw new InvalidOperationException("Verified completion was not credited once.");
                    return;
                }
            }
            throw new InvalidOperationException("Console deck never offered a Tab prompt.");
        }

        private static string DecideFromOutput(int recipe, string[] visible, out string wrong)
        {
            switch (recipe)
            {
                case 0:
                    string journal = visible.Single(line => line.Contains(" timeout ")).Split(':')[0];
                    string other = visible.Single(line => line.Contains(" backlog ")).Split(':')[0];
                    wrong = @"copy /y " + other + @" handoff\incident.log";
                    return @"copy /y " + journal + @" handoff\incident.log";
                case 1:
                    string open = visible.Single(line => !line.StartsWith("CLOSED ")).Split(' ')[0];
                    wrong = "findstr /b CLOSED *.ticket > open.txt";
                    return "findstr /b " + open + " *.ticket > open.txt";
                case 2:
                    string tableExtension = Path.GetExtension(visible.Single(name => name.StartsWith("cost-")));
                    wrong = "dir /b *.txt > manifest.txt";
                    return "dir /b *" + tableExtension + " > manifest.txt";
                case 3:
                    string configExtension = Path.GetExtension(visible.Single(name => name.StartsWith("release-")));
                    wrong = @"dir /b handoff\release-* > handoff\manifest.txt";
                    return @"dir /b handoff\*" + configExtension + @" > handoff\manifest.txt";
                case 4:
                    string outstanding = visible.Single(line => line.Contains(" verify ")).Split(' ')[0];
                    wrong = "findstr /b DONE brief.txt > next.txt";
                    return "findstr /b " + outstanding + " brief.txt > next.txt";
                case 5:
                    string highestPriority = visible.First().Split(' ')[0];
                    wrong = "findstr /b P3 tasks.txt > today.txt";
                    return "findstr /b " + highestPriority + " tasks.txt > today.txt";
                default: throw new ArgumentOutOfRangeException("recipe");
            }
        }

        private static void CheckInspectionSequence(string root)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var target = new IntPtr(25);
            using (var course = new DesktopCourse(root, DesktopTrack.Console))
            {
                var steps = (IList)typeof(DesktopCourse).GetField("steps", fields).GetValue(course);
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    var type = step.GetType();
                    string inspect = (string)type.GetField("ReadCommand").GetValue(step);
                    if (inspect == null) continue;
                    bool ready = false;
                    typeof(DesktopCourse).GetField("index", fields).SetValue(course, i);
                    type.GetField("Source").SetValue(step, new Func<IntPtr, bool>(w => w == target));
                    type.GetField("Result").SetValue(step, new Func<IntPtr, bool>(w => w == target && ready));
                    var enter = new DesktopStroke { Chord = "Enter", Window = target, Timestamp = DateTime.UtcNow };
                    if (course.CopyText != inspect) throw new Exception("Inspection example must show the read-only command.");
                    course.Key(enter);
                    if (course.Poll(target) || course.Rejected != 0 || course.Completed != 0 || course.CopyText != null)
                        throw new Exception("Looking at files changed the score or revealed the answer.");
                    course.Key(enter);
                    if (course.Poll(target)) throw new Exception("Unanswered inspection was credited.");
                    ready = true;
                    course.Key(enter);
                    if (!course.Poll(target) || course.Completed != 1) throw new Exception("Completed decision was not credited.");
                    return;
                }
            }
            throw new Exception("Missing read-and-decide prompt in actual route.");
        }

        private static string Execute(string folder, string command)
        {
            using (var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c " + command)
            {
                WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                if (!child.WaitForExit(30000))
                {
                    child.Kill();
                    throw new TimeoutException(command);
                }
                if (child.ExitCode != 0)
                    throw new InvalidOperationException(command + " exited " + child.ExitCode + ": " + child.StandardError.ReadToEnd());
                return child.StandardOutput.ReadToEnd();
            }
        }
    }
}
