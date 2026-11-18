using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio
{
    internal static class WorkbenchTasks
    {
        private static readonly string[] Reports = { "budget", "weekly", "sales", "delivery", "review", "timeline", "metrics" };
        private static readonly string[] Topics = { "brief", "feedback", "onboarding", "support", "quality", "roadmap", "research" };
        private static readonly Func<Random, WorkbenchTask>[] Windows =
        {
            RenameFile, OpenFolder, NewFolder, SwitchWindows, OpenExplorer, ShowDesktop,
            r => TransferFile(r, false), r => TransferFile(r, true), AppSearch, OpenSettings, OpenTaskManager, SnapWindow, CloseWindow, RunDialog
        };
        private static readonly Func<Random, WorkbenchTask>[] Chrome =
        {
            BrowsePage, FindOnPage, CloseRestoreTab, SwitchTabs, History, Downloads, UseHistory, Reload, OpenBookmarks, JumpToTab
        };
        private static readonly Func<Random, WorkbenchTask>[] Code =
        {
            OpenFile, SaveFile, GoToLine, SearchProject, ToggleTerminal, ToggleExplorer, OpenPalette, EditAndSave, CommentLine, CloseEditor, FormatCode
        };

        public static IEnumerable<Func<Random, WorkbenchTask>> Get(WorkbenchArea area, int difficulty)
        {
            if (area == WorkbenchArea.Shortcuts)
            {
                var common = new[]
                {
                    Shortcut("Ctrl+C", "Копировать выделенное"), Shortcut("Ctrl+V", "Вставить из буфера"),
                    Shortcut("Ctrl+X", "Вырезать выделенное"), Shortcut("Ctrl+Z", "Отменить последнее действие"),
                    Shortcut("Ctrl+A", "Выбрать всё"), Shortcut("Win+E", "Открыть Проводник"),
                    Shortcut("Alt+Tab", "Переключиться между окнами"), Shortcut("Win+R", "Открыть «Выполнить»"),
                    Shortcut("F2", "Переименовать файл"), Shortcut("Ctrl+L", "Выбрать адресную строку", WorkbenchArea.Chrome),
                    Shortcut("Ctrl+T", "Открыть вкладку", WorkbenchArea.Chrome), Shortcut("Ctrl+W", "Закрыть вкладку", WorkbenchArea.Chrome),
                    Shortcut("Ctrl+Shift+T", "Вернуть закрытую вкладку", WorkbenchArea.Chrome), Shortcut("Ctrl+F", "Найти на странице", WorkbenchArea.Chrome),
                    Shortcut("Ctrl+S", "Сохранить файл", WorkbenchArea.Code), Shortcut("Ctrl+P", "Найти и открыть файл", WorkbenchArea.Code),
                    Shortcut("Ctrl+Shift+P", "Открыть палитру команд", WorkbenchArea.Code), Shortcut("Ctrl+G", "Перейти к строке", WorkbenchArea.Code),
                    Shortcut("Ctrl+/", "Переключить комментарий", WorkbenchArea.Code)
                };
                return common.Select(task => new Func<Random, WorkbenchTask>(r => Custom(task)));
            }
            int limit = difficulty == 0 ? 6 : int.MaxValue;
            if (area == WorkbenchArea.Windows) return Windows.Take(limit);
            if (area == WorkbenchArea.Chrome) return Chrome.Take(limit);
            if (area == WorkbenchArea.Code) return Code.Take(limit);
            return Windows.Take(limit).Concat(Chrome.Take(limit)).Concat(Code.Take(limit));
        }

        private static WorkbenchCustomTask Shortcut(string chord, string goal, WorkbenchArea area = WorkbenchArea.Windows)
        { return new WorkbenchCustomTask(area, chord, goal); }

        public static WorkbenchTask Custom(WorkbenchCustomTask custom)
        {
            var scene = Scene(WorkbenchArea.Shortcuts, "Команды", AreaTitle(custom.Area), custom.Command, "Вызовите команду с клавиатуры.");
            scene.Sidebar = false;
            return new WorkbenchTask(scene, custom.Command, S(custom.Chord, custom.Command, s =>
            {
                s.Body = "Комбинация принята.";
                s.Detail = "Следующее задание откроется автоматически.";
            }));
        }

        public static string AreaTitle(WorkbenchArea area)
        {
            switch (area)
            {
                case WorkbenchArea.Windows: return "Windows";
                case WorkbenchArea.Chrome: return "Chrome";
                case WorkbenchArea.Code: return "VS Code";
                case WorkbenchArea.Mixed: return "Windows · Chrome · VS Code";
                default: return "Мой набор";
            }
        }

        private static WorkbenchStep S(string chord, string goal, Action<WorkbenchScene> apply, string choose = null)
        { return new WorkbenchStep(chord, goal, apply, select: choose); }

        private static WorkbenchStep E(string chord, string goal, string prompt, string value, Action<WorkbenchScene> apply, string choose = null)
        { return new WorkbenchStep(chord, goal, apply, prompt, value, choose); }

        private static string Pick(Random random, string[] choices) { return choices[random.Next(choices.Length)]; }
        private static string Project(Random random) { return Pick(random, Reports) + "-" + random.Next(12, 86); }
        private static string Page(Random random) { return Pick(random, Topics) + "-" + random.Next(2, 18); }

        private static WorkbenchScene Scene(WorkbenchArea area, string caption, string location, string headline, string body, params string[] items)
        {
            var scene = new WorkbenchScene { Area = area, Caption = caption, Location = location, Headline = headline, Body = body };
            scene.ReplaceItems(items);
            return scene;
        }

        private static WorkbenchScene Explorer(string location, string headline, params string[] items)
        { return Scene(WorkbenchArea.Windows, "Файлы и окна", location, headline, "", items); }

        private static WorkbenchTask RenameFile(Random rng)
        {
            string title = Project(rng), before = title + "-draft.csv", after = title + ".csv";
            var scene = Explorer(@"C:\Lab\Reports", "Рабочие отчёты", "archive", before, "index.txt");
            return new WorkbenchTask(scene, "Переименуйте черновик",
                E("F2", "Стрелками выделите " + before + " и переименуйте в " + after + ".", "Имя файла", after,
                    s => { s.Items[s.Selection] = after; s.Detail = "Имя обновлено."; }, before));
        }

        private static WorkbenchTask OpenFolder(Random rng)
        {
            string title = Project(rng), path = @"C:\Lab\" + title;
            var scene = Explorer(@"C:\Lab", "Учебный диск", "Reports", title, "Temp");
            return new WorkbenchTask(scene, "Откройте папку по адресу",
                E("Ctrl+L", "Перейдите в папку " + path, "Адрес папки", path,
                    s => { s.Location = path; s.Headline = title; s.ReplaceItems("notes.txt", title + ".csv"); }));
        }

        private static WorkbenchTask NewFolder(Random rng)
        {
            string title = Project(rng);
            var scene = Explorer(@"C:\Lab\Reports", "Рабочие отчёты", "Archive", "Incoming", "notes.txt");
            return new WorkbenchTask(scene, "Создайте папку проекта",
                E("Ctrl+Shift+N", "Создайте папку " + title + ".", "Имя новой папки", title, s => s.AddItem(title)));
        }

        private static WorkbenchTask SwitchWindows(Random rng)
        {
            string first = Project(rng) + ".csv", second = Pick(rng, Topics) + ".txt";
            var scene = Explorer("Рабочий стол", "Открытые окна", first, second);
            scene.Body = first;
            return new WorkbenchTask(scene, "Смените активное окно",
                S("Alt+Tab", "Переключитесь на " + second + ".", s => { s.Selection = 1; s.Body = second; }),
                S("Alt+Tab", "Вернитесь в " + first + ".", s => { s.Selection = 0; s.Body = first; }));
        }

        private static WorkbenchTask OpenExplorer(Random rng)
        {
            string folder = Project(rng);
            var scene = Explorer("Рабочий стол", "Рабочий стол", "Заметки", folder);
            return new WorkbenchTask(scene, "Запустите Проводник",
                S("Win+E", "Откройте окно Проводника.", s => { s.Location = @"C:\Lab"; s.Headline = "Проводник"; s.ReplaceItems("Reports", folder); }),
                S("Enter", "Стрелками выберите " + folder + " и откройте папку.", s =>
                { s.Location += "\\" + folder; s.Headline = folder; s.ReplaceItems("brief.txt", "tasks.csv"); }, folder));
        }

        private static WorkbenchTask ShowDesktop(Random rng)
        {
            string name = Project(rng) + ".csv";
            var scene = Explorer("Рабочий стол", "Активное окно", name);
            return new WorkbenchTask(scene, "Сверните и верните окна",
                S("Win+D", "Покажите рабочий стол.", s => { s.Headline = "Рабочий стол"; s.ReplaceItems("Корзина", "Проекты"); }),
                S("Win+D", "Верните открытые окна.", s => { s.Headline = "Активное окно"; s.ReplaceItems(name); }));
        }

        private static WorkbenchTask TransferFile(Random rng, bool move)
        {
            string name = Project(rng) + ".csv";
            var scene = Explorer(@"C:\Lab\Incoming", "Входящие файлы", "index.txt", name, "notes.txt");
            return new WorkbenchTask(scene, move ? "Перенесите отчёт" : "Скопируйте отчёт",
                S(move ? "Ctrl+X" : "Ctrl+C", "Стрелками выделите " + name + ". " + (move ? "Вырежьте" : "Скопируйте") + " файл.",
                    s => { s.Detail = "В учебном буфере " + name; if (move) s.RemoveSelected(); }, name),
                E("Ctrl+L", @"Перейдите в C:\Lab\Archive.", "Адрес папки", @"C:\Lab\Archive",
                    s => { s.Location = @"C:\Lab\Archive"; s.Headline = "Архив"; s.ReplaceItems("index.txt"); }),
                S("Ctrl+V", "Вставьте файл в архив.", s => { s.AddItem(name); s.Detail = move ? "Файл перенесён." : "Копия создана."; }));
        }

        private static WorkbenchTask AppSearch(Random rng)
        {
            string app = Pick(rng, new[] { "Notepad", "Calculator", "Paint" });
            return new WorkbenchTask(Explorer("Система", "Рабочий стол", "Заметки", "Отчёты"), "Найдите приложение",
                E("Win+S", "Откройте поиск и введите " + app + ".", "Поиск в учебной среде", app,
                    s => { s.Headline = app; s.Body = "Учебное окно открыто."; s.ReplaceItems(app); }));
        }

        private static WorkbenchTask OpenSettings(Random rng)
        {
            return new WorkbenchTask(Explorer("Система", "Рабочий стол", "Заметки", Project(rng)), "Откройте параметры",
                S("Win+I", "Откройте системные параметры.", s => { s.Headline = "Параметры"; s.ReplaceItems("Система", "Сеть", "Персонализация"); }));
        }

        private static WorkbenchTask OpenTaskManager(Random rng)
        {
            return new WorkbenchTask(Explorer("Система", "Рабочий стол", "Chrome", "VS Code", Project(rng)), "Откройте диспетчер задач",
                S("Ctrl+Shift+Esc", "Просмотрите список запущенных приложений.", s => { s.Headline = "Диспетчер задач"; s.ReplaceItems("Проводник", "Chrome", "VS Code"); }));
        }

        private static WorkbenchTask SnapWindow(Random rng)
        {
            return new WorkbenchTask(Explorer("Рабочий стол", "Окна", Project(rng), "Заметки"), "Разместите окно слева",
                S("Win+Left", "Прикрепите активное окно к левой половине.", s => { s.Body = "Окно слева. Справа свободно."; s.Detail = "┌──── Отчёт ────┬────────────────┐"; }));
        }

        private static WorkbenchTask CloseWindow(Random rng)
        {
            return new WorkbenchTask(Explorer("Рабочий стол", "Открытые окна", Project(rng) + ".csv", "Заметки"), "Закройте активное окно",
                S("Alt+F4", "Закройте текущее окно, чтобы вернуться к заметкам.", s => { s.RemoveSelected(); s.Headline = "Заметки"; }));
        }

        private static WorkbenchTask RunDialog(Random rng)
        {
            return new WorkbenchTask(Explorer("Система", "Рабочий стол", "Документы", Project(rng)), "Покажите окно «Выполнить»",
                S("Win+R", "Откройте окно «Выполнить».", s => { s.Headline = "Выполнить"; s.Body = "Открыть: __________________"; }),
                S("Esc", "Закройте окно «Выполнить».", s => { s.Headline = "Рабочий стол"; s.Body = "Диалог закрыт."; }));
        }

        private static WorkbenchScene Web(string page, params string[] tabs)
        { return Scene(WorkbenchArea.Chrome, "Вкладки и страницы", "workspace.test/" + page, "Внутренний справочник", "Материалы проекта · " + page, tabs); }

        private static WorkbenchTask BrowsePage(Random rng)
        {
            string page = Page(rng), address = "workspace.test/" + page;
            return new WorkbenchTask(Web("home", "Старт"), "Откройте страницу в новой вкладке",
                S("Ctrl+T", "Откройте новую вкладку.", s => { s.AddItem("Новая вкладка"); s.Location = ""; s.Headline = "Новая вкладка"; s.Body = ""; }),
                E("Ctrl+L", "Перейдите по адресу " + address, "Учебная адресная строка", address,
                    s => { s.Location = address; s.Headline = page; s.Items[s.Selection] = page; s.Body = "Страница открыта в учебной среде."; }));
        }

        private static WorkbenchTask FindOnPage(Random rng)
        {
            string page = Page(rng), word = Pick(rng, Reports);
            var scene = Web(page, page);
            scene.Body = "status: ready\ncategory: " + word + "\nowner: team\npriority: normal";
            return new WorkbenchTask(scene, "Найдите слово на странице",
                E("Ctrl+F", "Найдите на странице слово " + word + ".", "Текст для поиска", word, s => s.Detail = "Найдено: " + word));
        }

        private static WorkbenchTask CloseRestoreTab(Random rng)
        {
            string page = Page(rng);
            var scene = Web(page, "Старт", page);
            scene.Selection = 1;
            return new WorkbenchTask(scene, "Закройте и верните вкладку",
                S("Ctrl+W", "Закройте вкладку " + page + ".", s => { s.RemoveSelected(); s.Headline = "Старт"; s.Location = "workspace.test/home"; }),
                S("Ctrl+Shift+T", "Верните только что закрытую вкладку.", s => { s.AddItem(page); s.Headline = page; s.Location = "workspace.test/" + page; }));
        }

        private static WorkbenchTask SwitchTabs(Random rng)
        {
            string page = Page(rng);
            return new WorkbenchTask(Web("home", "Старт", page, "Отчёты"), "Переключитесь между вкладками",
                S("Ctrl+Tab", "Перейдите на следующую вкладку.", s => { s.Selection = 1; s.Location = "workspace.test/" + page; }),
                S("Ctrl+Shift+Tab", "Вернитесь на предыдущую вкладку.", s => { s.Selection = 0; s.Location = "workspace.test/home"; }));
        }

        private static WorkbenchTask History(Random rng)
        {
            string page = Page(rng);
            return new WorkbenchTask(Web(page, page), "Откройте историю",
                S("Ctrl+H", "Откройте журнал посещений.", s => { s.AddItem("История"); s.Location = "История браузера"; s.Headline = "Последние страницы"; s.Body = page + "\nСправочник\nСтарт"; }));
        }

        private static WorkbenchTask Downloads(Random rng)
        {
            string file = Project(rng) + ".pdf";
            return new WorkbenchTask(Web("reports", "Отчёты"), "Найдите список загрузок",
                S("Ctrl+J", "Откройте страницу загрузок.", s => { s.AddItem("Загрузки"); s.Location = "Загрузки браузера"; s.Headline = "Завершённые загрузки"; s.Body = file; }));
        }

        private static WorkbenchTask UseHistory(Random rng)
        {
            string page = Page(rng);
            return new WorkbenchTask(Web(page, page), "Пройдите по истории вкладки",
                S("Alt+Left", "Отправьтесь на предыдущую страницу.", s => { s.Location = "workspace.test/home"; s.Headline = "Старт"; }),
                S("Alt+Right", "Вернитесь на следующую страницу.", s => { s.Location = "workspace.test/" + page; s.Headline = page; }));
        }

        private static WorkbenchTask Reload(Random rng)
        {
            var scene = Web(Page(rng), "Отчёт");
            scene.Body = "Версия страницы 1";
            return new WorkbenchTask(scene, "Обновите страницу",
                S("Ctrl+R", "Перезагрузите текущую страницу.", s => { s.Body = "Версия страницы 2"; s.Detail = "Страница перезагружена."; }));
        }

        private static WorkbenchTask OpenBookmarks(Random rng)
        {
            var scene = Web(Page(rng), "Старт");
            return new WorkbenchTask(scene, "Управляйте закладками",
                S("Ctrl+Shift+B", "Покажите панель закладок.", s => s.Detail = "Панель закладок   ·   Проекты   ·   Справочник"),
                S("Ctrl+Shift+O", "Откройте менеджер закладок.", s => { s.AddItem("Закладки"); s.Headline = "Менеджер закладок"; s.Body = "Проекты\nСправочник"; }));
        }

        private static WorkbenchTask JumpToTab(Random rng)
        {
            string page = Page(rng);
            return new WorkbenchTask(Web("home", "Старт", "Заметки", page, "Архив"), "Перейдите сразу к вкладке",
                S("Ctrl+3", "Сделайте активной третью вкладку " + page + ".", s => { s.Selection = 2; s.Location = "workspace.test/" + page; s.Headline = page; }),
                S("Ctrl+1", "Вернитесь к первой вкладке.", s => { s.Selection = 0; s.Location = "workspace.test/home"; s.Headline = "Старт"; }));
        }

        private static WorkbenchScene Editor(Random rng)
        {
            return Scene(WorkbenchArea.Code, "Проект и редактор", "demo / app.py", "app.py",
                "def label(text):\n    return text.strip()\n\nvalue = " + rng.Next(3, 29) + "\nprint(label(\"Demo\"))",
                "app.py", "config.py", "main.py", "README.md");
        }

        private static WorkbenchTask OpenFile(Random rng)
        {
            string file = Pick(rng, new[] { "main.py", "config.py", "README.md" });
            return new WorkbenchTask(Editor(rng), "Откройте файл по имени",
                E("Ctrl+P", "Найдите и откройте " + file + ".", "Быстрое открытие файла", file,
                    s => { s.Selection = s.Items.IndexOf(file); s.Headline = file; s.Location = "demo / " + file; s.Body = "# " + file + "\n\n# Учебный проект"; }));
        }

        private static WorkbenchTask SaveFile(Random rng)
        {
            var scene = Editor(rng);
            scene.Detail = "Есть несохранённые изменения.";
            scene.Headline += "  ●";
            return new WorkbenchTask(scene, "Сохраните файл",
                S("Ctrl+S", "Сохраните изменения в app.py.", s => { s.Headline = "app.py"; s.Detail = "Файл сохранён в учебном проекте."; }));
        }

        private static WorkbenchTask GoToLine(Random rng)
        {
            string line = rng.Next(2, 6).ToString();
            return new WorkbenchTask(Editor(rng), "Перейдите к строке",
                E("Ctrl+G", "Перейдите к строке " + line + ".", "Номер строки", line, s => s.Detail = "Курсор на строке " + line));
        }

        private static WorkbenchTask SearchProject(Random rng)
        {
            string query = Pick(rng, new[] { "label", "value", "print" });
            return new WorkbenchTask(Editor(rng), "Найдите значение в проекте",
                E("Ctrl+Shift+F", "Найдите в проекте " + query + ".", "Поиск по проекту", query,
                    s => { s.Detail = "Поиск  ·  " + query; s.ReplaceItems("app.py  ·  " + query, "main.py  ·  " + query); }));
        }

        private static WorkbenchTask ToggleTerminal(Random rng)
        {
            return new WorkbenchTask(Editor(rng), "Управляйте терминалом",
                S("Ctrl+Oem3", "Откройте встроенный терминал.", s => s.Terminal = true),
                S("Ctrl+Oem3", "Скройте терминал и вернитесь в редактор.", s => { s.Terminal = false; s.Detail = "Терминал скрыт."; }));
        }

        private static WorkbenchTask ToggleExplorer(Random rng)
        {
            return new WorkbenchTask(Editor(rng), "Освободите место в редакторе",
                S("Ctrl+B", "Сверните боковую панель проекта.", s => s.Sidebar = false),
                S("Ctrl+B", "Верните дерево файлов.", s => s.Sidebar = true));
        }

        private static WorkbenchTask OpenPalette(Random rng)
        {
            const string command = "Toggle Terminal";
            return new WorkbenchTask(Editor(rng), "Вызовите команду из палитры",
                E("Ctrl+Shift+P", "Найдите команду " + command + ".", "Палитра команд", command, s => { s.Terminal = true; s.Detail = command; }));
        }

        private static WorkbenchTask EditAndSave(Random rng)
        {
            string value = "value = " + rng.Next(30, 90);
            return new WorkbenchTask(Editor(rng), "Измените и сохраните значение",
                E("Ctrl+G", "Перейдите к четвёртой строке.", "Номер строки", "4", s => s.Detail = "Курсор на строке 4"),
                E("Enter", "Откройте строку для правки и введите " + value + ".", "Новая строка в учебном редакторе", value,
                    s => { var lines = s.Body.Split('\n'); lines[3] = value; s.Body = string.Join("\n", lines); s.Headline = "app.py  ●"; }),
                S("Ctrl+S", "Сохраните новую версию файла.", s => { s.Headline = "app.py"; s.Detail = "Учебный файл сохранён."; }));
        }

        private static WorkbenchTask CommentLine(Random rng)
        {
            return new WorkbenchTask(Editor(rng), "Закомментируйте строку",
                E("Ctrl+G", "Перейдите к пятой строке с print.", "Номер строки", "5", s => s.Detail = "Курсор на строке 5"),
                S("Ctrl+/", "Закомментируйте вызов print.", s => { s.Body = s.Body.Replace("\nprint", "\n# print"); s.Detail = "Строка 5 стала комментарием."; }));
        }

        private static WorkbenchTask CloseEditor(Random rng)
        {
            var scene = Editor(rng);
            scene.ReplaceItems("app.py", "config.py");
            return new WorkbenchTask(scene, "Закройте текущий редактор",
                S("Ctrl+W", "Закройте вкладку app.py.", s => { s.RemoveSelected(); s.Location = "demo / config.py"; s.Headline = "config.py"; s.Body = "DEBUG = False"; }));
        }

        private static WorkbenchTask FormatCode(Random rng)
        {
            var scene = Editor(rng);
            scene.Body = "value= " + rng.Next(10, 88) + "\nprint( value )";
            return new WorkbenchTask(scene, "Отформатируйте документ",
                S("Alt+Shift+F", "Примените форматирование документа.", s =>
                { s.Body = s.Body.Replace("value= ", "value = ").Replace("( value )", "(value)"); s.Detail = "Форматирование применено."; }));
        }
    }
}
