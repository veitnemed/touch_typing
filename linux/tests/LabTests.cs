using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal static class LabTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Chord(LabRun run, string keys, bool releaseModifiers = true)
    {
        var chord = Gesture.Parse(keys).Chords[0];
        var modifiers = chord.Modifiers.Select(key => key switch { "Ctrl" => "RCtrl", "Shift" => "LShift", "Alt" => "LAlt", "Super" => "LSuper", _ => key }).ToArray();
        foreach (string modifier in modifiers) run.Input(modifier, true, "");
        if (chord.Trigger != null) { run.Input(chord.Trigger, true, ""); run.Input(chord.Trigger, false, ""); }
        if (releaseModifiers) foreach (string modifier in modifiers.Reverse()) run.Input(modifier, false, "");
    }
    private static void Type(LabRun run, string text)
    {
        foreach (char c in text)
        {
            var hint = TypingKeyboard.Hint(c, run.Scene.Layout == "RU" ? Layout.Russian : Layout.English);
            if (hint == null) throw new InvalidOperationException("Unsupported test character " + c);
            string shift = hint.Modifier == LinuxKey.LeftShift ? "LShift" : hint.Modifier == LinuxKey.RightShift ? "RShift" : "";
            if (shift.Length > 0) run.Input(shift, true, "");
            string key = ShortcutCatalog.KeyName(hint.Cap);
            run.Input(key, true, c.ToString()); run.Input(key, false, "");
            if (shift.Length > 0) run.Input(shift, false, "");
        }
    }
    private static void Complete(LabRun run)
    {
        var task = run.Task;
        if (task.Keys.Length > 0)
        {
            int before = run.Solved;
            for (int i = 0; i < 30 && run.Solved == before; i++) Chord(run, task.Keys);
        }
        else
        {
            Type(run, task.Text);
            if (task.Action != "type-code") Chord(run, "Enter");
        }
    }
    private static LabRun Create(string app, int seed = 1, int level = 2, Func<double> clock = null)
        => new(app, "sandbox", level, 180, "EN", [], new Random(seed), clock);
    private static void Main()
    {
        foreach (string app in LabCatalog.Apps)
        foreach (int level in new[] { 0, 1, 2 })
        {
            var courses = LiveCourse.Missions.Where(m => m.App == app && m.Level == level).ToList();
            Check(courses.Count == (app == "workday" ? 1 : 4), "Course count: " + app);
            foreach (var course in courses)
            foreach (int variant in Enumerable.Range(0, 3))
            foreach (string layout in new[] { "RU", "EN" })
            {
                var initial = new LabScene(app == "workday" ? "ubuntu" : app, layout);
                initial.PrepareCase(variant, app);
                var route = LiveCourse.Compile(course, initial);
                Check(route.Count >= 20, course.Id + ": minimum route length");
                Check(route.Select(step => step.Task.Id).Distinct().Count() >= 8, course.Id + ": distinct actions");
                Check(route.Select(step => step.Phase).Distinct().Count() >= 3, course.Id + ": meaningful phases");
            }
        }
        var reportRun = new LabRun("workday", "sandbox", 0, 0, "EN", [], new Random(3), missionId: "workday-report");
        reportRun.Resume();
        const string verifyId = "command/head -n 2 spectrum-report.txt";
        for (int i = 0; i < 50 && reportRun.Task.Id != verifyId; i++) Complete(reportRun);
        Check(reportRun.Task.Id == verifyId && reportRun.ReportArtifact == null, "Earlier steps do not award report evidence");
        var report = reportRun.Scene.ReadReportArtifact();
        Check(report.Content.Split('\n').Length > 3 && !report.DownloadStillPresent, "Delivery creates a multiline report only in the project");
        var lines = report.Content.Split('\n');
        lines[2] = "quietly corrupted outside the terminal preview";
        reportRun.Scene.Terminal.StoreFile(report.Path, string.Join("\n", lines));
        int reportScore = reportRun.Solved;
        Complete(reportRun);
        Check(reportRun.Scene.Terminal.LastSuccess && reportRun.Scene.Terminal.LastOutput.SequenceEqual(lines.Take(2)) &&
            reportRun.Solved == reportScore && reportRun.ReportArtifact == null && reportRun.Status.Contains("отчёт не совпадает"),
            "Successful preview cannot verify corrupted report content below line two");
        reportRun.Scene.Terminal.StoreFile(report.Path, report.Content);
        reportRun.Scene.Terminal.StoreFile("/home/student/Downloads/spectrum-report.txt", report.Content);
        Complete(reportRun);
        Check(reportRun.Solved == reportScore && reportRun.ReportArtifact == null, "Copy does not qualify as moved delivery");
        reportRun.Scene.Terminal.Execute("rm ~/Downloads/spectrum-report.txt");
        Complete(reportRun);
        Check(reportRun.Solved == reportScore + 1 && reportRun.ReportArtifact == report, "Exact saved report is verified as an artifact");

        // Hold the actual profile mutex on another thread; the timeout must not
        // disappear when Update falls back to returning readable profile contents.
        using (var ready = new System.Threading.ManualResetEventSlim())
        using (var release = new System.Threading.ManualResetEventSlim())
        {
            var writer = new ProfileStore(true, System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            var lockThread = System.Threading.Tasks.Task.Run(() =>
            {
                using var occupied = new System.Threading.Mutex(false, @"Local\TouchTypingStudio.QA.Profile");
                occupied.WaitOne();
                try { ready.Set(); release.Wait(); }
                finally { occupied.ReleaseMutex(); }
            });
            try
            {
                Check(ready.Wait(5000), "Another thread locked the QA profile");
                bool changed = false;
                var snapshot = writer.Update(_ => changed = true);
                Check(snapshot != null && !changed && writer.Error?.Contains("Профиль занят") == true,
                    "Failed profile update retains storage error after fallback read");
            }
            finally { release.Set(); lockThread.GetAwaiter().GetResult(); }
        }

        double now = 0;
        var unlimited = new LabRun("chrome", "sandbox", 0, 0, "EN", [], new Random(4), () => now);
        unlimited.Resume(); now += 8 * 3600 * 1000;
        Check(!unlimited.Finished && unlimited.Seconds == 8 * 3600, "An untimed lesson survives eight hours");
        var timed = Create("chrome", clock: () => now);
        now += 100000; Check(timed.Seconds == 0, "Capture waiting excludes clock");
        timed.Resume(); now += 10000; timed.Pause(); now += 90000; Check(timed.Seconds == 10, "Pause excludes clock");
        timed.Resume(); Complete(timed); now += 170000;
        Check(timed.Finished && timed.Seconds == 180, "Timed completion");
        var record = timed.SessionRecord(); Check(record.Completed && record.Solved > 0, "Completed session record");
        var result = JsonSerializer.SerializeToElement(timed.Result([record]), Program.Json);
        Check(result.GetProperty("newRecord").GetBoolean(), "New record only after full session");
        Check(Create("chrome").SessionRecord().Completed == false, "Early finish record excluded");
        var scene = new LabScene("code", "EN");
        Check(scene.Active.Body.Contains("np.fft.fft"), "Code source comes from piling fixture");
        scene.Apply("comment"); Check(scene.Active.Body != scene.Active.Tab.SavedBody, "Editor dirty content");
        scene.Apply("save"); Check(scene.Active.Body == scene.Active.Tab.SavedBody, "Save writes document snapshot");
        var files = new LabScene("files", "EN");
        const string file = "/home/student/notes.txt", occupied = "/home/student/.Trash/1-notes.txt";
        const string movedFile = "/home/student/.Trash/2-notes.txt";
        files.Terminal.StoreFile(file, "original");
        files.Terminal.StoreFile(occupied, "already in the bin");
        files.Active.Item = Array.FindIndex(files.Entries(files.Active), item => item.Path == file);
        Check(files.Apply("trash") && files.Terminal.ReadFile(occupied) == "already in the bin" &&
            files.Terminal.ReadFile(movedFile) == "original", "Deterministic trash skips an occupied slot without overwriting");
        var clonedFiles = files.Clone();
        Check(clonedFiles.Apply("restore-trash") && clonedFiles.Terminal.ReadFile(file) == "original" &&
            files.Terminal.ReadFile(movedFile) == "original", "Restoring the cloned bin does not mutate the source scene");
        clonedFiles.Active.Item = Array.FindIndex(clonedFiles.Entries(clonedFiles.Active), item => item.Path == file);
        Check(clonedFiles.Apply("trash") && clonedFiles.Terminal.ReadFile("/home/student/.Trash/3-notes.txt") == "original",
            "Cloning carries the bin allocation sequence forward");

        var shot = new LabScene("ubuntu", "EN"); shot.Apply("screenshot"); Check(shot.Screenshot && !shot.ScreenshotReady, "Print opens capture panel");
        shot.Apply("screenshot-confirm"); Check(!shot.Screenshot && shot.ScreenshotReady, "Enter captures scene"); shot.Apply("copy-shot"); Check(shot.ScreenshotCopied, "Clipboard owns capture");
        foreach (string layout in new[] { "EN", "RU" })
        {
            var run = new LabRun("terminal", "sandbox", 2, 180, layout, [], new Random(1)); run.Resume();
            // The physical V key works in either layout while both modifiers remain down.
            run.Input("RCtrl", true, ""); run.Input("LShift", true, "");
            int old = run.Scene.Version;
            run.Input("V", true, layout == "RU" ? "М" : "V");
            Check(run.Scene.Version > old && run.Scene.TerminalInput.Length > 0, "Physical paste in " + layout);
            int same = run.Scene.Version; run.Input("V", true, "М"); Check(run.Scene.Version == same, "Autorepeat ignored");
            run.Input("V", false, "");
            run.Input("LShift", false, ""); run.Input("RCtrl", false, "");
            Chord(run, "Ctrl+U");
            Check(run.Scene.TerminalInput == "", "Readline kills to beginning in " + layout);
        }
        foreach (string layout in new[] { "EN", "RU" })
        {
            var edit = new LabRun("terminal", "sandbox", 0, 0, layout, [], new Random(3));
            edit.Resume();
            string line = layout == "RU" ? "абвгде" : "abcdef";
            Type(edit, line);
            int startingScore = edit.Solved;
            edit.Input("Left", true, "");
            edit.Input("Left", true, "");
            edit.Input("Left", false, "");
            Check(edit.Scene.Active.Cursor == 4, "Held Left moves caret twice in " + layout);
            edit.Input("Delete", true, "");
            edit.Input("Delete", true, "");
            edit.Input("Delete", false, "");
            Check(edit.Scene.TerminalInput == line[..4], "Held Delete removes forward in " + layout);
            edit.Input("Backspace", true, "");
            edit.Input("Backspace", true, "");
            edit.Input("Backspace", false, "");
            Check(edit.Scene.TerminalInput == line[..2] && edit.Scene.Active.Cursor == 2, "Held Backspace removes backward in " + layout);
            Check(edit.Solved == startingScore, "Repeat edits do not count as training steps");
            var reopened = new LabRun("terminal", "sandbox", 0, 0, layout, [], new Random(3), draft: edit.Draft());
            Check(reopened.Scene.TerminalInput == line[..2] && reopened.Scene.Active.Cursor == 2,
                "Current draft replays bounded editing repeats in " + layout);
            Chord(edit, "Ctrl+U");
            Type(edit, "pwd");
            edit.Input("Enter", true, "");
            string[] output = edit.Scene.Terminal.History;
            edit.Input("Enter", true, "");
            edit.Input("Enter", false, "");
            Check(edit.Scene.Terminal.History.SequenceEqual(output), "Held Enter does not submit twice");
            Type(edit, line);
            edit.Input("LShift", true, "");
            edit.Pause();
            edit.Resume();
            edit.Input("LShift", false, "");
            Chord(edit, "Left");
            Check(!edit.Scene.Active.Selected, "Focus loss clears held Shift before navigation");
        }
        var editor = Create("code");
        editor.Resume();
        string body = editor.Scene.Active.Body;
        editor.Scene.Active.Cursor = body.IndexOf("np.fft.fft", StringComparison.Ordinal);
        if (editor.Scene.Active.Cursor < 0) editor.Scene.Active.Cursor = 0;
        int beforeFind = editor.Scene.Active.Cursor;
        Chord(editor, "Ctrl+F");
        Check(editor.Scene.Active.Focus == "find", "Find opens as a separate editor field");
        Type(editor, "notfoundxyz");
        Chord(editor, "Esc");
        Check(editor.Scene.Active.Focus == "body" && editor.Scene.Active.Body == body && editor.Scene.Active.Cursor == beforeFind,
            "Escape returns to the document without replacing its content");
        int originalCaret = editor.Scene.Active.Cursor;
        editor.Input("Right", true, "");
        editor.Input("Right", true, "");
        editor.Input("Right", false, "");
        Check(editor.Scene.Active.Cursor == originalCaret + 2, "Held Right edits the focused document");

        var strict = new LabRun("chrome", "sandbox", 0, 0, "EN", [], new Random(3));
        strict.Resume();
        for (int step = 0; step < 100 && strict.Task.Id != "url"; step++) Complete(strict);
        Check(strict.Task.Id == "url", "Browser route reaches the physical Ctrl+L step");
        int previousScore = strict.Solved;
        Chord(strict, "Alt+D");
        Check(strict.Scene.Active.Focus == "url" && strict.Scene.Active.Selected && strict.Solved == previousScore,
            "Alt+D selects the address without satisfying Ctrl+L");
        var currentDraft = strict.Draft();
        LabRun Reopen(LiveDraft draft) => new("chrome", "sandbox", 0, 0, "EN", [], new Random(3), draft: draft);
        var reopenedCurrent = Reopen(currentDraft);
        Check(reopenedCurrent.Task.Id == "url" && reopenedCurrent.Scene.Active.Selected,
            "Current trace keeps address effects without crediting the alternate");
        var oldEvents = JsonSerializer.Deserialize<LiveEvent[]>(currentDraft.Trace)
            .Where(item => item.Key != "@exact-url");
        currentDraft.Trace = JsonSerializer.Serialize(oldEvents);
        var reopenedOld = Reopen(currentDraft);
        Check(reopenedOld.Task.Id != "url", "Legacy draft does not revoke earlier alternate credit");
        Check(Reopen(reopenedOld.Draft()).Task.Id == reopenedOld.Task.Id,
            "Legacy state remains stable after upgrade and re-save");
        Chord(strict, "Ctrl+L");
        Check(strict.Solved == previousScore + 1, "Physical Ctrl+L passes even after Alt+D selected the address");

        var tie = new LabSessionRecord { Session = "previous", App = timed.App, Mode = timed.Mode, Level = timed.Level, Completed = true, Solved = timed.Solved, Errors = timed.Errors + 1 };
        var tiedResult = JsonSerializer.SerializeToElement(timed.Result([tie, record]), Program.Json);
        Check(tiedResult.GetProperty("newRecord").GetBoolean(), "Equal score with fewer errors improves record");
        var retry = Create("chrome"); retry.Resume();
        string missed = retry.Task.Id;
        retry.Input("LCtrl", true, ""); retry.Input("F12", true, ""); retry.Input("F12", false, ""); retry.Input("LCtrl", false, "");
        Check(retry.Errors == 1 && retry.Task.Id == missed, "Mistake keeps current instruction for retry");
        Complete(retry); Check(retry.Solved == 1, "Correct retry verifies scene after miss");
        var sceneTabs = new LabScene("chrome", "EN");
        Check(sceneTabs.Apply("new-tab") && sceneTabs.Active.Body.Length == 0 && sceneTabs.Active.Input.Length == 0,
            "New Chrome tab is really blank");
        Check(!LabCatalog.Available(LabCatalog.Shortcuts.Single(item => item.Id == "select-all"), sceneTabs) &&
              !LabCatalog.Available(LabCatalog.Shortcuts.Single(item => item.Id == "cut"), sceneTabs),
            "Empty address cannot offer select-all or cut");
        sceneTabs.Apply("close-tab");
        string address = sceneTabs.Active.Url;
        sceneTabs.Apply("url");
        Check(LabCatalog.Available(LabCatalog.Shortcuts.Single(item => item.Id == "cut"), sceneTabs), "Selected real URL can be cut");
        sceneTabs.Apply("cut"); sceneTabs.Apply("paste"); sceneTabs.Apply("navigate-pasted");
        Check(sceneTabs.Active.Url == address, "Cut and paste restores a navigable address");
        var codeTabs = new LabScene("code", "EN");
        codeTabs.Apply("new-tab");
        Check(!LabCatalog.Available(LabCatalog.Shortcuts.Single(item => item.Id == "select-all"), codeTabs) &&
              !LabCatalog.Available(LabCatalog.Shortcuts.Single(item => item.Id == "cut"), codeTabs),
            "Empty editor cannot offer select-all or cut");
        codeTabs.Apply("close-tab");
        string original = codeTabs.Active.Body;
        codeTabs.Apply("select-all"); codeTabs.Apply("cut"); codeTabs.Apply("paste");
        Check(codeTabs.Active.Body == original, "Code select-cut-paste restores real source");
        var wrongAction = Create("code"); wrongAction.Resume();
        var pending = wrongAction.Task; int unchanged = wrongAction.Scene.Version;
        Chord(wrongAction, "Alt+F4");
        Check(wrongAction.Task == pending && wrongAction.Scene.Version > unchanged && wrongAction.Scene.Windows.Count == 0,
            "Alt+F4 closes only the virtual window while the lesson stays active");
        Chord(wrongAction, "Super+2");
        Check(wrongAction.Scene.Active?.App == "code", "User can reopen the simulated editor");
        LabRun function = null;
        for (int seed = 1; seed <= 8 && function == null; seed++)
        {
            var candidate = Create("chrome", seed, 2); candidate.Resume();
            for (int index = 0; index < 200 && candidate.Task.Id != "fullscreen"; index++) Complete(candidate);
            if (candidate.Task.Id == "fullscreen") function = candidate;
        }
        Check(function != null, "F11 scene is reachable");
        int beforeShot = function.Scene.Version, beforeError = function.Errors;
        function.Input("Print", true, ""); function.Input("Print", false, "");
        Check(function.Task.Id == "fullscreen" && function.Scene.Version == beforeShot && !function.Scene.Screenshot &&
              function.Errors == beforeError + 1 && function.LastKey == "Print" && function.Status.Contains("Fn+F11"),
            "MateBook Print event cannot satisfy F11 or create an unintended screenshot");
        int solvedFunction = function.Solved; Chord(function, "F11");
        Check(function.Solved == solvedFunction + 1, "Real F11 physical key completes fullscreen step");
        var repair = Create("terminal"); repair.Resume();
        for (int index = 0; repair.Task.Action != "command" && index < 200; index++) Complete(repair);
        Check(repair.Task.Action == "command", "Terminal command story is reachable");
        string commandTask = repair.Task.Id, oldCommand = repair.Scene.Terminal.LastCommand;
        Type(repair, "not-a-command"); Chord(repair, "Enter");
        Check(repair.Errors == 1 && repair.Task.Id == commandTask && repair.Scene.TerminalInput == "" &&
              repair.Scene.Terminal.LastCommand == "not-a-command" && !repair.Scene.Terminal.LastSuccess, "Shell executes an unknown command, shows failure and leaves the task pending");
        Chord(repair, "Ctrl+U");
        Check(repair.Scene.TerminalInput.Length == 0 && repair.Task.Id == commandTask, "Ctrl+U repairs command without skipping task");
        int repaired = repair.Solved; Type(repair, repair.Task.Text); Chord(repair, "Enter");
        Check(repair.Solved == repaired + 1, "Corrected command completes the same scenario step");
        var unicode = Create("code"); unicode.Resume();
        unicode.Scene.Active.Focus = "body"; unicode.Scene.Active.Body = "";
        Check(unicode.Scene.Text("A", "ф") && unicode.Scene.Active.Body == "ф", "Scene preserves actual Unicode before virtual layout switch");
        unicode.Input("A", true, "ф"); unicode.Input("A", false, "");
        Check(unicode.Scene.Active.Body == "фф" && unicode.Errors == 0, "Ordinary Unicode editing works during a shortcut mission");
        var oldWinner = new LabSessionRecord { Session = "old-winner", App = "chrome", Mode = "sandbox", Level = 2,
            Completed = true, Seconds = 180, Solved = 250, Errors = 2, Date = DateTime.Now.AddYears(-2) };
        var migrated = new StudioProfile(); migrated.LabSessions.Add(oldWinner);
        for (int index = 0; index < 510; index++)
            LabSessionRecords.Update(migrated, new LabSessionRecord { Session = "later-" + index, App = "chrome", Mode = "sandbox", Level = 2,
                Completed = true, Seconds = 180, Solved = 30, Errors = 1, Date = DateTime.Now.AddMinutes(index) });
        Check(migrated.LabSessions.Count == 500 && migrated.LabSessions.All(item => item.Session != "old-winner"), "Bounded history evicts old run");
        Check(migrated.LabRecords.Single().Session == "old-winner", "All-time record survives 510 later worse sessions");
        var oldRecordResult = JsonSerializer.SerializeToElement(Create("chrome").Result(migrated.LabSessions, migrated.LabRecords), Program.Json);
        Check(oldRecordResult.GetProperty("record").GetInt32() == 250, "Result includes surviving all-time record");
        Check(oldRecordResult.GetProperty("history").EnumerateArray().Select(item => item.GetProperty("session").GetString()).Distinct().Count() ==
            oldRecordResult.GetProperty("history").GetArrayLength(), "Result history deduplicates record sessions");
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(StudioProfile));
        using (var buffer = new System.IO.MemoryStream())
        {
            serializer.Serialize(buffer, migrated); buffer.Position = 0;
            var reopened = (StudioProfile)serializer.Deserialize(buffer);
            Check(reopened.LabRecords.Single().Session == "old-winner", "All-time record survives profile XML roundtrip");
        }
        var legacy = new StudioProfile(); legacy.LabSessions.Add(oldWinner);
        for (int index = 0; index < 500; index++) legacy.LabSessions.Add(new LabSessionRecord { Session = "legacy-" + index,
            App = "chrome", Mode = "sandbox", Level = 2, Completed = true, Seconds = 180, Solved = 10 });
        LabSessionRecords.Update(legacy, new LabSessionRecord { Session = "migration", App = "chrome", Mode = "sandbox", Level = 2, Solved = 1 });
        Check(legacy.LabRecords.Single().Session == "old-winner" && legacy.LabSessions.Count == 500, "Legacy history migrates record before trimming");
        Console.WriteLine("PASS " + checks + " lab checks across state-valid chains, virtual commands, RU/EN physical input, pause and records");
    }
}
