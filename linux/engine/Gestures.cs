using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record KeyChord(string[] Modifiers, string Trigger)
{
    internal static readonly Dictionary<string, string> ModifierFamilies = new()
    {
        ["LCtrl"] = "Ctrl", ["RCtrl"] = "Ctrl", ["LShift"] = "Shift", ["RShift"] = "Shift",
        ["LAlt"] = "Alt", ["RAlt"] = "Alt", ["LSuper"] = "Super", ["RSuper"] = "Super",
    };
    internal static readonly string[] Families = ["Ctrl", "Alt", "Shift", "Super"];
    internal static readonly string[] StandardKeys = [
        .. "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c => c.ToString()),
        .. Enumerable.Range(1, 12).Select(n => $"F{n}"),
        "Esc", "Tab", "Space", "Enter", "Backspace", "CapsLock", "Print", "Up", "Down", "Left", "Right",
        "Home", "End", "PageUp", "PageDown", "Insert", "Delete", "Menu", "Pause", "ScrollLock", "NumLock",
        "`", "-", "=", "[", "]", "\\", ";", "'", ",", ".", "/",
        .. Enumerable.Range(0, 10).Select(n => $"NumPad{n}"),
        "NumAdd", "NumSubtract", "NumMultiply", "NumDivide", "NumDecimal", "NumEnter"
    ];
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["⊞"] = "Super",
        ["Control"] = "Ctrl", ["LeftCtrl"] = "LCtrl", ["RightCtrl"] = "RCtrl",
        ["LeftShift"] = "LShift", ["RightShift"] = "RShift", ["LeftAlt"] = "LAlt", ["RightAlt"] = "RAlt",
        ["Win"] = "Super", ["Windows"] = "Super", ["LWin"] = "LSuper", ["RWin"] = "RSuper",
        ["LeftSuper"] = "LSuper", ["RightSuper"] = "RSuper",
        ["Escape"] = "Esc", ["Return"] = "Enter", ["Back"] = "Backspace", ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown", ["Del"] = "Delete", ["PrtSc"] = "Print", ["PrintScreen"] = "Print",
        ["↑"] = "Up", ["↓"] = "Down", ["←"] = "Left", ["→"] = "Right",
        ["Пробел"] = "Space", ["Plus"] = "=", ["−"] = "-",
    };

    internal static string Family(string key) => ModifierFamilies.GetValueOrDefault(key, key);
    internal static bool IsModifier(string key) => ModifierFamilies.ContainsKey(key);
    internal static bool Fits(string requested, string actual) => requested == actual || requested == Family(actual);

    // Physical keypad positions remain distinct from the digit row. A
    // navigation exercise may explicitly accept the same key's secondary
    // legend, regardless of how Num Lock was delivered by the platform.
    internal bool AcceptsNavigationKeypad(string physicalKey, IEnumerable<string> held) =>
        Trigger != null && Trigger == TypingKeyboard.NavigationAlias(physicalKey) && FitsModifiers(held);


    internal static bool TryParse(string value, out KeyChord chord)
    {
        chord = null;
        var tokens = value.Split('+', StringSplitOptions.TrimEntries).Select(Normalize).ToArray();
        if (tokens.Length == 0 || tokens.Any(token => token == null)) return false;
        string trigger = IsModifier(tokens[^1]) || Families.Contains(tokens[^1]) ? null : tokens[^1];
        var modifiers = trigger == null ? tokens : tokens[..^1];
        if (modifiers.Any(token => !Families.Contains(Family(token))) ||
            modifiers.Select(Family).Distinct().Count() != modifiers.Length) return false;
        chord = new KeyChord(modifiers.OrderBy(token => Array.IndexOf(Families, Family(token))).ToArray(), trigger);
        return true;
    }

    private static string Normalize(string token)
    {
        if (Aliases.TryGetValue(token, out var alias)) return alias;
        return ModifierFamilies.Keys.Concat(Families).Concat(StandardKeys)
            .FirstOrDefault(name => string.Equals(name, token, StringComparison.OrdinalIgnoreCase));
    }

    internal string Label => string.Join("+", Tokens);
    internal IEnumerable<string> Tokens => Trigger == null ? Modifiers : Modifiers.Append(Trigger);

    internal bool UsesSystem => Trigger == "Print" || Modifiers.Any(key => Family(key) == "Super") ||
        Modifiers.Any(key => Family(key) == "Alt") &&
        (Trigger is "F4" or "Tab" or "Esc" or "Space" || Modifiers.Any(key => Family(key) == "Ctrl"));

    internal KeyChord Substitute()
    {
        string fallback = Trigger switch
        {
            "Print" => "Ctrl+Shift+P",
            "F4" => "Alt+J",
            "Up" => "Ctrl+Shift+Up",
            "T" => "Ctrl+Shift+T",
            _ => "Ctrl+Shift+F12"
        };
        TryParse(fallback, out var chord);
        return chord;
    }

    internal bool FitsModifiers(IEnumerable<string> held)
    {
        var active = held.Where(IsModifier).ToArray();
        return active.Length == Modifiers.Length && Modifiers.All(expected => active.Any(key => Fits(expected, key)));
    }
}

internal sealed record Gesture(KeyChord[] Chords)
{
    internal string Label => string.Join(" → ", Chords.Select(chord => chord.Label));

    internal static bool TryParse(string text, out Gesture gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // The spaced arrow is a sequence separator. Unspaced arrows are also valid arrow-key names.
        var parts = text.Trim().Replace(" -> ", " → ").Split(" → ", StringSplitOptions.TrimEntries);
        if (parts.Length > 6) return false;
        var chords = new KeyChord[parts.Length];
        for (int index = 0; index < parts.Length; index++)
            if (!KeyChord.TryParse(parts[index], out chords[index])) return false;
        gesture = new Gesture(chords);
        return true;
    }

    internal static Gesture Parse(string text) => TryParse(text, out var gesture) ? gesture
        : throw new ArgumentException("Укажите сочетание, например RCtrl+Shift+V. Шаги можно разделить через « → ».");
}

internal enum StrokeOutcome { Pending, Correct, Error }

internal sealed class GestureTracker
{
    private readonly HashSet<string> held = [];
    private bool matched, invalid, hasInput, suppressUntilTriggerReleased;
    private double reaction, started;
    internal string LastKey { get; private set; }
    internal string ErrorKey { get; private set; }
    internal int ReactionMs => (int)Math.Clamp(reaction - started, 1, 60000);
    internal IReadOnlyCollection<string> Held => held;

    internal void Reset(double now, bool keepHeld = false)
    {
        if (!keepHeld) held.Clear();
        matched = invalid = hasInput = false;
        suppressUntilTriggerReleased = keepHeld && held.Any(key => !KeyChord.IsModifier(key));
        started = reaction = now;
        LastKey = ErrorKey = null;
    }

    internal StrokeOutcome Change(KeyChord chord, string key, bool pressed, bool characterMatches, double now)
    {
        if (pressed)
        {
            if (!held.Add(key)) return StrokeOutcome.Pending;
            LastKey = key;
            if (suppressUntilTriggerReleased) return StrokeOutcome.Pending;
            hasInput = true;
            if (matched)
                Invalidate(key);
            else if (KeyChord.IsModifier(key))
            {
                if (!chord.Modifiers.Any(expected => KeyChord.Fits(expected, key)) ||
                    held.Count(active => KeyChord.Family(active) == KeyChord.Family(key)) > 1)
                    Invalidate(key);
                if (!invalid && chord.Trigger == null && chord.FitsModifiers(held))
                    Match(now);
            }
            else if ((key == chord.Trigger || chord.AcceptsNavigationKeypad(key, held)) &&
                characterMatches && chord.FitsModifiers(held) &&
                held.All(active => active == key || KeyChord.IsModifier(active)) && !invalid)
                Match(now);
            else
                Invalidate(key);
            if (matched && !invalid) return StrokeOutcome.Correct;
            return invalid && !KeyChord.IsModifier(key) ? StrokeOutcome.Error : StrokeOutcome.Pending;
        }

        if (!held.Remove(key)) return StrokeOutcome.Pending;
        if (suppressUntilTriggerReleased)
        {
            if (held.All(KeyChord.IsModifier)) suppressUntilTriggerReleased = false;
            return StrokeOutcome.Pending;
        }
        if (!hasInput) return StrokeOutcome.Pending;
        if (!matched && KeyChord.IsModifier(key)) Invalidate(key);
        if (held.Count > 0) return StrokeOutcome.Pending;
        return matched && !invalid ? StrokeOutcome.Correct : StrokeOutcome.Error;
    }

    private void Invalidate(string key)
    {
        invalid = true;
        ErrorKey ??= key;
    }

    private void Match(double now)
    {
        reaction = now;
        matched = true;
    }
}
