using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TouchTypingStudio;

internal sealed partial class LabRun
{
    private ShortcutAttempt Perform(LabTask actual, string key)
    {

        if (actual.Id == "screenshot" && ShowGuide && Task.Keys == "F11") return Miss(key, WrongKeyMessage(key));
        bool pythonEdit = actual.Action.StartsWith("python-edit-", StringComparison.Ordinal);
        bool attempted = !pythonEdit && LabCatalog.Available(actual, Scene);
        bool changed = pythonEdit ? Scene.PerformPythonEdit(actual.Action) : attempted && Scene.Apply(actual.Action);
        if (changed && mission != null)
        {
            if (Scene.Switcher && (actual.Id is "switch-next" or "switch-prev" or "switch-group-next" or "switch-group-prev" or "win-switch"))
            {
                switchEvidenceId = actual.Id;
                switchReleaseKey = new[] { "Super", "Alt", "Ctrl" }
                    .Select(family => held.FirstOrDefault(keyName => KeyChord.Family(keyName) == family))
                    .FirstOrDefault(keyName => keyName != null);
            }
            // Alt+D mirrors the browser behavior but does not demonstrate
            // the requested physical Ctrl+L shortcut.
            if (!replayLegacyTriggers && actual.Id is ("next-tab" or "prev-tab") && actual.Keys is ("Ctrl+PageUp" or "Ctrl+PageDown"))
            {
                if (Task.Id == actual.Id) return Miss(key, "Вкладка изменилась. Это задание проверяет " + Task.Keys + ".");
                return null;
            }
            if (!replayLegacyUrl && actual.Id == "url" && actual.Keys == "Alt+D")
            {
                if (Task.Id == "url") return Miss(key, "Адрес выделен. Это задание проверяет Ctrl+L.");
                return null;
            }
            var passed = Observe(key, acceptExistingUrlSelection: !replayLegacyUrl && actual.Id == "url" && actual.Keys != "Alt+D", actualTaskId: actual.Id);
            if (passed != null) return passed;
            if (actual.Id == Task.Id && Scene.ChromeTabReviewRejection(actual.Action) is { } tabError)
                return Miss(key, tabError);
            if (actual.Id == "terminal-interrupt" && Task.Id == "terminal-interrupt" && !Scene.Terminal.LogIncidentArrived)
                return Miss(key, "Просмотр остановлен до новой ошибки. Снова наберите " +
                    Scene.TerminalFollowCommand + " и дождитесь строки ERROR.");
        }
        if (!changed && attempted && actual.Action == "file-paste" && Task.Text == "!conflict" && Scene.NotebookConflictExpected())
        {
            // Only a real new paste can verify the refusal; an unrelated
            // unchanged state or an old error message cannot earn the goal.
            var conflict = Observe(key, actualTaskId: actual.Id, renewedCsvConflict: true);
            if (conflict != null) return conflict;
        }
        if (changed && actual.Id == "focus-editor" && Task.Id == "focus-editor" && Scene.GuideNoteActive && !Scene.GuideNoteVerified())
            return Miss(key, "Заметка не совпала с тремя фрагментами. Сверьте review-notes.txt и сохраните его, не меняя исходники.");
        if (changed) { Status = ""; LastKey = ""; Feedback = "ready"; return null; }
        if (actual.Action is "file-name-commit" or "file-paste" or "file-undo" && Scene.FileProblem is string reason)
            return Miss(key, reason);
        return Miss(key, "Действие сейчас недоступно. " + Task.Title + ".");
    }

    private void Watch()
    {
        if (mission == null) return;
        if (AdaptiveGuide) attemptCoachContext = CoachContext();
        var eligible = Guidance == "independent" ? pending.TakeWhile(goal => goal.Phase == pending[0].Phase) : pending.Take(1);
        watching = eligible.Select(goal => (goal, ObserveEvidence(goal, null, goal.VerifyReport && !replayLegacyReport))).ToList();
    }

    private bool UsesCsvExplorerScope(LiveGoal goal) => !replayLegacyCsvExplorer && goal.CsvExplorerTarget != null;
    private string ObserveEvidence(LiveGoal goal, string command, bool strictReport) => UsesCsvExplorerScope(goal)
        ? LiveCourse.CsvExplorerProof(goal.Task, Scene)
        : LiveCourse.Evidence(goal.Task, Scene, command, strictReport, goal.ArtifactPath);

    private ShortcutAttempt Observe(string key, string equivalentCommand = null, bool acceptExistingUrlSelection = false,
        string actualTaskId = null, bool renewedCsvConflict = false)
    {
        if (mission == null || watching == null || Scene.Switcher) return null;
        foreach (var (goal, before) in watching)
        {
            bool csvExplorerScope = UsesCsvExplorerScope(goal);
            bool alternatePicker = csvExplorerScope && Guidance == "independent" &&
                goal.Task.Id == "file-next" && actualTaskId == "file-prev";
            if (mission.Curated && actualTaskId != goal.Task.Id && !alternatePicker) continue;
            bool strictReport = goal.VerifyReport && !replayLegacyReport;
            string target = csvExplorerScope ? goal.CsvExplorerTarget :
                goal.VerifyReport && replayLegacyReport ? goal.LegacyTarget : goal.Target;
            bool recheckingArtifact = goal.ArtifactPath != null && goal.Task == Task && equivalentCommand != null;
            if (goal.ArtifactPath != null && !recheckingArtifact) continue;
            bool recheckingConflict = csvExplorerScope && renewedCsvConflict && goal.Task.Text == "!conflict";
            if (before == target && !(Scene.NotebookCourse && actualTaskId == goal.Task.Id && goal.Task.Action is "notebook-run" or "save") &&
                !recheckingArtifact && !recheckingConflict && !(acceptExistingUrlSelection && goal.Task.Id == "url")) continue;
            string command = goal.Task == Task ? equivalentCommand : null;
            if (ObserveEvidence(goal, command, strictReport) != target) continue;
            // Published drafts retain earned progress, but a report receipt is only issued
            // when the reconstructed workspace also meets today's verification rules.
            if (goal.VerifyReport && (strictReport || LiveCourse.Evidence(goal.Task, Scene, command, verifyReport: true) == goal.Target))
                verifiedReport = Scene.ReadReportArtifact();
            Task = goal.Task;
            pending.Remove(goal);
            return Pass(key, alternatePicker ? actualTaskId : null);
        }
        return null;
    }

    private ShortcutAttempt Pass(string key, string actualSkill = null)
    {
        if (!restoring && ActionLimit > 0 && Solved == ActionLimit - 1 && latchedLimit == null)
        {
            latchedLimit = "actions";
            // If an admitted key consumes the last action, processing that key
            // may finish after the timer boundary. Its receipt won, not CPU time.
            if (TimeLimit > 0 && !FinishingMission)
            {
                activeMilliseconds = inputAtActiveMilliseconds;
                activeSince = null;
            }
        }
        var attempt = Record(true, key, actualSkill);
        Solved++; taskInBlock++;
        string result = Task.Action == "command" ? Task.Text switch
        {
            "pwd" => "Текущая папка: " + Scene.Terminal.Cwd,
            "cd" => "Вы перешли в домашнюю папку: " + Scene.Terminal.Cwd,
            "ls" => "В выводе видно содержимое папки: " + Scene.Terminal.Cwd,
            "whoami" => "Имя пользователя показано в выводе терминала.",
            "id" => "Вывод показывает идентификатор пользователя и его группы.",
            "uname" => "Вывод показывает имя операционной системы.",
            "ps" => "Вывод показывает процессы симуляции и их PID.",
            "history" => "В выводе видны команды этой сессии.",
            "clear" => "Вывод очищен. Файлы и текущая папка не изменились.",
            "reboot" => "Перезапущена только учебная система. Текущая папка: " + Scene.Terminal.Cwd,
            _ => Task.Title + " — выполнено."
        } : Task.Title + " — выполнено.";
        if (taskInBlock == 20) { Block++; taskInBlock = 0; }
        LastKey = "";
        Next(); Status = result; Feedback = "good";
        return attempt;
    }

    private ShortcutAttempt Miss(string key, string message)
    {
        hadError = true; Errors++; Feedback = "error"; Status = message; LastKey = key;
        var attempt = Record(false, key);
        if (AdaptiveGuide && !restoring && !coach.Blind) UpdateCoaching();
        return attempt;
    }

    private ShortcutAttempt Record(bool correct, string key, string actualSkill = null)
    {
        int milliseconds = (int)Math.Clamp(clock() - sinceTask, 1, 60000);
        string skill = actualSkill ?? Task.Id;
        var attempt = new ShortcutAttempt { Skill = "lab/" + App + "/" + skill, Session = Session, Verification = "lab-scene",
            Date = DateTime.Now, Correct = correct, Milliseconds = milliseconds, MistakeKey = correct ? null : key, InputMode = "exact", Mission = MissionId, Layout = attemptLayout,
            Assisted = ShowGuide, Independent = Guidance == "independent" || coach.Blind, FirstTry = correct && !hadError,
            TerminalCase = Scene.PracticalTerminal ? variant + 1 : 0,
            CoachStage = CoachStage.Length == 0 ? null : CoachStage, CoachContext = CoachStage.Length == 0 ? null : attemptCoachContext };
        Attempts.Add(attempt); points.Add(new LabPoint(Seconds, correct, key, skill, milliseconds));
        return attempt;
    }
}
