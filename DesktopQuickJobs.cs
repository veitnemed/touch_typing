using System;
using System.IO;
using System.Linq;

namespace TouchTypingStudio
{
    internal sealed partial class DesktopCourse
    {
        private void BuildQuickJob(DesktopBriefing briefing, int kind)
        {
            string desk = Path.Combine(Folder, "Desk-" + round + "-" + kind);
            MakeOwnedDirectory(desk);
            switch (kind)
            {
                case 0: RenameAttachment(desk, briefing); break;
                case 1: DeliverFiles(desk, briefing, true); break;
                case 2: DeliverFiles(desk, briefing, false); break;
                case 3: RemoveDuplicate(desk, briefing); break;
                case 4: MoveUrgentLine(desk, briefing); break;
                case 5: CorrectHeader(desk, briefing); break;
            }
        }

        private void OpenPracticeFolder(Step step, string folder)
        {
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            OpenForStep(step, Path.GetFileName(folder), shell, Quote(folder), hwnd =>
            {
                if (!Native.IsExplorer(hwnd) || !SamePath(Native.ExplorerFolder(hwnd), folder)) return false;
                explorer = hwnd;
                return true;
            });
        }

        private void RenameAttachment(string folder, DesktopBriefing briefing)
        {
            const string goal = "Дайте вложению понятное имя";
            string old = Path.Combine(folder, "attachment.txt");
            string name = "brief-" + briefing.Topic;
            string renamed = Path.Combine(folder, name + ".txt");
            string content = "Team: " + briefing.Team + "\nDocument: " + briefing.Topic;
            WriteNew(old, content.Replace("\n", "\r\n"));
            Add(goal, "Перейдите в список файлов и выделите attachment.txt", "F2",
                commandSource: hwnd => AtFolder(hwnd, folder) && DesktopCourseWindows.SelectionContains(hwnd, old),
                result: hwnd => AtFolder(hwnd, folder) && DesktopCourseWindows.HasTextFocus(),
                detail: "Если курсор в поиске, нажимайте Tab или F6 до списка файлов. Затем Home выделит вложение. Нажмите F2.",
                expected: "Переведите фокус из поиска в список через Tab или F6. Нажмите Home, затем F2.");
            OpenPracticeFolder(steps.Last(), folder);
            Add(goal, "Введите " + name + " и подтвердите", "Enter", commandSource: hwnd => AtFolder(hwnd, folder),
                result: hwnd => AtFolder(hwnd, folder) && !File.Exists(old) && SameText(renamed, content),
                detail: "Замените выделенное имя, оставив расширение .txt.", copyText: name,
                expected: "Если имя отличается, выделите вложение, нажмите F2 и исправьте его.",
                sourceLabel: "attachment.txt", destLabel: Path.GetFileName(renamed), outcome: "Название вложения укажет, к какому документу оно относится.");
        }

        private void DeliverFiles(string desk, DesktopBriefing briefing, bool batch)
        {
            string goal = batch ? "Передайте два вложения одним копированием" : "Уберите принятый отчёт в архив";
            string from = Path.Combine(desk, batch ? "Input" : "Approved");
            string to = Path.Combine(desk, batch ? "Send" : "Archive");
            Directory.CreateDirectory(from);
            Directory.CreateDirectory(to);
            if (((File.GetAttributes(from) | File.GetAttributes(to)) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Учебная папка перемещена.");
            string[] names = batch ? new[] { "report.txt", "details.txt" } : new[] { "approved.txt" };
            string[] files = names.Select(n => Path.Combine(from, n)).ToArray();
            string[] contents = names.Select(n => briefing.Team + " / " + briefing.Topic + " / " + n).ToArray();
            for (int i = 0; i < files.Length; i++) WriteNew(files[i], contents[i]);
            int first = steps.Count;
            if (batch)
                Add(goal, "Перейдите в список и выделите отчёт с приложением", "Ctrl+A", commandSource: hwnd => AtFolder(hwnd, from),
                    result: hwnd => AtFolder(hwnd, from) && DesktopCourseWindows.SelectionContains(hwnd, files),
                    detail: "Если курсор в поиске, нажимайте Tab или F6 до списка файлов. Затем Ctrl+A выделит оба документа.",
                    expected: "В Input переведите фокус из поиска в список через Tab или F6 и повторите Ctrl+A.");
            Add(goal, batch ? "Скопируйте оба файла" : "Вырежьте утверждённый отчёт", batch ? "Ctrl+C" : "Ctrl+X",
                commandSource: hwnd => AtFolder(hwnd, from) && DesktopCourseWindows.SelectionContains(hwnd, files),
                result: hwnd => AtFolder(hwnd, from) && DesktopCourseWindows.ClipboardContainsOnly(files),
                detail: batch ? "Оба оригинала должны остаться во входящих." : "Если курсор в поиске, нажимайте Tab или F6 до списка файлов. Нажмите Home для выбора approved.txt, затем Ctrl+X.",
                expected: "В " + Path.GetFileName(from) + " перейдите в список через Tab или F6, выделите документы и повторите сочетание.");
            OpenPracticeFolder(steps[first], from);
            Add(goal, "Перейдите к папкам отправки", "Alt+Up", commandSource: IsCurrentExplorer,
                result: hwnd => Native.IsExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), desk),
                accepted: hwnd => explorer = hwnd,
                expected: "Из " + Path.GetFileName(from) + " перейдите на один уровень вверх.");
            EnterExplorerChild(goal, to);
            Add(goal, batch ? "Передайте оба документа в Send" : "Поместите отчёт в Archive", "Ctrl+V",
                commandSource: hwnd => AtFolder(hwnd, to),
                result: hwnd => AtFolder(hwnd, to) && DeliveryComplete(to, files, contents, batch),
                expected: "Если буфер пуст, вернитесь в " + Path.GetFileName(from) + ", повторите " +
                    (batch ? "Ctrl+A и Ctrl+C" : "Home и Ctrl+X") + ", затем вставьте в " + Path.GetFileName(to) + ".",
                sourceLabel: Path.GetFileName(from), destLabel: Path.GetFileName(to),
                outcome: batch ? "В Send появятся обе копии. Оригиналы останутся у вас." : "Принятый отчёт окажется в архиве.");
        }

        internal static bool DeliveryComplete(string destination, string[] sources, string[] contents, bool copy)
        {
            for (int i = 0; i < sources.Length; i++)
                if (!SameText(Path.Combine(destination, Path.GetFileName(sources[i])), contents[i]) ||
                    (copy ? !SameText(sources[i], contents[i]) : File.Exists(sources[i]))) return false;
            return true;
        }

        private void RemoveDuplicate(string desk, DesktopBriefing briefing)
        {
            const string goal = "Удалите повтор из списка дел";
            string file = Path.Combine(desk, "todo-" + briefing.Slug + ".txt");
            string line = "Check " + briefing.Topic;
            string final = line + "\nSend to " + briefing.Team;
            WriteNew(file, (line + "\n" + final).Replace("\n", "\r\n"));
            Add(goal, "Вернитесь к первому пункту списка", "Ctrl+Home", commandSource: hwnd => InNote(hwnd, file),
                detail: "Первые два пункта одинаковы. Удалите первую строку целиком.");
            OpenPracticeNote(steps.Last(), file);
            Add(goal, "Выделите первую строку вместе с переносом", "Shift+Down", commandSource: hwnd => InNote(hwnd, file),
                detail: "Из начала строки Shift+↓ выделит её до начала следующей.");
            Add(goal, "Удалите выделенный повтор", "Delete", commandSource: hwnd => InNote(hwnd, file),
                detail: "В списке останутся два разных пункта.");
            SaveQuickNote(goal, file, final, "Проверьте список и сохраните", "Если пропал лишний пункт, отмените правку через Ctrl+Z.");
        }

        private void MoveUrgentLine(string desk, DesktopBriefing briefing)
        {
            const string goal = "Поднимите срочную заявку на первое место";
            string file = Path.Combine(desk, "queue-" + briefing.Slug + ".txt");
            string urgent = "Urgent: " + briefing.Topic, regular = "Review\nSend to " + briefing.Team;
            WriteNew(file, (regular + "\n" + urgent).Replace("\n", "\r\n"));
            Add(goal, "Перейдите к срочному пункту в конце очереди", "Ctrl+End", commandSource: hwnd => InNote(hwnd, file),
                detail: "Последняя строка должна стать первой.");
            OpenPracticeNote(steps.Last(), file);
            Add(goal, "Выделите срочный пункт", "Shift+Home", commandSource: hwnd => InNote(hwnd, file));
            Add(goal, "Вырежьте текст срочного пункта", "Ctrl+X", commandSource: hwnd => InNote(hwnd, file),
                result: hwnd => InNote(hwnd, file) && ClipboardMatches(urgent), expected: "Выделите строку от конца до начала и повторите Ctrl+X.");
            Add(goal, "Вернитесь к началу очереди", "Ctrl+Home", commandSource: hwnd => InNote(hwnd, file));
            Add(goal, "Вставьте срочный пункт перед первой строкой", "Ctrl+V", commandSource: hwnd => InNote(hwnd, file),
                detail: "После вставки нажмите Enter, чтобы отделить следующий пункт.");
            SaveQuickNote(goal, file, urgent + "\n" + regular, "Разделите пункты через Enter и сохраните очередь",
                "Срочный пункт должен быть на отдельной первой строке.");
        }

        private void CorrectHeader(string desk, DesktopBriefing briefing)
        {
            const string goal = "Исправьте заголовок рабочего письма";
            string file = Path.Combine(desk, "message-" + briefing.Slug + ".txt");
            string header = "To: " + briefing.Team, body = "Subject: " + briefing.Topic;
            WriteNew(file, "To: draft\r\n" + body);
            Add(goal, "Перейдите к адресу получателя в начале письма", "Ctrl+Home", commandSource: hwnd => InNote(hwnd, file),
                detail: "Черновик пока адресован draft. Укажите команду " + briefing.Team + ".");
            OpenPracticeNote(steps.Last(), file);
            Add(goal, "Выделите заголовок без переноса строки", "Shift+End", commandSource: hwnd => InNote(hwnd, file));
            SaveQuickNote(goal, file, header + "\n" + body, "Введите " + header + " и сохраните письмо",
                "Тема во второй строке должна остаться прежней.", header);
        }

        private void SaveQuickNote(string goal, string file, string final, string action, string recovery, string literal = null)
        {
            Add(goal, action, "Ctrl+S", commandSource: hwnd => InNote(hwnd, file),
                result: hwnd => InNote(hwnd, file) && SameText(file, final),
                detail: literal == null ? "" : "Замените выделенный текст.", copyText: literal,
                expected: recovery + " После исправления сохраните через Ctrl+S.");
        }
    }
}
