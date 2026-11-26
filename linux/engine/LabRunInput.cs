using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabRun
{
    private readonly HashSet<string> repeatTextHeld = new(StringComparer.Ordinal);

    internal ShortcutAttempt Input(string key, bool pressed, string text, string nativeLayout = "")
    {
        if (Finished || AwaitingLimitChoice || Paused || string.IsNullOrEmpty(key)) return null;
        if (!restoring && !FinishingMission && TimeLimit > 0 && ActionLimit > 0)
        {
            inputAtActiveMilliseconds = ActiveMilliseconds;
            if (inputAtActiveMilliseconds >= TimeLimit * 1000.0) return null;
        }
        if (!pressed)
        {
            repeatTextHeld.Remove(key);
            if (layoutBlockedKeys.Remove(key)) return null;
        }
        bool targetedText = pressed && (nativeLayout is "EN" or "RU") && RequiresEnglishText(key, text);
        if (pressed && (layoutBlockedKeys.Contains(key) || targetedText && nativeLayout == "RU"))
        {
            layoutBlockedKeys.Add(key);
            LastKey = "";
            Feedback = "layout";
            Status = "Переключи раскладку на EN";
            return null;
        }
        if (pressed && held.Contains(key))
        {
            // A held palette arrow scrolls, but only a fresh qualifying
            // gesture can earn a lesson step or execute a command.
            if (key is "Up" or "Down" && Scene.Active?.Focus == "palette" && !Scene.Switcher &&
                !held.Any(KeyChord.IsModifier))
            {
                if (Scene.Apply(key == "Up" ? "palette-prev" : "palette-next"))
                    trace.Add(new(key, true, ""));
                return null;
            }
            bool repeatText = repeatTextHeld.Contains(key) && text?.Length > 0 && !text.Any(char.IsControl);
            if (!replayLegacyUrl && (Scene.CanRepeatEditing(key) || repeatText && Scene.CanRepeatText()) && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super")
                && (!held.Any(item => KeyChord.Family(item) == "Ctrl") || key is "Left" or "Right" or "Home" or "End" ||
                    CanEraseNotebookWord(key, held.Any(item => KeyChord.Family(item) == "Shift"))))
            {
                string repeated = repeatText ? text : "";
                bool nativeEnglish = repeatText && targetedText && nativeLayout == "EN";
                trace.Add(new(key, true, repeated, nativeEnglish));
                Scene.Text(key, nativeEnglish ? repeated : VirtualText(key, repeated), held.Any(item => KeyChord.Family(item) == "Shift"),
                    held.Any(item => KeyChord.Family(item) == "Ctrl"));
            }
            return null; // repeat edits can move the caret, never complete a lesson step
        }
        if (!pressed && !held.Contains(key)) return null;
        // Capture before a layout switch or other action can modify the scene.
        bool useNativeEnglish = targetedText && nativeLayout == "EN";
        attemptLayout = useNativeEnglish ? "EN" : Scene.Layout;
        trace.Add(new(key, pressed, text, useNativeEnglish));
        if (!pressed)
        {
            if (!held.Remove(key)) return null;
            if (Scene.Switcher && (key == switchReleaseKey || switchReleaseKey == null && KeyChord.Family(key) == "Alt"))
            {
                Watch(); Scene.CommitSwitch();
                var credited = switchEvidenceId; switchEvidenceId = switchReleaseKey = null;
                return Observe(key, actualTaskId: credited);
            }
            if (Task.Id == "gnome-left-super" && held.Count == 0)
            {
                if (key == "LSuper" && !superUsed)
                {
                    Watch();
                    return Perform(LabCatalog.Shortcuts.First(task => task.Id == "gnome-left-super"), key);
                }
                if (KeyChord.Family(key) is "Alt" or "Super")
                    return Miss(key, key == "RSuper" ? "Это правый Super. Найдите левую клавишу ⊞ рядом с пробелом."
                        : "Это Alt. Сдвиньтесь на одну клавишу левее к кнопке ⊞.");
            }
            if (KeyChord.Family(key) == "Super" && !superUsed && held.Count == 0)
                { Watch(); return Perform(Scene.WindowsDesktop ? LabCatalog.WindowsStartTask : LabCatalog.Shortcuts.First(task => task.Id == "overview"), key); }
            return null;
        }
        if (!held.Add(key)) return null;
        repeatTextHeld.Remove(key);
        if (KeyChord.IsModifier(key))
        {
            if (KeyChord.Family(key) == "Super") superUsed = false;
            return null;
        }
        if (held.Any(item => KeyChord.Family(item) == "Super")) superUsed = true;
        bool commandModifiers = held.Any(item => KeyChord.Family(item) is "Ctrl" or "Alt" or "Super");
        if (Task.Id == "gnome-left-super" && key == "Z")
            return Miss(key, "Это клавиша Z (Я). Левый Super ⊞ находится ниже, левее Alt.");
        if (LiveCourse.ResolveGitKey(key, held, Scene) is LabTask git)
        {
            if (git.Action == "scm-commit" && Scene.ReleasePractice && Task.Action != "scm-commit")
                return Miss(key, "Сначала завершите текущую проверку: " + Task.Title);
            Watch(); return Perform(git, key);
        }
        if (key is "Up" or "Down" or "Enter" && Scene.OnSourceControl && !held.Any(KeyChord.IsModifier))
        {
            Scene.ReviewSourceControl(key);
            Status = ""; LastKey = ""; Feedback = "ready"; return null;
        }
        Watch();
        if (Task.Action == "disk-capacity-verdict")
        {
            // This is the exercise's decision, not another shell input.
            if (commandModifiers || held.Any(item => KeyChord.Family(item) == "Shift") ||
                (key is not ("1" or "2") && !(key is "NumPad1" or "NumPad2" && text is "1" or "2")))
                return null;
            if (Scene.Active?.App != "terminal" || Scene.Overview)
                return Miss(key, "Вернитесь к окну задания.");
            bool enough = key is "2" or "NumPad2";
            if (!Scene.SubmitDiskCapacityVerdict(enough))
                return Miss(key, "Сравните свободное место на диске назначения с размером архива.");
            return Observe(key, actualTaskId: Task.Id);
        }
        // Keep the physical key in held and trace for release, repeats and
        // saved-draft replay. Interpret its secondary legend only for a
        // navigation goal eligible in the current scene.
        var keypadGoal = replayLegacyTriggers ? null : watching?.Select(item => item.Goal.Task).FirstOrDefault(goal =>
            Gesture.TryParse(goal.Keys, out var gesture) && gesture.Chords.Length == 1 &&
            gesture.Chords[0].AcceptsNavigationKeypad(key, held));
        if (keypadGoal != null && Gesture.TryParse(keypadGoal.Keys, out var keypadGesture))
        {
            key = keypadGesture.Chords[0].Trigger;
            text = "";
        }
        // Lock toggles affect the physical keyboard, not progress in a lesson.
        if (key is "NumLock" or "CapsLock" or "ScrollLock") return null;
        if (!replayLegacyExplorerTargetGuard && Task.TargetPath is string expectedFile &&
            Gesture.TryParse(Task.Keys, out var transfer) && transfer.Chords.Length == 1 &&
            transfer.Chords[0].Trigger == key && transfer.Chords[0].FitsModifiers(held))
        {
            string expected = Scene.EditorLessonRelativePath(expectedFile);
            var window = Scene.Active;
            if (window?.App != "code" || window.Focus != "explorer")
                return Miss(key, "Действие относится к «" + expected + "» в проводнике, а не к тексту. Нажмите Ctrl+Shift+E.");
            var actualFile = Scene.Entries(window).ElementAtOrDefault(window.Item);
            if (actualFile?.Path != expectedFile)
                return Miss(key, "Выберите «" + expected + "» в проводнике. Другие файлы не изменены.");
        }
        if (Scene.OnNotebook && LiveCourse.ResolveNotebook(key, held, Scene) is LabTask notebook) return Perform(notebook, key);
        var pythonEdit = Scene.OnNotebook ? null : LiveCourse.ResolvePythonEdit(key, held, Scene);
        if (pythonEdit != null) return Perform(pythonEdit, key);
        var actual = Scene.Overview && !commandModifiers && key != "Esc" ? null : LabCatalog.Resolve(key, held, Scene);
        if (Scene.Terminal.FollowingLog && Scene.Active?.App == "terminal" && actual?.Id != "terminal-interrupt")
            return Miss(key, "Журнал ещё открыт. Ctrl+C остановит просмотр.");
        if (Scene.OnNotebook && actual != null && (!LabScene.NotebookGlobalAction(actual.Action) ||
            Scene.ReplayLegacyNotebookActions && actual.Action is "palette" or "code-definition" or "code-back" or "restore-tab")) actual = null;
        string before = Scene.Overview ? Scene.Search : Scene.Active?.App == "terminal" || Scene.Active?.Focus is "terminal" or "reverse-search" ? Scene.TerminalInput : Scene.Active?.Input ?? "";
        bool searching = !Scene.ReplayLegacyTerminalSearch && Scene.Active?.Focus == "reverse-search";
        if (actual == null && Scene.WindowsDesktop && (Scene.DesktopOverlay.Length > 0 || Scene.ShowDesktop))
            return Miss(key, WrongKeyMessage(key));
        if (searching && key == "Enter" && !commandModifiers && !Scene.CanExecuteHistoryMatch)
        {
            // No shell command ran. Stay in search instead of inferring a
            // credit (or a wrong submission) from a prior terminal result.
            Scene.Text(key, "");
            Status = Scene.Active.Status; LastKey = ""; Feedback = "ready";
            return null;
        }
        bool submitting = Task.Action == "command" && key == "Enter" && !commandModifiers;
        bool matches = submitting && Scene.Terminal.MatchesCommand(before, Task.Text);
        if (actual != null && !submitting)
        {
            if (actual.Id is "screenshot" or "debug-window-shot") Scene.StartCapture(text);
            return Perform(actual, key);
        }
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        bool word = held.Any(item => KeyChord.Family(item) == "Ctrl") && !held.Any(item => KeyChord.Family(item) is "Alt" or "Super");
        bool navigation = key is "Left" or "Right" or "Home" or "End";
        bool notebookWordDelete = CanEraseNotebookWord(key, shift);
        if (commandModifiers && !(word && (navigation || notebookWordDelete)))
        {
            // Cutting an empty selection in an ordinary field is a valid no-op.
            if (word && key == "X" && Scene.Active?.Focus == "url") return null;
            return Miss(key, WrongKeyMessage(key));
        }
        bool editableText = !commandModifiers && text?.Length > 0 && !text.Any(char.IsControl)
            && key is not ("Enter" or "NumEnter" or "Tab" or "Backspace" or "Delete" or "Print")
            && Scene.CanRepeatText();
        bool changed = Scene.Text(key, useNativeEnglish ? text : VirtualText(key, text), shift, word);
        if (editableText && changed) repeatTextHeld.Add(key);
        bool continuousTextGoal = Task.Action is "python-edit-write" or "notebook-type" or "file-search-query";
        bool typedEdit = changed && continuousTextGoal &&
            key is not ("Left" or "Right" or "Up" or "Down" or "Home" or "End" or "Enter" or "Backspace" or "Delete") &&
            !string.IsNullOrEmpty(text) && !text.Any(char.IsControl);
        var passed = Observe(key, matches && Scene.Terminal.LastSuccess ? Task.Text : null, actualTaskId:
            matches && Scene.Terminal.LastSuccess || typedEdit || changed && !continuousTextGoal && Task.Keys.Length == 0 && key == "Enter" && !submitting ? Task.Id : null);
        if (passed != null) return passed;
        if (submitting && !Scene.Terminal.LastSuccess) return Miss(key, Scene.Active.Status);
        if (submitting && searching && !matches) return Miss(key, ShowGuide
            ? "Выполнена другая команда. Перед Enter сравните строку с заданием."
            : "Выполнена другая команда.");
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.VerifyReport) == true)
            return Miss(key, "Команда выполнена, но отчёт не совпадает с заданием. Проверьте весь сохранённый файл и папку «Загрузки».");
        if (matches && watching?.Any(item => item.Goal.Task == Task && item.Goal.ArtifactPath != null) == true)
            return Miss(key, "Сохранённая заметка отличается от исходника. Вернитесь в редактор, вставьте скопированный текст целиком и сохраните файл.");
        if (submitting && Scene.Terminal.DeliveryLesson != "") return Miss(key, Task.Id switch {
            "command/delivery-read-match" => "Открытая версия не совпала с квитанцией. Сверьте обе суммы и попробуйте другую копию.",
            "command/delivery-read-copy" => "Проверьте заметку в Backup, а не её исходник в Work.",
            "command/delivery-preview" or "command/delivery-recheck" => "Нужен предварительный список без записи в Backup.",
            _ => "Команда сработала, но пока не показала нужный результат. Сверьте папку и цель."
        });
        if (submitting && Scene.DiskSpaceLesson) return Miss(key, "Вы проверили другой диск. Сверьте назначение в заявке.");
        if (submitting && Scene.PracticalTerminal)
            return Miss(key, LiveCourse.TerminalIncidentTask(Task.Id)
                ? "Сверьте идентификатор именно у новой строки ERROR; результат этого запроса пока не показан."
                : "Команда выполнена, но вывод не соответствует цели этого шага.");
        if (!changed) return Miss(key, key == "Enter" && Scene.Active?.Focus == "quick-open"
            ? Scene.Active.Status : "Здесь эта клавиша не выполняет действие.");
        Feedback = "ready"; LastKey = ""; Status = ""; return null;
    }

    private bool CanEraseNotebookWord(string key, bool shift) =>
        !replayLegacyNotebookWordErase && Scene.OnNotebook && Scene.Active.Tab.Notebook.Editing &&
        !shift && (key is "Backspace" or "Delete");

    // The virtual keyboard/switching mission and the actual input source are
    // different things. Require EN for known English text goals only; ordinary
    // shortcuts, edits, virtual layout switching and free Unicode input still work.
    private bool RequiresEnglishText(string key, string text)
    {
        if (KeyChord.IsModifier(key) ||
            string.IsNullOrEmpty(text) || text.Any(char.IsControl) ||
            held.Any(item => KeyChord.Family(item) is "Ctrl" or "Alt" or "Super"))
            return false;
        var window = Scene.Active;
        if (Scene.Overview) return Task.Action == "launch" && Task.Text.All(char.IsAscii);
        if (window == null || Scene.Switcher) return false;
        return Task.Action switch
        {
            "command" => window.App == "terminal" || window.Focus is "terminal" or "reverse-search",
            "navigate-pasted" => Scene.BrowserPracticeActive && Task.Text.Length > 0 && window.Focus == "url",
            "file-name-commit" => window.Focus == "file-name",
            "code-clone-command" => Scene.BrowserPracticeActive && window.Focus == "palette",
            "palette-choose" => Scene.AdvancedNotebook && window.Focus == "palette" && Task.Text.Length > 0,
            "notebook-type" => Scene.OnNotebook && window.Tab.Notebook.Editing,
            "release-find" => window.App == "code" && window.Focus == "find",
            "scm-commit" => window.App == "code" && window.Focus == "scm-message",
            "checkout-expect" => window.App == "code" && window.Focus == "find",
            "python-edit-write" or "gnome-write" or "debug-rewrite" => window.App == "code" && window.Focus == "body",
            "debug-doc-submit" => window.App == "chrome" && window.Focus == "find",
            "project-search-submit" => window.Focus == "project-search",
            "project-search-repair" => window.App == "code" && window.Focus == "body",
            "code-rename-confirm" => window.Focus == "symbol-rename",
            "exact-find-submit" or "research-find" => window.Focus == "find",
            "quick-choose" => (Scene.NotebookCourse || Scene.ExactEdit) && window.Focus == "quick-open" && Task.Text.Length > 0,
            "save" => (Scene.NotebookCourse || Scene.ExactEdit || Scene.CompletingMovedReport) && window.App == "code" && window.Focus == "body" && Task.Text.Length > 0,
            _ => false
        };
    }

    private string WrongKeyMessage(string key) =>
        Task.Keys == "F11" && key == "Print"
            ? "Получена клавиша PrtSc. Для F11 на MateBook попробуйте Fn+F11 или переключите Fn Lock."
            : !ShowGuide || Task.Keys.Length == 0 ? "Клавиша «" + key + "» здесь не выполняет действие." : "Нажата «" + key + "». Подсказка: " + Task.Keys + ".";

    private string VirtualText(string key, string fallback)
    {
        if (!Scene.LayoutIsVirtual) return fallback;
        var cap = TypingKeyboard.Rows.SelectMany(row => row).FirstOrDefault(item => ShortcutCatalog.KeyName(item) == key);
        if (key == "Space") return " ";
        if (cap == null) return fallback;
        bool shift = held.Any(item => KeyChord.Family(item) == "Shift");
        string value = shift ? cap.Shifted(Scene.Layout == "RU" ? Layout.Russian : Layout.English) : cap.Base(Scene.Layout == "RU" ? Layout.Russian : Layout.English);
        return value.Length == 1 ? value : fallback;
    }
}
