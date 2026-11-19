using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TouchTypingStudio
{
    internal enum DesktopTrack { Workday, Console }
    internal enum WorkdayLevel { Intro, Standard, Advanced }

    internal sealed partial class DesktopCourse : IDisposable
    {
        private sealed class Step
        {
            public string Goal, Action, Shortcut, Detail, CopyText, Expected, ConsoleTitle;
            public string SourceLabel, DestinationLabel, Outcome, ReadCommand, TaskHint;
            public bool ReadOpened;
            public Func<IntPtr, bool> Source, Result, ObserveBefore, SwitchTarget;
            public string[] CancelKeys;
            public Action<IntPtr> Accepted;
            public bool QuickRetry;
            public string CompletionPrefix, CompletionName;
            public bool SawCompletion;
            public Action Launch, Reopen;
            public DateTime ReopenAfter, RetireUntil;
            public string WindowName;
            public Func<IntPtr, bool> BindWindow;
            public bool AcceptNoteDialog;
            public IntPtr NoteDialog;
            public bool CopiesFilePath;
            public bool FreshCopy, RequiresExcerptCopy;
            public bool TerminalHandoff;
            public string[] Alternatives;
            public string[] EquivalentKeys;
            public Process Process;
            public IntPtr OwnedWindow;
            public Func<IntPtr, bool> OwnedIdentity;
            public bool CloseChrome;
            public string BeforeTitle;
        }

        private const string FileLabGoal = "Перенесите блок между файлами через дерево VS Code";
        private readonly string token = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string chrome, code;
        private readonly List<Step> steps = new List<Step>();
        private readonly SortedDictionary<int, Func<bool>> missionEntries = new SortedDictionary<int, Func<bool>>();
        private readonly DesktopTrack track;
        private readonly WorkdayLevel workdayLevel;
        private readonly DesktopItinerary itinerary;
        private int round, index;
        private DateTime listeningSince, commandAt, nextCheck;
        private IntPtr explorer, noteWindow, browser, editor, settings, runWindow, startMenu;
        private bool watchingStartSearch;
        private bool entered, observedSource;
        private uint clipboardBeforePath, excerptCopySequence;
        private IntPtr commandWindow;
        private readonly List<Step> launched = new List<Step>();
        private readonly List<Step> retiring = new List<Step>();
        private string WindowMarker { get { return "TouchTypingStudio." + token; } }
        private IntPtr switchOrigin, switchDestination;
        private bool abortCheck, repeatFileLab, lastFileLayout, markedTerminal;
        private bool? requestedTerminalRetry;
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
        public bool Independent { get; set; }

        private IEnumerable<string> ShortcutAlternatives
        {
            get
            {
                var equivalent = Current.EquivalentKeys ?? new string[0];
                return Independent ? equivalent.Concat(Current.Alternatives ?? new string[0]) : equivalent;
            }
        }
        public IntPtr ObservedWindow { get { return Current.BeforeTitle != null || Current.CloseChrome ? browser : IntPtr.Zero; } }
        public string ObservedWindowMarker { get { return Current.CloseChrome ? WindowMarker : null; } }
        public string[] ObservedShortcuts { get {
            if (Current.CopiesFilePath) return new[] { Current.Shortcut, "Esc", "Ctrl+C" };
            return Current.CompletionPrefix == null ? new[] { Current.Shortcut }.Concat(ShortcutAlternatives).Concat(Current.CancelKeys ?? new string[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : new[] { "Tab", Current.Shortcut };
        } }

        public DesktopCourse(string profileRoot, DesktopTrack track = DesktopTrack.Workday, WorkdayLevel level = WorkdayLevel.Standard)
        {
            this.track = track;
            workdayLevel = level;
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
            if (step.CancelKeys != null && step.CancelKeys.Contains(input.Chord, StringComparer.OrdinalIgnoreCase))
            {
                entered = false;
                observedSource = false;
                Notice = step.Expected;
                return;
            }
            if (WaitingForTab && input.Chord == "Enter")
            {
                RecordRejection();
                Notice = "Сначала наберите начало команды и дополните имя клавишей Tab.";
                return;
            }
            if (step.CopiesFilePath && (input.Chord == "Esc" || input.Chord == "Ctrl+C"))
            {
                entered = false;
                Notice = step.Expected;
                return;
            }
            if (!MatchesShortcut(input.Chord, Shortcut, ShortcutAlternatives)) return;
            if ((step.RequiresExcerptCopy && (excerptCopySequence == 0 || input.ClipboardSequence != excerptCopySequence)) ||
                (step.ObserveBefore != null && !observedSource) ||
                (step.CloseChrome && !IsPracticeChromeClose(input)) ||
                (!step.CloseChrome && !Check(step.Source, input.Window)) ||
                (step.BeforeTitle != null && !DesktopCourseWindows.IsChromeCaption(input.WindowTitle, step.BeforeTitle)))
            {
                entered = false;
                RecordRejection();
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
            switchOrigin = input.Chord == "Alt+Tab" || input.Chord == "Alt+Esc" ? input.Window : IntPtr.Zero;
            commandWindow = input.Window;
            if (step.CopiesFilePath || step.FreshCopy) clipboardBeforePath = input.ClipboardSequence;
            switchDestination = input.SwitchDestination;
            commandAt = switchOrigin == IntPtr.Zero ? DateTime.UtcNow : input.Timestamp;
            nextCheck = DateTime.MinValue;
            entered = true;
            Notice = step.Result == null ? "Сочетание принято в учебном окне." : "Сочетание принято. Проверяю результат.";
        }

        public bool Poll(IntPtr active)
        {
            if (retiring.Count != 0) DrainRetiringWindows(false);
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
            if (!entered)
            {
                if (Current.ObserveBefore != null) observedSource = Check(Current.ObserveBefore, active);
                return false;
            }
            var now = DateTime.UtcNow;
            if (now < nextCheck) return false;
            nextCheck = now.AddMilliseconds(300);
            var step = Current;
            bool switching = switchOrigin != IntPtr.Zero;
            if (step.FreshCopy && active != IntPtr.Zero && active != commandWindow)
            {
                entered = false;
                Notice = step.Expected;
                return false;
            }
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
                    (switchDestination == IntPtr.Zero || !Check(step.SwitchTarget ?? step.Result, switchDestination));
                if (selectionExpired || elapsed >= TimeSpan.FromSeconds(20))
                {
                    entered = false;
                    RecordRejection();
                    Notice = step.Expected + " Повторите переключение с клавиатуры.";
                    return false;
                }
            }
            bool changedWindow = switchOrigin != IntPtr.Zero && active != IntPtr.Zero && active != switchOrigin &&
                (switchDestination == IntPtr.Zero || active == switchDestination);
            bool readyToCheck = !step.FreshCopy || (active == commandWindow &&
                DesktopCourseWindows.FreshClipboardFrom(commandWindow, clipboardBeforePath));
            if (readyToCheck && (!switching || changedWindow) && Check(step.Result, active))
            {
                try { if (step.Accepted != null) step.Accepted(active); }
                catch (IOException) { Notice = "Учебная папка недоступна. Восстановите сеанс через панель."; abortCheck = true; }
                catch (UnauthorizedAccessException) { Notice = "Нет доступа к учебной папке. Восстановите сеанс через панель."; abortCheck = true; }
                if (abortCheck) { entered = false; abortCheck = false; return false; }
                Completed++;
                index++;
                if (index == steps.Count) BuildNextProject();
                else if (missionEntries.ContainsKey(index))
                {
                    ClosePracticeApps();
                    if (!missionEntries[index]()) AdvanceToMission();
                }
                Ready("Шаг зачтён.");
                if (Current.ObserveBefore != null) observedSource = Check(Current.ObserveBefore, active);
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
                RecordRejection();
                Notice = step.Expected + " Нажмите ↑, исправьте команду и нажмите Enter.";
            }
            else if (now - commandAt >= TimeSpan.FromSeconds(20))
            {
                entered = false;
                RecordRejection();
                Notice = "Результат пока не подтверждён. " + step.Expected + " Затем повторите сочетание.";
            }
            else if (now - commandAt >= TimeSpan.FromSeconds(1.2))
                Notice = "Результат пока не подтверждён. " + step.Expected;
            return false;
        }

        private void RecordRejection()
        {
            Rejected++;
        }

        // Clicking a control or result manually doesn't satisfy a keyboard step.
        public void RejectMouseAction()
        {
            observedSource = false;
            if (!entered && !Current.SawCompletion) return;
            entered = false;
            Current.SawCompletion = false;
            RecordRejection();
            Notice = "Зафиксирован щелчок. Повторите шаг с клавиатуры.";
        }

        public bool Recover()
        {
            if (Current.Goal == FileLabGoal)
            {
                repeatFileLab = true;
                if (Current.TerminalHandoff) requestedTerminalRetry = markedTerminal;
            }
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
            Action<ProcessStartInfo> configure = null, bool isolated = false, Action<IntPtr> onBind = null)
        {
            if (isolated) step.OwnedIdentity = bind;
            step.BindWindow = hwnd =>
            {
                if (isolated && !Native.WindowBelongsTo(hwnd, step.Process)) return false;
                if (!bind(hwnd)) return false;
                if (isolated)
                {
                    if (step.OwnedWindow != IntPtr.Zero)
                    {
                        if (step.OwnedWindow != hwnd || !Native.HasWindowMark(hwnd, WindowMarker)) return false;
                    }
                    else
                    {
                        if (!Native.MarkWindow(hwnd, WindowMarker)) return false;
                        step.OwnedWindow = hwnd;
                    }
                }
                if (onBind != null) onBind(hwnd);
                return true;
            };
            step.WindowName = name;
            step.ReopenAfter = DateTime.MaxValue;
            step.Launch = step.Reopen = delegate
            {
                step.ReopenAfter = DateTime.UtcNow.AddSeconds(8);
                try
                {
                    if (isolated && step.Process != null)
                    {
                        if (!step.Process.HasExited)
                        {
                            Notice = "Учебное приложение уже запущено. Выберите его через Alt+Tab.";
                            return;
                        }
                        step.Process.Dispose();
                        step.Process = null;
                        launched.Remove(step);
                    }
                    var start = new ProcessStartInfo(executable, arguments)
                        { UseShellExecute = false, WorkingDirectory = Folder };
                    if (configure != null) configure(start);
                    var process = Process.Start(start);
                    if (isolated && process != null)
                    {
                        step.Process = process;
                        launched.Add(step);
                    }
                    else if (process != null) process.Dispose();
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

        private void ClosePracticeApps()
        {
            foreach (var entry in launched) entry.RetireUntil = DateTime.UtcNow.AddSeconds(20);
            retiring.AddRange(launched);
            launched.Clear();
            DrainRetiringWindows(false);
        }

        private void DrainRetiringWindows(bool final)
        {
            for (int i = retiring.Count - 1; i >= 0; i--)
            {
                Step entry = retiring[i];
                bool finished = final || DateTime.UtcNow >= entry.RetireUntil;
                try
                {
                    if (entry.Process.HasExited) finished = true;
                    else if (entry.OwnedWindow != IntPtr.Zero)
                    {
                        Native.CloseOwnedWindow(entry.OwnedWindow, entry.Process, WindowMarker);
                        finished = true;
                    }
                    else
                    {
                        entry.Process.Refresh();
                        IntPtr window = entry.Process.MainWindowHandle;
                        if (Native.WindowBelongsTo(window, entry.Process) && Check(entry.OwnedIdentity, window) &&
                            Native.MarkWindow(window, WindowMarker))
                        {
                            Native.CloseOwnedWindow(window, entry.Process, WindowMarker);
                            finished = true;
                        }
                    }
                }
                catch (InvalidOperationException) { finished = true; }
                catch (System.ComponentModel.Win32Exception) { }
                if (!finished) continue;
                entry.Process.Dispose();
                entry.Process = null;
                retiring.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            FinishProject();
            DrainRetiringWindows(true);
        }

        private void FinishProject()
        {
            pendingLaunch = null;
            ClosePracticeApps();
            if (ownedConsole == null) return;
            ownedConsole.Dispose();
            ownedConsole = null;
        }

        public bool SkipMission()
        {
            ClosePracticeApps();
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
            FinishProject();
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
            bool practiseFileLabEarly = repeatFileLab;
            repeatFileLab = false;
            Action browserLab = () => BuildChrome(browserPath, board, brief, titleA, titleB, briefing);
            string inbox = Path.Combine(Folder, "Inbox-" + round);
            MakeOwnedDirectory(inbox);
            string message = Path.Combine(inbox, "status-" + briefing.Slug + ".txt");
            WriteNew(message, originalText.Replace("\n", "\r\n"));
            Action editLab = () => BuildNotepadRevision(message, originalText);
            Action handoffLab = () => BuildHandoff(inbox, briefing, projectName);
            bool directAdvanced = round == 1 && workdayLevel == WorkdayLevel.Advanced;
            if (directAdvanced)
            {
                if (code != null) Mission(() => BuildCodeFileCopy(projectName));
                else if (chrome != null) Mission(browserLab);
                else Mission(() => BuildProjectFiles(projectName, project, source, note, renamed, copies, originalText));
            }
            Mission(() => BuildQuickJob(briefing, itinerary.NextQuickJob()));
            if (workdayLevel != WorkdayLevel.Intro || round > 1)
                Mission(() => BuildQuickJob(briefing, itinerary.NextQuickJob()));
            if (practiseFileLabEarly) Mission(() => BuildCodeFileCopy(projectName, true));
            if (order == 1) Mission(editLab);
            if (order == 2) Mission(handoffLab);
            if (order == 3 && (!directAdvanced || code != null)) Mission(browserLab);
            if (!directAdvanced || chrome != null || code != null)
                Mission(() => BuildProjectFiles(projectName, project, source, note, renamed, copies, originalText));
            if (order != 1) Mission(editLab);
            if (order != 3 && (!directAdvanced || code != null)) Mission(browserLab);
            Mission(() => BuildCode(project, renamed, originalText));
            if (!practiseFileLabEarly && !directAdvanced) Mission(() => BuildCodeFileCopy(projectName));
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
            steps.Last().FreshCopy = true;
            // Type-to-select leaves the copied file in the clipboard.
            EnterExplorerChild(filing, copies);
            Add(filing, "Вставьте копию заметки", "Ctrl+V",
                commandSource: hwnd => AtFolder(hwnd, copies),
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
            Func<IntPtr, bool> atBoard = hwnd => IsChrome(hwnd) &&
                (DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), titleA) ||
                 DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), titleA + SubmittedSuffix));
            Func<IntPtr, bool> atJournal = hwnd => IsChrome(hwnd) && DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), titleB);
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
            steps.Last().BeforeTitle = titleB;
            if (!followUp) steps.Last().Alternatives = new[] { "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+1", "Ctrl+PgUp", "Ctrl+PgDn" };
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
            CloseChromeStep(goal);
        }

        private void CloseChromeStep(string goal)
        {
            Add(goal, "Закройте учебное окно Chrome", "Alt+F4",
                result: hwnd => browser != IntPtr.Zero && !IsOwnedPracticeWindow(browser),
                detail: "Переключитесь в окно с учебной вкладкой перед закрытием.",
                expected: "Должно закрыться именно окно браузера из этого задания.");
            steps.Last().CloseChrome = true;
        }

        private bool IsPracticeChromeClose(DesktopStroke stroke)
        {
            return stroke.Window == browser && stroke.MarkedWindow && stroke.ProcessId != 0 && launched.Any(entry =>
                entry.OwnedWindow == browser && entry.Process != null && (uint)entry.Process.Id == stroke.ProcessId);
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
            string extension = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "code-lesson");
            OpenForStep(entry, "проект в VS Code", code, "", hwnd =>
            {
                if (!DesktopCourseWindows.WindowTitle(hwnd, "Code", Path.GetFileName(workspace()))) return false;
                return true;
            }, start =>
            {
                if (!File.Exists(Path.Combine(extension, "lesson.js")) || !File.Exists(Path.Combine(extension, "package.json")))
                    throw new IOException("Для проверки редактора распакуйте папку code-lesson рядом с TouchTyping.exe.");
                prepare();
                string profile = Path.Combine(Folder, "Code-" + Path.GetFileName(workspace()));
                start.Arguments = "--user-data-dir " + Quote(profile) +
                    " --extensions-dir " + Quote(Path.Combine(Folder, "CodeExtensions")) +
                    " --extensionDevelopmentPath=" + Quote(extension) +
                    " --locale en --skip-welcome --force-renderer-accessibility --new-window " + Quote(workspace());
                string user = Path.Combine(profile, "User");
                Directory.CreateDirectory(user);
                WriteNew(Path.Combine(user, "settings.json"),
                    "{\"workbench.startupEditor\":\"none\",\"security.workspace.trust.startupPrompt\":\"never\",\"files.autoSave\":\"off\"}");
                start.EnvironmentVariables["TOUCH_TYPING_WORKDAY_ROOT"] = Folder;
            }, isolated: true, onBind: hwnd => editor = hwnd);
        }

        private void BuildCodeFileCopy(string projectName, bool repeat = false)
        {
            if (code == null) return;
            const string goal = FileLabGoal;
            string workspace = Path.Combine(Folder, "files-" + projectName);
            string status = Path.Combine(Folder, ".editor-" + Path.GetFileName(workspace));
            bool reverse = repeat ? !lastFileLayout : itinerary.NextEditorFileLayout() == 1;
            lastFileLayout = reverse;
            excerptCopySequence = 0;
            string source = Path.Combine(workspace, reverse ? "c-source.txt" : "a-source.txt");
            string target = Path.Combine(workspace, reverse ? "a-target.txt" : "c-target.txt");
            const string block = "count = 4\nprint(count)";
            const string header = "# Черновик\n";
            const string revised = "# Копия\n" + block;
            string intro = reverse ? "# Запасной вариант\ncount = 12\nprint(count)\n# Примечание\n" :
                "# Черновые строки\ncount = 8\n";
            string original = intro + "# BEGIN TRANSFER\n" + block + "\n# END TRANSFER\n# Контрольный пример\n" + block;
            int firstLine = intro.Count(c => c == '\n') + 2;
            Func<IntPtr, bool> sourceFocused = w => CodeEditorAt(w, status, source);
            Func<IntPtr, bool> targetFocused = w => CodeEditorAt(w, status, target);
            Func<IntPtr, bool> treeFocused = w => IsCode(w) && DesktopCourseWindows.CodeExplorerRowFocused(w, null);

            Add(goal, "Из открытого редактора переведите фокус в дерево файлов", "Ctrl+Shift+E",
                commandSource: w => IsCode(w) && WorkdayEditorState(status, source, null),
                result: treeFocused,
                detail: "В Explorer нужна рамка на строке дерева. Если боковая панель скрылась, откройте её снова.",
                expected: "Переведите фокус в дерево Explorer с открытым исходным файлом.");
            OpenCodeForStep(steps.Last(), () => workspace, delegate
            {
                MakeOwnedDirectory(workspace);
                WriteNew(source, original);
                WriteNew(target, header);
                WriteNew(Path.Combine(workspace, "b-reference.txt"), "Справочный файл");
                WriteNew(Path.Combine(workspace, ".workday-source"), Path.GetFileName(source));
            });
            Add(goal, "Стрелками выберите " + Path.GetFileName(target), reverse ? "Up" : "Down",
                commandSource: IsCode,
                result: w => IsCode(w) && DesktopCourseWindows.CodeExplorerRowFocused(w, Path.GetFileName(target)),
                expected: "Оставьте клавиатурный фокус на нужной строке дерева.");
            steps.Last().EquivalentKeys = new[] { reverse ? "Down" : "Up" };
            Add(goal, "Откройте выбранный файл из дерева", "Enter",
                commandSource: IsCode,
                result: w => targetFocused(w) || (IsCode(w) && WorkdayEditorState(status, target, null) &&
                    DesktopCourseWindows.CodeExplorerRowFocused(w, Path.GetFileName(target))),
                expected: "Если файл не открылся, вернитесь в Explorer, выберите его и нажмите Enter.");
            steps.Last().ObserveBefore = w => IsCode(w) && DesktopCourseWindows.CodeExplorerRowFocused(w, Path.GetFileName(target));
            steps.Last().CancelKeys = new[] { "Ctrl+P", "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+Shift+P" };
            Add(goal, "Переведите фокус из дерева в редактор выбранного файла", "Ctrl+1",
                commandSource: IsCode, result: targetFocused,
                expected: "Нужен курсор в редакторе " + Path.GetFileName(target) + ".");
            steps.Last().Alternatives = new[] { "F6", "Shift+F6" };
            Add(goal, "Выделите только две строки между BEGIN TRANSFER и END TRANSFER", "Shift+End",
                commandSource: w => IsCode(w) && WorkdayEditorState(status, source, null),
                result: w => sourceFocused(w) && WorkdayEditorState(status, source, "block"),
                detail: "Ctrl+Tab вернёт исходный файл. Ctrl+G → " + firstLine + " → Enter, затем Home, Shift+↓, Shift+End.",
                expected: "Не включайте метки и соседний пример. Выделите только строки между метками.");
            steps.Last().Alternatives = new[] { "Shift+Left", "Shift+Right", "Shift+Up", "Shift+Down", "Shift+Home",
                "Ctrl+Shift+Left", "Ctrl+Shift+Right", "Ctrl+Shift+Up", "Ctrl+Shift+Down" };
            Add(goal, "Скопируйте выделенный фрагмент", "Ctrl+C",
                commandSource: w => IsCode(w) && WorkdayEditorState(status, source, "block"),
                result: w => sourceFocused(w) && ClipboardTextIs(block),
                accepted: w => excerptCopySequence = Native.GetClipboardSequenceNumber(),
                expected: "Выделите только строки между метками в исходном файле и скопируйте их.");
            steps.Last().FreshCopy = true;
            Add(goal, "Вставьте блок после заголовка в " + Path.GetFileName(target), "Ctrl+V",
                commandSource: w => IsCode(w) && WorkdayEditorState(status, target, null),
                result: w => targetFocused(w) && WorkdayEditorState(status, target, "pasted"),
                detail: "Перейдите к копии через Ctrl+Tab и поставьте курсор в конец через Ctrl+End.",
                expected: "Вставьте свежую копию отмеченных строк сразу после «# Черновик». Если буфер заменён, Ctrl+Alt+F9 начнёт новый проект.");
            steps.Last().RequiresExcerptCopy = true;
            steps.Last().ObserveBefore = w => targetFocused(w) && !WorkdayEditorState(status, target, "pasted");
            Add(goal, "Замените заголовок копии на «# Копия» и сохраните", "Ctrl+S",
                commandSource: w => IsCode(w) && WorkdayEditorState(status, target, null),
                result: w => targetFocused(w) && WorkdayEditorState(status, target, "revised") && SameText(target, revised) && SameText(source, original),
                detail: "Ctrl+Home, Shift+End выделят заголовок. Введите новый текст и нажмите Ctrl+S.",
                copyText: "# Копия", expected: "Сохраните копию с новым заголовком. Исходный файл должен остаться без изменений.");
            if (chrome != null)
            {
                markedTerminal = requestedTerminalRetry ?? (itinerary.NextTerminalTransfer() == 1);
                requestedTerminalRetry = null;
                BuildTerminalHandoff(goal, workspace, status, target, reverse, markedTerminal);
            }
        }

        private void BuildTerminalHandoff(string goal, string workspace, string status, string target, bool reverse, bool markedOnly)
        {
            int firstStep = steps.Count;
            string title = Path.GetFileName(workspace) + " / Local receipt";
            string pages = Path.Combine(Folder, "pages");
            MakeOwnedDirectory(pages);
            string card = Path.Combine(pages, "receipt-" + round + ".html");
            string cover = Path.Combine(pages, "cover-" + round + ".html");
            string output = TerminalJournal(token + "-" + round);
            string transfer = TerminalTransfer(output, markedOnly);
            WriteNew(card, TerminalReceipt(title, transfer, markedOnly));
            WriteNew(cover, ChromePageShell(title + " / Index", "Журнал проекта", "Учебные записи",
                "<p>Поле для передачи вывода открыто в соседней вкладке.</p>"));

            Add(goal, "Откройте встроенный терминал с учебным журналом", "Ctrl+`",
                commandSource: IsCode, result: w => IsCode(w) && DesktopCourseWindows.CodeTerminalFocused(w),
                detail: "Если Chrome вышел на передний план, вернитесь в VS Code через Alt+Tab. Затем откройте терминал.",
                expected: "Активным должен стать встроенный терминал учебного VS Code.");
            Step terminal = steps.Last();
            var chromeEntry = new Step();
            string arguments = "--user-data-dir=" + Quote(Path.Combine(Folder, "Chrome-receipt-" + round)) +
                " --disable-background-networking --no-first-run --no-default-browser-check --force-renderer-accessibility --new-window " +
                Quote(new Uri(reverse ? card : cover).AbsoluteUri) + " " + Quote(new Uri(reverse ? cover : card).AbsoluteUri);
            OpenForStep(chromeEntry, "учебные вкладки Chrome", chrome, arguments, w =>
            {
                string caption = Native.Title(w);
                if (!DesktopCourseWindows.IsChromeCaption(caption, title) &&
                    !DesktopCourseWindows.IsChromeCaption(caption, title + " / Index")) return false;
                return true;
            }, isolated: true, onBind: w => browser = w);
            terminal.Launch = delegate
            {
                // The tabs are ready before the handoff. Foreground could change when Chrome loads.
                chromeEntry.Launch();
            };
            // Stage the journal before the isolated extension activates.
            WriteNew(Path.Combine(Folder, ".terminal-" + Path.GetFileName(workspace)), output);

            Add(goal, "Прокрутите вывод терминала вверх", "Ctrl+Home",
                commandSource: IsCode, result: w => IsCode(w) && DesktopCourseWindows.CodeTerminalFocused(w),
                detail: markedOnly ? "Отмеченные строки находятся в начале журнала." : "Ctrl+Home прокручивает терминал к первой строке.",
                expected: "Вернитесь в терминал учебного VS Code.");
            if (markedOnly)
            {
                Add(goal, "Откройте доступное представление журнала", "Alt+F2",
                    commandSource: IsCode, result: w => IsCode(w) && DesktopCourseWindows.CodeAccessibleViewFocused(w),
                    expected: "Нажмите Alt+F2 в фокусе встроенного терминала.");
            }
            Add(goal, markedOnly ? "Скопируйте только три строки [SEND]" : "Выделите весь журнал терминала и скопируйте",
                markedOnly ? "Ctrl+C" : "Ctrl+Shift+C", commandSource: IsCode,
                result: w => IsCode(w) && (markedOnly ? DesktopCourseWindows.CodeAccessibleViewFocused(w) :
                    DesktopCourseWindows.CodeTerminalFocused(w)) && ClipboardTextIs(transfer, true),
                detail: markedOnly ? "В представлении нажмите Ctrl+Home, три раза Shift+Down, затем Ctrl+C." :
                    "Ctrl+Shift+P → Terminal: Select All → Enter. Затем Ctrl+Shift+C.",
                expected: markedOnly ? "Нужны ровно первые три строки [SEND], включая отметки." :
                    "В буфер должны попасть все 70 строк учебного журнала.");
            steps.Last().FreshCopy = true;
            Add(goal, "Переключитесь в окно с уже открытой карточкой Chrome", "Alt+Tab",
                commandSource: IsCode, result: w => Check(chromeEntry.BindWindow, w),
                expected: "Выберите учебный Chrome. Если он не открылся, повторите проект через Ctrl+Alt+F9.");
            steps.Last().SwitchTarget = w => Native.WindowBelongsTo(w, chromeEntry.Process) && Check(chromeEntry.OwnedIdentity, w);
            steps.Last().Alternatives = new[] { "Alt+Esc" };
            Add(goal, "Выберите вкладку с полем для вывода", reverse ? "Ctrl+1" : "Ctrl+2",
                commandSource: IsChrome, result: w => IsChrome(w) && DesktopCourseWindows.IsChromeCaption(Native.Title(w), title),
                detail: "Если поле уже видно, сначала перейдите к вкладке «Учебные записи», затем вернитесь к полю.",
                expected: "Переключитесь из вкладки «Учебные записи» к полю «Вывод терминала».");
            steps.Last().BeforeTitle = title + " / Index";
            steps.Last().Alternatives = new[] { "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+PgUp", "Ctrl+PgDn" };
            Add(goal, "Вставьте вывод в учебное поле", "Ctrl+V",
                commandSource: IsChrome, result: w => IsChrome(w) && DesktopCourseWindows.IsChromeCaption(Native.Title(w), title + " / Pasted"),
                detail: "Если поле вне фокуса, перейдите к нему клавишей Tab.",
                expected: markedOnly ? "Вставьте только три отмеченные строки в локальное поле." :
                    "Вставьте все строки журнала в локальное поле.");
            steps.Last().BeforeTitle = title;
            Add(goal, "Вернитесь к редактированию копии в VS Code", "Alt+Tab",
                commandSource: IsChrome, result: IsCode, detail: markedOnly ? "После возврата нажмите Esc, чтобы закрыть доступное представление." : "",
                expected: "Выберите то же учебное окно VS Code.");
            steps.Last().Alternatives = new[] { "Alt+Esc" };
            Add(goal, "Переведите фокус из панели терминала в редактор", "Ctrl+1",
                commandSource: IsCode, result: w => CodeEditorAt(w, status, target),
                expected: "Нужен курсор в редакторе изменённой копии.");
            steps.Last().Alternatives = new[] { "F6", "Shift+F6" };
            foreach (var step in steps.Skip(firstStep)) step.TerminalHandoff = true;
        }

        internal static string TerminalJournal(string identifier)
        {
            return string.Join("\n", Enumerable.Range(1, 70)
                .Select(n => (n <= 3 ? "[SEND] " : "") + identifier + "  record " + n.ToString("00")));
        }

        internal static string TerminalTransfer(string journal, bool markedOnly)
        {
            return markedOnly ? string.Join("\n", journal.Split('\n').Take(3)) : journal;
        }

        internal static string TerminalReceipt(string title, string output, bool markedOnly = false)
        {
            return ChromePageShell(title, "Локальная карточка", "Передача вывода",
                (markedOnly ? "<p>Вставьте только три строки [SEND].</p>" : "<p>Вставьте все строки журнала.</p>") +
                "<label for=receipt>Вывод терминала</label><textarea id=receipt rows=12 cols=40 autofocus></textarea>" +
                "<p role=status id=status></p><script>const box=document.getElementById('receipt'),base=document.title," +
                "expected='" + output.Replace("\n", "\\n") + "';" +
                "box.addEventListener('input',()=>document.title=base);" +
                "box.addEventListener('paste',event=>{if(!event.isTrusted)return;event.preventDefault();" +
                "const pasted=event.clipboardData.getData('text/plain');" +
                "const valid=pasted.replace(/\\r\\n/g,'\\n').replace(/\\n+$/,'')===expected;" +
                "box.value=valid?pasted:'';document.title=valid?base+' / Pasted':base;" +
                "document.getElementById('status').textContent=valid?'Вывод принят':'Проверьте требуемые строки';});</script>");
        }

        private bool CodeEditorAt(IntPtr hwnd, string status, string file)
        {
            return InCodeDocument(hwnd, file) && WorkdayEditorState(status, file, null) &&
                DesktopCourseWindows.CodeEditorFocused(hwnd);
        }

        private static bool ClipboardTextIs(string expected, bool trimFinalNewlines = false)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return false;
                string actual = System.Windows.Clipboard.GetText().Replace("\r\n", "\n");
                return (trimFinalNewlines ? actual.TrimEnd('\n') : actual) == expected;
            }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }
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

        internal static bool MatchesShortcut(string chord, string primary, IEnumerable<string> alternatives)
        {
            return string.Equals(chord, primary, StringComparison.OrdinalIgnoreCase) ||
                (alternatives != null && alternatives.Contains(chord, StringComparer.OrdinalIgnoreCase));
        }

        private void Ready(string message)
        {
            entered = false;
            observedSource = false;
            listeningSince = DateTime.UtcNow;
            Notice = message;
        }

        private bool IsCurrentExplorer(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == explorer && Native.IsExplorer(hwnd); }
        private bool IsChrome(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == browser && IsOwnedPracticeWindow(hwnd); }
        private bool IsCode(IntPtr hwnd) { return hwnd != IntPtr.Zero && hwnd == editor && IsOwnedPracticeWindow(hwnd); }
        private bool IsOwnedPracticeWindow(IntPtr hwnd)
        {
            return launched.Any(step => step.OwnedWindow == hwnd && Native.WindowBelongsTo(hwnd, step.Process) && Native.HasWindowMark(hwnd, WindowMarker));
        }
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
            string arguments = "--user-data-dir=" + Quote(Path.Combine(Folder, "Chrome-" + round)) +
                " --disable-background-networking --no-first-run --no-default-browser-check --force-renderer-accessibility --new-window " + Quote(new Uri(file).AbsoluteUri);
            OpenForStep(steps.Last(), "учебное окно Chrome", chrome, arguments, delegate(IntPtr hwnd)
            {
                string caption = Native.Title(hwnd);
                if (!DesktopCourseWindows.IsChromeCaption(caption, title) &&
                    !DesktopCourseWindows.IsChromeCaption(caption, title + LinkFocusSuffix)) return false;
                return true;
            }, isolated: true, onBind: hwnd => browser = hwnd);
        }

        private const string LinkFocusSuffix = " / Resource link";

        private void BuildChromeDirectory(string catalog, string document, string catalogTitle, string docTitle, DesktopBriefing briefing)
        {
            string label = "Инструкция для " + briefing.Team;
            string goal = "Подготовьте ссылку на инструкцию для команды " + briefing.Team;
            string archive = Path.ChangeExtension(catalog, ".archive.html");
            string contacts = Path.ChangeExtension(catalog, ".contacts.html");
            string next = Path.ChangeExtension(catalog, ".checklist.html");
            string nextTitle = docTitle + " / Checklist";
            string clue = "LINK-" + token;
            string anchor = "<a id=resource href='" + LocalName(document) + "'>" + label + "</a>";
            WriteNew(catalog, ChromePageShell(catalogTitle, "Локальный поиск", "Результаты поиска",
                "<label for=query>Поисковый запрос</label><input id=query readonly value='документы проекта'>" +
                "<p>Откройте действующую инструкцию команды.</p><section><ol>" +
                "<li><a href='" + LocalName(archive) + "'>Архив инструкций проекта</a><p>Предыдущая редакция регламента.</p></li><li>" +
                anchor + "<p>Действующая инструкция и контрольный список передачи.</p></li>" +
                "<li><a href='" + LocalName(contacts) + "'>Контакты команды</a><p>Справочник участников проекта.</p></li>" +
                "</ol></section>" + LinkFocusScript("resource")));
            WriteNew(archive, ChromePageShell(catalogTitle + " / Archive", "Локальный поиск", "Архив инструкций", "<p>Предыдущая редакция.</p>"));
            WriteNew(contacts, ChromePageShell(catalogTitle + " / Contacts", "Локальный поиск", "Контакты", "<p>Справочник проекта.</p>"));
            WriteNew(document, ChromePageShell(docTitle, "Рабочая инструкция", label,
                "<section><p>Перед передачей материалов проверьте " + briefing.Topic +
                ".</p><p>Метка " + clue + " ведёт к <a id=next href='" + LocalName(next) +
                "'>контрольному списку</a>.</p></section>" + LinkFocusScript("next")));
            WriteNew(next, ChromePageShell(nextTitle, "Контрольный список", "Передача материалов", "<p>Список для команды.</p>"));
            Func<IntPtr, bool> atDocument = hwnd => IsChrome(hwnd) && (DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), docTitle) ||
                DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), docTitle + LinkFocusSuffix));
            Func<IntPtr, bool> atChecklist = hwnd => IsChrome(hwnd) && DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), nextTitle);
            Add(goal, "Выберите ссылку «" + label + "»", "Tab", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), catalogTitle + LinkFocusSuffix),
                detail: "Перебирайте ссылки клавишей Tab. Shift+Tab вернёт на одну назад.", expected: "Оставьте рамку фокуса на нужной инструкции.");
            steps.Last().EquivalentKeys = new[] { "Shift+Tab" };
            OpenPracticeChrome(catalog, catalogTitle);
            Add(goal, "Откройте выбранную инструкцию", "Enter", commandSource: IsChrome, result: atDocument,
                expected: "Если открыли другой документ, вернитесь через Alt+← и выберите инструкцию команды.");
            steps.Last().BeforeTitle = catalogTitle + LinkFocusSuffix;
            Add(goal, "Откройте поиск по инструкции", "Ctrl+F", commandSource: atDocument,
                result: hwnd => atDocument(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, false),
                expected: "Откройте поиск в выбранной инструкции.");
            Add(goal, "Найдите в инструкции метку " + clue, "Enter", commandSource: atDocument,
                result: hwnd => atDocument(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, false, clue),
                copyText: clue, expected: "Введите фразу в поле поиска и нажмите Enter.");
            Add(goal, "Закройте поиск и продолжите с инструкцией", "Esc", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && !DesktopCourseWindows.ChromeEditFocused(hwnd, false) &&
                    (DesktopCourseWindows.ChromePageFocused(hwnd, docTitle) ||
                     DesktopCourseWindows.ChromePageFocused(hwnd, docTitle + LinkFocusSuffix)),
                expected: "Закройте поиск в учебной вкладке.");
            Add(goal, "Перейдите по ссылке из найденной строки", "Enter", commandSource: IsChrome, result: atChecklist,
                detail: "Нажимайте Tab или Shift+Tab до рамки на ссылке «контрольному списку», затем Enter.",
                expected: "Откройте именно контрольный список из инструкции.");
            steps.Last().BeforeTitle = docTitle + LinkFocusSuffix;
            Add(goal, "Выделите адрес контрольного списка", "Alt+D", commandSource: IsChrome,
                result: hwnd => atChecklist(hwnd) && DesktopCourseWindows.ChromeEditFocused(hwnd, true),
                expected: "Нажмите Alt+D в учебном документе, чтобы выбрать его адрес.");
            Add(goal, "Скопируйте ссылку для команды", "Ctrl+C", commandSource: IsChrome,
                result: hwnd => atChecklist(hwnd) && ClipboardHasFileAddress(next),
                expected: "Нажмите Alt+D, затем Ctrl+C. В буфере должен быть адрес открытой инструкции.");
            steps.Last().FreshCopy = true;
            Add(goal, "Вернитесь в каталог с готовой ссылкой", "Alt+Left", commandSource: IsChrome,
                result: hwnd => IsChrome(hwnd) && (DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), catalogTitle) ||
                    DesktopCourseWindows.IsChromeCaption(Native.Title(hwnd), catalogTitle + LinkFocusSuffix)),
                expected: "Вернитесь к выдаче двумя переходами Alt+←.",
                sourceLabel: label, destLabel: "Буфер обмена", outcome: "Ссылку можно вставить в сообщение команде.");
            CloseChromeStep(goal);
        }

        private static string LocalName(string path)
        {
            return Uri.EscapeDataString(Path.GetFileName(path));
        }

        private static string LinkFocusScript(string id)
        {
            return "<script>const link=document.getElementById('" + id + "'),base=document.title;" +
                "function mark(){document.title=document.activeElement===link?base+'" + LinkFocusSuffix + "':base;}" +
                "link.onfocus=mark;link.onblur=()=>document.title=base;document.addEventListener('keyup',e=>{if(e.key==='Tab')mark();});</script>";
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
            return "<!doctype html><html lang=ru><meta charset=utf-8><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; form-action 'none'\"><meta name=viewport content='width=device-width, initial-scale=1'><title>" +
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
