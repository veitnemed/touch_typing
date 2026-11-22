using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TouchTypingStudio;

internal sealed class ShortcutRun
{
    private static int nextStepToken;
    private readonly Func<double> clock;
    private readonly SkillPicker picker;
    private readonly Func<IEnumerable<ShortcutAttempt>> history;
    private readonly GestureTracker tracker = new();
    private PracticeScene scene;
    private Gesture gesture;
    private int step, elapsed;
    private string entry = "", beforeLayout = "";
    private bool hadErrors, usedSubstitute;
    private double started, activeMilliseconds;
    private double? activeSince;
    internal readonly List<ShortcutAttempt> Attempts = [];
    internal readonly string Session = Guid.NewGuid().ToString("N");
    internal readonly int Limit;
    internal int Solved { get; private set; }
    internal Skill Skill { get; private set; }
    internal int Tier => picker.Tier;
    internal bool Finished => Solved >= Limit;
    internal bool Paused { get; private set; }
    internal int Seconds => (int)((activeMilliseconds +
        (activeSince is double since ? Math.Max(0, clock() - since) : 0)) / 1000);
    internal object VerifiedScene { get; private set; }
    internal string Status { get; private set; } = "";
    internal string Feedback { get; private set; } = "ready";
    internal bool NeedsSystem => !Finished && !IsLayout && TargetChord?.UsesSystem == true;
    internal bool IsLayout => Skill.Action is "layout" or "layout-back";
    internal int Step => step;
    internal int StepToken { get; private set; }
    internal string InputMode { get; private set; }
    private KeyChord TargetChord => gesture != null && step < gesture.Chords.Length ? gesture.Chords[step] : null;
    private KeyChord Chord => InputMode switch
    {
        "waiting" => null,
        "substitute" => TargetChord?.Substitute(),
        _ => TargetChord
    };
    internal string TargetKeys => TargetChord?.Label ?? "";
    internal string InputKeys => Chord?.Label ?? "";
    internal string SubstituteKeys => NeedsSystem ? TargetChord.Substitute().Label : "";
    internal Gesture Guide => InputMode == "substitute" && Chord is { } replacement
        ? new Gesture(gesture.Chords.Select((chord, index) => index == step ? replacement : chord).ToArray())
        : gesture;
    internal string LastKey => tracker.LastKey;
    internal string MistakeKey { get; private set; }
    internal string[] Held => tracker.Held.ToArray();
    internal object SceneRow => scene.Snapshot(entry);
    internal string ExitKey => Chord?.Trigger == "Esc" ? "F12" : "Esc";
    internal string PreviewKey => Chord?.Trigger == "F6" ? "F7" : "F6";
    internal string Activity => IsLayout && beforeLayout != "" ? "layout-after" : Skill.Action;

    internal ShortcutRun(Skill[] skills, int tier, bool auto, bool studyOnly, int limit,
        Func<IEnumerable<ShortcutAttempt>> history, Random random = null, Func<double> clock = null)
    {
        if (skills.Length == 0) throw new ArgumentException("Выберите хотя бы одно упражнение.");
        this.clock = clock ?? (() => Environment.TickCount64);
        this.history = history;
        picker = new SkillPicker(random ?? Random.Shared, skills, tier, auto, studyOnly);
        Limit = Math.Clamp(limit, 3, 60);
        Next(false);
    }

    private void Next(bool keepFeedback)
    {
        Skill = picker.Next(history());
        scene = new PracticeScene(Skill.Action);
        gesture = Skill.Gesture;
        step = elapsed = 0;
        entry = beforeLayout = "";
        hadErrors = usedSubstitute = false;
        if (!keepFeedback) ClearFeedback();
        BeginStep();
    }

    private void BeginStep()
    {
        StepToken = Interlocked.Increment(ref nextStepToken);
        InputMode = NeedsSystem ? "waiting" : "exact";
        UpdateSessionClock();
        ResetTiming(true);
    }

    internal void SetInputMode(string mode, int token)
    {
        if (mode is not ("exact" or "substitute" or "waiting"))
            throw new ArgumentException("Выберите проверку исходного сочетания или учебную замену.");
        if (token != StepToken || !NeedsSystem || mode == InputMode) return;
        bool beginsStep = InputMode == "waiting";
        InputMode = mode;
        UpdateSessionClock();
        ResetTiming();
        if (!beginsStep || Feedback != "good") ClearFeedback();
    }

    internal void Pause()
    {
        Paused = true;
        UpdateSessionClock();
        ResetTiming();
    }

    internal void Resume()
    {
        Paused = false;
        UpdateSessionClock();
        ResetTiming();
        ClearFeedback();
    }

    private void UpdateSessionClock()
    {
        if (!Paused && !Finished && InputMode != "waiting")
            activeSince ??= clock();
        else if (activeSince is double since)
        {
            activeMilliseconds += Math.Max(0, clock() - since);
            activeSince = null;
        }
    }

    private void ResetTiming(bool keepHeld = false)
    {
        started = clock();
        tracker.Reset(started, keepHeld);
    }

    private void ClearFeedback()
    {
        Feedback = "ready";
        MistakeKey = null;
        Status = "";
    }

    internal ShortcutAttempt Input(string key, bool pressed, string text)
    {
        if (Finished || Paused || InputMode == "waiting") return null;
        if (pressed && tracker.Held.Count == 0) VerifiedScene = null;
        if (IsLayout) return pressed ? CheckLayout(key, text) : null;
        if (Skill.Action == "reboot") return pressed ? CheckCommand(key, text) : null;
        if (Chord == null) return null;
        var outcome = tracker.Change(Chord, key, pressed, Skill.Character == "" || text == Skill.Character, clock());
        if (outcome == StrokeOutcome.Pending) return null;
        if (outcome == StrokeOutcome.Error)
        {
            string keyName = tracker.ErrorKey ?? tracker.LastKey ?? key;
            var attempt = Fail("Промах. Повторите текущий шаг.", keyName);
            return attempt;
        }

        elapsed += tracker.ReactionMs;
        scene.Apply(step);
        if (!scene.Verify(step))
            return Fail("Проверьте результат в учебной сцене.");
        usedSubstitute |= InputMode == "substitute";
        step++;
        string result = scene.Outcome;
        if (step < gesture.Chords.Length)
        {
            Feedback = "good";
            Status = result.Length > 0 ? result : "Шаг выполнен. Следующее нажатие.";
            BeginStep();
            return null;
        }
        return Pass(result.Length > 0 ? result : "Сочетание выполнено.", scene.HasResult ? "scene" : "keys", elapsed);
    }

    private ShortcutAttempt CheckLayout(string key, string text)
    {
        if (key != "A")
            return KeyChord.IsModifier(key) || key == "Space" || key == TargetChord?.Trigger
                ? null : Fail("Нажмите физическую A.", key);
        string observed = text.ToLowerInvariant() switch { "a" => "EN", "ф" => "RU", _ => "" };
        if (observed.Length == 0) return Fail("Для проверки нужна раскладка EN (US) или RU.", "A");
        if (beforeLayout == "")
        {
            beforeLayout = observed;
            Feedback = "good";
            Status = $"Сейчас {observed}. Переключите раскладку и снова нажмите A.";
            ResetTiming();
            return null;
        }
        if (observed == beforeLayout) return Fail($"По-прежнему {observed}. Повторите переключение.", "A");
        return Pass($"{beforeLayout} → {observed}. Раскладка сменилась.", "layout", (int)(clock() - started));
    }

    private ShortcutAttempt CheckCommand(string key, string text)
    {
        if (key == "Enter")
        {
            if (entry == "reboot")
            {
                scene.Apply(0);
                if (scene.Verify(0)) return Pass(scene.Outcome, "scene", (int)(clock() - started));
            }
            return Fail("Команда не совпала. Исправьте строку и нажмите Enter.", "Enter");
        }
        if (key == "Backspace")
            entry = entry.Length > 0 ? entry[..^1] : "";
        else if (text.Length == 1 && !char.IsControl(text[0]) && entry.Length < 64)
            entry += text;
        return null;
    }

    private ShortcutAttempt Fail(string message, string mistake = null)
    {
        hadErrors = true;
        MistakeKey = mistake;
        Feedback = "error";
        Status = message;
        var attempt = Record(false, "keys", 0, mistake);
        ResetTiming(true);
        return attempt;
    }

    private ShortcutAttempt Pass(string message, string verification, int milliseconds)
    {
        Feedback = "good";
        Status = message;
        var completed = Skill;
        VerifiedScene = scene.HasResult && !IsLayout ? scene.Snapshot(entry) : null;
        var attempt = Record(true, verification, Math.Clamp(milliseconds, 1, 60000));
        Solved++;
        picker.Update(completed, hadErrors, Attempts);
        if (!Finished) Next(true);
        else UpdateSessionClock();
        return attempt;
    }

    internal object Result()
    {
        var exact = Attempts.Where(attempt => !attempt.IsSubstitute).ToArray();
        var substitute = Attempts.Where(attempt => attempt.IsSubstitute).ToArray();
        return new
        {
            Solved, Limit, Finished, Seconds,
            score = ShortcutScore.From(Attempts),
            series = Times(exact),
            topMistakes = Mistakes(exact),
            substituteSeries = Times(substitute),
            substituteTopMistakes = Mistakes(substitute)
        };
    }

    private static IEnumerable<int?> Times(IEnumerable<ShortcutAttempt> attempts) =>
        attempts.Select(attempt => attempt.Correct ? (int?)attempt.Milliseconds : null);

    private static object Mistakes(IEnumerable<ShortcutAttempt> attempts) =>
        attempts.Where(attempt => !attempt.Correct && attempt.MistakeKey != null)
            .GroupBy(attempt => attempt.MistakeKey).Select(group => new { key = group.Key, count = group.Count() })
            .OrderByDescending(item => item.count).Take(8);

    private ShortcutAttempt Record(bool correct, string verification, int milliseconds, string mistake = null)
    {
        var attempt = new ShortcutAttempt
        {
            Skill = Skill.Id, Session = Session, Date = DateTime.Now,
            Correct = correct, Verification = verification, Milliseconds = milliseconds, MistakeKey = mistake,
            InputMode = usedSubstitute || InputMode == "substitute" ? "substitute" : "exact"
        };
        Attempts.Add(attempt);
        return attempt;
    }
}

internal sealed class PracticeScene
{
    private readonly WorkbenchScene panel = new();
    private readonly string action;
    private const string Sample = "build: 17 tests passed";
    private const string Capture = "Отчёт · 17 проверок";
    private string clipboard = "";
    private string captureId, clipboardCaptureId;
    private bool window = true, maximized, fullscreen, rebooted;

    internal string Outcome { get; private set; } = "";
    internal bool HasResult => action is not ("keys" or "digit" or "digits" or "symbol");

    internal PracticeScene(string action)
    {
        this.action = action;
        if (action.StartsWith("tab-"))
        {
            panel.ReplaceItems("Журнал", "Черновик", "Справка");
            panel.Selection = 1;
        }
        if (action == "paste") clipboard = Sample;
        if (action is "copy" or "cut" or "transfer") panel.Body = Sample;
        if (action == "terminal-open") panel.Terminal = false;
    }

    internal void Apply(int step)
    {
        Outcome = action switch
        {
            "copy" => Copy(),
            "cut" => Cut(),
            "paste" => Paste(),
            "transfer" => step == 0 ? Cut() : Paste(),
            "tab-new" => NewTab(),
            "tab-cycle" => step == 0 ? NewTab() : ChangeTab(-1),
            "tab-close" => CloseTab(),
            "tab-prev" => ChangeTab(-1),
            "tab-next" => ChangeTab(1),
            "window-close" => Toggle(ref window, "Учебное окно закрыто."),
            "window-max" => Toggle(ref maximized, "Учебное окно развёрнуто."),
            "window-full" => Toggle(ref fullscreen, "Учебное окно перешло в полный экран."),
            "terminal-open" => Toggle(ref panel.Terminal, "Учебная консоль открыта."),
            "screenshot" => TakeCapture("Учебный снимок создан."),
            "screenshot-copy" => step == 0 ? TakeCapture("Снимок готов. Скопируйте его.") : CopyCapture(),
            "reboot" => Toggle(ref rebooted, "Учебная консоль приняла команду."),
            _ => ""
        };
    }

    internal bool Verify(int step) => action switch
    {
        "copy" => clipboard == Sample && panel.Body == Sample,
        "cut" => clipboard == Sample && panel.Body.Length == 0,
        "paste" => clipboard == Sample && panel.Body == Sample,
        "transfer" => clipboard == Sample && panel.Body == (step == 0 ? "" : Sample),
        "tab-new" => panel.Items.Count == 4 && panel.Selected == "Новая вкладка",
        "tab-cycle" => panel.Items.Count == 4 && panel.Selected == (step == 0 ? "Новая вкладка" : "Справка"),
        "tab-close" => panel.Items.Count == 2 && !panel.Items.Contains("Черновик"),
        "tab-prev" => panel.Selected == "Журнал",
        "tab-next" => panel.Selected == "Справка",
        "window-close" => !window,
        "window-full" => fullscreen,
        "window-max" => maximized,
        "terminal-open" => panel.Terminal,
        "screenshot" => captureId != null,
        "screenshot-copy" => captureId != null && (step == 0 || clipboardCaptureId == captureId),
        "reboot" => rebooted,
        _ => true
    };

    internal object Snapshot(string entry) => new
    {
        action, tabs = panel.Items, active = panel.Selection, body = panel.Body, entry, clipboard,
        window, maximized, fullscreen, captured = captureId != null, captureId, clipboardCaptureId, terminal = panel.Terminal, rebooted
    };

    private string Copy() { clipboard = panel.Body; return "Учебная строка в буфере обмена."; }
    private string Cut() { clipboard = panel.Body; panel.Body = ""; return "Строка вырезана в учебный буфер."; }
    private string Paste() { panel.Body = clipboard; return "Вставленная строка совпала с содержимым буфера."; }
    private string NewTab() { panel.AddItem("Новая вкладка"); return "Новая вкладка открыта."; }
    private string CloseTab() { panel.RemoveSelected(); return "Закрыта выбранная вкладка «Черновик»."; }
    private string ChangeTab(int offset)
    {
        panel.Selection = (panel.Selection + offset + panel.Items.Count) % panel.Items.Count;
        return $"Активна вкладка «{panel.Selected}».";
    }
    private static string Toggle(ref bool value, string message) { value = !value; return message; }
    private string TakeCapture(string message)
    {
        captureId = Guid.NewGuid().ToString("N");
        return message;
    }

    private string CopyCapture()
    {
        if (captureId == null) return "Сначала сделайте снимок.";
        clipboardCaptureId = captureId;
        clipboard = Capture;
        return "Свежий учебный снимок найден в буфере обмена.";
    }
}
