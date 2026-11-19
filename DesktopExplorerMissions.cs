using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace TouchTypingStudio
{
    internal sealed partial class DesktopCourse
    {
        private void BuildExplorerExtension(string project, string renamed, string originalText, int scenario)
        {
            string copies = Path.Combine(project, "Copies");
            BuildFilePathReceipt(copies, Path.Combine(copies, Path.GetFileName(renamed)));
            if (scenario == 0)
            {
                const string goal = "Сверьте копию с папкой проекта";
                Add(goal, "Поднимитесь из Copies в папку проекта", "Alt+Up",
                    commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, project),
                    detail: "Alt со стрелкой вверх открывает папку на один уровень выше.",
                    expected: "Откройте родительскую папку из учебной Copies.");
                Add(goal, "Вернитесь в Copies по истории переходов", "Alt+Left",
                    commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, copies),
                    detail: "Проводник помнит предыдущий адрес.", expected: "Ожидаю возвращение в Copies.");
                Add(goal, "Снова откройте проект по истории", "Alt+Right",
                    commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, project),
                    expected: "Ожидаю папку проекта в том же окне Проводника.");
                return;
            }

            bool batch = scenario == 1;
            string task = batch ? "Передайте команде два документа одной вставкой" : "Переместите готовый отчёт в архив";
            string inboxName = batch ? "Inbox" : "Ready";
            string outboxName = batch ? "Send" : "Archive";
            string inbox = Path.Combine(project, inboxName), outbox = Path.Combine(project, outboxName);
            string first = Path.Combine(inbox, Path.GetFileName(renamed));
            string second = Path.Combine(inbox, "checklist.txt");
            string checklist = "Checked " + Path.GetFileName(renamed);
            string[] documents = batch ? new[] { first, second } : new[] { first };
            Add(task, "Вернитесь на уровень проекта", "Alt+Up",
                commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, project),
                accepted: hwnd =>
                {
                    MakeOwnedDirectory(project);
                    foreach (string directory in new[] { inbox, outbox })
                    {
                        Directory.CreateDirectory(directory);
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Учебная папка перемещена.");
                    }
                    WriteNew(first, originalText);
                    if (batch) WriteNew(second, checklist);
                },
                detail: "В папке проекта появятся материалы задания.",
                expected: "Поднимитесь из учебной Copies в её родительскую папку.");
            EnterExplorerChild(task, inbox);
            if (batch)
                Add(task, "Выделите оба документа", "Ctrl+A",
                    commandSource: hwnd => AtFolder(hwnd, inbox),
                    result: hwnd => AtFolder(hwnd, inbox) && DesktopCourseWindows.SelectionContains(hwnd, documents),
                    detail: "Tab переводит фокус в список файлов. Ctrl+A выбирает всё в этой папке.",
                    expected: "Должны быть выделены оба учебных документа.");
            Add(task, batch ? "Скопируйте выделенный пакет" : "Вырежьте готовый отчёт", batch ? "Ctrl+C" : "Ctrl+X",
                commandSource: hwnd => AtFolder(hwnd, inbox),
                result: hwnd => AtFolder(hwnd, inbox) && DesktopCourseWindows.ClipboardContainsOnly(documents),
                detail: batch ? "В буфер обмена попадут сразу два файла." : "Нажмите Home в списке файлов, чтобы выделить отчёт.",
                expected: batch ? "Выделите оба документа в Inbox и скопируйте." : "Выделите учебный отчёт в Ready и вырежьте его.",
                sourceLabel: batch ? inboxName + " · 2 документа" : Path.Combine(inboxName, Path.GetFileName(first)),
                destLabel: "Буфер обмена", outcome: batch ? "Оба файла можно будет вставить одной командой." :
                    "После вставки в Archive отчёт переместится туда.");
            steps.Last().FreshCopy = true;
            Add(task, "Вернитесь к папкам проекта", "Alt+Up",
                commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, project),
                expected: "Проводник должен открыть родительскую папку.");
            EnterExplorerChild(task, outbox);
            string firstCopy = Path.Combine(outbox, Path.GetFileName(first));
            string secondCopy = Path.Combine(outbox, Path.GetFileName(second));
            Add(task, batch ? "Вставьте оба документа в Send" : "Закончите перенос в Archive", "Ctrl+V",
                commandSource: hwnd => AtFolder(hwnd, outbox),
                result: hwnd => AtFolder(hwnd, outbox) && SameText(firstCopy, originalText) &&
                    (batch ? SameText(secondCopy, checklist) && SameText(first, originalText) && SameText(second, checklist)
                           : !File.Exists(first)),
                expected: batch ? "В Send ожидаются две копии. Оригиналы остаются в Inbox." :
                    "В Archive ожидается отчёт, а папка Ready должна освободиться.",
                sourceLabel: batch ? inboxName + " · 2 документа" : Path.Combine(inboxName, Path.GetFileName(first)),
                destLabel: batch ? outboxName : Path.Combine(outboxName, Path.GetFileName(first)),
                outcome: batch ? "В Send появятся две копии. Оригиналы останутся в Inbox." :
                    "Отчёт окажется в Archive. Папка Ready станет пустой.");
        }

        private void BuildFilePathReceipt(string folder, string file)
        {
            const string goal = "Оставьте команде путь к проверенной копии";
            string fileName = Path.GetFileName(file);
            bool ribbon = Environment.OSVersion.Version.Build < 22000;
            string receipt = Path.Combine(Folder, "Inbox-" + round, "file-location-" + round + ".txt");
            const string heading = "Report file: ";
            WriteNew(receipt, heading);
            Add(goal, ribbon ? "Выберите файл и откройте ленту Проводника" : "Скопируйте путь выделенного файла",
                ribbon ? "Alt" : "Ctrl+Shift+C",
                commandSource: hwnd => AtFolder(hwnd, folder) && DesktopCourseWindows.SelectionContains(hwnd, file),
                result: hwnd => AtFolder(hwnd, folder) && DesktopCourseWindows.SelectionContains(hwnd, file) &&
                    DesktopCourseWindows.FreshClipboardFrom(hwnd, clipboardBeforePath) && HasFreshClipboardPath(clipboardBeforePath, file),
                detail: ribbon ? "Выделите " + fileName + " стрелками. Нажмите и отпустите Alt. По буквам на ленте выберите «Главная» и «Копировать путь»." :
                    "В списке Copies выделите " + fileName + " стрелками, затем нажмите сочетание.",
                expected: "В папке Copies выделите " + fileName + " и повторите копирование пути.",
                sourceLabel: fileName, destLabel: Path.GetFileName(receipt), outcome: "В карточке будет полный адрес файла с именем и расширением.");
            steps.Last().CopiesFilePath = true;
            Add(goal, "Перейдите к полю адреса в карточке", "Ctrl+End", commandSource: hwnd => InNote(hwnd, receipt),
                expected: "В открытой карточке нажмите Ctrl+End.");
            OpenPracticeNote(steps.Last(), receipt);
            Add(goal, "Вставьте скопированный путь", "Ctrl+V", commandSource: hwnd => InNote(hwnd, receipt),
                detail: "После Report file появится адрес из Проводника. Кавычки можно оставить.",
                expected: "Вернитесь в карточку и вставьте адрес файла.");
            Add(goal, "Сохраните карточку с адресом файла", "Ctrl+S", commandSource: hwnd => InNote(hwnd, receipt),
                result: hwnd => InNote(hwnd, receipt) && (SameText(receipt, heading + file) || SameText(receipt, heading + Quote(file))),
                detail: "Проверьте, что строка заканчивается именем " + fileName + ".",
                expected: "После Report file должен стоять полный путь к " + fileName + " в Copies.");
            Add(goal, "Вернитесь в Copies, чтобы продолжить передачу", "Alt+Tab",
                result: hwnd => AtFolder(hwnd, folder),
                detail: "Выберите Проводник с проверенной копией и отпустите Alt.",
                expected: "Нужен учебный Проводник с папкой Copies.");
        }

        internal static bool IsCopiedFilePath(uint before, uint after, IDataObject data, string file)
        {
            // Ctrl+C normally publishes a file list. Only accept explicit text from Copy Path.
            if (before == 0 || after == 0 || before == after || data == null ||
                !data.GetDataPresent(DataFormats.UnicodeText, false)) return false;
            string text = data.GetData(DataFormats.UnicodeText, false) as string;
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);
            return string.Equals(text, file, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasFreshClipboardPath(uint before, string file)
        {
            uint after = Native.GetClipboardSequenceNumber();
            if (after == before || after == 0) return false;
            try
            {
                return IsCopiedFilePath(before, after, Clipboard.GetDataObject(), file);
            }
            catch (ExternalException) { return false; }
        }

        private void EnterExplorerChild(string goal, string child)
        {
            string name = Path.GetFileName(child);
            Add(goal, "Откройте папку " + name, "Enter",
                commandSource: IsCurrentExplorer,
                // Explorer can open each folder in a new window. Keep observing the
                // source until the foreground window actually displays the destination.
                result: hwnd => Native.IsExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), child),
                accepted: hwnd => explorer = hwnd,
                detail: "Выберите " + name + " в списке стрелками. Если папка уже выделена, нажмите Enter.",
                expected: "Откройте учебную папку " + name + " и оставьте её окно активным.");
        }
    }
}
