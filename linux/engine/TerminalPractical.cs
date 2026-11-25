using System;
using System.Collections.Generic;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    // Isolated learning fixtures, each with a different failure and answer.
    private sealed record JournalCase(string Title, string OldJob, string Job, string OldError, string Error);
    private static readonly JournalCase[] BashCases = [
        new("Очередь отчётов", "job-18", "job-27", "empty report name", "missing source file"),
        new("Проверка выгрузки", "job-36", "job-81", "invalid header", "checksum mismatch"),
        new("Публикация заметки", "job-52", "job-42", "missing title", "write permission denied")
    ];
    private static readonly JournalCase[] PowerShellCases = [
        new("Синхронизация отдела", "copy-14", "copy-51", "network share unavailable", "sharing violation"),
        new("Обмен сканами", "copy-28", "copy-73", "volume not ready", "destination path too long"),
        new("Резервная копия", "copy-39", "copy-64", "checkpoint not found", "destination disk full")
    ];
    private JournalCase journalCase;
    internal bool PracticalTerminal => journalCase != null;
    internal string TerminalIncidentFile => Terminal.IsPowerShell ? "sync.log" : "app.log";
    internal string TerminalIncidentFilter => (Terminal.IsPowerShell ? "Select-String " : "grep ") +
        journalCase.Job + " " + TerminalIncidentFile;
    internal string TerminalFollowCommand => Terminal.IsPowerShell ? "Get-Content sync.log -Wait" : "tail -f app.log";

    internal void PrepareTerminalIncident(bool powershell)
    {
        journalCase = (powershell ? PowerShellCases : BashCases)[browserCaseVariant];
        if (powershell) Terminal.UsePowerShell();
        else Terminal.StartInsideProject();
        Terminal.StoreFile(TerminalIncidentFile,
            $"INFO {journalCase.OldJob} started\nERROR {journalCase.OldJob} {journalCase.OldError}\nINFO service ready\n");
        Terminal.InstallLogFeed(TerminalIncidentFile, [
            "INFO health-check ok",
            $"INFO {journalCase.Job} started",
            $"ERROR {journalCase.Job} {journalCase.Error}",
            "INFO background check ok"
        ], incidentIndex: 2);
        if (powershell)
            Terminal.StoreFile("archive.log", "INFO previous run complete\nERROR copy-02 archive unavailable\n");
        if (Active is LabWindow w) w.Title = (powershell ? "Windows PowerShell · " : "Терминал Ubuntu · ") + journalCase.Title;
    }

    internal object TerminalInterruptEvidence() => new {
        app = Active?.App, focus = Active?.Focus, Terminal.FollowingLog, Terminal.LogIncidentArrived,
        Terminal.LogInterrupted, input = TerminalInput
    };
}

internal static partial class LiveCourse
{
    internal const string PracticalBashId = "terminal-practical-incident";
    internal const string PracticalPowerShellId = "terminal-powershell-diagnostic";
    internal static bool PracticalTerminalMission(string mission) => mission is PracticalBashId or PracticalPowerShellId;
    internal static bool TerminalIncidentTask(string task) => task is "command/filter-incident" or "command/powershell-incident";

    private static void AddPracticalTerminal(List<LiveMission> list)
    {
        list.Add(new("terminal", 0, PracticalBashId, "Найти новый сбой в журнале Bash", [
            ShellPhase("Отделите ошибки от обычных сообщений в app.log", "grep ERROR app.log"),
            ShellPhase("Новые сообщения ещё поступают. Откройте живой журнал app.log", "tail -f app.log"),
            new("Дождитесь новой строки ERROR и остановите только просмотр журнала", "terminal-interrupt"),
            new("Найдите идентификатор у новой ошибки и покажите все строки этого запроса", "terminal-incident-filter")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareTerminalIncident(powershell: false)));
        list.Add(new("terminal", 0, PracticalPowerShellId, "Разобрать сбой синхронизации в Windows PowerShell", [
            ShellPhase("Найдите ошибки синхронизации в sync.log вместе с номерами строк", "Select-String ERROR sync.log"),
            ShellPhase("Запустите просмотр новых записей sync.log", "Get-Content sync.log -Wait"),
            new("Дождитесь новой ошибки копирования и прервите только просмотр", "terminal-interrupt"),
            new("Выведите обе строки попытки копирования по ID из новой ошибки", "terminal-incident-filter")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareTerminalIncident(powershell: true)));
    }
}
