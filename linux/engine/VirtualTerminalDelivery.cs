using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchTypingStudio;

// These exercises never start a host process. Files and modification stamps belong
// to this scene. The real GNU/Windows tools are exercised separately on temporary files.
internal sealed partial class VirtualTerminal
{
    internal string DeliveryLesson { get; private set; } = "";
    internal string TrustedCopy { get; private set; } = "";
    internal int TransferCount { get; private set; } = -1;
    private bool previewed;
    private string lastDigest;
    private readonly Dictionary<string,int> stamps = new(StringComparer.Ordinal);
    private static string Digest(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    internal void PrepareDelivery(string kind, bool powershell, int variant)
    {
        if (powershell) UsePowerShell(); else StartInsideProject();
        DeliveryLesson = kind; history.Clear();
        if (kind == "sync")
        {
            foreach (var d in new[]{"Work","Backup"}) { CreateFolder(d); CreateFolder(d + "/notes"); }
            string[] places = ["east", "west", "home"];
            string place = places[variant];
            Seed("Work/notes/weekly.txt", place + ": ready Fri\n", 2);
            Seed("Backup/notes/weekly.txt", place + ": ready Tue\n", 1);
            Seed("Work/notes/contacts.txt", "desk: 14\n", 1);
            Seed("Backup/notes/contacts.txt", "desk: 14\n", 1);
            Seed("Work/summary.csv", "week,total\n4," + (16 + variant * 7) + "\n", 2);
            Seed("Backup/archive.txt", "Старый акт: сохранить.\n", 1);
            history.Add("Рабочая папка: Work. Прошлая копия: Backup.");
            history.Add("В Backup есть старый архив: не удаляйте его.");
        }
        else
        {
            string body = "invoice,amount\n"+(312+variant)+","+(1140+variant*61)+"\n";
            TrustedCopy = variant == 1 ? "west.csv" : "east.csv";
            Seed("east.csv", TrustedCopy == "east.csv" ? body : body.Replace("invoice", "invo1ce"), 2);
            Seed("west.csv", TrustedCopy == "west.csv" ? body : body.Replace("invoice", "invo1ce"), 2);
            Seed("receipt.txt", Digest(body) + "\n", 1);
            history.Add("Получены east.csv и west.csv. Имена и размеры не доказывают подлинность содержимого.");
            history.Add("Отправитель отдельно передал SHA-256 в receipt.txt.");
        }
        void Seed(string name, string body, int stamp) { StoreFile(name, body); stamps[Resolve(name)] = stamp; }
    }
    private void CopyDelivery(VirtualTerminal target)
    {
        target.DeliveryLesson = DeliveryLesson; target.TrustedCopy = TrustedCopy;
        target.previewed = previewed; target.TransferCount = TransferCount; target.lastDigest = lastDigest;
        foreach (var pair in stamps) target.stamps[pair.Key] = pair.Value;
    }
    internal object DeliveryProof(string taskId, string creditedCommand) => new
    {
        command = creditedCommand ?? LastCommand, LastSuccess, output = lastDigest == null ? LastOutput : [lastDigest],
        // A harmless dry-run between copying and reading is not part of the
        // read goal. Bind the count only to the operation which produced it.
        count = taskId is "command/delivery-preview" or "command/delivery-apply" or "command/delivery-recheck"
            ? (int?)TransferCount : null,
        // Evidence is taken from the student's current scene, not from the string
        // they typed or an expected list of filenames.
        files = stamps.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new {
            path=p.Key, stamp=p.Value, sha256=Digest(files[p.Key]) }).ToArray()
    };
    private string[] DeliveryWords(string command)
    {
        string[] words;
        if (IsPowerShell)
        {
            // Only this literal, read-only property access; not a general evaluator.
            var displayHash = Regex.Match(command.Trim(), @"^\(\s*(Get-FileHash\s+[^()]+)\s*\)\.Hash$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            words = PowerShellWords(displayHash.Success ? displayHash.Groups[1].Value : command).Select(w => w.Value).ToArray();
            if (displayHash.Success && words.Length == 2) words[0] = "Get-FileHash/Hash";
        }
        else
        {
            var tokens = Tokenize(command);
            if (tokens.Any(t => t.Operator)) throw Error("терминал", "в этом поручении нужна одна команда без перенаправлений");
            words = tokens.Select(t => t.Value).ToArray();
        }
        return words;
    }
    private bool DeliveryMatches(string submitted, string expected)
    {
        var a = DeliveryWords(submitted); var b = DeliveryWords(expected);
        var cmp = IsPowerShell ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string Family(string tool) => tool.Equals("Get-FileHash/Hash",cmp) && IsPowerShell ? "Get-FileHash" : tool;
        if (a.Length < 2 || b.Length < 2 || !Family(a[0]).Equals(Family(b[0]),cmp)) return false;
        bool sync = b[0] is "rsync" or "robocopy";
        if (sync)
        {
            bool Dry(string[] words) => IsPowerShell ? words.Skip(1).Any(w => w.Equals("/L",cmp)) :
                words.Length > 1 && words[1].StartsWith('-') && words[1].Contains('n');
            if (Dry(a) != Dry(b)) return false;
        }
        else if (a.Length != 2 || b.Length != 2 || LessonFile(a[1]) != LessonFile(b[1])) return false;
        return Clone().Execute(submitted).Success;
    }
    private string DeliveryCommand(string command)
    {
        var words = DeliveryWords(command); lastDigest = null;
        if (words.Length == 0) return "";
        string tool = IsPowerShell ? words[0].ToLowerInvariant() : words[0];
        string[] args = words[1..];
        if (tool == (IsPowerShell ? "get-content" : "cat") && args.Length == 1)
            return files[LessonFile(args[0])];
        if (DeliveryLesson == "hash" && (IsPowerShell ? tool is "get-filehash" or "get-filehash/hash" : tool == "sha256sum") && args.Length == 1)
        {
            string path = LessonFile(args[0]);
            lastDigest = Digest(files[path]);
            if (tool == "get-filehash/hash") return lastDigest + "\n";
            return IsPowerShell ? $"Algorithm  Hash                                                              Path\nSHA256     {Digest(files[path])}  {Name(path)}\n" :
                $"{Digest(files[path]).ToLowerInvariant()}  {Name(path)}\n";
        }
        if (DeliveryLesson != "sync") throw Error(tool, "здесь можно сверить SHA-256 и прочитать учебные файлы");
        bool preview;
        if (!IsPowerShell && tool == "rsync")
        {
            if (args.Length == 0) throw Error(tool, "укажите параметры, Work/ и Backup/");
            string opts = args[0];
            if (!opts.StartsWith('-') || opts.Length < 2 || opts[1..].Any(c => !"rtni".Contains(c)) || !"rti".All(opts.Contains) ||
                !(args[1..].SequenceEqual(new[]{"--","Work/","Backup/"}) || args[1..].SequenceEqual(new[]{"Work/","Backup/"})))
                throw Error(tool, "нужны содержимое Work/, назначение Backup/ и параметры r, t, i; n включает предварительный просмотр");
            preview = opts.Contains('n');
        }
        else if (IsPowerShell && tool == "robocopy")
        {
            var flags = args.Skip(2).Select(s => s.ToUpperInvariant()).ToArray();
            if (args.Length < 3 || !args[0].Equals("Work", StringComparison.OrdinalIgnoreCase) ||
                !args[1].Equals("Backup", StringComparison.OrdinalIgnoreCase) || !flags.Contains("/E") ||
                flags.Any(x => x is not ("/E" or "/L" or "/R:1" or "/W:1")))
                throw Error(tool, "в этом поручении копируется Work в Backup с подпапками (/E), без удаления; /L только показывает план");
            preview = flags.Contains("/L");
        }
        else throw Error(tool, "команда не относится к этой учебной копии");
        if (!preview && !previewed) throw Error(tool, "Сначала просмотрите план: архив в Backup должен остаться.");
        string src = Resolve("Work") + "/", dst = Resolve("Backup") + "/";
        var selected = files.Keys.Where(f => f.StartsWith(src, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(f => (Source:f, Name:f[src.Length..], Destination:dst + f[src.Length..]))
            .Where(p => !files.TryGetValue(p.Destination, out var existing) ||
                Encoding.UTF8.GetByteCount(existing) != Encoding.UTF8.GetByteCount(files[p.Source]) ||
                stamps.GetValueOrDefault(p.Source) != stamps.GetValueOrDefault(p.Destination)).ToArray();
        var output = new List<string>();
        foreach (var p in selected)
        {
            bool existed = files.ContainsKey(p.Destination);
            output.Add(IsPowerShell ? (existed ? "Newer       " : "New File    ") + p.Name.Replace('/','\\') :
                (existed ? ">f..t...... " : ">f+++++++++ ") + p.Name);
            if (!preview) { StoreFile(p.Destination, files[p.Source]); stamps[p.Destination] = stamps[p.Source]; }
        }
        TransferCount = selected.Length; previewed = true;
        if (IsPowerShell)
        {
            bool extra = files.Keys.Any(f => f.StartsWith(dst, StringComparison.Ordinal) && !files.ContainsKey(src + f[dst.Length..]));
            output.Add($"Files: copied {selected.Length}; extras {(extra ? 1 : 0)}");
        }
        // With -i an unchanged Bash run prints no changed path. Do not invent an rsync success message.
        return string.Join("\n", output);
    }
    private string LessonFile(string arg)
    {
        var candidate = IsPowerShell ? arg.Replace('\\','/') : arg;
        string path = Resolve(candidate);
        var match = files.Keys.FirstOrDefault(f => stamps.ContainsKey(f) && f.Equals(path,
            IsPowerShell ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        return match ?? throw Error("файл", "нет такого файла в этом поручении: " + arg);
    }
}
