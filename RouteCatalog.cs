namespace TouchTypingStudio
{
    internal enum Chord
    {
        None, SwitchWindow, SwitchLayout, OpenExplorer, Address, NewFolder, Rename, Copy, Paste, Arrow,
        Find, Undo, Save, QuickOpen, Palette, Confirm, ExerciseCharacter, StartApp, CloseWindow, SelectAll, MoveWord,
        ParentFolder, ForwardFolder, BackFolder, Cut, SnapLeft, SnapRight, GoLine, Replace, Comment, MoveLine,
        CloseEditor, ReopenEditor, SwitchEditor, ReplaceAll,
        RunDialog, WindowsSettings, TaskManager, Dismiss
    }

    internal enum RouteKind
    {
        Switch, Layout, Explorer, Rename, Edit, Python, StartMenu, CloseWindow, SelectText, NavigateWords,
        Snap, FolderHistory, MoveFile, CodeLine, CodeComment, CodeReplace, CodeMoveLine, CodeTabs, CodeReopen,
        RunDialog, WindowsSettings, TaskManager
    }

    internal sealed class RouteSpec
    {
        public readonly RouteKind Kind;
        public readonly string Name, Goal, Category;
        public readonly string Shortcuts;
        public readonly Chord SystemChord;
        public bool InCode { get { return Category == "VS Code"; } }
        public RouteSpec(RouteKind kind, string name, string goal, string category = "Windows",
            string shortcuts = "", Chord systemChord = Chord.None)
        {
            Kind = kind;
            Name = name;
            Goal = goal;
            Category = category;
            Shortcuts = shortcuts;
            SystemChord = systemChord;
        }
        public string PassId(int stage) { return Kind + "/" + stage; }
    }

    internal static class RouteCatalog
    {
        internal static readonly RouteSpec[] Routes =
        {
            new RouteSpec(RouteKind.Switch, "Переключить окно", "Из первого учебного окна перейдите во второе.", shortcuts: "Alt+Tab"),
            new RouteSpec(RouteKind.Layout, "Две раскладки", "Наберите две строки на разных раскладках в Блокноте.", shortcuts: "Win+Пробел"),
            new RouteSpec(RouteKind.StartMenu, "Запуск через меню «Пуск»", "Найдите Блокнот из меню «Пуск» и вернитесь в тренажёр.", shortcuts: "Win или Ctrl+Esc"),
            new RouteSpec(RouteKind.CloseWindow, "Закрытие окна", "Закройте только учебный Блокнот.", shortcuts: "Alt+F4"),
            new RouteSpec(RouteKind.Snap, "Два окна рядом", "Разместите два учебных окна по половинам экрана.", shortcuts: "Win+← · Win+→"),
            new RouteSpec(RouteKind.RunDialog, "Окно «Выполнить»", "Откройте окно «Выполнить» и закройте его с клавиатуры.", shortcuts: "Win+R · Esc",
                systemChord: Chord.RunDialog),
            new RouteSpec(RouteKind.WindowsSettings, "Параметры Windows", "Откройте приложение «Параметры» с клавиатуры.", shortcuts: "Win+I",
                systemChord: Chord.WindowsSettings),
            new RouteSpec(RouteKind.TaskManager, "Диспетчер задач", "Откройте Диспетчер задач с клавиатуры.", shortcuts: "Ctrl+Shift+Esc",
                systemChord: Chord.TaskManager),
            new RouteSpec(RouteKind.Explorer, "Открыть путь и создать папку", "Откройте нужный путь в Проводнике и создайте подкаталог.", "Проводник", "Win+E · Ctrl+L · Ctrl+Shift+N"),
            new RouteSpec(RouteKind.Rename, "Имя и копия файла", "Переименуйте учебный файл и скопируйте его в другую папку.", "Проводник", "F2 · Ctrl+C · Ctrl+V"),
            new RouteSpec(RouteKind.FolderHistory, "Навигация по папкам", "Сходите на уровень выше, вернитесь назад и вперёд.", "Проводник", "Alt+↑ · Alt+← · Alt+→"),
            new RouteSpec(RouteKind.MoveFile, "Перенос файла", "Перенесите файл в папку Archive, сохранив содержимое.", "Проводник", "Ctrl+X · Ctrl+V"),
            new RouteSpec(RouteKind.SelectText, "Замена всего текста", "Выделите всё, замените содержимое и сохраните файл.", "Текст", "Ctrl+A · Ctrl+S"),
            new RouteSpec(RouteKind.NavigateWords, "Редактировать слово", "Исправьте слово в середине строки.", "Текст", "Ctrl+стрелки · Ctrl+Shift+стрелки"),
            new RouteSpec(RouteKind.Edit, "Найти, заменить, отменить", "Исправьте значение в Блокноте и сохраните результат.", "Текст", "Ctrl+F · Ctrl+Z · Ctrl+S"),
            new RouteSpec(RouteKind.Python, "Открыть код, исправить, запустить", "Откройте, измените, сохраните и запустите учебный код.", "VS Code"),
            new RouteSpec(RouteKind.CodeLine, "Перейти к строке", "Найдите строку по номеру, измените значение и сохраните.", "VS Code"),
            new RouteSpec(RouteKind.CodeComment, "Временно убрать строку", "Отключите вывод с помощью комментария и сохраните код.", "VS Code"),
            new RouteSpec(RouteKind.CodeReplace, "Заменить имя во всём файле", "Замените все вхождения имени и сохраните результат.", "VS Code"),
            new RouteSpec(RouteKind.CodeMoveLine, "Переставить строку", "Переместите строку целиком и сохраните код.", "VS Code"),
            new RouteSpec(RouteKind.CodeTabs, "Переключить редактор", "Откройте два файла и переключитесь на первый из второго.", "VS Code"),
            new RouteSpec(RouteKind.CodeReopen, "Вернуть закрытый файл", "Закройте учебный файл и восстановите вкладку.", "VS Code")
        };
    }
}
