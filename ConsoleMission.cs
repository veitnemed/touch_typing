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
            public string Action, Command, Detail, Retry, SourceLabel, DestinationLabel, Outcome;
            public string ReadCommand, TaskHint;
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
                case 0: BuildLogReport(batch); break;
                case 1: BuildTicketQueue(); break;
                case 2: BuildDataDelivery(); break;
                case 3: BuildConfigRelease(); break;
                case 4: BuildShiftNotes(batch); break;
                case 5: BuildPriorityList(batch); break;
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

        private void Add(string action, string command, string source, string destination, string outcome, string detail,
            Func<bool> check, string retry, bool quickRetry = true)
        {
            Commands.Add(new Prompt { Action = action, Command = command, SourceLabel = source, DestinationLabel = destination,
                Outcome = outcome, Detail = detail, Done = check,
                Retry = retry, QuickRetry = quickRetry });
        }

        private void ReadFirst(string command, string question)
        {
            var step = Commands.Last();
            step.ReadCommand = command;
            step.TaskHint = question;
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
            Add("Составьте опись файлов", "dir /b " + wildcard + " > " + output,
                (inHandoff ? "Папка handoff" : "Папка задания") + ", файлы *." + extension, output,
                "В описи будет " + names.Length + " строки, в том числе " + names[0] + ".",
                "dir /b выводит имена. Знак > запишет этот список в " + output + ". Содержимое самих файлов останется на месте.",
                () => ContainsLines(output, names, false), "Проверьте маску и имя списка " + output + ".");
        }

        private void AddFolder()
        {
            const string destination = "handoff";
            string path = Path.Combine(Folder, destination);
            Add("Подготовьте место для передачи", "md handoff", "Текущая папка задания", @"handoff\",
                "Появится пустая папка для готового комплекта.", "md создаёт папку. На следующем шаге положите в неё файлы.",
                () => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0,
                "В папке задания ожидается подпапка handoff.");
        }

        private void Deliver(string file, string[] lines)
        {
            Delivery = file;
            deliveredLines = lines;
            string copy = Path.Combine("handoff", file);
            Add("Передайте копию готового файла", @"copy /y " + file + @" handoff\", file, copy,
                "Исходный файл останется на месте. В handoff будет передана его точная копия.",
                "После copy /y идёт имя источника, затем папка для копии. Окончание \\ указывает папку. /y допускает запись поверх предыдущей копии.",
                () => ContainsLines(file, lines, false) && Regular(copy) &&
                    File.ReadAllBytes(Path.Combine(Folder, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(Folder, copy))),
                "В handoff ожидается " + file + " с данными из этого задания.");
        }

        private void BatchCopy(string extension, params string[] files)
        {
            Add("Передайте группу файлов одной командой", "copy /y *." + extension + @" handoff\",
                string.Join(", ", files), @"handoff\", "В handoff появятся " + files.Length + " файла с прежними именами.",
                "Маска *." + extension + " выбирает сразу весь комплект. Например, " + files[0] + " будет скопирован в handoff\\" + files[0] + ".",
                () => files.All(name => ContainsLines(name, inputs[name]) && ContainsLines(Path.Combine("handoff", name), inputs[name])) &&
                    Directory.GetFileSystemEntries(Path.Combine(Folder, "handoff")).Length == files.Length,
                "В handoff нужны только " + string.Join(", ", files) + ". Если туда попали другие файлы, переместите их в отдельную папку и повторите команду.");
        }

        private void BuildLogReport(int batch)
        {
            Title = "Найдите сбой в журналах сборки";
            bool timeoutInBuild = batch % 2 == 0;
            string first = Seed("build-" + number + ".log", "OK compiling " + topic,
                "ERROR " + keyword + (timeoutInBuild ? " timeout " : " backlog ") + number);
            string second = Seed("worker-" + number + ".log",
                "ERROR " + team + (timeoutInBuild ? " backlog " : " timeout ") + number, "OK closed");
            Seed("readme.txt", "Operator notes");
            AddInventory("log", "index.txt", new[] { first, second });
            var errors = new[] { first + ":" + inputs[first][1], second + ":" + inputs[second][0] };
            Add("Соберите строки ERROR из всех журналов в errors.txt", "findstr /i ERROR *.log > errors.txt",
                first + ", " + second, "errors.txt", "В отчёте будут две строки ERROR с именами их журналов.",
                "findstr /i ищет ERROR в каждом журнале. Знак > сохранит найденные строки в отчёт.",
                () => ContainsLines("errors.txt", errors, false), "В errors.txt ожидаются обе ошибки с именами журналов.");
            AddFolder();
            Deliver("errors.txt", errors);
            string journal = timeoutInBuild ? first : second;
            const string attachment = @"handoff\incident.log";
            Add("Приложите к отчёту журнал с тайм-аутом", "copy /y " + journal + " " + attachment,
                first + ", " + second, attachment, "В комплекте появится полный исходный журнал с записью timeout.",
                "В строке с timeout возьмите имя перед двоеточием. Наберите copy /y, пробел, это имя, пробел и handoff\\incident.log.",
                () => ContainsLines(attachment, inputs[journal]) && ContainsLines(journal, inputs[journal]),
                "Нужен полный журнал со словом timeout. Команда type errors.txt снова покажет имена. Повторный copy /y заменит вложение.");
            ReadFirst("type errors.txt", "Откройте в CMD свой отчёт и найдите строку с timeout. Из какого журнала она взята?");
        }

        private void BuildTicketQueue()
        {
            Title = "Подготовьте список открытых обращений";
            string first = Seed("desk-" + number + ".ticket", "OPEN " + number + " " + topic, "CLOSED case " + number);
            string second = Seed("mail-" + number + ".ticket", "CLOSED " + team, "OPEN " + keyword + " " + team);
            var found = new[] { first + ":" + inputs[first][0], second + ":" + inputs[second][1] };
            Add("Оставьте только незакрытые обращения", "findstr /b OPEN *.ticket > open.txt",
                first + ", " + second, "open.txt", "В списке останутся две заявки, по одной из каждого файла.",
                "Наберите findstr /b, пробел и найденную метку, затем пробел и *.ticket > open.txt. Ключ /b ищет метку в начале строки.",
                () => ContainsLines("open.txt", found, false), "В open.txt ожидаются две строки со статусом OPEN.");
            ReadFirst("type " + first, "Посмотрите две записи в первом файле. Какой меткой начинается обращение, которое ещё ждёт ответа?");
            Add("Отсортируйте открытые обращения в queue.txt", "sort /r /o queue.txt open.txt",
                "open.txt", "queue.txt", "Первым в очереди станет обращение из " + second + ".",
                "sort читает open.txt. /r меняет порядок сортировки, /o указывает, куда записать очередь.",
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
            var inventory = Commands.Last();
            inventory.Action = "Составьте опись только для таблиц";
            inventory.SourceLabel = "Три таблицы в учебной папке";
            inventory.Detail = "После dir /b введите маску из звёздочки, точки и расширения таблиц, затем пробел и > manifest.txt.";
            ReadFirst("dir /b", "Посмотрите файлы в папке. Какое расширение общее у трёх таблиц с данными cost, time и units?");
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
                @"handoff\draft.cfg", @"handoff\" + release, "Копия получит имя выпуска. Исходный draft.cfg останется в папке задания.",
                "ren принимает сначала путь к файлу, затем его новое имя внутри той же папки.",
                () => !File.Exists(Path.Combine(Folder, "handoff", draft)) &&
                    ContainsLines(@"handoff\" + release, inputs[draft]) && ContainsLines(draft, inputs[draft]),
                "Переименуйте файл внутри handoff. Исходный draft.cfg должен остаться на месте.");
            Delivery = "manifest.txt";
            deliveredLines = new[] { release, target };
            AddInventory("cfg", @"handoff\manifest.txt", deliveredLines, true);
            var inventory = Commands.Last();
            inventory.SourceLabel = "Файлы выпуска в handoff";
            inventory.Action = "Включите в опись только конфигурации выпуска";
            inventory.Detail = @"Наберите dir /b handoff\*, добавьте точку и расширение двух конфигураций, затем пробел и > handoff\manifest.txt.";
            ReadFirst(@"dir /b handoff", "Посмотрите на готовую папку выпуска. Какую маску использовать для двух конфигураций в ней?");
        }

        private void BuildShiftNotes(int batch)
        {
            Title = "Передайте следующей смене незакрытые дела";
            string pending = batch % 2 == 0 ? "TODO" : "NEXT";
            string a = Seed("morning.part", "DONE " + number, pending + " verify " + topic);
            string b = Seed("evening.part", pending + " call " + team, "DONE " + keyword);
            Add("Объедините morning.part и evening.part в brief.txt", "copy /y /b morning.part+evening.part brief.txt",
                a + ", " + b, "brief.txt", "В одной сводке будут четыре строки: сначала утренние, затем вечерние.",
                "Знак + у команды copy объединяет содержимое в один файл. Обе исходные части останутся в папке.",
                () => ContainsLines("brief.txt", inputs[a].Concat(inputs[b])), "В brief.txt ожидаются обе части в порядке смен.");
            string[] todos = { inputs[a][1], inputs[b][0] };
            Add("Оставьте дела для следующей смены", "findstr /b " + pending + " brief.txt > next.txt",
                "brief.txt", "next.txt", "Следующая смена получит два дела: проверить " + topic + " и связаться с " + team + ".",
                "После findstr /b наберите метку незакрытой строки из сводки, затем пробел и brief.txt > next.txt.",
                () => ContainsLines("next.txt", todos), "В next.txt ожидаются два незакрытых дела. Посмотрите их метку через type brief.txt.");
            ReadFirst("type brief.txt", "Посмотрите, какая метка стоит перед verify и call в этой сводке. DONE отмечает завершённые дела.");
            AddFolder();
            Deliver("next.txt", todos);
        }

        private void BuildPriorityList(int batch)
        {
            Title = "Поставьте срочные задачи в начало очереди";
            string priority = batch % 2 == 0 ? "P0" : "P1";
            string a = Seed("team-" + number + ".todo", "P3 " + team, priority + " A-" + number + " " + topic, "P2 " + keyword);
            string b = Seed("desk-" + number + ".todo", "P3 " + topic, priority + " Z-" + number + " " + team);
            string[] tasks = inputs[a].Concat(inputs[b]).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Add("Соберите две очереди *.todo в tasks.txt по приоритету", "type *.todo | sort > tasks.txt",
                a + ", " + b, "tasks.txt", "В очереди будет пять задач из двух списков, отсортированных по метке приоритета.",
                "type читает две очереди. Знак | передаёт строки на сортировку, затем > записывает отсортированную очередь.",
                () => ContainsLines("tasks.txt", tasks), "Проверьте, что в tasks.txt все пять строк по порядку приоритета.");
            string[] urgent = tasks.Where(line => line.StartsWith(priority + " ", StringComparison.Ordinal)).ToArray();
            Add("Выберите задачи с наивысшим приоритетом", "findstr /b " + priority + " tasks.txt > today.txt",
                "tasks.txt", "today.txt", "В плане на сегодня останутся две самые срочные задачи.",
                "Меньшая цифра означает более высокий приоритет. После findstr /b укажите выбранную метку, затем пробел и tasks.txt > today.txt.",
                () => ContainsLines("today.txt", urgent), "В today.txt ожидаются обе строки с самой маленькой цифрой приоритета.");
            ReadFirst("type tasks.txt", "Прочитайте отсортированную очередь. Какая метка с буквой P стоит у самых срочных задач?");
            AddFolder();
            Deliver("today.txt", urgent);
        }

        private void AddCount()
        {
            Add("Посчитайте строки " + Delivery + " для передачи", @"type handoff\" + Delivery + " | find /v /c \"\" > handoff\\count.txt",
                @"handoff\" + Delivery, @"handoff\count.txt", "В контрольном файле будет одно число — " + deliveredLines.Length + ".",
                "type передаёт строки через |. find /v /c \"\" считает их, затем > сохраняет число в файл.",
                () => ContainsLines(@"handoff\count.txt", new[] { deliveredLines.Length.ToString() }),
                "В handoff\\count.txt ожидается число строк готового файла.");
        }

        private void AddArchive()
        {
            bool tar = File.Exists(Path.Combine(Environment.SystemDirectory, "tar.exe"));
            Add("Упакуйте папку handoff в handoff.zip", ArchiveCommand(tar),
                @"handoff\", "handoff.zip", "В одном архиве будут файлы комплекта и контрольное число строк.",
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
