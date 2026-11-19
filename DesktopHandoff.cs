using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace TouchTypingStudio
{
    internal sealed partial class DesktopCourse
    {
        private void BuildHandoff(string inbox, DesktopBriefing briefing, string projectName)
        {
            string source = Path.Combine(inbox, "ticket-" + briefing.Slug + ".txt");
            string cardName = "handoff-" + briefing.Slug + ".txt";
            string card = Path.Combine(inbox, cardName);
            string reference = projectName.ToUpperInvariant();
            string heading = "Team: " + briefing.Team + "\r\nReference: ";
            WriteNew(source, "Incoming: " + briefing.Topic + "\r\nReference on the last line:\r\n" + reference);
            WriteNew(card, heading);

            string goal = "Передайте номер заявки команде " + briefing.Team;
            Add(goal, "Откройте входящую заявку, чтобы заполнить карточку", "Ctrl+O", commandSource: hwnd => InNote(hwnd, card),
                result: hwnd => DesktopCourseWindows.EditorDialog(hwnd, noteWindow),
                detail: "В карточке смены пока пустует Reference. Найдите номер в письме команды.",
                expected: "В учебной карточке нажмите Ctrl+O.");
            OpenPracticeNote(steps.Last(), card);
            Add(goal, "Откройте файл входящей заявки", "Enter",
                result: hwnd => DesktopCourseWindows.InFileApp(hwnd, "notepad", source),
                accepted: hwnd => noteWindow = hwnd, copyText: source,
                detail: "Вставьте путь из подсказки в поле имени файла.",
                expected: "Нужно письмо " + Path.GetFileName(source) + ". Если открыли другой файл, снова нажмите Ctrl+O.");
            AcceptDialogOnEnter();
            Add(goal, "Найдите номер в конце входящей заявки", "Ctrl+End", commandSource: hwnd => InNote(hwnd, source),
                detail: "В последней строке указан номер. Вы перенесёте его в карточку смены " + cardName + ".",
                expected: "В открытой заявке нажмите Ctrl+End и оставьте курсор после номера.");
            Add(goal, "Выделите строку с номером", "Shift+Home", commandSource: hwnd => InNote(hwnd, source),
                detail: "Удерживайте Shift и нажмите Home. Выделится текст от конца строки до её начала.",
                expected: "Если курсор сместился, нажмите Ctrl+End, затем Shift+Home.");
            Add(goal, "Скопируйте выделенный номер заявки", "Ctrl+C", commandSource: hwnd => InNote(hwnd, source),
                result: hwnd => InNote(hwnd, source) && ClipboardMatches(reference),
                expected: "Выделите только последнюю строку через Ctrl+End, Shift+Home и повторите Ctrl+C.",
                sourceLabel: Path.GetFileName(source), destLabel: cardName,
                outcome: "В буфере обмена будет номер из последней строки входящей заявки.");
            Add(goal, "Откройте карточку, в которой команда ждёт номер", "Ctrl+O", commandSource: hwnd => InNote(hwnd, source),
                result: hwnd => DesktopCourseWindows.EditorDialog(hwnd, noteWindow),
                expected: "В Блокноте с исходной заявкой нажмите Ctrl+O.");
            Add(goal, "Введите имя карточки и откройте её", "Enter",
                result: hwnd => DesktopCourseWindows.InFileApp(hwnd, "notepad", card),
                accepted: hwnd => noteWindow = hwnd,
                detail: "Карточка лежит рядом с заявкой. Наберите имя вручную, чтобы сохранить номер в буфере.\n" + cardName,
                expected: "Нужна карточка " + cardName + " в той же папке, где лежит заявка. Если открыли другой файл, снова нажмите Ctrl+O.");
            AcceptDialogOnEnter();
            Add(goal, "Поставьте курсор после Reference в карточке", "Ctrl+End", commandSource: hwnd => InNote(hwnd, card),
                expected: "В карточке нажмите Ctrl+End.");
            Add(goal, "Вставьте номер из входящей заявки", "Ctrl+V", commandSource: hwnd => InNote(hwnd, card),
                expected: "Вернитесь в карточку и вставьте номер после Reference.",
                sourceLabel: Path.GetFileName(source), destLabel: cardName,
                outcome: "Команда увидит, к какой заявке относится передача.");
            Add(goal, "Сохраните заполненную карточку смены", "Ctrl+S", commandSource: hwnd => InNote(hwnd, card),
                result: hwnd => InNote(hwnd, card) && SameText(card, heading.Replace("\r\n", "\n") + reference),
                expected: "Проверьте вторую строку: Reference: " + reference + ". Затем повторите Ctrl+S.",
                outcome: "Номер заявки останется в карточке команды на диске.");
        }

        private static bool ClipboardMatches(string value)
        {
            try { return Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), value, StringComparison.Ordinal); }
            catch (ExternalException) { return false; }
        }

        private void OpenPracticeNote(Step step, string file)
        {
            OpenForStep(step, Path.GetFileName(file), Path.Combine(Environment.SystemDirectory, "notepad.exe"), Quote(file),
                delegate(IntPtr hwnd)
                {
                    if (!DesktopCourseWindows.InFileApp(hwnd, "notepad", file)) return false;
                    noteWindow = hwnd;
                    return true;
                }, start => start.WorkingDirectory = Path.GetDirectoryName(file));
        }

        private void AcceptDialogOnEnter()
        {
            Step enter = steps[steps.Count - 1], open = steps[steps.Count - 2];
            enter.AcceptNoteDialog = true;
            // Enter can dismiss a file dialog before the queued key is processed.
            // Remember the verified initial dialog and any replacement seen on retry.
            open.Accepted += hwnd => enter.NoteDialog = hwnd;
            enter.Source = hwnd => hwnd != IntPtr.Zero &&
                (hwnd == enter.NoteDialog || DesktopCourseWindows.EditorDialog(hwnd, noteWindow));
        }
    }
}
