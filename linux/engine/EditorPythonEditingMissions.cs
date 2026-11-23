using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static LabTask PythonEdit(string id, string title, string keys, string hint) =>
        new("python-edit-" + id, title, keys, hint, Action: "python-edit-" + id);
    private static readonly LabTask[] PythonEditActions =
    [
        PythonEdit("write", "Наберите расчёт на новой строке", "", "Печатайте на предложенном отступе; Enter для зачёта не нужен.") with { Text = LabScene.PythonEditStatement },
        PythonEdit("newline", "Продолжите блок после if", "Enter", "Проверьте, что новая строка начинается на один уровень глубже."),
        PythonEdit("indent", "Сдвиньте выделенные строки вправо", "Tab", "Выделение должно остаться на тех же строках."),
        PythonEdit("outdent", "Верните отступ выделенных строк", "Shift+Tab", "Снимите ровно один уровень отступа."),
        PythonEdit("word-left", "Поставьте курсор перед словом", "Ctrl+Left", "Перейдите влево сразу на слово."),
        PythonEdit("word-right", "Перейдите к концу слова", "Ctrl+Right", "Перейдите вправо сразу на слово."),
        PythonEdit("select-word-left", "Выделите слово слева", "Ctrl+Shift+Left", "Сохраните якорь справа от слова."),
        PythonEdit("select-word-right", "Выделите слово справа", "Ctrl+Shift+Right", "Сохраните якорь слева от слова."),
        PythonEdit("erase-left", "Удалите слово слева", "Ctrl+Backspace", "Сочетание удаляет слово сразу, без посимвольного стирания."),
        PythonEdit("erase-right", "Удалите слово справа", "Ctrl+Delete", "Проверьте, что курсор не сдвинулся вправо."),
        PythonEdit("insert-below", "Вставьте строку ниже", "Ctrl+Enter", "Сочетание оставляет исходную строку целой, даже из середины выражения."),
        PythonEdit("insert-above", "Вставьте строку выше", "Ctrl+Shift+Enter", "Сочетание не должно разрезать выражение."),
        PythonEdit("up", "Перейдите на строку выше", "Up", "Не меняйте текст."),
        PythonEdit("left", "Перейдите внутрь строки", "Left", "Новая строка должна вставляться без разрыва выражения."),
        PythonEdit("home", "Перейдите к началу строки", "Home", "Начните выделение с левого края."),
        PythonEdit("end", "Перейдите к концу выражения", "End", "Встаньте за последним словом."),
        PythonEdit("select-down", "Расширьте выделение на одну строку", "Shift+Down", "Проверьте, что выделение не захватывает лишнюю строку.")
    ];

    private static LabTask PythonEditTask(string token) => PythonEditActions.FirstOrDefault(t => t.Id == token);

    internal static LabTask ResolvePythonEdit(string key, IEnumerable<string> held, LabScene scene)
    {
        if (scene.ReplayLegacyPythonEditing || scene.Active?.App != "code" || scene.Active.Focus != "body" ||
            scene.Active.Tab == null || scene.Overview || scene.Switcher || scene.Screenshot) return null;
        return PythonEditActions.FirstOrDefault(task =>
        {
            // Plain navigation remains uncredited outside this dedicated mission.
            if (task.Keys.Length == 0) return false;
            if (!scene.PythonEditingExercise && task.Keys is "Enter" or "Up" or "Left" or "Home" or "End" or "Shift+Down") return false;
            var chord = Gesture.Parse(task.Keys).Chords[0];
            return chord.Trigger == key && chord.FitsModifiers(held);
        });
    }

    private static void PythonEditingMissions(List<LiveMission> missions) =>
        missions.Add(new("code", 1, "code-python-edit", "Отредактировать Python-функцию без мыши",
        [
            new("Продолжите блок с правильным отступом", "python-edit-newline python-edit-write python-edit-left"),
            new("Вставьте строки выше и ниже, не ломая выражение", "python-edit-insert-below undo python-edit-insert-above undo"),
            new("Выделите и удалите целое слово", "python-edit-up python-edit-up python-edit-up python-edit-up python-edit-end python-edit-select-word-left python-edit-erase-left undo python-edit-word-right python-edit-erase-left undo python-edit-word-left python-edit-erase-right undo python-edit-select-word-right"),
            new("Измените отступы и порядок блока", "python-edit-home python-edit-select-down python-edit-select-down python-edit-indent python-edit-outdent move-line-up move-line-down undo save")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PreparePythonEditExercise()));
}

internal sealed partial class LabScene
{
    private const string PythonEditRoot = Project + "/task-list";
    internal const string PythonEditPath = PythonEditRoot + "/tasks.py";
    internal const string PythonEditStatement = "cost = subtotal + fee + tax + marker";
    internal const string PythonEditSource = "def total_cost(unit_price, quantity, flat_fee):\n" +
        "    marker = flat_fee\n" +
        "    subtotal = unit_price * quantity\n" +
        "    tax = 2\n" +
        "    fee = 1\n" +
        "    if quantity > 0:\n" +
        "        return cost\n" +
        "    return 0\n";
    internal void PreparePythonEditExercise()
    {
        PythonEditingExercise = true;
        Terminal.CreateFolder(PythonEditRoot);
        var files = new Dictionary<string, string>
        {
            ["app.py"] = "from tasks import total_cost\n\nprint(total_cost(4, 3, 2))\n",
            ["tasks.py"] = PythonEditSource,
            ["storage.py"] = "def format_cost(cost):\n    return f'{cost:.2f}'\n",
            ["test_tasks.py"] = "from tasks import total_cost\n\nassert total_cost(4, 3, 2) == 17\n"
        };
        foreach (var (name, source) in files)
        {
            string path = PythonEditRoot + "/" + name;
            Terminal.StoreFile(path, source);
            originalFiles[path] = source;
        }
        OpenDocument(PythonEditPath);
        Active.Cursor = PythonEditSource.IndexOf("    if quantity", StringComparison.Ordinal) + "    if quantity > 0:".Length;
    }

    internal object PythonEditEvidence()
    {
        var w = Active;
        return new { Layout, w?.App, w?.Focus, w?.Url, w?.Body, w?.Anchor, w?.Cursor,
            saved = w?.Tab?.SavedBody, disk = Terminal.ReadFile(PythonEditPath) };
    }
}
