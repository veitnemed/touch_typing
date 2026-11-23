using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal string GnomePractice = "";
    internal string[] DesktopProfile = [];
    internal string[] SwitchChoices = [];

    // Capture a normalized, supported single chord per exercise. The package
    // never changes the host's settings. A stored draft retains its key map.
    internal static readonly string[] GnomeProfileActions =
        ["workspace-next", "workspace-prev", "move-next", "move-prev", "switch-next", "switch-prev",
         "switch-group-next", "switch-group-prev"];

    internal static string[] NormalizeDesktopProfile(IEnumerable<KeyValuePair<string, string>> source)
    {
        var result = new List<string>();
        var gestures = new HashSet<string>();
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
                !gestures.Add(chord.Label)) continue;
            result.Add(id + "=" + chord.Label);
        }
        return [.. result];
    }

    internal LabTask GnomeProfileBinding(LabTask task)
    {
        if (GnomePractice is not ("workspaces" or "same-app") || !GnomeProfileActions.Contains(task.Id)) return task;
        var entry = DesktopProfile.FirstOrDefault(value => value.StartsWith(task.Id + "=", StringComparison.Ordinal));
        var binding = entry?[(task.Id.Length + 1)..];
        return binding == null
            ? task with { Hint = task.Hint + " Используется учебная привязка по умолчанию." }
            : task with { Keys = binding, Hint = task.Hint + " Привязка из профиля GNOME." };
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
