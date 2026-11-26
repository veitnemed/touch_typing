using System.Collections.Generic;

namespace TouchTypingStudio;

internal sealed record DiskSpaceCase(string Mount, string Letter, string Folder, string Report, int NeededMiB, int FreeMiB);

internal sealed partial class LabScene
{
    private static readonly DiskSpaceCase[] DiskSpaceCases = [
        new("/mnt/team", "D", "Dispatch", "batch.zip", 2048, 512),
        new("/mnt/export", "E", "Exports", "reports.zip", 1024, 3072),
        new("/srv/reports", "F", "Reports", "quarter.zip", 4096, 1024),
    ];
    // Keep this separate from the original fixtures, which are replayed to resume existing drafts.
    private static readonly DiskSpaceCase RepeatRequest = new("/mnt/team", "D", "Dispatch", "batch.zip", 2048, 512);
    private static readonly int[] RepeatFreeMiB = [3072, 512, 3072];
    private DiskSpaceCase diskCase;
    private string diskTarget;
    private bool? diskSpaceEnough;

    internal bool DiskSpaceLesson => diskCase != null;
    internal bool DiskSpaceExpected => diskCase.FreeMiB >= diskCase.NeededMiB;
    private string DiskDestinationLabel => Terminal.IsPowerShell ? diskCase.Letter + ":\\" + diskCase.Folder :
        diskCase.Mount + "/" + diskCase.Folder;
    private string DiskLessonCommand => Terminal.IsPowerShell ? "Get-PSDrive " + diskCase.Letter :
        "df -h " + DiskDestinationLabel;

    internal void PrepareDiskCapacity(bool powershell, bool repeat = false)
    {
        diskCase = repeat ? RepeatRequest with { FreeMiB = RepeatFreeMiB[browserCaseVariant] }
            : DiskSpaceCases[browserCaseVariant];
        diskTarget = powershell ? diskCase.Letter : diskCase.Mount;
        if (powershell) Terminal.UsePowerShell();
        Terminal.SetTrainingVolumes(
            powershell ? [new("C", @"C:\", 24576, 8192), new(diskCase.Letter, diskCase.Letter + ":\\", 16384, diskCase.FreeMiB)] :
                         [new("/dev/sda1", "/", 24576, 8192), new("/dev/sdb1", diskCase.Mount, 16384, diskCase.FreeMiB)],
            "Не удалось сохранить архив " + diskCase.Report,
            "Назначение: " + DiskDestinationLabel,
            "Размер архива: " + VirtualTerminal.LessonCapacityGiB(diskCase.NeededMiB) + ".");
        if (!powershell) Terminal.AddTrainingFolder(DiskDestinationLabel);
        if (Active is LabWindow window)
            window.Title = (powershell ? "Windows PowerShell" : "Терминал Ubuntu") + " · Проверка места";
    }

    internal LabTask MakeDiskCapacityTask(string token) => token switch
    {
        "disk-capacity-query" => new("command/check-destination-space", "Архив не сохранился. Проверьте место в папке назначения",
            "", "Путь назначения указан в заявке. Проверка ничего не удаляет.", DiskLessonCommand, "command"),
        "disk-capacity-verdict" => new(token, "Хватит места в папке назначения? 1 — нет, 2 — да", "",
            "Сравните свободное место и размер архива. Нажмите 1 или 2.", Action: token),
        _ => null
    };

    internal bool SubmitDiskCapacityVerdict(bool enough)
    {
        var volume = Terminal.LastCapacityQuery;
        if (diskCase == null || volume == null || (Terminal.IsPowerShell ? volume.Name : volume.Mount) != diskTarget ||
            enough != (volume.FreeMiB >= diskCase.NeededMiB)) return false;
        diskSpaceEnough = enough;
        return true;
    }

    internal object DiskCapacityEvidence(LabTask task)
    {
        if (diskCase == null) return null;
        var volume = Terminal.LastCapacityQuery;
        return task.Id switch
        {
            "command/check-destination-space" => new { queried = Terminal.IsPowerShell ? volume?.Name : volume?.Mount },
            "disk-capacity-verdict" => new { diskSpaceEnough },
            _ => null
        };
    }

    internal string DiskCapacityConclusion => diskSpaceEnough is bool enough
        ? $"На диске назначения ({DiskDestinationLabel}) свободно {VirtualTerminal.LessonCapacityGiB(diskCase.FreeMiB)}. " +
          $"Для архива нужно {VirtualTerminal.LessonCapacityGiB(diskCase.NeededMiB)}. " +
          (enough ? "Места хватает: причину сбоя нужно искать дальше." : "Места не хватает. Ничего не удаляйте без согласования.")
        : "";
    internal void CloneDiskCapacity(LabScene source)
    {
        diskCase = source.diskCase; diskTarget = source.diskTarget; diskSpaceEnough = source.diskSpaceEnough;
    }
}

internal static partial class LiveCourse
{
    internal const string BashDiskId = "terminal-disk-space-bash";
    internal const string PowerShellDiskId = "terminal-disk-space-powershell";

    internal const string BashDiskRepeatId = "terminal-disk-repeat-bash";
    internal const string PowerShellDiskRepeatId = "terminal-disk-repeat-powershell";

    private static void AddTerminalDiskSpace(List<LiveMission> list)
    {
        foreach (bool powershell in new[] {false, true})
        {
            Add(powershell ? PowerShellDiskId : BashDiskId, "Не сохранился архив: проверить место", powershell, false);
            Add(powershell ? PowerShellDiskRepeatId : BashDiskRepeatId, "Проверить место без подсказки", powershell, true);
        }
        void Add(string id, string title, bool powershell, bool repeat) =>
            list.Add(new("terminal", 0, id, title,
            [
                new("Прочитайте заявку и проверьте диск назначения", "disk-capacity-query"),
                new("Хватит места в папке назначения? 1 — нет, 2 — да", "disk-capacity-verdict"),
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareDiskCapacity(powershell, repeat)));
    }
}
