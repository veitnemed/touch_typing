using System;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record TrainingVolume(string Name, string Mount, int SizeMiB, int FreeMiB);

internal sealed partial class VirtualTerminal
{
    private TrainingVolume[] trainingVolumes = [];
    internal TrainingVolume LastCapacityQuery { get; private set; }

    internal void SetTrainingVolumes(TrainingVolume[] volumes, params string[] alert)
    {
        trainingVolumes = [.. volumes];
        history.Clear();
        history.AddRange(alert);
        foreach (var volume in volumes)
            if (volume.Mount.StartsWith('/') && volume.Mount.Length > 1) AddTrainingFolder(volume.Mount);
        StartInsideProject();
    }

    internal void AddTrainingFolder(string path)
    {
        // Seed virtual administrator-owned mount parents without changing
        // normal guest permissions or asking the student to run sudo.
        for (string current = path; current.Length > 1; current = Parent(current))
            directories.Add(current);
    }

    private void CopyTrainingVolumes(VirtualTerminal target)
    {
        target.trainingVolumes = [.. trainingVolumes];
        target.LastCapacityQuery = LastCapacityQuery;
    }

    private static string DiskBashSize(int mib) => mib < 1024 ? $"{mib}M" :
        (mib / 1024m).ToString("0.##", CultureInfo.InvariantCulture) + "G";
    private static string DiskPsSize(int mib) => (mib / 1024m).ToString("0.00", CultureInfo.InvariantCulture);

    internal static string LessonCapacityGiB(int mib) =>
        (mib / 1024m).ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) + " ГиБ";

    private static string CapacityReading(TrainingVolume volume) =>
        $"Проверен диск {volume.Mount} · свободно {LessonCapacityGiB(volume.FreeMiB)}\n";

    private string RunBashStorage(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Any(token => token.Operator)) throw Error("терминал", "это проверка места; файлы менять не нужно");
        string[] args = tokens.Select(token => token.Value).ToArray();
        if (args is ["pwd"]) return Cwd + "\n";
        if (args is ["cd", var destination])
        {
            if (!directories.Contains(Resolve(destination))) throw Error("cd", "учебная папка не найдена: " + destination);
            Cwd = Resolve(destination);
            return "";
        }
        if (args.Length is not (3 or 4) || args[0] != "df" || args[1] is not ("-h" or "--human-readable") ||
            args.Length == 4 && args[2] != "--")
            throw Error("терминал", "для этого задания введите df -h и путь к выбранной папке");
        var path = Resolve(args[^1]);
        if (!directories.Contains(path) && !files.ContainsKey(path))
            throw Error("df", "учебный путь не найден: " + args[^1]);
        LastCapacityQuery = trainingVolumes.Where(v => v.Mount == "/" || path == v.Mount ||
            path.StartsWith(v.Mount + "/", StringComparison.Ordinal)).MaxBy(v => v.Mount.Length);
        var volume = LastCapacityQuery;
        int used = volume.SizeMiB - volume.FreeMiB;
        return CapacityReading(volume) + "Filesystem    Size  Used Avail Use% Mounted on\n" +
            $"{volume.Name,-12} {DiskBashSize(volume.SizeMiB),5} {DiskBashSize(used),5} " +
            $"{DiskBashSize(volume.FreeMiB),5} {Math.Ceiling(100m * used / volume.SizeMiB),3}% {volume.Mount}\n";
    }

    private string RunPowerShellStorage(string command)
    {
        var words = PowerShellWords(command);
        if (words.Count == 0 || words[0].Quoted || !words[0].Value.Equals("Get-PSDrive", StringComparison.OrdinalIgnoreCase))
            throw Error("терминал", "для этого задания введите Get-PSDrive и букву диска без двоеточия");
        if (words.Count is not (2 or 3) || words.Count == 3 && !words[1].Value.Equals("-Name", StringComparison.OrdinalIgnoreCase))
            throw Error("Get-PSDrive", "укажите одну букву диска без двоеточия");
        string name = words[^1].Value;
        LastCapacityQuery = trainingVolumes.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw Error("Get-PSDrive", "учебный диск не найден: " + name + ". Укажите букву без двоеточия.");
        var volume = LastCapacityQuery;
        return CapacityReading(volume) + "Name   Used (GB)   Free (GB)   Root\n" +
            $"{volume.Name,-5} {DiskPsSize(volume.SizeMiB - volume.FreeMiB),11} {DiskPsSize(volume.FreeMiB),11}   {volume.Mount}\n";
    }
}
