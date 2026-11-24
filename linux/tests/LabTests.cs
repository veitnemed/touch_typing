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
    private sealed class MissionDraws(params double[] draws) : Random
    {
        private int index;
        public override double NextDouble() => draws[index++];
    }

    private static void TestCommandReviewWeighting()
    {
        static ShortcutAttempt Miss(string skill, string layout = "EN") => new() { Skill = skill, Correct = false, Layout = layout };
        string Pick(double[] draws, params ShortcutAttempt[] history) =>
            new LabRun("terminal", "sandbox", 0, 0, "EN", history, new MissionDraws(draws)).MissionId;

        double[] pwdDraws = [.4, .8, .8, .25];
        var pwd = Miss("lab/terminal/command/pwd");
        Check(Pick(pwdDraws) == "terminal-investigate", "Unweighted draw selects a route without pwd");
        Check(Pick(pwdDraws, pwd, pwd) == "terminal-explore",
            "Recorded pwd misses favor an otherwise unselected route that actually practices pwd");
        Check(Pick(pwdDraws, pwd) == Pick(pwdDraws, pwd, pwd),
            "Repeated misses identify the same skill, not multiple skill IDs");
        Check(Pick(pwdDraws, Miss("lab/code/command/pwd")) == "terminal-investigate",
            "Matching command recorded in another class does not affect the terminal picker");
        double[] pathDraws = [.8, .8, .4, .3];
        Check(Pick(pathDraws, Miss("lab/terminal/command/head -n 2 backup/signals.py")) == "terminal-backup",
            "Picker retains the full path of a spaced command");
        Check(Pick(pathDraws, Miss("lab/terminal/command/signals.py")) == "terminal-investigate",
            "Path suffix alone does not match a command");

        var pwdRoute = LiveCourse.Missions.Single(m => m.Id == "terminal-explore");
        var terminal = new LabScene("terminal", "RU");
        terminal.PrepareCase(0, "terminal");
        var goals = LiveCourse.Compile(pwdRoute, terminal);
        Check(goals.FindIndex(g => g.Task.Id == "layout" && g.Layout == "RU") <
              goals.FindIndex(g => g.Task.Id == "command/pwd" && g.Layout == "EN") &&
              goals.Any(g => g.Task.Id == "layout" && g.Layout == "RU") && terminal.Layout == "RU",
            "RU-start plan records the switch in RU and pwd in EN without changing the starting desk");
        Check(LiveCourse.CountWeakGoals(goals, [("command/pwd", "EN")]) > 0 &&
              LiveCourse.CountWeakGoals(goals, [("command/pwd", "RU")]) == 0 &&
              LiveCourse.CountWeakGoals(goals, [("layout", "RU")]) == 1 &&
              LiveCourse.CountWeakGoals(goals, [("layout", "EN")]) == 0,
            "Only the layout of the actual goal contributes to mission weight");
        LabRun StartRu(params ShortcutAttempt[] history) =>
            new("terminal", "sandbox", 0, 0, "RU", history, new MissionDraws(pwdDraws));
        Check(StartRu(pwd).MissionId == "terminal-explore" && StartRu(pwd).Scene.Layout == "RU",
            "EN pwd misses choose the right RU-start route without running its layout switch");
        Check(StartRu(Miss("lab/terminal/command/pwd", "RU")).MissionId == "terminal-investigate" &&
              StartRu(Miss("lab/terminal/command/pwd", null)).MissionId == "terminal-investigate",
            "Wrong-layout and unlabeled legacy misses cannot affect the choice");

        var editor = new LabScene("code", "EN");
        editor.PrepareCase(0, "code");
        var editorGoals = LiveCourse.Compile(LiveCourse.Missions.Single(m => m.Id == "code-guide"), editor);
        Check(LiveCourse.CountWeakGoals(editorGoals, [("quick-next", "EN")]) > 0 &&
              LiveCourse.CountWeakGoals(editorGoals, [("quick:signals.py", "EN")]) == 0 &&
              LiveCourse.CountWeakGoals(editorGoals, [("quick-next", "RU")]) == 0,
            "Picker weighting uses the compiled task identity and layout, not the raw token");

        var withSpacedPath = pwdRoute with { Phases = [..pwdRoute.Phases,
            new("Read report", "cmd:mkdir~'/home/student/Work~Notes' cmd:echo~hello~>~'/home/student/Work~Notes/report.txt' cmd:cat~'/home/student/Work~Notes/report.txt'")] };
        var spaced = LiveCourse.Compile(withSpacedPath, terminal);
        Check(LiveCourse.CouldPracticeAny(withSpacedPath, ["command/cat '/home/student/Work Notes/report.txt'"]) &&
              !LiveCourse.CouldPracticeAny(pwdRoute, ["command/cat '/home/student/Work Notes/report.txt'"]) &&
              LiveCourse.CouldPracticeAny(pwdRoute, ["layout"]) &&
              LiveCourse.CouldPracticeAny(LiveCourse.Missions.Single(m => m.Id == "code-guide"), ["quick-next"]),
            "Rehearsal prefilter handles exact commands, implicit layout switches and translated picker IDs");
        Check(LiveCourse.CountWeakGoals(spaced, [("command/cat '/home/student/Work Notes/report.txt'", "EN")]) == 1 &&
              LiveCourse.CountWeakGoals(spaced, [("command/cat '/home/student/Other Notes/report.txt'", "EN")]) == 0,
            "A quoted path with spaces matches only the full command, not an equal basename in another directory");

        var recentOther = new ShortcutAttempt { Skill = "lab/terminal/command/ls", Correct = true };
        Check(Pick(pwdDraws, new[] { pwd }.Concat(Enumerable.Repeat(recentOther, 60)).ToArray()) == "terminal-investigate",
            "An older pwd miss beyond the current 60 attempts does not increase weight");
        var run = new LabRun("terminal", "sandbox", 0, 0, "EN", [], new Random(3), missionId: "terminal-explore");
        run.Resume();
        for (int i = 0; i < 5 && run.Task.Id != "command/pwd"; i++) Complete(run);
        Check(run.Task.Id == "command/pwd", "The chosen route really reaches a pwd goal");
        Type(run, "invalid-command");
        Chord(run, "Enter");
        var actualMiss = run.Attempts.Last();
        Check(!actualMiss.Correct && actualMiss.Skill == "lab/terminal/command/pwd",
            "A failed key-driven pwd submission records the matching full skill ID");
        double[] freshDraws = [.8, .4, .3]; // recent terminal-explore is excluded
        Check(Pick(freshDraws, actualMiss) == "terminal-backup",
            "Novelty still excludes the recently practiced route; another route practicing pwd gains priority");
    }

    private static void TestNativeEnglishTextAndRouteProgress()
    {
        var freshRussian = new LabRun("terminal", "sandbox", 0, 0, "RU", [], new Random(3),
            missionId: "terminal-explore", nativeEnglishInput: true);
        Check(freshRussian.Task.Action == "command", "A lesson launched in RU requests real EN, not a virtual layout prerequisite");
        var nativeDraft = new LabRun("terminal", "sandbox", 0, 0, "RU", [], draft: freshRussian.Draft());
        Check(nativeDraft.Task.Id == freshRussian.Task.Id, "New native input draft replays without synthetic steps");
        var oldRussian = new LabRun("terminal", "sandbox", 0, 0, "RU", [], new Random(3), missionId: "terminal-explore");
        var oldDraft = new LabRun("terminal", "sandbox", 0, 0, "EN", [], draft: oldRussian.Draft(), nativeEnglishInput: true);
        Check(oldRussian.Task.Id == "layout" && oldDraft.Task.Id == "layout", "Existing virtual layout drafts are not reinterpreted");
        var layoutDrill = new LabRun("ubuntu", "sandbox", 1, 0, "RU", [], new Random(3),
            missionId: "ubuntu-matebook-layout", nativeEnglishInput: true);
        var virtualGoals = LiveCourse.Compile(LiveCourse.Missions.Single(m => m.Id == layoutDrill.MissionId),
            new LabScene("ubuntu", "RU"), nativeEnglishInput: true);
        Check(virtualGoals.Count(goal => goal.Task.Id == "layout") == 2, "Dedicated virtual layout training retains its required keys");

        var run = new LabRun("terminal", "sandbox", 0, 0, "EN", [], new Random(3), missionId: "terminal-explore");
        run.Resume();
        Check(run.ScenarioCompletedGoals == 0 && run.ScenarioStep == 1, "A new route has zero completed goals");
        for (int i = 0; i < 10 && run.Task.Id != "command/pwd"; i++) Complete(run);
        Check(run.Task.Id == "command/pwd", "The native layout check exercises the real pwd mission");
        var before = (run.Solved, run.Errors, run.Scene.Version, run.Scene.TerminalInput, run.Scene.Active.Cursor, run.Draft().Trace);
        run.Input("P", true, "з", "RU");
        run.Input("P", true, "з", "RU");
        run.Input("P", false, "", "RU");
        Check(run.Feedback == "layout" && run.Status == "Переключи раскладку на EN" &&
              before == (run.Solved, run.Errors, run.Scene.Version, run.Scene.TerminalInput, run.Scene.Active.Cursor, run.Draft().Trace),
            "Wrong native layout leaves text, caret, progress, accuracy and replay intact");
        void Key(string key, string text, string layout = "EN")
        {
            run.Input(key, true, text, layout);
            run.Input(key, false, "", layout);
        }
        // Change only the host input source; do not synthesize a virtual desktop layout gesture.
        Key("P", "p");
        Key("W", "w");
        Key("D", "d");
        var saved = run.Draft();
        var restored = new LabRun("terminal", "sandbox", 0, 0, "RU", [], draft: saved);
        Check(restored.Task.Id == "command/pwd" && restored.Scene.TerminalInput == "pwd" &&
              restored.ScenarioCompletedGoals == run.ScenarioCompletedGoals, "EN text survives draft replay and a new host layout");
        Key("Left", "", "RU");
        Check(run.Scene.TerminalInput == "pwd" && run.Scene.Active.Cursor == 2,
            "Navigation is still available while native RU is active");
        Key("Enter", "", "RU");
        Check(run.Solved == before.Solved + 1 && run.Errors == before.Errors,
            "An actual EN command earns one credit; Enter does not require the US layout");
        var route = new LabRun("terminal", "sandbox", 1, 0, "EN", [], new Random(3), actionLimit: 1,
            missionId: "terminal-access", softFinish: true);
        route.Resume();
        for (int i = 0; !route.Finished && i < route.ScenarioLength; i++)
        {
            Check(route.ScenarioCompletedGoals == i && i < route.ScenarioLength,
                "A standalone route's displayed progress measures effects, not current instructions");
            Complete(route);
            if (i == 0)
            {
                Check(route.AwaitingLimitChoice && route.ScenarioCompletedGoals == 1, "Action cap remains separate from route completion");
                Check(route.ChooseLimit("continue"), "Continue explicit route after reaching action cap");
                route.Resume();
            }
        }
        Check(route.Finished && route.ScenarioCompletedGoals == route.ScenarioLength,
            "Verified final effect fills a complete route");
    }

    private static void TestKeypadNavigation()
    {
        var run = new LabRun("workday", "sandbox", 0, 0, "EN", [], new Random(3), missionId: "workday-report");
        run.Resume();
        for (int i = 0; i < 60 && run.Task.Id != "file-home"; i++) Complete(run);
        Check(run.Task.Id == "file-home", "Workday reaches the exact Alt+Home exercise");
        int prior = run.Solved;
        Chord(run, "Alt+7");
        Chord(run, "Ctrl+Alt+NumPad7");
        Check(run.Solved == prior, "Top-row digit and extra modifiers do not satisfy Alt+Home");
        Chord(run, "Alt+NumPad7");
        Check(run.Solved == prior + 1 && run.Scene.Active?.App == "files",
            "Alt+keypad 7 runs the Home action and credits it exactly once");
        Check(!Gesture.Parse("Ctrl+7").Chords[0].AcceptsNavigationKeypad("NumPad7", ["LCtrl"]),
            "Numeric shortcuts never alias the keypad to the top row");
        foreach (var pair in new[] { ("NumPad7", "Home"), ("NumPad1", "End"), ("NumPad9", "PageUp"),
            ("NumPad3", "PageDown"), ("NumPad8", "Up"), ("NumPad2", "Down"), ("NumPad4", "Left"),
            ("NumPad6", "Right"), ("NumPad0", "Insert"), ("NumDecimal", "Delete") })
            Check(Gesture.Parse("LAlt+" + pair.Item2).Chords[0].AcceptsNavigationKeypad(pair.Item1, ["LAlt"]) &&
                !Gesture.Parse("LAlt+" + pair.Item2).Chords[0].AcceptsNavigationKeypad(pair.Item1, ["RAlt"]),
                pair.Item1 + " honors modifiers and sides");
    }

    private static void TestBrowserSearchClone()
    {
        var run = new LabRun("chrome", "sandbox", 0, 0, "EN", [], new Random(11),
            missionId: "chrome-search-clone", neutralProject: true);
        run.Resume();
        Complete(run);
        Check(run.Scene.Active.Url == LabScene.SearchHome && run.Task.Action == "navigate-pasted", "Course begins on authored Google page");
        int before = run.Solved;
        Type(run, "volcano");
        Chord(run, "Enter");
        Check(run.Solved == before && run.Scene.Entries(run.Scene.Active).Length == 0, "An unrelated query does not reveal the repo or earn credit");
        Chord(run, "Ctrl+L");
        string query = run.Task.Text;
        Complete(run);
        string results = run.Scene.Active.Url;
        Check(LabScene.SearchQuery(results) == query && run.Scene.Entries(run.Scene.Active).Length == 2, "Search returns query-specific candidates");
        Complete(run); Complete(run);
        Check(run.Scene.Active.Url == LabScene.SearchDecoy, "Course opens the similarly named article first");
        Complete(run);
        Check(run.Scene.Active.Url == results && run.Scene.Entries(run.Scene.Active).Length == 2 && run.Scene.Active.Item == 0, "Alt+Left restores the exact previous query, results, and selected link");
        for (int step = 0; step < 12 && run.Task.Id != "bookmark"; step++) Complete(run);
        Check(run.Task.Id == "bookmark", "Only the requested repository leads to the required bookmark step");
        Complete(run);
        Check(run.Scene.Active.SavedBookmarks.Contains(LabScene.RepositoryPage), "Saved URL appears in the bookmark collection");
        for (int step = 0; step < 15 && run.Task.Id != "code-clone-confirm"; step++) Complete(run);
        Check(run.Task.Id == "code-clone-confirm" && run.Scene.Active.Focus == "clone-url" &&
            run.Scene.Active.Input == LabScene.RepositoryPage && run.Scene.Clipboard == LabScene.RepositoryPage,
            "After tab change and bookmark reopen, the exact repo URL transfers through the clipboard into VS Code");
        before = run.Solved;
        Chord(run, "Ctrl+A"); Type(run, "https://github.com/other/typing-demo"); Chord(run, "Enter");
        Check(run.Solved == before && !run.Scene.Terminal.Exists(LabScene.CloneRoot), "An unrelated HTTPS link cannot create or credit a clone");
        Chord(run, "Ctrl+A"); Chord(run, "Ctrl+V");
        for (int step = 0; step < 12 && !run.Finished; step++) Complete(run);
        Check(run.Finished && run.Scene.Active.Url == LabScene.CloneReadme &&
            run.Scene.Terminal.ReadFile(LabScene.CloneReadme) == LabScene.CloneReadmeBody,
            "The full course ends only after opening the README of the actual in-memory copy");
    }

    private static void TestTerminalEssentialsTimedRotation()
    {
        var run = new LabRun("terminal", "sandbox", 0, 180, "EN", [], new Random(31),
            () => 5000, missionId: "terminal-essentials-locate", neutralProject: true);
        run.Resume();
        Check(run.Scene.Terminal.Cwd == "/home/student/project", "Starter starts inside a real project");
        string first = run.MissionId;
        for (int i = 0; i < 20 && run.MissionId == first; i++)
        {
            Check(!run.Task.Text.Contains(' '), "Starter does not require multiword commands");
            bool home = run.Task.Text == "cd";
            Complete(run);
            if (home) Check(run.Scene.Terminal.Cwd == "/home/student", "cd demonstrably changes directory");
        }
        Check(run.MissionId != first && run.MissionId.StartsWith("terminal-essentials-") &&
            run.Scene.Terminal.Cwd == "/home/student/project", "Timed starter rotates without falling back to complex commands");
    }

    private static void TestOtherShortcutContexts()
    {
        static void Expect(string app, string gesture, string expectedId, Action<LabScene> setup, Func<LabScene, bool> outcome)
        {
            var scene = new LabScene(app, "EN");
            setup(scene);
            var chord = Gesture.Parse(gesture).Chords[0];
            var held = chord.Modifiers.Select(x => x switch {
                "Ctrl" => "RCtrl", "Alt" => "LAlt", "Shift" => "LShift", "Super" => "LSuper", _ => x
            }).Append(chord.Trigger);
            var actual = LabCatalog.Resolve(chord.Trigger, held, scene);
            Check(actual != null && actual.Id == expectedId && scene.Apply(actual.Action) && outcome(scene),
                app + " " + gesture + " must perform " + expectedId);
        }
        Expect("terminal", "Alt+B", "terminal-word-left", s => {s.TerminalInput="cd project"; s.Active.Cursor=10;}, s => s.Active.Cursor==3 && s.TerminalInput=="cd project");
        Expect("terminal", "Alt+F", "terminal-word-right", s => {s.TerminalInput="cd project"; s.Active.Cursor=3;}, s => s.Active.Cursor==10 && s.TerminalInput=="cd project");
        Expect("ubuntu", "Alt+Shift+Tab", "switch-prev", s => {s.Open("chrome"); s.Open("code");}, s => {string before=s.ActiveWindow; s.CommitSwitch(); return !s.Switcher && s.ActiveWindow!=before;});
        Expect("chrome", "Ctrl+-", "zoom-out", s => s.Active.Zoom=100, s => s.Active.Zoom==90);
        Expect("code", "Ctrl+2", "focus-second", s => {s.Apply("new-tab"); s.Apply("split"); s.Apply("focus-editor");}, s => s.Active.Split && s.Active.Group==1);
        Expect("code", "Ctrl+G", "goto-line", s => {}, s => s.Active.Focus=="goto-line");
        Expect("chrome", "Ctrl+G", "find-next", s => {s.Active.Tab.Body="draft text draft"; s.Active.Find="draft"; s.Active.Select(0,5);}, s => s.Active.Start==11 && s.Active.End==16);
        Expect("terminal", "Ctrl+K", "terminal-kill", s => {s.TerminalInput="echo draft"; s.Active.Cursor=5;}, s => s.TerminalInput=="echo " && s.Active.Cursor==5);
        Expect("terminal", "Ctrl+R", "terminal-search", s => {s.TerminalInput="pwd"; s.Text("Enter",""); s.TerminalInput="draft"; s.Active.Cursor=5;}, s => s.Active.Focus=="reverse-search" && s.TerminalInput=="pwd");
        Expect("chrome", "Ctrl+Shift+B", "bookmarks", s => s.Active.Bookmarks=false, s => s.Active.Bookmarks);
        Expect("terminal", "Ctrl+Shift+C", "terminal-copy", s => {s.TerminalInput="echo draft"; s.Active.Select(5,10);}, s => s.Clipboard=="draft" && s.TerminalInput=="echo draft");
        Expect("code", "Ctrl+Shift+P", "palette", s => {}, s => s.Active.Focus=="palette");
    }

    private static void Main()
    {
        TestTerminalEssentialsTimedRotation();
        TestOtherShortcutContexts();
        TestKeypadNavigation();
        TestBrowserSearchClone();
        TestCommandReviewWeighting();
        TestNativeEnglishTextAndRouteProgress();
        foreach (string app in LabCatalog.Apps)
        foreach (int level in new[] { 0, 1, 2 })
        {
            var courses = LiveCourse.Missions.Where(m => m.App == app && m.Level == level).ToList();
            int expectedCourses = app == "windows" ? 0 : app == "workday" ? (level == 0 ? 2 : 1) : 4;
            Check(courses.Count(course => !course.Curated) == expectedCourses, "Course count: " + app);
            if (app == "windows") Check(courses.Count == 2 && courses.All(course => course.Curated), "Windows curated route: " + level);
            foreach (var course in courses)
            foreach (int variant in Enumerable.Range(0, 3))
            foreach (string layout in new[] { "RU", "EN" })
            {
                var initial = new LabScene(app == "workday" ? "ubuntu" : app, layout);
                initial.PrepareCase(variant, app);
                var route = LiveCourse.Compile(course, initial);
                Check(route.Count >= (course.Curated ? 1 : 20), course.Id + ": minimum route length");
                if (!course.Curated)
                {
                    Check(route.Select(step => step.Task.Id).Distinct().Count() >= 8, course.Id + ": distinct actions");
                    Check(route.Select(step => step.Phase).Distinct().Count() >= 3, course.Id + ": meaningful phases");
                }
                else Check(course.FinishAfterRoute, course.Id + ": standalone ends at its last result");
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
        {
            double budgetNow = 0;
            LabRun OpenBudget(int seconds, int actions, LiveDraft draft = null, string mode = "sandbox") =>
                new LabRun("terminal", mode, 1, seconds, "EN", [], new Random(7), () => budgetNow,
                    actionLimit: actions, missionId: "terminal-access", draft: draft);
            LiveDraft RoundTrip(LiveDraft draft)
            {
                var xml = new System.Xml.Serialization.XmlSerializer(typeof(LiveDraft));
                using var bytes = new System.IO.MemoryStream();
                xml.Serialize(bytes, draft); bytes.Position = 0;
                return (LiveDraft)xml.Deserialize(bytes);
            }
            var budgetTimed = OpenBudget(180, 0);
            budgetTimed.Resume(); budgetNow = 10500;
            Type(budgetTimed, "bad-command"); Chord(budgetTimed, "Enter"); Complete(budgetTimed);
            budgetTimed.Pause();
            var checkpoint = RoundTrip(budgetTimed.Draft());
            Check(checkpoint.Progress.ActiveMilliseconds == 10500 && budgetTimed.Solved == 1 && budgetTimed.Errors == 1,
                "Timed draft atomically describes elapsed fractional time with both counters");
            budgetNow = 600000;
            var timedReopened = OpenBudget(0, 100, checkpoint, mode: "drill");
            Check(timedReopened.Paused && timedReopened.Mode == budgetTimed.Mode && timedReopened.Solved == 1 &&
                timedReopened.Errors == 1 && timedReopened.Seconds == 10 && timedReopened.Remaining == 170 &&
                timedReopened.ActionLimit == 0 && timedReopened.Session == budgetTimed.Session &&
                timedReopened.ScenarioStep == budgetTimed.ScenarioStep && timedReopened.Attempts.Count == 0,
                "Saved timed attempt outranks current picker and replay emits no credits");
            timedReopened.Resume(); budgetNow += 500;
            Check(timedReopened.Seconds == 11 && timedReopened.Remaining == 169,
                "A fractional second survives restart and only the active clock advances");
            timedReopened.Pause(); budgetNow += 700000;
            Check(timedReopened.Seconds == 11 && timedReopened.Remaining == 169,
                "Time spent paused is not counted");
            var timeline = JsonSerializer.SerializeToElement(timedReopened.Result([]), Program.Json).GetProperty("series");
            Check(timeline.GetArrayLength() == 2 && timeline[0].GetProperty("second").GetInt32() == 10,
                "Result timeline survives without replaying its timestamps");
            var legacyBudget = RoundTrip(checkpoint);
            legacyBudget.Progress = null;
            var legacyReopened = OpenBudget(0, 20, legacyBudget);
            Check(legacyReopened.ScenarioStep == budgetTimed.ScenarioStep && legacyReopened.Solved == 0 &&
                legacyReopened.Seconds == 0 && legacyReopened.ActionLimit == 20 && legacyReopened.Session != budgetTimed.Session,
                "Legacy draft keeps old semantics when duration and session are unknown");
            var actionLimited = OpenBudget(180, 2);
            actionLimited.Resume(); Complete(actionLimited);
            var limitedReopened = OpenBudget(600, 0, RoundTrip(actionLimited.Draft()));
            Check(limitedReopened.ActionLimit == 2 && limitedReopened.TimeLimit == 0 && limitedReopened.Solved == 1,
                "Saved action limit retains precedence over the incoming time limit");
            limitedReopened.Resume(); Complete(limitedReopened);
            Check(limitedReopened.Finished && !limitedReopened.HasDraft && limitedReopened.SessionRecord().Completed,
                "Only remaining actions are available and final result retires the draft");
            var budgetUnlimited = OpenBudget(0, 0);
            budgetUnlimited.Resume(); Complete(budgetUnlimited);
            var unlimitedReopened = OpenBudget(180, 0, RoundTrip(budgetUnlimited.Draft()));
            Check(unlimitedReopened.TimeLimit == 0 && unlimitedReopened.ActionLimit == 0,
                "Stored unlimited is distinct from an unknown legacy limit");
        }
        {
            double softNow = 0;
            LabRun OpenSoft(int seconds, int actions, LiveDraft saved = null) =>
                new LabRun("terminal", "sandbox", 1, seconds, "EN", [], new Random(7), () => softNow,
                    actionLimit: actions, missionId: "terminal-access", draft: saved, softFinish: true);
            var soft = OpenSoft(10, 0);
            soft.Resume(); Type(soft, "cd /home/student/");
            softNow = 15800;
            soft.HoldForLimit();
            Check(soft.AwaitingLimitChoice && soft.Paused && !soft.Finished && soft.HasDraft,
                "Timer cap holds the open mission instead of making a result");
            Check(soft.Scene.TerminalInput == "cd /home/student/" && soft.Remaining == 0,
                "The unfinished command and timer boundary remain available");
            soft.Resume(); Type(soft, "ignored");
            Check(soft.Scene.TerminalInput == "cd /home/student/" && soft.Paused,
                "Normal resume or keyboard input cannot bypass choice");
            softNow += 500000;
            var softRestored = OpenSoft(123, 20, soft.Draft());
            Check(softRestored.Session == soft.Session && softRestored.AwaitingLimitChoice &&
                softRestored.Scene.TerminalInput == soft.Scene.TerminalInput,
                "Restored cap decision preserves attempt, scene, and current keyboard input");
            Check(softRestored.ChooseLimit("continue") && softRestored.FinishingMission &&
                softRestored.Paused && !softRestored.AwaitingLimitChoice, "Continuation is explicit and waits for input capture");
            softRestored.Resume(); softNow += 1200;
            Type(softRestored, "project"); Chord(softRestored, "Enter");
            Check(softRestored.Solved == 1 && softRestored.Errors == 0 && softRestored.Seconds == 11 &&
                !softRestored.Finished, "Only active post-choice time and fresh action are credited");
            var actionSoft = OpenSoft(0, 1);
            string currentMission = actionSoft.MissionId;
            actionSoft.Resume(); Complete(actionSoft); actionSoft.HoldForLimit();
            Check(actionSoft.AwaitingLimitChoice && actionSoft.Solved == 1 && actionSoft.HasDraft,
                "The last in-budget action pauses at the same mission");
            var finishNow = OpenSoft(0, 0, actionSoft.Draft());
            Check(finishNow.ChooseLimit("finish") && finishNow.Finished && finishNow.Session == actionSoft.Session &&
                finishNow.Solved == 1 && finishNow.SessionRecord().Completed && !finishNow.HasDraft &&
                finishNow.ChooseLimit("finish"), "An accepted but unsaved finish can be retried");
            finishNow.MarkSaved();
            Check(!finishNow.ChooseLimit("finish"), "An already saved finish will never write again");
            softNow = 0;
            var stopRacingLimit = OpenSoft(10, 0);
            stopRacingLimit.Resume(); Type(stopRacingLimit, "cd ");
            softNow = 9999;
            Check(!stopRacingLimit.AwaitingLimitChoice, "Last UI snapshot can be below the timer limit");
            softNow = 10001;
            Check(stopRacingLimit.ChooseLimit("finish") && stopRacingLimit.Finished &&
                stopRacingLimit.SessionRecord().Completed && !stopRacingLimit.HasDraft &&
                stopRacingLimit.Scene.TerminalInput == "cd ", "Explicit Stop at a cap wins without an earlier engine poll");
            var stopBeforeLimit = OpenSoft(10, 0);
            stopBeforeLimit.Resume(); Type(stopBeforeLimit, "cd ");
            Check(stopBeforeLimit.ChooseLimit("finish") && stopBeforeLimit.Finished &&
                !stopBeforeLimit.SessionRecord().Completed && stopBeforeLimit.HasDraft,
                "Explicit Stop before an unexpired cap remains an early, resumable result");
            Check(actionSoft.ChooseLimit("continue"), "Action cap can explicitly extend the current mission");
            var continuation = OpenSoft(0, 0, actionSoft.Draft());
            Check(continuation.FinishingMission && !continuation.AwaitingLimitChoice && continuation.Session == actionSoft.Session,
                "Continuation choice survives reopening");
            continuation.Resume();
            for (int step = 0; step < continuation.ScenarioLength && !continuation.Finished; step++)
                Complete(continuation);
            Check(continuation.Finished && continuation.MissionId == currentMission && continuation.Solved == continuation.ScenarioLength &&
                continuation.SessionRecord().Completed && !continuation.HasDraft, "Continued mission completes without rotation or residual checkpoint");
            var capAtBoundary = OpenSoft(0, continuation.ScenarioLength);
            capAtBoundary.Resume();
            for (int step = 0; step < capAtBoundary.ScenarioLength && !capAtBoundary.Finished; step++)
                Complete(capAtBoundary);
            Check(capAtBoundary.Finished && capAtBoundary.MissionId == currentMission && !capAtBoundary.AwaitingLimitChoice,
                "No continuation is offered if the limit coincides with the mission's final action");
        }
        {
            double nowCombo = 0;
            LabRun OpenCombo(int seconds, int actions, bool combined, LiveDraft saved = null, Func<double> clock = null) =>
                new LabRun("terminal", "sandbox", 1, seconds, "EN", [], new Random(7), clock ?? (() => nowCombo),
                    actionLimit: actions, missionId: "terminal-access", draft: saved, softFinish: true, combinedLimits: combined);
            var timerWins = OpenCombo(10, 1, true);
            timerWins.Resume(); Type(timerWins, timerWins.Task.Text);
            nowCombo = 10000;
            Chord(timerWins, "Enter");
            timerWins.HoldForLimit();
            Check(timerWins.LimitReached == "timer" && timerWins.Solved == 0 &&
                timerWins.Scene.TerminalInput == timerWins.Task.Text && timerWins.AwaitingLimitChoice,
                "An Enter observed at the exact deadline cannot consume the action limit");
            var timerRestored = OpenCombo(100, 20, false, timerWins.Draft());
            Check(timerRestored.TimeLimit == 10 && timerRestored.ActionLimit == 1 && timerRestored.LimitReached == "timer" &&
                timerRestored.Session == timerWins.Session, "Saved pair and timer winner outrank both new picker choices and opt-in flag");
            Check(timerRestored.ChooseLimit("continue"), "Timer-won attempt can continue its current scene");
            timerRestored.Resume(); nowCombo += 2000; Chord(timerRestored, "Enter");
            Check(timerRestored.Solved == 1 && timerRestored.Seconds == 12 && timerRestored.LimitReached == "timer",
                "Extending past both thresholds does not replace the timer winner with actions");

            nowCombo = 0;
            int reads = 0;
            bool processing = false;
            var actionWins = OpenCombo(10, 1, true, clock: () => processing && ++reads > 2 ? 11001 : nowCombo);
            actionWins.Resume(); Type(actionWins, actionWins.Task.Text);
            nowCombo = 9999; processing = true;
            Chord(actionWins, "Enter");
            actionWins.HoldForLimit();
            Check(actionWins.LimitReached == "actions" && actionWins.Solved == 1 && actionWins.AwaitingLimitChoice &&
                actionWins.Seconds == 9 && actionWins.Remaining == 1,
                "Admitted Enter before the deadline wins on actions even if processing crosses the deadline");
            var actionRestored = OpenCombo(0, 0, true, actionWins.Draft());
            Check(actionRestored.ActionLimit == 1 && actionRestored.TimeLimit == 10 && actionRestored.LimitReached == "actions" &&
                actionRestored.Session == actionWins.Session, "The action winner and original pair survive a new process");
            Check(actionRestored.ChooseLimit("continue"), "Action-won attempt can continue");
            actionRestored.Resume(); nowCombo += 5000;
            Check(actionRestored.Seconds == 14 && actionRestored.Remaining == 0 && actionRestored.LimitReached == "actions" &&
                OpenCombo(600, 40, true, actionRestored.Draft()).LimitReached == "actions",
                "Crossing the timer after action extension never changes the original cap notification");

            var oldCaller = OpenCombo(180, 20, false);
            Check(oldCaller.TimeLimit == 0 && oldCaller.ActionLimit == 20 && oldCaller.LimitReached == null,
                "Unopted action cap still disables the legacy/default timer");
            var profile = new StudioProfile();
            var timerRecord = new LabSessionRecord { Session = "combo-timer", App = "terminal", Mode = "sandbox", Level = 1,
                TimeLimit = 180, ActionLimit = 20, Seconds = 180, Solved = 9, Completed = true };
            var actionRecord = new LabSessionRecord { Session = "combo-action", App = "terminal", Mode = "sandbox", Level = 1,
                TimeLimit = 180, ActionLimit = 40, Seconds = 90, Solved = 40, Completed = true };
            var oldTimerRecord = new LabSessionRecord { Session = "legacy-timer", App = "terminal", Mode = "sandbox", Level = 1,
                TimeLimit = 180, ActionLimit = 0, Seconds = 180, Solved = 12, Completed = true };
            var oldActionRecord = new LabSessionRecord { Session = "legacy-action", App = "terminal", Mode = "sandbox", Level = 1,
                TimeLimit = 0, ActionLimit = 20, Seconds = 45, Solved = 20, Completed = true };
            foreach (var budgetRecord in new[] { timerRecord, actionRecord, oldTimerRecord, oldActionRecord })
                LabSessionRecords.Update(profile, budgetRecord);
            Check(profile.LabRecords.Count == 4, "Either combined winner is eligible, and both single-cap groups remain distinct");
            var resultTimerCombo = JsonSerializer.SerializeToElement(OpenCombo(180, 20, true)
                .Result(profile.LabSessions, profile.LabRecords), Program.Json);
            Check(resultTimerCombo.GetProperty("previousRecord").GetInt32() == 9 &&
                resultTimerCombo.GetProperty("history").GetArrayLength() == 1, "Comparable history still uses the exact pair");
        }
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
