using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LabScenario(string Id, string Title, LabTask[] Steps);

/// <summary>Short, connected work stories. Every story is rehearsed on a copy before it is offered.</summary>
internal static class LabScenarios
{
    private static readonly Dictionary<string, LabTask> Tasks = LabCatalog.Shortcuts.ToDictionary(task => task.Id);

    private static LabScenario Story(string id, string title, params string[] steps) =>
        new(id, title, steps.Select(step => Tasks[step]).ToArray());

    private static LabTask Command(string value) => new("command/" + value, "Выполните «" + value + "»", "",
        "Наберите команду в учебном терминале и нажмите Enter.", value, "command");

    private static LabScenario Commands(string id, string title, params string[] commands) =>
        new(id, title, commands.Select(Command).ToArray());

    internal static LabScenario[] Eligible(string app, LabScene scene, int level)
    {
        var stories = new List<LabScenario>();
        var window = scene.Active;
        if (app == "ubuntu")
        {
            if (window == null)
                stories.Add(Story("ubuntu-start", "Начало рабочего дня", "open-chrome", "maximize", "restore"));
            else
            {
                if (level >= 1 && window.App == "chrome" && window.State == "normal")
                    stories.Add(Story("ubuntu-place", "Разместить и восстановить окно", "maximize", "tile-left", "restore", "tile-right", "restore"));
                if (level >= 1 && window.App == "chrome" && !scene.Has("code") && scene.Windows.Count(item => item.Workspace == scene.Workspace) == 1)
                    stories.Add(Story("ubuntu-windows", "Работа с двумя окнами", "open-code", "switch-next", "switch-prev", "minimize", "open-code", "close-window"));
                if (level >= 2 && window.App == "chrome" && scene.Workspace == 0 && !scene.Has("terminal"))
                    stories.Add(Story("ubuntu-workspaces", "Перенести окно между рабочими столами", "workspace-next", "open-terminal", "move-next",
                        "workspace-prev", "workspace-next", "close-window", "workspace-prev", "workspace-prev"));
                if (window.App == "chrome")
                    stories.Add(Story("ubuntu-close", "Закрыть окно и открыть снова", "close-window", "open-chrome"));
            }
            if (!scene.Screenshot && !scene.Overview && !scene.ShowDesktop)
            {
                stories.Add(Story("ubuntu-capture", "Снимок в учебный буфер", "screenshot", "screenshot-confirm", "copy-shot"));
                stories.Add(Story("ubuntu-overview", "Раскладка и обзор", "layout", "layout", "overview", "escape"));
                stories.Add(Story("ubuntu-desktop", "Показать и вернуть рабочий стол", "desktop", "desktop"));
            }
        }
        else if (app == "chrome" && window?.App == "chrome" && !scene.Overview && !scene.Screenshot && !scene.ShowDesktop)
        {
            if (window.Tabs.Count < 6)
                stories.Add(Story("chrome-tab-basic", "Создать и закрыть вкладку", "new-tab", "close-tab"));
            if (window.Tabs.Count < 6 && window.Focus == "body" && window.Url != "about:blank")
            {
                stories.Add(Story("chrome-address-basic", "Открыть адрес через буфер", "url", "copy", "new-tab", "paste", "navigate-pasted", "close-tab"));
                stories.Add(Story("chrome-address-cut", "Вырезать и восстановить адрес", "url", "cut", "paste", "navigate-pasted"));
            }
            if (window.Focus == "body")
                stories.Add(Story("chrome-find-basic", "Найти и вернуться на страницу", "find", "escape", "refresh"));
            if (level >= 1 && window.Tabs.Count is >= 2 and <= 4)
                stories.Add(Story("chrome-tabs", "Закрыть и восстановить вкладку", "new-tab", "close-tab", "restore-tab", "prev-tab", "next-tab", "close-tab"));
            if (level >= 1 && window.Tabs.Count < 6 && window.Focus == "body" && window.Url != "about:blank")
                stories.Add(Story("chrome-address", "Скопировать адрес и открыть его в новой вкладке", "url", "copy", "new-tab", "paste", "navigate-pasted", "back", "forward", "close-tab"));
            if (level >= 1 && window.Focus == "body" && window.Zoom == 100)
                stories.Add(Story("chrome-reading", "Поиск, масштаб и обновление страницы", "find", "escape", "zoom-in", "zoom-in", "zoom-out", "zoom-reset", "refresh"));
            if (window.State == "normal")
                stories.Add(Story("chrome-fullscreen", "Полный экран и возврат", "fullscreen", "fullscreen"));
            if (level >= 1 && window.Tabs.Count > 1)
                stories.Add(window.ActiveTab == 0
                    ? Story("chrome-tab-jump", "Перейти к крайней вкладке и назад", "last-tab", "first-tab")
                    : Story("chrome-tab-jump", "Перейти к крайней вкладке и назад", "first-tab", "last-tab"));
            if (window.Focus == "body" && window.Body.Length > 0)
                stories.Add(Story("chrome-copy", "Выделить и скопировать страницу", "select-all", "copy"));
            if (level >= 1 && window.Focus == "body" && window.Url != "about:blank" && !window.SavedBookmarks.Contains(window.Url))
                stories.Add(Story("chrome-bookmark", "Закладка и панель закладок", "bookmark", "bookmarks", "bookmarks"));
        }
        else if (app == "code" && window?.App == "code" && !scene.Overview && !scene.Screenshot && !scene.ShowDesktop)
        {
            if (window.Focus == "body" && window.Body.Length > 0 && window.Tabs.Count < 6)
            {
                stories.Add(Story("code-copy-basic", "Копия кода в новом файле", "select-all", "copy", "new-file", "paste", "save", "close-tab"));
                stories.Add(Story("code-cut", "Вырезать и восстановить документ", "select-all", "cut", "paste"));
                if (level >= 1)
                    stories.Add(Story("code-copy", "Копия с отменой и повтором", "select-all", "copy", "new-file", "paste", "save", "undo", "redo", "close-tab"));
            }
            if (window.Focus == "body")
            {
                stories.Add(Story("code-sidebar", "Скрыть и показать панель", "sidebar", "sidebar"));
                stories.Add(Story("code-find-basic", "Открыть поиск и вернуться", "find", "escape"));
                if (level >= 1)
                {
                    stories.Add(Story("code-panels", "Проводник и разделение редактора", "explorer", "sidebar", "sidebar", "split", "split"));
                    stories.Add(Story("code-search", "Открыть поиск и вернуться в редактор", "find", "escape", "project-search", "escape"));
                    stories.Add(Story("code-terminal", "Встроенный терминал", "terminal-toggle", "terminal-clear", "terminal-toggle"));
                }
                if (level >= 1 && window.Tabs.Count > 1)
                    stories.Add(Story("code-quick-open", "Открыть другой файл без мыши", "quick-open", "quick-next", "quick-choose"));
                stories.Add(Story("code-palette", "Команда через палитру", "palette", "palette-next", "palette-choose", "sidebar"));
                if (level >= 1 && window.Body.Length > 0)
                    stories.Add(Story("code-edit", "Правка, сохранение и отмена", "comment", "save", "undo", "redo", "comment", "save"));
            }
        }
        else if (app == "terminal" && window?.App == "terminal")
        {
            if (scene.Layout == "RU") stories.Add(Story("terminal-layout", "Вернуться к английской раскладке", "layout"));
            else
            {
                stories.Add(Commands("terminal-basics", "Осмотреть рабочее место", "pwd", "ls", "whoami"));
                stories.Add(Commands("terminal-files", "Создать, проверить и убрать учебный файл", "mkdir -p practice", "touch practice/note.txt",
                    "echo ready > practice/note.txt", "cat practice/note.txt", "cp practice/note.txt practice/copy.txt",
                    "mv practice/copy.txt practice/draft.txt", "rm practice/draft.txt"));
                stories.Add(Commands("terminal-search", "Найти данные проекта", "cd ~/project", "grep -n ERROR ~/project/logs/app.log",
                    "head -n 2 ~/project/notes.txt", "wc -l ~/project/notes.txt", "cd ~"));
                if (scene.Clipboard == "pwd" && scene.TerminalInput.Length == 0)
                {
                    stories.Add(Story("terminal-keys-basic", "Вставить и повторить команду", "terminal-paste", "terminal-submit", "terminal-history-up", "terminal-cancel"));
                    if (level >= 1)
                        stories.Add(Story("terminal-keys", "Вставить и исправить команду", "terminal-paste", "terminal-home", "terminal-end",
                            "terminal-cut", "terminal-paste", "terminal-submit", "terminal-history-up", "terminal-cancel"));
                }
                if (level >= 1)
                {
                    stories.Add(Commands("terminal-packages", "Установить и убрать учебный пакет", "sudo apt update", "sudo apt install tree", "tree", "sudo apt remove tree"));
                    stories.Add(Commands("terminal-restart", "Проверить процессы и перезагрузку", "ps", "sudo reboot", "pwd"));
                }
                if (level >= 2)
                {
                    stories.Add(Commands("terminal-git", "Первый коммит проекта", "sudo apt install git", "cd ~/project", "git init",
                        "git status", "git add .", "git commit -m 'Save lesson'", "git log --oneline", "cd ~"));
                    stories.Add(Commands("terminal-git-review", "Проверить историю проекта", "cd ~/project", "git status", "git log --oneline", "git branch", "cd ~"));
                }
            }
        }
        return stories.Where(story => Works(story, scene, app, level)).ToArray();
    }

    private static bool Works(LabScenario story, LabScene scene, string app, int level)
    {
        var trial = scene.Clone();
        foreach (var task in story.Steps)
        {
            if (task.Level > level) return false;
            if (task.Action == "command")
            {
                if (trial.Active?.App != "terminal" || trial.Layout != "EN" || trial.TerminalInput.Length != 0) return false;
                trial.TerminalInput = task.Text; trial.Active.Cursor = task.Text.Length;
                if (!trial.Text("Enter", "") || !trial.Terminal.LastSuccess) return false;
            }
            else
            {
                if (!LabCatalog.Available(task, trial) || !trial.Apply(task.Action)) return false;
            }
            if (trial.Windows.Count > (app == "ubuntu" ? 4 : 1) || trial.Active?.Tabs.Count > 6) return false;
        }
        return true;
    }
}
