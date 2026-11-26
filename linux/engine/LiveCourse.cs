using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed record LivePhase(string Title, string Route);
internal sealed record LiveMission(string App, int Level, string Id, string Title, LivePhase[] Phases, bool FinishAfterRoute = false, bool Curated = false, Action<LabScene> Prepare = null);
internal sealed record LiveGoal(int Phase, LabTask Task, string Target, string Layout, bool VerifyReport = false, string LegacyTarget = null, string ArtifactPath = null, string CsvExplorerTarget = null, string WindowsRecoveryTarget = null);

internal static partial class LiveCourse
{
    private static readonly Lazy<List<LiveMission>> catalog = new(Build);
    internal static List<LiveMission> Missions => catalog.Value;
    private static List<LiveMission> Build()
    {
        var result = new List<LiveMission>();
        BrowserMissions(result);
        ChromeTabReviewMissions(result);
        EditorMissions(result);
        EditorProjectSearchMissions(result);
        EditorExactEditMissions(result);
        LineEditMissions(result);
        PythonEditingMissions(result);
        BlockRepairMissions(result);
        EditorDiagnosticMissions(result);
        EditorTracebackMissions(result);
        TerminalMissions(result);
        DesktopMissions(result);
        WindowsDesktopMissions(result);
        WorkdayDebugMissions(result);
        ProjectFileMissions(result);
        SourceHandoffMissions(result);
        DesktopFileSearchMissions(result);
        return result;
    }

    // The same case has a reproducible starting desk. The user's live desk is never used for rehearsal.
    internal static List<LiveGoal> Compile(LiveMission mission, LabScene initial, bool prepared = false, bool nativeEnglishInput = false)
    {
        var trial = initial.Clone();
        if (!prepared) mission.Prepare?.Invoke(trial);
        var goals = new List<LiveGoal>();
        for (int phase = 0; phase < mission.Phases.Length; phase++)
        {
            foreach (string token in RouteTokens(mission.Phases[phase]))
            {
                if (!nativeEnglishInput && (IsCommandToken(token) || token is "disk-capacity-query" or "source-log-command" or "source-config-test" || IsProjectSearchQuery(token) || token is "browser-search" or "code-clone-command" or "research-find" or "file-name-commit" ||
                    NotebookEnglishToken(token) || token is "checkout-expect" or "terminal-incident-filter" or "code-rename-confirm" or "debug-unit" or "debug-report-check"
                    or "debug-project-query" or "debug-doc-submit" or "debug-rewrite" or "exact-find-submit" or "python-edit-write" or "python-edit-write-fee" or "python-edit-write-total" or "notebook-file" or "notebook-report" or "notebook-type" or "notebook-report-save" or "moved-report-save" ||
                    trial.ExactEdit && token is "quick-choose" or "save") && trial.Layout == "RU")
                {
                    var change = trial.WithBinding(Actions["layout"]);
                    string layout = trial.Layout;
                    trial.Apply("layout");
                    goals.Add(new(phase, change with { Title = "Переключите ввод на английский для команды" }, Evidence(change, trial), layout));
                }
                var task = MakeTask(token, trial);
                bool verifyReport = mission.Id == "workday-report" && task.Id == "command/head -n 2 " + trial.Report;
                string artifactPath = mission.Id == "workday-source-note" && task.Id == "command/cat notes.txt"
                    ? "/home/student/project/notes.txt" : null;
                if (trial.DebugDay && token == "debug-report-check") artifactPath = trial.DebugReportArtifact(trial.DebugReportName);
                string original = Evidence(task, trial, verifyReport: verifyReport, artifactPath: artifactPath);
                var before = trial.Clone();
                if (IsPickerToken(token))
                {
                    int count = trial.Entries(trial.Active).Length;
                    for (int i = 0; i < count && !AtItem(trial, task.Text); i++) trial.Apply(task.Action);
                    if (!AtItem(trial, task.Text)) throw new InvalidOperationException(mission.Id + ": объект " + task.Text + " отсутствует");
                }
                else if (token.StartsWith("open-") && trial.Active?.App == token[5..]) continue;
                else if ((token == "maximize" && trial.Active?.State == "maximized") ||
                    (!trial.GnomeWindows && ((token == "tile-left" && trial.Active?.State == "left") || (token == "tile-right" && trial.Active?.State == "right")))) continue;
                else if (token == "save" && !trial.ExactEdit && !trial.TracebackPractice && trial.Active?.Body == trial.Active?.Tab?.SavedBody) continue;
                else if (token == "bookmark" && trial.Active.SavedBookmarks.Contains(trial.Active.Url)) continue;
                else if (token == "first-tab" && trial.Active.ActiveTab == 0 || token == "last-tab" && trial.Active.ActiveTab == trial.Active.Tabs.Count - 1) continue;
                else if (!Demonstrate(task, trial)) throw new InvalidOperationException(mission.Id + ": недоступно " + token);
                string proof = Evidence(task, trial, verifyReport: verifyReport, artifactPath: artifactPath);
                if (original == proof) { trial = before; continue; }
                goals.Add(new(phase, task, proof, before.Layout, verifyReport, verifyReport ? Evidence(task, trial) : null,
                    artifactPath, CsvExplorerProof(task, trial),
                    HasWindowsRecoveryScope(mission.Id) ? WindowsRecoveryProof(task, trial) : null));
            }
        }
        if (goals.Count < (mission.Curated ? 1 : 20)) throw new InvalidOperationException(mission.Id + ": меньше 20 результатов");
        return goals;
    }

    private static IEnumerable<string> RouteTokens(LivePhase phase) =>
        phase.Route.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsPickerToken(string token) => token.StartsWith("pick:", StringComparison.Ordinal) ||
        token.StartsWith("quick:", StringComparison.Ordinal) || token.StartsWith("sym:", StringComparison.Ordinal);
    private static bool IsProjectSearchQuery(string token) => token == "project-search-submit" || token.StartsWith("search-query:", StringComparison.Ordinal);
    private static bool IsCommandToken(string token) => token.StartsWith("cmd:", StringComparison.Ordinal);
    private static string DecodeCommand(string token) => token[4..].Replace('~', ' ');
    private static string CommandId(string command) => "command/" + command;

    // Only a coarse prefilter: the compiled list is authoritative for layout and
    // for picker goals that turn into different IDs depending on focus.
    internal static bool CouldPracticeAny(LiveMission mission, HashSet<string> taskIds) =>
        mission.Phases.SelectMany(RouteTokens).Any(token =>
            IsCommandToken(token) ? taskIds.Contains(CommandId(DecodeCommand(token))) || taskIds.Contains("layout") :
            token == "browser-review-tab" ? taskIds.Any(id => id.StartsWith("browser-tab-", StringComparison.Ordinal)) :
            IsPickerToken(token) ? taskIds.Contains("file-next") || taskIds.Contains("link-next") ||
                taskIds.Contains("quick-next") || taskIds.Contains("symbol-next") :
            taskIds.Contains(token));

    internal static int CountWeakGoals(IEnumerable<LiveGoal> goals, HashSet<(string Task, string Layout)> weak) =>
        goals.Count(goal => weak.Contains((goal.Task.Id, goal.Layout)));

    private static bool AtItem(LabScene scene, string target) => scene.Entries(scene.Active).ElementAtOrDefault(scene.Active.Focus is "quick-open" or "symbols" ? scene.Active.OptionIndex : scene.Active.Item)?.Title == target;
    private static readonly Dictionary<string, LabTask> Actions = LabCatalog.AllTasks.ToDictionary(task => task.Id);
    private static LabTask MakeTask(string token, LabScene scene)
    {
        token = scene.ResolveTabReviewToken(token);
        if (scene.DiskSpaceLesson && scene.MakeDiskCapacityTask(token) is LabTask diskTask) return diskTask;
        if (token == "terminal-incident-filter")
            return new(scene.Terminal.IsPowerShell ? "command/powershell-incident" : "command/filter-incident",
                "Покажите строки новой попытки из журнала", "",
                "Возьмите ID из новой строки ERROR, подставьте его в показанную команду и нажмите Enter.",
                scene.TerminalIncidentFilter, "command");
        if (scene.DescribeTracebackTask(token) is LabTask traceback) return traceback;
        if (NotebookTask(token, scene) is LabTask notebook) return notebook;
        if (PythonEditTask(token) is LabTask edit) return edit.Action == "python-edit-write" ? edit with { Id = edit.Action } : edit;
        if (token == "checkout-expect") return new(token, "Найдите вызов checkout_total(4, 3) в тесте", "",
            "Введите выражение без обёртки assertEqual и нажмите Enter. Сверьте значение 14.",
            LabScene.CheckoutExpectedCall, token);
        if (token == "file-search-query")
            return new(token, "Введите номер заявки «" + scene.FileSearchQuery + "» в поле поиска", "",
                "В результатах должны быть две версии отчёта из этой папки.", scene.FileSearchQuery, token);
        if (token == "file-search-verify")
            return new(token, "В строке approved проверьте контрольное число «" + scene.FileSearchControl + "»", "",
                "Введите контрольное число в поиске открытой копии и нажмите Enter.", scene.FileSearchControl, token);
        if (token == "moved-report-save") return Actions["save"] with {
            Title = "Допишите «" + scene.NewReportRow + "» в конце перемещённого CSV и сохраните файл",
            Hint = "Ctrl+End перейдёт к пустой строке после данных. Введите " + scene.NewReportRow + " и нажмите Ctrl+S.",
            Text = scene.NewReportRow };
        if (token == "source-log-command") token = "cmd:" + scene.SourceLogCommand.Replace(' ', '~');
        if (token == "source-config-test") token = "cmd:" + LabScene.SourceTestCommand.Replace(' ', '~');
        if (token == "file-route-parent") token = scene.WindowsDesktop ? "win-explorer-parent" : "file-parent";
        if (token == "file-name-commit") return Actions[token] with { Keys = "", Text = scene.FileStem,
            Title = "Введите «" + scene.FileStem + "» вместо выделенного имени и нажмите Enter",
            Hint = "Сохраните расширение .csv. Если передумали, Esc отменит переименование." };
        if (token == "browser-search")
            return Actions["navigate-pasted"] with { Keys = "", Text = scene.BrowserPracticeQuery,
                Title = "Введите «" + scene.BrowserPracticeQuery + "» и нажмите Enter",
                Hint = "Ищем в учебной выдаче; откройте результат и вернитесь к тому же запросу." };
        if (token == "research-find")
            return new(token, scene.ContextHandoffActive ? scene.SourceReadTitle :
                "Найдите запись «" + scene.BrowserResearchNeedle + "» в открытом файле",
                "", "Введите идентификатор записи и нажмите Enter.",
                scene.ContextHandoffActive ? scene.SourceReadNeedle : scene.BrowserResearchNeedle, token);
        if (token == "code-clone-command")
            return Actions[token] with { Keys = "", Text = "Git: Clone",
                Title = "Введите Git: Clone в палитре и нажмите Enter" };
        if (scene.BrowserPracticeActive && token == "paste" && scene.Active?.Focus == "clone-url")
            return Actions[token] with { Title = "Вставьте скопированную HTTPS-ссылку репозитория" };
        if (scene.DebugDay)
        {
            if (token == "debug-project-query") token = "search-query:" + scene.DebugCallQuery;
            else if (token == "debug-unit") token = "cmd:" + scene.DebugTestCommand.Replace(' ', '~');
            else if (token == "debug-report-check") token = "cmd:cat~" + scene.DebugReportName;
            else if (token == "debug-doc-submit") return new LabTask(token, "Найдите коэффициент в справке", "",
                "Наберите «" + scene.DebugDocumentation + "» в Chrome и нажмите Enter.", scene.DebugDocumentation, token);
            else if (token == "debug-rewrite") return new LabTask(token, "Исправьте формулу", "",
                "Замените выбранную строку по справке. Сохраните отступы и нажмите Enter.", scene.DebugRewrite, token);
        }
        if (IsPickerToken(token))
        {
            var w = scene.Active;
            string raw = scene.DesktopSearchTarget(scene.SourceRouteTarget(scene.FileRouteTarget(scene.ResolveBrowserRouteTarget(token[(token.IndexOf(':') + 1)..]))));
            string query = raw.Replace('_', ' ');
            var entry = scene.Entries(w).FirstOrDefault(item => item.Path == raw || item.Path.EndsWith("/" + raw, StringComparison.Ordinal) ||
                item.Path == query || item.Title == query || item.Path.EndsWith("/" + query, StringComparison.Ordinal));
            if (entry == null) throw new InvalidOperationException("Не найден объект: " + query + " в " + w.Url + w.Folder);
            var picker = Actions[w.Focus == "quick-open" ? "quick-next" : w.Focus == "symbols" ? "symbol-next" : (w.App == "files" || w.Focus == "explorer") ? "file-next" : "link-next"];
            return scene.DescribeDesktopSearchTask(picker with { Title = "Выберите «" + entry.Title + "»", Text = entry.Title });
        }
        if (IsCommandToken(token))
        {
            string value = DecodeCommand(token);
            string objective = value switch
            {
                "pwd" => "Узнайте, в какой папке вы находитесь",
                "ls" => "Посмотрите, что лежит в текущей папке",
                "cd" => "Перейдите в домашнюю папку",
                "whoami" => "Узнайте имя текущего пользователя",
                "id" => "Узнайте идентификатор пользователя и его группы",
                "uname" => "Узнайте имя операционной системы",
                "ps" => "Посмотрите процессы и их номера PID",
                "history" => "Посмотрите, какие команды уже выполнялись",
                "clear" => "Очистите экран, не удаляя файлы и историю",
                "reboot" => "Перезапустите только учебную систему",
                "cd " + LabScene.BlockRepairRoot when scene.BlockRepair => "Перейдите в папку checkout для запуска проверок",
                "python3 -m unittest test_checkout" when scene.BlockRepair => "Запустите обе проверки сохранённого расчёта",
                "python3 -m unittest test_checkout > " + LabScene.CheckoutReport when scene.BlockRepair => "Сохраните вывод проверок в checkout-test.txt",
                "cat " + LabScene.CheckoutReport when scene.BlockRepair => "Убедитесь, что файл содержит две пройденные проверки",
                "grep ERROR app.log" when scene.PracticalTerminal => "Выведите только строки ERROR из app.log",
                "tail -f app.log" when scene.PracticalTerminal => "Следите за новыми строками app.log",
                "Select-String ERROR sync.log" when scene.PracticalTerminal => "Покажите ошибки синхронизации с номерами строк",
                "Get-Content sync.log -Wait" when scene.PracticalTerminal => "Следите за новыми строками sync.log",
                _ => "Выполните «" + value + "»"
            };
            return new(CommandId(value), objective, "", !scene.BlockRepair && !scene.PracticalTerminal && value.Length > 12 ?
                "Введите команду и нажмите Enter. Если она уже в истории: Ctrl+R, фрагмент, сверка строки, затем Enter." :
                "Наберите команду целиком и нажмите Enter.", value, "command");
        }
        if (token == "gnome-write")
            return new(token, "Дописать комментарий: «" + LabScene.GnomeNoteText + "»", "",
                "Наберите слово в активной строке app.py и нажмите Enter.", LabScene.GnomeNoteText, token);
        if (IsProjectSearchQuery(token))
        {
            string query = token == "project-search-submit" ? LabScene.SearchNeedle : token[13..].Replace('~', ' ');
            return Actions["project-search-submit"] with { Text = query, Title = "Введите «" + query + "» и нажмите Enter" };
        }
        var task = scene.DescribeTabReview(scene.DescribeSourceTask(scene.WithBinding(Actions[token])));
        if (scene.GnomePractice == "matebook" && token == "layout")
            return task with { Title = "Переключите учебный ввод на " + (scene.Layout == "RU" ? "EN" : "RU") };
        if (!scene.ContextHandoffActive && scene.GnomePractice == "same-app" && token.StartsWith("switch-group-", StringComparison.Ordinal))
            return task with { Title = scene.Active?.Url == "app.py" ? "Выберите окно с README.md" : "Выберите окно с app.py" };
        if (scene.ExactEdit && token == "quick-choose") return task with
            { Title = "Введите ap и откройте app.py", Text = LabScene.ExactFileFragment };
        if (scene.ExactEdit && token == "exact-find-submit") return task with { Text = LabScene.ExactNeedle };
        if (scene.ExactEdit && token == "save") return task with
            { Title = "Введите ready вместо выделенного draft и сохраните", Text = LabScene.ExactReplacement };
        if (task.Action == "code-rename-confirm") return task with { Text = LabScene.RenameTarget };
        if (scene.GnomeWindows && (token == "tile-left" && scene.Active?.State == "left" ||
            token == "tile-right" && scene.Active?.State == "right"))
            return task with { Title = "Снимите привязку окна повторным нажатием" };
        if (task.Action is "file-cut" or "file-copy" or "file-paste")
        {
            var window = scene.Active;
            var selected = window == null ? null : scene.Entries(window).ElementAtOrDefault(window.Item);
            if (window?.App == "code" && window.Focus == "explorer" && selected != null)
            {
                string path = scene.EditorLessonRelativePath(selected.Path);
                if (task.Action == "file-paste") return task with
                {
                    Title = "Вставьте файл «" + System.IO.Path.GetFileName(scene.Clipboard) + "» в папку «" + path + "»",
                    Hint = "Фокус должен быть в проводнике проекта. После вставки проверьте путь, а не только открытую вкладку.",
                    TargetPath = selected.Path
                };
                return task with
                {
                    Title = (task.Action == "file-cut" ? "Вырежьте файл «" : "Скопируйте файл «") + path + "» в проводнике",
                    Hint = "Ctrl+Shift+E вернёт фокус в проводник. Затем выберите строку файла, не открывая его снова.",
                    TargetPath = selected.Path
                };
            }
        }
        if (task.Action == "item-open")
        {
            var item = scene.Entries(scene.Active).ElementAtOrDefault(scene.Active.Item);
            if (item?.Kind == "reveal") return task with {
                Title = "Покажите «" + System.IO.Path.GetFileName(item.Path) + "» в папке загрузок",
                Hint = "Здесь Chrome откроет папку с выбранным файлом. Переименование делается в файловом менеджере." };
            if (item != null) return scene.DescribeDesktopSearchTask(task with { Title = (item.Kind == "download" ? "Скачайте" : "Откройте") + " «" + item.Title + "»" });
        }
        return scene.DescribeDesktopSearchTask(scene.DescribeChecklistTask(scene.DescribeBlockTask(scene.DescribeSymbolTarget(task))));
    }

    private static bool Demonstrate(LabTask task, LabScene trial)
    {
        trial.DemonstrateTabReview(task);
        if (task.Action == "checkout-expect")
        {
            trial.Active.Input = task.Text;
            return trial.Text("Enter", "");
        }
        if (task.Action == "file-search-query") return trial.Text("Text", task.Text);
        if (task.Action == "file-search-verify")
        {
            trial.Insert(trial.Active, task.Text);
            return trial.Text("Enter", "");
        }
        if (trial.DemonstrateTraceback(task) is bool trace) return trace;
        if (trial.CompletingMovedReport && task.Action == "save" && task.Text.Length > 0)
        {
            trial.Text("End", "", word: true);
            trial.Insert(trial.Active, task.Text);
        }
        if (trial.NotebookCourse)
        {
            if (task.Action == "notebook-type") return trial.Text("Text", task.Text);
            if (task.Action is "quick-choose" or "palette-choose" && task.Text.Length > 0)
            { trial.Active.Input = task.Text; trial.Active.OptionIndex = 0; }
            if (task.Action == "save" && task.Text.Length > 0)
            {
                if (trial.CsvNotebook && trial.Active.Url.EndsWith("/" + trial.CsvModuleName, StringComparison.Ordinal)) trial.Active.Selected = true;
                trial.Insert(trial.Active, task.Text);
            }
        }
        if (task.Action == "python-edit-write" && trial.BlockRepair) return trial.Text("Text", task.Text);
        if (task.Action.StartsWith("python-edit-", StringComparison.Ordinal)) return trial.PerformPythonEdit(task.Action);
        if (task.Action == "launch") { trial.Ensure(task.Id[5..]); return true; }
        if (task.Action == "gnome-write")
        {
            foreach (char ch in task.Text)
                if (!trial.Text(ch.ToString().ToUpperInvariant(), ch.ToString())) return false;
            return trial.Text("Enter", "");
        }
        if (task.Action == "file-name-commit") trial.Insert(trial.Active, task.Text);
        if (task.Action == "research-find")
        {
            trial.Active.Input = task.Text;
            return trial.Text("Enter", "");
        }
        if (task.Action == "disk-capacity-verdict") return trial.SubmitDiskCapacityVerdict(trial.DiskSpaceExpected);
        if (task.Action == "command")
        {
            trial.TerminalInput = task.Text;
            return trial.Text("Enter", "") && trial.Terminal.LastSuccess;
        }
        if (trial.BrowserPracticeActive && (task.Action is "navigate-pasted" or "code-clone-command") && task.Text.Length > 0)
            trial.Active.Input = task.Text;
        if (trial.ExactEdit && task.Action is "quick-choose" or "exact-find-submit")
            trial.Active.Input = task.Text;
        if (trial.ExactEdit && task.Action == "save") trial.Insert(trial.Active, task.Text);
        if (task.Action == "project-search-submit") trial.Active.Input = task.Text;
        if (task.Action == "project-search-repair") trial.DemonstrateProjectRepair();
        if (task.Action == "debug-window-shot") trial.PrepareCompilerDebugCapture();
        if (task.Action is "debug-doc-submit" or "debug-rewrite")
        {
            foreach (char ch in task.Text)
                if (!trial.Text(ch.ToString().ToUpperInvariant(), ch.ToString())) return false;
            return trial.Text("Enter", "");
        }
        if (task.Action == "code-rename-confirm") trial.Active.Input = LabScene.RenameTarget;
        if (task.Action == "terminal-interrupt")
            while (!trial.Terminal.LogIncidentArrived && trial.Terminal.AppendLogFeedLine()) { }
        if (trial.CsvNotebook && task.Action == "file-paste" && task.Text == "!conflict")
            return !trial.Apply(task.Action) && trial.NotebookConflictExpected();
        if (!LabCatalog.Available(task, trial) || !trial.Apply(task.Action)) return false;
        if (trial.NotebookCourse && task.Action == "save" &&
            !(trial.CsvNotebook && trial.Active.Url.EndsWith("/" + trial.CsvModuleName, StringComparison.Ordinal)) && !trial.NotebookSavedCorrect())
            throw new InvalidOperationException("Маршрут не подтвердил содержимое сохранённого блокнота.");
        if (trial.Switcher) trial.CommitSwitch();
        return true;
    }

    internal static string CsvExplorerProof(LabTask task, LabScene scene) =>
        scene.ScopedCsvExplorerEvidence(task) is object evidence ? JsonSerializer.Serialize(evidence) : null;

    internal static string Evidence(LabTask task, LabScene scene, string equivalentCommand = null, bool verifyReport = false, string artifactPath = null)
    {
        var w = scene.Active;
        string app = w?.App ?? "", action = task.Action;
        if (scene.DiskCapacityEvidence(task) is object disk) return JsonSerializer.Serialize(disk);
        if (scene.DesktopFileSearchEvidence(task) is object desktopSearch) return JsonSerializer.Serialize(desktopSearch);
        if (scene.GuideNoteEvidence(task) is object note) return JsonSerializer.Serialize(note);
        if (scene.TracebackEvidence(task) is object trace) return JsonSerializer.Serialize(trace);
        if (scene.SourceHandoffEvidence(task, equivalentCommand) is object source) return JsonSerializer.Serialize(source);
        if (scene.ProjectFileEvidence(task) is object files) return JsonSerializer.Serialize(files);
        if (scene.NotebookCourse) return JsonSerializer.Serialize(scene.NotebookEvidence(task));
        if (scene.WindowsDesktop && (!scene.BrowserResearchActive || action.StartsWith("win-", StringComparison.Ordinal)))
            return JsonSerializer.Serialize(scene.WindowsDesktopEvidence());
        if (scene.BlockRepair) return JsonSerializer.Serialize(scene.CheckoutEvidence(task, equivalentCommand));
        if (scene.PythonEditingExercise) return JsonSerializer.Serialize(scene.PythonEditEvidence());
        if (scene.GnomePractice is "workspaces" or "same-app")
            return JsonSerializer.Serialize(scene.GnomeScopeEvidence(task, equivalentCommand));
        if (scene.GnomePractice == "matebook")
            return JsonSerializer.Serialize(new { scene.Overview, scene.Layout, scene.LayoutIsVirtual });
        if (scene.GnomeWindows)
            return JsonSerializer.Serialize(new
            {
                scene.ActiveWindow,
                windows = scene.Windows.Select(v => new
                {
                    v.Id, v.App, v.Workspace, v.State, v.NormalSlot,
                    restoreState = v.State == "minimized" ? v.MinimizedFrom : null,
                    v.ActiveTab, v.Url, v.Focus,
                    caret = v.Tab?.Position.Caret,
                    anchor = v.Tab?.Position.Anchor,
                    body = v.App == "code" ? v.Body : null,
                    saved = v.App == "code" ? v.Tab?.SavedBody : null
                }).ToArray()
            });
        object value = scene.ChromeTabEvidence(task, w) ?? scene.BrowserPracticeEvidence(task, w) ?? scene.ExactEditEvidence(task) ?? scene.DebugEvidence(action) ?? (action switch
        {
            _ when action.StartsWith("project-search-", StringComparison.Ordinal) => scene.ProjectSearchEvidence(action),
            _ when action == "code-problem-next" || action.StartsWith("code-fix-", StringComparison.Ordinal) => scene.DiagnosticEvidence(),
            _ when action.StartsWith("code-", StringComparison.Ordinal) => scene.SymbolEvidence(action),
            "command" or "terminal-submit" => new { command = equivalentCommand ?? scene.Terminal.LastCommand, scene.Terminal.LastSuccess, scene.Terminal.Cwd, scene.Terminal.LastOutput, input = scene.TerminalInput, report = verifyReport ? scene.ReadReportArtifact() : null },
            "terminal-history-up" or "terminal-history-down" or "terminal-erase" or "terminal-kill" or "terminal-yank" or "word-left" or "word-right" or "terminal-home" or "terminal-end" or "terminal-cancel" => new { scene.TerminalInput, w?.Cursor },
            "terminal-clear" => scene.Terminal.History.Count(),
            "terminal-interrupt" => scene.TerminalInterruptEvidence(),
            "overview" => scene.Overview, "desktop" => scene.ShowDesktop, "layout" => scene.Layout,
            "screenshot" => scene.Screenshot, "screenshot-confirm" => scene.Screenshots,
            "open-chrome" or "open-code" or "open-terminal" or "open-files" or "launch" or "switch-next" or "switch-prev" => app,
            "close-window" or "minimize" => scene.Windows.Select(v => new { v.App, v.State, v.Workspace }).ToArray(),
            "maximize" or "restore" or "tile-left" or "tile-right" or "fullscreen" => new { app, w?.State },
            "workspace-next" or "workspace-prev" or "move-next" or "move-prev" => new { scene.Workspace, windows = scene.Windows.Select(v => new { v.App, v.Workspace }).ToArray() },
            "item-next" or "item-prev" => w == null ? null : new { app, w.Folder, Url = scene.BrowserGoalAddress(w.Url), item = scene.Entries(w).ElementAtOrDefault(w.Item)?.Path },
            "file-parent" or "file-home" or "file-back" or "file-forward" => new { app, w?.Folder },
            "file-cut" or "file-copy" => scene.Clipboard,
            "file-paste" or "trash" or "restore-trash" => w == null ? null : new { w.Folder, items = scene.Entries(w).Select(e => e.Path).ToArray() },
            "item-open" => new { app, w?.Folder, w?.Url, scene.DownloadCount },
            "url" or "select-all" or "select-line" or "occurrence" => new { app, w?.Focus, w?.Url, w?.Cursor, w?.Anchor, matches = w?.Tab?.ExtraMatches.Count },
            "quick-next" or "symbol-next" => new { w?.Focus, item = w == null ? "" : scene.Entries(w).ElementAtOrDefault(w.OptionIndex)?.Path },
            "quick-choose" or "symbol-open" => new { w?.Focus, w?.Url, w?.Cursor },
            "copy" or "terminal-copy" => new { scene.Clipboard },
            "undo" or "comment" or "delete-line" when w?.Url == "/home/student/project/line-edit.py" =>
                new { app, w.Focus, w.Url, w.Body, w.Anchor, w.Cursor },
            "cut" or "paste" or "undo" or "redo" or "comment" or "delete-line" or "move-line-up" or "move-line-down" => new { app, w?.Focus, w?.Url, w?.Input, w?.Body, scene.TerminalInput },
            "replace-field" or "replace-one" => new { w?.Focus, w?.Body, w?.Find },
            "zoom-in" or "zoom-out" or "zoom-reset" => new { w?.Url, w?.Zoom },
            "bookmark" => w?.SavedBookmarks.Order().ToArray(), "bookmarks" => w?.Bookmarks,
            "new-tab" or "close-tab" or "restore-tab" => new { app, w?.ActiveTab, tabs = w?.Tabs.Select(tab => tab.Url).ToArray() },
            "next-tab" or "prev-tab" or "first-tab" or "last-tab" => new { app, w?.Url, w?.ActiveTab },
            "back" or "forward" or "navigate-pasted" => new { w?.Focus, w?.Url, w?.ActiveTab },
            "history" when LabScene.OnChromeHistory(w) => new { focus = w.Focus is "body" or "history" ? "history" : w.Focus, w.Url },
            "history" or "downloads" or "bookmark-manager" => new { w?.Focus, w?.Url },
            "panel" or "terminal-toggle" => new { w?.Panel, w?.Focus }, "sidebar" => w?.Sidebar, "split" or "focus-editor" or "focus-second" => new { w?.Split, w?.Group, w?.Url, w?.Focus },
            "save" => new { w?.Url, w?.Tab?.SavedBody },
            _ => new { app, w?.Focus, w?.OptionIndex, w?.Url, w?.Cursor, w?.View, w?.Find, scene.Overview, scene.ScreenshotReady }
        });
        // A matching terminal display can omit trailing newlines. The artifact
        // still has to be the exact saved bytes expected for this exercise.
        return JsonSerializer.Serialize(artifactPath == null ? value :
            scene.DebugDay ? new { state = value, file = scene.Terminal.ReadFile(artifactPath), proof = scene.DebugReportValid } :
            new { state = value, file = scene.Terminal.ReadFile(artifactPath) });
    }
}
