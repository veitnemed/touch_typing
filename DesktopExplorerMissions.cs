using System;
using System.IO;

namespace TouchTypingStudio
{
    internal sealed partial class DesktopCourse
    {
        private void BuildExplorerExtension(string project, string renamed, string originalText, int scenario)
        {
            string copies = Path.Combine(project, "Copies");
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
                expected: batch ? "Выделите оба документа в Inbox и скопируйте." : "Выделите учебный отчёт в Ready и вырежьте его.");
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
                    "В Archive ожидается отчёт, а папка Ready должна освободиться.");
        }

        private void EnterExplorerChild(string goal, string child)
        {
            string name = Path.GetFileName(child);
            Add(goal, "Откройте папку " + name, "Enter",
                commandSource: IsCurrentExplorer, result: hwnd => AtFolder(hwnd, child),
                detail: "В списке файлов наберите " + name + ", чтобы выделить папку, затем нажмите Enter.",
                expected: "В том же Проводнике выделите " + name + " и откройте её.");
        }
    }
}
