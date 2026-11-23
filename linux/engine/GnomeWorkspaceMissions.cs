using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal string GnomePractice = "";
    internal string[] DesktopProfile = [];
    internal bool ReplayLegacyDesktopBindings;
    internal string[] SwitchChoices = [];

    // Capture a normalized, supported single chord per exercise. The package
    // never changes the host's settings. A stored draft retains its key map.
    internal static readonly string[] GnomeProfileActions =
        ["workspace-next", "workspace-prev", "move-next", "move-prev", "switch-next", "switch-prev",
         "switch-group-next", "switch-group-prev"];

    internal static string[] NormalizeDesktopProfile(IEnumerable<KeyValuePair<string, string>> source)
    {
        var result = new List<string>();
        var gestures = new List<KeyChord>();
        var entries = source.ToDictionary(item => item.Key, item => item.Value);
        for (int i = 0; i < GnomeProfileActions.Length; i++)
        {
            string id = GnomeProfileActions[i];
            if (!entries.TryGetValue(id, out var raw) || raw?.Length > 80 ||
                !Gesture.TryParse(raw, out var gesture) || gesture.Chords.Length != 1) continue;
            var chord = gesture.Chords[0];
            // No chord sequences, modifier-only gestures, or unmodified
            // data-entry keys. Desktop-global keys have at least one Ctrl,
            // Alt or Super; Shift on its own cannot initiate a switcher.
            if (chord.Trigger == null || !chord.Modifiers.Any(key => KeyChord.Family(key) is "Ctrl" or "Alt" or "Super") ||
                gestures.Any(existing => DesktopChordsOverlap(existing, chord))) continue;
            gestures.Add(chord);
            result.Add(id + "=" + chord.Label);
        }
        return [.. result];
    }

    // A generic modifier can match either real side; different labels therefore
    // can describe the same physical gesture (e.g. Super+F1 and LSuper+F1).
    private static bool DesktopChordsOverlap(KeyChord first, KeyChord second) =>
        first.Trigger == second.Trigger && first.Modifiers.Length == second.Modifiers.Length &&
        first.Modifiers.Zip(second.Modifiers).All(pair =>
            KeyChord.Fits(pair.First, pair.Second) || KeyChord.Fits(pair.Second, pair.First));

    internal static string[] ReadSavedDesktopProfile(IEnumerable<string> source)
    {
        // Older drafts store these entries directly in XML. Keep the first
        // usable binding for each ID, without reprioritizing different IDs.
        var first = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var entry in source)
        {
            int separator = entry?.IndexOf('=') ?? -1;
            if (separator < 1 || entry.Length - separator > 81) continue;
            string id = entry[..separator];
            if (!GnomeProfileActions.Contains(id) || !Gesture.TryParse(entry[(separator + 1)..], out var gesture) ||
                gesture.Chords.Length != 1 || gesture.Chords[0].Trigger == null ||
                !gesture.Chords[0].Modifiers.Any(key => KeyChord.Family(key) is "Ctrl" or "Alt" or "Super") ||
                !first.Add(id)) continue;
            result.Add(id + "=" + gesture.Chords[0].Label);
        }
        return [.. result];
    }

    internal LabTask GnomeProfileBinding(LabTask task)
    {
        if (GnomePractice is not ("workspaces" or "same-app") || !GnomeProfileActions.Contains(task.Id)) return task;
        if (DesktopProfile.Length == 0)
            return task with { Hint = task.Hint + " Используется учебная привязка по умолчанию." };

        var bindings = DesktopProfile.ToDictionary(entry => entry[..entry.IndexOf('=')],
            entry => Gesture.Parse(entry[(entry.IndexOf('=') + 1)..]).Chords[0]);
        if (bindings.TryGetValue(task.Id, out var binding))
            return task with { Keys = binding.Label, Hint = task.Hint + " Привязка из профиля GNOME." };

        bool DefaultCollides(LabTask item) =>
            bindings.Values.Any(imported => DesktopChordsOverlap(imported, Gesture.Parse(item.Keys).Chords[0]));
        if (ReplayLegacyDesktopBindings || !DefaultCollides(task))
            return task with { Hint = task.Hint + " Используется учебная привязка по умолчанию." };

        // Allocate consistently from the frozen imported profile, irrespective of
        // which window is active or which task is asking for its binding.
        var reserved = LabCatalog.Shortcuts.Where(item => Gesture.TryParse(item.Keys, out _))
            .SelectMany(item => Gesture.Parse(item.Keys).Chords).Concat(bindings.Values).ToList();
        if (Gesture.TryParse(LayoutKey, out var layout)) reserved.AddRange(layout.Chords);
        // First choose familiar function keys, then other normal triggers for
        // headroom. No Ctrl+Alt+Fn candidates (can be intercepted for VT switch).
        var triggers = Enumerable.Range(1, 12).Select(index => "F" + index)
            .Concat(KeyChord.StandardKeys.Where(key => key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])));
        var candidates = new Queue<KeyChord>(triggers.Select(key => Gesture.Parse("Ctrl+Shift+" + key).Chords[0])
            .Where(candidate => !reserved.Any(existing => DesktopChordsOverlap(existing, candidate))));
        var conflicted = LabCatalog.Shortcuts.Where(item => GnomeProfileActions.Contains(item.Id) &&
            !bindings.ContainsKey(item.Id) && DefaultCollides(item));
        foreach (var item in conflicted)
        {
            if (!candidates.TryDequeue(out var alternate)) break;
            if (item.Id == task.Id)
                return task with { Keys = alternate.Label, Hint = task.Hint +
                    " Обычная учебная комбинация занята в профиле GNOME. Это сочетание работает только в тренажёре." };
        }
        // Defensive handling if a future catalog takes every training chord.
        return task with { Hint = task.Hint + " Свободная учебная комбинация не найдена. Выберите другое упражнение в каталоге." };
    }

    internal void PrepareGnomeWorkspaces()
    {
        PrepareGnomeCode("workspaces");
        Open("code");
        Open("terminal");
    }

    internal void PrepareGnomeSameApp()
    {
        PrepareGnomeCode("same-app");
        var editor = Open("code");
        editor.Title = "VS Code: app.py";
        Open("chrome"); // a different app for the normal switcher, not the group
        var notes = Open("code");
        notes.ActiveTab = 1;
        notes.Cursor = Math.Min(4, notes.Body.Length);
        notes.Title = "VS Code: README.md";
    }

    internal void PrepareMateBook()
    {
        GnomePractice = "matebook";
        if (!Gesture.TryParse(LayoutKey, out var gesture) || gesture.Chords.Length != 1 ||
            gesture.Chords[0].Trigger == null)
            LayoutKey = "Super+Space"; // modifier-only and composed sequences are not simulated
    }

    private void PrepareGnomeCode(string practice)
    {
        GnomePractice = practice;
        GnomeWindows = true;
        Workspace = 0;
        WorkspaceCount = 2;
        const string file = "/home/student/project/app.py";
        Terminal.StoreFile(file, GnomeCodeBody);
        originalFiles[file] = GnomeCodeBody;
    }

    internal bool GoGnomeWorkspace(int delta, LabWindow move = null)
    {
        int target = Workspace + delta;
        if (target < 0 || target >= WorkspaceCount) return false;
        Workspace = target;
        ActiveWindow = null;
        if (move != null) move.Workspace = target;
        Activate(move ?? Windows.Where(w => w.Workspace == target && w.State != "minimized").MaxBy(w => w.Activation));
        Version++;
        return true;
    }

    internal int GnomeGroupSize => Active == null ? 0 : Windows.Count(w => w.Workspace == Workspace && w.App == Active.App);

    internal object GnomeScopeEvidence(LabTask task, string equivalentCommand)
    {
        var editor = Active?.App == "code" ? Active : null;
        return new
        {
            Workspace, ActiveWindow,
            layout = task.Action == "layout" ? Layout : null,
            windows = Windows.Select(w => new { w.Id, w.App, w.Workspace, w.State }).ToArray(),
            editor = editor == null ? null : new { editor.Url, editor.ActiveTab, editor.Cursor, editor.Anchor, editor.Body, editor.Tab.SavedBody },
            command = task.Action != "command" ? null : new
            {
                value = equivalentCommand ?? Terminal.LastCommand,
                Terminal.LastSuccess, Terminal.LastOutput, Terminal.Cwd
            }
        };
    }
}
