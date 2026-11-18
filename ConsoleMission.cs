using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TouchTypingStudio
{
    // Each prompt has a real artifact that a different, equivalent command can also produce.
    internal sealed class ConsoleMission
    {
        internal sealed class Prompt
        {
            public string Action, Command, Detail, Retry;
            public string CompletionPrefix, CompletionName;
            public Func<bool> Done;
            public bool QuickRetry;
        }

        public readonly string Folder;
        public readonly List<Prompt> Commands = new List<Prompt>();
        public string Title { get; private set; }
        public string Delivery { get; private set; }
        private string[] deliveredLines;
        private readonly string number, team, topic, keyword;
        private readonly Dictionary<string, string[]> inputs = new Dictionary<string, string[]>();

        public ConsoleMission(string folder, int recipe, DesktopBriefing briefing, int batch, int completed)
        {
            Folder = folder;
            number = batch.ToString();
            team = briefing.Team;
            topic = briefing.Topic;
            keyword = briefing.Keyword;
            switch (recipe)
            {
                case 0: BuildLogReport(); break;
                case 1: BuildTicketQueue(); break;
                case 2: BuildDataDelivery(); break;
                case 3: BuildConfigRelease(); break;
                case 4: BuildShiftNotes(); break;
                case 5: BuildPriorityList(); break;
                default: throw new ArgumentOutOfRangeException("recipe");
            }
            if (completed >= 3) AddCount();
            if (completed >= 6) AddArchive();
        }

        private string Seed(string name, params string[] lines)
        {
            inputs.Add(name, lines);
            using (var file = new FileStream(Path.Combine(Folder, name), FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(file, Encoding.ASCII))
                foreach (string line in lines) writer.WriteLine(line);
            return name;
        }

        private void Add(string action, string command, string detail, Func<bool> check, string retry, bool quickRetry = true)
        {
            Commands.Add(new Prompt { Action = action, Command = command, Detail = detail, Done = check,
                Retry = retry, QuickRetry = quickRetry });
        }

        private bool Regular(string relative)
        {
            string path = Path.Combine(Folder, relative);
            string parent = Path.GetDirectoryName(path);
            var file = new FileInfo(path);
            return file.Exists && file.Length <= 65536 && (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                (File.GetAttributes(Folder) & FileAttributes.ReparsePoint) == 0 &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) == 0;
        }

        private bool ContainsLines(string relative, IEnumerable<string> expected, bool ordered = true)
        {
            if (!Regular(relative)) return false;
            string[] lines = File.ReadAllLines(Path.Combine(Folder, relative));
            if (ordered) return lines.SequenceEqual(expected, StringComparer.Ordinal);
            return lines.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
        }

        private void AddInventory(string extension, string output, string[] names, bool inHandoff = false)
        {
            string wildcard = (inHandoff ? @"handoff\" : "") + "*." + extension;
            Add("Запишите имена " + wildcard + " в " + output, "dir /b " + wildcard + " > " + output,
                "Маска * выбирает все совпадения. Ключ /b оставляет только имена, а > записывает их в файл.",
                () => ContainsLines(output, names, false), "Проверьте маску и имя списка " + output + ".");
        }

        private void AddFolder()
        {
            const string destination = "handoff";
            string path = Path.Combine(Folder, destination);
            Add("Создайте папку handoff для передачи", "md handoff",
                "Команда md создаёт папку в текущем каталоге.",
                () => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
                "В папке задания ожидается подпапка handoff.");
        }

        private void Deliver(string file, string[] lines)
        {
            Delivery = file;
            deliveredLines = lines;
            string copy = Path.Combine("handoff", file);
            Add("Положите " + file + " в папку handoff", @"copy /y " + file + @" handoff\",
                "Укажите исходный файл и папку назначения. Ключ /y позволяет повторить копирование.",
                () => ContainsLines(file, lines, false) && Regular(copy) &&
                    File.ReadAllBytes(Path.Combine(Folder, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(Folder, copy))),
                "В handoff ожидается " + file + " с данными из этого задания.");
        }

        private void BatchCopy(string extension, params string[] files)
        {
            Add("Скопируйте все *." + extension + " в handoff", "copy /y *." + extension + @" handoff\",
                "Одна маска отправляет в папку сразу все подходящие файлы.",
                () => files.All(name => ContainsLines(Path.Combine("handoff", name), inputs[name])),
                "Проверьте, что все файлы *." + extension + " скопированы с содержимым.");
        }

        private void BuildLogReport()
        {
            Title = "Найдите сбой в журналах сборки";
            string first = Seed("build-" + number + ".log", "OK compiling " + topic, "ERROR " + keyword + " timeout " + number);
            string second = Seed("worker-" + number + ".log", "ERROR " + team + " backlog " + number, "OK closed");
            Seed("readme.txt", "Operator notes");
            AddInventory("log", "index.txt", new[] { first, second });
            var errors = new[] { first + ":" + inputs[first][1], second + ":" + inputs[second][0] };
            Add("Соберите строки ERROR из всех журналов в errors.txt", "findstr /i ERROR *.log > errors.txt",
                "findstr ищет сразу в нескольких файлах. Ключ /i разрешает любой регистр букв.",
                () => ContainsLines("errors.txt", errors, false), "В errors.txt ожидаются обе ошибки с именами журналов.");
            AddFolder();
            Deliver("errors.txt", errors);
        }

        private void BuildTicketQueue()
        {
            Title = "Подготовьте список открытых обращений";
            string first = Seed("desk-" + number + ".ticket", "OPEN " + number + " " + topic, "CLOSED case " + number);
            string second = Seed("mail-" + number + ".ticket", "CLOSED " + team, "OPEN " + keyword + " " + team);
            var found = new[] { first + ":" + inputs[first][0], second + ":" + inputs[second][1] };
            Add("Найдите обращения со статусом OPEN в *.ticket", "findstr /b OPEN *.ticket > open.txt",
                "Ключ /b проверяет начало строки. Сохраните совпадения в open.txt вместе с именами источников.",
                () => ContainsLines("open.txt", found, false), "В open.txt ожидаются две строки со статусом OPEN.");
            Add("Отсортируйте открытые обращения в queue.txt", "sort /r /o queue.txt open.txt",
                "Ключ /r задаёт обратный порядок, /o указывает файл для результата.",
                () => ContainsLines("queue.txt", found.Reverse()), "В queue.txt сначала ожидается обращение из mail.");
            Commands.Last().CompletionPrefix = "sort /r /o queue.txt op";
            Commands.Last().CompletionName = "open.txt";
            AddFolder();
            Deliver("queue.txt", found.Reverse().ToArray());
        }

        private void BuildDataDelivery()
        {
            Title = "Передайте комплект таблиц с описью";
            string a = Seed("cost-" + number + ".csv", "id,team", number + "," + team);
            string b = Seed("time-" + number + ".csv", "id,topic", number + "," + topic);
            string c = Seed("units-" + number + ".csv", "id,unit", number + "," + keyword);
            Seed("private-notes.txt", "Internal rough draft");
            var files = new[] { a, b, c };
            AddInventory("csv", "manifest.txt", files);
            AddFolder();
            BatchCopy("csv", files);
            Deliver("manifest.txt", files);
            Commands.Last().CompletionPrefix = "copy /y manifest.txt ha";
            Commands.Last().CompletionName = "handoff";
        }

        private void BuildConfigRelease()
        {
            Title = "Подготовьте конфигурацию к выпуску";
            string draft = Seed("draft.cfg", "mode=" + keyword, "build=" + number);
            string target = Seed("target-" + number + ".cfg", "owner=" + team);
            Seed("notes.txt", topic);
            AddFolder();
            BatchCopy("cfg", draft, target);
            string release = "release-" + number + ".cfg";
            Add("Переименуйте копию draft.cfg в " + release, @"ren handoff\draft.cfg " + release,
                "У команды ren путь указывается в первом аргументе. Новое имя относится к той же папке.",
                () => !File.Exists(Path.Combine(Folder, "handoff", draft)) &&
                    ContainsLines(@"handoff\" + release, inputs[draft]) && ContainsLines(draft, inputs[draft]),
                "Переименуйте файл внутри handoff. Исходный draft.cfg должен остаться на месте.");
            Delivery = "manifest.txt";
            deliveredLines = new[] { release, target };
            AddInventory("cfg", @"handoff\manifest.txt", deliveredLines, true);
        }

        private void BuildShiftNotes()
        {
            Title = "Передайте следующей смене незакрытые дела";
            string a = Seed("morning.part", "DONE " + number, "TODO verify " + topic);
            string b = Seed("evening.part", "TODO call " + team, "DONE " + keyword);
            Add("Объедините morning.part и evening.part в brief.txt", "copy /y /b morning.part+evening.part brief.txt",
                "Знак + склеивает файлы в указанном порядке. Каждая часть уже заканчивается переводом строки.",
                () => ContainsLines("brief.txt", inputs[a].Concat(inputs[b])), "В brief.txt ожидаются обе части в порядке смен.");
            string[] todos = { inputs[a][1], inputs[b][0] };
            Add("Отберите строки TODO в next.txt", "findstr /b TODO brief.txt > next.txt",
                "Префикс TODO отмечает дела для передачи. Ключ /b берёт совпадения в начале строки.",
                () => ContainsLines("next.txt", todos), "В next.txt ожидаются только два незакрытых дела.");
            AddFolder();
            Deliver("next.txt", todos);
        }

        private void BuildPriorityList()
        {
            Title = "Поставьте срочные задачи в начало очереди";
            string a = Seed("team-" + number + ".todo", "P3 " + team, "P1 A-" + number + " " + topic, "P2 " + keyword);
            string b = Seed("desk-" + number + ".todo", "P3 " + topic, "P1 Z-" + number + " " + team);
            string[] tasks = inputs[a].Concat(inputs[b]).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Add("Соберите две очереди *.todo в tasks.txt по приоритету", "type *.todo | sort > tasks.txt",
                "type читает очереди. Знак | передаёт их строки на сортировку, а > сохраняет результат.",
                () => ContainsLines("tasks.txt", tasks), "Проверьте, что в tasks.txt пять строк и первыми идут P1.");
            string[] urgent = tasks.Where(line => line.StartsWith("P1 ")).ToArray();
            Add("Сохраните срочные строки P1 в today.txt", "findstr /b P1 tasks.txt > today.txt",
                "Команда оставит в today.txt только задачи с высшим приоритетом.",
                () => ContainsLines("today.txt", urgent), "В today.txt ожидаются обе строки P1.");
            AddFolder();
            Deliver("today.txt", urgent);
        }

        private void AddCount()
        {
            Add("Посчитайте строки " + Delivery + " для передачи", @"type handoff\" + Delivery + " | find /v /c \"\" > handoff\\count.txt",
                "type передаёт текст дальше по |. Конструкция find /v /c \"\" возвращает число строк, сохраните его в handoff\\count.txt.",
                () => ContainsLines(@"handoff\count.txt", new[] { deliveredLines.Length.ToString() }),
                "В handoff\\count.txt ожидается число строк готового файла.");
        }

        private void AddArchive()
        {
            bool tar = File.Exists(Path.Combine(Environment.SystemDirectory, "tar.exe"));
            Add("Упакуйте папку handoff в handoff.zip", ArchiveCommand(tar),
                tar ? "Ключи -c и -f создают архив с указанным именем. Ключ -a выбирает ZIP по расширению." :
                    "PowerShell вызовет упаковщик .NET. Если имя архива уже занято, сначала переименуйте предыдущую копию.",
                ArchiveMatches, "В handoff.zip ожидаются файлы комплекта и число строк.", false);
        }

        internal static string ArchiveCommand(bool tar)
        {
            if (tar) return "tar -caf handoff.zip handoff";
            return "powershell -NoProfile -Command \"Add-Type -AssemblyName System.IO.Compression.FileSystem; " +
                "[IO.Compression.ZipFile]::CreateFromDirectory('handoff','handoff.zip',0,$true)\"";
        }

        private bool ArchiveMatches()
        {
            if (!Regular("handoff.zip") || !ContainsLines(Path.Combine("handoff", Delivery), deliveredLines, false) ||
                !ContainsLines(@"handoff\count.txt", new[] { deliveredLines.Length.ToString() })) return false;
            string folder = Path.Combine(Folder, "handoff");
            try
            {
                using (var stream = File.OpenRead(Path.Combine(Folder, "handoff.zip")))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string[] files = Directory.GetFiles(folder);
                    int folders = archive.Entries.Count(e => e.FullName == "handoff/" && e.Length == 0);
                    if (folders > 1 || archive.Entries.Count != files.Length + folders) return false;
                    foreach (string file in files)
                    {
                        string name = Path.GetFileName(file);
                        var entry = archive.GetEntry("handoff/" + name) ?? archive.GetEntry("handoff\\" + name);
                        if (entry == null || !Regular(Path.Combine("handoff", name)) || entry.Length != new FileInfo(file).Length)
                            return false;
                        using (var content = entry.Open())
                        using (var copy = new MemoryStream())
                        {
                            // Bound decompression as well as the declared size.
                            var buffer = new byte[8192];
                            int read;
                            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (copy.Length + read > 65536) return false;
                                copy.Write(buffer, 0, read);
                            }
                            if (!File.ReadAllBytes(file).SequenceEqual(copy.ToArray())) return false;
                        }
                    }
                    return files.Any(f => Path.GetFileName(f) == Delivery) && files.Any(f => Path.GetFileName(f) == "count.txt");
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
