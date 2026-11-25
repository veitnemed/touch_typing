using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record ChromeSourceReviewReceipt(string Url, string Code);

internal sealed partial class LabScene
{
    private sealed record TabReviewCase(string Slug, string Title, string Code, string Archived, int ReviewSlot)
    {
        internal string CurrentUrl => TabReviewRoot + Slug + "/current";
        internal string ArchiveUrl => TabReviewRoot + Slug + "/archive";
        internal string OverviewUrl => TabReviewRoot + Slug + "/overview";
        internal string ReviewUrl => TabReviewRoot + Slug + "/handoff";
        internal string RequestUrl => TabReviewRoot + Slug + "/request";
    }
    private sealed record TabSearchRow(int Index, string Title, string Url);
    private const string TabReviewRoot = "https://practice.local/tab-review/";
    internal bool ReplayLegacyTabSearch;
    internal int BrowserTabLimit => ReplayLegacyTabSearch ? 6 : 16;
    private const string TabReviewArtifact = Project + "/source-review.txt";
    private const string TabSearchFocus = "tab-search", SourceReviewFocus = "source-review";
    private const string ReviewLabel = "control-code";
    private static readonly TabReviewCase[] TabReviewCases = [
        new("backup", "Backup · Резервная копия", "041", "014", 5),
        new("inventory", "Inventory · Складские остатки", "237", "207", 2),
        new("delivery", "Delivery · Доставка", "802", "820", 8)
    ];
    private TabReviewCase tabReviewCase;
    internal bool ChromeTabReviewActive => tabReviewCase != null;

    internal void PrepareTabReview()
    {
        tabReviewCase = TabReviewCases[browserCaseVariant];
        var c = tabReviewCase;
        Ensure("chrome");
        var w = Active;
        var candidates = new[] { c.ArchiveUrl, c.CurrentUrl, c.OverviewUrl, TabReviewRoot + "glossary",
            TabReviewRoot + "calendar", TabReviewRoot + "support" };
        var ordered = candidates.Skip(browserCaseVariant).Concat(candidates.Take(browserCaseVariant)).ToList();
        ordered.Insert(0, c.RequestUrl);
        ordered.Insert(c.ReviewSlot - 1, c.ReviewUrl);
        w.Tabs.Clear(); w.ClosedTabs.Clear(); w.ActiveTab = 0; w.Focus = "body"; w.Item = -1;
        foreach (string url in ordered)
        {
            var tab = new LabTab("", url, "");
            SetPage(tab, url);
            w.Tabs.Add(tab);
            recentUrls.Remove(url);
            recentUrls.Insert(0, url);
        }
    }

    private static (string Title, string Body)? TabReviewPage(string url)
    {
        if (!url.StartsWith(TabReviewRoot, StringComparison.Ordinal)) return null;
        foreach (var c in TabReviewCases)
        {
            if (url == c.RequestUrl) return ("Запрос на сверку",
                "Открыто восемь рабочих вкладок. Тема: " + c.Title + ".\n\n"
                + "Нужен код действующего регламента, не архива и не обзора.\n"
                + "Найдите нужную вкладку и скопируйте адрес регламента.\n\n"
                + "На вкладке «Лист сверки» внесите трёхзначный код.\n"
                + "Если он начинается с нуля, сохраните и этот ноль.");
            if (url == c.CurrentUrl || url == c.ArchiveUrl)
            {
                bool current = url == c.CurrentUrl;
                return (c.Title + (current ? " · действующий регламент" : " · архив"),
                    (current ? "Действующая редакция · применяется сейчас" : "Архивная редакция · больше не применяется") +
                    "\n\nКод контрольного образца для сверки с журналом:" +
                    "\n\n" + ReviewLabel + ": " + (current ? c.Code : c.Archived) + "\n\nНе переносите код из другой редакции.");
            }
            if (url == c.OverviewUrl) return (c.Title + " · обзор",
                "Краткое описание процесса. Контрольный код следует брать из действующего регламента.");
            if (url == c.ReviewUrl) return ("Лист сверки",
                "Перед отправкой укажите код действующего регламента. Скопированный адрес должен вести именно к нему.");
        }
        return url[(TabReviewRoot.Length)..] switch
        {
            "glossary" => ("Термины проекта", "Словарь для чтения материалов."),
            "calendar" => ("График работы", "На этой неделе сверка выданных инструкций."),
            "support" => ("Контакты поддержки", "В учебном браузере нет внешней отправки."),
            _ => null
        };
    }

    private static TabSearchRow[] TabSearchRows(LabWindow w)
    {
        string[] words = w.Input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return w.Tabs.Select((tab, index) => new TabSearchRow(index, tab.Title, tab.Url))
            .Where(row => words.All(word => row.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                row.Url.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private bool IsReviewPage(LabWindow w) => tabReviewCase != null && w?.Url == tabReviewCase.ReviewUrl;
    private static bool IsTabReviewUrl(string url) => url.StartsWith(TabReviewRoot, StringComparison.Ordinal);
    private LabWindow BrowserForTabOperation => !Overview && !Switcher && !Screenshot && !ShowDesktop &&
        DesktopOverlay.Length == 0 && Active is { App: "chrome" } w ? w : null;
    private static int TabNumber(string action) =>
        action.StartsWith("browser-tab-", StringComparison.Ordinal) && action.Length == "browser-tab-2".Length &&
        action[^1] is >= '2' and <= '8' ? action[^1] - '0' : 0;

    internal bool? ChromeTabActionAvailable(string action)
    {
        int slot = TabNumber(action);
        if (slot == 0 && !action.StartsWith("browser-tabs-", StringComparison.Ordinal) &&
            !action.StartsWith("browser-review-", StringComparison.Ordinal)) return null;
        if (ReplayLegacyTabSearch) return false;
        var w = BrowserForTabOperation;
        if (w == null) return false;
        if (slot != 0) return slot <= w.Tabs.Count;
        return action switch
        {
            "browser-tabs-open" => w.Tabs.Count > 0,
            "browser-tabs-next" or "browser-tabs-prev" or "browser-tabs-choose" => w.Focus == TabSearchFocus,
            "browser-review-read" => tabReviewCase != null && w.Focus == "find",
            "browser-review-focus" => IsReviewPage(w) && w.Focus == "body",
            "browser-review-submit" => IsReviewPage(w) && w.Focus == SourceReviewFocus,
            _ => false
        };
    }

    private bool? ApplyChromeTabAction(string action, LabWindow w)
    {
        bool? available = ChromeTabActionAvailable(action);
        if (available != true) return available;
        int slot = TabNumber(action);
        if (slot > 0) { SelectChromeTab(w, slot - 1); return true; }
        switch (action)
        {
            case "browser-tabs-open":
                if (w.Focus == TabSearchFocus) return CloseTabSearch(w);
                w.TabSearchFrom = w.Focus;
                w.TabSearchFromStatus = w.Status;
                w.Focus = TabSearchFocus; w.Input = ""; w.Cursor = 0;
                w.TabSearchIndex = 0;
                return true;
            case "browser-tabs-next": case "browser-tabs-prev":
                int n = TabSearchRows(w).Length;
                if (n > 0)
                    w.TabSearchIndex = (w.TabSearchIndex + (action == "browser-tabs-next" ? 1 : n - 1)) % n;
                return true;
            case "browser-tabs-choose":
                return ChooseChromeTab(w, w.TabSearchIndex);
            case "browser-review-read":
                w.Find = w.Input;
                FindMatch(w, false);
                return true;
            case "browser-review-focus":
                w.Focus = SourceReviewFocus; w.Select(0, w.Input.Length); return true;
            case "browser-review-submit":
                var c = tabReviewCase;
                if (Clipboard != c.CurrentUrl)
                {
                    w.Status = "Адрес в буфере не ведёт к действующему регламенту. Скопируйте нужную ссылку.";
                    return true;
                }
                string code = w.Input.Trim();
                if (code != c.Code || !Terminal.Exists(Project))
                {
                    w.Status = "Код не совпадает с действующей редакцией. Сверьте три цифры; ноль в начале важен.";
                    return true;
                }
                Terminal.StoreFile(TabReviewArtifact, ReviewReceiptText(c));
                w.Focus = "body";
                w.Status = "Сверка сохранена. Код " + code + " · адрес действующей редакции.";
                return true;
            default: return false;
        }
    }

    private static bool CloseTabSearch(LabWindow w)
    {
        if (w?.Focus != TabSearchFocus) return false;
        w.Focus = w.TabSearchFrom; w.Status = w.TabSearchFromStatus; return true;
    }

    private static void SelectChromeTab(LabWindow w, int index)
    {
        w.ActiveTab = index; w.Focus = "body"; w.Item = -1;
        w.Find = ""; w.Status = "Готово";
    }

    private static bool ChooseChromeTab(LabWindow w, int resultIndex)
    {
        var rows = TabSearchRows(w);
        var target = rows.ElementAtOrDefault(Math.Max(resultIndex, 0));
        if (target == null) { w.Status = "Открытых вкладок по этому запросу нет."; return true; }
        bool narrowed = rows.Length < w.Tabs.Count && w.Input.Any(ch => !char.IsWhiteSpace(ch));
        w.TabSearchSelection = target.Url;
        w.TabSearchNarrowed = narrowed;
        SelectChromeTab(w, target.Index);
        w.Status = "Открытая вкладка: " + target.Title;
        return true;
    }

    private object ChromeTabsSnapshot(LabWindow w) => w.Focus != TabSearchFocus ? null : new
    {
        rows = TabSearchRows(w), selected = w.TabSearchIndex
    };

    private object ChromeReviewSnapshot(LabWindow w) => !IsReviewPage(w) ? null : new
    {
        code = w.ReviewInput,
        copied = Clipboard.StartsWith(TabReviewRoot, StringComparison.Ordinal) ? Clipboard : "",
        saved = Terminal.ReadFile(TabReviewArtifact)
    };

    private static string ReviewReceiptText(TabReviewCase c) =>
        "source=" + c.CurrentUrl + "\n" + ReviewLabel + "=" + c.Code + "\n";

    internal ChromeSourceReviewReceipt TabReviewReceipt() =>
        tabReviewCase is { } c && Terminal.ReadFile(TabReviewArtifact) == ReviewReceiptText(c)
            ? new(c.CurrentUrl, c.Code) : null;

    internal bool BrowserTabPointer(string action, string index)
    {
        var w = BrowserForTabOperation;
        if (w == null) return false;
        if (action == "open") return Apply("browser-tabs-open");
        if (action == "review") return Apply("browser-review-focus");
        if (w.Focus != TabSearchFocus ||
            !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int selected) ||
            selected < 0 || selected >= TabSearchRows(w).Length || action != "select") return false;
        Version++;
        return ChooseChromeTab(w, selected);
    }

    internal LabTask DescribeTabReview(LabTask task)
    {
        if (tabReviewCase is not { } c) return task;
        return task.Action switch
        {
            "browser-tabs-choose" => task with { Title = "Выберите действующий регламент: " + c.Title,
                Hint = "Сузьте список по названию или адресу; стрелки выбирают строку, Enter открывает существующую вкладку." },
            "browser-review-read" => task with { Title = "Найдите поле с контрольным кодом в действующей редакции",
                Text = ReviewLabel, Hint = "Найдите в документе control-code и внимательно прочитайте три цифры рядом." },
            "browser-review-submit" => task with { Title = "Передайте считанный код и адрес действующей редакции",
                Hint = "Введите три цифры в поле и подтвердите Enter. Адрес берётся из учебного буфера." },
            _ when TabNumber(task.Action) > 0 => task with { Title = "Перейдите напрямую к «Листу сверки», вкладка " + c.ReviewSlot },
            _ => task
        };
    }

    internal string ResolveTabReviewToken(string token) => token == "browser-review-tab" && tabReviewCase != null
        ? "browser-tab-" + tabReviewCase.ReviewSlot : token;

    internal void DemonstrateTabReview(LabTask task)
    {
        if (tabReviewCase is not { } c || Active is not { App: "chrome" } w) return;
        if (task.Id == "browser-tabs-choose")
        {
            w.Input = c.Slug;
            w.Cursor = w.Input.Length;
            w.TabSearchIndex = Array.FindIndex(TabSearchRows(w), row => row.Url == c.CurrentUrl);
        }
        else if (task.Id == "browser-review-read") w.Input = ReviewLabel;
        else if (task.Id == "browser-review-submit") w.Input = c.Code;
    }

    internal object ChromeTabEvidence(LabTask task, LabWindow w)
    {
        if (w?.App != "chrome") return null;
        if (TabNumber(task.Action) > 0) return new { w.Focus, w.Url, w.ActiveTab };
        return task.Action switch
        {
            "browser-tabs-open" => new { w.Focus, w.Url },
            "browser-tabs-choose" => new { w.Focus, w.Url, w.ActiveTab, w.TabSearchNarrowed, w.TabSearchSelection },
            "browser-review-read" => new { w.Url, w.Focus, label = w.Find.Equals(ReviewLabel, StringComparison.OrdinalIgnoreCase),
                w.Tab.Position.Anchor, w.Tab.Position.Caret, found = w.Status.StartsWith("Найдено:", StringComparison.Ordinal) },
            "browser-review-focus" => new { w.Focus, w.Url },
            "browser-review-submit" => new { w.Focus, w.Url, artifact = Terminal.ReadFile(TabReviewArtifact) },
            _ => null
        };
    }

    internal string ChromeTabReviewRejection(string action)
    {
        if (!ChromeTabReviewActive || Active is not { App: "chrome" } w) return null;
        return action switch
        {
            "browser-tabs-choose" => w.Focus == TabSearchFocus ? w.Status :
                w.Url != tabReviewCase.CurrentUrl ? "Выберите действующий регламент, не архив и не обзор. Поиск вкладок можно открыть снова." :
                "Документ верный. В этом шаге сначала сузьте список открытых вкладок по теме.",
            "browser-review-read" => w.Url != tabReviewCase.CurrentUrl ? "Поле нужно найти в действующем регламенте." :
                "Найдите строку с контрольным кодом, а не общее упоминание редакции.",
            "browser-review-submit" => w.Status,
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void ChromeTabReviewMissions(List<LiveMission> list)
    {
        // Keep the established level-1 ID for explicit selections and saved drafts.
        // The level-0 alias is supplemental; the ordinary card still starts its core route.
        LivePhase[] phases = [
            new("Вернитесь к действующему документу среди восьми вкладок", "browser-tabs-open browser-tabs-choose"),
            new("Найдите контрольный код в актуальной редакции и скопируйте адрес", "find browser-review-read escape url copy"),
            new("Откройте лист сверки и подтвердите найденные данные", "browser-review-tab browser-review-focus browser-review-submit")
        ];
        foreach (var (level, id) in new[] { (0, "chrome-open-tab-review-basics"), (1, "chrome-open-tab-review") })
            list.Add(new("chrome", level, id, "Найти открытый регламент и передать проверенный код", phases,
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareTabReview()));
    }
}
