using System;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private sealed record ResearchCase(string Query, string Title, string Topic, string Target, string SectionTitle,
        string Section, string Fact, string Download = "");
    private int browserCaseVariant;
    internal bool BrowserResearchActive => browserResearch != null;
    private ResearchCase browserResearch;

    private static ResearchCase Reference(string name, string slug, string section, string fact) =>
        new("python " + slug, "Документация Python: " + name, name,
            "https://docs.python.org/3/library/" + slug + ".html", section,
            "https://docs.python.org/3/library/" + slug + ".html#" + (slug == "pathlib" ? "pathlib." : "") + section, fact);
    private static ResearchCase ReportCase(string slug, string title, string fact) =>
        new("workshop " + slug, "Мастерская · " + title, title,
            "https://practice.local/workshop/" + slug, "", "", fact, slug + ".csv");
    private static ResearchCase Release(string slug, string app, string before, string after, string change) =>
        new("release " + slug, "Версии · " + app + " " + after, app,
            "https://practice.local/releases/" + slug + "/" + after, "Предыдущий выпуск " + before,
            "https://practice.local/releases/" + slug + "/" + before, change);
    private static ResearchCase Handbook(string slug, string title, string section, string fact) =>
        new("handbook " + slug, "Справочник · " + title, title, "https://practice.local/handbook/" + slug,
            section, "https://practice.local/handbook/" + slug + "#procedure", fact);

    private static readonly ResearchCase[] ReferenceCases = [
        Reference("JSON", "json", "json.dumps", "ensure_ascii=False сохраняет кириллицу читаемой. indent=2 задаёт отступы."),
        Reference("Работа с путями", "pathlib", "Path.read_text", "read_text(encoding='utf-8') возвращает текст файла."),
        Reference("CSV", "csv", "csv.DictReader", "DictReader берёт имена столбцов из первой строки, если они не заданы явно.")
    ];
    private static readonly ResearchCase[] ReportCases = [
        ReportCase("inventory", "Остатки на складе", "sku,quantity\nKB-04,18\nUSB-C,32"),
        ReportCase("shipments", "Отправки", "dispatch,status\nPK-31,sent\nPK-42,ready"),
        ReportCase("checks", "Проверка комплектов", "kit,result\nST-05,pass\nST-08,recheck")
    ];
    private static readonly ResearchCase[] ReleaseCases = [
        Release("reader", "Учебная читалка", "2.3", "2.4", "Читалка теперь восстанавливает позицию на странице после перезапуска."),
        Release("files", "Учебные файлы", "1.5", "1.6", "При конфликте имён можно сохранить обе копии, не перезаписывая оригинал."),
        Release("notes", "Учебные заметки", "3.1", "3.2", "Поиск научился находить заметки по заголовку и содержимому.")
    ];
    private static readonly ResearchCase[] HandbookCases = [
        Handbook("backup", "Резервная копия", "Проверить копию", "Восстановите один файл в отдельную папку и сравните содержимое."),
        Handbook("handoff", "Передача проекта", "Проверить пакет", "Сверьте файлы с оглавлением и откройте отчёт перед отправкой."),
        Handbook("accounts", "Учёт задач", "Проверить отчёт", "Прежде чем передать сводку, отфильтруйте завершённые задачи и сверьте количество.")
    ];
    private static readonly ResearchCase[] ResearchCases =
        [.. ReferenceCases, .. ReportCases, .. ReleaseCases, .. HandbookCases];

    internal void PrepareBrowserPractice(string family)
    {
        PrepareBrowserPractice();
        browserResearch = (family switch
        {
            "reference" => ReferenceCases, "download" => ReportCases,
            "compare" => ReleaseCases, "recover" => HandbookCases,
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        })[browserCaseVariant];
        BrowserPracticeQuery = browserResearch.Query;
    }

    internal string ResolveBrowserRouteTarget(string value) => browserResearch == null ? value : value switch
    {
        "@target" => browserResearch.Target, "@section" => browserResearch.Section, "@download" => browserResearch.Download, _ => value
    };

    private static string ResearchDecoy(ResearchCase item) => item.Target.TrimEnd('/') + "/overview";
    private static string ResearchDecoyTitle(ResearchCase item) => item.Topic + " · обзор";
    private static (string Title, string Body)? ResearchPage(string url)
    {
        foreach (var item in ResearchCases)
        {
            if (url == item.Target)
                return (item.Title, item.Download.Length > 0
                    ? "Рабочий отчёт · CSV\nВ таблице есть заголовки и две строки с данными.\nСкачайте CSV и откройте локальную копию для сверки."
                    : WrapResearchCopy(item.Fact) + "\n\nСвязанные материалы");
            if (url == item.Section && url.Length > 0)
                return (item.SectionTitle, (item.Target.StartsWith("https://practice.local/releases/", StringComparison.Ordinal)
                        ? "Архивная заметка.\nВ этой версии новой возможности ещё нет."
                        : WrapResearchCopy(item.Fact)));
            if (url == ResearchDecoy(item))
                return (ResearchDecoyTitle(item), "Материал для начинающих.\nЗдесь нет исходной инструкции или рабочего файла.");
        }
        return null;
    }

    private static string WrapResearchCopy(string text)
    {
        const int width = 58;
        var lines = new System.Collections.Generic.List<string>();
        while (text.Length > width)
        {
            int cut = text.LastIndexOf(' ', width);
            if (cut < 1) break;
            lines.Add(text[..cut]);
            text = text[(cut + 1)..];
        }
        lines.Add(text);
        return string.Join("\n", lines);
    }

    private static LabEntry[] BrowserResearchLinks(string url)
    {
        var item = ResearchCases.FirstOrDefault(x => x.Target == url);
        if (item == null) return [];
        return item.Download.Length > 0
            ? [new LabEntry(item.Download, item.Download, "download")]
            : [new LabEntry(item.SectionTitle, item.Section, "page")];
    }

    internal string BrowserResearchNeedle => browserResearch?.Fact.Split('\n').Last().Split(',')[0] ?? "";

    private string BrowserDownloadBody(string name)
    {
        var item=browserResearch ?? (!LegacyOrdinarySearch && Active?.App=="chrome" ?
            ReportCases.FirstOrDefault(c=>c.Target==Active.Url) : null);
        return item!=null && name==item.Download ? item.Fact+"\n" : null;
    }
}
