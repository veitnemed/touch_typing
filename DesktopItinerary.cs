using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TouchTypingStudio
{
    internal sealed class DesktopBriefing
    {
        public readonly string Slug, Project, Topic, Keyword, Team;

        public DesktopBriefing(string slug, string project, string topic, string keyword, string team)
        {
            Slug = slug;
            Project = project;
            Topic = topic;
            Keyword = keyword;
            Team = team;
        }
    }

    // A learner can practise several rounds in a sitting or close the app after one.
    // Both paths consume the same deck. Each skill returns after the rest of its deck.
    internal sealed class DesktopItinerary
    {
        private static readonly DesktopBriefing[] Briefings = {
            new DesktopBriefing("release", "Подготовка релиза", "package", "BLOCKED", "release"),
            new DesktopBriefing("orders", "Сверка заказов", "order", "PENDING", "sales"),
            new DesktopBriefing("sync", "Проверка синхронизации", "replica", "RETRY", "storage"),
            new DesktopBriefing("docs", "Обновление документации", "guide", "DRAFT", "docs"),
            new DesktopBriefing("support", "Передача обращений", "ticket", "OPEN", "support"),
            new DesktopBriefing("audit", "Сверка записей", "record", "MISSING", "audit"),
            new DesktopBriefing("build", "Проверка сборки Python", "wheel", "FAILED", "build"),
            new DesktopBriefing("assets", "Приёмка иллюстраций", "image", "REVIEW", "design"),
            new DesktopBriefing("export", "Отправка выгрузки", "export", "READY", "data"),
            new DesktopBriefing("survey", "Разбор анкет", "response", "FLAGGED", "research"),
            new DesktopBriefing("stock", "Сверка остатков", "item", "LOW", "supply"),
            new DesktopBriefing("tests", "Итоги прогона тестов", "test", "FAIL", "quality"),
            new DesktopBriefing("inbox", "Разбор входящих заявок", "request", "URGENT", "triage"),
            new DesktopBriefing("locale", "Проверка перевода", "string", "UNTRANSLATED", "locale"),
            new DesktopBriefing("billing", "Сверка счетов", "invoice", "OVERDUE", "finance"),
            new DesktopBriefing("batch", "Итоги пакетной обработки", "job", "TIMEOUT", "compute"),
            new DesktopBriefing("links", "Проверка ссылок", "page", "BROKEN", "web"),
            new DesktopBriefing("returns", "Приёмка возвратов", "parcel", "RETURNED", "logistics"),
            new DesktopBriefing("catalog", "Публикация каталога", "entry", "STAGED", "catalog"),
            new DesktopBriefing("backup", "Проверка резервных копий", "snapshot", "STALE", "backup"),
            new DesktopBriefing("forms", "Разбор заполненных форм", "form", "INVALID", "intake"),
            new DesktopBriefing("devices", "Проверка устройств", "device", "OFFLINE", "hardware"),
            new DesktopBriefing("imports", "Результаты импорта", "row", "SKIPPED", "import"),
            new DesktopBriefing("handoff", "Передача смены", "case", "ACTIVE", "duty"),
            new DesktopBriefing("audio", "Приёмка записей", "clip", "NOISY", "audio"),
            new DesktopBriefing("archive", "Подготовка архива", "bundle", "UNSORTED", "archive"),
            new DesktopBriefing("review", "Очередь проверок кода", "change", "WAITING", "review"),
            new DesktopBriefing("alerts", "Разбор уведомлений", "alert", "CRITICAL", "ops"),
            new DesktopBriefing("reports", "Сводка за неделю", "report", "LATE", "analysts"),
            new DesktopBriefing("dispatch", "Передача отправлений", "shipment", "HOLD", "dispatch")
        };

        private readonly string directory;
        private readonly Random random = new Random(Guid.NewGuid().GetHashCode());

        public DesktopItinerary(string profileRoot)
        {
            directory = Path.Combine(profileRoot, "DesktopPractice", "decks");
            Directory.CreateDirectory(directory);
        }

        public DesktopBriefing NextBriefing() { return Briefings[Draw("briefings", Briefings.Length)]; }
        public int NextConsoleScenario() { return Draw("console", 6); }
        public int NextBrowserScenario() { return Draw("browser", 2); }
        public int NextExplorerScenario() { return Draw("explorer", 3); }
        public int NextQuickJob() { return Draw("quick-jobs", 6); }
        public int NextEditorScenario() { return Draw("editor", 2); }
        public int NextWorkdayOrder(bool chromeAvailable) { return Draw(chromeAvailable ? "day-with-browser" : "day", chromeAvailable ? 4 : 3); }
        public int NextVariant(int maximum) { return random.Next(maximum); }

        public int CompletedConsoleMissions
        {
            get { return WithLock(ReadCompleted); }
        }

        public void RecordConsoleMission()
        {
            WithLock(delegate
            {
                int count = ReadCompleted();
                if (count < int.MaxValue) count++;
                WriteState(Path.Combine(directory, "console-completed.txt"), count.ToString());
                return count;
            });
        }

        private int ReadCompleted()
        {
            string path = Path.Combine(directory, "console-completed.txt");
            int count;
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), out count) && count >= 0 ? count : 0;
        }

        private int Draw(string deck, int count)
        {
            string path = Path.Combine(directory, deck + ".txt");
            return WithLock(() => DrawLocked(path, count));
        }

        private static int WithLock(Func<int> work)
        {
            // The live dock is single-instance, but two Windows sessions can share a profile.
            using (var gate = new Mutex(false, @"Global\TouchTypingStudio.Itinerary." + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
            {
                try { gate.WaitOne(); }
                catch (AbandonedMutexException) { }
                try { return work(); }
                finally { gate.ReleaseMutex(); }
            }
        }

        private int DrawLocked(string path, int count)
        {
            var bag = new List<int>();
            int last = -1;
            if (File.Exists(path))
            {
                var saved = File.ReadAllText(path).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int number;
                if (saved.Length > 0 && saved.Length <= count && int.TryParse(saved[0], out number) && number >= 0 && number < count)
                {
                    last = number;
                    foreach (string word in saved.Skip(1))
                    {
                        if (!int.TryParse(word, out number) || number < 0 || number >= count || number == last || bag.Contains(number))
                        {
                            bag.Clear();
                            break;
                        }
                        bag.Add(number);
                    }
                }
            }
            if (bag.Count == 0)
            {
                bag.AddRange(Enumerable.Range(0, count));
                for (int end = count - 1; end > 0; end--)
                {
                    int from = random.Next(end + 1);
                    int value = bag[from]; bag[from] = bag[end]; bag[end] = value;
                }
                if (bag[0] == last && bag.Count > 1)
                {
                    int other = random.Next(1, count);
                    bag[0] = bag[other]; bag[other] = last;
                }
            }
            int result = bag[0];
            WriteState(path, string.Join(" ", bag));
            return result;
        }

        private static void WriteState(string path, string value)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, value);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
