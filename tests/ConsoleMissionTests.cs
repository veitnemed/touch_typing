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
                        if (step.Done()) throw new InvalidOperationException("Premature completion: " + step.Command);
                        if (step.Command.Contains("handoff.zip"))
                        {
                            string path = Path.Combine(folder, "handoff.zip");
                            File.WriteAllText(path, "not an archive", Encoding.ASCII);
                            if (step.Done()) throw new InvalidOperationException("Invalid archive passed.");
                            File.Delete(path);
                        }
                        string command = step.Command;
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
                CheckCompletionSequence(root);
                Console.WriteLine("PASS: " + commands + " commands executed in real cmd.exe across six console missions, including ZIP handoffs.");
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

        private static void Execute(string folder, string command)
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
            }
        }
    }
}
