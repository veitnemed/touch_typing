using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private const string NotebookRoot = Project + "/task-list";
    private sealed record NotebookCase(string Slug, string Name, string Label, string What, string Operation, int[] Numbers, int Extra)
    {
        internal string File => Slug + ".ipynb";
        internal string Report => Slug + "-result.txt";
        internal string Formula => Operation + "(" + Name + ")";
        internal string Binding(bool newData) => Name + " = [" + string.Join(", ", newData ? Numbers.Append(Extra) : Numbers) + "]";
        internal int Value(bool newData) { var a = newData ? Numbers.Append(Extra).ToArray() : Numbers; return Operation switch
            { "sum" => a.Sum(), "max" => a.Max(), _ => a.Length }; }
    }
    private static readonly NotebookCase[] NotebookCases = [
        new("weekly", "counts", "Задачи за неделю", "общее число выполненных задач", "sum", [3, 7, 4], 9),
        new("delivery", "times", "Время доставки", "самую долгую доставку в минутах", "max", [16, 21, 12], 28),
        new("checks", "codes", "Контрольные отчёты", "количество проверок, а не сумму кодов", "len", [12, 18, 15, 19], 24)
    ];
    private NotebookCase notebookCase;
    private int notebookLevel;
    internal bool NotebookCourse => notebookCase != null || CsvNotebook;
    internal string NotebookFile => CsvNotebook ? CsvNotebookFile : notebookCase.File;
    internal string NotebookReport => CsvNotebook ? "verification-result.txt" : notebookCase.Report;
    internal string NotebookEditTarget => notebookLevel == 2 ? notebookCase.Binding(true) : notebookCase.Formula;
    internal string NotebookAnswer => CsvNotebook ? Invariant(csvNotebook.Expect(csvNotebook.Readings)) :
        notebookCase.Value(notebookLevel == 2).ToString(CultureInfo.InvariantCulture);
    internal string NotebookBrief => notebookCase.What;
    internal int NotebookExtra => notebookCase.Extra;

    internal void PrepareNotebookCourse(int level)
    {
        notebookLevel = level; notebookCase = NotebookCases[browserCaseVariant];
        var item = notebookCase;
        Terminal.CreateFolder(NotebookRoot);
        string context = level switch {
            1 => "# В отчёте посчитан минимум. Проверьте формулу.",
            2 => "# Пришла ещё одна запись: " + item.Extra + ". Обновите данные и повторите расчёт.",
            _ => "# Проверьте расчёт в соседнем блокноте." };
        foreach (var (name, content) in new[] {
            ("app.py", "# " + item.Label + "\n" + context + "\n# Определите " + item.What + ".\n# Запишите только число в " + item.Report + ".\nnotebook = '" + item.File + "'\n"),
            (item.File, EditorNotebook.Create(item.Label, item.Binding(false), level == 1 ? "min(" + item.Name + ")" : item.Formula)),
            (item.Report, ""), ("archive.txt", "Старая сводка. Не изменять.\n") })
        {
            string path = NotebookRoot + "/" + name; Terminal.StoreFile(path, content); originalFiles[path] = content;
        }
        Ensure("code"); Active.Tabs.Clear(); Active.ClosedTabs.Clear(); Active.ActiveTab = 0; OpenDocument(NotebookRoot + "/app.py");
    }

    internal bool OnNotebook => Active?.App == "code" && Active.Focus == "body" && Active.Tab?.Notebook != null &&
        !Switcher && !Overview && !Screenshot && DesktopOverlay.Length == 0;
    internal object NotebookEvidence(LabTask task) => AdvancedNotebook ? AdvancedNotebookEvidence(task) : NotebookEvidence(task.Action);
    private object NotebookEvidence(string action)
    {
        var w = Active; var n = w?.Tab?.Notebook;
        n?.SynchronizeWorkspace(NotebookRead);
        if (action == "save")
        {
            bool correctNotebook = NotebookSavedCorrect();
            return new { path = w?.Url, w?.Focus, correctNotebook, report = Terminal.ReadFile(NotebookRoot + "/" + NotebookReport),
                archive = Terminal.ReadFile(NotebookRoot + "/archive.txt"), source = Terminal.ReadFile(NotebookRoot + "/app.py") };
        }
        if (n == null || !action.StartsWith("notebook-", StringComparison.Ordinal)) return new { path = w?.Url, w?.Focus, layout = Layout };
        if (action == "notebook-run-next") {
            int previous = Math.Max(0, n.Selected - 1);
            return new { path = w.Url, n.Selected, n.Editing, source = n.Source(previous),
                ran = n.Fresh(previous), valid = !n.Failed(previous), output = n.Output(previous) };
        }
        if (action == "notebook-run") return new { path = w.Url, n.Selected, n.Editing, source = n.Source(n.Selected), ran = n.Fresh(n.Selected),
            failed = n.Failed(n.Selected), output = n.Output(n.Selected) };
        if (action == "notebook-all") return new { path = w.Url, n.Selected, n.Editing,
            entire = Math.Min(n.Caret, n.Anchor) == 0 && Math.Max(n.Caret, n.Anchor) == n.Source(n.Selected).Length };
        if (action == "notebook-type") return new { path = w.Url, n.Selected, n.Editing, source = n.Source(n.Selected) };
        return new { path = w.Url, w.Focus, n.Selected, n.Editing };
    }
    internal bool NotebookSavedCorrect()
    {
        if (AdvancedNotebook) return AdvancedNotebookSaved();
        var saved = EditorNotebook.Open(NotebookFile, Terminal.ReadFile(NotebookRoot + "/" + NotebookFile) ?? "");
        return saved != null && saved.Count >= 3 && saved.Source(1) == notebookCase.Binding(notebookLevel == 2) &&
            saved.Source(2) == notebookCase.Formula && saved.Runs(1) > 0 && saved.Runs(2) > 0 && !saved.Failed(2) && saved.Output(2) == NotebookAnswer;
    }
    internal static bool NotebookGlobalAction(string action) => action is "save" or "quick-open" or "explorer" or "focus-editor" or
        "next-tab" or "prev-tab" or "code-definition" or "code-back" or "close-tab" or "new-tab" or "restore-tab" or "palette" or "new-file" or "sidebar" or "panel" or "terminal-toggle" or "layout" or "overview" or
        "switch-next" or "switch-prev" or "minimize" or "maximize" or "restore" or "screenshot" or "close-window" or
        "open-code" or "open-chrome" or "open-files" or "open-terminal" or "launch";
    private bool? ApplyNotebook(string action, LabWindow w)
    {
        if (!OnNotebook) return null;
        var n = w.Tab.Notebook;
        bool changed;
        switch (action)
        {
            case "notebook-down": changed = n.Move(1); break;
            case "notebook-up": changed = n.Move(-1); break;
            case "notebook-delete-arm": n.DeleteArmed = true; w.Status = "Нажмите D ещё раз, чтобы удалить ячейку."; return true;
            case "notebook-edit": case "notebook-leave": case "notebook-run": case "notebook-run-next":
            case "notebook-undo": case "notebook-redo": case "notebook-add-above": case "notebook-add-below":
            case "notebook-move-up": case "notebook-move-down": case "notebook-delete-cell":
            case "notebook-copy-cell": case "notebook-cut-cell": case "notebook-paste-cell":
            case "notebook-code": case "notebook-markdown":
                return NotebookCommand(w, action[9..]);
            case "notebook-all": n.SelectAll(); changed = true; break;
            case "notebook-copy": case "notebook-cut":
                int lo = Math.Min(n.Caret, n.Anchor), hi = Math.Max(n.Caret, n.Anchor);
                if (lo == hi) return false;
                Clipboard = n.Source(n.Selected)[lo..hi];
                if (action == "notebook-cut" && !n.Replace("")) return false;
                changed = true; break;
            case "notebook-paste": if (Clipboard.Length == 0) return false; changed = n.Replace(Clipboard); break;
            default: return NotebookGlobalAction(action) || action.StartsWith("win-", StringComparison.Ordinal) ? null : false;
        }
        if (changed) {
            if (action is "notebook-cut" or "notebook-paste")
                w.Body = n.Pack();
            w.Status = n.Describe(); Version++;
        }
        if (!changed && n.Problem.Length > 0) w.Status = n.Problem;
        return changed;
    }
    private bool NotebookText(string key, string text, bool extend, bool word)
    {
        var w = Active; var n = w.Tab.Notebook; if (!n.Editing || n.Busy) return false;
        n.ClearProblem();
        string src = n.Source(n.Selected); int caret = n.Caret, anchor = n.Anchor;
        int next;
        switch (key)
        {
            case "Home": next = LineStart(src, caret); break;
            case "End": next = LineEnd(src, caret); break;
            case "Left": case "Right":
                bool left = key == "Left";
                next = !extend && !word && anchor != caret ? (left ? Math.Min(anchor, caret) : Math.Max(anchor, caret)) :
                    word ? WordBoundary(src, caret, left) : left ? PreviousBoundary(src, caret) : NextBoundary(src, caret); break;
            case "Up": case "Down":
                int start = LineStart(src, caret), end = LineEnd(src, caret), column = caret - start;
                if (key == "Up") { if (start == 0) return false; next = Math.Min(start - 1, LineStart(src, start - 1) + column); }
                else { if (end == src.Length) return false; next = Math.Min(LineEnd(src, end + 1), end + 1 + column); }
                break;
            case "Backspace": case "Delete":
                if (word)
                {
                    int edge = caret != anchor ? anchor : key == "Backspace"
                        ? EditorWordEdge(src, caret, true) : EditorDeleteRightEdge(src, caret);
                    if (edge == caret) return true;
                    return Mark(n.ReplaceRange(Math.Min(caret, edge), Math.Max(caret, edge), ""));
                }
                if (anchor == caret)
                {
                    int edge = key == "Backspace" ? PreviousBoundary(src, caret) : NextBoundary(src, caret);
                    if (edge == caret) return false;
                    return Mark(n.ReplaceRange(Math.Min(caret, edge), Math.Max(caret, edge), ""));
                }
                return Mark(n.Replace(""));
            case "Enter": return Mark(n.Replace("\n"));
            default:
                if (string.IsNullOrEmpty(text) || text.Any(char.IsControl)) return false;
                return Mark(n.Replace(text));
        }
        n.Caret = InsertionBoundary(src, next); if (!extend) n.Anchor = n.Caret; Version++; return true;
        bool Mark(bool changed)
        {
            if (changed) { w.Body = n.Pack(); Version++; }
            w.Status = n.Describe();
            return changed;
        }
    }
}

internal static partial class LiveCourse
{
    private static LabTask NotebookKey(string id, string title, string key, string hint) =>
        new("notebook-" + id, title, key, hint, Action: "notebook-" + id);
    private static readonly LabTask[] NotebookKeys = [
        NotebookKey("down", "Выберите следующую ячейку", "Down", "В режиме выбора стрелка переходит к другой ячейке; код не меняется."),
        NotebookKey("up", "Вернитесь к предыдущей ячейке", "Up", "Слева от выбранной ячейки видна синяя полоса."),
        NotebookKey("edit", "Откройте выбранную ячейку для редактирования", "Enter", "Внутри ячейки появится текстовая каретка."),
        NotebookKey("leave", "Вернитесь к выбору ячеек", "Esc", "Текст останется в ячейке; стрелки снова выбирают ячейки."),
        NotebookKey("all", "Выделите содержимое только этой ячейки", "Ctrl+A", "Соседние ячейки нельзя изменять."),
        NotebookKey("run", "Выполните выбранную ячейку", "Ctrl+Enter", "Слева появится номер запуска; результат выражения виден под ячейкой."),
        NotebookKey("run-next", "Выполните данные и перейдите к ячейке расчёта", "Shift+Enter", "Ячейка выполняется, а выделение переходит на следующую. В конце добавится пустая ячейка."),
        NotebookKey("undo", "Отмените правку ячейки", "Ctrl+Z", "Отмена не запускает код."),
        NotebookKey("redo", "Повторите правку ячейки", "Ctrl+Shift+Z", "Проверьте содержимое ячейки."),
        NotebookKey("copy", "Скопируйте выделение", "Ctrl+C", "Копируется только выделенный текст ячейки."),
        NotebookKey("cut", "Вырежьте выделенный текст", "Ctrl+X", "Соседние ячейки остаются на месте."),
        NotebookKey("paste", "Вставьте в ячейку", "Ctrl+V", "Это общий учебный буфер обмена."),
        NotebookKey("add-above", "Добавьте ячейку выше", "A", "В режиме выбора A вставляет отдельную ячейку перед выбранной."),
        NotebookKey("add-below", "Добавьте ячейку ниже", "B", "В режиме выбора B вставляет отдельную ячейку после выбранной."),
        NotebookKey("move-up", "Переместите выбранную ячейку выше", "Alt+Up", "Данные и вывод перемещаются вместе с ячейкой."),
        NotebookKey("move-down", "Переместите выбранную ячейку ниже", "Alt+Down", "Граница документа не меняется при повторе."),
        NotebookKey("delete-cell", "Удалите выбранную ячейку", "D → D", "Дважды нажмите D в режиме выбора. Delete в тексте удаляет символ."),
        NotebookKey("copy-cell", "Скопируйте выбранную ячейку", "C", "Копируется ячейка, а не её прежний запуск."),
        NotebookKey("cut-cell", "Вырежьте выбранную ячейку", "X", "Содержимое ячейки останется в учебном буфере."),
        NotebookKey("paste-cell", "Вставьте копию ячейки ниже", "V", "Новая ячейка получит собственный результат при выполнении."),
        NotebookKey("markdown", "Сделайте выбранную ячейку Markdown", "M", "Пояснение не выполняется как код."),
        NotebookKey("code", "Сделайте выбранную ячейку кодом", "Y", "Ячейку снова можно выполнить.")
    ];
    internal static IEnumerable<LabTask> NotebookActions => NotebookKeys;
    internal static LabTask ResolveNotebook(string key, IEnumerable<string> held, LabScene s)
    {
        var n = s.Active.Tab.Notebook;
        if (s.ReplayLegacyNotebookActions)
            return NotebookKeys.Take(12).Where(t => t.Id is "notebook-run" or "notebook-run-next" ||
                (n.Editing ? t.Id is not ("notebook-down" or "notebook-up" or "notebook-edit") :
                    t.Id is "notebook-down" or "notebook-up" or "notebook-edit"))
                .FirstOrDefault(t => { var chord = Gesture.Parse(t.Keys).Chords[0]; return chord.Trigger == key && chord.FitsModifiers(held); });
        bool armed = n.DeleteArmed; n.DeleteArmed = false;
        bool plain = !held.Any(KeyChord.IsModifier);
        if (!n.Editing && key == "D" && plain)
            return armed ? NotebookKeys.First(t => t.Id == "notebook-delete-cell") :
                NotebookKey("delete-arm", "Подтвердите удаление клавишей D", "D", "");
        if (!n.Editing && key == "Z" && (plain || Gesture.Parse("Shift+Z").Chords[0].FitsModifiers(held)))
        {
            string binding = plain ? "Z" : "Shift+Z";
            return NotebookKeys.First(t => t.Id == (plain ? "notebook-undo" : "notebook-redo")) with { Keys = binding };
        }
        return NotebookKeys.Where(t => t.Id is "notebook-run" or "notebook-run-next" ||
            (n.Editing ? t.Id is "notebook-leave" or "notebook-all" or "notebook-undo" or "notebook-redo" or "notebook-copy" or "notebook-cut" or "notebook-paste" :
                t.Id is "notebook-down" or "notebook-up" or "notebook-edit" or "notebook-add-above" or "notebook-add-below" or
                    "notebook-move-up" or "notebook-move-down" or "notebook-copy-cell" or "notebook-cut-cell" or "notebook-paste-cell" or "notebook-code" or "notebook-markdown"))
            .FirstOrDefault(t => { var c = Gesture.Parse(t.Keys).Chords[0]; return c.Trigger == key && c.FitsModifiers(held); });
    }
    private static void NotebookMission(List<LiveMission> list, int level)
    {
        string id = level switch { 0 => "read", 1 => "repair", _ => "recheck" };
        string title = level switch { 0 => "Проверить расчёт из блокнота и передать результат",
            1 => "Исправить формулу в блокноте по заявке", _ => "Пересчитать сводку на новых данных" };
        var phases = new List<LivePhase> {
            new("Прочитайте заявку в app.py и откройте указанный блокнот", "quick-open notebook-file"),
            new("Выполните данные, затем расчёт. Сверьте вывод с целью из заявки",
                level == 0 ? "notebook-down notebook-run-next notebook-run" : "notebook-down notebook-run notebook-down notebook-run")
        };
        if (level == 1) phases.Add(new("Исправьте неверную формулу и повторно выполните только расчёт",
            "notebook-edit notebook-all notebook-type notebook-leave notebook-run"));
        if (level == 2) phases.Add(new("Добавьте запись из заявки и обновите обе ячейки по порядку",
            "notebook-up notebook-edit notebook-all notebook-type notebook-leave notebook-run notebook-down notebook-run"));
        phases.Add(new("Сохраните проверенный блокнот и передайте число в итоговый файл",
            "save quick-open notebook-report notebook-report-save"));
        list.Add(new("code", level, "code-notebook-" + id, title, phases.ToArray(), FinishAfterRoute: true, Curated: true,
            Prepare: s => s.PrepareNotebookCourse(level)));
    }
    private static LabTask NotebookTask(string token, LabScene s)
    {
        if (!s.NotebookCourse) return null;
        if (AdvancedNotebookTask(token, s) is LabTask expanded) return expanded;
        switch (token)
        {
            case "notebook-file": case "notebook-report":
                string file = token == "notebook-file" ? s.NotebookFile : s.NotebookReport;
                return Actions["quick-choose"] with { Title = "Откройте " + file + " из этого проекта", Text = file,
                    Hint = "Введите имя в быстром поиске файлов и подтвердите: " + file };
            case "notebook-type":
                return new(token, s.NotebookEditTarget.Contains('=') ? "Добавьте значение " + s.NotebookExtra + " к исходному списку"
                    : "Исправьте выражение: нужно получить " + s.NotebookBrief,
                    "", "Введите: " + s.NotebookEditTarget, s.NotebookEditTarget, token);
            case "notebook-report-save":
                return Actions["save"] with { Title = "Запишите число из вывода в " + s.NotebookReport + " и сохраните",
                    Hint = "Нужен только итог последнего расчёта: " + s.NotebookAnswer + ". Затем Ctrl+S.", Text = s.NotebookAnswer };
            case "notebook-run":
                var n = s.Active?.Tab?.Notebook;
                return NotebookKeys.First(t => t.Action == token) with { Title = n?.IsCode(n.Selected) == false ? "Завершите редактирование и прочитайте пояснение Markdown" :
                    s.AdvancedNotebook ? "Выполните выбранную ячейку и проверьте появившийся вывод" :
                    n?.Selected == 1 ? "Выполните ячейку данных: создайте список в ядре" : "Выполните ячейку расчёта и прочитайте число под ней" };
            default: return NotebookKeys.FirstOrDefault(t => t.Action == token);
        }
    }
}
