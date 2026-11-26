using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    internal const string TerminalCourseRevision = "shell-course/v2";
    internal static bool DeliveryMission(string id) => id.StartsWith("terminal-delivery-", StringComparison.Ordinal);
    internal static string TerminalEnvironment(string id)
    {
        if (DeliveryMission(id)) return id.EndsWith("-ps", StringComparison.Ordinal) ? "windows" : "ubuntu";
        return id switch {
            PracticalPowerShellId or PowerShellDiskId or PowerShellDiskRepeatId => "windows",
            PracticalBashId or BashDiskId or BashDiskRepeatId => "ubuntu",
            _ => null };
    }
    // All twelve historical Bash projects stay on their appropriate level;
    // isolated command drills are still in the explicit chooser, not the main card.
    internal static bool TerminalCourseMission(LiveMission mission, string env) =>
        TerminalEnvironment(mission.Id) is string native ? native == env :
        env == "ubuntu" && !mission.Curated && !mission.Id.StartsWith("terminal-essentials-", StringComparison.Ordinal);

    // Keep the exact historical route for old drafts and explicit catalog selection.
    // Ordinary practice keeps the project artifact, not repeated identity checks
    // or erase/yank pairs with no effect on that artifact.
    internal static LiveMission PracticalBashRoute(LiveMission mission)
    {
        if (mission.App != "terminal" || mission.Curated) return mission;
        var phases = mission.Phases.Select(p => {
            if (p.Route.Contains("terminal-yank", StringComparison.Ordinal))
                return ShellPhase("Найдите ошибку по содержимому и проверьте выдержку для передачи",
                    "cd /home/student/project", "grep -n ERROR logs/app.log > case.txt", "cat case.txt",
                    "cp case.txt /home/student/Downloads/incident.txt", "cat /home/student/Downloads/incident.txt");
            if (p.Route.Contains("cmd:ps~aux~>~processes.txt", StringComparison.Ordinal))
                return ShellPhase("Подготовьте снимок процессов для запроса поддержки",
                    "cd /home/student/project", "ps aux > processes.txt", "head -n 2 processes.txt",
                    "grep python processes.txt > worker.txt", "cat worker.txt");
            if (p.Route.Contains("cmd:apt~search~tree", StringComparison.Ordinal))
                return ShellPhase("Составьте перечень файлов для следующих поисков",
                    "cd /home/student/project", "find . -name '*.py' > paths.txt", "wc -l paths.txt", "cat paths.txt",
                    "cp paths.txt /home/student/Downloads/python-files.txt", "cat /home/student/Downloads/python-files.txt");
            return p with { Route = string.Join(" ", p.Route.Split(' ').Where(t => t is not ("cmd:whoami" or "cmd:pwd"))) };
        }).ToArray();
        return mission with { Phases = phases };
    }

    private static void AddTerminalDeliveries(List<LiveMission> list)
    {
        foreach (bool ps in new[]{false,true})
        {
            string suffix = ps ? "ps" : "bash";
            void Add(string kind, string title, params LivePhase[] phases) =>
                list.Add(new("terminal", 0, $"terminal-delivery-{kind}-{suffix}", title, phases,
                    FinishAfterRoute:true, Curated:true, Prepare: s => s.PrepareDeliveryLesson(kind, ps)));
            Add("sync", "Обновить копию папки без потери архива",
                new("Узнайте, что изменится в Backup", "delivery-preview"),
                new("Перенесите отличия из Work, оставив архив", "delivery-apply"),
                new("Прочитайте обновлённую заметку в самой копии", "delivery-read-copy"),
                new("Убедитесь, что при повторе нечего копировать", "delivery-recheck"));
            Add("hash", "Выбрать неповреждённую загрузку",
                new("Узнайте контрольную сумму от отправителя", "delivery-receipt"),
                new("Проверьте содержимое двух загрузок", "delivery-hash-east delivery-hash-west"),
                new("Откройте только совпавший с квитанцией CSV", "delivery-read-match"));
        }
    }
}

internal sealed partial class LabScene
{
    internal void PrepareDeliveryLesson(string kind, bool ps)
    {
        Terminal.PrepareDelivery(kind, ps, browserCaseVariant);
        if (Active is LabWindow w) w.Title = (ps ? "Windows PowerShell · " : "Терминал Ubuntu · ") +
            (kind == "sync" ? "Обновление копии" : "Проверка загрузки");
    }

    internal LabTask DeliveryTask(string token)
    {
        bool ps = Terminal.IsPowerShell;
        string read = ps ? "Get-Content " : "cat ";
        string Hash(string name) => ps ? "(Get-FileHash " + name + ").Hash" : "sha256sum " + name;
        string sync = ps ? "robocopy Work Backup /E /R:1 /W:1" : "rsync -rti -- Work/ Backup/";
        string plan = ps ? sync + " /L" : "rsync -rtni -- Work/ Backup/";
        (string title,string cmd) = token switch {
            "delivery-preview" => ("Посмотрите, что попадёт из Work в Backup, пока ничего не копируя",plan),
            "delivery-apply" => ("Обновите Backup по плану. Сохраните всё, что есть только в копии",sync),
            "delivery-read-copy" => ("Сверьте срок в notes/weekly.txt из обновлённой папки Backup",
                read + (ps ? @"Backup\notes\weekly.txt" : "Backup/notes/weekly.txt")),
            "delivery-recheck" => ("Ещё раз просмотрите план: остались ли файлы для копирования?",plan),
            "delivery-receipt" => ("Прочитайте SHA-256 из проверенной квитанции receipt.txt",read+"receipt.txt"),
            "delivery-hash-east" => ("Вычислите SHA-256 загрузки east.csv",Hash("east.csv")),
            "delivery-hash-west" => ("Вычислите SHA-256 загрузки west.csv",Hash("west.csv")),
            _ => ("Сравните обе суммы с квитанцией и откройте только совпавший CSV",read+Terminal.TrustedCopy)
        };
        return new("command/"+token, title, "", "Введите команду и нажмите Enter.", cmd,"command");
    }
}
