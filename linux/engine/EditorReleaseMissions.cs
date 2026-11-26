using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

// The same workbench and classroom Git repository serve both platforms.
// Their data (rather than a remembered key sequence) decides what can be committed.
internal sealed record ReleaseCase(bool Move, string Facts, string Notes, string Target,
    string FactsText, string NotesHead, string NotesText, string TargetHead, string BlockA, string BlockB,
    string MarkerA, string MarkerB, string Message)
{
    internal string Expected => TargetHead.Replace(MarkerA + "\n", BlockA).Replace(MarkerB + "\n", BlockB);
    internal string ExpectedNotes => Move ? NotesText.Replace(BlockB, "") : NotesText;
}
internal sealed partial class LabScene
{
    internal const string ReleaseRoot = Project + "/handoff";
    private ReleaseCase releaseCase;
    internal bool ReleasePractice => releaseCase != null;
    internal void PrepareEditorRelease(bool move)
    {
        string station = new[]{"north","west","east"}[browserCaseVariant];
        string day = new[]{"Monday","Wednesday","Friday"}[browserCaseVariant];
        string hour = new[]{"09:30","15:15","18:45"}[browserCaseVariant];
        string reviewer = new[]{"Nora","Ivan","Mira"}[browserCaseVariant];
        string first = move ? $"endpoint = {station}.local\ntimeout = {10 + 5 * browserCaseVariant}\n" : $"Window: {day} {hour}\nZone: UTC\n";
        string second = move ? $"owner = {reviewer}\nchannel = {station}-support\n" : $"Contact: {reviewer}\nChannel: {station}-support\n";
        string firstMarker = move ? "# OLD CONNECTION" : "[old schedule]";
        string secondMarker = move ? "# OLD CONTACT" : "[old contact]";
        releaseCase = new(move, move ? "service.ini" : "schedule.md", move ? "draft.ini" : "notes.md",
            move ? "delivery.ini" : "handoff.md",
            (move ? "# Tested connection\n" : "# Approved plan\n") + first + "# Archive: last month\n",
            "# Private notes\n# Keep local\n", "# Private notes\n" + second + "# Phone: private\n",
            (move ? "[service]\n" : "# Shift handoff\n") + firstMarker + "\n\n" + (move ? "[support]\n" : "## Support\n") + secondMarker + "\n# End\n",
            first, second, firstMarker, secondMarker, move ? "Update delivery config" : "Publish shift handoff");
        Terminal.CreateFolder(ReleaseRoot);
        void Seed(string file, string initial) { string path = ReleaseRoot + "/" + file; originalFiles[path] = initial; Terminal.StoreFile(path, initial); }
        Seed(releaseCase.Facts, releaseCase.FactsText); Seed(releaseCase.Notes, releaseCase.NotesHead); Seed(releaseCase.Target, releaseCase.TargetHead);
        if (!Terminal.AttachWorkbench(ReleaseRoot, originalFiles)) throw new InvalidOperationException("Невозможно подготовить учебный репозиторий");
        string scratch = ReleaseRoot + "/" + releaseCase.Notes;
        Terminal.StoreFile(scratch, releaseCase.NotesText);
        Terminal.ModifyStage(ReleaseRoot, scratch, true); // the unwanted change predates the student
        Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0;
        OpenDocument(ReleaseRoot + "/" + releaseCase.Target);
    }

    internal bool ReleaseIndexReady() => !ReleasePractice ||
        Terminal.GitChanges().Where(change => change.Staged).Select(change => change.Path).SequenceEqual(new[]{ReleaseRoot + "/" + releaseCase.Target}) &&
        Terminal.StagedBody(releaseCase.Target) == releaseCase.Expected &&
        Terminal.ReadFile(ReleaseRoot + "/" + releaseCase.Target) == releaseCase.Expected &&
        Terminal.StagedBody(releaseCase.Facts) == releaseCase.FactsText &&
        Terminal.ReadFile(ReleaseRoot + "/" + releaseCase.Facts) == releaseCase.FactsText &&
        Terminal.StagedBody(releaseCase.Notes) == releaseCase.NotesHead &&
        Terminal.ReadFile(ReleaseRoot + "/" + releaseCase.Notes) == releaseCase.ExpectedNotes;
    internal string ReleasePicker(string token) => !ReleasePractice ? token : token switch {
        "quick:@facts" => "quick:" + releaseCase.Facts, "quick:@notes" => "quick:" + releaseCase.Notes,
        "quick:@target" => "quick:" + releaseCase.Target, _ => token };
    internal string ReleaseQuery(string token) => token switch {
        "release-find-facts" => releaseCase.BlockA.Split('\n')[0],
        "release-find-notes" => releaseCase.BlockB.Split('\n')[0],
        "release-find-target-a" => releaseCase.MarkerA,
        "release-find-target-b" => releaseCase.MarkerB, _ => null };
    internal string CommitSuggestion => releaseCase?.Message ?? "";
    internal bool? DemonstrateRelease(LabTask task)
    {
        if (task.Action == "release-find") { Active.Input = task.Text; return Text("Enter",""); }
        if (task.Action == "scm-commit") { Insert(Active, task.Text); return Apply("scm-commit"); }
        return null;
    }
    internal object ReleaseEvidence(LabTask task, string equivalentCommand)
    {
        if (!ReleasePractice) return null;
        var w = Active;
        if (task.Action == "command")
        {
            // Inspect the real reply, but don't freeze the learner's commit message into
            // the compiled comparison. Git is free to commit any nonempty description.
            bool logMatches = task.Text != "git log --oneline" || Terminal.LocalCommits == 1 &&
                Terminal.LastOutput.LastOrDefault() == "0000001 " + Terminal.LastLocalCommit;
            bool statusMatches = task.Text != "git status --short" ||
                Terminal.LastOutput.SequenceEqual(new[] { " M " + releaseCase.Notes });
            return new { w.App, w.Focus, Terminal.Cwd, command = equivalentCommand ?? Terminal.LastCommand,
                Terminal.LastSuccess, logMatches, statusMatches, committed = Terminal.CommittedBody(releaseCase.Target) == releaseCase.Expected };
        }
        if (task.Action == "release-find")
        {
            var p = w.Tab.Position;
            int left = Math.Min(p.Anchor,p.Caret), right = Math.Max(p.Anchor,p.Caret);
            return new { w.Focus, w.Url, w.Find, selected = w.Body[left..right] };
        }
        if (task.Action.StartsWith("scm-", StringComparison.Ordinal))
        {
            // Mouse selection or an empty commit message cannot prove a command. Do not
            // encode a particular commit message in the goal: a real description is enough.
            var selected = SelectedChange(w);
            return new { w.View, w.Focus, selected = selected?.Path, staged = selected?.Staged,
                index = Terminal.GitChanges().Where(x => x.Staged).Select(x => new { x.Path, x.After }),
                Terminal.LocalCommits, delivered = Terminal.CommittedBody(releaseCase.Target) == releaseCase.Expected,
                kept = Terminal.CommittedBody(releaseCase.Notes) == releaseCase.NotesHead };
        }
        if (task.Action is "select-line" or "copy" or "cut" or "paste" or "save")
            return new { w.Focus, w.Url, w.Body, w.Anchor, w.Cursor, Clipboard, disk = Terminal.ReadFile(w.Url) };
        return null;
    }
    internal string ChangeName => SelectedChange(Active) is WorkbenchChange item ? NameOf(item.Path) : "";
}
internal static partial class LiveCourse
{
    private static readonly (string Action, string Gesture)[] GitKeys = [
        ("scm-menu","Shift+F10"), ("scm-stage","Enter"), ("scm-unstage","Enter"), ("scm-message","Shift+Tab"),
        ("scm-commit","Ctrl+Enter"), ("scm-close","Esc") ];
    private static LabTask ReleaseTask(string token, LabScene s)
    {
        if (token.StartsWith("release-find-", StringComparison.Ordinal) && s.ReleasePractice) {
            string q = s.ReleaseQuery(token);
            return new("release-find", $"Найдите «{q}» в {s.Active.Tab.Title}", "", "Введите указанный фрагмент в поле поиска и нажмите Enter.", q, "release-find");
        }
        var item = GitKeys.FirstOrDefault(x => x.Action == token);
        if (item.Action == null) return null;
        string title = token switch {
            "scm-menu" => $"Откройте меню правки «{s.ChangeName}»",
            "scm-stage" => $"Подготовьте только «{s.ChangeName}» к локальному коммиту",
            "scm-unstage" => $"Уберите личный черновик «{s.ChangeName}» из индекса, не удаляя правку",
            "scm-message" => "Перейдите из списка изменений к сообщению коммита",
            "scm-commit" => "Опишите правку и создайте локальный коммит",
            _ => "Вернитесь к списку изменений" };
        return new(token, title, item.Gesture, token == "scm-commit" ? $"Например: «{s.CommitSuggestion}». Ctrl+Enter фиксирует только файлы в Staged Changes, без отправки в сеть." : title, token == "scm-commit" ? s.CommitSuggestion : "", token);
    }
    internal static LabTask ResolveGitKey(string key, IEnumerable<string> held, LabScene scene)
    {
        if (scene.Active?.View != "changes") return null;
        foreach (var (action, gesture) in GitKeys)
        {
            var c = Gesture.Parse(gesture).Chords[0];
            if (c.Trigger == key && c.FitsModifiers(held) && scene.GitActionAvailable(action)) return ReleaseTask(action,scene);
        }
        return null;
    }
    private static void EditorReleaseMissions(List<LiveMission> missions)
    {
        foreach (bool move in new[] { false, true }) {
            missions.Add(new("code", move ? 2 : 1, move ? "code-publish-config" : "code-publish-handoff",
                move ? "Собрать рабочую конфигурацию из двух источников и зафиксировать её в Git" :
                       "Собрать инструкцию дежурному из двух документов и передать через Git", [
                new("Исключите личные заметки из будущего коммита, не удаляя их из проекта",
                    "changes scm-menu scm-unstage focus-editor"),
                new("Возьмите из проверенного источника две актуальные строки, не захватывая архивную запись",
                    "quick-open quick:@facts quick-choose find release-find-facts escape select-line select-line copy"),
                new("Замените в итоговом документе первую устаревшую строку подтверждёнными данными",
                    "quick-open quick:@target quick-choose find release-find-target-a escape select-line paste"),
                new(move ? "Перенесите из черновика правила поддержки; личная пометка остаётся в нём" : "Возьмите из заметок две строки контакта, не копируя личный телефон",
                    "quick-open quick:@notes quick-choose find release-find-notes escape select-line select-line " + (move ? "cut save" : "copy")),
                new("Замените вторую устаревшую строку, сохраните и сверьте оба блока в Git diff",
                    "quick-open quick:@target quick-choose find release-find-target-b escape select-line paste save changes"),
                new("Подготовьте итоговый файл и зафиксируйте его без личных заметок",
                    "scm-menu scm-stage scm-message scm-commit"),
                new("В терминале убедитесь, что появился новый коммит и личная правка осталась только в рабочей копии",
                    "terminal-toggle cmd:cd~" + LabScene.ReleaseRoot + " cmd:git~log~--oneline cmd:git~status~--short")
            ], FinishAfterRoute:true, Curated:true, Prepare:scene => scene.PrepareEditorRelease(move)));
        }
    }
}
