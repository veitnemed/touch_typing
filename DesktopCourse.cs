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
            public string Goal, Action, Shortcut, Detail, CopyText, Expected, ConsoleTitle;
            public string SourceLabel, DestinationLabel, Outcome, ReadCommand, TaskHint;
            public bool ReadOpened;
            public Func<IntPtr, bool> Source, Result;
            public Action<IntPtr> Accepted;
            public bool QuickRetry;
            public string CompletionPrefix, CompletionName;
            public bool SawCompletion;
            public Action Launch, Reopen;
            public DateTime ReopenAfter;
            public string WindowName;
            public Func<IntPtr, bool> BindWindow;
            public bool AcceptNoteDialog;
            public IntPtr NoteDialog;
            public bool CopiesFilePath;
        }

        private readonly string token = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string chrome, code;
        private readonly List<Step> steps = new List<Step>();
        private readonly SortedDictionary<int, Func<bool>> missionEntries = new SortedDictionary<int, Func<bool>>();
        private readonly DesktopTrack track;
        private readonly DesktopItinerary itinerary;
        private int round, index;
        private DateTime listeningSince, commandAt, nextCheck;
        private IntPtr explorer, noteWindow, browser, editor, settings, runWindow, startMenu;
        private bool watchingStartSearch;
        private bool entered;
        private uint clipboardBeforePath;
        private IntPtr switchOrigin, switchDestination;
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
        public string StepSource { get { return Current.SourceLabel; } }
        public string StepDestination { get { return Current.DestinationLabel; } }
        public string StepOutcome { get { return Current.Outcome; } }
        private bool ChoosingPathCommand { get { return entered && Current.CopiesFilePath && Current.Shortcut == "Alt"; } }
        public string Action { get { return ChoosingPathCommand ? "На вкладке «Главная» выберите «Копировать путь»" : Current.Action; } }
        private bool WaitingForTab { get { return Current.CompletionPrefix != null && !Current.SawCompletion; } }
        public string Shortcut { get { return WaitingForTab ? "Tab" : Current.Shortcut; } }
        public string ShortcutHint { get { return ChoosingPathCommand ? "" : Shortcut; } }
        public string Detail
        {
            get
            {
                if (ChoosingPathCommand)
                    return "Нажмите буквы над вкладкой «Главная», затем над «Копировать путь». Можно выбрать вкладку стрелками и Enter, затем команду через Tab и Enter.";
                if (Current.ReadCommand != null && !Current.ReadOpened) return Current.TaskHint;
                if (WaitingForTab)
                    return "Наберите начало команды. Клавиша Tab подставит имя " + Current.CompletionName + ".";
                if (Current.SawCompletion)
                    return "Проверьте, что подставилось " + Current.CompletionName + ", и нажмите Enter. Если требуется, допишите имя вручную.";
                return Current.Detail;
            }
        }
        public string CopyText
        {
            get
            {
                if (Current.ReadCommand != null) return Current.ReadOpened ? null : Current.ReadCommand;
                return WaitingForTab ? Current.CompletionPrefix : Current.CopyText;
            }
        }
        public bool IsConsoleCommand { get { return Current.ConsoleTitle != null; } }
        public bool WaitingForWindow { get { return Current.BindWindow != null; } }
        public bool CanReopenWindow { get { return WaitingForWindow && Current.Reopen != null && DateTime.UtcNow >= Current.ReopenAfter; } }
        public string WindowName { get { return Current.WindowName; } }

        public void ReopenWindow()
        {
            if (CanReopenWindow) Current.Reopen();
        }
        public string Notice { get; private set; }
        public string ProjectTitle { get; private set; }
        public string[] ObservedShortcuts { get {
            if (Current.CopiesFilePath) return new[] { Current.Shortcut, "Esc", "Ctrl+C" };
            return Current.CompletionPrefix == null ? new[] { Current.Shortcut } : new[] { "Tab", Current.Shortcut };
        } }

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
            if (step.BindWindow != null)
            {
                if (!Check(step.BindWindow, input.Window))
                {
                    if (step.WindowName != null)
                        Notice = "Сочетание нажато в другом окне. Перейдите в " + step.WindowName + " и повторите.";
                    return;
                }
                step.BindWindow = null;
            }
            if (WaitingForTab && input.Chord == "Enter")
            {
                Rejected++;
                Notice = "Сначала наберите начало команды и дополните имя клавишей Tab.";
                return;
            }
            if (step.CopiesFilePath && (input.Chord == "Esc" || input.Chord == "Ctrl+C"))
            {
                entered = false;
                Notice = step.Expected;
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
            if (step.ReadCommand != null && !step.ReadOpened)
            {
                step.ReadOpened = true;
                Notice = "Найдите ответ в выводе команды.";
                return;
            }
            switchOrigin = step.Shortcut == "Alt+Tab" ? input.Window : IntPtr.Zero;
            if (step.CopiesFilePath) clipboardBeforePath = input.ClipboardSequence;
            switchDestination = input.SwitchDestination;
            commandAt = switchOrigin == IntPtr.Zero ? DateTime.UtcNow : input.Timestamp;
            nextCheck = DateTime.MinValue;
            entered = true;
            Notice = step.Result == null ? "Сочетание принято в учебном окне." : "Сочетание принято. Проверяю результат.";
        }

        public bool Poll(IntPtr active)
        {
            if (Current.AcceptNoteDialog && DesktopCourseWindows.EditorDialog(active, noteWindow)) Current.NoteDialog = active;
            if (Current.BindWindow != null)
            {
                if (!Check(Current.BindWindow, active)) return false;
                Current.BindWindow = null;
                // Watching the first chord already started when the app opened. Preserve
                // matches queued during window activation without resetting its timestamp.
                Notice = "";
                return false;
            }
            if (watchingStartSearch && DesktopCourseWindows.StartMenu(active)) startMenu = active;
            if (!entered) return false;
            var now = DateTime.UtcNow;
            if (now < nextCheck) return false;
            nextCheck = now.AddMilliseconds(300);
            var step = Current;
            bool switching = step.Shortcut == "Alt+Tab";
            if (step.CopiesFilePath && active != IntPtr.Zero && !DesktopCourseWindows.InExplorerSurface(active, explorer))
            {
                entered = false;
                Notice = step.Expected;
                return false;
            }
            if (switching)
            {
                // If the shell identified the selected window, wait for that exact window
                // to activate. Without an identity only the immediate activation is evidence.
                TimeSpan elapsed = now - commandAt;
                bool selectionExpired = elapsed >= TimeSpan.FromSeconds(1.2) &&
                    (switchDestination == IntPtr.Zero || !Check(step.Result, switchDestination));
                if (selectionExpired || elapsed >= TimeSpan.FromSeconds(20))
                {
                    entered = false;
                    Rejected++;
                    Notice = step.Expected + " Повторите Alt+Tab. Удерживайте Alt, выберите нужное окно и отпустите Alt.";
                    return false;
                }
            }
            bool changedWindow = switchOrigin != IntPtr.Zero && active != IntPtr.Zero && active != switchOrigin &&
                (switchDestination == IntPtr.Zero || active == switchDestination);
            if ((!switching || changedWindow) && Check(step.Result, active))
            {
                try { if (step.Accepted != null) step.Accepted(active); }
                catch (IOException) { Notice = "Учебная папка недоступна. Восстановите сеанс через панель."; abortCheck = true; }
                catch (UnauthorizedAccessException) { Notice = "Нет доступа к учебной папке. Восстановите сеанс через панель."; abortCheck = true; }
                if (abortCheck) { entered = false; abortCheck = false; return false; }
                Completed++;
                index++;
                if (index == steps.Count) BuildNextProject();
                else if (missionEntries.ContainsKey(index) && !missionEntries[index]()) AdvanceToMission();
                Ready("Шаг зачтён.");
                return true;
            }
            if (switching && active != IntPtr.Zero && now - commandAt >= TimeSpan.FromSeconds(1.2))
            {
                Notice = "Ожидаю переключение в выбранное окно. " + step.Expected;
            }
            else if (ChoosingPathCommand)
            {
                // Reading the localized key tips can take longer than a single shortcut.
                Notice = "";
            }
            else if (step.QuickRetry && now - commandAt >= TimeSpan.FromSeconds(2))
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
            var open = Current.Launch;
            Current.Launch = null;
            if (open != null) open();
        }

        // Open only this project's practice window. Bind its verified foreground
        // identity before observing the first shortcut, which can change its title.
        private void OpenForStep(Step step, string name, string executable, string arguments, Func<IntPtr, bool> bind,
            Action<System.Diagnostics.ProcessStartInfo> configure = null)
        {
            step.BindWindow = bind;
            step.WindowName = name;
            step.ReopenAfter = DateTime.MaxValue;
            step.Launch = step.Reopen = delegate
            {
                step.ReopenAfter = DateTime.UtcNow.AddSeconds(8);
                try
                {
                    var start = new System.Diagnostics.ProcessStartInfo(executable, arguments)
                        { UseShellExecute = false, WorkingDirectory = Folder };
                    if (configure != null) configure(start);
                    using (System.Diagnostics.Process.Start(start)) { }
                    Notice = "Открывается " + name + ". Если окно осталось на заднем плане, выберите его через Alt+Tab.";
                }
                catch (Exception ex)
                {
                    if (!(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException) &&
                        !(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                    step.ReopenAfter = DateTime.MinValue;
                    Notice = "Windows не удалось открыть " + name + ". Можно повторить запуск на этом же шаге.";
                }
            };
        }

        public void Dispose()
        {
            pendingLaunch = null;
            if (ownedConsole == null) return;
            ownedConsole.Dispose();
            ownedConsole = null;
        }

        public bool SkipMission()
        {
            AdvanceToMission();
            Ready("Другое задание. Уже выполненные действия сохранены.");
            return true;
        }

        private void AdvanceToMission()
        {
            watchingStartSearch = false;
            foreach (var entry in missionEntries)
                if (entry.Key > index && entry.Value()) { index = entry.Key; return; }
            BuildNextProject();
        }

        private void Mission(Action build, Func<bool> available = null)
        {
            int start = steps.Count;
            build();
            if (steps.Count > start) missionEntries[start] = available ?? (() => true);
        }

        private void BuildNextProject()
        {
            Dispose();
            round++;
            index = 0;
            steps.Clear();
            missionEntries.Clear();
            explorer = noteWindow = browser = editor = settings = runWindow = startMenu = IntPtr.Zero;
            watchingStartSearch = false;
            DesktopBriefing briefing = itinerary.NextBriefing();
            ProjectTitle = briefing.Project;
            string projectName = briefing.Slug + "-" + round.ToString("000") + "-" + token;
            if (track == DesktopTrack.Console)
            {
                string lab = Path.Combine(Folder, "cmd-" + projectName);
                MakeOwnedDirectory(lab);
                var mission = new ConsoleMission(lab, itinerary.NextConsoleScenario(), briefing,
                    itinerary.NextVariant(900) + 100, itinerary.CompletedConsoleMissions);
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
            string browserPath = Path.Combine(Folder, "pages");
            string titleA = projectName + " / Board " + token;
            string titleB = projectName + " / Notes " + token;
            string board = Path.Combine(browserPath, "board-" + round + ".html");
            string brief = Path.Combine(browserPath, "notes-" + round + ".html");

            int order = itinerary.NextWorkdayOrder(chrome != null);
            Action browserLab = () => BuildChrome(browserPath, board, brief, titleA, titleB, briefing);
            string inbox = Path.Combine(Folder, "Inbox-" + round);
            MakeOwnedDirectory(inbox);
            string message = Path.Combine(inbox, "status-" + briefing.Slug + ".txt");
            WriteNew(message, originalText.Replace("\n", "\r\n"));
            Action editLab = () => BuildNotepadRevision(message, originalText);
            Action handoffLab = () => BuildHandoff(inbox, briefing, projectName);
            Mission(() => BuildQuickJob(briefing, itinerary.NextQuickJob()));
            Mission(() => BuildQuickJob(briefing, itinerary.NextQuickJob()));
            if (order == 1) Mission(editLab);
            if (order == 2) Mission(handoffLab);
            if (order == 3) Mission(browserLab);
            Mission(() => BuildProjectFiles(projectName, project, source, note, renamed, copies, originalText));
            if (order != 1) Mission(editLab);
            if (order != 3) Mission(browserLab);
            Mission(() => BuildCode(project, renamed, originalText));
            if (order != 2) Mission(handoffLab);
            Mission(BuildSystem);
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
            Add(notes, "Дайте черновику имя рабочей заметки", "Ctrl+Shift+S",
                commandSource: hwnd => InNote(hwnd, source), result: hwnd => DesktopCourseWindows.EditorDialog(hwnd, noteWindow),
                detail: "Черновик открывается в Блокноте. Сохраните отдельную заметку для передачи.",
                expected: "В учебном Блокноте откройте «Сохранить как».");
            OpenPracticeNote(steps.Last(), source);
            Add(notes, "Сохраните черновик под именем " + Path.GetFileName(note), "Enter",
                result: hwnd => DesktopCourseWindows.InFileApp(hwnd, "notepad", note) && File.Exists(note),
                accepted: hwnd => noteWindow = hwnd,
                detail: "В поле имени нажмите Ctrl+A и введите " + Path.GetFileName(note) + ". Если открылась другая папка, вставьте путь с панели.",
                copyText: note, expected: "Проверьте имя. Если диалог закрылся, снова нажмите Ctrl+Shift+S и укажите путь из задания.",
                sourceLabel: Path.GetFileName(source), destLabel: Path.GetFileName(note),
                outcome: "Заметка появится в папке проекта под новым именем.");
            AcceptDialogOnEnter();
            Add(notes, "Запишите статус проекта и сохраните заметку", "Ctrl+S", commandSource: hwnd => InNote(hwnd, note),
                result: hwnd => InNote(hwnd, note) && SameText(note, originalText),
                detail: "Введите две строки.\n" + originalText, copyText: originalText.Replace("\n", "\r\n"),
                expected: "Исправьте обе строки в открытой заметке и сохраните её через Ctrl+S.",
                outcome: "В папке проекта сохранится статус, который вы передадите на проверку.");

            const string filing = "Разложите файлы проекта";
            Add(filing, "Вернитесь в Проводник, чтобы передать сохранённую заметку", "Alt+Tab",
                result: IsCurrentExplorer,
                detail: "Выберите Проводник с учебной папкой и отпустите Alt. Дальше вы дадите заметке имя для проверки.",
                expected: "Нужно окно Проводника, в котором вы создали папку проекта.");
            EnterExplorerChild(filing, project);
            Add(filing, "Выберите заметку и начните переименование", "F2",
                commandSource: hwnd => AtFolder(hwnd, project) && DesktopCourseWindows.SelectionContains(hwnd, note),
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.HasTextFocus(),
                detail: "Стрелками выделите " + Path.GetFileName(note) + ", затем нажмите F2.",
                expected: "Выделите именно " + Path.GetFileName(note) + " в папке проекта.");
            Add(filing, "Введите новое имя и подтвердите", "Enter", commandSource: IsCurrentExplorer,
                result: hwnd => IsCurrentExplorer(hwnd) && !File.Exists(note) && SameText(renamed, originalText),
                detail: "Введите " + Path.GetFileNameWithoutExtension(renamed) + ". Расширение .txt оставьте прежним.",
                copyText: Path.GetFileNameWithoutExtension(renamed), expected: "Проверьте новое имя и сохранённое расширение .txt.",
                sourceLabel: Path.GetFileName(note), destLabel: Path.GetFileName(renamed),
                outcome: "В этой же папке заметка получит новое имя.");
            CreateFolder(filing, project, copies, "Copies");
            Add(filing, "Скопируйте переименованную заметку", "Ctrl+C",
                commandSource: hwnd => AtFolder(hwnd, project) && DesktopCourseWindows.SelectionContains(hwnd, renamed),
                result: hwnd => IsCurrentExplorer(hwnd) && DesktopCourseWindows.ClipboardContainsOnly(renamed),
                detail: "Стрелками выделите " + Path.GetFileName(renamed) + " и скопируйте файл.",
                expected: "В буфере обмена ожидается только учебная заметка.",
                sourceLabel: Path.GetFileName(renamed), destLabel: "Буфер обмена",
                outcome: "Теперь файл можно вставить в папку Copies.");
            // Type-to-select leaves the copied file in the clipboard.
            EnterExplorerChild(filing, copies);
            Add(filing, "Вставьте копию заметки", "Ctrl+V",
                commandSource: hwnd => AtFolder(hwnd, copies) && DesktopCourseWindows.ClipboardContainsOnly(renamed),
                result: hwnd => AtFolder(hwnd, copies) && SameText(Path.Combine(copies, Path.GetFileName(renamed)), originalText),
                expected: "В подпапке Copies должна появиться копия заметки.",
                sourceLabel: Path.GetFileName(renamed), destLabel: Path.Combine("Copies", Path.GetFileName(renamed)),
                outcome: "У заметки появится копия с таким же содержимым.");
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
                var step = steps[steps.Count - 1];
                step.ConsoleTitle = title;
                step.QuickRetry = current.QuickRetry;
                step.CompletionPrefix = current.CompletionPrefix;
                step.CompletionName = current.CompletionName;
                step.SourceLabel = current.SourceLabel;
                step.DestinationLabel = current.DestinationLabel;
                step.Outcome = current.Outcome;
                step.ReadCommand = current.ReadCommand;
                step.TaskHint = current.TaskHint;
            }
            Add(goal, "Покажите готовый файл " + mission.Delivery + " в Проводнике", "Enter", commandSource: inConsole,
                result: hwnd => Native.IsExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), Path.GetDirectoryName(target)) &&
                    DesktopCourseWindows.SelectionContains(hwnd, target),
                detail: "Параметр /select открывает папку и выделяет готовый файл. %cd% подставляет путь текущей папки.",
                copyText: "explorer /select,\"%cd%\\handoff\\" + mission.Delivery + "\"",
                expected: "В Проводнике должен быть выделен " + mission.Delivery + ".");
            steps[steps.Count - 1].ConsoleTitle = title;
            steps[steps.Count - 1].SourceLabel = @"handoff\" + mission.Delivery;
            steps[steps.Count - 1].DestinationLabel = "Проводник · handoff";
            steps[steps.Count - 1].Outcome = "Откроется папка, а готовый файл будет выделен.";
            Add(goal, "Вернитесь из Проводника к командной строке", "Alt+Tab", result: inConsole,
                detail: "Удерживайте Alt, перебирайте окна клавишей Tab. Когда выделится " + title + ", отпустите Alt.",
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
            if (itinerary.NextBrowserScenario() == 1)
            {
                BuildChromeDirectory(board, brief, titleA, titleB, briefing);
                return;
            }
            int caseNumber = 1000 + itinerary.NextVariant(9000);
            WriteNew(board, Page(titleA, briefing, caseNumber, false));
            WriteNew(brief, Page(titleB, briefing, caseNumber, true));
            string goal = "Передайте команде " + briefing.Team + " номер записи с пометкой " + briefing.Keyword;
            Func<IntPtr, bool> atBoard = hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA);
            Func<IntPtr, bool> atJournal = hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleB);
            Add(goal, "Оставьте карточку открытой и заведите вкладку для заметок", "Ctrl+T", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && !atBoard(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, true),
                detail: "Первая вкладка понадобится для сверки. Новая откроется рядом с ней.",
                expected: "В учебном окне должна открыться новая вкладка с активной адресной строкой.");
            OpenPracticeChrome(board, titleA);
            string url = new Uri(brief).AbsoluteUri;
            Add(goal, "Откройте журнал во второй вкладке", "Enter", commandSource: IsChrome,
                result: atJournal,
                detail: "Ctrl+Alt+C скопирует адрес журнала. Вставьте его через Ctrl+V. Чтобы снова выбрать адресную строку, нажмите Ctrl+L.", copyText: url,
                sourceLabel: "Адрес заметок", destLabel: "Новая вкладка Chrome", outcome: "Рядом с карточкой проекта откроется журнал передачи.",
                expected: "Ожидаю учебную страницу заметок.");
            Add(goal, "Откройте поиск в журнале", "Ctrl+F", commandSource: IsChrome,
                result: hwnd => atJournal(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, false),
                expected: "Откройте поле поиска в учебной вкладке.");
            Add(goal, "Найдите пометку " + briefing.Keyword + " и прочитайте номер рядом", "Enter", commandSource: IsChrome,
                result: hwnd => atJournal(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, false, briefing.Keyword),
                detail: "Введите пометку в поле поиска. В найденной записи четыре цифры — они понадобятся в карточке.",
                copyText: briefing.Keyword, expected: "Нажмите Ctrl+F в журнале и найдите " + briefing.Keyword + ".");
            Add(goal, "Запомните номер или запишите его и закройте поиск", "Esc", commandSource: IsChrome,
                result: hwnd => atJournal(hwnd) && DesktopCourseWindows.ChromePageFocused(hwnd, titleB),
                expected: "Закройте именно поле поиска в учебном Chrome.");
            bool followUp = caseNumber % 3 == 0;
            string returnKey = followUp ? "Ctrl+W" : caseNumber % 2 == 0 ? "Ctrl+Tab" : "Ctrl+Shift+Tab";
            Add(goal, followUp ? "Закройте прочитанный журнал и передайте номер в карточке" :
                "Вернитесь к карточке, чтобы передать номер", returnKey, commandSource: IsChrome,
                result: atBoard,
                expected: "Должна активироваться вкладка проекта.");
            Add(goal, "Введите найденный номер в карточку и подтвердите его", "Enter", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA + SubmittedSuffix),
                detail: followUp ? "Введите четыре цифры. Чтобы подсмотреть снова, верните журнал через Ctrl+Shift+T и закройте через Ctrl+W." :
                    "Поле принимает четыре цифры. Если нужно свериться, Ctrl+Tab откроет журнал, Ctrl+Shift+Tab вернёт к карточке.",
                sourceLabel: "Строка " + briefing.Keyword + " в журнале", destLabel: "Поле «Номер записи»",
                outcome: "В карточке появится подтверждение передачи команде " + briefing.Team + ".",
                expected: "В карточке введите номер из строки " + briefing.Keyword + " и нажмите Enter. Если поле вне фокуса, выберите его клавишей Tab.");
            if (followUp)
            {
                goal = "Дополните подтверждённую карточку временем записи " + briefing.Keyword;
                Add(goal, "Команда уточняет время записи. Восстановите закрытый журнал", "Ctrl+Shift+T",
                    commandSource: IsChrome, result: atJournal, detail: "Откроется недавно закрытая вкладка. Найдите время рядом с " + briefing.Keyword + ".",
                    expected: "В учебном Chrome верните вкладку с журналом.");
                Add(goal, "Прочитайте время нужной строки и вернитесь к карточке", "Ctrl+Shift+Tab",
                    commandSource: IsChrome, result: atBoard, expected: "В карточке открылось поле для времени.");
                Add(goal, "Укажите время из журнала и отправьте уточнение", "Enter", commandSource: IsChrome,
                    result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", titleA + TimeConfirmedSuffix),
                    detail: "Запишите часы и минуты через двоеточие. Клавиша Tab выбирает следующее поле.",
                    sourceLabel: "Время в строке " + briefing.Keyword, destLabel: "Карточка команды " + briefing.Team,
                    outcome: "У команды будут и номер, и время записи.", expected: "Введите время из той же строки и нажмите Enter.");
            }
        }

        private void BuildNotepadRevision(string copy, string original)
        {
            const string goal = "Обновите статус во входящей сводке";
            Add(goal, "Перейдите к строке со статусом", "Ctrl+End", commandSource: hwnd => InNote(hwnd, copy),
                detail: "Команда закончила проверку. В последней строке ещё указано Status: ready.",
                expected: "Откройте учебную заметку в Блокноте.");
            OpenPracticeNote(steps.Last(), copy);
            Add(goal, "Выделите текущее значение ready", "Ctrl+Shift+Left", commandSource: hwnd => InNote(hwnd, copy),
                detail: "Из конца слова Ctrl+Shift+← выделит ready. Название поля останется на месте.",
                expected: "Нажмите Ctrl+End, затем Ctrl+Shift+←.");
            Add(goal, "Замените ready на checked и сохраните", "Ctrl+S", commandSource: hwnd => InNote(hwnd, copy),
                result: hwnd => InNote(hwnd, copy) && SameText(copy, original.Replace("Status: ready", "Status: checked")),
                detail: "Наберите checked поверх выделенного слова.", copyText: "checked",
                expected: "Проверьте вторую строку. В ней должно быть Status: checked. Затем сохраните через Ctrl+S.",
                outcome: "В файле сводки останется статус выполненной проверки.");
        }

        private void BuildCode(string project, string note, string originalText)
        {
            if (code == null) return;
            if (itinerary.NextEditorScenario() == 1)
            {
                BuildCodeRename(project);
                return;
            }
            const string goal = "Внесите правку в заметку проекта через VS Code";
            string projectName = Path.GetFileName(project);
            string status = Path.Combine(Folder, ".editor-" + projectName);
            string editedText = null;
            Step openNote = null;
            Func<IntPtr, bool> inNote = hwnd => InCodeDocument(hwnd, note) && WorkdayEditorState(status, note, null);
            Add(goal, "Найдите сохранённую заметку по имени", "Ctrl+P", commandSource: IsCode,
                result: hwnd => IsCode(hwnd) && DesktopCourseWindows.CodeQuickInputFocused(hwnd),
                detail: "Быстрый поиск открывает файл в проекте, даже если вы забыли, в какой он папке.",
                expected: "Дождитесь загрузки учебного проекта в VS Code и откройте быстрый поиск клавишами Ctrl+P.");
            var findNote = steps.Last();
            OpenCodeForStep(findNote, () => project, delegate
            {
                // Choose the handoff at entry, after earlier Explorer actions or skips.
                // Retrying the launch must retain the chosen file and any edits.
                if (editedText == null)
                {
                    string target = PrepareCodeNote(note, originalText, out editedText);
                    if (!SamePath(target, note))
                    {
                        note = target;
                        project = Path.GetDirectoryName(note);
                        projectName = Path.GetFileName(project);
                        status = Path.Combine(Folder, ".editor-" + projectName);
                        findNote.Detail = "Для этого задания подготовлена отдельная заметка со статусом проекта. Откройте быстрый поиск файлов.";
                        openNote.Action = "Откройте подготовленную заметку со статусом";
                        openNote.Expected = "Нажмите Ctrl+P и откройте подготовленную заметку. Ctrl+Alt+C скопирует её полный путь.";
                        openNote.CopyText = note;
                        openNote.SourceLabel = projectName;
                    }
                }
            });
            Add(goal, "Откройте заметку из шага с Блокнотом", "Enter", commandSource: IsCode,
                result: inNote,
                detail: "Введите " + Path.GetFileName(note) + ". Стрелками выберите файл в корне проекта и нажмите Enter.",
                copyText: note, sourceLabel: projectName, destLabel: Path.GetFileName(note),
                expected: "В Copies лежит копия. Нажмите Ctrl+P и откройте исходную заметку. Ctrl+Alt+C скопирует её полный путь.");
            openNote = steps.Last();
            Add(goal, "Вызовите переход к строке", "Ctrl+G", commandSource: inNote,
                result: hwnd => inNote(hwnd) && DesktopCourseWindows.CodeQuickInputFocused(hwnd),
                expected: "Откройте поле перехода к строке из учебной заметки.");
            Add(goal, "Перейдите к строке 2", "Enter", commandSource: inNote,
                result: hwnd => InCodeDocument(hwnd, note) && WorkdayEditorState(status, note, "start") &&
                    !DesktopCourseWindows.CodeQuickInputFocused(hwnd),
                detail: "Введите 2 и нажмите Enter. Курсор окажется перед Status.", copyText: "2",
                expected: "Нажмите Ctrl+G, введите 2 и подтвердите через Enter. Курсор нужен в начале второй строки.");
            Add(goal, "Выделите строку со статусом", "Shift+End",
                commandSource: inNote,
                result: hwnd => InCodeDocument(hwnd, note) && WorkdayEditorState(status, note, "selected"),
                expected: "Вернитесь на строку 2 через Ctrl+G, нажмите Home и выделите весь статус через Shift+End.");
            Add(goal, "Замените статус и сохраните файл", "Ctrl+S",
                commandSource: inNote, result: hwnd => inNote(hwnd) && SameText(note, editedText),
                detail: "Замените выделенную строку на Status: reviewed, затем сохраните файл.", copyText: "Status: reviewed",
                expected: "Должны получиться две строки. Если затронули первую, отмените правку через Ctrl+Z и повторите выделение.");
        }

        private void OpenCodeForStep(Step entry, Func<string> workspace, Action prepare)
        {
            string profile = Path.Combine(Folder, "Code");
            string extension = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "code-lesson");
            OpenForStep(entry, "проект в VS Code", code, "", hwnd =>
            {
                if (!DesktopCourseWindows.WindowTitle(hwnd, "Code", Path.GetFileName(workspace()))) return false;
                editor = hwnd;
                return true;
            }, start =>
            {
                if (!File.Exists(Path.Combine(extension, "lesson.js")) || !File.Exists(Path.Combine(extension, "package.json")))
                    throw new IOException("Для проверки редактора распакуйте папку code-lesson рядом с TouchTyping.exe.");
                prepare();
                start.Arguments = "--user-data-dir " + Quote(profile) +
                    " --extensions-dir " + Quote(Path.Combine(Folder, "CodeExtensions")) +
                    " --extensionDevelopmentPath=" + Quote(extension) +
                    " --skip-welcome --force-renderer-accessibility --new-window " + Quote(workspace());
                string user = Path.Combine(profile, "User");
                Directory.CreateDirectory(user);
                WriteNew(Path.Combine(user, "settings.json"),
                    "{\"workbench.startupEditor\":\"none\",\"security.workspace.trust.startupPrompt\":\"never\",\"files.autoSave\":\"off\"}");
                start.EnvironmentVariables["TOUCH_TYPING_WORKDAY_ROOT"] = Folder;
            });
        }

        private void BuildCodeRename(string project)
        {
            const string goal = "Переименуйте переменную Python в двух местах через VS Code";
            string lab = Path.Combine(Folder, "editor-" + Path.GetFileName(project));
            string file = Path.Combine(lab, "summary.py"), status = Path.Combine(Folder, ".editor-" + Path.GetFileName(lab));
            string original = "count = " + (round + 2) + "\nprint(count)";
            Func<IntPtr, bool> inFile = w => InCodeDocument(w, file) && WorkdayEditorState(status, file, null);
            Add(goal, "Найдите скрипт сводки проекта", "Ctrl+P", commandSource: IsCode,
                result: w => IsCode(w) && DesktopCourseWindows.CodeQuickInputFocused(w),
                detail: "В сводке имя count встречается при присваивании и при выводе. Вы замените оба вхождения одним вводом.");
            OpenCodeForStep(steps.Last(), () => lab, delegate
            {
                MakeOwnedDirectory(lab);
                WriteNew(file, original.Replace("\n", "\r\n"));
            });
            Add(goal, "Откройте summary.py", "Enter", commandSource: IsCode, result: inFile,
                detail: "Введите summary.py в быстром поиске и нажмите Enter.", copyText: file,
                expected: "Нажмите Ctrl+P и откройте summary.py из учебного проекта.");
            Add(goal, "Перейдите к объявлению переменной", "Ctrl+Home", commandSource: inFile,
                result: w => InCodeDocument(w, file) && WorkdayEditorState(status, file, "variable-start"),
                detail: "Курсор окажется перед первым словом count.", expected: "Вернитесь во вкладку summary.py и нажмите Ctrl+Home.");
            Add(goal, "Выделите первое слово count", "Ctrl+D", commandSource: inFile,
                result: w => InCodeDocument(w, file) && WorkdayEditorState(status, file, "variable-one"),
                expected: "Нажмите Esc, затем Ctrl+Home и Ctrl+D. Должно выделиться первое слово count.");
            Add(goal, "Добавьте остальные вхождения в выделение", "Ctrl+Shift+L", commandSource: inFile,
                result: w => InCodeDocument(w, file) && WorkdayEditorState(status, file, "variable-both"),
                expected: "Выделите первое слово count через Ctrl+Home и Ctrl+D, затем нажмите Ctrl+Shift+L.");
            Add(goal, "Переименуйте оба вхождения и сохраните скрипт", "Ctrl+S", commandSource: inFile,
                result: w => inFile(w) && SameText(file, original.Replace("count", "total")),
                detail: "Наберите total один раз. Проверьте присваивание и print(total), затем сохраните.", copyText: "total",
                expected: "Новое имя total должно оказаться в обеих строках. При ошибке отмените правку через Ctrl+Z.");
        }

        private string PrepareCodeNote(string original, string template, out string reviewed)
        {
            string first = null;
            try
            {
                var info = new FileInfo(original);
                if (info.Exists && info.Length <= 4096 && (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    (info.Directory.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    string[] lines = File.ReadAllLines(original);
                    if (lines.Length == 2 && !string.IsNullOrWhiteSpace(lines[0]) && lines[1] == "Status: ready")
                        first = lines[0];
                }
            }
            catch (IOException) { } // The previous app may still have the handoff locked.
            catch (UnauthorizedAccessException) { }
            if (first != null)
            {
                reviewed = first + "\nStatus: reviewed";
                return original;
            }
            string separate = Path.Combine(Folder, "editor-" + Path.GetFileName(Path.GetDirectoryName(original)));
            MakeOwnedDirectory(separate);
            string note = Path.Combine(separate, Path.GetFileName(original));
            WriteNew(note, template.Replace("\n", "\r\n"));
            reviewed = template.Split('\n')[0] + "\nStatus: reviewed";
            return note;
        }

        private static bool WorkdayEditorState(string status, string note, string state)
        {
            if (!File.Exists(status)) return false;
            using (var file = new FileStream(status, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(file))
            {
                string name = reader.ReadLine(), reported = reader.ReadLine();
                return string.Equals(name, Path.GetFileName(note), StringComparison.OrdinalIgnoreCase) &&
                    reported != null && (state == null || reported == state);
            }
        }

        private void BuildSystem()
        {
            const string goal = "Откройте системные инструменты и вернитесь на рабочий стол";
            Add(goal, "Откройте Параметры Windows", "Win+I", result: Native.IsSettings,
                accepted: hwnd => settings = hwnd, expected: "Ожидаю окно Параметров Windows.");
            Add(goal, "Откройте Диспетчер задач", "Ctrl+Shift+Esc", commandSource: hwnd => hwnd == settings,
                result: hwnd => Native.IsProcess(hwnd, "Taskmgr"),
                expected: "Начните в Параметрах. Должен открыться Диспетчер задач.");
            Add(goal, "Вернитесь в Параметры через переключение окон", "Alt+Tab",
                result: hwnd => Native.IsSettings(hwnd), accepted: hwnd => settings = hwnd,
                detail: "Удерживайте Alt, выберите «Параметры» клавишей Tab и отпустите Alt. Shift+Tab листает окна назад.",
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
            Add(goal, "Перейдите в " + name, "Enter", commandSource: source,
                result: hwnd => Native.IsExplorer(hwnd) && SamePath(Native.ExplorerFolder(hwnd), target),
                accepted: hwnd => explorer = hwnd,
                detail: "Вставьте путь в выделенную адресную строку и нажмите Enter.", copyText: target,
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
            string expected = "Вернитесь в учебное окно и проверьте результат.",
            string sourceLabel = null, string destLabel = null, string outcome = null)
        {
            steps.Add(new Step
            {
                Goal = goal, Action = action, Shortcut = shortcut, Source = commandSource, Result = result,
                Detail = detail, CopyText = copyText, Expected = expected, Accepted = accepted,
                SourceLabel = sourceLabel, DestinationLabel = destLabel, Outcome = outcome
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

        private void OpenPracticeChrome(string file, string title)
        {
            string arguments = "--user-data-dir=" + Quote(Path.Combine(Folder, "Chrome")) +
                " --no-first-run --no-default-browser-check --force-renderer-accessibility --new-window " + Quote(new Uri(file).AbsoluteUri);
            OpenForStep(steps.Last(), "учебное окно Chrome", chrome, arguments, delegate(IntPtr hwnd)
            {
                if (!DesktopCourseWindows.WindowTitle(hwnd, "chrome", title)) return false;
                browser = hwnd;
                return true;
            });
        }

        private const string LinkFocusSuffix = " / Resource link";

        private void BuildChromeDirectory(string catalog, string document, string catalogTitle, string docTitle, DesktopBriefing briefing)
        {
            string label = "Инструкция для " + briefing.Team;
            string goal = "Подготовьте ссылку на инструкцию для команды " + briefing.Team;
            string anchor = "<a id=resource href='" + Uri.EscapeDataString(Path.GetFileName(document)) + "'>" + label + "</a>";
            WriteNew(catalog, ChromePageShell(catalogTitle, "Каталог документов", briefing.Project,
                "<p>Нужна ссылка на документ «" + label + "».</p><section><ul><li><a href='#contacts'>Контакты команды</a></li><li>" +
                anchor + "</li></ul></section><p id=contacts>Команда проекта · " + briefing.Team + "</p>" +
                "<script>const link=document.getElementById('resource'),base=document.title;" +
                "function mark(){document.title=document.activeElement===link?base+'" + LinkFocusSuffix + "':base;}" +
                "link.onfocus=mark;link.onblur=()=>document.title=base;document.addEventListener('keyup',e=>{if(e.key==='Tab')mark();});</script>"));
            WriteNew(document, ChromePageShell(docTitle, "Рабочая инструкция", label,
                "<section><strong>" + briefing.Project + "</strong><p>Перед передачей материалов проверьте " + briefing.Topic +
                ". Укажите ссылку на эту инструкцию в сообщении команде.</p></section>"));
            Func<IntPtr, bool> atDocument = hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", docTitle);
            Add(goal, "Выберите ссылку «" + label + "»", "Tab", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", catalogTitle + LinkFocusSuffix),
                detail: "Перебирайте ссылки клавишей Tab. Shift+Tab вернёт на одну назад.", expected: "Оставьте рамку фокуса на нужной инструкции.");
            OpenPracticeChrome(catalog, catalogTitle);
            Add(goal, "Откройте выбранную инструкцию", "Enter", commandSource: IsChrome, result: atDocument,
                expected: "Если открыли другой документ, вернитесь через Alt+← и выберите инструкцию команды.");
            Add(goal, "Выделите адрес открытой инструкции", "Alt+D", commandSource: IsChrome,
                result: hwnd => atDocument(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, true),
                expected: "Нажмите Alt+D в учебном документе, чтобы выбрать его адрес.");
            Add(goal, "Скопируйте ссылку для команды", "Ctrl+C", commandSource: IsChrome,
                result: hwnd => atDocument(hwnd) && ClipboardHasFileAddress(document),
                expected: "Нажмите Alt+D, затем Ctrl+C. В буфере должен быть адрес открытой инструкции.");
            Add(goal, "Вернитесь в каталог с готовой ссылкой", "Alt+Left", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.WindowTitle(hwnd, "chrome", catalogTitle) && ClipboardHasFileAddress(document),
                expected: "Вернитесь кнопками Alt+← к каталогу. Если ссылка потерялась, снова откройте документ и скопируйте адрес.",
                sourceLabel: label, destLabel: "Буфер обмена", outcome: "Ссылку можно вставить в сообщение команде.");
        }

        internal static bool IsFileAddress(string text, string document)
        {
            Uri address;
            return !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(text.Trim(), UriKind.Absolute, out address) && address.IsFile &&
                string.IsNullOrEmpty(address.Fragment) && string.IsNullOrEmpty(address.Query) &&
                string.Equals(address.LocalPath, document, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ClipboardHasFileAddress(string document)
        {
            try { return System.Windows.Clipboard.ContainsText() && IsFileAddress(System.Windows.Clipboard.GetText(), document); }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }
        }

        private const string SubmittedSuffix = " / Submitted", TimeConfirmedSuffix = " / Time confirmed";
        private const int JournalRows = 15;

        private static string JournalTime(int row)
        {
            return TimeSpan.FromMinutes(8 * 60 + 29 * row).ToString(@"hh\:mm");
        }

        private static string ChromePageShell(string title, string section, string heading, string content)
        {
            const string style = "<style>*{box-sizing:border-box}body{margin:0;background:#20232a;color:#f2ede6;font:18px/1.55 'Segoe UI',sans-serif}" +
                "main{max-width:760px;margin:7vh auto;padding:0 32px 180px}header{color:#bcc7c2;border-bottom:1px solid #535a61;padding-bottom:16px}" +
                "h1{font-size:clamp(30px,5vw,49px);line-height:1.12;margin:30px 0 12px}section{margin:22px 0;padding:20px 24px;background:#30353d;border-radius:12px}" +
                "p{margin:5px 0}code{color:#b9e3ce;font:1em Consolas,monospace}li{padding:8px 0}strong{color:#f6dfaf}" +
                "label{display:block;margin:0 0 8px}input,button{padding:9px 14px;font:inherit;border:1px solid #848d98;border-radius:6px}" +
                "input{width:9ch;background:#20232a;color:white}button{background:#c4e3d1;color:#192621}input:focus-visible,button:focus-visible{outline:3px solid #aad9ff;outline-offset:3px}</style>";
            return "<!doctype html><html lang=ru><meta charset=utf-8><meta name=viewport content='width=device-width, initial-scale=1'><title>" +
                System.Net.WebUtility.HtmlEncode(title) + "</title>" + style +
                "<style>a{color:#b5d8fa}a:focus-visible{outline:3px solid #eac17a;outline-offset:3px}</style><main><header>" +
                section + "</header><h1>" + heading + "</h1>" + content + "</main></html>";
        }

        internal static string Page(string title, DesktopBriefing briefing, int caseNumber, bool notes)
        {
            string content;
            if (notes)
            {
                string[] statuses = { "Поступило", "На сверке", "Передано", "Закрыто" };
                var rows = new StringBuilder();
                for (int i = 0; i < JournalRows; i++)
                {
                    bool selected = i == caseNumber % JournalRows;
                    int number = selected ? caseNumber : 1000 + (caseNumber - 1000 + 17 + i) % 9000;
                    string label = selected ? briefing.Keyword : statuses[i % statuses.Length];
                    rows.Append("<li>").Append(JournalTime(i)).Append(" · <code>").Append(label)
                        .Append("</code> · Запись <strong>").Append(number).Append("</strong></li>");
                }
                content = "<p>Найдите пометку из карточки и возьмите номер той же строки.</p><section><ul>" + rows + "</ul></section>";
            }
            else
            {
                content = "<p>Команде <strong>" + briefing.Team + "</strong> нужен номер записи с пометкой <code>" +
                    briefing.Keyword + "</code>. Найдите его в журнале и внесите в карточку.</p>" +
                    "<section><form><label for=number>Номер записи</label><input id=number autocomplete=off inputmode=numeric autofocus> <button>Передать</button>" +
                    "<p id=receipt role=status aria-live=polite></p></form></section>" +
                    "<script>const field=document.getElementById('number'),receipt=document.getElementById('receipt'),initial=document.title;" +
                    "field.form.addEventListener('submit',event=>{event.preventDefault();const ok=field.value.trim()==='" + caseNumber + "';" +
                    "document.title=ok?initial+'" + SubmittedSuffix + "':initial;" +
                    "receipt.textContent=ok?'Номер '+field.value.trim()+' передан команде " + briefing.Team + ".':" +
                    "'Сверьте четыре цифры в строке " + briefing.Keyword + " и повторите.';if(!ok){field.focus();field.select();}});" +
                    "field.addEventListener('input',()=>{document.title=initial;receipt.textContent='';});</script>";
                if (caseNumber % 3 == 0)
                {
                    content += "<section id=followup hidden><form><label for=time>Команда уточняет время записи · чч:мм</label>" +
                        "<input id=time autocomplete=off> <button>Отправить</button><p id=reply role=status></p></form></section>" +
                        "<script>const followup=document.getElementById('followup'),time=document.getElementById('time'),reply=document.getElementById('reply');" +
                        "field.form.addEventListener('submit',()=>{followup.hidden=document.title!==initial+'" + SubmittedSuffix + "';if(!followup.hidden)time.focus();});" +
                        "field.addEventListener('input',()=>{followup.hidden=true;time.value='';reply.textContent='';});" +
                        "time.form.addEventListener('submit',event=>{event.preventDefault();const ok=time.value.trim().padStart(5,'0')==='" +
                        JournalTime(caseNumber % JournalRows) + "';document.title=initial+(ok?'" + TimeConfirmedSuffix + "':'" + SubmittedSuffix + "');" +
                        "reply.textContent=ok?'Время подтверждено.':'Сверьте время в той же строке журнала.';if(!ok){time.focus();time.select();}});" +
                        "time.addEventListener('input',()=>{document.title=initial+'" + SubmittedSuffix + "';reply.textContent='';});</script>";
                }
            }
            return ChromePageShell(title, notes ? "Заметки команды" : "Карточка проекта", briefing.Project, content);
        }
    }
}
