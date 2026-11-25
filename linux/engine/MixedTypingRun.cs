using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TouchTypingStudio;

// A command is a physical gesture, never a string that can be typed or pasted for credit.
// Layout only describes what to draw on the keycaps and which text the exercise asks for.
internal sealed record TypingAtom(string Kind, string Value, string Layout)
{
    internal bool Command => Kind == "command";
    internal string Display => Command ? "⟦" + Value + "⟧" : Value;
    internal KeyChord Chord => Command ? Gesture.Parse(Value).Chords[0] : null;

    internal bool IsValid() => (Layout is "RU" or "EN") &&
        (Kind == "text" ? Value?.Length == 1 && (TypingKeyboard.Supports(Value[0]) || Value[0] is '\n' or '\t') :
        Kind == "command" && Gesture.TryParse(Value, out var g) && g.Chords.Length == 1 && g.Chords[0].Label == Value);
}

internal sealed partial class MixedTypingRun
{
    internal static readonly IReadOnlyDictionary<string, string> Modes = new Dictionary<string, string>
    {
        ["Digits"] = "Цифры", ["TopRow"] = "Цифры и знаки верхнего ряда",
        ["Hotkeys"] = "Горячие клавиши", ["WordsDigits"] = "Слова и цифры",
        ["WordsDigitsHotkeys"] = "Слова, цифры и горячие клавиши"
    };
    // These run inside a normal unprivileged foreground window. System-reserved chords
    // (e.g. Alt+Tab) match by the same mechanism if delivered, but are not made a
    // prerequisite in the course: don't train an impossible shortcut under a compositor.
    private static readonly string[] BasicCommands = ["Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+S", "Ctrl+A", "F2"];
    private static readonly string[] MoreCommands = ["Ctrl+Shift+P", "Ctrl+Enter", "Ctrl+P", "Ctrl+F", "F3", "Shift+F3", "F12", "Ctrl+2", "Alt+Left"];
    private static readonly string[] RussianWords = ["отчёт", "готово", "этап", "смета", "итог", "план", "срок", "файл", "задача", "смена", "строка"];
    private static readonly string[] EnglishWords = ["report", "total", "ready", "plan", "item", "batch", "draft", "file", "row", "task", "order", "stock"];

    internal readonly string Mode, Title, StartLayout;
    internal readonly int Level, TimeLimit, Cycles;
    private readonly Random random;
    private readonly Stopwatch watch = new();
    private readonly double priorSeconds;
    private readonly Dictionary<TypingAtom, int> mistakes;
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    // A key consumed as a command stays consumed until key-up, even if its OS text
    // callback or auto-repeat arrives after the exercise advanced to a text unit.
    private readonly HashSet<string> consumedTriggers = new(StringComparer.Ordinal);
    private readonly List<TypingAtom> plan;
    private readonly int[] lengths;
    private int cycleIndex, cycleStart, absoluteOffset;
    private string carry = "";
    private KeyChord[] layoutBindings = [];
    private bool saved;
    internal string AttemptId { get; } = Guid.NewGuid().ToString("N");
    internal int Position { get; private set; }
    internal int Errors { get; private set; }
    internal bool IsInfinite => Cycles == 0;
    internal int? PlannedUnits => IsInfinite ? null : plan.Count;
    internal bool Completed => !IsInfinite && Position >= plan.Count;
    internal bool Finished => Completed || (TimeLimit > 0 && Seconds >= TimeLimit);
    internal bool Running => watch.IsRunning && !Finished;
    internal bool CanSave => !saved && (Position > 0 || Errors > 0);
    internal bool LastCorrect { get; private set; } = true;
    internal bool ConsumePhysical { get; private set; }
    internal double Seconds => TimeLimit > 0 ? Math.Min(TimeLimit, priorSeconds + watch.Elapsed.TotalSeconds) : priorSeconds + watch.Elapsed.TotalSeconds;
    internal double Accuracy => Position == 0 ? 0 : 100d * Position / ((double)Position + Errors);
    internal IReadOnlyDictionary<TypingAtom, int> Mistakes => mistakes;
    internal IReadOnlyList<TypingAtom> Units => plan;
    internal TypingAtom Expected => !Completed ? plan[Position - absoluteOffset] : null;

    private MixedTypingRun(string mode, string title, string layout, int level, int seconds, bool infinite,
        IReadOnlyList<IReadOnlyList<TypingAtom>> cycles, Random random, int position = 0, int errors = 0,
        double activeSeconds = 0, IReadOnlyDictionary<TypingAtom,int> previousMistakes = null, string attemptId = null)
    {
        if (cycles.Count is < 1 or > 5 || cycles.Any(c => c == null || c.Count is < 1 or > 550))
            throw new ArgumentException("Некорректная длина тренировки.");
        Mode = mode; Title = title; StartLayout = layout == "RU" ? "RU" : "EN"; Level = level;
        TimeLimit = Math.Clamp(seconds, 0, 3600); Cycles = infinite ? 0 : cycles.Count;
        this.random = random ?? Random.Shared;
        lengths = cycles.Select(c => c.Count).ToArray();
        plan = cycles.SelectMany(c => c).ToList();
        if (plan.Distinct().Any(a => a == null || !a.IsValid()) || position < 0 || position > plan.Count || errors < 0 ||
            !double.IsFinite(activeSeconds) || activeSeconds < 0) throw new ArgumentException("Некорректные данные тренировки.");
        Position = position; Errors = errors; priorSeconds = activeSeconds;
        if (Guid.TryParseExact(attemptId, "N", out _)) AttemptId = attemptId;
        mistakes = previousMistakes == null ? [] : new(previousMistakes);
        while (cycleIndex < lengths.Length - 1 && Position >= cycleStart + lengths[cycleIndex]) cycleStart += lengths[cycleIndex++];
        if (!Finished) watch.Start();
    }

    internal static MixedTypingRun Create(string mode, string layout, int level, int seconds, int cycles, bool infinite, Random random)
    {
        if (!Modes.TryGetValue(mode, out string title)) throw new ArgumentException("Выберите режим практики.");
        layout = layout == "RU" ? "RU" : "EN"; level = Math.Clamp(level, 0, 2);
        var plans = Enumerable.Range(0, infinite ? 1 : Math.Clamp(cycles, 1, 5))
            .Select(_ => (IReadOnlyList<TypingAtom>)Generate(mode, layout, level, random)).ToArray();
        return new(mode, title, layout, level, seconds, infinite, plans, random);
    }

    internal static MixedTypingRun FromPlan(string mode, string title, IReadOnlyList<TypingAtom> units,
        int position = 0, int errors = 0, double activeSeconds = 0, IReadOnlyDictionary<TypingAtom,int> mistakes = null, string attemptId = null)
        => new(mode, title, units.FirstOrDefault()?.Layout, 0, 0, false, [units], null, position, errors, activeSeconds, mistakes, attemptId);

    private static List<TypingAtom> Generate(string mode, string firstLayout, int level, Random random)
    {
        int goal = random.Next(350, 551);
        var atoms = new List<TypingAtom>(goal);
        string[] commands = (level == 0 ? BasicCommands : [.. BasicCommands, .. MoreCommands]).OrderBy(_ => random.Next()).ToArray();
        void Text(string text, string lang)
        {
            foreach (char c in text)
                if (atoms.Count < goal) atoms.Add(new("text", c.ToString(), lang));
        }
        int i = 0;
        int[] digits = Enumerable.Range(0, 10).ToArray(), signs = (int[])digits.Clone();
        while (atoms.Count < goal)
        {
            string lang = (i % 2 == 0 || mode is "TopRow" or "Digits") ? firstLayout : (firstLayout == "RU" ? "EN" : "RU");
            string[] words = lang == "RU" ? RussianWords : EnglishWords;
            if (mode == "Digits") Text(random.Next(10).ToString(System.Globalization.CultureInfo.InvariantCulture), lang);
            else if (mode == "TopRow")
            {
                // Cover every digit and shifted symbol in every 20 units, in new orders.
                if (i % 20 == 0) { random.Shuffle(digits); random.Shuffle(signs); }
                int digit = (i % 2 == 0 ? digits : signs)[i / 2 % 10];
                var cap = TypingKeyboard.Rows[0][digit == 0 ? 10 : digit];
                var layout = lang == "RU" ? Layout.Russian : Layout.English;
                Text(i % 2 == 0 ? cap.Base(layout) : cap.Shifted(layout), lang);
            }
            else
            {
                if (mode != "Hotkeys")
                {
                    var num = random.Next(1, level == 0 ? 30 : level == 1 ? 300 : 10000);
                    Text(words[random.Next(words.Length)] + " " + num.ToString(System.Globalization.CultureInfo.InvariantCulture), lang);
                    if (mode == "WordsDigitsHotkeys" && i % 3 == 1)
                        Text(lang == "RU" ? "№" : "#", lang);
                    Text(" ", lang);
                }
                if (mode != "WordsDigits" && atoms.Count < goal)
                    atoms.Add(new("command", commands[i % commands.Length], lang));
            }
            i++;
        }
        return atoms;
    }

    internal void SetLayoutBindings(params string[] bindings) => layoutBindings = bindings
        .Where(b => Gesture.TryParse(b, out var g) && g.Chords.Length == 1)
        .Select(b => Gesture.Parse(b).Chords[0]).ToArray();
    internal void Pause() { watch.Stop(); held.Clear(); consumedTriggers.Clear(); ConsumePhysical = false; }
    internal void Resume() { if (!saved && !Finished) watch.Start(); }
    internal void Saved() => saved = true;

    internal void Enter(string text)
    {
        if (!Running || text?.Length != 1) return; // a commit from an IME may span many chars; bulk paste never completes a drill
        if (consumedTriggers.Count > 0 || held.Any(k => KeyChord.Family(k) is "Ctrl" or "Alt" or "Super")) return;
        var target = Expected;
        // Physical events are the only way to enter or miss a command. Its
        // accompanying text notification must never count the same strike twice.
        if (target.Command) return;
        if (text == target.Value) Advance(); else Miss();
    }

    internal bool Physical(string key, bool pressed)
    {
        ConsumePhysical = false;
        if (string.IsNullOrEmpty(key)) return false;
        if (!pressed) { held.Remove(key); consumedTriggers.Remove(key); return false; }
        if (!Running) return false;
        var chord = Expected.Command ? Expected.Chord : null;
        bool modifier = KeyChord.IsModifier(key);
        bool blockedText = held.Any(k => KeyChord.Family(k) is "Ctrl" or "Alt" or "Super");
        ConsumePhysical = chord != null || blockedText || modifier && KeyChord.Family(key) is not "Shift";
        // Physical lock events can change native text but are not failures unless
        // a lesson explicitly expects that particular key.
        if (key is ("NumLock" or "CapsLock" or "ScrollLock") && chord?.Trigger != key)
            return ConsumePhysical = false;
        if (!held.Add(key)) return ConsumePhysical = ConsumePhysical || consumedTriggers.Contains(key);
        bool onlyTrigger = held.All(k => KeyChord.IsModifier(k) || k == key);
        bool matches = chord != null && (chord.Trigger == null ? modifier && chord.FitsModifiers(held) :
            !modifier && onlyTrigger && (chord.Trigger == key && chord.FitsModifiers(held) ||
            chord.AcceptsNavigationKeypad(key, held)));
        // A modifier-only OS binding (e.g. Alt+Shift) has no trigger key. Its
        // prefix must also remain unconsumed so a native frontend can route the
        // full switch. Track it for exact matching; never credit an unrelated step.
        bool switchingLayout = layoutBindings.Any(c => c.Trigger == null
            ? modifier && held.All(active => KeyChord.IsModifier(active) &&
                c.Modifiers.Any(expected => KeyChord.Fits(expected, active))) &&
                held.Select(KeyChord.Family).Distinct().Count() == held.Count
            : c.Trigger == key && onlyTrigger && c.FitsModifiers(held));
        if (switchingLayout && !matches) return ConsumePhysical = false;
        if (modifier && (chord == null || chord.Trigger != null)) return ConsumePhysical;
        if (chord != null)
        {
            if (!modifier) consumedTriggers.Add(key);
            if (matches) Advance();
            else if (!modifier) Miss();
        }
        else if (blockedText && !modifier) Miss(); // Ctrl+V over a text unit is not text input
        return ConsumePhysical;
    }

    private void Miss()
    {
        var atom = Expected;
        mistakes[atom] = mistakes.GetValueOrDefault(atom) + 1;
        Errors++; LastCorrect = false;
    }
    private void Advance()
    {
        Position++; LastCorrect = true;
        if (Completed) { watch.Stop(); return; }
        if (IsInfinite && Position == absoluteOffset + plan.Count)
        {
            carry = string.Concat(plan.TakeLast(9).Select(a=>a.Display));
            absoluteOffset += plan.Count; cycleIndex++;
            plan.Clear(); plan.AddRange(Generate(Mode, StartLayout, Level, random));
        }
        else if (!IsInfinite && Position == cycleStart + lengths[cycleIndex]) cycleStart += lengths[cycleIndex++];
    }

    internal object State(string nativeLayout)
    {
        var atom = Expected;
        var preferred = atom?.Layout == "RU" ? Layout.Russian : Layout.English;
        KeyHint hint = atom != null && !atom.Command ? TypingKeyboard.Hint(atom.Value[0], preferred) : null;
        string key = atom == null ? "" : atom.Command ? atom.Chord.Trigger ?? "" : hint == null ? "" : ShortcutCatalog.KeyName(hint.Cap);
        string modifier = hint?.Modifier switch { LinuxKey.LeftShift => "LShift", LinuxKey.RightShift => "RShift", _ => "" };
        string[] targets = atom == null ? [] : atom.Command ? atom.Chord.Tokens.Select(t =>
            KeyChord.Families.Contains(t) ? "L" + t : t).ToArray() : modifier == "" ? [key] : [modifier, key];
        int visibleIndex = Position - absoluteOffset;
        string before = (IsInfinite && visibleIndex < 9 ? carry : "") + string.Concat(plan.Take(visibleIndex).TakeLast(18).Select(a => a.Display));
        string after = string.Concat(plan.Skip(visibleIndex).Take(75).Select(a => a.Display));
        var errors = Mistakes.GroupBy(kv => kv.Key.Value).Select(g => new { key = g.Key, count = g.Sum(kv => kv.Value) }).OrderByDescending(a => a.count).ThenBy(a => a.key).ToArray();
        return new {
            id = AttemptId, infinite = IsInfinite, completedPlan = Completed, title = Title, source = "", mode = Mode, typedActions = true, expectedKind = atom?.Kind ?? "", expectedKeys = targets,
            expectedTargets = targets.Select(k => new { key = k, finger = ShortcutCatalog.Finger(k) }).ToArray(),
            expected = atom == null ? "" : atom.Command ? atom.Value : CodeText.Display(atom.Value[0]),
            // Ribbon: command label is an atomic unit, not the sequence of characters C,t,r,l,+,...
            expectedDisplay = atom?.Display ?? "", before, after,
            wantedLayout = atom?.Layout ?? StartLayout, key, modifier, finger = key == "" ? (int?)null : ShortcutCatalog.Finger(key),
            held = held.ToArray(), consumePhysical = ConsumePhysical, written = "", weak = (string)null,
            count = Position, progressDone = Position, progressTotal = PlannedUnits, progressVisible = !IsInfinite,
            progressFraction = PlannedUnits is int total ? (double)Position / total : (double?)null,
            fragmentProgress = !IsInfinite ? (double)Position / plan.Count : 0d, cycle = cycleIndex + 1, cycles = Cycles,
            cycleLengths = IsInfinite ? Array.Empty<int>() : lengths, level = Level,
            errors = Errors, accuracy = Accuracy, cpm = Seconds > 0 ? Math.Round(60 * Position / Seconds) : 0, wpm = Seconds > 0 ? Math.Round(12 * Position / Seconds) : 0,
            seconds = (int)Seconds, timeLimit = TimeLimit, topMistakes = errors, running = Running, finished = Finished,
            correct = LastCorrect, round = 0, rounds = 0, lesson = (string)null
        };
    }

    internal object Result(string layout) => new { state = State(layout), series = Array.Empty<object>() };

    internal TypingResult MakeResult(DateTime now) => new() {
        Date = now, Lesson = Title, Task = Mode, Activity = "TypingPractice", Count = Position, Errors = Errors,
        Seconds = Seconds, Language = StartLayout, Completed = Completed || (IsInfinite && TimeLimit > 0 && Finished)
    };
}
