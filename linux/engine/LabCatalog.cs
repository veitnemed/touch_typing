using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LabTask(string Id, string Title, string Keys, string Hint, string Text = "", string Action = "", int Level = 0)
{
    internal object Snapshot() => new { Id, Title, Keys, Hint, Text };
}

internal static class LabCatalog
{
    internal static readonly string[] Apps = ["ubuntu", "windows", "chrome", "code", "terminal", "workday"];
    internal static string Title(string app) => app switch { "workday" => "Рабочий день", "ubuntu" => "Ubuntu", "windows" => "Windows 10", "chrome" => "Chrome", "code" => "VS Code", _ => "Терминал" };
    private static LabTask T(string id, string title, string keys, string action, int level = 0, string hint = "") =>
        new(id, title, keys, hint.Length == 0 ? "Удерживайте модификаторы и нажмите основную клавишу." : hint, Action: action, Level: level);
    internal static readonly LabTask[] Shortcuts =
    [
        T("overview", "Откройте обзор рабочего стола", "Super", "overview"),
        T("switch-next", "Перейдите к следующему окну", "Alt+Tab", "switch-next"),
        T("switch-prev", "Перейдите к предыдущему окну", "Alt+Shift+Tab", "switch-prev", 1),
        T("open-terminal", "Откройте терминал", "Ctrl+Alt+T", "open-terminal"),
        T("open-chrome", "Откройте Chrome", "Super+1", "open-chrome"),
        T("open-code", "Откройте VS Code", "Super+2", "open-code"),
        T("open-files", "Откройте домашнюю папку", "Super+4", "open-files", 1),
        T("close-window", "Закройте активное окно", "Alt+F4", "close-window"),
        T("maximize", "Разверните активное окно", "Super+Up", "maximize"),
        T("restore", "Восстановите размер окна", "Super+Down", "restore"),
        T("tile-left", "Разместите окно слева", "Super+Left", "tile-left", 1),
        T("tile-right", "Разместите окно справа", "Super+Right", "tile-right", 1),
        T("minimize", "Сверните активное окно", "Super+H", "minimize", 1),
        T("desktop", "Покажите рабочий стол", "Super+D", "desktop"),
        T("workspace-next", "Перейдите на следующий рабочий стол", "Super+PageDown", "workspace-next", 1),
        T("workspace-prev", "Перейдите на предыдущий рабочий стол", "Super+PageUp", "workspace-prev", 1),
        T("move-next", "Переместите окно на следующий рабочий стол", "Super+Shift+PageDown", "move-next", 2),
        T("move-prev", "Переместите окно на предыдущий рабочий стол", "Super+Shift+PageUp", "move-prev", 2),
        T("layout", "Переключите учебную раскладку", "Super+Space", "layout"),
        T("screenshot", "Откройте панель снимка экрана", "Print", "screenshot"),
        T("debug-window-shot", "Скопируйте снимок активного терминала", "Alt+Print", "debug-window-shot"),
        T("debug-next-line", "Перейдите на строку с формулой", "Down", "debug-next-line"),
        T("debug-report-end", "Перейдите в конец учебного отчёта", "Ctrl+End", "debug-report-end"),
        T("screenshot-confirm", "Подтвердите снимок экрана", "Enter", "screenshot-confirm"),
        T("copy-shot", "Скопируйте учебный снимок", "Ctrl+C", "copy-shot"),
        T("code-clone-command", "Выполните Git: Clone", "Enter", "code-clone-command"),
        T("code-clone-confirm", "Подтвердите HTTPS-ссылку", "Enter", "code-clone-confirm"),
        T("code-definition", "Перейдите к определению функции", "F12", "code-definition"),
        T("code-back", "Вернитесь к исходному вызову", "Ctrl+Alt+-", "code-back"),
        T("code-references", "Покажите использования функции", "Shift+F12", "code-references"),
        T("code-reference-next", "Выберите следующий вызов", "Down", "code-reference-next"),
        T("code-reference-prev", "Выберите предыдущий вызов", "Up", "code-reference-prev"),
        T("code-reference-open", "Откройте выбранный вызов", "Enter", "code-reference-open"),
        T("code-rename", "Откройте переименование функции", "F2", "code-rename"),
        T("code-rename-confirm", "Введите scale_sample и нажмите Enter", "Enter", "code-rename-confirm", hint: "Введите новое имя латиницей. Enter применит правки, Esc отменит."),
        T("code-rename-cancel", "Отмените переименование без изменений", "Esc", "code-rename-cancel"),
        T("escape", "Закройте панель и вернитесь к работе", "Esc", "escape"),
        T("history", "Найдите страницу в истории", "Ctrl+H", "history"),
        T("downloads", "Откройте загрузки", "Ctrl+J", "downloads"),
        T("bookmark-manager", "Откройте сохранённые страницы", "Ctrl+Shift+O", "bookmark-manager"),
        T("link-next", "Перейдите к следующей ссылке", "Tab", "item-next"),
        T("link-prev", "Вернитесь к предыдущей ссылке", "Shift+Tab", "item-prev"),
        T("link-open", "Откройте выбранную ссылку", "Enter", "item-open"),
        T("file-name-start", "Переименуйте выбранный файл", "F2", "file-name-start"),
        T("file-name-commit", "Подтвердите новое имя", "Enter", "file-name-commit"),
        T("file-name-cancel", "Отмените переименование", "Esc", "file-name-cancel"),
        T("file-next", "Выберите следующий объект", "Down", "item-next"),
        T("file-prev", "Выберите предыдущий объект", "Up", "item-prev"),
        T("file-open", "Откройте выбранный объект", "Enter", "item-open"),
        T("file-copy", "Скопируйте выбранный файл", "Ctrl+C", "file-copy"),
        T("file-cut", "Подготовьте файл к переносу", "Ctrl+X", "file-cut"),
        T("file-paste", "Поместите файл в эту папку", "Ctrl+V", "file-paste"),
        T("file-parent", "Перейдите на уровень выше", "Alt+Up", "file-parent"),
        T("file-home", "Откройте домашнюю папку", "Alt+Home", "file-home"),
        T("file-back", "Вернитесь в предыдущую папку", "Alt+Left", "file-back"),
        T("file-forward", "Перейдите вперёд по истории папок", "Alt+Right", "file-forward"),
        T("trash", "Переместите выбранный файл в корзину", "Delete", "trash"),
        T("restore-trash", "Отмените перемещение в корзину", "Ctrl+Z", "restore-trash"),
        T("new-tab", "Создайте вкладку", "Ctrl+T", "new-tab"),
        T("new-file", "Создайте файл", "Ctrl+N", "new-tab"),
        T("close-tab", "Закройте текущую вкладку", "Ctrl+W", "close-tab"),
        T("restore-tab", "Восстановите закрытую вкладку", "Ctrl+Shift+T", "restore-tab", 1),
        T("next-tab", "Перейдите на следующую вкладку", "Ctrl+Tab", "next-tab"),
        T("prev-tab", "Перейдите на предыдущую вкладку", "Ctrl+Shift+Tab", "prev-tab", 1),
        T("first-tab", "Перейдите на первую вкладку", "Ctrl+1", "first-tab", 1),
        T("last-tab", "Перейдите на последнюю вкладку", "Ctrl+9", "last-tab", 1),
        T("url", "Выделите адрес страницы", "Ctrl+L", "url"),
        T("navigate-pasted", "Откройте вставленный адрес", "Enter", "navigate-pasted", hint: "Подтвердите адрес, скопированный из учебной страницы."),
        T("find", "Откройте поиск в документе", "Ctrl+F", "find"),
        T("find-next", "Перейдите к следующему совпадению", "Ctrl+G", "find-next", 1),
        T("refresh", "Обновите страницу", "Ctrl+R", "refresh"),
        T("back", "Вернитесь на предыдущую страницу", "Alt+Left", "back", 1),
        T("forward", "Откройте следующую страницу истории", "Alt+Right", "forward", 1),
        T("fullscreen", "Переключите полноэкранный режим", "F11", "fullscreen"),
        T("zoom-in", "Увеличьте масштаб страницы", "Ctrl+=", "zoom-in", 1),
        T("zoom-out", "Уменьшите масштаб страницы", "Ctrl+-", "zoom-out", 1),
        T("zoom-reset", "Верните масштаб 100%", "Ctrl+0", "zoom-reset", 1),
        T("bookmark", "Сохраните страницу в закладки", "Ctrl+D", "bookmark", 1),
        T("bookmarks", "Переключите панель закладок", "Ctrl+Shift+B", "bookmarks", 2),
        T("exact-find-submit", "Введите draft и подтвердите запрос", "Enter", "exact-find-submit"),
        T("exact-find-next", "Выберите следующее совпадение", "F3", "exact-find-next"),
        T("exact-find-prev", "Выберите предыдущее совпадение", "Shift+F3", "exact-find-prev"),
        T("exact-call-home", "Поставьте курсор на вызов функции", "Home", "exact-call-home"),
        T("quick-open", "Откройте поиск файла", "Ctrl+P", "quick-open"),
        T("quick-next", "Выберите следующий файл", "Down", "quick-next"),
        T("quick-choose", "Откройте выбранный файл", "Enter", "quick-choose"),
        T("palette", "Откройте палитру команд", "Ctrl+Shift+P", "palette"),
        T("palette-next", "Выберите следующую команду", "Down", "palette-next"),
        T("palette-choose", "Выполните выбранную команду", "Enter", "palette-choose"),
        T("goto-line", "Откройте переход к строке", "Ctrl+G", "goto-line", 1),
        T("explorer", "Откройте проводник проекта", "Ctrl+Shift+E", "explorer", 1),
        T("project-search", "Откройте поиск по проекту", "Ctrl+Shift+F", "project-search", 1),
        T("move-line-up", "Поднимите строку", "Alt+Up", "move-line-up"),
        T("move-line-down", "Перенесите строку ниже", "Alt+Down", "move-line-down"),
        T("document-start", "Перейдите в начало файла", "Ctrl+Home", "document-start"),
        T("occurrence", "Выделите вхождение", "Ctrl+D", "occurrence"),
        T("replace", "Откройте замену", "Ctrl+H", "replace"),
        T("replace-field", "Перейдите к тексту замены", "Tab", "replace-field"),
        T("replace-one", "Замените найденный фрагмент", "Enter", "replace-one"),
        T("symbols", "Найдите символ в файле", "Ctrl+Shift+O", "symbols"),
        T("symbol-next", "Выберите символ ниже", "Down", "symbol-next"),
        T("symbol-open", "Перейдите к определению", "Enter", "symbol-open"),
        T("changes", "Просмотрите изменения проекта", "Ctrl+Shift+G", "changes"),
        T("focus-second", "Перейдите ко второй группе", "Ctrl+2", "focus-second"),
        T("focus-editor", "Вернитесь к первой группе редактора", "Ctrl+1", "focus-editor"),
        T("terminal-search", "Найдите команду в истории", "Ctrl+R", "terminal-search"),
        T("terminal-word-left", "К предыдущему слову", "Alt+B", "word-left"),
        T("terminal-word-right", "К следующему слову", "Alt+F", "word-right"),
        T("panel", "Переключите нижнюю панель", "Ctrl+J", "panel"),
        T("extensions", "Откройте расширения", "Ctrl+Shift+X", "extensions"),
        T("select-line", "Выделите текущую строку", "Ctrl+L", "select-line"),
        T("sidebar", "Переключите боковую панель", "Ctrl+B", "sidebar"),
        T("split", "Разделите редактор", "Ctrl+\\", "split", 1),
        T("terminal-toggle", "Переключите встроенный терминал", "Ctrl+`", "terminal-toggle", 1),
        T("project-search-submit", "Подтвердите поисковый запрос", "Enter", "project-search-submit",
            hint: "Введите фрагмент ошибки латиницей. Подтвердите запрос клавишей Enter."),
        T("project-search-next", "Выберите следующую строку", "Down", "project-search-next",
            hint: "Сверьте файл с путём в ошибке. Пример в archive.py менять не нужно."),
        T("project-search-prev", "Выберите предыдущую строку", "Up", "project-search-prev"),
        T("project-search-open", "Откройте выбранную строку", "Enter", "project-search-open"),
        T("project-search-repair", "Исправьте значение и сохраните конфигурацию", "Ctrl+S", "project-search-repair",
            hint: "Нажмите End, затем Backspace, введите 5 и сохраните Ctrl+S. В других файлах ничего не меняйте."),
        T("save", "Сохраните текущий файл", "Ctrl+S", "save"),
        T("select-all", "Выделите текст", "Ctrl+A", "select-all"),
        T("copy", "Скопируйте текст", "Ctrl+C", "copy"),
        T("cut", "Вырежьте выделенный текст", "Ctrl+X", "cut"),
        T("paste", "Вставьте текст", "Ctrl+V", "paste"),
        T("terminal-copy", "Скопируйте выделенный фрагмент", "Ctrl+Shift+C", "terminal-copy"),
        T("terminal-yank", "Верните удалённую часть команды", "Ctrl+Y", "terminal-yank", 1),
        T("terminal-paste", "Вставьте команду в терминал", "Ctrl+Shift+V", "paste"),
        T("undo", "Отмените изменение", "Ctrl+Z", "undo"),
        T("redo", "Повторите изменение", "Ctrl+Shift+Z", "redo", 1),
        T("comment", "Переключите комментарий строки", "Ctrl+/", "comment", 1),
        T("delete-line", "Удалите строку", "Ctrl+Shift+K", "delete-line", 2),
        T("terminal-clear", "Очистите экран терминала", "Ctrl+L", "terminal-clear"),
        T("terminal-cancel", "Отмените ввод команды", "Ctrl+C", "terminal-cancel"),
        T("terminal-erase", "Очистите строку ввода", "Ctrl+U", "terminal-erase", 1),
        T("terminal-kill", "Удалите текст после курсора", "Ctrl+K", "terminal-kill", 2),
        T("terminal-home", "Перейдите в начало команды", "Ctrl+A", "terminal-home", 1),
        T("terminal-end", "Перейдите в конец команды", "Ctrl+E", "terminal-end", 1),
        T("terminal-history-up", "Восстановите предыдущую команду", "Up", "terminal-history-up"),
        T("terminal-history-down", "Перейдите вперёд по истории команд", "Down", "terminal-history-down", 1),
        T("terminal-submit", "Выполните введённую команду", "Enter", "terminal-submit"),
        T("code-problem-next", "Перейдите к диагностике из панели проблем", "F8", "code-problem-next", 1),
        T("code-fix-open", "Откройте быстрые исправления в строке диагностики", "Ctrl+.", "code-fix-open", 1),
        T("code-fix-next", "Выберите исправление, сохраняющее дробную часть", "Down", "code-fix-next", 1),
        T("code-fix-prev", "Выберите предыдущее исправление", "Up", "code-fix-prev", 1),
        T("code-fix-apply", "Примените выбранное исправление", "Enter", "code-fix-apply", 1),
        T("code-fix-cancel", "Закройте быстрые исправления", "Esc", "code-fix-cancel", 1),
        T("switch-group-next", "Выберите другое окно того же приложения", "Alt+`", "switch-group-next", 1,
            "Удерживайте переключатель, выберите нужный файл по подписи и отпустите модификатор."),
        T("switch-group-prev", "Вернитесь к предыдущему окну приложения", "Alt+Shift+`", "switch-group-prev", 1),
        T("gnome-left-super", "Нажмите и отпустите левый Super (⊞)", "LSuper", "gnome-left-super", 1,
            "На MateBook клавиша ⊞ слева от Alt. Буква Z (Я) находится выше."),
    ];
    private static readonly LabTask[] WindowsTasks =
    [
        T("win-start", "Откройте меню «Пуск» клавишей ⊞", "Super", "win-start"),
        T("win-explorer", "Откройте Проводник", "Super+E", "win-explorer"),
        T("win-explorer-parent", "Перейдите в родительскую папку", "Alt+Up", "win-explorer-parent"),
        T("win-overlay-next", "Выберите следующий вариант", "Down", "win-overlay-next"),
        T("win-overlay-prev", "Выберите предыдущий вариант", "Up", "win-overlay-prev"),
        T("win-overlay-open", "Подтвердите выбранный вариант", "Enter", "win-overlay-open"),
        T("win-cancel", "Закройте меню без выбора", "Esc", "win-cancel"),
        T("win-left", "Прикрепите окно слева", "Super+Left", "win-left"),
        T("win-right", "Прикрепите окно справа", "Super+Right", "win-right"),
        T("win-maximize", "Разверните окно", "Super+Up", "win-maximize"),
        T("win-down", "Восстановите или сверните окно", "Super+Down", "win-down"),
        T("win-close", "Закройте активное окно", "Alt+F4", "win-close"),
        T("win-desktop", "Переключите показ рабочего стола", "Super+D", "win-desktop"),
        T("win-task-view", "Откройте представление задач", "Super+Tab", "win-task-view"),
        T("win-new-desktop", "Создайте виртуальный рабочий стол", "Super+Ctrl+D", "win-new-desktop"),
        T("win-close-desktop", "Закройте текущий виртуальный рабочий стол", "Super+Ctrl+F4", "win-close-desktop"),
        T("win-prev-desktop", "Перейдите на левый рабочий стол", "Super+Ctrl+Left", "win-prev-desktop"),
        T("win-next-desktop", "Перейдите на правый рабочий стол", "Super+Ctrl+Right", "win-next-desktop"),
        T("win-switch", "Переключитесь на предыдущее окно", "Alt+Tab", "win-switch"),
        T("win-layout", "Переключите учебную раскладку", "Super+Space", "win-layout"),
    ];
    internal static IEnumerable<LabTask> AllTasks => Shortcuts.Concat(WindowsTasks);
    internal static LabTask WindowsStartTask => WindowsTasks[0];
    private static readonly HashSet<string> UbuntuIds = new([
        "overview", "switch-next", "switch-prev", "open-terminal", "open-chrome", "open-code", "open-files",
        "close-window", "maximize", "restore", "tile-left", "tile-right", "minimize", "desktop",
        "workspace-next", "workspace-prev", "move-next", "move-prev", "layout",
        "screenshot", "screenshot-confirm", "copy-shot", "escape",
        "switch-group-next", "switch-group-prev", "gnome-left-super"
    ]);
    private static readonly HashSet<string> FilesIds = new(Shortcuts.Where(task => task.Id.StartsWith("file-") || task.Id is "trash" or "restore-trash").Select(task => task.Id));
    private static readonly HashSet<string> ChromeIds = new(["history", "downloads", "bookmark-manager", "link-next", "link-prev", "link-open", "new-tab", "close-tab", "restore-tab", "next-tab", "prev-tab", "first-tab", "last-tab", "url", "navigate-pasted", "find", "find-next", "refresh", "back", "forward", "fullscreen", "zoom-in", "zoom-out", "zoom-reset", "bookmark", "bookmarks", "select-all", "copy", "cut", "paste", "escape", "layout"]);
    private static readonly HashSet<string> CodeIds = new(["file-next", "file-prev", "file-open", "file-cut", "file-copy", "file-paste", "file-name-start", "file-name-commit", "file-name-cancel", "document-start", "move-line-up", "move-line-down", "occurrence", "replace", "replace-field", "replace-one", "symbols", "symbol-next", "symbol-open", "changes", "focus-editor", "focus-second", "panel", "extensions", "select-line", "new-file", "close-tab", "next-tab", "prev-tab", "quick-open", "quick-next", "quick-choose", "palette", "palette-next", "palette-choose", "goto-line", "explorer", "project-search", "sidebar", "split", "terminal-toggle", "save", "select-all", "copy", "cut", "paste", "undo", "redo", "comment", "delete-line", "find", "escape", "layout"]);
    static LabCatalog()
    {
        CodeIds.UnionWith(Shortcuts.Where(task => task.Id.StartsWith("code-", StringComparison.Ordinal) || task.Id.StartsWith("project-search-", StringComparison.Ordinal)).Select(task => task.Id));
    }
    private static readonly HashSet<string> TerminalIds = new(Shortcuts.Where(task => task.Id.StartsWith("terminal-")).Select(task => task.Id).Concat(["layout", "escape"]));
    internal static IEnumerable<string> VisibleApps(string environment) =>
        Apps.Where(app => app != (environment == "windows" ? "ubuntu" : "windows"));

    internal static object Catalog(IEnumerable<LiveDraft> drafts, IEnumerable<ShortcutAttempt> history, string layout,
        bool neutralProject = false, string environment = "ubuntu")
    {
        var draftRows = (drafts ?? []).Select(d => d.App is "ubuntu" or "windows" or "workday"
            ? (object)new { d.App, d.Level, d.Mission, d.Guidance, desktopProfile = LabScene.DesktopEnvironment(d.DesktopEnvironment) }
            : new { d.App, d.Level, d.Mission, d.Guidance });
        var profiles = new[] { new { id = "ubuntu", title = "Ubuntu" }, new { id = "windows", title = "Windows 10" } };
        return new {
            desktopProfile = environment, desktopProfiles = profiles,
            recommendations = LiveStartingLevel.Recommend(history, layout, environment), drafts = draftRows,
            classes = VisibleApps(environment).Select(app => new {
                id = app, title = Title(app), description = app switch {
                    "workday" => "Общее рабочее место, файлы и окна приложений",
                    "ubuntu" => "Окна, рабочие столы и системные сочетания",
                    "windows" => "Пуск, Проводник, окна и виртуальные рабочие столы Windows 10",
                    "chrome" => "Вкладки, навигация, поиск и адресная строка",
                    "code" => "Файлы, код, блокноты, панели и встроенный терминал",
                    _ => "Сочетания, команды и учебная файловая система"
                },
                desktopProfile = app is "windows" or "ubuntu" ? app : null,
                shortcuts = app == "windows" || app == "workday" && environment == "windows"
                    ? WindowsTasks.Concat(Shortcuts.Where(task => FilesIds.Contains(task.Id))).Select(task => task.Snapshot())
                    : Shortcuts.Where(task => InApp(task, app))
                        .Concat(app == "code" ? LiveCourse.NotebookActions : []).Select(task => task.Snapshot())
            }),
            missions = (neutralProject ? NeutralProject.MissionCatalog : LiveCourse.Missions)
                .Where(m => LabScene.MissionEnvironment(m) is null || LabScene.MissionEnvironment(m) == environment)
                .Select(m => new { m.App, m.Level, m.Id, m.Title, m.Curated, desktopProfile = LabScene.MissionEnvironment(m) }),
            duration = 0, blockSize = 20
        };
    }

    private static bool InApp(LabTask task, string app) => app == "workday" || (app switch { "ubuntu" => UbuntuIds, "chrome" => ChromeIds, "code" => CodeIds, _ => TerminalIds }).Contains(task.Id);

    internal static bool Available(LabTask task, LabScene scene)
    {
        if (task.Id.StartsWith("win-", StringComparison.Ordinal)) return scene.WindowsActionAvailable(task.Action);
        var window = scene.Active;
        bool terminal = window != null && (window.App == "terminal" || window.Focus == "terminal");
        bool fileList = window?.App == "files" && window.Focus == "body" || window?.App == "code" && window.Focus == "explorer";
        return task.Id switch
        {
            "file-name-commit" or "file-name-cancel" => window?.Focus == "file-name",
            "file-name-start" => fileList && scene.Entries(window).ElementAtOrDefault(window.Item)?.Kind == "file",
            "file-next" or "file-prev" or "file-paste" => fileList,
            "code-clone-command" => scene.BrowserPracticeActive && window?.Focus == "palette" && window.Input.Trim().Equals("Git: Clone", StringComparison.OrdinalIgnoreCase),
            "code-clone-confirm" => scene.BrowserPracticeActive && window?.Focus == "clone-url",
            "code-problem-next" or "code-fix-open" or "code-fix-next" or "code-fix-prev" or
            "code-fix-apply" or "code-fix-cancel" => scene.DiagnosticActionAvailable(task.Action),
            "document-start" or "move-line-up" or "move-line-down" or "occurrence" => window?.App == "code" && window.Focus == "body",
            "exact-find-submit" or "exact-find-next" or "exact-find-prev" => scene.ExactEdit && window?.Focus == "find",
            "exact-call-home" => scene.ExactEdit && window?.App == "code" && window.Focus == "body",
            "project-search-submit" => window?.Focus == "project-search" && !string.IsNullOrWhiteSpace(window.Input),
            "project-search-next" or "project-search-prev" or "project-search-open" => window?.Focus == "project-results",
            "project-search-repair" => scene.SearchRepairExercise && window?.App == "code" && window.Focus == "body" && window.Tab?.SavedBody != window.Body,
            "code-definition" or "code-references" or "code-rename" => window?.App == "code" && window.Focus == "body",
            "code-back" => window?.App == "code" && window.Focus == "body" && window.SymbolBack.Count > 0,
            "code-reference-prev" or "code-reference-next" or "code-reference-open" => window?.Focus == "symbol-references",
            "code-rename-cancel" or "code-rename-confirm" => window?.Focus == "symbol-rename",
            "replace-field" => window?.Focus == "replace-find",
            "replace-one" => window?.Focus == "replace-with",
            "symbol-next" or "symbol-open" => window?.Focus == "symbols",
            "file-open" or "file-copy" or "file-cut" => fileList && window.Item >= 0,
            "trash" => window?.App == "files" && window.Focus == "body" && window.Item >= 0,
            "link-open" => window?.App == "chrome" && window.Focus == "body" && window.Item >= 0,
            "link-next" or "link-prev" => window?.App == "chrome" && window.Focus == "body",
            "switch-next" or "switch-prev" => scene.Windows.Count(item => item.Workspace == scene.Workspace)
                >= (scene.GnomeWindows && window == null ? 1 : 2),
            "switch-group-next" or "switch-group-prev" => scene.GnomePractice == "same-app" && scene.GnomeGroupSize >= 2,
            "workspace-next" when scene.GnomePractice == "workspaces" => scene.Workspace + 1 < scene.WorkspaceCount,
            "workspace-prev" when scene.GnomePractice == "workspaces" => scene.Workspace > 0,
            "move-next" when scene.GnomePractice == "workspaces" => window != null && scene.Workspace + 1 < scene.WorkspaceCount,
            "move-prev" when scene.GnomePractice == "workspaces" => window != null && scene.Workspace > 0,
            "close-window" => window != null,
            "open-chrome" => window?.App != "chrome",
            "open-code" => window?.App != "code",
            "open-terminal" => window?.App != "terminal",
            "open-files" => window?.App != "files",
            "maximize" => window != null && window.State != "maximized",
            "restore" => window != null && window.State != "normal",
            "tile-left" => window != null && (scene.GnomeWindows || window.State != "left"),
            "tile-right" => window != null && (scene.GnomeWindows || window.State != "right"),
            "minimize" => window != null && (scene.GnomeWindows || scene.Windows.Any(
                item => item.Workspace == scene.Workspace && item.Id != window.Id && item.State != "minimized")),
            "move-next" or "move-prev" => window != null,
            "copy-shot" => scene.ScreenshotReady && !scene.ScreenshotCopied,
            "screenshot-confirm" => scene.Screenshot,
            "debug-window-shot" => scene.DebugDay && window?.App == "terminal" && scene.DebugTestCurrent,
            "debug-next-line" or "debug-report-end" => scene.DebugDay && window?.App == "code" && window.Focus == "body",
            "escape" => scene.Overview || scene.Screenshot || scene.ScreenshotReady || window?.Focus is not (null or "body"),
            "new-tab" => window?.App == "chrome" && window.Tabs.Count < 6,
            "url" => window?.App == "chrome" && !scene.Overview && !scene.Switcher && !scene.Screenshot,
            "navigate-pasted" => window?.App == "chrome" && window.Focus == "url" && !string.IsNullOrWhiteSpace(window.Input),
            "refresh" or "fullscreen" or "bookmarks" => window?.App == "chrome",
            "zoom-in" => window?.App == "chrome" && window.Zoom < 200,
            "zoom-out" => window?.App == "chrome" && window.Zoom > 50,
            "bookmark" => window?.App == "chrome" && !window.SavedBookmarks.Contains(window.Url),
            "new-file" => window?.App == "code" && window.Tabs.Count < 6,
            "close-tab" or "next-tab" or "prev-tab" => window?.Tabs.Count > 1,
            "first-tab" => window?.ActiveTab > 0,
            "last-tab" => window != null && window.ActiveTab < window.Tabs.Count - 1,
            "restore-tab" => window?.ClosedTabs.Count > 0,
            "back" => window?.Tab?.HistoryIndex > 0,
            "forward" => window?.Tab != null && window.Tab.HistoryIndex < window.Tab.History.Count - 1,
            "zoom-reset" => window?.Zoom != 100,
            "find" => window != null && !terminal && window.Focus != "find",
            "find-next" => window?.Find.Length > 0,
            "panel" or "extensions" or "quick-open" or "palette" or "goto-line" or "explorer" or "project-search" or "sidebar" or "split" or "terminal-toggle" => window?.App == "code",
            "quick-next" or "quick-choose" => window?.App == "code" && window.Focus == "quick-open" && scene.Entries(window).Length > 0,
            "palette-next" or "palette-choose" => window?.App == "code" && window.Focus == "palette",
            "save" => window?.App == "code" && window.Tab?.Body != window.Tab?.SavedBody,
            "undo" => window?.Tab?.Undo.Count > 0,
            "redo" => window?.Tab?.Redo.Count > 0,
            "select-line" or "comment" or "delete-line" => window?.App == "code" && window.Body.Length > 0 && window.Focus == "body",
            "select-all" => window != null && !window.Selected && (window.Focus == "body" ? window.Body.Length > 0 : window.Input.Length > 0),
            "copy" => window != null && (window.Selected || window.App == "code" && window.Focus == "body" && window.Body.Length > 0),
            "cut" => window != null && (window.Focus == "body" ? window.App == "code" && window.Body.Length > 0 : window.Selected),
            "paste" => window != null && scene.Clipboard.Length > 0 && (window.App == "code" || window.App == "chrome" && window.Focus != "body"),
            "terminal-copy" => terminal && window.Selected,
            "terminal-clear" or "terminal-yank" => terminal,
            "terminal-paste" => terminal && scene.Clipboard.Length > 0,
            "terminal-cancel" or "terminal-erase" or "terminal-kill" or "terminal-home" or "terminal-end" => terminal && scene.TerminalInput.Length > 0,
            "terminal-submit" => terminal && window.Focus != "reverse-search" && scene.TerminalInput.Length > 0,
            "terminal-history-up" => terminal && scene.CanHistoryUp,
            "terminal-history-down" => terminal && scene.CanHistoryDown,
            _ => true
        };
    }

    internal static LabTask Resolve(string key, IEnumerable<string> held, LabScene scene)
    {
        if (scene.WindowsDesktop)
        {
            var windowsTask = WindowsTasks.Skip(1).Where(task => Available(task, scene)).FirstOrDefault(task =>
            {
                var chord = Gesture.Parse(task.Keys).Chords[0];
                return chord.Trigger == key && chord.FitsModifiers(held);
            });
            if (windowsTask != null || scene.DesktopOverlay.Length > 0 || scene.ShowDesktop) return windowsTask;
        }
        string app = scene.Active?.App ?? "ubuntu";
        bool terminal = app == "terminal" || scene.Active?.Focus is "terminal" or "reverse-search";
        HashSet<string> context = terminal ? TerminalIds : app switch
        { "chrome" => ChromeIds, "code" => CodeIds, "files" => FilesIds, _ => UbuntuIds };
        int Priority(LabTask task)
        {
            if ((scene.Active?.Focus is "file-name" or "explorer") && task.Id.StartsWith("file-", StringComparison.Ordinal)) return 3;
            if (task.Id.StartsWith("code-fix-", StringComparison.Ordinal) && scene.DiagnosticActionAvailable(task.Action))
                return 2;
            if (task.Id == "copy-shot" && scene.ScreenshotReady && !scene.ScreenshotCopied) return 2;
            if (task.Id == "screenshot-confirm" && scene.Screenshot) return 2;
            if (scene.GnomePractice is "workspaces" or "same-app" && LabScene.GnomeProfileActions.Contains(task.Id) &&
                scene.DesktopProfile.Any(entry => entry.StartsWith(task.Id + "=", StringComparison.Ordinal))) return 3;
            return context.Contains(task.Id) ? 1 : 0;
        }
        if (app == "chrome")
        {
            string alias = key == "D" && Gesture.Parse("Alt+D").Chords[0].FitsModifiers(held) ? "url" :
                (key is "PageUp" or "PageDown") && Gesture.Parse("Ctrl+Tab").Chords[0].FitsModifiers(held)
                    ? (key == "PageUp" ? "prev-tab" : "next-tab") : null;
            if (alias != null)
            {
                var task = Shortcuts.FirstOrDefault(task => task.Id == alias && Available(task, scene));
                return task == null ? null : task with { Keys = alias == "url" ? "Alt+D" : "Ctrl+" + key };
            }
        }
        var ordered = Shortcuts.Select(scene.WithBinding).Where(task => task.Keys.Length > 0).Where(task => task.Id != "copy-shot" || scene.ScreenshotReady && !scene.ScreenshotCopied)
            .Where(task => task.Id != "screenshot-confirm" || scene.Screenshot)
            .OrderByDescending(Priority);
        return ordered.FirstOrDefault(task =>
        {
            var chord = Gesture.Parse(task.Keys).Chords[0];
            bool trigger = chord.Trigger == null ? KeyChord.IsModifier(key) : chord.Trigger == key;
            if (!trigger || !chord.FitsModifiers(held) || !Available(task, scene)) return false;
            if (scene.Active?.Focus == "file-name") return task.Id is "file-name-commit" or "file-name-cancel" or "select-all" or "copy" or "cut" or "paste";
            if (scene.DebugDay && task.Id.StartsWith("debug-", StringComparison.Ordinal)) return true;
            if (scene.ExactEdit && task.Id.StartsWith("exact-", StringComparison.Ordinal)) return true;
            if (task.Id == "escape" && scene.BrowserResearchActive && context.Contains(task.Id))
                return true; // Esc closes the app's find bar when no Windows overlay has focus.
            if (UbuntuIds.Contains(task.Id) || task.Id is "switch-group-next" or "switch-group-prev") return !scene.WindowsDesktop;
            return context.Contains(task.Id) || terminal && app == "code" && task.Id is "terminal-toggle" or "panel" or "escape";
        });
    }
}
