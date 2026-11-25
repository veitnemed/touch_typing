using System;
using System.Collections.Generic;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private bool SourceRevision => sourceHandoff == "revision";
    private string RevisionNote => Project + "/передача-смены.md";
    private string OldCitation => browserResearch.Target + "/archive";
    private bool citationCopiedFromSavedNote;
    private string RevisionRecord(string url) =>
        "# Передача смены: " + browserResearch.Topic + "\n\n" +
        "Статус: работа принята.\nОтветственный: вторая смена.\n" +
        "Итог: " + browserResearch.Fact + "\n\n" +
        "Инструкция для следующей смены:\n" + url;

    internal void PrepareSourceRevision(bool windows)
    {
        if (windows) PrepareWindowsDesktop();
        sourceHandoff = "revision";
        PrepareBrowserPractice("recover");
        // These are real prior visits in this simulated profile. The section,
        // handbook cover and archive must be distinguishable in the History UI.
        var browser = Active;
        Navigate(browser, browserResearch.Section);
        Navigate(browser, OldCitation);
        Navigate(browser, browserResearch.Target);
        Navigate(browser, SearchHome);
        StoreSourceFile(RevisionNote, RevisionRecord(OldCitation));
        SeedSourceEditor(RevisionNote, OldCitation);
    }

    private static (string Title, string Body)? ArchivedHandbookPage(string url)
    {
        foreach (var item in HandbookCases)
            if (url == item.Target + "/archive")
                return (item.Topic + " · архив", "Это предыдущий порядок работы. Для передачи используйте действующий раздел «" + item.SectionTitle + "».");
        return null;
    }

    // Reading from the saved note matters even when the clipboard happens to
    // still hold an identical value copied earlier from Chrome.
    private void RememberCitationCopy(LabWindow w)
    {
        if (!SourceRevision) return;
        citationCopiedFromSavedNote = w?.App == "code" && w.Focus == "body" && w.Url == RevisionNote &&
            Terminal.ReadFile(RevisionNote) == RevisionRecord(browserResearch.Section) &&
            Clipboard == browserResearch.Section && w.Body == Terminal.ReadFile(RevisionNote);
    }

    private LabTask DescribeRevision(LabTask t) => t.Action switch
    {
        "history" => t with { Title = "Восстановите из истории инструкцию «" + browserResearch.SectionTitle + "», а не обложку или архив",
            Hint = "Ctrl+H откроет историю. Сузьте выдачу по слову из адреса или названию, затем выберите нужную страницу." },
        "url" => t with { Title = "Выделите адрес открытого раздела инструкции" },
        "copy" when Active?.App == "code" => t with { Title = "Скопируйте ссылку из сохранённой записки для проверки" },
        "copy" => t with { Title = "Скопируйте адрес раздела вместе с #procedure" },
        "win-switch" or "switch-next" => t with { Title = Active?.App == "chrome" ? "Вернитесь к записке: прежняя ссылка уже выделена" :
            citationCopiedFromSavedNote ? "Перейдите в браузер и проверьте ссылку читателя" :
                "В записке выделена прежняя ссылка. Перейдите в Chrome за действующей инструкцией" },
        "paste" => t with { Title = Active?.App == "code" ? "Замените только выделенную ссылку, не меняя принятый итог" : "Вставьте ссылку, взятую из сохранённой записки" },
        "save" => t with { Title = "Сохраните исправленный адрес в прежней записке" },
        "select-line" => t with { Title = "Для проверки выберите ссылку, которую теперь получит следующая смена" },
        "new-tab" => t with { Title = "Откройте чистую вкладку для проверки ссылки" },
        "navigate-pasted" => t with { Title = "Откройте ссылку и убедитесь, что видна инструкция «" + browserResearch.SectionTitle + "»" },
        _ => t
    };

    private object RevisionEvidence(LabTask t)
    {
        var w = Active;
        return t.Action switch
        {
            "item-open" => new { w?.App, w?.Url, w?.Focus, w?.Body },
            "paste" => w?.App == "code" ? new { w.Url, w.Focus, w.Body, w.Cursor, w.Anchor } :
                (object)new { w?.Url, w?.Focus, w?.Input, citationCopiedFromSavedNote },
            "save" => new { w?.Url, saved = Terminal.ReadFile(RevisionNote), w?.Tab?.SavedBody,
                correct = Terminal.ReadFile(RevisionNote) == RevisionRecord(browserResearch.Section) },
            "copy" => new { w?.Url, w?.Focus, Clipboard, citationCopiedFromSavedNote },
            "navigate-pasted" => new { w?.Url, w?.Body, w?.Tab?.Title, citationCopiedFromSavedNote,
                correct = w?.Url == browserResearch.Section && Terminal.ReadFile(RevisionNote) == RevisionRecord(w.Url) },
            _ => null
        };
    }
}

internal static partial class LiveCourse
{
    private static void SourceRevisionMissions(List<LiveMission> missions)
    {
        foreach (bool windows in new[] { false, true })
        {
            string change = windows ? "win-switch" : "switch-next";
            missions.Add(new("workday", 1, "workday-" + (windows ? "windows-" : "") + "repair-source-link",
                "Исправить только устаревшую ссылку в передаче смены",
                [new("Верните точный раздел из истории Chrome; архив и обложка не подходят",
                     change + " history pick:@section link-open url copy"),
                 new("Продолжите сохранённую записку: исправьте ссылку, оставив принятый текст без изменений",
                     change + " paste save"),
                 new("Проверьте адрес из сохранённой записки глазами следующей смены",
                     "select-line copy " + change + " new-tab paste navigate-pasted")],
                FinishAfterRoute: true, Curated: true, Prepare: s => s.PrepareSourceRevision(windows)));
        }
    }
}
