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
    internal static readonly string[] Apps = ["ubuntu", "chrome", "code", "terminal"];
    internal static string Title(string app) => app switch { "ubuntu" => "Ubuntu", "chrome" => "Chrome", "code" => "VS Code", _ => "Терминал" };
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
        T("screenshot-confirm", "Подтвердите снимок экрана", "Enter", "screenshot-confirm"),
        T("copy-shot", "Скопируйте учебный снимок", "Ctrl+C", "copy-shot"),
        T("escape", "Закройте панель и вернитесь к работе", "Esc", "escape"),
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
        T("quick-open", "Откройте поиск файла", "Ctrl+P", "quick-open"),
        T("quick-next", "Выберите следующий файл", "Down", "quick-next"),
        T("quick-choose", "Откройте выбранный файл", "Enter", "quick-choose"),
        T("palette", "Откройте палитру команд", "Ctrl+Shift+P", "palette"),
        T("palette-next", "Выберите следующую команду", "Down", "palette-next"),
        T("palette-choose", "Выполните выбранную команду", "Enter", "palette-choose"),
        T("goto-line", "Откройте переход к строке", "Ctrl+G", "goto-line", 1),
        T("explorer", "Откройте проводник проекта", "Ctrl+Shift+E", "explorer", 1),
        T("project-search", "Откройте поиск по проекту", "Ctrl+Shift+F", "project-search", 1),
        T("sidebar", "Переключите боковую панель", "Ctrl+B", "sidebar"),
        T("split", "Разделите редактор", "Ctrl+\\", "split", 1),
        T("terminal-toggle", "Переключите встроенный терминал", "Ctrl+`", "terminal-toggle", 1),
        T("save", "Сохраните текущий файл", "Ctrl+S", "save"),
        T("select-all", "Выделите текст", "Ctrl+A", "select-all"),
        T("copy", "Скопируйте текст", "Ctrl+C", "copy"),
        T("cut", "Вырежьте выделенный текст", "Ctrl+X", "cut"),
        T("paste", "Вставьте текст", "Ctrl+V", "paste"),
        T("terminal-copy", "Скопируйте вывод терминала", "Ctrl+Shift+C", "copy"),
        T("terminal-cut", "Вырежьте ввод терминала", "Ctrl+Shift+X", "cut", 1),
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
    ];
    private static readonly HashSet<string> UbuntuIds = new(Shortcuts.Take(23).Select(task => task.Id));
    private static readonly HashSet<string> ChromeIds = new(["new-tab", "close-tab", "restore-tab", "next-tab", "prev-tab", "first-tab", "last-tab", "url", "navigate-pasted", "find", "find-next", "refresh", "back", "forward", "fullscreen", "zoom-in", "zoom-out", "zoom-reset", "bookmark", "bookmarks", "select-all", "copy", "cut", "paste", "escape", "layout"]);
    private static readonly HashSet<string> CodeIds = new(["new-file", "close-tab", "next-tab", "prev-tab", "quick-open", "quick-next", "quick-choose", "palette", "palette-next", "palette-choose", "goto-line", "explorer", "project-search", "sidebar", "split", "terminal-toggle", "save", "select-all", "copy", "cut", "paste", "undo", "redo", "comment", "delete-line", "find", "escape", "layout"]);
    private static readonly HashSet<string> TerminalIds = new(Shortcuts.Where(task => task.Id.StartsWith("terminal-")).Select(task => task.Id).Concat(["layout", "escape"]));
    internal static object Catalog() => new { classes = Apps.Select(app => new { id = app, title = Title(app), description = app switch
        { "ubuntu" => "Окна, рабочие столы и системные сочетания", "chrome" => "Вкладки, навигация, поиск и адресная строка", "code" => "Файлы, код, панели и встроенный терминал", _ => "Сочетания, команды и учебная файловая система" },
        shortcuts = Shortcuts.Where(task => InApp(task, app)).Select(task => task.Snapshot()) }), duration = 180, blockSize = 20 };
    private static bool InApp(LabTask task, string app) => (app switch { "ubuntu" => UbuntuIds, "chrome" => ChromeIds, "code" => CodeIds, _ => TerminalIds }).Contains(task.Id);

    internal static LabTask[] Candidates(string app, LabScene scene, int level)
    {
        bool integratedTerminal = app == "code" && scene.Active?.Focus == "terminal";
        var tasks = Shortcuts.Where(task => (integratedTerminal ? TerminalIds.Contains(task.Id) || task.Id is "terminal-toggle" or "escape" : InApp(task, app)) && task.Level <= level && Available(task, scene)).ToList();
        var window = scene.Active;
        if (app == "ubuntu" && scene.Overview && scene.Layout == "EN")
            foreach (string appName in new[] { "chrome", "code", "terminal", "files" })
                tasks.Add(new LabTask("launch/" + appName, "Найдите и запустите " + (appName == "files" ? "Файлы" : Title(appName)), "", "Введите название в поиске обзора и нажмите Enter.", appName, "launch"));
        if (window != null && scene.Layout == "EN" && !scene.Overview)
        {
            if (app == "terminal" && (window.App == "terminal" || window.Focus == "terminal"))
            {
                if (scene.TerminalInput.Length == 0)
                {
                    foreach (string command in scene.Terminal.SuggestedCommands(level))
                        tasks.Add(new LabTask("command/" + command, "Выполните команду «" + command + "»", "",
                            "Наберите команду и нажмите Enter. Команда выполняется в учебном терминале.", command, "command"));
                }
            }
            else if (app == "chrome" && window.Focus == "url" && (window.Input.Length == 0 || window.Selected))
                tasks.Add(new LabTask("navigate", "Откройте документацию Python", "", "Введите адрес и нажмите Enter.", "https://docs.python.org/3/", "navigate"));
            else if ((app == "chrome" || app == "code") && (window.Focus == "find" || window.Focus == "project-search") && (window.Input.Length == 0 || window.Selected))
                {
                string word = System.Text.RegularExpressions.Regex.Matches(window.Body, @"[A-Za-z]{3,}").Select(match => match.Value).FirstOrDefault() ?? "";
                if (word.Length > 0) tasks.Add(new LabTask("find-text", "Найдите слово «" + word + "»", "", "Введите текст поиска и нажмите Enter.", word, "find-text"));
            }
            else if (app == "code" && window.Focus == "quick-open" && (window.Input.Length == 0 || window.Selected))
                tasks.Add(new LabTask("open-file", "Откройте файл app.py", "", "Введите имя файла и нажмите Enter.", "app.py", "open-file"));
            else if (app == "code" && window.Focus == "palette" && (window.Input.Length == 0 || window.Selected))
                tasks.Add(new LabTask("palette-command", "Выполните команду Format Document", "", "Введите название и нажмите Enter.", "Format Document", "palette-command"));
            else if (app == "code" && window.Focus == "goto-line" && (window.Input.Length == 0 || window.Selected))
                tasks.Add(new LabTask("goto-number", "Перейдите к первой строке", "", "Введите номер и нажмите Enter.", "1", "goto-number"));
            else if (app == "code" && window.App == "code" && window.Focus == "body" && window.Body.Length == 0)
                tasks.Add(new LabTask("type-code", "Введите Python-код", "", "Введите строку кода. Символы проверяются в учебном документе.", "fft_values = np.fft.fft(signal)", "type-code"));
        }
        return tasks.ToArray();
    }

    internal static bool Available(LabTask task, LabScene scene)
    {
        var window = scene.Active;
        bool terminal = window != null && (window.App == "terminal" || window.Focus == "terminal");
        return task.Id switch
        {
            "switch-next" or "switch-prev" => scene.Windows.Count(item => item.Workspace == scene.Workspace) >= 2,
            "close-window" => window != null,
            "open-chrome" => window?.App != "chrome",
            "open-code" => window?.App != "code",
            "open-terminal" => window?.App != "terminal",
            "open-files" => window?.App != "files",
            "maximize" => window != null && window.State != "maximized",
            "restore" => window != null && window.State != "normal",
            "tile-left" => window != null && window.State != "left",
            "tile-right" => window != null && window.State != "right",
            "minimize" => window != null && scene.Windows.Count(item => item.Workspace == scene.Workspace && item.Id != window.Id && item.State != "minimized") > 0,
            "move-next" or "move-prev" => window != null,
            "copy-shot" => scene.ScreenshotReady && !scene.ScreenshotCopied,
            "screenshot-confirm" => scene.Screenshot,
            "escape" => scene.Overview || scene.Screenshot || scene.ScreenshotReady || window?.Focus is not (null or "body"),
            "new-tab" => window?.App == "chrome" && window.Tabs.Count < 6,
            "url" => window?.App == "chrome" && window.Focus != "url" && window.Url != "about:blank",
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
            "quick-open" or "palette" or "goto-line" or "explorer" or "project-search" or "sidebar" or "split" or "terminal-toggle" => window?.App == "code",
            "quick-next" or "quick-choose" => window?.App == "code" && window.Focus == "quick-open" && window.Tabs.Count > 1,
            "palette-next" or "palette-choose" => window?.App == "code" && window.Focus == "palette",
            "save" => window?.App == "code" && window.Tab?.Body != window.Tab?.SavedBody,
            "undo" => window?.Undo.Count > 0,
            "redo" => window?.Redo.Count > 0,
            "comment" or "delete-line" => window?.App == "code" && window.Body.Length > 0 && window.Focus == "body",
            "select-all" => window != null && !window.Selected && (window.Focus == "body" ? window.Body.Length > 0 : window.Input.Length > 0),
            "copy" => window != null && window.Selected && (window.Focus == "body" ? window.Body.Length > 0 : window.Input.Length > 0),
            "cut" => window != null && window.Selected && (window.Focus == "body" ? window.App == "code" && window.Body.Length > 0 : window.Input.Length > 0),
            "paste" => window != null && scene.Clipboard.Length > 0 && (window.App == "code" && window.Focus == "body" || window.App == "chrome" && window.Focus == "url"),
            "terminal-copy" or "terminal-clear" => terminal,
            "terminal-paste" => terminal && scene.TerminalInput.Length == 0 && scene.Clipboard.Length > 0,
            "terminal-cut" or "terminal-cancel" or "terminal-erase" or "terminal-kill" or "terminal-home" or "terminal-end" => terminal && scene.TerminalInput.Length > 0,
            "terminal-submit" => terminal && scene.TerminalInput.Length > 0,
            "terminal-history-up" => terminal && scene.CanHistoryUp,
            "terminal-history-down" => terminal && scene.CanHistoryDown,
            _ => true
        };
    }

    internal static LabTask Resolve(string key, IEnumerable<string> held, LabScene scene)
    {
        string app = scene.Active?.App ?? "ubuntu";
        bool terminal = app == "terminal" || scene.Active?.Focus == "terminal";
        HashSet<string> context = terminal ? TerminalIds : app switch
        { "chrome" => ChromeIds, "code" => CodeIds, _ => UbuntuIds };
        int Priority(LabTask task)
        {
            if (task.Id == "copy-shot" && scene.ScreenshotReady && !scene.ScreenshotCopied) return 2;
            if (task.Id == "screenshot-confirm" && scene.Screenshot) return 2;
            return context.Contains(task.Id) ? 1 : 0;
        }
        var ordered = Shortcuts.Where(task => task.Id != "copy-shot" || scene.ScreenshotReady && !scene.ScreenshotCopied)
            .Where(task => task.Id != "screenshot-confirm" || scene.Screenshot)
            .OrderByDescending(Priority);
        return ordered.FirstOrDefault(task =>
        {
            var chord = Gesture.Parse(task.Keys).Chords[0];
            bool trigger = chord.Trigger == null ? KeyChord.IsModifier(key) : chord.Trigger == key;
            if (!trigger || !chord.FitsModifiers(held) || !Available(task, scene)) return false;
            if (UbuntuIds.Contains(task.Id)) return true;
            return context.Contains(task.Id) || terminal && app == "code" && task.Id is "terminal-toggle" or "escape";
        });
    }
}
