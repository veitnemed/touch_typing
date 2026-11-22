using System;
using System.IO;
using System.Text;
using System.Windows.Automation;

namespace TouchTypingStudio
{
    internal sealed class RouteSession
    {
        internal static readonly RouteSpec[] Routes = RouteCatalog.Routes;

        public readonly RouteSpec Spec;
        public readonly int Stage;
        public readonly string Folder;
        public string Notice = "";
        public bool Passed { get; private set; }
        public int Step { get; private set; }
        public IntPtr FromWindow, ToWindow;

        private readonly string label, token, sample, ready, en, ru, newFolder, oldWord, trialWord, finalWord;
        private readonly string root;
        private string file, copies, original, trial, expected, expectedOutput, receipt, childFolder, secondFile;
        private bool sawChord, didUndo, didFindAgain, noVerification;
        private bool addressSubmitted, paletteConfirmed;
        private bool layoutChord, englishLine;
        private IntPtr layoutTarget;
        private IntPtr closeTarget;
        private IntPtr previousNotepad;
        private IntPtr startOrigin;
        private bool parentCandidate, sawStartMenu;
        private DateTime folderAction;
        private bool firstSnapped, secondSnapped;
        private readonly StringBuilder typedLine = new StringBuilder();
        public bool ExplorerWarmup { get; private set; }
        public bool HasSampleWindows { get { return Spec.Kind == RouteKind.Switch || Spec.Kind == RouteKind.Snap; } }
        public bool IsSystemShortcut { get { return Spec.SystemChord != Chord.None; } }
        private DateTime actionAt;

        public RouteSession(RouteSpec spec, int stage, string root)
        {
            Spec = spec;
            Stage = stage;
            this.root = root;
            token = Guid.NewGuid().ToString("N");
            label = "lesson-" + token.Substring(0, 7);
            Folder = Path.Combine(root, "Упражнения", label);
            sample = new[] { "sample", "notes", "draft" }[stage];
            ready = new[] { "ready", "checked", "result" }[stage];
            en = new[] { "ok", "word", "copy" }[stage];
            ru = new[] { "лава", "вода", "два" }[stage];
            newFolder = new[] { "Practice", "Review", "Ready" }[stage];
            oldWord = new[] { "draft", "wait", "old" }[stage];
            trialWord = new[] { "test", "temp", "trial" }[stage];
            finalWord = new[] { "ready", "done", "saved" }[stage];
            if (!IsSystemShortcut)
            {
                Directory.CreateDirectory(Folder);
                Prepare();
            }
            if (spec.Kind == RouteKind.Explorer) Notice = "F6 в тренажёре — скопировать адрес. Затем Win+E.";
            if (IsSystemShortcut) Notice = "Выполните действие из окна тренажёра.";
        }

        private void Prepare()
        {
            switch (Spec.Kind)
            {
                case RouteKind.Layout:
                    file = Path.Combine(Folder, "layout-" + label + ".txt");
                    expected = en + "\n" + ru;
                    Write(file, "");
                    break;
                case RouteKind.Rename:
                    file = Path.Combine(Folder, sample + ".txt");
                    copies = Path.Combine(Folder, "Copies");
                    Directory.CreateDirectory(copies);
                    original = "touch-typing-" + token;
                    Write(file, original);
                    break;
                case RouteKind.Edit:
                    file = Path.Combine(Folder, "edit-" + label + ".txt");
                    original = "status: " + oldWord + "\nkeep: yes\n";
                    trial = original.Replace(oldWord, trialWord);
                    expected = original.Replace(oldWord, finalWord);
                    Write(file, original);
                    break;
                case RouteKind.CloseWindow:
                    file = Path.Combine(Folder, "close-" + label + ".txt");
                    Write(file, "Учебное окно для Alt+F4.\n");
                    break;
                case RouteKind.SelectText:
                    file = Path.Combine(Folder, "replace-" + label + ".txt");
                    expected = new[] { "file ready", "check done", "note saved" }[Stage];
                    Write(file, "sample text\nreplace this line\n");
                    break;
                case RouteKind.NavigateWords:
                    file = Path.Combine(Folder, "words-" + label + ".txt");
                    original = "alpha " + oldWord + " omega\n";
                    expected = "alpha " + finalWord + " omega\n";
                    Write(file, original);
                    break;
                case RouteKind.FolderHistory:
                    childFolder = Path.Combine(Folder, "Notes");
                    Directory.CreateDirectory(childFolder);
                    Write(Path.Combine(childFolder, sample + ".txt"), "Учебный файл\n");
                    break;
                case RouteKind.MoveFile:
                    file = Path.Combine(Folder, sample + ".txt");
                    copies = Path.Combine(Folder, "Archive");
                    Directory.CreateDirectory(copies);
                    original = "move-" + token;
                    Write(file, original);
                    break;
                default:
                    if (Spec.InCode) PrepareCode();
                    break;
            }
        }

        private void PrepareCode()
        {
            var runtime = RuntimeTools.Current;
            if (runtime.Code == null || Spec.Kind == RouteKind.Python && runtime.Python == null)
                throw new InvalidOperationException("Для этого маршрута нужны VS Code" + (Spec.Kind == RouteKind.Python ? " и Python." : "."));
            file = Path.Combine(Folder, sample + ".py");
            if (Spec.Kind != RouteKind.Python)
            {
                PrepareCodeEdit();
                return;
            }
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "code-lesson", "package.json")))
                throw new InvalidOperationException("Папка code-lesson должна находиться рядом с тренажёром.");
            int value = 2 + Stage, result = Stage == 0 ? 7 : value * (Stage + 2);
            if (Stage == 0)
            {
                original = "a = 4\nj = 6\nprint(j)\n";
                expected = "a = 4\nj = 7\nprint(j)\n";
            }
            else
            {
                string function = Stage == 1 ? "triple" : "scale";
                original = "def " + function + "(value):\n    return value\n\nprint(" + function + "(" + value + "))\n";
                expected = original.Replace("    return value\n", "    return value * " + (Stage + 2) + "\n");
            }
            expectedOutput = result + "\n";
            receipt = Path.Combine(Folder, ".checked-run.txt");
            Write(file, original);
            Write(Path.Combine(Folder, ".lesson-token"), token);
        }

        private int CodeLineNumber { get { return 3 + Stage; } }
        private int OldNumber { get { return 4 + Stage; } }
        private int NewNumber { get { return 7 + Stage; } }
        private string CodeWord { get { return new[] { "count", "size", "index" }[Stage]; } }
        private string CodeReplacement { get { return new[] { "total", "length", "slot" }[Stage]; } }
        private string FileName { get { return Path.GetFileName(file); } }

        private void PrepareCodeEdit()
        {
            switch (Spec.Kind)
            {
                case RouteKind.CodeLine:
                    original = new string('\n', CodeLineNumber - 2) + "# settings\n" + CodeWord + " = " + OldNumber + "\n";
                    expected = original.Replace(" = " + OldNumber, " = " + NewNumber);
                    break;
                case RouteKind.CodeComment:
                    string row = "print(" + OldNumber + ")";
                    original = row + "\nvalue = " + OldNumber + "\n";
                    expected = "# " + original;
                    break;
                case RouteKind.CodeReplace:
                    original = CodeWord + " = " + OldNumber + "\nprint(" + CodeWord + ")\n" + CodeWord + " += 1\n";
                    expected = original.Replace(CodeWord, CodeReplacement);
                    break;
                case RouteKind.CodeMoveLine:
                    string a = "left = " + OldNumber + "\n", b = "right = " + NewNumber + "\n";
                    original = a + b + "print(left, right)\n";
                    expected = b + a + "print(left, right)\n";
                    break;
                case RouteKind.CodeTabs:
                    secondFile = Path.Combine(Folder, ready + ".py");
                    Write(secondFile, "right = " + NewNumber + "\n");
                    original = "left = " + OldNumber + "\n";
                    break;
                default:
                    original = CodeWord + " = " + OldNumber + "\n";
                    break;
            }
            Write(file, original);
        }

        public string Goal()
        {
            switch (Spec.Kind)
            {
                case RouteKind.Layout:
                    return "Сохраните в Блокноте две строки: «" + en + "» на EN и «" + ru + "» на RU.";
                case RouteKind.Explorer:
                    return "Откройте указанный путь в Проводнике и создайте папку " + newFolder + ".";
                case RouteKind.Rename:
                    return "Переименуйте " + sample + " в " + ready + ". Поместите копию файла в Copies.";
                case RouteKind.Edit:
                    return "Найдите «" + oldWord + "». Сохраните замену «" + trialWord + "», отмените её и сохраните. Затем замените исходное слово на «" + finalWord + "» и сохраните.";
                case RouteKind.Python:
                    if (Stage == 0) return "Откройте sample.py. Замените 6 на 7 в строке j = 6. Сохраните и запустите учебный Python.";
                    return "Откройте " + sample + ".py. Замените return value на return value * " + (Stage + 2)
                        + ". Сохраните и запустите учебный Python.";
                case RouteKind.StartMenu:
                    return "Откройте Блокнот через меню «Пуск». Когда он откроется, вернитесь в тренажёр через Alt+Tab.";
                case RouteKind.CloseWindow:
                    return "Закройте учебный Блокнот «" + Path.GetFileNameWithoutExtension(file) + "».";
                case RouteKind.SelectText:
                    return "Замените весь текст в учебном Блокноте на «" + expected + "» и сохраните.";
                case RouteKind.NavigateWords:
                    return "Замените «" + oldWord + "» на «" + finalWord + "» в строке alpha " + oldWord + " omega. Сохраните файл.";
                case RouteKind.Snap:
                    return "Разместите учебное окно «" + SnapLabel(false) + "» слева, а «" + SnapLabel(true) + "» справа.";
                case RouteKind.FolderHistory:
                    return "Из Notes поднимитесь в учебную папку. Затем по истории вернитесь в Notes и снова в учебную папку.";
                case RouteKind.MoveFile:
                    return "Перенесите " + FileName + " в Archive. В исходной папке файл должен исчезнуть.";
                case RouteKind.CodeLine:
                    return "В " + FileName + " на строке " + CodeLineNumber + " замените " + OldNumber + " на " + NewNumber + " и сохраните.";
                case RouteKind.CodeComment:
                    return "В " + FileName + " закомментируйте первую строку с print() и сохраните файл.";
                case RouteKind.CodeReplace:
                    return "В " + FileName + " замените все «" + CodeWord + "» на «" + CodeReplacement + "» и сохраните.";
                case RouteKind.CodeMoveLine:
                    return "В " + FileName + " поменяйте местами первые две строки и сохраните. Строка right должна быть первой.";
                case RouteKind.CodeTabs:
                    return "Откройте " + FileName + " и " + Path.GetFileName(secondFile) + ". Из второго редактора вернитесь к первому через переключатель вкладок.";
                case RouteKind.CodeReopen:
                    return "Откройте " + FileName + ", закройте вкладку и восстановите её.";
                default: return Spec.Goal;
            }
        }

        public string[] Hints()
        {
            switch (Spec.Kind)
            {
                case RouteKind.Switch:
                    return new[] { "В первом окне нажмите Alt+Tab. Удерживайте Alt, пока выбираете второе учебное окно." };
                case RouteKind.Layout:
                    return new[] { "Выберите EN через Win+Space, если нужна смена. Введите «" + en + "», затем Enter.",
                        "Win+Space — выбрать RU. Введите «" + ru + "» и нажмите Ctrl+S. Подойдёт и настроенное в Windows Alt+Shift." };
                case RouteKind.Explorer:
                    return new[] { "Win+E — открыть Проводник.",
                        "Ctrl+L — строка адреса. Вставьте учебный путь через Ctrl+V, затем Enter.",
                        "Ctrl+Shift+N — новая папка. Введите " + newFolder + " и нажмите Enter." };
                case RouteKind.Rename:
                    return new[] { "Tab — перейти к списку файлов. Стрелками выберите " + sample + ". Включите EN для имени файла.",
                        "F2 — переименовать. При необходимости нажмите Fn+F2. Введите " + ready + " и нажмите Enter. Сохраните расширение .txt.",
                        "Выберите переименованный файл. Ctrl+C. Откройте Copies стрелками и Enter. Нажмите Ctrl+V." };
                case RouteKind.Edit:
                    return new[] { "На EN нажмите Ctrl+Home, затем Ctrl+F. Введите «" + oldWord + "», Enter, Esc. Напечатайте «" + trialWord + "» и нажмите Ctrl+S.",
                        "Ctrl+Z, затем Ctrl+S. Дождитесь перехода к следующему шагу.",
                        "Ctrl+Home, Ctrl+F. Найдите «" + oldWord + "» снова. Enter, Esc, введите «" + finalWord + "», Ctrl+S." };
                case RouteKind.Python:
                    return new[] { "На EN нажмите Ctrl+P, введите " + sample + ".py и нажмите Enter.",
                        Stage == 0 ? "Ctrl+Home, ↓, End. Замените последнюю цифру 6 на 7 и нажмите Ctrl+S." :
                            "На строке return value нажмите End и допишите  * " + (Stage + 2) + ". Нажмите Ctrl+S.",
                        "Ctrl+Shift+P. Найдите Python Lesson: Run Exercise и нажмите Enter. Вывод появится в панели Python Lesson." };
                case RouteKind.StartMenu:
                    return new[] { "Откройте меню «Пуск» клавишей Win. Введите «Блокнот» или notepad и нажмите Enter. После открытия вернитесь через Alt+Tab.",
                        "Подходит и Ctrl+Esc для открытия меню «Пуск»." };
                case RouteKind.CloseWindow:
                    return new[] { "Убедитесь, что активен учебный Блокнот. Нажмите Alt+F4." };
                case RouteKind.RunDialog:
                    return new[] { "В тренажёре нажмите Win+R.", "Когда появится отметка об открытии окна «Выполнить», нажмите Esc." };
                case RouteKind.WindowsSettings:
                    return new[] { "В тренажёре нажмите Win+I. Дождитесь окна «Параметры»." };
                case RouteKind.TaskManager:
                    return new[] { "В тренажёре нажмите Ctrl+Shift+Esc. Дождитесь окна Диспетчера задач." };
                case RouteKind.SelectText:
                    return new[] { "На EN нажмите Ctrl+A, введите «" + expected + "», затем Ctrl+S." };
                case RouteKind.NavigateWords:
                    return new[] { "Ctrl+Home, Ctrl+→. Выберите слово через Ctrl+Shift+→ и введите «" + finalWord + " » с пробелом.",
                        "Проверьте, что между словами по одному пробелу. Нажмите Ctrl+S." };
                case RouteKind.Snap:
                    return new[] { "В первом учебном окне нажмите Win+←. При появлении выбора соседнего окна нажмите Esc.",
                        "Переключитесь на второе через Alt+Tab и нажмите Win+→. Проверьте, что оба окна видны." };
                case RouteKind.FolderHistory:
                    return new[] { "Alt+↑ — подняться из Notes в учебную папку.", "Alt+← — вернуться в Notes. Alt+→ — снова перейти вперёд." };
                case RouteKind.MoveFile:
                    return new[] { "Через Tab и стрелки выделите " + FileName + ". Нажмите Ctrl+X.",
                        "Откройте Archive стрелками и Enter. Нажмите Ctrl+V." };
                case RouteKind.CodeLine:
                    return CodeHints("Ctrl+G, введите " + CodeLineNumber + " и нажмите Enter. Нажмите End, замените последнюю цифру на " + NewNumber + " и сохраните Ctrl+S.");
                case RouteKind.CodeComment:
                    return CodeHints("На EN нажмите Ctrl+Home, затем Ctrl+/. Сохраните через Ctrl+S.");
                case RouteKind.CodeReplace:
                    return CodeHints("Ctrl+H, введите " + CodeWord + ". Нажмите Tab к полю замены, введите " + CodeReplacement +
                        ". Ctrl+Alt+Enter заменяет все совпадения. Esc и Ctrl+S.");
                case RouteKind.CodeMoveLine:
                    return CodeHints("Ctrl+Home, Alt+↓ и Ctrl+S.");
                case RouteKind.CodeTabs:
                    return CodeHints("Через Ctrl+P откройте " + Path.GetFileName(secondFile) + ". Ctrl+Tab возвращает к предыдущему редактору.");
                case RouteKind.CodeReopen:
                    return CodeHints("Закройте вкладку через Ctrl+W и верните её через Ctrl+Shift+T.");
                default: return new string[0];
            }
        }

        private string[] CodeHints(string next) { return new[] { "На EN нажмите Ctrl+P, введите " + FileName + " и нажмите Enter.", next }; }

        public string SnapLabel(bool right)
        {
            var labels = new[] { "Заметки", "Редактор", "Справка", "Вывод", "Проект", "Пример" };
            return labels[Stage * 2 + (right ? 1 : 0)];
        }

        public void OpenTargets()
        {
            switch (Spec.Kind)
            {
                case RouteKind.Layout:
                case RouteKind.Edit:
                case RouteKind.CloseWindow:
                case RouteKind.SelectText:
                case RouteKind.NavigateWords:
                    RuntimeTools.OpenApp(Path.Combine(Environment.SystemDirectory, "notepad.exe"), RuntimeTools.Quote(file));
                    break;
                case RouteKind.Rename:
                case RouteKind.FolderHistory:
                case RouteKind.MoveFile:
                    RuntimeTools.OpenApp(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                        RuntimeTools.Quote(Spec.Kind == RouteKind.FolderHistory ? childFolder : Folder));
                    break;
                default:
                    if (Spec.InCode) OpenCode();
                    break;
            }
        }

        public void OpenExplorerWarmup()
        {
            if (Spec.Kind != RouteKind.Explorer || Step != 0) return;
            RuntimeTools.OpenApp(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "");
            ExplorerWarmup = true;
            Observe();
        }

        private void OpenCode()
        {
            var runtime = RuntimeTools.Current;
            string extension = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "code-lesson");
            string profile = Path.Combine(root, "code-profile");
            string userConfig = Path.Combine(profile, "User", "settings.json");
            if (!File.Exists(userConfig))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(userConfig));
                Write(userConfig, @"{
  ""workbench.startupEditor"": ""none"",
  ""workbench.welcomePage.experimentalOnboarding"": false,
  ""workbench.secondarySideBar.defaultVisibility"": ""hidden"",
  ""chat.disableAIFeatures"": true,
  ""editor.formatOnSave"": false,
  ""files.autoSave"": ""off""
}");
            }
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = runtime.Code, UseShellExecute = false, WorkingDirectory = Folder,
                Arguments = "--new-window --user-data-dir " + RuntimeTools.Quote(profile) +
                    " --extensions-dir " + RuntimeTools.Quote(Path.Combine(root, "code-empty-extensions")) +
                    " --extensionDevelopmentPath=" + RuntimeTools.Quote(extension) + " " + RuntimeTools.Quote(Folder)
            };
            if (runtime.Python != null) start.EnvironmentVariables["TOUCH_TYPING_PYTHON"] = runtime.Python;
            start.EnvironmentVariables["TOUCH_TYPING_PRACTICE_ROOT"] = Path.GetDirectoryName(Folder);
            using (System.Diagnostics.Process.Start(start)) { }
        }

        public void Key(InputChord e)
        {
            if (Passed) return;
            if (IsSystemShortcut)
            {
                if (Step == 0 && e.Chord == Spec.SystemChord && e.Window == FromWindow) Observe();
                if (Step == 1 && e.Chord == Chord.Dismiss && e.Window == ToWindow) Observe();
                return;
            }
            if (Spec.Kind == RouteKind.Switch)
            {
                if (e.Chord == Chord.SwitchWindow && e.Window == FromWindow) Observe();
                return;
            }
            if (Spec.Kind == RouteKind.Snap)
            {
                if (e.Chord == Chord.SnapLeft && e.Window == FromWindow) firstSnapped = true;
                if (e.Chord == Chord.SnapRight && e.Window == ToWindow) secondSnapped = true;
                return;
            }
            if (Spec.Kind == RouteKind.StartMenu)
            {
                if (e.Chord == Chord.StartApp && Step == 0)
                {
                    startOrigin = e.Window;
                    sawStartMenu = false;
                    previousNotepad = Native.IsProcess(e.Window, "notepad") ? e.Window : IntPtr.Zero;
                    Observe();
                }
                if (e.Chord == Chord.SwitchWindow && e.Window == ToWindow && Step == 1) Observe();
                return;
            }
            if (Spec.Kind == RouteKind.CloseWindow)
            {
                if (e.Chord == Chord.CloseWindow && e.Window == closeTarget) Observe();
                return;
            }
            if (Spec.Kind == RouteKind.Explorer && Step == 0)
            {
                if (e.Chord == Chord.OpenExplorer) Observe();
                return;
            }
            if (!IsTarget(e.Window)) return;

            switch (Spec.Kind)
            {
                case RouteKind.Layout: TrackLayout(e); break;
                case RouteKind.Explorer:
                    if (Step == 1 && e.Chord == Chord.Address)
                    {
                        addressSubmitted = false;
                        Observe();
                    }
                    if (Step == 1 && sawChord && e.Chord == Chord.Confirm) addressSubmitted = true;
                    if (Step == 2 && e.Chord == Chord.NewFolder && InFolder(e.Window, Folder)) Observe();
                    break;
                case RouteKind.Rename:
                    if (Step == 0 && e.Chord == Chord.Arrow && InFolder(e.Window, Folder)) Advance();
                    if (Step == 0 && e.Chord == Chord.Rename)
                        Notice = "Сначала выберите учебный файл стрелками, затем нажмите F2.";
                    if (Step == 1 && e.Chord == Chord.Rename && InFolder(e.Window, Folder)) Observe();
                    if (Step == 2 && e.Chord == Chord.Copy && InFolder(e.Window, Folder)) Advance();
                    if (Step == 3 && e.Chord == Chord.Paste && InFolder(e.Window, copies)) Observe();
                    break;
                case RouteKind.Edit:
                    if (Step == 0 && e.Chord == Chord.Find) Advance();
                    if (Step == 2 && e.Chord == Chord.Undo) didUndo = true;
                    if (Step == 3 && e.Chord == Chord.Find) didFindAgain = true;
                    if ((Step == 1 || Step == 2 && didUndo || Step == 3 && didFindAgain) && e.Chord == Chord.Save) Observe();
                    break;
                case RouteKind.Python:
                    if (Step == 0 && e.Chord == Chord.QuickOpen) Observe();
                    if (Step == 1 && e.Chord == Chord.Save && InPythonDocument(e.Window)) Observe();
                    if (Step == 2 && e.Chord == Chord.Palette && InPythonDocument(e.Window))
                    {
                        paletteConfirmed = false;
                        Observe();
                    }
                    if (Step == 2 && sawChord && e.Chord == Chord.Confirm) paletteConfirmed = true;
                    break;
                case RouteKind.SelectText:
                    if (Step == 0 && e.Chord == Chord.SelectAll) Advance();
                    if (Step == 1 && e.Chord == Chord.Save) Observe();
                    break;
                case RouteKind.NavigateWords:
                    if (Step == 0 && e.Chord == Chord.MoveWord) Advance();
                    if (Step == 1 && e.Chord == Chord.Save) Observe();
                    break;
                case RouteKind.FolderHistory:
                    if (Step == 0 && e.Chord == Chord.ParentFolder && parentCandidate) Observe();
                    if (Step == 1 && e.Chord == Chord.BackFolder) Observe();
                    if (Step == 2 && e.Chord == Chord.ForwardFolder) Observe();
                    break;
                case RouteKind.MoveFile:
                    if (Step == 0 && e.Chord == Chord.Cut && InFolder(e.Window, Folder)) Advance();
                    if (Step == 1 && e.Chord == Chord.Paste && InFolder(e.Window, copies)) Observe();
                    break;
                default:
                    if (Spec.InCode) TrackCode(e);
                    break;
            }
        }

        private bool codeAction, replaced;

        private void TrackCode(InputChord e)
        {
            if (Step == 0 && e.Chord == Chord.QuickOpen) { Observe(); return; }
            if (Spec.Kind == RouteKind.CodeTabs)
            {
                if (Step == 1 && e.Chord == Chord.QuickOpen) Observe();
                if (Step == 2 && e.Chord == Chord.SwitchEditor && InDocument(e.Window, secondFile)) Observe();
                return;
            }
            if (Spec.Kind == RouteKind.CodeReopen)
            {
                if (Step == 1 && e.Chord == Chord.CloseEditor && InPythonDocument(e.Window)) Observe();
                if (Step == 2 && e.Chord == Chord.ReopenEditor) Observe();
                return;
            }
            if (Step != 1 || !InPythonDocument(e.Window)) return;
            Chord required = Spec.Kind == RouteKind.CodeLine ? Chord.GoLine :
                Spec.Kind == RouteKind.CodeComment ? Chord.Comment :
                Spec.Kind == RouteKind.CodeReplace ? Chord.Replace : Chord.MoveLine;
            if (e.Chord == required) { codeAction = true; replaced = false; }
            if (Spec.Kind == RouteKind.CodeReplace && codeAction && e.Chord == Chord.ReplaceAll) replaced = true;
            if (e.Chord == Chord.Save && codeAction && (Spec.Kind != RouteKind.CodeReplace || replaced)) Observe();
        }

        private void TrackLayout(InputChord e)
        {
            if (Step == 0 && e.Chord == Chord.SwitchLayout && e.Layout == Layout.English)
            {
                layoutChord = true;
                if (englishLine) Observe();
            }
            if (e.Chord == Chord.ExerciseCharacter && Step == 0 && e.Layout == Layout.English)
            {
                if (e.Character == '\n')
                {
                    englishLine = typedLine.ToString() == en;
                    typedLine.Clear();
                }
                else if (e.Character == '\b')
                {
                    if (typedLine.Length > 0) typedLine.Length--;
                }
                else if (typedLine.Length < 32) typedLine.Append(e.Character);
            }
            if (Step == 1 && e.Chord == Chord.Save && e.Layout == Layout.Russian) Observe();
        }

        public bool IsLayoutExerciseWindow(IntPtr hwnd) { return Step == 0 && hwnd != IntPtr.Zero && hwnd == layoutTarget; }

        private bool IsTarget(IntPtr hwnd)
        {
            switch (Spec.Kind)
            {
                case RouteKind.Layout:
                case RouteKind.Edit:
                case RouteKind.CloseWindow:
                case RouteKind.SelectText:
                case RouteKind.NavigateWords:
                    return Native.IsProcess(hwnd, "notepad") && Native.Title(hwnd).IndexOf(
                        Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase) >= 0;
                case RouteKind.Explorer:
                case RouteKind.Rename:
                case RouteKind.FolderHistory:
                case RouteKind.MoveFile:
                    return Native.IsExplorer(hwnd);
                default:
                    if (Spec.InCode) return Native.IsProcess(hwnd, "Code") && Native.Title(hwnd).IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0;
                    return hwnd == FromWindow || hwnd == ToWindow;
            }
        }

        private bool InFolder(IntPtr hwnd, string folder)
        {
            string actual = Native.ExplorerFolder(hwnd);
            if (string.IsNullOrEmpty(actual))
            {
                noVerification = true;
                Notice = "Windows пока не сообщает путь Проводника. Откройте указанную папку и повторите шаг.";
                return false;
            }
            if (!Path.IsPathRooted(actual)) return false;
            return string.Equals(Path.GetFullPath(actual).TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private bool InPythonDocument(IntPtr hwnd)
        { return InDocument(hwnd, file); }

        private static bool InDocument(IntPtr hwnd, string path)
        { return Native.Title(hwnd).IndexOf(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) >= 0; }

        public void Validate(IntPtr active)
        {
            if (Passed) return;
            if (IsSystemShortcut)
            {
                ValidateSystemShortcut(active);
                return;
            }
            if (Spec.Kind == RouteKind.Layout && layoutTarget != active && IsTarget(active))
                layoutTarget = active;
            if (Spec.Kind == RouteKind.Switch)
            {
                if (sawChord && active == ToWindow) Pass();
                else if (sawChord && active != FromWindow && DateTime.UtcNow - actionAt > TimeSpan.FromMilliseconds(600))
                {
                    sawChord = false;
                    Notice = "Активно другое окно. Вернитесь в первое и повторите Alt+Tab.";
                }
                else if (active == FromWindow) Notice = "Первое окно активно. Переключитесь на второе.";
                else Notice = "Вернитесь в первое учебное окно, затем перейдите во второе через Alt+Tab.";
                return;
            }
            if (Spec.Kind == RouteKind.Snap)
            {
                if (firstSnapped && secondSnapped && Native.AtScreenHalf(FromWindow, true) && Native.AtScreenHalf(ToWindow, false)) Pass();
                else Notice = firstSnapped ? "Проверьте правое учебное окно." : "Начните с левого учебного окна.";
                return;
            }
            if (Spec.Kind == RouteKind.CloseWindow && sawChord && closeTarget != IntPtr.Zero && !Native.IsWindow(closeTarget))
            {
                Pass();
                return;
            }
            if (Spec.Kind == RouteKind.CloseWindow && IsTarget(active)) closeTarget = active;
            if (Spec.Kind == RouteKind.StartMenu)
            {
                bool isNotepad = Native.IsProcess(active, "notepad");
                if (Step == 0 && sawChord && active != startOrigin && active != IntPtr.Zero && !isNotepad)
                    sawStartMenu |= Native.IsProcess(active, "StartMenuExperienceHost") || Native.IsProcess(active, "SearchApp") ||
                        Native.IsProcess(active, "SearchUI") || Native.IsProcess(active, "ShellExperienceHost");
                if (Step == 0 && sawChord && sawStartMenu && isNotepad && active != previousNotepad)
                {
                    ToWindow = active;
                    Advance();
                    Notice = "Блокнот открыт. Вернитесь в тренажёр через Alt+Tab.";
                }
                else if (Step == 1 && sawChord && active == FromWindow) Pass();
                else if (Step == 0) Notice = "Ожидаю запуск Блокнота через меню «Пуск».";
                return;
            }
            if (Spec.Kind == RouteKind.FolderHistory && Step == 0 && !parentCandidate && IsTarget(active) &&
                DateTime.UtcNow >= folderAction)
            {
                folderAction = DateTime.UtcNow.AddMilliseconds(250);
                parentCandidate = InFolder(active, childFolder);
            }
            if (!sawChord)
            {
                if (Step == 0 && Spec.Kind == RouteKind.Layout && layoutChord && !englishLine)
                    Notice = "Сначала наберите английскую строку на EN, нажмите Enter и только затем переключитесь на RU.";
                else if (Step == 2 && Spec.Kind == RouteKind.Edit && didUndo)
                    Notice = "Ctrl+Z получено. Сохраните возвращённый текст через Ctrl+S.";
                else if (Step == 3 && Spec.Kind == RouteKind.Edit && didFindAgain)
                    Notice = "Поиск открыт. Сделайте итоговую замену и сохраните файл.";
                else if (active != IntPtr.Zero && !IsTarget(active) && !(Spec.Kind == RouteKind.Explorer && Step == 0))
                    Notice = "Откройте учебное окно. Сочетания в других окнах не засчитываются.";
                else if (!noVerification) Notice = "Шаг " + (Step + 1) + " · ожидаю действие.";
                return;
            }
            if (!IsTarget(active))
            {
                Notice = "Ожидаю указанное окно и результат действия.";
                return;
            }
            noVerification = false;
            bool good;
            switch (Spec.Kind)
            {
                case RouteKind.Layout:
                    good = Step == 0 ? Native.LayoutAt(active) == Layout.Russian : SameText(file, expected, true);
                    break;
                case RouteKind.Explorer:
                    if (Step == 0) good = Native.IsExplorer(active);
                    else if (Step == 1) good = addressSubmitted && InFolder(active, Folder);
                    else good = InFolder(active, Folder) && Directory.Exists(Path.Combine(Folder, newFolder));
                    break;
                case RouteKind.Rename:
                    string renamed = Path.Combine(Folder, ready + ".txt");
                    if (Step == 1)
                        good = InFolder(active, Folder) && !File.Exists(file) && SameText(renamed, original);
                    else
                        good = InFolder(active, copies) && SameText(renamed, original) &&
                            SameText(Path.Combine(copies, Path.GetFileName(renamed)), original);
                    break;
                case RouteKind.Edit:
                    good = SameText(file, Step == 1 ? trial : Step == 2 ? original : expected);
                    break;
                case RouteKind.Python:
                    good = Step == 0 ? InPythonDocument(active) : Step == 1 ? SameText(file, expected) :
                        paletteConfirmed && SameText(file, expected) && SameText(receipt, token + "\n" + expectedOutput);
                    break;
                case RouteKind.SelectText:
                case RouteKind.NavigateWords:
                    good = Step == 1 && SameText(file, expected, Spec.Kind == RouteKind.SelectText);
                    break;
                case RouteKind.FolderHistory:
                    good = InFolder(active, Step == 1 ? childFolder : Folder);
                    break;
                case RouteKind.MoveFile:
                    good = Step == 1 && InFolder(active, copies) && !File.Exists(file) && SameText(Path.Combine(copies, FileName), original);
                    break;
                default: good = Spec.InCode && VerifyCode(active); break;
            }
            if (good)
            {
                if (Step >= FinalStep) Pass(); else Advance();
            }
            else if (!noVerification)
            {
                if (DateTime.UtcNow - actionAt < TimeSpan.FromMilliseconds(750)) Notice = "Проверяю результат…";
                else if (Spec.Kind == RouteKind.Python && Step == 2)
                    Notice = "Ожидаю вывод. В палитре выберите Python Lesson: Run Exercise.";
                else if (Spec.Kind == RouteKind.Layout && Step == 1)
                    Notice = "Нужны две строки из карточки. Исправьте текст и сохраните файл на RU.";
                else if (Spec.Kind == RouteKind.Edit)
                    Notice = "В сохранённом файле ожидается «" + (Step == 1 ? trialWord : Step == 2 ? oldWord : finalWord) + "».";
                else if (Spec.Kind == RouteKind.Rename && Step == 1)
                    Notice = "Пока не найден файл " + ready + ".txt с исходным содержимым.";
                else if (Spec.Kind == RouteKind.Explorer && Step == 2)
                    Notice = "В учебной папке пока нет подкаталога " + newFolder + ".";
                else if (Spec.Kind == RouteKind.Rename && Step == 3)
                    Notice = "В папке Copies пока нет файла " + ready + ".txt с исходным содержимым.";
                else Notice = "Действие получено. Ожидаемый результат ещё не найден.";
            }
        }

        private void ValidateSystemShortcut(IntPtr active)
        {
            if (Step == 1)
            {
                if (Native.IsWindowVisible(ToWindow)) return;
                if (sawChord) Pass();
                else
                {
                    Step = 0;
                    sawChord = false;
                    ToWindow = IntPtr.Zero;
                    Notice = "Вернитесь в тренажёр и повторите открытие окна «Выполнить».";
                }
                return;
            }
            if (!sawChord) return;
            if (active != FromWindow && SystemShortcutTargetIsOpen(active))
            {
                if (Spec.Kind != RouteKind.RunDialog) Pass();
                else
                {
                    ToWindow = active;
                    Advance();
                    Notice = "Окно «Выполнить» распознано. Закройте его с клавиатуры.";
                }
            }
            else if (DateTime.UtcNow - actionAt > TimeSpan.FromSeconds(10))
            {
                sawChord = false;
                Notice = "Окно не найдено. Вернитесь в тренажёр и повторите сочетание.";
            }
            else
            {
                Notice = "Сочетание получено. Проверяю, открылось ли окно…";
            }
        }

        private bool SystemShortcutTargetIsOpen(IntPtr active)
        {
            switch (Spec.Kind)
            {
                case RouteKind.RunDialog: return Native.IsRunDialog(active);
                case RouteKind.WindowsSettings: return Native.IsSettings(active);
                case RouteKind.TaskManager: return Native.IsProcess(active, "Taskmgr");
                default: return false;
            }
        }

        private bool VerifyCode(IntPtr active)
        {
            if (Step == 0) return InPythonDocument(active);
            if (Spec.Kind == RouteKind.CodeTabs) return Step == 1 ? InDocument(active, secondFile) : InPythonDocument(active);
            if (Spec.Kind == RouteKind.CodeReopen) return Step == 1 ? !InPythonDocument(active) : InPythonDocument(active);
            return SameText(file, expected);
        }

        private int FinalStep
        {
            get
            {
                switch (Spec.Kind)
                {
                    case RouteKind.Edit: case RouteKind.Rename: return 3;
                    case RouteKind.Explorer: case RouteKind.Python: case RouteKind.FolderHistory: case RouteKind.CodeTabs: case RouteKind.CodeReopen: return 2;
                    default: return 1;
                }
            }
        }

        public string EditorStatus(IntPtr hwnd)
        {
            if (file == null || !Native.IsProcess(hwnd, "notepad") || !IsTarget(hwnd)) return "";
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                var condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                return root.FindFirst(TreeScope.Descendants, condition) == null ? "Поле Блокнота пока не найдено." : "Поле Блокнота доступно.";
            }
            catch (ElementNotAvailableException) { return "Поле Блокнота пока не доступно."; }
            catch (InvalidOperationException) { return "Проверка поля временно недоступна."; }
            catch (System.Runtime.InteropServices.COMException) { return "Проверка поля временно недоступна."; }
        }

        private static void Write(string path, string text) { File.WriteAllText(path, text, new UTF8Encoding(false)); }

        private bool SameText(string path, string value, bool allowFinalNewline = false)
        {
            if (path == null || !File.Exists(path)) return false;
            try
            {
                if (new FileInfo(path).Length > 65536) return false;
                string contents;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) contents = reader.ReadToEnd().Replace("\r\n", "\n");
                return contents == value || allowFinalNewline && contents == value + "\n";
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException)
            {
                noVerification = true;
                Notice = "Учебный файл недоступен для проверки. Проверьте доступ и повторите.";
                return false;
            }
        }

        private void Observe() { sawChord = true; actionAt = DateTime.UtcNow; }
        private void Advance() { Step++; sawChord = false; noVerification = false; Notice = "Шаг " + (Step + 1) + " · предыдущий выполнен."; }
        private void Pass() { Passed = true; Notice = "Задание выполнено. Результат подтверждён."; }
    }
}
