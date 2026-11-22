using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LabPoint(int Second, bool Correct, string Key, string Task, int Milliseconds);

internal sealed class LabRun
{
    private readonly Func<double> clock;
    private readonly Random random;
    private readonly HashSet<string> held = [];
    private readonly List<LabPoint> points = [];
    private readonly Queue<string> repeat = new();
    private readonly HashSet<string> used = [];
    private readonly ShortcutAttempt[] past;
    private double activeMilliseconds, sinceTask;
    private double? activeSince;
    private bool stopped, superUsed;
    private int taskInBlock, completedScenarios;
    internal readonly string Session = Guid.NewGuid().ToString("N");
    internal readonly string App, Mode;
    internal readonly int Level, TimeLimit;
    internal readonly LabScene Scene;
    internal readonly List<ShortcutAttempt> Attempts = [];
    internal LabTask Task { get; private set; }
    internal bool Paused { get; private set; } = true;
    internal bool Saved { get; private set; }
    internal string Status { get; private set; } = "Нажмите «Продолжить» после передачи клавиш в учебное окно.";
    internal string Feedback { get; private set; } = "ready";
    internal string LastKey { get; private set; } = "";
    internal int Solved { get; private set; }
    internal int Errors { get; private set; }
    internal int Block { get; private set; } = 1;
    internal string ScenarioTitle => scenario?.Title ?? "";
    internal int ScenarioStep => scenario == null ? 0 : scenarioStep + 1;
    internal int ScenarioLength => scenario?.Steps.Length ?? 0;
    private LabScenario scenario;
    private int scenarioStep;
    internal int Seconds => Math.Min(TimeLimit, (int)((activeMilliseconds + (activeSince is double since ? Math.Max(0, clock() - since) : 0)) / 1000));
    internal bool Finished => stopped || Seconds >= TimeLimit;
    internal int Remaining => Math.Max(0, TimeLimit - Seconds);
    internal string[] Held => held.ToArray();

    internal LabRun(string app, string mode, int level, int seconds, string layout, IEnumerable<ShortcutAttempt> history,
        Random random = null, Func<double> clock = null)
    {
        if (!LabCatalog.Apps.Contains(app)) throw new ArgumentException("Выберите Ubuntu, Chrome, VS Code или Терминал.");
        if (mode is not ("drill" or "sandbox")) throw new ArgumentException("Выберите тренировку или учебный рабочий стол.");
        App = app; Mode = mode; Level = Math.Clamp(level, 0, 2); TimeLimit = Math.Clamp(seconds, 1, 180);
        this.random = random ?? Random.Shared; this.clock = clock ?? (() => Environment.TickCount64);
        past = history.Where(attempt => attempt.Skill?.StartsWith("lab/" + app + "/", StringComparison.Ordinal) == true).ToArray();
        Scene = new LabScene(app, layout); Next();
    }

    internal void Pause()
    {
        if (activeSince is double since) activeMilliseconds += Math.Max(0, clock() - since);
        activeSince = null; Paused = true; held.Clear(); superUsed = false;
    }
    internal void Resume()
    {
        if (Finished) return;
        if (!Paused) return;
        Paused = false; held.Clear(); activeSince = clock(); sinceTask = clock();
        Feedback = "ready"; Status = "Клавиши принимаются. Выполните задание.";
    }
    internal void Stop() { Pause(); stopped = true; }
    internal void MarkSaved() => Saved = true;

    internal ShortcutAttempt Input(string key, bool pressed, string text)
    {
        if (Finished || Paused || string.IsNullOrEmpty(key)) return null;
        if (!pressed)
        {
            if (!held.Remove(key)) return null;
            if (KeyChord.Family(key) == "Alt" && Scene.Switcher) Scene.Switcher = false;
            if (KeyChord.Family(key) == "Super" && !superUsed && held.Count == 0)
                return Perform(LabCatalog.Shortcuts.First(task => task.Id == "overview"), key);
            return null;
        }
        if (!held.Add(key)) return null;
        if (KeyChord.IsModifier(key))
        {
            if (KeyChord.Family(key) == "Super") superUsed = false;
            return null;
        }
        if (held.Any(item => KeyChord.Family(item) == "Super")) superUsed = true;
        bool commandModifiers = held.Any(item => KeyChord.Family(item) is "Ctrl" or "Alt" or "Super");
        var actual = LabCatalog.Resolve(key, held, Scene);
        if (Task.Action == "command" && actual?.Id is "terminal-erase" or "terminal-home" or "terminal-end" or
            "terminal-kill" or "terminal-cancel" or "terminal-paste")
        {
            if (!Scene.Apply(actual.Action)) return Miss(key, "Не удалось исправить строку команды.");
            Status = "Строка изменена. Дополните команду из задания и нажмите Enter.";
            return null;
        }
        if (Task.Keys.Length > 0)
        {
            if (actual != null) return Perform(actual, key);
        }
        if (commandModifiers) return Miss(key, "Это сочетание не выполнило действие. Проверьте задание и удерживаемые клавиши.");
        if (Task.Keys.Length > 0) return Miss(key, WrongKeyMessage(key));
        string entered = VirtualText(key, text);
        string before = Scene.Overview ? Scene.Search : Scene.Active?.App == "terminal" || Scene.Active?.Focus == "terminal" ? Scene.TerminalInput : Scene.Active?.Input ?? "";
        bool equivalentCommand = Task.Action == "command" && key == "Enter" && Scene.Terminal.MatchesCommand(before, Task.Text);
        if (Task.Action == "command" && key == "Enter" && before != Task.Text && !equivalentCommand)
            return Miss(key, "Команда отличается от задания. Исправьте строку и нажмите Enter.");
        bool changed = Scene.Text(key, entered);
        if (Task.Keys.Length == 0)
        {
            bool complete = Task.Action == "type-code" ? Scene.Active?.Body == Task.Text : key == "Enter" && (before == Task.Text || equivalentCommand);
            if (complete && changed && VerifyTextResult()) return Pass(key);
            if (key == "Enter")
            {
                var failed = Miss(key, "Результат не совпал с заданием. Исправьте строку и повторите Enter.");
                return failed;
            }
            Status = "Ввод принят. " + (Task.Action == "type-code" ? "Допечатайте строку из задания." : "Для выполнения нажмите Enter.");
            Feedback = "ready"; return null;
        }
        return Miss(key, WrongKeyMessage(key));
    }

    private string WrongKeyMessage(string key) =>
        Task.Keys == "F11" && key == "Print"
            ? "Получена клавиша PrtSc, а задание ждёт F11. На MateBook попробуйте Fn+F11 или переключите Fn Lock. Снимок не сделан."
            : "Нажата «" + key + "». Ожидается " + Task.Keys + ". Исправьте нажатие.";

    private bool VerifyTextResult() => Task.Action switch
    {
        "command" => Scene.Terminal.LastSuccess,
        "find-text" => Scene.Active?.Body.Contains(Task.Text, StringComparison.OrdinalIgnoreCase) == true && Scene.Active.Find == Task.Text,
        "navigate" => Scene.Active?.Url.TrimEnd('/') == Task.Text.TrimEnd('/'),
        "open-file" => Scene.Active?.Tab?.Title == Task.Text,
        "launch" => Scene.Active?.App == Task.Text && !Scene.Overview,
        "goto-number" => Scene.Active?.Cursor == 0,
        "type-code" => Scene.Active?.Body == Task.Text,
        _ => true
    };

    private string VirtualText(string key, string fallback)
    {
        if (!Scene.LayoutIsVirtual) return fallback;
        var cap = TypingKeyboard.Rows.SelectMany(row => row).FirstOrDefault(item => ShortcutCatalog.KeyName(item) == key);
        if (key == "Space") return " ";
        if (cap == null) return fallback;
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        string value = shift ? cap.Shifted(Scene.Layout == "RU" ? Layout.Russian : Layout.English) : cap.Base(Scene.Layout == "RU" ? Layout.Russian : Layout.English);
        return value.Length == 1 ? value : fallback;
    }

    private ShortcutAttempt Perform(LabTask actual, string key)
    {
        if (actual.Id != Task.Id) return Miss(key, WrongKeyMessage(key));
        bool eligible = LabCatalog.Available(actual, Scene);
        bool changed = eligible && Scene.Apply(actual.Action);
        if (changed) return Pass(key);
        return Miss(key, "Действие сейчас недоступно. " + Task.Title + ".");
    }

    private ShortcutAttempt Pass(string key)
    {
        var attempt = Record(true, key);
        Solved++; taskInBlock++;
        string result = Task.Title + " — выполнено.";
        if (taskInBlock == 20) { Block++; taskInBlock = 0; used.Clear(); result += " Начался следующий блок."; }
        LastKey = ""; scenarioStep++;
        if (scenarioStep >= scenario.Steps.Length) { completedScenarios++; scenario = null; }
        Next(); Status = result; Feedback = "good";
        return attempt;
    }

    private ShortcutAttempt Miss(string key, string message)
    {
        Errors++; Feedback = "error"; Status = message; LastKey = key;
        if (scenario != null && !repeat.Contains(scenario.Id)) repeat.Enqueue(scenario.Id);
        return Record(false, key);
    }

    private ShortcutAttempt Record(bool correct, string key)
    {
        int milliseconds = (int)Math.Clamp(clock() - sinceTask, 1, 60000);
        var attempt = new ShortcutAttempt { Skill = "lab/" + App + "/" + Task.Id, Session = Session, Verification = "lab-scene",
            Date = DateTime.Now, Correct = correct, Milliseconds = milliseconds, MistakeKey = correct ? null : key, InputMode = "exact" };
        Attempts.Add(attempt); points.Add(new LabPoint(Seconds, correct, key, Task.Id, milliseconds));
        return attempt;
    }

    private void Next()
    {
        if (scenario == null)
        {
            var candidates = LabScenarios.Eligible(App, Scene, Level);
            if (candidates.Length == 0) throw new InvalidOperationException("В учебной сцене нет доступных связанных заданий.");
            LabScenario weak = null;
            if (completedScenarios % 3 == 2)
                for (int index = repeat.Count; index > 0; index--)
                {
                    string id = repeat.Dequeue();
                    weak = candidates.FirstOrDefault(item => item.Id == id);
                    if (weak != null) break;
                    repeat.Enqueue(id);
                }
            var unseen = candidates.Where(item => !used.Contains(item.Id)).ToArray();
            if (unseen.Length == 0) unseen = candidates;
            var errors = past.Where(attempt => !attempt.Correct).GroupBy(attempt => attempt.Skill)
                .ToDictionary(group => group.Key, group => group.Count());
            scenario = weak ?? unseen.OrderBy(item => random.NextDouble() / (1 + item.Steps.Sum(step =>
                errors.GetValueOrDefault("lab/" + App + "/" + step.Id)))).First();
            scenarioStep = 0; used.Add(scenario.Id);
        }
        Task = scenario.Steps[scenarioStep]; sinceTask = clock();
        if (Task.Action == "command") { Scene.TerminalInput = ""; Scene.Active.Cursor = 0; }
    }

    internal LabSessionRecord SessionRecord() => new() { Session = Session, App = App, Mode = Mode, Level = Level, Date = DateTime.Now,
        Seconds = Seconds, Solved = Solved, Errors = Errors, Completed = TimeLimit == 180 && Seconds >= 180 };

    internal object Result(IEnumerable<LabSessionRecord> sessions, IEnumerable<LabSessionRecord> records = null)
    {
        var matching = sessions.Concat(records ?? []).GroupBy(item => item.Session).Select(group => group.First()).Where(item => item.App == App && item.Mode == Mode && item.Level == Level).ToArray();
        var previousBest = matching.Where(item => item.Session != Session && item.Completed)
            .OrderByDescending(item => item.Solved).ThenBy(item => item.Errors).FirstOrDefault();
        int previousRecord = previousBest?.Solved ?? 0;
        bool complete = TimeLimit == 180 && Seconds >= 180;
        bool better = complete && (previousBest == null || Solved > previousBest.Solved || Solved == previousBest.Solved && Errors < previousBest.Errors);
        return new { App, Mode, Level, Solved, Errors, Seconds, completed = complete,
            accuracy = Solved + Errors == 0 ? 0 : Math.Round(100.0 * Solved / (Solved + Errors), 1),
            record = Math.Max(previousRecord, complete ? Solved : 0), previousRecord, previousRecordErrors = previousBest?.Errors ?? 0, newRecord = better,
            series = points, weak = points.Where(point => !point.Correct).GroupBy(point => point.Task).Select(group =>
            { var task = LabCatalog.Shortcuts.FirstOrDefault(item => item.Id == group.Key); return new { id = group.Key, title = task?.Title ?? "Ввод команды", keys = task?.Keys ?? "", errors = group.Count() }; }),
            history = matching.OrderByDescending(item => item.Date).Take(15).Select(item => new
            { item.Session, item.App, item.Mode, item.Level, item.Date, item.Seconds, item.Solved, item.Errors, item.Completed }) };
    }
}

internal static class LabSessionRecords
{
    internal static void Update(StudioProfile profile, LabSessionRecord record)
    {
        profile.LabSessions.RemoveAll(item => item.Session == record.Session);
        profile.LabSessions.Add(record);
        // Read legacy history before pruning so its best completed run survives migration.
        profile.LabRecords = profile.LabRecords.Concat(profile.LabSessions)
            .Where(item => item.Completed && item.Seconds >= 180 && LabCatalog.Apps.Contains(item.App) &&
                item.Mode is "drill" or "sandbox" && item.Level is >= 0 and <= 2)
            .GroupBy(item => new { item.App, item.Mode, item.Level })
            .Select(group => group.OrderByDescending(item => item.Solved).ThenBy(item => item.Errors)
                .ThenBy(item => item.Date).First()).ToList();
        if (profile.LabSessions.Count > 500)
            profile.LabSessions.RemoveRange(0, profile.LabSessions.Count - 500);
    }
}
