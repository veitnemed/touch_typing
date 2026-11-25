namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    // A pointer is a navigation aid, never evidence that a shortcut was pressed.
    // Window IDs and tab positions (rather than random per-process tab GUIDs)
    // are replayable when restoring a saved classroom session.
    internal bool ChromeSurfacePointer(string owner, string action, int slot)
    {
        var w = Active;
        if (w?.App != "chrome" || w.Id != owner || w.Tab == null || Overview || Switcher ||
            Screenshot || ShowDesktop || DesktopOverlay.Length > 0 || w.Focus == "tab-search") return false;
        switch (action)
        {
            case "address":
                if (slot != -1 || w.Focus == "url") return false;
                return Apply("url");
            case "tab":
                if ((uint)slot >= (uint)w.Tabs.Count || w.ActiveTab == slot) return false;
                w.ActiveTab = slot; w.Focus = "body"; w.Find = ""; w.Item = -1; w.Status = "Готово";
                Version++; return true;
            case "back":
                if (slot != -1 || w.Tab.HistoryIndex <= 0 || !Apply("back")) return false;
                w.Focus = "body";
                return true;
            default: return false;
        }
    }
}
