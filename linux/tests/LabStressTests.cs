using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal static class LabStressTests
{
    private static int checks, sessions, events, recoveries;
    private static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class Driver
    {
        internal readonly LabRun Run;
        internal readonly Random Random;
        internal double Now;
        private readonly Queue<string> trace = [];
        private readonly string context;
        internal Driver(string app, string mode, int level, int seed)
        {
            context = $"app={app} mode={mode} level={level} seed={seed}";
            Random = new Random(seed);
            Run = new LabRun(app, mode, level, 180, seed % 2 == 0 ? "RU" : "EN", [], new Random(seed), () => Now);
            sessions++;
        }
        internal void Check(bool condition, string message)
            => Assert(condition, context + " task=" + Run.Task.Id + " " + message + " trace=" + string.Join(" | ", trace));
        internal void Event(string key, bool down, string text = "")
        {
            trace.Enqueue((down ? "+" : "-") + key + (text.Length == 0 ? "" : "[" + text.Replace("\n", "↵") + "]"));
            while (trace.Count > 36) trace.Dequeue();
            Now += 1; events++;
            int old = Run.Solved, errors = Run.Errors;
            try { Run.Input(key, down, text); }
            catch (Exception error) { throw new InvalidOperationException(context + " task=" + Run.Task.Id + " crash trace=" + string.Join(" | ", trace), error); }
            Check(Run.Solved >= old && Run.Solved <= old + 1, "One physical event scores at most once");
            Check(Run.Errors >= errors, "Errors are monotonic");
            Check(Run.Held.Distinct().Count() == Run.Held.Length, "Held keys contain no duplicates");
            Check(Run.Remaining >= 0 && Run.Remaining <= 180, "Time remains bounded");
        }
        internal void Release()
        { foreach (string key in Run.Held.ToArray().Reverse()) Event(key, false); }
        internal void Chord(string keys, bool retainModifiers = false, bool reverseModifiers = false, int repeats = 0)
        {
            var chord = Gesture.Parse(keys).Chords[0];
            string[] modifiers = chord.Modifiers.Select(key => key switch
            { "Ctrl" => Random.Next(2) == 0 ? "LCtrl" : "RCtrl", "Shift" => Random.Next(2) == 0 ? "LShift" : "RShift", "Alt" => "LAlt", "Super" => "LSuper", _ => key }).ToArray();
            if (reverseModifiers) Array.Reverse(modifiers);
            foreach (string modifier in modifiers) Event(modifier, true);
            if (chord.Trigger != null)
            {
                Event(chord.Trigger, true, chord.Modifiers.Count() > 0 ? "М" : "");
                int accepted = Run.Solved;
                for (int repeat = 0; repeat < repeats; repeat++)
                { Event(chord.Trigger, true, "V"); Check(Run.Solved == accepted, "Autorepeat cannot add a second success"); }
                Event(chord.Trigger, false);
            }
            if (!retainModifiers) foreach (string modifier in modifiers.Reverse()) Event(modifier, false);
        }
        internal void Type(string text)
        {
            foreach (char character in text)
            {
                var hint = TypingKeyboard.Hint(character, Run.Scene.Layout == "RU" ? Layout.Russian : Layout.English);
                Check(hint != null, "Template character can be entered " + character);
                string shift = hint.Modifier == LinuxKey.LeftShift ? "LShift" : hint.Modifier == LinuxKey.RightShift ? "RShift" : "";
                if (shift.Length > 0) Event(shift, true);
                string key = ShortcutCatalog.KeyName(hint.Cap);
                Event(key, true, character.ToString()); Event(key, false);
                if (shift.Length > 0) Event(shift, false);
            }
        }
        internal void Complete()
        {
            Release();
            var task = Run.Task;
            int before = Run.Solved;
            if (task.Keys.Length > 0)
            {
                for (int i = 0; i < 32 && Run.Solved == before; i++)
                    Chord(task.Keys, reverseModifiers: Random.Next(2) == 0, repeats: 3);
            }
            else
            {
                var window = Run.Scene.Active;
                if (task.Action == "launch")
                {
                    int length = Run.Scene.Search.Length;
                    for (int index = 0; index < length; index++) Chord("Backspace");
                }
                else if (task.Action == "command") Chord("Ctrl+U");
                else if ((task.Action == "type-code" && window?.Body.Length > 0) || window?.Input.Length > 0) Chord("Ctrl+A");
                Type(task.Text);
                if (task.Action != "type-code") Chord("Enter");
            }
            Check(Run.Solved == before + 1, "Displayed instruction must remain solvable after recovery: " + Run.Status);
        }
    }

    private static void GuidedStress()
    {
        foreach (string app in LabCatalog.Apps)
        foreach (string mode in new[] { "drill", "sandbox" })
        foreach (int level in new[] { 0, 1, 2 })
        foreach (int seed in new[] { 17, 42, 309 })
        {
            var driver = new Driver(app, mode, level, seed);
            driver.Chord("Ctrl+Shift+V");
            driver.Check(driver.Run.Solved == 0 && driver.Run.Errors == 0 && driver.Run.Seconds == 0, "Uncaptured/paused input has no effect");
            driver.Run.Resume();
            for (int cycle = 0; cycle < 100; cycle++)
            {
                if (cycle % 7 == 0)
                {
                    driver.Event("RCtrl", true); driver.Event("LShift", true);
                    int solved = driver.Run.Solved, errors = driver.Run.Errors, version = driver.Run.Scene.Version;
                    driver.Run.Pause(); driver.Now += 30000;
                    driver.Chord("Ctrl+Shift+V");
                    driver.Check(driver.Run.Solved == solved && driver.Run.Errors == errors && driver.Run.Scene.Version == version,
                        "Focus loss discards keys and prevents scene changes");
                    driver.Check(driver.Run.Held.Length == 0, "Pause clears held modifiers");
                    driver.Run.Resume();
                }
                if (cycle % 9 == 0)
                {
                    driver.Event("Unmapped-" + cycle, true); driver.Event("Unmapped-" + cycle, true); driver.Event("Unmapped-" + cycle, false);
                    driver.Event("RightCtrlUnknown", false);
                }
                if (cycle % 11 == 0)
                {
                    driver.Chord("Ctrl+Alt+F12"); // Unsupported physical combination must never close the simulation.
                    driver.Check(!driver.Run.Finished, "Unsupported shortcut cannot end a session");
                }
                if (cycle % 13 == 0)
                {
                    // Interrupt the current action, then recover before resuming it.
                    driver.Event("LAlt", true);
                    driver.Run.Pause();
                    driver.Run.Resume();
                    driver.Check(driver.Run.Held.Length == 0, "Interrupted modifier cannot stick");
                    recoveries++;
                }
                driver.Complete();
                driver.Check(driver.Run.Solved >= cycle + 1, "Guided chain keeps advancing");
                driver.Check(driver.Run.Block == driver.Run.Solved / 20 + 1, "Chain blocks continue after20 successes");
            }
            driver.Release();
            driver.Run.Pause(); int seconds = driver.Run.Seconds; driver.Now += 900000;
            driver.Check(driver.Run.Seconds == seconds, "Long pause excludes wall time"); driver.Run.Resume();
            driver.Now += (180 - seconds) * 1000 + 1;
            driver.Check(driver.Run.Finished && driver.Run.Remaining == 0 && driver.Run.Seconds == 180, "Deadline is exact");
            int finalCount = driver.Run.Solved, finalErrors = driver.Run.Errors, finalVersion = driver.Run.Scene.Version;
            for (int index = 0; index < 6; index++) driver.Chord("Ctrl+Shift+V");
            driver.Check(driver.Run.Solved == finalCount && driver.Run.Errors == finalErrors && driver.Run.Scene.Version == finalVersion,
                "Post-deadline events cannot change score or scene");
        }
    }

    private static void PhysicalAndUnicodeEdges()
    {
        foreach (string app in new[] { "terminal", "code" })
        foreach (string layout in new[] { "EN", "RU" })
        foreach (string ctrl in new[] { "LCtrl", "RCtrl" })
        foreach (string shift in new[] { "LShift", "RShift" })
        {
            var scene = new LabScene(app, layout);
            if (app == "code") scene.Active.Focus = "terminal";
            scene.Clipboard = "pwd";
            for (int count = 1; count <= 4; count++)
            {
                var paste = LabCatalog.Resolve("V", [ctrl, shift], scene);
                Assert(paste?.Id == "terminal-paste", "Physical V resolves in " + layout);
                Assert(scene.Apply(paste.Action) && scene.TerminalInput == "pwd", "Physical paste changes virtual terminal in " + layout);
                var erase = LabCatalog.Resolve("U", [ctrl], scene);
                Assert(erase?.Id == "terminal-erase", "Ctrl+U resolves in " + layout);
                Assert(scene.Apply(erase.Action) && scene.TerminalInput.Length == 0, "Readline removes command before caret");
            }
        }
        var unicode = new LabScene("code", "RU");
        unicode.Active.Body = ""; unicode.Active.Focus = "body";
        foreach (string value in new[] { "ф", "ё", "№", "Ж", "🐍" })
            Assert(unicode.Text("UnknownUnicode", value), "Scene accepts received Unicode");
        Assert(unicode.Active.Body == "фё№Ж🐍", "Received Unicode is preserved including supplementary code points");
        Assert(unicode.Text("Backspace", "") && unicode.Active.Body == "фё№Ж", "Backspace removes a whole supplementary character");
        unicode.Text("UnknownUnicode", "a\u0301");
        Assert(unicode.Text("Backspace", "") && unicode.Active.Body == "фё№Ж", "Backspace removes a combining sequence as a whole text element");
        unicode.Active.Focus = "terminal"; unicode.TerminalInput = "command-that-does-not-exist";
        Assert(unicode.Text("Enter", "") && !unicode.Terminal.LastSuccess && unicode.TerminalInput == "", "Unknown virtual command reports failure and returns prompt");
    }

    private static JsonElement Message(string command, params object[] values)
    {
        var fields = new Dictionary<string, object> { ["command"] = command };
        for (int index = 0; index < values.Length; index += 2) fields[(string)values[index]] = values[index + 1];
        return JsonSerializer.SerializeToElement(fields, Program.Json);
    }
    private static JsonElement Call(PracticeService service, string command, params object[] values)
        => JsonSerializer.SerializeToElement(service.Handle(Message(command, values)), Program.Json);
    private static void PersistenceEdges()
    {
        string old = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string directory = Path.Combine(Path.GetTempPath(), "lab-stress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); Environment.SetEnvironmentVariable("XDG_DATA_HOME", directory);
        try
        {
            var service = new PracticeService(true);
            Call(service, "configure", "layout", "RU", "platform", "linux", "forward", "Super+Space", "backward", "Super+Shift+Space");
            var state = Call(service, "lab-start", "app", "chrome", "mode", "sandbox", "level", 0, "mission", "chrome-reader", "seed", 432);
            Call(service, "lab-resume");
            var chord = Gesture.Parse(state.GetProperty("task").GetProperty("keys").GetString()).Chords[0];
            foreach (string key in chord.Modifiers.Select(key => key switch { "Ctrl" => "RCtrl", "Shift" => "RShift", "Alt" => "LAlt", "Super" => "LSuper", _ => key }).Concat(chord.Trigger == null ? [] : new[] { chord.Trigger }))
                Call(service, "lab-key", "key", key, "pressed", true, "text", "М");
            // Also close modifier-only strokes, then duplicate stop/result/shutdown operations.
            foreach (string key in new[] { "RCtrl", "RShift", "LAlt", "LSuper", chord.Trigger ?? "V" })
                Call(service, "lab-key", "key", key, "pressed", false, "text", "");
            Call(service, "lab-stop"); Call(service, "lab-stop"); service.FinishLab();
            var result = Call(service, "lab-result");
            Assert(result.GetProperty("history").GetArrayLength() == 1, "Duplicate finish produces one persisted session");
            Assert(!result.GetProperty("completed").GetBoolean() && !result.GetProperty("newRecord").GetBoolean(), "Early stop cannot award record");
            var reopened = new PracticeService(true); Call(reopened, "lab-start", "app", "chrome", "mode", "sandbox", "level", 0);
            Assert(Call(reopened, "lab-result").GetProperty("history").GetArrayLength() == 1, "Session is retained across service recreation");
            Call(reopened, "lab-start", "app", "terminal", "mode", "drill", "level", 0);
            Assert(Call(reopened, "lab-result").GetProperty("history").GetArrayLength() == 0, "App/mode histories stay independent");
            string profile = Path.Combine(directory, "TouchTypingStudio-QA", "progress.xml");
            byte[] intact = File.ReadAllBytes(profile); byte[] broken = "<broken-profile>"u8.ToArray(); File.WriteAllBytes(profile, broken);
            var damaged = new PracticeService(true); Call(damaged, "settings", "keys", false);
            Assert(damaged.StorageError != null && File.ReadAllBytes(profile).SequenceEqual(broken), "Corrupt profile is reported and preserved");
            File.WriteAllBytes(profile, intact); var recovered = new PracticeService(true);
            Call(recovered, "lab-start", "app", "chrome", "mode", "sandbox", "level", 0);
            Assert(Call(recovered, "lab-result").GetProperty("history").GetArrayLength() == 1, "Restored profile recovers session data");
        }
        finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", old); Directory.Delete(directory, true); }
    }

    private static int Main()
    {
        try
        {
            GuidedStress(); PhysicalAndUnicodeEdges(); PersistenceEdges();
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", suite = "lab-adversarial", checks, sessions, events, recoveries }, Program.Json));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "fail", suite = "lab-adversarial", checks, sessions, events, recoveries, error = error.ToString() }, Program.Json));
            return 1;
        }
    }
}
