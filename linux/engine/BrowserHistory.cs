using System;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal const string ChromeHistory = "chrome://history";
    internal bool ReplayLegacyChromeHistory;

    internal static bool OnChromeHistory(LabWindow w) => w?.App == "chrome" && w.Url == ChromeHistory;

    private LabEntry[] HistoryEntries(LabWindow w)
    {
        var visited = recentUrls.Where(url => !url.StartsWith("chrome:", StringComparison.Ordinal)).Select(PageEntry);
        string[] query = w.HistorySearch.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return (query.Length == 0 ? visited : visited.Where(entry => query.All(word =>
            entry.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            entry.Path.Contains(word, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }

    private bool FocusChromeHistory(LabWindow w, bool clear = false, bool selectAll = false)
    {
        if (!OnChromeHistory(w)) return false;
        w.Focus = "history"; w.Item = -1; w.PromptHistorySelection = false;
        if (clear) w.Input = "";
        if (selectAll) w.Select(0, w.Input.Length);
        else w.Cursor = w.Input.Length;
        Version++;
        return true;
    }

    internal void FinishLegacyChromeHistory()
    {
        ReplayLegacyChromeHistory = false;
        if (OnChromeHistory(Active) && Active.Focus == "body")
        {
            var w = Active; int item = w.Item;
            FocusChromeHistory(w);
            w.Item = item;
        }
    }

    internal bool BrowserHistoryPointer(string action, string path)
    {
        var w = Active;
        if (!OnChromeHistory(w) || Overview || Switcher || ShowDesktop || DesktopOverlay.Length > 0) return false;
        if (action == "search") return FocusChromeHistory(w);
        if (action != "entry") return false;
        int index = Array.FindIndex(Entries(w), item => item.Path == path);
        if (index < 0) return false;
        w.Focus = "history"; w.Item = index; Version++;
        return true;
    }
}
