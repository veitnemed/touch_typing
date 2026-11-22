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
        if (task.Keys.Length > 0) Chord(run, task.Keys);
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
        var initialUbuntu = LabScenarios.Eligible("ubuntu", new LabScene("ubuntu", "EN"), 2).Select(item => item.Id).ToHashSet();
        var initialChrome = LabScenarios.Eligible("chrome", new LabScene("chrome", "EN"), 2).Select(item => item.Id).ToHashSet();
        var initialCode = LabScenarios.Eligible("code", new LabScene("code", "EN"), 2).Select(item => item.Id).ToHashSet();
        var initialTerminal = LabScenarios.Eligible("terminal", new LabScene("terminal", "EN"), 2).Select(item => item.Id).ToHashSet();
        Check(initialUbuntu.Contains("ubuntu-start") && initialUbuntu.Contains("ubuntu-capture"), "Ubuntu opens from desktop and offers capture chain");
        Check(initialChrome.Contains("chrome-address-cut") && initialChrome.Contains("chrome-fullscreen"), "Chrome offers meaningful address cut and physical F11 chains");
        Check(initialCode.Contains("code-cut") && initialCode.Contains("code-quick-open"), "VS Code offers real text selection and keyboard file picker");
        Check(initialTerminal.Contains("terminal-keys-basic") && initialTerminal.Contains("terminal-files"), "Terminal offers connected paste and file chains");
        foreach (string app in LabCatalog.Apps)
        foreach (int level in new[] { 0, 1, 2 })
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var run = Create(app, seed, level);
            Check(run.Paused, "Starts paused before native capture"); run.Resume();
            Check(run.Scene.Windows.Count == (app == "ubuntu" ? 0 : 1), "Only the relevant application is opened at start");
            var seen = new HashSet<string>();
            for (int index = 0; index < 60; index++)
            {
                int before = run.Solved;
                var task = run.Task; seen.Add(task.Id);
                Check(run.ScenarioTitle.Length > 0 && run.ScenarioStep is >= 1 && run.ScenarioStep <= run.ScenarioLength,
                    "Connected scenario and step are visible");
                Check(task.Level <= level, "Scenario does not leak a higher-level task");
                if (app is "chrome" or "code") Check(task.Keys.Length > 0 && task.Text.Length == 0, "Live app class uses shortcuts only");
                Check(task.Keys.Length == 0 || LabCatalog.Available(task, run.Scene), "State valid " + app + ": " + task.Id);
                Complete(run);
                Check(run.Solved == before + 1, app + "/" + level + " seed" + seed + " task " + task.Id + " failed: " + run.Status);
                Check(run.Scene.Windows.Count <= (app == "ubuntu" ? 4 : 1), "Window count remains bounded");
            }
            Check(run.Block == 4, "Continuing blocks of20 " + app);
            Check(seen.Count >= (level == 0 ? 5 : 10), "Diverse tasks " + app + "/" + level + "/" + seen.Count);
            Check(run.Errors == 0, "Valid chain has no errors " + app);
        }
        double now = 0;
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
        var shot = new LabScene("ubuntu", "EN"); shot.Apply("screenshot"); Check(shot.Screenshot && !shot.ScreenshotReady, "Print opens capture panel");
        shot.Apply("screenshot-confirm"); Check(!shot.Screenshot && shot.ScreenshotReady, "Enter captures scene"); shot.Apply("copy-shot"); Check(shot.ScreenshotCopied, "Clipboard owns capture");
        foreach (string layout in new[] { "EN", "RU" })
        {
            var run = new LabRun("terminal", "sandbox", 2, 180, layout, [], new Random(1)); run.Resume();
            for (int index = 0; run.Task.Id != "terminal-paste" && index < 300; index++) Complete(run);
            Check(run.Task.Id == "terminal-paste", "Linked terminal paste becomes reachable in " + layout);
            // The physical V key works in either layout while both modifiers remain down.
            run.Input("RCtrl", true, ""); run.Input("LShift", true, "");
            int old = run.Scene.Version;
            run.Input("V", true, layout == "RU" ? "М" : "V");
            Check(run.Scene.Version > old && run.Scene.TerminalInput.Length > 0, "Physical paste in " + layout);
            int same = run.Scene.Version; run.Input("V", true, "М"); Check(run.Scene.Version == same, "Autorepeat ignored");
            run.Input("V", false, "");
            var cut = LabCatalog.Resolve("X", ["RCtrl", "LShift"], run.Scene);
            Check(cut?.Id == "terminal-cut", "Physical CtrlShiftX resolves in " + layout);
            Check(run.Scene.Apply(cut.Action) && run.Scene.TerminalInput.Length == 0, "Physical CtrlShiftX scene effect in " + layout);
            Check(run.Task.Id != "terminal-cut", "Guided task still controls scoring in " + layout);
        }
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
        Check(wrongAction.Task == pending && wrongAction.Scene.Version == unchanged && wrongAction.Scene.Windows.Count == 1 &&
              wrongAction.LastKey == "F4", "Wrong valid shortcut leaves guided scene unchanged and marks actual key");
        LabRun function = null;
        for (int seed = 1; seed <= 8 && function == null; seed++)
        {
            var candidate = Create("chrome", seed, 0); candidate.Resume();
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
        Check(repair.Errors == 1 && repair.Task.Id == commandTask && repair.Scene.TerminalInput == "not-a-command" &&
              repair.Scene.Terminal.LastCommand == oldCommand, "Wrong terminal command preserves input and virtual system");
        Chord(repair, "Ctrl+U");
        Check(repair.Scene.TerminalInput.Length == 0 && repair.Task.Id == commandTask, "Ctrl+U repairs command without skipping task");
        int repaired = repair.Solved; Type(repair, repair.Task.Text); Chord(repair, "Enter");
        Check(repair.Solved == repaired + 1, "Corrected command completes the same scenario step");
        var unicode = Create("code"); unicode.Resume();
        unicode.Scene.Active.Focus = "body"; unicode.Scene.Active.Body = "";
        Check(unicode.Scene.Text("A", "ф") && unicode.Scene.Active.Body == "ф", "Scene preserves actual Unicode before virtual layout switch");
        unicode.Input("A", true, "ф"); unicode.Input("A", false, "");
        Check(unicode.Scene.Active.Body == "ф" && unicode.Errors == 1, "Wrong text does not mutate guided shortcut scene");
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
