using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// Authored, deterministic course fixtures. No browser, Git process, host
// filesystem, network request, or third-party page code is involved.
internal sealed partial class LabScene
{
    internal const string SearchHome = "https://www.google.com/";
    internal const string RepositoryPage = "https://github.com/veitnemed/touch_typing";
    internal const string SearchDecoy = "https://practice.local/tutorials/touch-typing";
    internal const string CloneRoot = "/home/student/projects/touch_typing";
    internal const string CloneReadme = CloneRoot + "/README.md";
    internal const string CloneReadmeBody = "# TouchTyping\n\nУчебная копия проекта.\nРазделы: «Печать» и Live.\n";
    private const string ResultsPrefix = "https://www.google.com/search?q=";
    internal bool BrowserPracticeActive;
    internal bool RepoCloned;
    internal string BrowserPracticeQuery = "touch_typing github";

    internal void PrepareBrowserPractice()
    {
        BrowserPracticeActive = true;
        var chrome = Active?.App == "chrome" ? Active : Open("chrome");
        chrome.ActiveTab = 0;
        var tab = chrome.Tab;
        tab.History.Clear();
        tab.History.Add(SearchHome);
        tab.HistoryIndex = 0;
        SetPage(tab, SearchHome);
        chrome.Focus = "body";
        chrome.Item = -1;
        chrome.SavedBookmarks.Add("https://docs.python.org/3/");
        // Keep the expected repository below another real choice in bookmarks.
        chrome.SavedBookmarks.Add("https://github.com/other/typing-demo");
    }

    internal static bool IsSearchResult(string url) => url.StartsWith(ResultsPrefix, StringComparison.Ordinal);
    internal static string SearchQuery(string url) => Uri.UnescapeDataString(url[ResultsPrefix.Length..]);
    private static string SearchAddress(string query) => ResultsPrefix + Uri.EscapeDataString(query);

    private string PracticeAddress(string input)
    {
        // A bare domain can still be opened as a page. Phrases are queries,
        // including phrases such as "github.com touch typing".
        if (input.Contains("://", StringComparison.Ordinal) || input == "about:blank") return input;
        return input.Contains('.') && !input.Any(char.IsWhiteSpace) ? "https://" + input : SearchAddress(input);
    }

    private static (string Title, string Body)? BrowserPracticePage(string url)
    {
        if (url == SearchHome) return ("Google", "Учебный поиск без интернета");
        if (IsSearchResult(url)) return ("Google · Результаты", SearchQuery(url));
        if (url == RepositoryPage) return ("veitnemed / touch_typing", "GitHub · репозиторий\nКод вслепую · Учебный тренажёр с режимами «Печать» и Live.");
        if (url == SearchDecoy) return ("Статьи о слепой печати", "Здесь собраны советы. Исходников тренажёра здесь нет.");
        if (url == "https://github.com/other/typing-demo") return ("Учебный пример typing-demo", "Это другой репозиторий.");
        return ResearchPage(url);
    }

    private LabEntry[] PracticeSearchEntries(string url)
    {
        string query = SearchQuery(url);
        var catalog = new (string Title, string Path, string Keywords)[]
        {
            ("Статьи о слепой печати", SearchDecoy, "touch typing touch_typing blind keyboard печать github veitnemed"),
            ("veitnemed/touch_typing · GitHub", RepositoryPage, "touch_typing touch typing veitnemed github репозиторий"),
            ("Документация Python", "https://docs.python.org/3/", "python docs documentation документация"),
        };
        var terms = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (browserResearch != null)
        {
            var research = ResearchCases.Where(item => terms.Length > 0 && terms.All(term =>
                (item.Query + " " + item.Title).Contains(term, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(item =>
                {
                    LabEntry target = new(item.Title, item.Target, "page");
                    LabEntry decoy = new(ResearchDecoyTitle(item), ResearchDecoy(item), "page");
                    return browserCaseVariant == 1 ? new[] { target, decoy } : new[] { decoy, target };
                }).ToArray();
            if (research.Length > 0) return research;
        }
        return catalog.Where(site => terms.Length > 0 && terms.All(term =>
                site.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                site.Path.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(site => new LabEntry(site.Title, site.Path, "page")).ToArray();
    }

    private bool TryPracticeClone(LabWindow window)
    {
        string value = window.Input.Trim();
        if (value != RepositoryPage && value != RepositoryPage + ".git")
        {
            window.Status = "Вставьте ссылку на veitnemed/touch_typing из Chrome.";
            return false;
        }
        if (RepoCloned || Terminal.Exists(CloneRoot)) { window.Status = "Учебная копия уже существует."; return false; }
        Terminal.CreateFolder(CloneRoot);
        Terminal.StoreFile(CloneReadme, CloneReadmeBody);
        Terminal.StoreFile(CloneRoot + "/origin.txt", RepositoryPage + "\n");
        Terminal.StoreFile(CloneRoot + "/practice.txt", "Учебная рабочая копия · скачивание не выполнялось.\n");
        RepoCloned = true;
        foreach (string file in Terminal.Entries(CloneRoot)) originalFiles[file] = Terminal.ReadFile(file);
        window.Input = "";
        window.Focus = "body";
        window.Sidebar = true;
        window.View = "explorer";
        window.Status = "Учебная копия: projects/touch_typing. Откройте README.md.";
        return true;
    }

    private bool? ApplyBrowserPractice(string action, LabWindow window)
    {
        if (!BrowserPracticeActive || window?.App != "code") return null;
        switch (action)
        {
            case "code-clone-command" when window.Focus == "palette" &&
                string.Equals(window.Input.Trim(), "Git: Clone", StringComparison.OrdinalIgnoreCase):
                window.Input = "";
                window.Focus = "clone-url";
                window.Input = "";
                window.Cursor = 0;
                window.Status = "Вставьте HTTPS-ссылку из Chrome. Это учебная копия без сети.";
                return true;
            case "code-clone-confirm" when window.Focus == "clone-url":
                return TryPracticeClone(window);
            case "palette-choose" when window.Focus == "palette":
                window.Status = "В этой миссии выберите Git: Clone.";
                return false;
            default:
                return null;
        }
    }

    internal object BrowserPracticeEvidence(LabTask task, LabWindow window) => !BrowserPracticeActive ? null : task.Action switch
    {
        "code-clone-command" => new { window?.Focus },
        "code-clone-confirm" => new { RepoCloned, file = Terminal.ReadFile(CloneReadme),
            origin = Terminal.ReadFile(CloneRoot + "/origin.txt"), window?.Focus },
        // In this lesson the returned query is encoded in the selected history
        // entry. Also verify the generated results, not just the address.
        "back" or "navigate-pasted" when window?.App == "chrome" =>
            new { window.Url, window.Focus, results = Entries(window).Select(entry => entry.Path).ToArray(), window.Item },
        "research-find" => new { window?.Url, window?.Focus, window?.Find,
            from = window?.Tab?.Position.Anchor, to = window?.Tab?.Position.Caret,
            matched = window?.Status == "Найдено: " + BrowserResearchNeedle },
        "item-open" when browserResearch is { Download.Length: > 0 } =>
            new { window?.App, window?.Url, window?.Focus, window?.Folder, DownloadCount,
                file = Terminal.ReadFile("/home/student/Downloads/" + browserResearch.Download),
                delivered = Terminal.ReadFile(Project + "/" + browserResearch.Download) },
        _ => null
    };
}
