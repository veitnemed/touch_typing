using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TouchTypingStudio
{
    internal enum DesktopTrack { Workday, Console }

    internal sealed partial class DesktopCourse : IDisposable
    {
        private sealed class Step
        {
            public string Goal, Action, Shortcut, Detail, CopyText, Expected, Optional, ConsoleTitle;
            public Func<IntPtr, bool> Source, Result;
            public Action<IntPtr> Accepted;
            public bool QuickRetry;
            public string CompletionPrefix, CompletionName;
            public bool SawCompletion;
        }

        private readonly string token = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string chrome, code;
        private readonly List<Step> steps = new List<Step>();
        private readonly DesktopTrack track;
        private readonly DesktopItinerary itinerary;
        private string optional;
        private int round, index;
        private DateTime listeningSince, commandAt, nextCheck;
        private IntPtr explorer, noteWindow, browser, editor, settings, taskManager, runWindow, saveDialog, startMenu;
        private bool watchingStartSearch;
        private bool entered;
        private bool abortCheck;
        private Action pendingLaunch;
        private IDisposable ownedConsole;

        private Step Current { get { return steps[index]; } }
        public readonly string Folder;
        public int Completed { get; private set; }
        public int Rejected { get; private set; }
        public int StageNumber { get { return index + 1; } }
        public int StageLength { get { return steps.Count; } }
        public int RoundNumber { get { return round; } }
        public int MissionStep { get { return 1 + steps.Take(index).Reverse().TakeWhile(step => step.Goal == Goal).Count(); } }
        public int MissionLength { get { return MissionStep + steps.Skip(index + 1).TakeWhile(step => step.Goal == Goal).Count(); } }
        public string Goal { get { return Current.Goal; } }
        public string Action { get { return Current.Action; } }
        private bool WaitingForTab { get { return Current.CompletionPrefix != null && !Current.SawCompletion; } }
        public string Shortcut { get { return WaitingForTab ? "Tab" : Current.Shortcut; } }
        public string Detail
        {
            get
            {
                if (WaitingForTab)
                    return "Наберите начало команды. Клавиша Tab подставит имя " + Current.CompletionName + ".";
                if (Current.SawCompletion)
                    return "Проверьте, что подставилось " + Current.CompletionName + ", и нажмите Enter. Если требуется, допишите имя вручную.";
                return Current.Detail;
            }
        }
        public string CopyText { get { return WaitingForTab ? Current.CompletionPrefix : Current.CopyText; } }
        public bool IsConsoleCommand { get { return Current.ConsoleTitle != null; } }
        public string Notice { get; private set; }
        public string ProjectTitle { get; private set; }
        public bool CanSkipOptional { get { return Current.Optional != null; } }
        public string[] ObservedShortcuts { get { return Current.CompletionPrefix == null ?
            new[] { Current.Shortcut } : new[] { "Tab", Current.Shortcut }; } }

        public DesktopCourse(string profileRoot, DesktopTrack track = DesktopTrack.Workday)
        {
            this.track = track;
            itinerary = new DesktopItinerary(profileRoot);
            Folder = Path.Combine(profileRoot, "DesktopPractice", DateTime.Now.ToString("yyyy-MM-dd") + "-" + token);
            Directory.CreateDirectory(Folder);
            chrome = DesktopCourseWindows.FindChrome();
            code = RuntimeTools.Current.Code;
            BuildNextProject();
        }

        // The observer only supplies the expected key. Foreground and result are checked here independently.
        public void Key(DesktopStroke input)
        {
            var step = Current;
            if (input.Timestamp < listeningSince) return;
            if (WaitingForTab && input.Chord == "Enter")
            {
                Rejected++;
                Notice = "Сначала наберите начало команды и дополните имя клавишей Tab.";
                return;
            }
            if (!string.Equals(input.Chord, Shortcut, StringComparison.OrdinalIgnoreCase)) return;
            if (!Check(step.Source, input.Window))
            {
                Rejected++;
                Notice = step.ConsoleTitle == null ? "Сочетание зафиксировано. " + step.Expected :
                    "Переключитесь через Alt+Tab в учебное окно " + step.ConsoleTitle + " и повторите команду.";
                return;
            }
            if (WaitingForTab)
            {
                step.SawCompletion = true;
                Notice = "Теперь выполните подставленную команду.";
                return;
            }
            commandAt = DateTime.UtcNow;
            nextCheck = commandAt;
            entered = true;
            Notice = step.Result == null ? "Сочетание принято в учебном окне." : "Сочетание принято. Проверяю результат.";
        }

        public bool Poll(IntPtr active)
        {
            if (watchingStartSearch && DesktopCourseWindows.StartMenu(active)) startMenu = active;
            if (!entered) return false;
            var now = DateTime.UtcNow;
            if (now < nextCheck) return false;
            nextCheck = now.AddMilliseconds(300);
            var step = Current;
            if (Check(step.Result, active))
            {
                try { if (step.Accepted != null) step.Accepted(active); }
                catch (IOException) { Notice = "Учебная папка недоступна. Восстановите сеанс через панель."; abortCheck = true; }
                catch (UnauthorizedAccessException) { Notice = "Нет доступа к учебной папке. Восстановите сеанс через панель."; abortCheck = true; }
                if (abortCheck) { entered = false; abortCheck = false; return false; }
                Completed++;
                if (++index == steps.Count) BuildNextProject();
                Ready("Шаг зачтён.");
                return true;
            }
            if (step.QuickRetry && now - commandAt >= TimeSpan.FromSeconds(2))
            {
                entered = false;
                Rejected++;
                Notice = step.Expected + " Нажмите ↑, исправьте команду и нажмите Enter.";
            }
            else if (now - commandAt >= TimeSpan.FromSeconds(20))
            {
                entered = false;
                Rejected++;
                Notice = "Результат пока не подтверждён. " + step.Expected + " Затем повторите сочетание.";
            }
            else if (now - commandAt >= TimeSpan.FromSeconds(1.2))
                Notice = "Результат пока не подтверждён. " + step.Expected;
            return false;
        }

        // Clicking a control or result manually doesn't satisfy a keyboard step.
        public void RejectMouseAction()
        {
            if (!entered && !Current.SawCompletion) return;
            entered = false;
            Current.SawCompletion = false;
            Rejected++;
            Notice = "Зафиксирован щелчок. Повторите шаг с клавиатуры.";
        }

        public bool Recover()
        {
            BuildNextProject();
            Ready("Начат новый проект. Результаты и файлы предыдущих заданий сохранены.");
            return true;
        }

        public void ActivateMission()
        {
            var launch = pendingLaunch;
            pendingLaunch = null;
            if (launch != null) launch();
        }

        public void Dispose()
        {
            pendingLaunch = null;
            if (ownedConsole == null) return;
            ownedConsole.Dispose();
            ownedConsole = null;
        }

        public bool SkipOptional()
        {
            var group = Current.Optional;
            if (group == null) return false;
            while (index < steps.Count && steps[index].Optional == group) index++;
            if (index == steps.Count) BuildNextProject();
            Ready(group + " пропущен. Продолжайте со следующего шага.");
            return true;
        }

        private void BuildNextProject()
        {
            Dispose();
            round++;
            index = 0;
            steps.Clear();
            explorer = noteWindow = browser = editor = settings = taskManager = runWindow = saveDialog = startMenu = IntPtr.Zero;
            watchingStartSearch = false;
            DesktopBriefing briefing = itinerary.NextBriefing();
            ProjectTitle = briefing.Project;
            string projectName = briefing.Slug + "-" + round.ToString("000") + "-" + token;
            string lab = Path.Combine(Folder, "cmd-" + projectName);
            MakeOwnedDirectory(lab);
            var mission = new ConsoleMission(lab, itinerary.NextConsoleScenario(), briefing,
                itinerary.NextVariant(900) + 100, itinerary.CompletedConsoleMissions);
            if (track == DesktopTrack.Console)
            {
                BuildConsole(mission);
                Ready("Введите команду в учебной командной строке и нажмите Enter.");
                return;
            }
            string project = Path.Combine(Folder, projectName);
            string source = Path.Combine(project, "draft-" + briefing.Slug + ".txt");
            string note = Path.Combine(project, "note-" + briefing.Slug + ".txt");
            string renamed = Path.Combine(project, "review-" + briefing.Slug + ".txt");
            string copies = Path.Combine(project, "Copies");
            string originalText = briefing.Team + " / " + briefing.Topic + "\nStatus: ready";
            string editedText = briefing.Team + " / " + briefing.Topic + "\nStatus: reviewed";
            string browserPath = Path.Combine(Folder, "pages");
            string titleA = projectName + " / Board " + token;
            string titleB = projectName + " / Notes " + token;
            string board = Path.Combine(browserPath, "board-" + round + ".html");
            string brief = Path.Combine(browserPath, "notes-" + round + ".html");

            int order = itinerary.NextWorkdayOrder(chrome != null);
            Action consoleLab = () => BuildConsole(mission);
            Action browserLab = () => BuildChrome(browserPath, board, brief, titleA, titleB, briefing);
            if (order == 1) consoleLab();
            if (order == 2) BuildSystem();
            if (order == 3) browserLab();
            BuildProjectFiles(projectName, project, source, note, renamed, copies, originalText);
            if (order != 1) consoleLab();
            if (round % 2 == 0 || code == null)
                BuildNotepadRevision(Path.Combine(copies, Path.GetFileName(renamed)), originalText);
            if (order != 3) browserLab();
            BuildCode(project, renamed, originalText, editedText);
            if (order != 2) BuildSystem();
            Ready("Начните с указанного сочетания.");
        }

        private void BuildProjectFiles(string projectName, string project, string source, string note,
            string renamed, string copies, string originalText)
        {
            const string setup = "Откройте учебную папку и создайте проект";
            Add(setup, "Откройте Проводник", "Win+E", result: Native.IsExplorer,
                accepted: hwnd => explorer = hwnd, expected: "Должно открыться настоящее окно Проводника.");
            Address(setup, "учебную папку", Folder, IsCurrentExplorer);
            CreateFolder(setup, Folder, project, projectName, () => PrepareDraft(project, source));

            const string notes = "Создайте заметку и сохраните её в проекте";
            OpenRun(notes);
            Add(notes, "Откройте учебный черновик в Блокноте", "Enter", commandSource: hwnd => hwnd == runWindow,
                result: hwnd => DesktopCourseWindows.InFileApp(hwnd, "notepad", source),
                accepted: hwnd => noteWindow = hwnd,
                detail: "В окне «Выполнить» введите команду и нажмите Enter.",
                copyText: "notepad.exe " + Quote(source), expected: "Нужен учебный черновик в Блокноте.");
            Add(notes, "Наберите заметку и вызовите «Сохранить как»", "Ctrl+Shift+S",
                commandSource: hwnd => InNote(hwnd, source), result: hwnd => DesktopCourseWindows.EditorDialog(hwnd, noteWindow),
                accepted: hwnd => saveDialog = hwnd,
                detail: "Наберите две строки точно как здесь, затем нажмите сочетание.\n" + originalText,
                copyText: originalText.Replace("\n", "\r\n"), expected: "Ожидаю диалог «Сохранить как» учебного Блокнота.");
            Add(notes, "Сохраните заметку по указанному пути", "Enter",
                commandSource: hwnd => hwnd == saveDialog,
                result: hwnd => InNote(hwnd, note) && SameText(note, originalText),
                detail: "Укажите полный путь в поле «Имя файла».\n" + note,
                copyText: note, expected: "Проверьте имя файла и обе строки заметки.");
            Add(notes, "Сверните окно Блокнота", "Win+Down", commandSource: hwnd => InNote(hwnd, note),
                result: hwnd => DesktopCourseWindows.Minimized(noteWindow),
                expected: "Ожидаю свёрнутое окно. Если оно было развёрнуто, нажмите сочетание ещё раз.");
            Add(notes, "Вернитесь в Блокнот переключением окон", "Alt+Tab", result: hwnd => InNote(hwnd, note),
                detail: "Удерживайте Alt и нажимайте Tab, пока не выделится окно с вашей заметкой.",
                expected: "Вернитесь в учебную заметку, а не в другой Блокнот.");
            Add(notes, "Поставьте Блокнот у левого края", "Win+Left", commandSource: hwnd => InNote(hwnd, note),
                result: hwnd => Native.AtScreenHalf(noteWindow, true),
                expected: "Учебный Блокнот должен занять левую половину экрана.");
            Add(notes, "Закройте сохранённую заметку", "Alt+F4",
                commandSource: hwnd => hwnd == noteWindow && SameText(note, originalText),
                result: hwnd => !Native.IsWindowVisible(noteWindow) || !DesktopCourseWindows.InFileApp(noteWindow, "notepad", note),
                detail: "Если Windows предлагает выбрать второе окно, сначала выйдите из выбора клавишей Esc.",
                expected: "Закройте окно учебной заметки после сохранения.");

            const string filing = "Разложите файлы проекта";
            Add(filing, "Откройте папку проекта в Проводнике", "Win+E", result: Native.IsExplorer,
                accepted: hwnd => explorer = hwnd, expected: "Ожидаю окно Проводника.");
            Address(filing, "папку проекта", project, IsCurrentExplorer);
            Add(filing, "Выберите заметку и начните переименование", "F2",
                commandSource: hwnd => AtFolder(hwnd, project) && DesktopCourseWindows.SelectionContains(hwnd, note),
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.HasTextFocus(),
                detail: "Стрелками выделите " + Path.GetFileName(note) + ", затем нажмите F2.",
                expected: "Выделите именно " + Path.GetFileName(note) + " в папке проекта.");
            Add(filing, "Введите новое имя и подтвердите", "Enter", commandSource: IsCurrentExplorer,
                result: hwnd => IsCurrentExplorer(hwnd) && !File.Exists(note) && SameText(renamed, originalText),
                detail: "Введите " + Path.GetFileNameWithoutExtension(renamed) + ". Расширение .txt оставьте прежним.",
                copyText: Path.GetFileNameWithoutExtension(renamed), expected: "Проверьте новое имя и сохранённое расширение .txt.");
            CreateFolder(filing, project, copies, "Copies");
            Add(filing, "Скопируйте переименованную заметку", "Ctrl+C",
                commandSource: hwnd => AtFolder(hwnd, project) && DesktopCourseWindows.SelectionContains(hwnd, renamed),
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.ClipboardContainsOnly(renamed),
                detail: "Стрелками выделите " + Path.GetFileName(renamed) + " и скопируйте файл.",
                expected: "В буфере обмена ожидается только учебная заметка.");
            // Keep the copied file in the clipboard while entering the address of the destination.
            Add(filing, "Выделите адресную строку Проводника", "Ctrl+L", commandSource: IsCurrentExplorer,
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.HasTextFocus(),
                detail: "В следующем шаге можно дописать имя подпапки к текущему пути.",
                expected: "Ожидаю адресную строку учебного Проводника.");
            Add(filing, "Откройте подпапку Copies", "Enter", commandSource: IsCurrentExplorer,
                result: hwnd => AtFolder(hwnd, copies),
                detail: "Нажмите End, допишите \\Copies к адресу в строке и нажмите Enter.",
                expected: "Откройте подпапку Copies в папке проекта. Файл должен остаться в буфере обмена.");
            Add(filing, "Вставьте копию заметки", "Ctrl+V",
                commandSource: hwnd => AtFolder(hwnd, copies) && DesktopCourseWindows.ClipboardContainsOnly(renamed),
                result: hwnd => AtFolder(hwnd, copies) && SameText(Path.Combine(copies, Path.GetFileName(renamed)), originalText),
                expected: "В подпапке Copies должна появиться копия заметки.");
            BuildExplorerExtension(project, renamed, originalText, itinerary.NextExplorerScenario());
        }

        private void BuildConsole(ConsoleMission mission)
        {
            string title = "CMD-" + token + "-" + round;
            string target = Path.Combine(mission.Folder, "handoff", mission.Delivery);
            string goal = mission.Title;
            IntPtr console = IntPtr.Zero;
            Func<IntPtr, bool> inConsole = hwnd =>
            {
                if (!DesktopCourseWindows.IsPracticeConsole(hwnd, title)) return false;
                console = hwnd;
                return true;
            };
            Action addManualLaunch = delegate
            {
                string launcher = DesktopCourseWindows.PracticeConsoleCommand(mission.Folder, title);
                OpenRun(goal);
                Add(goal, "Откройте командную строку в папке задания", "Enter", commandSource: hwnd => hwnd == runWindow,
                    result: inConsole,
                    detail: "Скопируйте адрес через Ctrl+Alt+C, вставьте в «Выполнить» через Ctrl+V и нажмите Enter.",
                    copyText: launcher, expected: "Ожидаю окно " + title + ".");
            };
            if (track == DesktopTrack.Console)
            {
                pendingLaunch = delegate
                {
                    try { ownedConsole = DesktopCourseWindows.OpenPracticeConsole(mission.Folder, title); }
                    catch (Exception error)
                    {
                        if (!(error is System.ComponentModel.Win32Exception) && !(error is InvalidOperationException) &&
                            !(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                        int start = steps.Count;
                        addManualLaunch();
                        var fallback = steps.GetRange(start, steps.Count - start);
                        steps.RemoveRange(start, fallback.Count);
                        steps.InsertRange(index, fallback);
                        Ready("Откройте учебную консоль через «Выполнить». " + error.Message);
                    }
                };
            }
            else addManualLaunch();
            foreach (var task in mission.Commands)
            {
                var current = task;
                Add(goal, current.Action, "Enter", commandSource: inConsole,
                    result: hwnd => inConsole(hwnd) && current.Done(), detail: current.Detail, copyText: current.Command,
                    expected: current.Retry);
                steps[steps.Count - 1].ConsoleTitle = title;
                steps[steps.Count - 1].QuickRetry = current.QuickRetry;
                steps[steps.Count - 1].CompletionPrefix = current.CompletionPrefix;
                steps[steps.Count - 1].CompletionName = current.CompletionName;
            }
            Add(goal, "Покажите готовый файл " + mission.Delivery + " в Проводнике", "Enter", commandSource: inConsole,
                result: hwnd => Native.IsExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), Path.GetDirectoryName(target)) &&
                    DesktopCourseWindows.SelectionContains(hwnd, target),
                detail: "Параметр /select открывает папку и выделяет готовый файл. %cd% подставляет путь текущей папки.",
                copyText: "explorer /select,\"%cd%\\handoff\\" + mission.Delivery + "\"",
                expected: "В Проводнике должен быть выделен " + mission.Delivery + ".");
            steps[steps.Count - 1].ConsoleTitle = title;
            Add(goal, "Вернитесь из Проводника к командной строке", "Alt+Tab", result: inConsole,
                detail: "Удерживайте Alt и выберите окно " + title + " клавишей Tab.",
                expected: "Выберите учебное окно " + title + ".");
            Add(goal, "Закройте командную строку и передайте результат", "Enter",
                commandSource: hwnd => console != IntPtr.Zero && hwnd == console,
                result: hwnd => !DesktopCourseWindows.IsPracticeConsole(console, title),
                accepted: hwnd => itinerary.RecordConsoleMission(), detail: "Команда exit завершает текущий сеанс CMD.",
                copyText: "exit", expected: "Ожидаю закрытие учебной командной строки.");
            steps[steps.Count - 1].ConsoleTitle = title;
        }

        private void BuildChrome(string pages, string board, string brief, string titleA, string titleB, DesktopBriefing briefing)
        {
            if (chrome == null) return;
            MakeOwnedDirectory(pages);
            WriteNew(board, Page(titleA, briefing.Topic, "Read the notes from " + briefing.Team + "."));
            WriteNew(brief, Page(titleB, briefing.Team, briefing.Keyword + " is the marker for the " + briefing.Topic + " handoff."));
            const string goal = "Проверьте учебные страницы в Chrome";
            optional = "Chrome";
            OpenRun(goal);
            string command = Quote(chrome) + " --user-data-dir=" + Quote(Path.Combine(Folder, "Chrome")) +
                " --no-first-run --no-default-browser-check --new-window " + Quote(new Uri(board).AbsoluteUri);
            Add(goal, "Откройте отдельное учебное окно Chrome", "Enter", commandSource: hwnd => hwnd == runWindow,
                result: hwnd => DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA),
                accepted: hwnd => browser = hwnd,
                detail: "Вставьте подготовленную команду в «Выполнить». Она открывает учебный профиль Chrome.",
                copyText: command, expected: "Ожидаю страницу проекта в учебном Chrome.");
            Add(goal, "Создайте вторую вкладку", "Ctrl+T", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && !DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA) && DesktopCourseWindows.HasTextFocus(),
                expected: "В учебном окне должна открыться новая вкладка с активной адресной строкой.");
            Add(goal, "Выделите адресную строку новой вкладки", "Ctrl+L", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "Фокус должен перейти в адресную строку учебного Chrome.");
            string url = new Uri(brief).AbsoluteUri;
            Add(goal, "Откройте во второй вкладке страницу заметок", "Enter", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleB),
                detail: "Введите адрес в строку новой вкладки и подтвердите.\n" + url, copyText: url,
                expected: "Ожидаю учебную страницу заметок.");
            Add(goal, "Найдите слово на странице", "Ctrl+F", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "Откройте поле поиска в учебной вкладке.");
            Add(goal, "Найдите на странице " + briefing.Keyword, "Enter", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.FocusedValueIs(briefing.Keyword),
                copyText: briefing.Keyword, expected: "В открытом поле поиска ожидается " + briefing.Keyword + ".");
            Add(goal, "Закройте поле поиска", "Esc", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && !DesktopCourseWindows.FocusedValueIs(briefing.Keyword),
                expected: "Закройте именно поле поиска в учебном Chrome.");
            Add(goal, "Переключитесь на страницу проекта", "Ctrl+Shift+Tab", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA),
                expected: "Должна активироваться вкладка проекта.");
            Add(goal, "Вернитесь к заметкам", "Ctrl+Tab", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleB),
                expected: "Должна активироваться вкладка заметок.");
            Add(goal, "Закройте вкладку заметок", "Ctrl+W", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA),
                expected: "После закрытия заметок должна остаться вкладка проекта.");
            Add(goal, "Восстановите закрытую вкладку", "Ctrl+Shift+T", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleB),
                expected: "Ожидаю восстановленную вкладку заметок.");
            optional = null;
        }

        private void BuildNotepadRevision(string copy, string original)
        {
            const string goal = "Внесите и отмените правку в копии заметки";
            string revised = original + "\nCopy verified";
            OpenRun(goal);
            Add(goal, "Откройте копию заметки в Блокноте", "Enter", commandSource: hwnd => hwnd == runWindow,
                result: hwnd => DesktopCourseWindows.InFileApp(hwnd, "notepad", copy),
                accepted: hwnd => noteWindow = hwnd, detail: "Вставьте команду запуска в диалог «Выполнить».",
                copyText: "notepad.exe " + Quote(copy), expected: "Ожидаю открытие копии заметки.");
            Add(goal, "Перейдите в конец заметки", "Ctrl+End", commandSource: hwnd => InNote(hwnd, copy),
                expected: "Откройте копию заметки в Блокноте.");
            Add(goal, "Допишите строку и сохраните копию", "Ctrl+S", commandSource: hwnd => InNote(hwnd, copy),
                result: hwnd => InNote(hwnd, copy) && SameText(copy, revised),
                detail: "Нажмите Enter и наберите Copy verified.", copyText: "Copy verified",
                expected: "Третья строка должна содержать Copy verified.");
            Add(goal, "Отмените последнюю правку", "Ctrl+Z", commandSource: hwnd => InNote(hwnd, copy),
                expected: "Вызовите отмену в открытой копии.");
            Add(goal, "Сохраните заметку после отмены", "Ctrl+S", commandSource: hwnd => InNote(hwnd, copy),
                result: hwnd => InNote(hwnd, copy) && SameText(copy, original),
                detail: "При необходимости повторите Ctrl+Z, пока третья строка не исчезнет.",
                expected: "Копия должна снова содержать две исходные строки.");
        }

        private void BuildCode(string project, string note, string originalText, string editedText)
        {
            if (code == null) return;
            const string goal = "Внесите правку в заметку проекта через VS Code";
            optional = "VS Code";
            OpenRun(goal);
            string command = Quote(code) + " --user-data-dir " + Quote(Path.Combine(Folder, "Code")) +
                " --extensions-dir " + Quote(Path.Combine(Folder, "CodeExtensions")) +
                " --disable-extensions --new-window " + Quote(project);
            string projectName = Path.GetFileName(project);
            Add(goal, "Откройте учебный проект в VS Code", "Enter",
                commandSource: hwnd => hwnd == runWindow, result: hwnd => DesktopCourseWindows.WindowTitle(hwnd, "Code", projectName),
                accepted: hwnd => editor = hwnd,
                detail: "Вставьте подготовленную команду в «Выполнить». Если появится запрос доверия, разрешите его только для этой учебной папки.",
                copyText: command, expected: "Ожидаю VS Code с открытой учебной папкой " + projectName + ".");
            Add(goal, "Откройте палитру команд VS Code", "Ctrl+Shift+P", commandSource: IsCode,
                result: hwnd => IsCode(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "Ожидаю поле палитры команд в учебном окне.");
            Add(goal, "Закройте палитру команд", "Esc", commandSource: IsCode,
                result: hwnd => IsCode(hwnd) && !DesktopCourseWindows.FocusedValueIs(">"),
                expected: "Закройте поле палитры команд и вернитесь в редактор.");
            Add(goal, "Откройте быстрый поиск файла", "Ctrl+P", commandSource: IsCode,
                result: hwnd => IsCode(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "В заголовке должен быть VS Code учебного проекта.");
            Add(goal, "Откройте заметку из шага с Блокнотом", "Enter", commandSource: IsCode,
                result: hwnd => InCodeDocument(hwnd, note),
                detail: "Введите имя файла и нажмите Enter.\n" + Path.GetFileName(note), copyText: Path.GetFileName(note),
                expected: "Ожидаю вкладку " + Path.GetFileName(note) + ".");
            Add(goal, "Вызовите переход к строке", "Ctrl+G", commandSource: hwnd => InCodeDocument(hwnd, note),
                result: hwnd => InCodeDocument(hwnd, note) && DesktopCourseWindows.HasTextFocus(),
                expected: "Откройте поле перехода к строке из учебной заметки.");
            Add(goal, "Перейдите к строке 2", "Enter", commandSource: hwnd => InCodeDocument(hwnd, note),
                detail: "Введите 2 и нажмите Enter.", copyText: "2",
                expected: "Вернитесь во вкладку учебной заметки.");
            Add(goal, "Выделите строку со статусом", "Shift+End",
                commandSource: hwnd => InCodeDocument(hwnd, note) && SameText(note, originalText),
                expected: "Начните выделение во второй строке учебной заметки.");
            Add(goal, "Замените статус и сохраните файл", "Ctrl+S",
                commandSource: hwnd => InCodeDocument(hwnd, note), result: hwnd => InCodeDocument(hwnd, note) && SameText(note, editedText),
                detail: "Замените выделенную строку на Status: reviewed, затем сохраните файл.", copyText: "Status: reviewed",
                expected: "Первая строка должна сохраниться. Во второй строке ожидается Status: reviewed.");
            optional = null;
        }

        private void BuildSystem()
        {
            const string goal = "Откройте системные инструменты и вернитесь на рабочий стол";
            Add(goal, "Откройте Параметры Windows", "Win+I", result: Native.IsSettings,
                accepted: hwnd => settings = hwnd, expected: "Ожидаю окно Параметров Windows.");
            Add(goal, "Откройте Диспетчер задач", "Ctrl+Shift+Esc", commandSource: hwnd => hwnd == settings,
                result: hwnd => Native.IsProcess(hwnd, "Taskmgr"), accepted: hwnd => taskManager = hwnd,
                expected: "Начните в Параметрах. Должен открыться Диспетчер задач.");
            Add(goal, "Вернитесь в Параметры через переключение окон", "Alt+Tab",
                commandSource: hwnd => hwnd == taskManager, result: hwnd => hwnd == settings && Native.IsSettings(hwnd),
                detail: "Удерживайте Alt и выберите окно Параметров клавишей Tab.",
                expected: "Окно Параметров должно снова стать активным.");
            Add(goal, "Покажите рабочий стол", "Win+D", commandSource: hwnd => hwnd == settings,
                result: DesktopCourseWindows.OnDesktop,
                expected: "Вернитесь в Параметры и покажите рабочий стол.");
            Add(goal, "Откройте меню «Пуск»", "Win", result: DesktopCourseWindows.StartMenu,
                accepted: hwnd => { startMenu = hwnd; watchingStartSearch = true; },
                expected: "Нажмите и отпустите одну клавишу Win. Должен открыться Пуск.");
            Add(goal, "Запустите Блокнот из поиска «Пуск»", "Enter", commandSource: hwnd => hwnd == startMenu,
                result: hwnd => Native.IsProcess(hwnd, "notepad"), accepted: hwnd => watchingStartSearch = false,
                detail: "Наберите Блокнот или Notepad и выберите найденное приложение клавишей Enter.",
                expected: "Ожидаю открытие Блокнота из меню «Пуск».");
        }

        private void Address(string goal, string name, string target, Func<IntPtr, bool> source)
        {
            Add(goal, "Выделите адресную строку Проводника", "Ctrl+L", commandSource: source,
                result: hwnd => source(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "Нужна активная адресная строка учебного Проводника.");
            Add(goal, "Перейдите в " + name, "Enter", commandSource: source, result: hwnd => AtFolder(hwnd, target),
                detail: "Введите полный путь и нажмите Enter.\n" + target, copyText: target,
                expected: "Проводник должен показать указанный путь.");
        }

        private void CreateFolder(string goal, string parent, string path, string name, Action after = null)
        {
            Add(goal, "Создайте папку сочетанием", "Ctrl+Shift+N", commandSource: hwnd => AtFolder(hwnd, parent),
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.HasTextFocus(),
                expected: "Ожидаю поле имени новой папки в учебном Проводнике.");
            Add(goal, "Введите имя новой папки и подтвердите", "Enter", commandSource: IsCurrentExplorer,
                result: hwnd => AtFolder(hwnd, parent) && Directory.Exists(path),
                accepted: hwnd => { if (after != null) after(); }, detail: "Имя папки — " + name, copyText: name,
                expected: "Проверьте имя " + name + " и расположение новой папки.");
        }

        private void OpenRun(string goal)
        {
            Add(goal, "Откройте окно «Выполнить»", "Win+R", result: Native.IsRunDialog, accepted: hwnd => runWindow = hwnd,
                expected: "Должно открыться системное окно «Выполнить».");
        }

        private void Add(string goal, string action, string shortcut, Func<IntPtr, bool> commandSource = null,
            Func<IntPtr, bool> result = null, Action<IntPtr> accepted = null, string detail = "", string copyText = null,
            string expected = "Вернитесь в учебное окно и проверьте результат.")
        {
            steps.Add(new Step
            {
                Goal = goal, Action = action, Shortcut = shortcut, Source = commandSource, Result = result,
                Detail = detail, CopyText = copyText, Expected = expected, Accepted = accepted, Optional = optional
            });
        }

        private void Ready(string message)
        {
            entered = false;
            listeningSince = DateTime.UtcNow;
            Notice = message;
        }

        private bool IsCurrentExplorer(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == explorer && Native.IsExplorer(hwnd); }
        private bool IsChrome(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == browser && Native.IsProcess(hwnd, "chrome"); }
        private bool IsCode(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == editor && Native.IsProcess(hwnd, "Code"); }
        private bool InNote(IntPtr hwnd, string file) { return hwnd == noteWindow && DesktopCourseWindows.InFileApp(hwnd, "notepad", file); }
        private bool InCodeDocument(IntPtr hwnd, string file) { return IsCode(hwnd) && DesktopCourseWindows.InFileApp(hwnd, "Code", file); }
        private bool AtFolder(IntPtr hwnd, string folder) { return IsCurrentExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), folder); }

        private static bool SamePath(string left, string right)
        {
            return left != null && string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Check(Func<IntPtr, bool> condition, IntPtr hwnd)
        {
            if (condition == null) return true;
            try { return condition(hwnd); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.Runtime.InteropServices.COMException) { return false; }
        }

        private static string Quote(string text) { return "\"" + text + "\""; }

        private void PrepareDraft(string project, string path)
        {
            MakeOwnedDirectory(project);
            WriteNew(path, "");
        }

        private void MakeOwnedDirectory(string directory)
        {
            if (!SamePath(Path.GetDirectoryName(directory), Folder) ||
                (File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Учебная папка перемещена.");
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Нужна обычная учебная папка.");
        }

        private static void WriteNew(string path, string content)
        {
            if (File.Exists(path)) return;
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static bool SameText(string path, string text)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n') == text;
        }

        private static string Page(string title, string heading, string text)
        {
            return "<!doctype html><meta charset=utf-8><title>" + title + "</title><main><h1>" +
                heading + "</h1><p>" + text + "</p></main>";
        }
    }
}
