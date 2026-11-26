using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LabTask(string Id, string Title, string Keys, string Hint, string Text = "", string Action = "", int Level = 0, string TargetPath = null)
{
    internal object Snapshot() => new { Id, Title, Keys, Hint, Text };
}

internal static partial class LabCatalog
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
        T("file-search-open", "Найдите файл по имени в этой папке", "Ctrl+F", "file-search-open"),
        T("file-search-win-alias", "Найдите файл по имени в этой папке", "Ctrl+F", "file-search-open"),
        T("file-search-close", "Закройте поиск и вернитесь в папку", "Esc", "file-search-close"),
        T("file-search-to-results", "Перейдите к результатам поиска", "Tab", "file-search-to-results"),
        T("file-search-to-field", "Вернитесь к поисковому запросу", "Shift+Tab", "file-search-to-field"),
        T("file-next", "Выберите следующий объект", "Down", "item-next"),
        T("file-prev", "Выберите предыдущий объект", "Up", "item-prev"),
        T("file-open", "Откройте выбранный объект", "Enter", "item-open"),
        T("file-copy", "Скопируйте выбранный файл", "Ctrl+C", "file-copy"),
        T("file-cut", "Подготовьте файл к переносу", "Ctrl+X", "file-cut"),
        T("file-paste", "Поместите файл в эту папку", "Ctrl+V", "file-paste"),
        T("file-undo", "Отмените последнее действие с файлом", "Ctrl+Z", "file-undo"),
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
        T("browser-tab-2", "Перейдите на вторую вкладку", "Ctrl+2", "browser-tab-2", 1),
        T("browser-tab-3", "Перейдите на третью вкладку", "Ctrl+3", "browser-tab-3", 1),
        T("browser-tab-4", "Перейдите на четвёртую вкладку", "Ctrl+4", "browser-tab-4", 1),
        T("browser-tab-5", "Перейдите на пятую вкладку", "Ctrl+5", "browser-tab-5", 1),
        T("browser-tab-6", "Перейдите на шестую вкладку", "Ctrl+6", "browser-tab-6", 1),
        T("browser-tab-7", "Перейдите на седьмую вкладку", "Ctrl+7", "browser-tab-7", 1),
        T("browser-tab-8", "Перейдите на восьмую вкладку", "Ctrl+8", "browser-tab-8", 1),
        T("browser-tabs-open", "Откройте поиск среди открытых вкладок", "Ctrl+Shift+A", "browser-tabs-open", 1),
        T("browser-tabs-next", "Выберите следующую открытую вкладку", "Down", "browser-tabs-next", 1),
        T("browser-tabs-prev", "Выберите предыдущую открытую вкладку", "Up", "browser-tabs-prev", 1),
        T("browser-tabs-choose", "Перейдите в выбранную открытую вкладку", "Enter", "browser-tabs-choose", 1),
        T("browser-review-read", "Найдите поле с контрольным кодом", "Enter", "browser-review-read", 1),
        T("browser-review-focus", "Переведите фокус в поле контрольного кода", "Tab", "browser-review-focus", 1),
        T("browser-review-submit", "Подтвердите код и адрес источника", "Enter", "browser-review-submit", 1),
        ..LaterShortcuts(),
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
    private static readonly HashSet<string> CodeIds = new(["file-undo", "file-next", "file-prev", "file-open", "file-cut", "file-copy", "file-paste", "file-name-start", "file-name-commit", "file-name-cancel", "document-start", "move-line-up", "move-line-down", "occurrence", "replace", "replace-field", "replace-one", "symbols", "symbol-next", "symbol-open", "changes", "focus-editor", "focus-second", "panel", "extensions", "select-line", "new-file", "close-tab", "next-tab", "prev-tab", "quick-open", "quick-next", "quick-choose", "palette", "palette-next", "palette-prev", "palette-choose", "goto-line", "explorer", "project-search", "sidebar", "split", "terminal-toggle", "save", "select-all", "copy", "cut", "paste", "undo", "redo", "comment", "delete-line", "find", "escape", "layout"]);
    static LabCatalog()
    {
        ChromeIds.UnionWith(Shortcuts.Where(task => task.Id.StartsWith("browser-", StringComparison.Ordinal)).Select(task => task.Id));
        CodeIds.UnionWith(Shortcuts.Where(task => task.Id.StartsWith("code-", StringComparison.Ordinal) || task.Id.StartsWith("project-search-", StringComparison.Ordinal)).Select(task => task.Id));
    }
    private static readonly HashSet<string> TerminalIds = new(Shortcuts.Where(task => task.Id.StartsWith("terminal-")).Select(task => task.Id).Concat(["layout", "escape"]));
    internal static IEnumerable<string> VisibleApps(string environment) =>
        Apps.Where(app => app != (environment == "windows" ? "ubuntu" : "windows"));

    internal static object Catalog(IEnumerable<LiveDraft> drafts, IEnumerable<ShortcutAttempt> history, string layout,
        bool neutralProject = false, string environment = "ubuntu")
    {
        var draftRows = (drafts ?? []).Select(d => d.App is "ubuntu" or "windows" or "workday" or "terminal"
            ? (object)new { d.App, d.Level, d.Mission, d.Guidance, d.AdaptiveGuide, desktopProfile = LabScene.DesktopEnvironment(d.DesktopEnvironment) }
            : new { d.App, d.Level, d.Mission, d.Guidance, d.AdaptiveGuide });
        var profiles = new[] { new { id = "ubuntu", title = "Ubuntu" }, new { id = "windows", title = "Windows 10" } };
        return new {
            desktopProfile = environment, desktopProfiles = profiles,
            recommendations = LiveStartingLevel.Recommend(history, layout, environment), drafts = draftRows,
            classes = VisibleApps(environment).Select(app => new {
                id = app, title = app == "terminal" && environment == "windows" ? "PowerShell" : Title(app), description = app switch {
                    "workday" => "Общее рабочее место, файлы и окна приложений",
                    "ubuntu" => "Окна, рабочие столы и системные сочетания",
                    "windows" => "Пуск, Проводник, окна и виртуальные рабочие столы Windows 10",
                    "chrome" => "Вкладки, навигация, поиск и адресная строка",
                    "code" => "Файлы, код, блокноты, панели и встроенный терминал",
                    _ => environment == "windows" ? "Windows PowerShell: журнал. Дополнительные задания Bash доступны в параметрах." :
                        "Ubuntu Bash: поиск сбоя и живой журнал"
                },
                desktopProfile = app is "windows" or "ubuntu" ? app : null,
                shortcuts = app == "windows" || app == "workday" && environment == "windows"
                    ? WindowsTasks.Concat(Shortcuts.Where(task => FilesIds.Contains(task.Id)))
                        .Select(task => (task.Id == "file-search-open" ? task with { Keys = "Ctrl+E" } : task).Snapshot())
                    : Shortcuts.Where(task => InApp(task, app))
                        .Where(task => task.Id != "file-search-win-alias")
                        .Concat(app == "code" ? LiveCourse.NotebookActions : []).Select(task => task.Snapshot())
            }),
            missions = (neutralProject ? NeutralProject.MissionCatalog : LiveCourse.Missions)
                .Where(m => LabScene.MissionEnvironment(m) is null || LabScene.MissionEnvironment(m) == environment)
                .Select(m => new { m.App, m.Level, m.Id,
                    Title = environment == "windows" && m.App == "terminal" && m.Id is not (LiveCourse.PracticalPowerShellId or LiveCourse.PowerShellDiskId or LiveCourse.PowerShellDiskRepeatId)
                        ? "Bash (учебный Ubuntu) · " + m.Title : m.Title,
                    m.Curated, desktopProfile = LabScene.MissionEnvironment(m) }),
            duration = 0, blockSize = 20
        };
    }

    private static bool InApp(LabTask task, string app) => app == "workday" ||
        app == "ubuntu" && task.Action.StartsWith("file-search-", StringComparison.Ordinal) ||
        (app switch { "ubuntu" => UbuntuIds, "chrome" => ChromeIds, "code" => CodeIds, _ => TerminalIds }).Contains(task.Id);
}
