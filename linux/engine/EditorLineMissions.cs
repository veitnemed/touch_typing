using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    // Old traces first replay with their original goals and behavior. Once all
    // old edits are reconstructed, derive the remaining expected results using
    // the new line operations and the scene the learner actually reopened.
    internal static void RebaseEditorLineGoals(List<LiveGoal> remaining, LabScene scene)
    {
        var trial = scene.Clone();
        for (int i = 0; i < remaining.Count; i++)
        {
            var goal = remaining[i];
            var task = goal.Task;
            string before = Evidence(task, trial, verifyReport: goal.VerifyReport, artifactPath: goal.ArtifactPath);
            if ((task.Action is "quick-next" or "symbol-next") && task.Text.Length > 0)
            {
                if (trial.Active == null) break;
                int count = trial.Entries(trial.Active).Length;
                for (int attempt = 0; attempt < count && !AtItem(trial, task.Text); attempt++)
                    if (!trial.Apply(task.Action)) break;
                if (!AtItem(trial, task.Text)) break;
            }
            else if (!Demonstrate(task, trial))
            {
                // Leave later expectations intact if this off-script
                // workspace cannot be projected further without user input.
                break;
            }
            string target = Evidence(task, trial, verifyReport: goal.VerifyReport, artifactPath: goal.ArtifactPath);
            if (target != before) remaining[i] = goal with { Target = target };
        }
    }

    private static void LineEditMissions(List<LiveMission> missions) =>
        missions.Add(new("code", 0, "code-line-edit", "Правка выделенной строки",
            [
                new("Выделите среднюю строку и проверьте комментарий", "select-line comment undo"),
                new("Удалите ту же строку и восстановите файл", "delete-line undo")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareLineEditExercise()));
}

internal sealed partial class LabScene
{
    internal void PrepareLineEditExercise()
    {
        const string first = "import numpy as np";
        const string body = first + "\ndef ifft(x): return x\nprint('next')";
        const string path = Project + "/line-edit.py";
        Terminal.StoreFile(path, body);
        originalFiles[path] = body;
        OpenDocument(path);
        Active.Cursor = first.Length + 1;
    }
}
