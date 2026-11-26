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
            case "new-tab":
            case "refresh":
            case "downloads":
            case "bookmark":
                return slot == -1 && Apply(action);
            case "page-search":
                if (slot!=-1 || !(w.Url==SearchHome || w.Url=="about:blank" || IsSearchResult(w.Url))) return false;
                w.Focus="url"; w.Input=IsSearchResult(w.Url) ? SearchQuery(w.Url) : ""; w.Select(0,w.Input.Length);
                Version++; return true;
            case "search-result":
                if (!IsSearchResult(w.Url) || (uint)slot >= (uint)Entries(w).Length) return false;
                w.Focus="body"; w.Item=slot; return Apply("item-open");
            case "home":
                if (slot != -1 || w.Url == SearchHome) return false;
                Navigate(w, SearchHome); // local training site, never a host-browser navigation
                return w.Url == SearchHome;
            case "close-tab":
                if ((uint)slot >= (uint)w.Tabs.Count || w.Tabs.Count <= 1) return false;
                if (slot == w.ActiveTab) return Apply("close-tab");
                // Closing a background tab must not steal keyboard focus or destroy
                // a draft in the address field of the still-visible document.
                w.ClosedTabs.Push(w.Tabs[slot]); w.Tabs.RemoveAt(slot);
                if (slot < w.ActiveTab) w.ActiveTab--;
                Version++; return true;
            case "back": case "forward":
                if (slot != -1 || !Apply(action)) return false;
                w.Focus = "body";
                return true;
            default: return false;
        }
    }
}
