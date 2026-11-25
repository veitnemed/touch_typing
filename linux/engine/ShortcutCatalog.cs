using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record Skill(string Id, string Group, string Title, string Keys, int Tier = 0,
    string Action = "keys", string Note = "", string Character = "", string Fingers = "", bool BuiltIn = true,
    bool Hidden = false)
{
    internal Gesture Gesture => Keys.Length == 0 ? null : Gesture.Parse(Keys);
}

internal sealed record GuideKey(string Token, string Key, int Finger);

internal static class ShortcutCatalog
{
    internal const string Terminal = "Терминал";
    internal const string Super = "Проблемы с ⊞";
    internal const string Tabs = "Вкладки";
    internal const string Desktop = "Окна и система";
    internal const string Modifiers = "Модификаторы";
    internal const string Digits = "Цифровой ряд";
    internal const string Symbols = "Спецсимволы";

    internal static Skill[] All(StudioProfile profile, string layout, string forward, string backward)
    {
        var builtins = new List<Skill>
        {
            new("terminal-paste", Terminal, "Вставить в терминал", "Ctrl+Shift+V", 0, "paste",
                "Удерживайте Ctrl и Shift и нажмите физическую V (М). Для повтора можно отпускать только V."),
            new("terminal-copy", Terminal, "Скопировать из терминала", "Ctrl+Shift+C", 0, "copy"),
            new("terminal-cut", Terminal, "Вырезать выделение", "Ctrl+Shift+X", 0, "cut", "Работает в учебной сцене."),
            new("terminal-right-paste", Terminal, "Вставить правым Ctrl", "RCtrl+Shift+V", 1, "paste"),
            new("terminal-right-copy", Terminal, "Скопировать правым Ctrl", "RCtrl+Shift+C", 1, "copy"),
            new("terminal-right-cut", Terminal, "Вырезать правым Ctrl", "RCtrl+Shift+X", 1, "cut"),
            new("terminal-transfer", Terminal, "Перенести строку", "Ctrl+Shift+X → Ctrl+Shift+V", 2, "transfer"),
            new("super-left", Super, "Нащупать ⊞ слева", "LSuper", 0, "keys",
                "Отличайте ⊞ от Alt и Z по положению в нижнем ряду. Попробуйте разные пальцы и сохраните удобный вариант."),
            new("layout-forward", Super, "Сменить раскладку", forward, 0, "layout",
                "Дважды нажмите физическую A: до и после системного переключения. Тренажёр сравнит символы."),
            new("layout-back", Super, "Сменить раскладку назад", backward, 1, "layout-back",
                "Начните с физической A, переключите раскладку и снова нажмите A. Набор останется в приложении."),
            new("ctrl-right", Modifiers, "Правый Ctrl", "RCtrl", 0, "keys",
                "Попробуйте правый мизинец или безымянный. В личной копии можно выбрать палец."),
            new("ctrl-left", Modifiers, "Левый Ctrl", "LCtrl", 0, "keys"),
            new("shift-left", Modifiers, "Левый Shift", "LShift", 0, "keys"),
            new("shift-right", Modifiers, "Правый Shift", "RShift", 0, "keys"),
            new("alt-left", Modifiers, "Левый Alt", "LAlt", 0, "keys"),
            new("alt-right", Modifiers, "Правый Alt", "RAlt", 0, "keys", "На некоторых раскладках обозначается AltGr."),
            new("modifiers-pair", Modifiers, "Удержать два модификатора", "RCtrl+LShift", 1, "keys"),
            new("tab-new", Tabs, "Открыть вкладку", "Ctrl+T", 0, "tab-new"),
            new("tab-close", Tabs, "Закрыть вкладку", "Ctrl+W", 0, "tab-close"),
            new("tab-next", Tabs, "Следующая вкладка", "Ctrl+Tab", 0, "tab-next"),
            new("tab-prev", Tabs, "Предыдущая вкладка", "Ctrl+Shift+Tab", 1, "tab-prev"),
            new("tab-cycle", Tabs, "Открыть вкладку и вернуться", "Ctrl+T → Ctrl+Shift+Tab", 2, "tab-cycle"),
            new("window-close", Desktop, "Закрыть учебное окно", "Alt+F4", 1, "window-close",
                "Если F4 управляет яркостью или звуком, используйте Fn + F4."),
            new("window-max", Desktop, "Развернуть учебное окно", "Super+Up", 1, "window-max"),
            new("window-full", Desktop, "Полноэкранный режим", "F11", 0, "window-full",
                "Если F11 выполняет мультимедийное действие, используйте Fn + F11."),
            new("terminal-open", Desktop, "Открыть учебный терминал", "Ctrl+Alt+T", 1, "terminal-open"),
            new("screenshot", Desktop, "Сделать учебный снимок", "Print", 1, "screenshot",
                "Print может называться PrtSc. На части ноутбуков требуется Fn."),
            new("screenshot-copy", Desktop, "Снимок и буфер обмена", "Print → Ctrl+C", 2, "screenshot-copy",
                "Захватите учебную область и скопируйте изображение. Тренажёр сверит свежий снимок в учебном буфере."),
            new("reboot", Desktop, "Ввести команду reboot", "", 1, "reboot",
                "Наберите reboot и нажмите Enter в учебной консоли.")
        };
        string digitOrder = "3478562910";
        foreach (char digit in digitOrder)
            builtins.Add(new($"digit-{digit}", Digits, $"Цифра {digit}", digit.ToString(), 0, "digit"));
        foreach (string digits in new[] { "40", "93", "26", "15", "87", "907", "382", "145", "0692", "3758" })
            builtins.Add(new($"digits-{digits}", Digits, $"Число {digits}", string.Join(" → ", digits.Select(c => c.ToString())),
                digits.Length == 2 ? 1 : 2, "digits"));

        string desired = layout == "RU" ? "!\"№;%:?*()-_+=/\\,." : "!@#$%^&*()-_+=[]{}/\\|:;\"',.<>?`~";
        for (int index = 0; index < desired.Length; index++)
        {
            char item = desired[index];
            var hint = TypingKeyboard.Hint(item, layout == "RU" ? Layout.Russian : Layout.English);
            if (hint == null || CodeText.LanguageName(hint.Layout) != layout) continue;
            string keys = KeyName(hint.Cap);
            if (hint.Modifier != LinuxKey.None) keys = "Shift+" + keys;
            builtins.Add(new($"symbol-{layout}-{(int)item}", Symbols, item.ToString(), keys, index < 12 ? 0 : 1,
                "symbol", $"Печатайте в раскладке {layout}.", item.ToString()));
        }

        var saved = profile.Shortcuts.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return builtins.Select(skill => saved.TryGetValue(skill.Id, out var item) ? skill with { Hidden = item.Hidden } : skill)
            .Concat(profile.Shortcuts.Where(item => item.Id.StartsWith("own/"))
                .Select(item => new Skill(item.Id, item.Group, item.Title, item.Gesture, Fingers: item.Fingers,
                    BuiltIn: false, Hidden: item.Hidden))).ToArray();
    }

    internal static string KeyName(Cap cap) => cap.Key switch
    {
        LinuxKey.Back => "Backspace", LinuxKey.Tab => "Tab", LinuxKey.Return => "Enter", LinuxKey.Space => "Space",
        LinuxKey.CapsLock => "CapsLock", LinuxKey.LeftShift => "LShift", LinuxKey.RightShift => "RShift",
        LinuxKey.LeftCtrl => "LCtrl", LinuxKey.RightCtrl => "RCtrl", LinuxKey.LeftAlt => "LAlt", LinuxKey.RightAlt => "RAlt",
        LinuxKey.LWin => "LSuper", LinuxKey.RWin => "RSuper", LinuxKey.Left => "Left", LinuxKey.Right => "Right",
        LinuxKey.Up => "Up", LinuxKey.Down => "Down", _ => cap.English.ToUpperInvariant()
    };

    // Suggestions do not change the authored gesture or its recognition. Pick
    // the whole chord together so two simultaneous keys never share a finger.
    internal static GuideKey[] Guide(KeyChord chord, Dictionary<string, int> overrides, bool paired, bool rightAltAvailable)
    {
        overrides ??= [];
        var tokens = chord.Tokens.ToArray();
        int triggerFinger = chord.Trigger == null ? 3 : overrides.GetValueOrDefault(chord.Trigger, Finger(chord.Trigger));
        bool onRight = PairOnRight(chord, overrides, paired);
        var choices = tokens.Select(token => Options(token).ToArray()).ToArray();
        var chosen = new GuideKey[tokens.Length];
        GuideKey[] winner = null;
        int best = int.MaxValue;
        // First reserve the main key's normal typing finger, then choose fingers
        // and sides for modifiers. A fallback finger is only needed on conflict.
        int[] order = chord.Trigger == null ? Enumerable.Range(0, tokens.Length).ToArray() :
            [tokens.Length - 1, .. Enumerable.Range(0, tokens.Length - 1)];
        Pick(0, 0, 0);
        return winner;

        void Pick(int at, int used, int cost)
        {
            if (cost >= best) return;
            if (at == order.Length) { best = cost; winner = (GuideKey[])chosen.Clone(); return; }
            int slot = order[at];
            foreach (var (key, finger, penalty) in choices[slot])
            {
                int bit = finger < 0 ? 0 : (1 << finger);
                if ((used & bit) != 0) continue;
                chosen[slot] = new(tokens[slot], key, finger);
                Pick(at + 1, used | bit, cost + penalty);
            }
        }

        IEnumerable<(string Key, int Finger, int Cost)> Options(string token)
        {
            string[] sides;
            if (!KeyChord.Families.Contains(token)) sides = [token];
            else
            {
                sides = KeyChord.ModifierFamilies.Where(pair => pair.Value == token &&
                    (pair.Key != "RAlt" || rightAltAvailable)).Select(pair => pair.Key).ToArray();
                string preferred = token switch
                {
                    "Ctrl" when triggerFinger == 0 || chord.Modifiers.Any(item => KeyChord.Family(item) == "Shift") && LeftHand(triggerFinger) => "RCtrl",
                    "Alt" when rightAltAvailable && chord.Trigger != null && LeftHand(triggerFinger) => "RAlt",
                    "Shift" when chord.Modifiers.Any(item => KeyChord.Family(item) == "Ctrl") && LeftHand(triggerFinger) => onRight ? "RShift" : "LShift",
                    "Shift" => LeftHand(triggerFinger) ? "RShift" : "LShift",
                    _ => sides[0]
                };
                // A custom side remains explicit whenever it is physically possible.
                if (sides.Count(overrides.ContainsKey) == 1) preferred = sides.Single(overrides.ContainsKey);
                sides = sides.OrderBy(side => side == preferred ? 0 : 1).ToArray();
                if (token == "Super" && sides[0] != "RSuper") sides = [sides[0]];
            }
            int sidePenalty = 0;
            foreach (string key in sides)
            {
                int primary = key == "RCtrl" && onRight ? 6 : Finger(key);
                int[] alternate = key switch {
                    "LCtrl" or "LShift" => [0, 1, 2], "RCtrl" or "RShift" => [7, 6, 5],
                    "LSuper" => [8, 1, 0], "RSuper" => [7, 9, 6], "Space" => [8, 9], _ => [primary]
                };
                int[] fingers = [overrides.GetValueOrDefault(key, primary), primary, .. alternate];
                int fingerPenalty = 0;
                foreach (int finger in fingers.Distinct())
                {
                    yield return (key, finger, sidePenalty + fingerPenalty);
                    fingerPenalty += overrides.ContainsKey(key) ? 10 : 3;
                }
                sidePenalty += 2;
            }
            // An overconstrained custom chord may still be rehearsed, but it
            // must not display two assignments for an impossible finger.
            yield return (sides[0], -1, chord.Trigger == token ? 2000 : 1000);
        }
    }

    private static bool LeftHand(int finger) => finger < 4 || finger == 8;
    internal static string AltGuideNote(KeyChord chord, string rightAltMode) =>
        rightAltMode == "alt" || !chord.Modifiers.Any(token => KeyChord.Family(token) == "Alt") ? "" :
        chord.Modifiers.Contains("RAlt") ? chord.Trigger == null ?
            "Здесь проверяется только физическое положение. Правый Alt может быть подписан AltGr." :
            "Правый Alt не подтверждён как обычный Alt. Для этого сочетания выберите раскладку с обычным правым Alt." :
        chord.Modifiers.Contains("Alt") ? rightAltMode == "altgr" ?
            "Справа AltGr: он вводит дополнительные символы, а не заменяет Alt. Для команды используйте левый Alt." :
            "Роль правого Alt в этой раскладке не подтверждена. Для команды используйте левый Alt." : "";

    internal static bool PairOnRight(KeyChord chord, Dictionary<string, int> overrides, bool paired) =>
        paired && chord.Trigger is ("C" or "V" or "X") && chord.Modifiers.Contains("Shift") &&
        chord.Modifiers.Any(token => token == "RCtrl" || token == "Ctrl" && !overrides.ContainsKey("LCtrl")) &&
        !overrides.ContainsKey("LShift");

    internal static int Finger(string key)
    {
        if (key == "RSuper") return 7;
        if (key is "LSuper" or "LAlt" or "Space") return 8;
        var cap = TypingKeyboard.Rows.SelectMany(row => row).FirstOrDefault(candidate => KeyName(candidate) == key);
        if (cap != null) return cap.Finger;
        return key switch
        {
            "Esc" or "F1" or "Tab" or "CapsLock" => 0,
            "F2" => 1,
            "F3" => 2,
            "F4" or "F5" or "F6" => 3,
            "F7" or "F8" => 4,
            "F9" => 5,
            "F10" => 6,
            _ => 7
        };
    }
}
