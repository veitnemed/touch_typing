using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static readonly Dictionary<string, LivePhase> BrowserParts = new()
    {
        ["method"] = new("Открыть методику со страницы проекта",
            "pick:https://practice.local/spectrum link-open zoom-in bookmark"),
        ["repo"] = new("Найти исходники по ссылке из материалов",
            "pick:https://github.com/veitnemed/piling-main link-open url copy escape"),
        ["docs"] = new("Перейти в справочник Python и сохранить источник",
            "pick:https://docs.python.org/3/ link-open bookmark url copy escape"),
        ["reference"] = new("Открыть копию источника для сравнения",
            "url copy new-tab paste navigate-pasted prev-tab"),
        ["download"] = new("Получить отчёт и проверить загрузку",
            "pick:spectrum-report.txt link-open downloads link-next close-tab"),
        ["shelf"] = new("Проверить закладку в коллекции",
            "bookmark bookmark-manager link-next link-open"),
        ["history"] = new("Восстановить последнюю страницу из истории",
            "history link-next link-open url copy escape"),
        ["draft"] = new("Перенести адрес через учебный буфер",
            "url cut paste navigate-pasted"),
        ["restore"] = new("Вернуть закрытую рабочую вкладку",
            "url copy new-tab paste navigate-pasted close-tab restore-tab"),
        ["present"] = new("Подготовить страницу к показу и вернуться к источникам",
            "fullscreen zoom-in zoom-in zoom-reset fullscreen prev-tab"),
        ["route"] = new("Сверить источник с предыдущей версией страницы",
            "pick:https://practice.local/spectrum link-open back forward url copy escape"),
        ["index"] = new("Собрать справочник и исходники в соседних вкладках",
            "url copy new-tab paste navigate-pasted pick:https://github.com/veitnemed/piling-main link-open prev-tab"),
        ["compare"] = new("Сопоставить текст двух источников",
            "select-all copy next-tab select-all copy prev-tab"),
        ["organize"] = new("Подготовить рабочий набор вкладок к следующему просмотру",
            "last-tab url copy close-tab restore-tab first-tab"),
    };

    private static void BrowserMissions(List<LiveMission> list)
    {
        void Add(int level, string id, string title, params string[] parts) =>
            list.Add(new("chrome", level, "chrome-" + id, title, parts.Select(p => BrowserParts[p]).ToArray()));
        Add(0, "reader", "Подготовить справочник к чтению", "reference","method","download","present","organize");
        Research(0, "docs-reference", "Найти нужный раздел справки", "reference",
            new("Найдите инструкцию по теме в поиске", "url browser-search pick:@target link-open"),
            new("Откройте нужный раздел и передайте его точный адрес", "pick:@section link-open url copy escape"));
        Add(0, "sources", "Собрать адреса первоисточников", "repo","reference","method","docs","compare");
        Add(0, "return", "Восстановить потерянный материал", "method","restore","history","download","organize");
        Research(0, "download-verify", "Скачать и проверить рабочий отчёт", "download",
            new("Найдите страницу отчёта, а не описание инструмента", "url browser-search pick:@target link-open"),
            new("Скачайте файл и найдите его в списке загрузок", "pick:@download link-open downloads pick:@download"),
            new("Откройте загруженный файл и найдите запись для сверки", "link-open find research-find escape"));
        Add(0, "report", "Найти и сохранить отчёт проекта", "repo","download","reference","route","present");
        Add(1, "library", "Собрать коллекцию проекта", "method","shelf","docs","reference","repo","organize");
        Add(1, "handoff", "Передать исследование коллеге", "reference","repo","draft","method","download","organize");
        Research(1, "release-compare", "Сверить изменение между двумя выпусками", "compare",
            new("Найдите обзор последнего выпуска", "url browser-search pick:@target link-open"),
            new("Перейдите к предыдущему выпуску и вернитесь к актуальному", "pick:@section link-open back"),
            new("Сохраните проверенный выпуск и откройте его через менеджер закладок", "bookmark bookmark-manager pick:@target link-open"));
        Add(1, "compare", "Сверить описание метода с исходниками", "index","method","reference","compare","download");
        Add(1, "recovery", "Вернуть исследование после закрытия вкладки", "reference","repo","restore","route","history");
        Add(2, "review", "Подготовить техническое ревью", "route","repo","download","history","compare","present");
        Add(2, "audit", "Проверить происхождение материалов", "index","history","docs","route","download","organize");
        Research(2, "restore-source", "Восстановить рабочий источник из истории", "recover",
            new("Найдите первоисточник и откройте раздел с инструкцией", "url browser-search pick:@target link-open pick:@section link-open"),
            new("Потеряйте вкладку и восстановите нужный раздел по истории", "close-tab history pick:@section link-open"),
            new("Подготовьте ссылку на конкретную инструкцию для передачи", "url copy escape"));
        Add(2, "briefing", "Провести показ и восстановить рабочее место", "repo","reference","method","present","restore","compare");
        Add(2, "collection", "Проверить и восстановить подборку источников", "method","shelf","repo","docs","restore","download","compare");
        list.Add(new("chrome", 0, "chrome-search-clone", "Найти репозиторий и открыть учебную копию",
            [
                new("Найдите исходники тренажёра в учебном Google",
                    "url browser-search pick:" + LabScene.SearchDecoy + " link-open back pick:" + LabScene.RepositoryPage + " link-open"),
                new("Сохраните репозиторий и откройте его из закладок",
                    "bookmark new-tab escape bookmark-manager pick:" + LabScene.RepositoryPage + " link-open"),
                new("Перенесите ссылку из Chrome в VS Code",
                    "url copy open-code palette code-clone-command paste code-clone-confirm"),
                new("Проверьте, что README.md открыт из учебной копии",
                    "quick-open quick:README.md quick-choose")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareBrowserPractice()));

        void Research(int level, string id, string title, string family, params LivePhase[] phases) =>
            list.Add(new("chrome", level, "chrome-" + id, title, phases, FinishAfterRoute: true,
                Curated: true, Prepare: scene => scene.PrepareBrowserPractice(family)));
    }
}
