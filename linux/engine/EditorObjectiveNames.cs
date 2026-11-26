using System;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal LabTask DescribeEditorContext(LabTask task)
    {
        var w = Active;
        if (w?.App != "code" || w.Tab == null) return task;
        string file = NameOf(w.Url);
        var entries = Entries(w);
        var entry = entries.ElementAtOrDefault(w.OptionIndex);
        var pickedFile = w.Focus == "explorer" ? entries.ElementAtOrDefault(w.Item) : null;
        LabEntry Neighbor(int direction) => entries.Length == 0 ? null :
            entries[(w.OptionIndex + entries.Length + direction) % entries.Length];
        string selection = w.Focus == "body" && w.Selected ? Slice(w, w.Body) : "";
        string firstLine = selection.TrimEnd().Split('\n')[0].Trim();
        if (firstLine.Length > 42) firstLine = firstLine[..40] + "…";
        int current = w.Body[..Math.Clamp(w.Tab.Position.Caret,0,w.Body.Length)].Count(c => c == '\n') + 1;
        string target = ReleasePractice ? releaseCase.Target : file;
        string[] palette = ["Форматировать документ", "Переключить боковую панель", "Разделить редактор"];
        string paletteName = palette[Math.Clamp(w.OptionIndex, 0, palette.Length-1)];
        return task.Action switch {
            "palette-next" when !BrowserPracticeActive && !AdvancedNotebook => task with { Title = "Выберите в палитре «" + palette[(w.OptionIndex + 1) % palette.Length] + "»" },
            "palette-choose" when !BrowserPracticeActive && !AdvancedNotebook => task with { Title = "Выполните «" + paletteName + "» в " + file },
            "file-name-start" when pickedFile?.Kind == "file" => task with {
                Title = "Начните переименование файла «" + EditorLessonRelativePath(pickedFile.Path) + "» в проводнике",
                Hint = "Меняется имя файла в дереве проекта, не имя символа в редакторе." },
            "file-open" when pickedFile != null => task with { Title = (pickedFile.Kind=="folder" ? "Откройте папку «" : "Откройте файл «") + EditorLessonRelativePath(pickedFile.Path) + "»" },
            "project-search-next" or "symbol-next" or "code-reference-next" or "code-fix-next" when Neighbor(1) is LabEntry next =>
                task with { Title = "Выберите «" + next.Title + "»" },
            "project-search-prev" or "symbol-prev" or "code-reference-prev" or "code-fix-prev" when Neighbor(-1) is LabEntry prev =>
                task with { Title = "Выберите «" + prev.Title + "»" },
            "code-reference-open" when entry!=null => task with { Title = "Перейдите к вызову «" + entry.Title + "»" },
            "code-fix-apply" when entry!=null => task with { Title = "Примените в " + file + " исправление «" + entry.Title + "»" },
            "symbol-open" when entry != null => task with { Title = $"Перейдите к определению «{entry.Title}» в {file}" },
            "quick-choose" when entry != null => task with { Title = $"Откройте «{EditorLessonRelativePath(entry.Path)}» в редакторе" },
            "project-search-open" when entry != null => task with { Title = "Откройте совпадение «" + entry.Title + "»" },
            "select-line" => task with { Title = $"Выделите строку {current} в {file}" + (w.Selected && selection.EndsWith('\n') ? " вместе с предыдущей" : "") },
            "copy" when firstLine != "" => task with { Title = $"Скопируйте выделение из {file}: «{firstLine}»" },
            "cut" when firstLine != "" => task with { Title = $"Вырежьте выделение из {file}: «{firstLine}»" },
            "paste" when ReleasePractice => task with { Title = $"Замените выделенную строку в {target} блоком из буфера" },
            "save" => task with { Title = "Сохраните изменения в «" + file + "»" },
            "changes" when ReleasePractice => task with { Title = Terminal.GitChanges().Any(x => x.Staged)
                ? "Откройте Git: из коммита нужно исключить личный черновик " + releaseCase.Notes
                : "Откройте Git: проверьте сохранённую правку " + releaseCase.Target },
            _ => task
        };
    }

    internal LabTask DescribeNotebookObjective(LabTask task)
    {
        var w = Active;
        var notebook = w?.Tab?.Notebook;
        if (notebook == null || notebook.Count == 0) return task;
        int i = Math.Clamp(notebook.Selected + (task.Action == "notebook-up" ? -1 : task.Action == "notebook-down" ? 1 : 0),0,notebook.Count-1);
        string firstLine = notebook.Source(i).Split('\n').Select(t => t.Trim()).FirstOrDefault(t => t.Length != 0) ?? "";
        if (firstLine.Length > 42) firstLine = firstLine[..40] + "…";
        string name = (notebook.IsCode(i) ? "кода" : "Markdown") + " №" + (i+1) +
            (firstLine.Length==0 ? " (пустая)" : ": «" + firstLine + "»");
        return task.Action switch {
            "notebook-up" or "notebook-down" => task with { Title = "Выберите ячейку " + name },
            "notebook-move-down" when i+1<notebook.Count => task with { Title = "Переставьте ячейку " + name + $" после ячейки №{i+2}" },
            "notebook-move-up" when i>0 => task with { Title = "Переставьте ячейку " + name + $" перед ячейкой №{i}" },
            "notebook-edit" => task with { Title = "Войдите в редактирование ячейки " + name },
            "notebook-run" when task.Title.Contains("выбранную") => task with { Title = "Выполните ячейку " + name, Hint = "Сверьте вывод под этой ячейкой; старый вывод не подтверждает новый запуск." },
            "notebook-delete-cell" => task with { Title = "Удалите ячейку " + name, Hint = "Удаляется целая ячейка в командном режиме, а не символ в редакторе." },
            "notebook-markdown" => task with { Title = $"Оформите ячейку №{i+1} как Markdown" + (firstLine.Length==0 ? "" : ": «" + firstLine + "»") },
            _ => task
        };
    }
}
