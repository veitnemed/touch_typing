using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace TouchTypingStudio
{
    internal static class TrainingTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 15, 10, 0, 0);
        private static int assertions;
        private static void Check(bool condition, string name)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(name);
        }

        [STAThread]
        private static void Main()
        {
            AtlasAndCampaign();
            MotorAndDraft();
            StoreCompatibility();
            ExerciseModes();
            RandomPythonDrills();
            PracticeRecordThreshold();
            AdaptivePythonCourse();
            EndlessTypingModes();
            EndlessTypingSummary();
            DailyAndRecords();
            Challenge();
#if !LINUX
            ConsoleMissionTests.Run();
            DesktopSwitchTests.Run();
            WorkdayAddresses();
            WorkdayNavigationTests.Run();
            WorkdayEditorTests.Run();
            DesktopItineraryTests.Run();
            ChromeMissionTests.Run();
#endif
            Console.WriteLine("PASS: " + assertions + " checks across campaign, routes, input, storage, analytics, Python drills and 48 hours.");
        }

#if !LINUX
        private static void WorkdayAddresses()
        {
            const string path = @"C:\Users\Имя с пробелом\folder\отчёт #1.txt";
            Check(DesktopStrip.ShortAddress(path) == "отчёт #1.txt", "Show filename for a long Windows path");
            Check(DesktopStrip.ShortAddress(new Uri(path).AbsoluteUri) == "отчёт #1.txt", "Show filename for a pasted Chrome file URL");
            Check(DesktopStrip.ShortAddress("Find text\r\nSave changes") == null, "Keep multiline practice text visible");
            Check(DesktopStrip.ShortAddress("Status: reviewed") == null, "Keep plain instructions as text");
        }

#endif

        private static void PracticeRecordThreshold()
        {
            var mistakes = new KeyValuePair<char, long>[0];
            Check(!PracticeRecords.CanRecordTyping(new ContinuousTypingSnapshot(179, 0, 10, 100, mistakes)),
                "A fast short attempt cannot claim a typing record");
            Check(PracticeRecords.CanRecordTyping(new ContinuousTypingSnapshot(180, 0, 60, 100, mistakes)),
                "A typing attempt counts from 180 characters");
            var profile = new StudioProfile();
            PracticeRecords.Record(profile, "typing/Python", 166, 95);
            PracticeRecords.Record(profile, "typing/Python", 123, 99);
            PracticeRecords.Record(profile, "set/custom", 220, 88);
            var python = profile.PracticeRecords.Find(record => record.Id == "typing/Python");
            Check(python.Speed == 166 && python.Accuracy == 99 && profile.PracticeRecords.Count == 2,
                "Speed and accuracy records are independent per mode and custom set");
        }

        private static void AtlasAndCampaign()
        {
            var p = new StudioProfile();
            var lessons = Atlas.Nodes.Where(n => n.Lesson != null).Select(n => n.Lesson).ToArray();
            Check(lessons.Distinct().Count() == Lessons.All.Length && lessons.Length == Lessons.All.Length, "One node per existing lesson");
            var routes = Atlas.Nodes.Where(n => n.Route != null).Select(n => n.Route).ToArray();
            Check(routes.Distinct().Count() == RouteCatalog.Routes.Length && routes.Length == RouteCatalog.Routes.Length, "One node per existing route");
            foreach (var territory in Atlas.Regions)
            {
                var nodes = Atlas.Nodes.Where(n => n.Region == territory).ToArray();
                Check(nodes.All(n => Atlas.State(n, p) == WaypointState.Open), territory.Id + " freely available on a blank profile");
                Check(nodes.Count(n => n.Kind == WaypointKind.Branch) == 1 &&
                    nodes.Count(n => n.Kind == WaypointKind.Exam) == 1, territory.Id + " branch/exam");
            }
            var en = Atlas.Find("python");
            var a = Atlas.Next(p, en).Lesson;
            Check(TrainingProgress.Variant(a, p) == 0 && a.Id == "assign", "Python starts with plain assignments");
            var later = Lessons.Find("list-index");
            Check(Atlas.NextAfter(later, p).Lesson.Id == "call" && Atlas.Next(p, en).Lesson == a,
                "Next after a freely selected level stays forward without changing the recommendation");
            Check(Atlas.NextAfter(Lessons.Find("comprehension"), p) == null, "No wraparound at the last regular level");
            TrainingProgress.OnLesson(p, a, 0, false, Now);
            Check(a.GoodRounds(p) == 0, "Failed run gets no credit");
            for (int i = 0; i < 5; i++) TrainingProgress.OnLesson(p, a, 0, true, Now);
            Check(TrainingProgress.Guided(a, p) == 1 && !TrainingProgress.Cleared(a, p), "No progress from repeating same variant");
            TrainingProgress.OnLesson(p, a, 1, true, Now);
            int solo = TrainingProgress.Variant(a, p);
            Check(a.Recall(solo) && solo < a.Rounds.Length - 1, "Skip to independent check after guided passes");
            TrainingProgress.OnLesson(p, a, solo, true, Now);
            Check(TrainingProgress.Cleared(a, p) && Atlas.Next(p, en).Lesson.Id == "print", "Open next node");
            Check(TrainingProgress.Xp(p) == 45 && p.RecallDates.Count == 1, "One milestone, one due date");
            TrainingProgress.OnLesson(p, a, 5, true, Now);
            Check(TrainingProgress.Xp(p) == 45 && p.RecallDates[0].Due == Now.Date.AddDays(1), "Optional variants do not farm XP");
            p.Training.GuidedRounds = 4;
            Check(TrainingProgress.Cleared(a, p), "Tuning does not revoke milestones");
            var windows = Atlas.Find("windows");
            var boss = Atlas.Nodes.Single(n => n.Region == windows && n.Kind == WaypointKind.Exam);
            Check(boss.Previous.Route != null, "Boss anchored to existing route tree");
            Check(Atlas.State(boss, p) == WaypointState.Open && Atlas.ExamRoute(windows, p).Kind == windows.Trial[0],
                "Exam is available before the preceding routes and begins at the first checkpoint");
            Atlas.PassRouteExam(p, windows, windows.Routes.First(r => r.Kind == RouteKind.NavigateWords), Now);
            Check(p.Exams.Count == 0, "Next verified step only");
            foreach (var kind in windows.Trial) Atlas.PassRouteExam(p, windows, windows.Routes.First(r => r.Kind == kind), Now);
            Check(Atlas.State(boss, p) == WaypointState.Complete, "All real route checkpoints required");
        }

        private static void MotorAndDraft()
        {
            string code = "f\n    jk⌫j";
            var a = new TypingAttempt(code, null);
            a.Start();
            Check(!a.Enter('g', "EN") && a.Position == 0, "Reject incorrect key");
            Check(a.Enter('f', "EN") && a.Enter('\n', "EN"), "Plain key and Enter");
            Check(a.Enter('\t', "EN") && a.Position == 6, "Tab only expands indentation");
            Check(!a.Enter('\t', "EN") && a.Errors == 2, "Reject Tab in code");
            Check(a.Enter('j', "EN") && a.Enter('k', "EN") && a.Enter('\b', "EN") && a.Written.EndsWith("j"), "Real Backspace removes text");
            a.Pause();
            var draft = a.Draft("x", 0);
            var restored = new TypingAttempt(code, draft);
            Check(restored.Position == a.Position && restored.Written == a.Written && restored.Errors == 2, "Restore repair draft and errors");
            restored.Start();
            Check(restored.Enter('j', "EN") && restored.Finished && !restored.Running, "Resumed completion");
            Check(restored.StrokeAt(9).IntervalMs == 0, "First stroke after pause has no interval");
            Check(restored.Trace().Count == code.Length && restored.Trace().SequenceEqual(restored.Trace().OrderBy(x => x)), "Ordered timeline including Tab");
            Check(restored.StrokeAt(3).IntervalMs == 0, "No synthetic intervals between Tab spaces");
            var p = new StudioProfile();
            TrainingProgress.SaveMotor(p, restored, false, Now, "sample");
            var f = TrainingProgress.Evidence(p, "EN", Now).Single(k => k.Sequence == "f");
            Check(f.Errors == 1 && f.Correct == 1 && f.Independent == 0, "Measure expected symbol on error");
            Check(TrainingProgress.Evidence(p, "EN", Now, true).Any(x => x.Sequence == "jk"), "Pair statistics retained");
            var m = new TypingAttempt("aaaааа", null);
            m.Start();
            foreach (char c in m.Target) m.Enter(c, c == 'a' ? "EN" : "RU");
            TrainingProgress.SaveMotor(p, m, true, Now, "target");
            TrainingProgress.SaveMotor(p, m, true, Now.AddHours(1), "same-day");
            Check(p.StableKeys.Count == 0, "Same-day practice not delayed retention");
            TrainingProgress.SaveMotor(p, m, true, Now.AddDays(1), "next-day");
            Check(p.StableKeys.Contains("RU/а") && p.StableKeys.Contains("EN/a"), "Recognize separate scripts and later recall");
            Check(TrainingProgress.MasteryPercent(p, Layout.Russian, false, Now.AddDays(75)) > 0, "Skills survive gaps");
            Check(!p.Motor.Any(k => k.Sequence == "aа"), "No pair latency across layouts");
        }

        private static void StoreCompatibility()
        {
            var exact = Lessons.Find("assign");
            var xml = "<StudioProfile><Configured>true</Configured><CustomText>f = 6</CustomText><SelectedLesson>assign</SelectedLesson>" +
                "<TypingCourse>5</TypingCourse><GoodPasses><string>" + exact.PassId(0) + "</string></GoodPasses>" +
                "<CompletedRoutes><string>Switch/2</string></CompletedRoutes><Results><TypingResult><Lesson>legacy</Lesson><Count>20</Count></TypingResult></Results>" +
                "<KeyHistory><KeyMistakes><Layout>RU</Layout><Symbol>1074</Symbol><Attempts>23</Attempts><Errors>5</Errors></KeyMistakes></KeyHistory></StudioProfile>";
            var serializer = new XmlSerializer(typeof(StudioProfile));
            StudioProfile p;
            using (var old = new MemoryStream(Encoding.UTF8.GetBytes(xml))) p = (StudioProfile)serializer.Deserialize(old);
            TrainingProgress.Migrate(p, Now);
            Check(p.Configured && p.CustomText == "f = 6" && exact.GoodRounds(p) == 1 && p.CompletedRoutes.Contains("Switch/2") &&
                p.Results.Single().Completed, "Legacy XML kept");
            Check(p.Training.Goal == 95 && TrainingProgress.Weak(p, "RU", Now).Single().Sequence == "в", "Settings default; legacy weakness still used");
            TrainingProgress.Earn(p, "exam/test", 110, Now);
            p.Theme = "aurora";
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, p);
                stream.Position = 0;
                var newProfile = (StudioProfile)serializer.Deserialize(stream);
                Check(newProfile.Theme == "aurora" && newProfile.Awards.Single().Xp == 110 &&
                    newProfile.ProgressionVersion == 1, "Modern XML round trip");
            }
        }

        private static void ExerciseModes()
        {
            var p = new StudioProfile();
            var en = Atlas.Find("qwerty");
            var ru = Atlas.Find("ru");
            Check(ExerciseBank.Create(ExerciseKind.Weak, p, en, Now) == null, "Do not fake weaknesses on a blank profile");
            p.KeyHistory.Add(new KeyMistakes { Layout = "RU", Symbol = 'в', Attempts = 40, Errors = 15 });
            var weak = ExerciseBank.Create(ExerciseKind.Weak, p, ru, Now);
            Check(weak.Text.Count(c => c == 'в') >= 8 && weak.Text.All(c => CodeText.Language(c) != Layout.English), "Real weaknesses drive focused repetition");
            for (int i = 0; i < 2; i++) p.Results.Add(new TypingResult { Date = Now, Region = "ru", Count = 20, Errors = 25 });
            Check(ExerciseBank.Recommend(p, ru, Now).Mode == ExerciseKind.Weak &&
                ExerciseBank.Create(ExerciseKind.Weak, p, ru, Now).Text.Length < weak.Text.Length, "Scale back after repeated mistakes");
            var blind = ExerciseBank.Create(ExerciseKind.Blind, p, en, Now);
            var memory = ExerciseBank.Create(ExerciseKind.Memory, p, en, Now);
            Check(blind.Independent && memory.Independent && memory.Text.Length == 40 && blind.Text.Length > memory.Text.Length, "Distinct blind and retrieval formats");
            var mix = ExerciseBank.Create(ExerciseKind.Mixed, p, en, Now);
            Check(mix.Text.Any(c => CodeText.Language(c) == Layout.English) && mix.Text.Any(c => CodeText.Language(c) == Layout.Russian), "Actual interleaving");
            var survival = ExerciseBank.Create(ExerciseKind.Survival, p, en, Now);
            var fail = new TypingAttempt(survival.Text, null);
            fail.Start();
            for (int i = 1; i < survival.ErrorLimit; i++) fail.Enter('ё', "EN");
            Check(!survival.Stops(fail), "Budget not exhausted prematurely");
            fail.Enter('ё', "EN");
            Check(survival.Stops(fail) && fail.Position == 0, "Budget stops in place");
            var lab = ExerciseBank.Create(ExerciseKind.Python, p, ru, Now);
            Check(lab.Layout == Layout.English && lab.Text.Contains(" = "), "Lab starts with valid programming constructions");
            var transfer = ExerciseBank.Transfer(p, true);
            Check(CodeText.LanguageOfText(transfer.Text) == "RU" && transfer.Text.Length > 50, "Authentic transfer in Russian");
            int count = 0;
            foreach (var region in Atlas.Regions.Where(r => r.Trial.Length == 0))
                for (int i = 0; i < 3; i++)
                {
                    var boss = ExerciseBank.Create(ExerciseKind.Boss, p, region, Now, i);
                    Check(boss.Independent && boss.Text.Length >= 90 && boss.Text.All(c => TypingKeyboard.Supports(c) || c == CodeText.Erase), "Boss text typable " + region.Id);
                    count++;
                }
            Check(count == 12, "Four typing bosses with three new variants each");
        }

        private static void DailyAndRecords()
        {
            var p = new StudioProfile();
            var l = Lessons.Find("assign");
            Lessons.ScheduleRecall(p, l, Now, false, true);
            Check(ExerciseBank.Create(ExerciseKind.Daily, p, Atlas.Find("python"), Now) == null, "No premature review");
            var day2 = Now.AddDays(1);
            var daily = ExerciseBank.Create(ExerciseKind.Daily, p, Atlas.Find("python"), day2);
            Check(daily.Recall == l && daily.Independent && daily.Text != l.Rounds[0], "Independent delayed variant");
            TrainingProgress.OnRecall(p, l, true, day2);
            TrainingProgress.OnRecall(p, l, true, day2.AddHours(1));
            Check(p.RecallDates.Single().Checks == 1 && p.RecallDates.Single().Due == day2.Date.AddDays(3) &&
                TrainingProgress.Xp(p) == 20, "Successful retention increases interval once");
            TrainingProgress.OnRecall(p, l, false, day2.AddDays(3));
            Check(p.RecallDates.Single().Due == day2.Date.AddDays(4) && p.RecallDates.Single().Checks == 1, "Failed retention brings back practice");
            var ghost = ExerciseBank.Create(ExerciseKind.Ghost, p, Atlas.Find("qwerty"), Now);
            var attempt = new TypingAttempt(ghost.Text, null);
            attempt.Start();
            foreach (char c in ghost.Text) attempt.Enter(c, "EN");
            var result = new TypingResult { Activity = "Ghost", Task = ghost.Task, Count = ghost.Text.Length, Seconds = 5,
                Date = Now, Independent = true };
            Check(!TrainingProgress.SaveRecord(p, result, attempt, true), "First ghost is a baseline");
            ghost = ExerciseBank.Create(ExerciseKind.Ghost, p, Atlas.Find("qwerty"), Now);
            Check(ghost.Ghost != null && ghost.GhostPosition(5) == ghost.Text.Length, "Ghost plays accepted timestamps");
            result.Seconds = 4;
            result.Errors = 1;
            Check(!TrainingProgress.SaveRecord(p, result, attempt, true) && p.Records.Count == 1, "Faster but worse is not new best");
            result.Errors = 0;
            Check(TrainingProgress.SaveRecord(p, result, attempt, true) && p.Records.Count == 2, "Same text/accuracy with faster pace improves best");
        }

        private static void RandomPythonDrills()
        {
            var random = new Random(906);
            foreach (var level in PythonDrills.WordLevels)
            {
                var samples = Enumerable.Range(0, 12).Select(i => level.Create(random)).ToArray();
                Check(samples.Distinct().Count() == samples.Length, level.Id + " changes between starts");
                Check(samples.All(s => s.Length >= 150 && s.All(c => c == ' ' || c >= 'a' && c <= 'z') &&
                    !s.StartsWith(" ") && !s.EndsWith(" ") && !s.Contains("  ")), level.Id + " is lowercase English with natural spaces");
            }
            var words = PythonDrills.WordLevels[0].Create(random).Split(' ');
            Check(new[] { "print", "len", "dict" }.All(w => words.Count(c => c == w) >= 2), "Frequently used words repeat in the first level");

            foreach (var level in PythonDrills.CodeLevels)
            {
                var samples = Enumerable.Range(0, 12).Select(i => level.Create(random)).ToArray();
                Check(samples.Distinct().Count() == samples.Length, level.Id + " changes between starts");
                Check(samples.All(s => s.Length >= 100 && s.Any(c => c >= '0' && c <= '9') &&
                    s.Any(c => "([{=:".Contains(c)) && s.Contains('\n') &&
                    s.All(c => c == CodeText.Erase || TypingKeyboard.Supports(c))), level.Id + " contains printable Python, digits and symbols");
                Check(samples.All(s => s.Contains(CodeText.Erase) == level.HasRepairs), level.Id + " exposes the expected Backspace actions");
                if (level.HasRepairs) Check(samples.All(ReplayEditedPython), level.Id + " replays all real Backspace actions");
            }
            string repairs = PythonDrills.CodeLevels.First(l => l.HasRepairs).Create(random);
            Check(repairs.Contains("prim⌫ntt⌫("), "Backspace deletes a slipped letter and a duplicated letter in print");
#if !LINUX
            Check(PracticeRibbon.VisibleText("len (\n  prim⌫nt)") == "len (↵  prim⌫nt)", "Practice ribbon preserves spaces and Backspace");
#endif
        }

        private static bool ReplayEditedPython(string script)
        {
            var attempt = new TypingAttempt(script, null);
            var corrected = new StringBuilder();
            attempt.Start();
            foreach (char symbol in script)
            {
                if (symbol == CodeText.Erase)
                {
                    if (corrected.Length == 0 || corrected[corrected.Length - 1] == '\n' || attempt.Enter(CodeText.Erase, "EN"))
                        return false;
                    corrected.Length--;
                    if (!attempt.Enter('\b', "EN") || attempt.Written.Length != corrected.Length) return false;
                }
                else
                {
                    corrected.Append(symbol);
                    if (!attempt.Enter(symbol, "EN")) return false;
                }
            }
            return attempt.Finished && attempt.Errors == 0 && attempt.Written == corrected.ToString();
        }

        private static void AdaptivePythonCourse()
        {
            var course = new AdaptivePython(new Random(76), 0, 95);
            var first = course.Attempt;
            first.Start();
            int punctuation = first.Target.IndexOfAny(new[] { '(', '=', '\'', '+', ',' });
            Check(punctuation >= 0, "Python exposes special characters at the first stage");
            while (first.Position < punctuation) course.Enter(first.Expected);
            Check(!course.Enter('@') && first.Position == punctuation && first.Running && course.Weakest != null,
                "Misses update focus without stopping or advancing the input");
            string weak = course.Weakest;
            FinishPythonFragment(course);
            Check(course.Attempt != first && course.Attempt.Running && course.Prefix.EndsWith(first.Written) &&
                course.Focus == weak && course.Attempt.Target.Contains(weak),
                "A focused fragment follows in the same session and preserves the visible prefix");
            Check(course.Accuracy < 100 && course.Level == 0, "A miss remains visible in session accuracy");
            FinishPythonFragment(course);
            Check(course.Level == 0 && course.Focus == null, "A focused fragment does not itself advance the course");

            var clean = new AdaptivePython(new Random(6), 0, 95);
            clean.Attempt.Start();
            for (int i = 0; i < 3; i++) FinishPythonFragment(clean);
            Check(clean.Accuracy == 100 && clean.Level == 1 && clean.Attempt.Running,
                "Three accurate normal fragments open the next level without pausing");

            var low = new AdaptivePython(new Random(26), 2, 95);
            low.Attempt.Start();
            int blocks = 0;
            for (int i = 0; i < 9; i++)
            {
                if (low.Focus == null)
                {
                    blocks++;
                    for (int miss = 0; miss < 12; miss++) low.Enter('@');
                }
                FinishPythonFragment(low);
            }
            Check(blocks >= 3 && low.Level == 2 && low.Attempt.Running,
                "Repeated low accuracy keeps the same level and continues training");

            var layout = new AdaptivePython(new Random(6), 0, 95);
            layout.Attempt.Start();
            layout.Enter('я');
            Check(layout.Weakest == null && layout.Accuracy == 0, "Wrong layout affects accuracy without scheduling a focus drill");

            var repairs = new AdaptivePython(new Random(6), PythonDrills.CodeLevels.Length - 1, 95);
            repairs.Attempt.Start();
            for (int i = 0; i < 5; i++) FinishPythonFragment(repairs);
            Check(repairs.Level == PythonDrills.CodeLevels.Length - 1 && repairs.Accuracy == 100 && repairs.Attempt.Running &&
                !repairs.Prefix.Contains(CodeText.Erase), "The last stage keeps running and shows edited text");

            var profile = new StudioProfile();
            clean.SaveProgress(profile, Now);
            clean.ProgressSaved();
            clean.SaveProgress(profile, Now);
            Check(profile.AdaptivePythonLevel == 1 && profile.Results.Count == 1 &&
                profile.Results[0].Accuracy == 100 && profile.Motor.Count > 0, "Completed blocks save once with progress and motor evidence");
        }

        private static void FinishPythonFragment(ContinuousTyping course)
        {
            var fragment = course.Attempt;
            while (!fragment.Finished)
            {
                char expected = fragment.Expected;
                if (!course.Enter(expected == CodeText.Erase ? '\b' : expected))
                    throw new InvalidOperationException("Python fragment rejected the expected key");
            }
        }

        private static void EndlessTypingModes()
        {
            foreach (ContinuousKind kind in new[] { ContinuousKind.Russian, ContinuousKind.English })
            {
                var expectedLayout = kind == ContinuousKind.Russian ? Layout.Russian : Layout.English;
                for (int level = 0; level < ContinuousTyping.Levels(kind).Length; level++)
                {
                    var course = new ContinuousTyping(new Random(35), kind, level, 95);
                    course.Attempt.Start();
                    for (int index = 0; index < 24; index++)
                    {
                        string text = course.Attempt.Target;
                        Check(char.IsUpper(text[0]) && text.TrimEnd().Any(c => ".?!".Contains(c)) && text.EndsWith(" ") &&
                            text.All(c => TypingKeyboard.Hint(c, expectedLayout) != null && TypingKeyboard.Hint(c, expectedLayout).Layout == expectedLayout),
                            "Ordinary text stays writable in its own layout with uppercase and punctuation");
                        FinishPythonFragment(course);
                    }
                    Check(course.Level == level && course.Accuracy == 100 && course.Attempt.Running,
                        "Selected prose difficulty stays active and the next prompt starts immediately");
                }
            }

            var symbols = new ContinuousTyping(new Random(4), ContinuousKind.Symbols, 1, 95, new[] { "#", "<=", "?>" });
            symbols.Attempt.Start();
            for (int index = 0; index < 30; index++)
            {
                Check(symbols.Attempt.Target.All(c => "#<=?> ".Contains(c)), "Edited symbols replace the default set");
                FinishPythonFragment(symbols);
            }

            var custom = new ContinuousTyping(new Random(23), ContinuousKind.Custom, 0, 95, new[] { "Код.", "Hello!\r\n\tDone." });
            custom.Attempt.Start();
            var seen = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                seen.Add(custom.Attempt.Target);
                FinishPythonFragment(custom);
            }
            Check(seen.SetEquals(new[] { "Код. ", "Hello!\n    Done.\n" }) && custom.Attempt.Running,
                "Custom sets cycle through exact supported multilingual content");
            Check(ContinuousTyping.ValidateSnippets(ContinuousKind.Custom, new[] { "\t " }) != null &&
                ContinuousTyping.ValidateSnippets(ContinuousKind.Symbols, new[] { "—" }) != null,
                "The editor can report blank or unsupported custom input");
            Check(custom.Restart().Inputs == 0 && !custom.Restart().Attempt.Running, "Restart keeps the difficulty and creates a fresh attempt");

            var easy = new ContinuousTyping(new Random(17), ContinuousKind.Python, 0, 95);
            var hard = new ContinuousTyping(new Random(17), ContinuousKind.Python, ContinuousTyping.Levels(ContinuousKind.Python).Length - 1, 95);
            easy.Attempt.Start();
            hard.Attempt.Start();
            var basicSigns = new HashSet<char>();
            var advancedSigns = new HashSet<char>();
            for (int index = 0; index < 30; index++)
            {
                basicSigns.UnionWith(easy.Attempt.Target.Where(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)));
                advancedSigns.UnionWith(hard.Attempt.Target.Where(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)));
                Check(hard.Attempt.Target.Contains(CodeText.Erase), "Advanced Python includes deliberate Backspace practice");
                FinishPythonFragment(easy);
                FinishPythonFragment(hard);
            }
            Check(advancedSigns.Count > basicSigns.Count + 8 && hard.Accuracy == 100 && easy.Level == 0,
                "Advanced Python has a broader symbol repertoire while a fixed difficulty stays fixed");
        }

        private static void EndlessTypingSummary()
        {
            var course = new ContinuousTyping(new Random(19), ContinuousKind.Custom, 0, 95, new[] { "Alpha." });
            var initial = course.Snapshot();
            Check(initial.Count == 0 && initial.Cpm == 0 && initial.Wpm == 0 && initial.Seconds == 0,
                "A fresh session reports zero summary data");
            course.Attempt.Start();
            course.Enter('!');
            course.Enter('!');
            FinishPythonFragment(course);
            course.SaveProgress(new StudioProfile(), Now);
            course.ProgressSaved();
            course.Enter('A');
            course.Enter('!');
            course.Attempt.Pause();

            var snapshot = course.Snapshot();
            Check(snapshot.Count == 8 && snapshot.Errors == 3 && snapshot.Seconds > 0 &&
                snapshot.Accuracy == course.Accuracy && snapshot.Wpm == snapshot.Cpm / 5,
                "Stop summary includes finished, saved and in-progress fragments");
            Check(snapshot.TopMistakes.Length == 2 && snapshot.TopMistakes[0].Key == 'A' &&
                snapshot.TopMistakes[0].Value == 2 && snapshot.TopMistakes[1].Key == 'l',
                "Stop summary ranks the expected keys missed across the whole session");
            Check(course.Snapshot().Seconds == snapshot.Seconds, "Stopped time stays fixed");
            course.Attempt.Start();
            course.Enter('l');
            Check(course.Snapshot().Count == snapshot.Count + 1, "Resuming keeps the existing summary data");

            var speed = new ContinuousTypingSnapshot(180, 0, 60, 100, new KeyValuePair<char, long>[0]);
            Check(speed.Cpm == 180 && speed.Wpm == 36, "Speed uses accepted characters per minute and five characters per word");

            var repair = new ContinuousTyping(new Random(22), ContinuousKind.Python, ContinuousTyping.Levels(ContinuousKind.Python).Length - 1, 95);
            repair.Attempt.Start();
            string target = repair.Attempt.Target;
            FinishPythonFragment(repair);
            Check(repair.Snapshot().Count == target.Count(character => character != CodeText.Erase),
                "Scripted Backspace counts as input for accuracy but not as a typed character");
        }

        private static void Challenge()
        {
            var p = new StudioProfile { ChallengeStart = Now, ChallengeMinutes = 30 };
            p.ChallengePlan = ChallengePlanner.Build(p, 30, Now);
            Check(p.ChallengePlan.Count == 10 && p.ChallengePlan.GroupBy(x => x.Day).All(x => x.Sum(y => y.Minutes) == 25), "Budget is inclusive of rest");
            var baseTask = p.ChallengePlan.Single(t => t.Id == "base");
            var next = p.ChallengePlan.Single(t => t.Id == "retest");
            var a = ChallengePlanner.ExerciseFor(p, baseTask, Now);
            var b = ChallengePlanner.ExerciseFor(p, next, Now.AddDays(1));
            Check(a.Text == b.Text && a.FixedGuidance && b.FixedGuidance, "Comparable baseline and retest");
            Check(ChallengePlanner.Context(p, baseTask) != ChallengePlanner.Context(p, next), "Two results independently measured");
            p.Results.Add(new TypingResult { Context = ChallengePlanner.Context(p, baseTask), Completed = true });
            Check(ChallengePlanner.Done(p, baseTask) && !ChallengePlanner.Done(p, next), "Tasks closed by actual evidence");
            var win = p.ChallengePlan.Single(t => t.Route != null && t.Day == 1);
            p.RouteResults.Add(new RouteResult { Context = ChallengePlanner.Context(p, win), Assisted = true });
            Check(!ChallengePlanner.Done(p, win), "Unverified shortcut not credited");
            Check(ChallengePlanner.ExerciseFor(p, p.ChallengePlan.Single(t => t.Activity == "recall"), Now.AddDays(1)).Independent, "Day two retrieves without hints");
        }
    }
}
