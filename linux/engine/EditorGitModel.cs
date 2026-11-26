using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record WorkbenchChange(string Path, string State, bool Staged, string Before, string After);

// The workbench is a second view of the SAME in-memory repository as the shell.
// Snapshot inspection must never stage a file or change cwd.
internal sealed partial class VirtualTerminal
{
    internal bool HasWorkbenchRepo(string root) => repository == root;
    internal string WorkbenchBranch => branch;
    internal string LastLocalCommit => branchLogs.TryGetValue(branch, out var log) && log.Count > 0 ? commits[log[^1]] : "";
    internal int LocalCommits => branchLogs.GetValueOrDefault(branch)?.Count ?? 0;
    internal string CommittedBody(string name) => committed.GetValueOrDefault(name);
    internal string StagedBody(string name) => index.GetValueOrDefault(name);

    internal bool AttachWorkbench(string root, IReadOnlyDictionary<string, string> baseline)
    {
        if (repository != null) return repository == root;
        if (!directories.Contains(root)) return false;
        repository = root;
        packages.Add("git");
        foreach (var (path, contents) in baseline)
            if (path.StartsWith(root + "/", StringComparison.Ordinal))
                committed[path[(root.Length + 1)..]] = contents;
        Replace(index, committed);
        branches[branch] = new(committed);
        branchLogs[branch] = [];
        Version++; return true;
    }

    internal WorkbenchChange[] GitChanges()
    {
        if (repository == null) return [];
        var working = RepositoryFiles();
        var result = new List<WorkbenchChange>();
        void Compare(Dictionary<string, string> before, Dictionary<string, string> after, bool staged)
        {
            foreach (var path in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
            {
                var a = before.GetValueOrDefault(path); var b = after.GetValueOrDefault(path);
                if (a != b) result.Add(new(repository + "/" + path, b == null ? "D" : a == null ? staged ? "A" : "U" : "M", staged, a, b));
            }
        }
        Compare(committed, index, true); Compare(index, working, false);
        return result.ToArray();
    }

    internal bool ModifyStage(string root, string path, bool stage)
    {
        if (!HasWorkbenchRepo(root) || !path.StartsWith(root + "/", StringComparison.Ordinal)) return false;
        string relative = path[(root.Length + 1)..];
        var from = stage ? files.GetValueOrDefault(path) : committed.GetValueOrDefault(relative);
        if (from == index.GetValueOrDefault(relative)) return false;
        if (from == null) index.Remove(relative); else index[relative] = from;
        Version++; return true;
    }

    internal bool CommitIndex(string message)
    {
        if (repository == null || string.IsNullOrWhiteSpace(message) || Same(committed, index)) return false;
        // Uses the existing Git command and branch log, but never mutates the shell's cwd or history.
        string cwd = Cwd;
        try { Cwd = repository; Git(["commit", "-m", message.Trim()]); return true; }
        finally { Cwd = cwd; }
    }
}

internal sealed partial class LabScene
{
    private string gitMessage = "";
    private string GitRoot => DebugDay ? debugDay.Root : EditorProjectRoot;
    private WorkbenchChange[] SourceControlFiles(string[] paths)
    {
        if (Terminal.HasWorkbenchRepo(GitRoot)) return Terminal.GitChanges();
        string prefix = GitRoot + "/";
        return paths.Concat(originalFiles.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
            {
                bool existed = originalFiles.TryGetValue(path, out var previous);
                string saved = Terminal.ReadFile(path);
                return previous == saved ? null : new WorkbenchChange(path, !existed ? "U" : saved == null ? "D" : "M", false, previous, saved);
            }).Where(row => row != null).ToArray();
    }
    private int SelectedChange(LabWindow w, WorkbenchChange[] rows)
    {
        if (rows.Length == 0) return -1;
        if (w.Item >= 0 && w.Item < rows.Length) return w.Item;
        int byTab = Array.FindIndex(rows, row => row.Path == w.Url);
        return Math.Max(0, byTab);
    }
    private WorkbenchChange SelectedChange(LabWindow w)
    {
        var rows = SourceControlFiles(ProjectDocuments().ToArray());
        return rows.ElementAtOrDefault(SelectedChange(w, rows));
    }
    private bool GitFocused => Active?.App == "code" && Active.View == "changes" && !Switcher && !Overview &&
        !Screenshot && DesktopOverlay.Length == 0;

    internal bool GitActionAvailable(string action)
    {
        if (!GitFocused) return false;
        return action switch
        {
            "scm-menu" => OnSourceControl && SelectedChange(Active) != null,
            "scm-stage" => Active.Focus == "scm-menu" && SelectedChange(Active)?.Staged == false,
            "scm-unstage" => Active.Focus == "scm-menu" && SelectedChange(Active)?.Staged == true,
            "scm-close" => Active.Focus is "scm-menu" or "scm-message",
            "scm-message" => OnSourceControl,
            "scm-commit" => Active.Focus == "scm-message",
            _ => false
        };
    }
    private bool ApplyGitAction(LabWindow w, string action)
    {
        if (!GitActionAvailable(action)) return false;
        switch (action)
        {
            case "scm-menu": w.Focus = "scm-menu"; return true;
            case "scm-message": w.Focus = "scm-message"; w.Input = gitMessage; w.Cursor = w.Input.Length; return true;
            case "scm-close": if (w.Focus == "scm-message") gitMessage = w.Input; w.Focus = "changes"; return true;
            case "scm-stage": case "scm-unstage":
                var item = SelectedChange(w);
                if (item == null || !Terminal.AttachWorkbench(GitRoot, originalFiles) ||
                    !Terminal.ModifyStage(GitRoot, item.Path, action == "scm-stage")) return false;
                w.Focus = "changes"; var next = Terminal.GitChanges();
                w.Item = Array.FindIndex(next, e => e.Path == item.Path && e.Staged == (action == "scm-stage"));
                w.Status = (action == "scm-stage" ? "В индексе: " : "Убрано из индекса: ") + NameOf(item.Path);
                return true;
            case "scm-commit":
                if (string.IsNullOrWhiteSpace(w.Input)) { w.Status = "Опишите изменения для истории Git."; return false; }
                if (!ReleaseIndexReady() || !Terminal.CommitIndex(w.Input)) { w.Status = "Проверьте Staged Changes: подготовьте только результат задания."; return false; }
                gitMessage = w.Input = ""; w.Focus = "changes"; w.Item = -1; w.Status = "Локальный коммит: " + Terminal.LastLocalCommit; return true;
        }
        return false;
    }
    private bool SourceControlPointer(LabWindow w, string action, string path)
    {
        if (Switcher || Overview || Screenshot || DesktopOverlay.Length > 0 || w?.App != "code") return false;
        if (action == "scm-open") return Apply("changes");
        if (w.View != "changes") return false;
        if (action is "scm-message" or "scm-commit")
        {
            if (w.Focus != "scm-message")
            {
                w.Focus = "changes"; if (!Apply("scm-message")) return false;
            }
            return action == "scm-message" || Apply("scm-commit");
        }
        bool staged = action is "scm-index" or "scm-unstage";
        if (!SourceControlPick(w, path, 0, staged)) return false;
        if (action is "source-control" or "scm-index") return true;
        if (action is not ("scm-stage" or "scm-unstage")) return false;
        return Apply("scm-menu") && Apply(action);
    }
}
