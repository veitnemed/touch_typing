using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private string notebookFamily, verifiedNotebookBody;
    private CsvNotebookCase csvNotebook;
    internal bool AdvancedNotebook => notebookFamily != null;
    internal bool CsvNotebook => csvNotebook != null;

    private sealed record CsvNotebookCase(string Stem, string Title, string Function, string Op, string Fault, string Column, int[] Readings)
    {
        internal string Input => Stem + ".csv";
        internal string Approved => Stem + "-updated";
        internal string Module => Stem.Replace('-', '_') + "_metrics";
        internal string File => Stem + "-verification.ipynb";
        internal string OldCsv => "item," + Column + ",state\nprevious-1,8,ready\nprevious-2,10,ready\n";
        internal string NewCsv => Csv(Readings);
        internal string Import => "from " + Module + " import " + Function;
        internal string Metric => Function + "()";
        internal string Csv(int[] data) => "item," + Column + ",state\n" +
            string.Join("\n", data.Select((v, i) => "\"line, " + (i + 1) + "\"," + v + ",ready")) + "\nold,1000,archived\n";
        internal int Expect(int[] values) => Op switch { "sum" => values.Sum(), "max" => values.Max(),
            _ => values.Order().ElementAt(values.Length / 2) };
        internal string Source(bool fixedCode) =>
            "from csv import DictReader\nfrom statistics import mean, median\n\n" +
            "def " + Function + "():\n" +
            "    rows = DictReader(open(\"data/" + (fixedCode ? Approved + ".csv" : Input) + "\"))\n" +
            "    values = [int(row[\"" + Column + "\"]) for row in rows if row[\"state\"] == \"ready\"]\n" +
            "    return " + (fixedCode ? Op : Fault) + "(values)\n";
    }
    private static readonly CsvNotebookCase[] CsvCases = [
        new("warehouse-count", "Итог остатков", "total_units", "sum", "mean", "units", [12, 9, 17]),
        new("delivery-minutes", "Длительность доставки", "longest_delivery", "max", "min", "minutes", [15, 26, 20]),
        new("sample-duration", "Проверка оборудования", "typical_duration", "median", "sum", "seconds", [34, 9, 16]),
    ];
    private static string Invariant(int number) => number.ToString(CultureInfo.InvariantCulture);
    private string NotebookAbs(string path) => NotebookRoot + "/" + path;
    internal string CsvNotebookFile => csvNotebook?.File;
    internal string CsvInputQuery => "incoming/" + csvNotebook.Stem[..Math.Min(csvNotebook.Stem.Length, 10)];
    internal string CsvApprovedName => csvNotebook.Approved;
    internal string CsvModuleSource => csvNotebook.Source(true);
    internal string CsvModuleName => csvNotebook.Module + ".py";
    internal string CsvCalculation => csvNotebook.Op;
    internal string CsvApprovedPath => "data/" + csvNotebook.Approved + ".csv";
    internal string VerifiedNotebookPath => "verified/" + NotebookFile;
    private int[] StructureOldNumbers => [.. notebookCase.Numbers, 90];
    internal string NotebookReviewNote => "# Source: current data";
    internal string NotebookCheckExpression => (notebookCase.Operation == "len" ? "sum" : "len") + "(" + notebookCase.Name + ")";
    private string NotebookReviewValue => Invariant(notebookCase.Operation == "len" ? notebookCase.Numbers.Sum() : notebookCase.Numbers.Length);
    private static int AggregateLesson(string op, int[] numbers) => op switch { "max" => numbers.Max(), "len" => numbers.Length, _ => numbers.Sum() };

    internal void PrepareAdvancedNotebook(string family)
    {
        notebookFamily = family;
        if (family == "csv") { PrepareCsvNotebook(); return; }
        var item = notebookCase = NotebookCases[browserCaseVariant];
        notebookLevel = family == "clean" ? 2 : 0;
        string body = EditorNotebook.Create(item.Label, item.Binding(false), item.Formula);
        string brief;
        if (family == "structure")
        {
            var doc = (JsonObject)JsonNode.Parse(body);
            var cells = (JsonArray)doc["cells"];
            var metric = cells[2]; cells.RemoveAt(2); cells.Insert(1, metric);
            var duplicate = (JsonObject)cells[2].DeepClone();
            duplicate["id"] = "old-data";
            duplicate["source"] = item.Name + " = [" + string.Join(", ", StructureOldNumbers) + "]";
            var note = (JsonObject)metric.DeepClone();
            note["id"] = "source-note"; note["source"] = "# TODO: identify source";
            cells.Add(duplicate); cells.Add(note); body = doc.ToJsonString();
            brief = "# Ячейка расчёта стоит перед данными. Старый список перезаписывает новый, если выполнить всё.\n" +
                "# Перенесите расчёт к данным, проверьте обе цифры и уберите только фрагмент с 90.\n" +
                "# Отметьте источник в Markdown и добавьте независимую контрольную ячейку.\n";
        }
        else
        {
            // A previous run saved in a file is observable, but is not a live kernel.
            var saved = EditorNotebook.Open(item.File, body);
            saved.RunBatch("run-all", false, _ => null);
            body = saved.Pack();
            brief = "# Открыт файл с выводом прошлого запуска. Пришла новая запись: " + item.Extra + ".\n" +
                "# Обновите список, перезапустите ядро и подтвердите расчёт с чистого состояния.\n";
        }
        InitialNotebookProject(
            ("app.py", "# " + item.Label + "\n" + brief + "# Итог запишите в " + NotebookReport + ".\n" +
                "notebook = '" + item.File + "'\n"),
            (item.File, body), (NotebookReport, ""), ("archive.txt", "Старая сводка. Не изменять.\n"));
    }

    private void PrepareCsvNotebook()
    {
        var item = csvNotebook = CsvCases[browserCaseVariant];
        Terminal.CreateFolder(NotebookAbs("data"));
        Terminal.CreateFolder(NotebookAbs("incoming"));
        string objective = item.Op switch { "sum" => "сумму", "max" => "максимум", _ => "медиану" };
        InitialNotebookProject(
            ("app.py", "# " + item.Title + "\n# В incoming появилась новая выгрузка. В data лежит предыдущая с тем же именем.\n" +
                "# Не заменяйте её. Новая выгрузка должна называться " + item.Approved + ".csv.\n" +
                "# В " + item.File + " воспроизведите " + objective + " только для готовых записей.\n" +
                "# Перенесите проверенный блокнот в verified/ и откройте его снова.\n"),
            ("incoming/" + item.Input, item.NewCsv), ("data/" + item.Input, item.OldCsv), (item.Module + ".py", item.Source(false)),
            ("archive_metrics.py", "def " + item.Function + "():\n    return 0\n"), // F12 must follow the import, not this name.
            (item.File, EditorNotebook.Create(item.Title, item.Import, item.Metric)));
    }

    private void InitialNotebookProject(params (string Name, string Content)[] files)
    {
        Terminal.CreateFolder(NotebookRoot);
        foreach (var (name, content) in files)
        { string path = NotebookAbs(name); Terminal.StoreFile(path, content); originalFiles[path] = content; }
        Ensure("code"); Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0;
        OpenDocument(NotebookAbs("app.py"));
    }

    internal string NotebookFileTarget(string key)
    {
        if (!CsvNotebook) return null;
        return key switch
        {
            "@csv-input" => NotebookAbs("incoming/" + csvNotebook.Input),
            "@csv-notebook" => NotebookAbs(NotebookFile),
            _ => null
        };
    }
    internal bool NotebookConflictExpected() => CsvNotebook &&
        lastFileConflict == (NotebookAbs("incoming/" + csvNotebook.Input), NotebookAbs("data/" + csvNotebook.Input)) &&
        NotebookRead("incoming/" + csvNotebook.Input) == csvNotebook.NewCsv &&
        NotebookRead("data/" + csvNotebook.Input) == csvNotebook.OldCsv;

    private bool ModulePrepared()
    {
        if (!CsvNotebook || NotebookRead(CsvApprovedPath) != csvNotebook.NewCsv ||
            NotebookRead("data/" + csvNotebook.Input) != csvNotebook.OldCsv ||
            NotebookRead("incoming/" + csvNotebook.Input) != null) return false;
        // Test the learner's saved function on a second dataset. The real notebook
        // run below isn't replaced by a canned answer or a synthetic test result.
        var probe = new[] { 4, 21, 5 };
        string Code(string path) => path == CsvApprovedPath ? csvNotebook.Csv(probe) : NotebookRead(path);
        var check = new NotebookPythonKernel().Run(csvNotebook.Import + "\n" + csvNotebook.Metric, Code);
        return check.Error == null && check.Files.ContainsKey(CsvApprovedPath) && check.Result == Invariant(csvNotebook.Expect(probe));
    }

    private bool AdvancedResultValid(EditorNotebook n)
    {
        n.SynchronizeWorkspace(NotebookRead);
        if (CsvNotebook) return n.Count == 3 && ModulePrepared() && n.Generation > 1 &&
            n.Fresh(1) && n.Fresh(2) && !n.Failed(2) && n.Output(2) == NotebookAnswer;
        if (notebookFamily == "clean")
            return n.Count >= 3 && n.Generation > 1 && n.Fresh(1) && n.Fresh(2) &&
                n.Source(1) == notebookCase.Binding(true) && !n.Failed(2) && n.Output(2) == NotebookAnswer;
        return n.Count == 5 && n.Source(1) == notebookCase.Binding(false) && n.Source(2) == notebookCase.Formula &&
            n.Fresh(1) && n.Fresh(2) && !n.Failed(2) && n.Output(2) == NotebookAnswer &&
            !n.IsCode(3) && n.Source(3) == NotebookReviewNote && n.IsCode(4) && n.Fresh(4) && n.Output(4) == NotebookReviewValue;
    }

    private void AfterNotebookSave(LabWindow w)
    {
        if (!AdvancedNotebook || w.Tab?.Notebook is not EditorNotebook n || NameOf(w.Url) != NotebookFile) return;
        verifiedNotebookBody = AdvancedResultValid(n) ? w.Body : null;
    }

    private bool AdvancedNotebookSaved() => verifiedNotebookBody != null &&
        (NotebookRead(NotebookFile) == verifiedNotebookBody ||
         CsvNotebook && NotebookRead(NotebookFile) == null && NotebookRead(VerifiedNotebookPath) == verifiedNotebookBody);

    private object CsvEvidenceFiles() => CsvNotebook ? new {
        incoming = NotebookRead("incoming/" + csvNotebook.Input), archive = NotebookRead("data/" + csvNotebook.Input),
        renamed = NotebookRead("incoming/" + csvNotebook.Approved + ".csv"), moved = NotebookRead(CsvApprovedPath),
        original = NotebookRead(NotebookFile) != null, verified = NotebookRead(VerifiedNotebookPath) != null
    } : null;

    internal object ScopedCsvExplorerEvidence(LabTask task)
    {
        if (!CsvNotebook || Active is not LabWindow w) return null;
        var path = w.Focus is "explorer" or "file-name" ? Entries(w).ElementAtOrDefault(w.Item)?.Path : null;
        // A destination selection only counts with the file being transferred.
        // Merely inspecting data before cutting the CSV is not that action.
        bool destination = task.Text is "data" or "verified";
        return task.Action switch
        {
            "quick-open" => new { w.App, w.Focus, w.Input },
            "explorer" => new { w.App, w.Focus, w.View, w.Sidebar },
            "item-next" => new { w.App, w.Focus, path,
                transfer = destination && fileCut && Clipboard == fileTransfer ? fileTransfer : null },
            "item-open" => new { w.App, w.Focus, path, expanded = path != null && w.ExpandedProjectFolders.Contains(path) },
            "file-cut" or "file-copy" => new { w.App, w.Focus, path, fileTransfer, fileCut },
            "file-name-start" => new { w.App, w.Focus, w.RenamePath, w.Input, w.Cursor, w.Anchor },
            "file-name-commit" => new { w.App, w.Focus, path, files = CsvEvidenceFiles() },
            "file-paste" when task.Text == "!conflict" =>
                new { w.App, w.Focus, conflict = NotebookConflictExpected(), files = CsvEvidenceFiles() },
            "file-paste" => new { w.App, w.Focus, path, files = CsvEvidenceFiles() },
            _ => null
        };
    }

    // Keep the original full scene proof solely for replaying older input traces.
    private object AdvancedNotebookEvidence(LabTask task)
    {
        var w = Active;
        if (w == null) return new { missing = true };
        var n = w.Tab?.Notebook;
        n?.SynchronizeWorkspace(NotebookRead);
        string action = task.Action;
        if (action == "palette-choose")
        {
            if (task.Text == "New Folder") return new { w.Focus, w.RenamePath };
            if (n == null) return new { path = w.Url, w.Focus };
            if (task.Text == "Restart Kernel")
                return new { path = w.Url, w.Focus, reset = n.Counter == 0 && n.Generation > 1,
                    live = Enumerable.Range(0, n.Count).Any(n.Fresh) };
            if (task.Text == "Clear All Outputs")
                return new { path = w.Url, w.Focus, clear = Enumerable.Range(0, n.Count).All(i => n.Runs(i) == 0 && n.Output(i) == "") };
            return new { path = w.Url, w.Focus, outputs = Enumerable.Range(0, n.Count).Select(i =>
                new { fresh = n.Fresh(i), output = n.Output(i), failed = n.Failed(i) }).ToArray() };
        }
        if (action == "quick-choose" && CsvNotebook)
            return new { path = w.Url, w.Focus, fresh = n != null && n.Count > 2 && n.Fresh(2), selected = n?.Selected, saved = AdvancedNotebookSaved() };
        if (action == "save")
        {
            if (CsvNotebook && w.Url.EndsWith("/" + CsvModuleName, StringComparison.Ordinal))
                return new { path = w.Url, w.Focus, correct = ModulePrepared() };
            return new { path = w.Url, w.Focus, correct = AdvancedNotebookSaved(), report = NotebookRead(NotebookReport),
                archive = NotebookRead("archive.txt"), source = NotebookRead("app.py") };
        }
        if (action == "code-definition")
            return new { path = w.Url, w.Focus, caret = w.Cursor, anchor = w.Anchor };
        if (action == "code-back")
            return new { path = w.Url, w.Focus, selected = n?.Selected, editing = n?.Editing };
        if (action == "close-tab" && CsvNotebook)
            return new { closed = !w.Tabs.Any(t => EditorPath(t) == NotebookAbs(VerifiedNotebookPath)), w.Focus };
        if (action == "file-paste" && task.Text == "!conflict")
            return new { path = w.Url, w.Focus, conflict = NotebookConflictExpected(), files = CsvEvidenceFiles() };
        if (action is "file-cut" or "file-copy")
            return new { path = w.Url, w.Focus, current = Entries(w).ElementAtOrDefault(w.Item)?.Path, fileTransfer, fileCut };
        if (action == "file-name-start" || action == "file-name-commit" || action == "file-paste" || action == "item-open" || action == "item-next")
            return new { path = w.Url, w.Focus, w.RenamePath, current = Entries(w).ElementAtOrDefault(w.Item)?.Path,
                expanded = w.ExpandedProjectFolders.Order().ToArray(), files = CsvEvidenceFiles() };
        if (action is "notebook-move-up" or "notebook-move-down" or "notebook-delete-cell" or "notebook-markdown" or "notebook-code" or
            "notebook-add-above" or "notebook-add-below" or "notebook-paste-cell")
            return n == null ? new { path = w.Url, missing = true } : new { path = w.Url, n.Selected, n.Editing, cells = Enumerable.Range(0,n.Count).Select(i => new { code = n.IsCode(i), source = n.Source(i) }).ToArray() };
        return NotebookEvidence(action);
    }
}

internal static partial class LiveCourse
{
    private static bool NotebookEnglishToken(string token) => token is "nb-note" or "nb-check" or "nb-incoming" or "nb-verified-open" or
        "nb-rename-csv" or "nb-module-save" or "nb-folder-name" or "nb-new-folder" or "nb-run-all" or "nb-run-above" or "nb-restart" or "nb-clear";

    private static LabTask AdvancedNotebookTask(string token, LabScene s)
    {
        if (!s.AdvancedNotebook) return null;
        LabTask Command(string query, string title) => Actions["palette-choose"] with { Text = query, Title = title,
            Hint = "В палитре введите «" + query + "» и нажмите Enter." };
        return token switch
        {
            "nb-note" => new("notebook-type", "Отметьте источник текущих данных в пояснении", "", "Введите «" + s.NotebookReviewNote + "».",
                s.NotebookReviewNote, "notebook-type"),
            "nb-check" => new("notebook-type", "Добавьте отдельный контроль по исходному списку", "", "Введите «" + s.NotebookCheckExpression + "».",
                s.NotebookCheckExpression, "notebook-type"),
            "nb-run-all" => Command("Run All Cells", "Выполните блокнот сверху вниз и сверьте вывод"),
            "nb-run-above" => Command("Run All Above", "Восстановите данные, выполнив только ячейки выше расчёта"),
            "nb-restart" => Command("Restart Kernel", "Перезапустите ядро, чтобы прошлые данные не влияли на результат"),
            "nb-clear" => Command("Clear All Outputs", "Уберите вывод старых проверок перед новым прогоном"),
            "nb-new-folder" => Command("New Folder", "Создайте отдельную папку для проверенного блокнота"),
            "nb-incoming" => Actions["quick-choose"] with { Text = s.CsvInputQuery,
                Title = "Откройте новую выгрузку по части пути: " + s.CsvInputQuery },
            "nb-verified-open" => Actions["quick-choose"] with { Text = s.VerifiedNotebookPath,
                Title = "Откройте сохранённый блокнот из папки verified" },
            "nb-file-conflict" => Actions["file-paste"] with { Text = "!conflict", Title = "Проверьте, почему нельзя просто заменить имеющийся CSV",
                Hint = "В папке data есть файл с таким же именем. Ctrl+V покажет конфликт и сохранит оба файла." },
            "nb-rename-csv" => Actions["file-name-commit"] with { Text = s.CsvApprovedName, Keys = "",
                Title = "Переименуйте входной файл в «" + s.CsvApprovedName + ".csv», чтобы не перезаписать предыдущий",
                Hint = "Подставьте «" + s.CsvApprovedName + "» вместо выделенного имени. Расширение .csv оставьте; затем Enter." },
            "nb-folder-name" => Actions["file-name-commit"] with { Text = "verified", Keys = "",
                Title = "Назовите папку verified и подтвердите", Hint = "Введите verified и нажмите Enter." },
            "nb-module-save" => Actions["save"] with { Text = s.CsvModuleSource,
                Title = "Исправьте путь к новой выгрузке и формулу в " + s.CsvModuleName + ", затем сохраните",
                Hint = "Укажите «" + s.CsvApprovedPath + "» в open(...), а в return замените расчёт на " +
                    s.CsvCalculation + "(values). Ctrl+S сохранит исправление." },
            _ => null
        };
    }

    private static void AdvancedNotebookMissions(List<LiveMission> list)
    {
        void Add(int level, string id, string title, string family, params LivePhase[] phases) =>
            list.Add(new("code", level, id, title, phases, FinishAfterRoute: true, Curated: true,
                Prepare: scene => scene.PrepareAdvancedNotebook(family)));
        Add(0, "code-notebook-structure", "Разобрать унаследованный блокнот и убрать двойной учёт", "structure",
            new("Откройте унаследованный расчёт. Проверьте, почему он не видит данные", "quick-open notebook-file notebook-down notebook-run"),
            new("Перенесите расчёт ниже обоих источников. Новый запуск покажет, какой список используется", "notebook-move-down notebook-move-down palette nb-run-all"),
            new("Уберите старую ячейку с 90: она перезаписывает текущие данные", "notebook-up notebook-delete-cell"),
            new("Превратите черновую заметку в Markdown и назовите источник", "notebook-down notebook-markdown notebook-edit notebook-all nb-note notebook-run"),
            new("Добавьте самостоятельную контрольную ячейку и подтвердите обе цифры", "notebook-add-below notebook-edit nb-check notebook-leave palette nb-restart palette nb-run-all"),
            new("Сохраните проверенный файл и передайте только основное число в итоговый отчёт", "save quick-open notebook-report notebook-report-save"));
        Add(1, "code-notebook-clean-rerun", "Воспроизвести расчёт начисто на обновлённых данных", "clean",
            new("Откройте блокнот с сохранённым выводом прошлого запуска", "quick-open notebook-file notebook-down notebook-run-next notebook-run"),
            new("Добавьте новую запись из заявки. Старый вывод оставьте как свидетельство прежнего расчёта", "notebook-up notebook-edit notebook-all notebook-type notebook-leave"),
            new("Сбросьте ядро. Запуск нижней ячейки покажет, почему сохранённый вывод не доказывает новый результат", "palette nb-restart notebook-down notebook-run"),
            new("Очистите старый вывод. Выполните зависимости, затем сам расчёт", "palette nb-clear palette nb-run-above notebook-run"),
            new("Сохраните расчёт и запишите новое число в итоговый файл", "save quick-open notebook-report notebook-report-save"));
        Add(2, "code-project-file-csv-notebook", "Сверить выгрузку, исправить Python-модуль и подтвердить блокнот", "csv",
            new("Найдите свежий CSV по части пути, сверив его с заявкой", "quick-open nb-incoming"),
            new("Подготовьте входной файл к переносу. Старый CSV с таким же именем нельзя заменять",
                "explorer pick:incoming file-open pick:@csv-input file-cut pick:data nb-file-conflict"),
            new("Разрешите конфликт именем, в котором различимы две выгрузки, и перенесите новую",
                "pick:@csv-input file-name-start nb-rename-csv pick:data file-paste"),
            new("Сверьте старую цифру со свежей выгрузкой. Из вызова в блокноте перейдите в настоящий модуль проекта",
                "quick-open notebook-file notebook-down notebook-run-next notebook-run notebook-edit code-definition"),
            new("Исправьте путь и операцию в модуле; затем воспроизведите результат с чистого ядра",
                "nb-module-save code-back palette nb-restart palette nb-run-all save"),
            new("Поместите подтверждённый блокнот в verified. Заново откройте тот же сохранённый документ с нового пути",
                "palette nb-new-folder nb-folder-name pick:@csv-notebook file-cut pick:verified file-paste focus-editor close-tab quick-open nb-verified-open"));
    }
}
