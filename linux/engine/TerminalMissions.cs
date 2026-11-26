using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static LivePhase ShellPhase(string title, params string[] commands) =>
        new(title, string.Join(" ", commands.Select(command => "cmd:" + command.Replace(' ', '~'))));

    private static readonly Dictionary<string, LivePhase> ShellParts = new()
    {
        ["inspect"] = ShellPhase("Найти исходники в рабочей копии", "cd /home/student/project", "pwd", "ls", "ls -la src", "head -n 2 src/signals.py"),
        ["logs"] = ShellPhase("Найти сбой и собрать выдержку из журнала", "cd /home/student/project", "ls logs", "cat logs/app.log", "grep -n ERROR logs/app.log > error.txt", "wc -l error.txt", "cat error.txt"),
        ["notes"] = ShellPhase("Собрать словарь меток из заметок", "cd /home/student/project", "cat notes.txt", "sort notes.txt", "sort notes.txt | uniq > tags.txt", "wc -l tags.txt", "cat tags.txt"),
        ["report"] = ShellPhase("Подготовить файл передачи", "cd /home/student/project", "mkdir -p handoff", "echo spectrum > handoff/report.txt", "cat handoff/report.txt", "cp handoff/report.txt handoff/review.txt", "ls -l handoff"),
        ["archive"] = ShellPhase("Сохранить и сверить копию исходника", "cd /home/student/project", "mkdir -p backup", "cp src/signals.py backup/signals.py", "head -n 2 backup/signals.py", "wc -l backup/signals.py", "ls -l backup"),
        ["permissions"] = ShellPhase("Проверить доступ к отчёту", "cd /home/student/project", "id", "touch private.txt", "chmod 600 private.txt", "ls -l private.txt", "echo checked > private.txt", "cat private.txt"),
        ["find"] = ShellPhase("Найти файлы с расчётами и проверить определения", "cd /home/student/project", "find . -name '*.py'", "grep -n def src/spectrum.py", "wc -l src/spectrum.py", "tail -n 2 src/spectrum.py"),
        ["inventory"] = ShellPhase("Собрать сведения об учебной системе", "cd /home/student/project", "uname -a", "whoami", "ps", "ps aux > processes.txt", "head -n 2 processes.txt"),
        ["tools"] = ShellPhase("Подготовить просмотр дерева каталогов", "cd /home/student/project", "apt search tree", "sudo apt update", "sudo apt install tree", "tree", "apt show tree"),
        ["commit"] = ShellPhase("Зафиксировать проверенную рабочую копию", "cd /home/student/project", "sudo apt install git", "git init", "git status", "git add .", "git commit -m 'Review'", "git log --oneline"),
        ["diff"] = ShellPhase("Проверить правку перед передачей", "cd /home/student/project", "git status", "echo reviewed >> notes.txt", "git diff", "git add notes.txt", "git commit -m 'Notes'", "git log --oneline"),
        ["delivery"] = ShellPhase("Доставить сводку в папку загрузок", "cd /home/student/project", "head -n 2 notes.txt > summary.txt", "cp summary.txt /home/student/Downloads/summary.txt", "cd /home/student/Downloads", "ls -l", "cat summary.txt"),
        ["history"] = new("Проверить путь и поправить команду из истории",
            "terminal-paste terminal-submit cmd:whoami cmd:pwd terminal-history-up terminal-home terminal-end terminal-erase terminal-yank terminal-submit cmd:whoami"),
    };
    private static void TerminalMissions(List<LiveMission> list)
    {
        AddTerminalDeliveries(list);
        AddTerminalDiskSpace(list);
        AddPracticalTerminal(list);
        var repeats = list.FindAll(m => m.Id is BashDiskRepeatId or PowerShellDiskRepeatId);
        list.RemoveAll(m => m.Id is BashDiskRepeatId or PowerShellDiskRepeatId);
        void Essentials(int level, string id, string title, params LivePhase[] phases) =>
            list.Add(new("terminal", level, "terminal-essentials-" + id, title, phases,
                FinishAfterRoute: true, Curated: true, Prepare: scene => scene.Terminal.StartInsideProject()));
        Essentials(0, "locate", "Где я и что лежит рядом?",
            ShellPhase("Определите путь: pwd печатает текущую папку, ls показывает её содержимое", "pwd", "ls"),
            ShellPhase("Вернитесь домой: cd без аргументов меняет папку, pwd проверяет результат", "cd", "pwd", "ls"),
            ShellPhase("Узнайте пользователя и очистите экран, не затрагивая файлы", "whoami", "clear"));
        Essentials(0, "inspect", "Кто запустил программу и что работает?",
            ShellPhase("Сравните имя пользователя и его идентификатор", "whoami", "id"),
            ShellPhase("Проверьте систему и список учебных процессов", "uname", "ps"),
            ShellPhase("Откройте историю и очистите вывод", "history", "clear"));
        Essentials(0, "restart", "Проверка системы после перезапуска",
            ShellPhase("Запишите мысленно текущий путь и посмотрите процессы", "pwd", "ps"),
            ShellPhase("Перезапустите только симуляцию и проверьте, где оказалось приглашение", "reboot", "pwd", "ls"),
            ShellPhase("Убедитесь, что имя пользователя осталось прежним", "whoami"));
        Essentials(1, "session", "Сравнить команду, вывод и историю",
            ShellPhase("Проверьте пользователя и среду", "whoami", "id", "uname"),
            ShellPhase("Посмотрите, что было выполнено, затем отделите старый вывод от нового", "history", "clear", "pwd", "ls"));
        Essentials(2, "support", "Быстрая диагностика учебной системы",
            ShellPhase("Снимите состояние до перезапуска: пользователь, система, процессы", "id", "uname", "ps"),
            ShellPhase("Перезапустите симуляцию и сравните путь и список процессов", "reboot", "pwd", "ps"),
            ShellPhase("Проверьте историю этой сессии", "history", "ls"));
        Essentials(1, "home-check", "Проверить рабочую папку после возврата",
            ShellPhase("Запомните текущую папку и посмотрите, что в ней", "pwd", "ls"),
            ShellPhase("Вернитесь домой и подтвердите новый путь", "cd", "pwd", "ls"),
            ShellPhase("Проверьте, сохранилась ли история после очистки экрана", "clear", "history"));
        Essentials(2, "process-check", "Сопоставить старое и новое состояние терминала",
            ShellPhase("Узнайте, кто запустил оболочку, и откройте список процессов", "whoami", "ps"),
            ShellPhase("Откройте чистый экран и проверьте папку и содержимое", "clear", "pwd", "ls"),
            ShellPhase("Восстановите последовательность по истории", "history"));
        void Add(int level, string id, string title, params string[] parts) =>
            list.Add(new("terminal", level, "terminal-" + id, title, parts.Select(p => ShellParts[p]).ToArray()));
        Add(0,"explore","Разобраться в незнакомом проекте","inspect","history","find","notes");
        Add(0,"handoff","Подготовить и передать отчёт","inspect","report","delivery","history");
        Add(0,"backup","Сохранить исходники перед работой","inspect","find","archive","report");
        Add(0,"investigate","Найти ошибку в журнале","inventory","logs","notes","delivery");
        Add(1,"access","Собрать отчёт с ограниченным доступом","inspect","logs","permissions","archive");
        Add(1,"catalog","Составить каталог файлов проекта","tools","find","notes","delivery");
        Add(1,"triage","Сопоставить журнал и процессы","history","inventory","logs","report");
        Add(1,"transfer","Проверить содержимое перед копированием","archive","permissions","report","delivery");
        Add(2,"revision","Подготовить проверенный коммит","inspect","find","commit","diff");
        Add(2,"snapshot","Зафиксировать состояние рабочей среды","inventory","tools","archive","commit");
        Add(2,"incident","Передать результаты разбора сбоя","logs","permissions","notes","commit","delivery");
        Add(2,"changes","Проверить историю и передать изменения","archive","commit","diff","delivery");
        list.AddRange(repeats); // Retry the disk decision after intervening projects.
    }
}
