using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed record LabEntry(string Title, string Path, string Kind);
internal sealed record LabReportArtifact(string Path, string Content, bool DownloadStillPresent);

internal sealed partial class LabScene
{
    private const string Project = "/home/student/project";
    private const string LegacyReport = "spectrum-report.txt";
    internal string Report => NeutralWorkspace ? NeutralProject.ReportName : LegacyReport;
    private const string ReportBody = "piling-main · спектральный анализ\nstatus: draft\nsource: src/spectrum.py\n";
    private string fileTransfer = "";
    private bool fileCut;
    private long trashSequence;
    private readonly Dictionary<string, string> trash = [];
    private readonly List<string> recentUrls = [], downloads = [];
    internal int DownloadCount => downloads.Count;
    internal LabReportArtifact ReadReportArtifact() =>
        new(Project + "/" + Report, Terminal.ReadFile(Project + "/" + Report),
            Terminal.Exists("/home/student/Downloads/" + Report));
    private static readonly LabEntry[] LegacyLinks = [
        new("Методика расчёта спектра", "https://practice.local/spectrum", "page"),
        new("Исходники piling-main", "https://github.com/veitnemed/piling-main", "page"),
        new("Документация Python", "https://docs.python.org/3/", "page"),
        new("Скачать отчёт спектрального анализа", LegacyReport, "download")];
    private LabEntry[] Links => NeutralWorkspace ? NeutralProject.Links : LegacyLinks;

    internal LabEntry[] Entries(LabWindow w)
    {
        if (IsFileSearch(w)) return FileSearchEntries(w);
        if (w.App == "files")
            return Terminal.Entries(w.Folder).Where(path => !NameOf(path).StartsWith('.'))
                .Select(path => new LabEntry(NameOf(path), path, Terminal.DirectoryExists(path) ? "folder" : "file")).ToArray();
        if (w.App == "chrome") return w.Url switch
        {
            ChromeHistory => HistoryEntries(w),
            "chrome://downloads" => DownloadEntries(),
            "chrome://bookmarks" => w.SavedBookmarks.Order().Select(PageEntry).ToArray(),
            _ when IsTabReviewUrl(w.Url) => [],
            _ when BrowserPracticeActive && IsSearchResult(w.Url) => PracticeSearchEntries(w.Url),
            _ when BulletinWorkflow => BulletinLinks(w.Url),
            _ when BrowserPracticeActive => BrowserResearchLinks(w.Url),
            _ => Links
        };
        if (w.App == "code" && w.Focus is "explorer" or "file-name") return ProjectTree(EditorProjectRoot, w.ExpandedProjectFolders).ToArray();
        if (w.Focus == "palette" && NotebookPalette(w) is { Length: > 0 } commands) return commands;
        if (w.Focus == "project-results") return ProjectMatchEntries(w);
        if (w.App == "code" && DiagnosticWorkspace &&
            (w.Focus is "code-problems" or "code-quick-fix" || w.Panel && w.Focus == "body"))
            return DiagnosticEntries(w);
        if (w.Focus == "symbol-references") return ReferenceEntries(w);
        if (w.Focus == "symbols")
        {
            var result = new List<LabEntry>();
            int offset = 0;
            foreach (string line in w.Body.Split('\n'))
            {
                if (line.StartsWith("def ")) result.Add(new(line[4..].Split('(')[0], offset.ToString(), "symbol"));
                offset += line.Length + 1;
            }
            return result.ToArray();
        }
        if (ExactEdit && w.Focus == "quick-open") return ExactFileEntries(w.Input);
        if (w.App == "code" && w.Focus == "quick-open")
            return ProjectDocuments()
                .Where(path => (w.Focus != "quick-open" || path.Contains(w.Input, StringComparison.OrdinalIgnoreCase))).Select(path => new LabEntry(NameOf(path), path, "file")).ToArray();
        return [];
    }

    private readonly Dictionary<string, string> originalFiles = [];
    private string EditorProjectRoot => NotebookCourse ? NotebookRoot : RepoCloned ? CloneRoot : ExactEdit ? ExactRoot : PythonEditingExercise ? PythonEditRoot : DiagnosticWorkspace ? DiagnosticRoot : SearchProjectRoot ?? (symbolWorkspace ? SymbolProject : Project);
    private IEnumerable<string> ProjectDocuments() => projectFileFamily != null || ContextHandoffActive || NotebookCourse ?
        ProjectTree(EditorProjectRoot, null, recursive: true).Where(e => e.Kind == "file").Select(e => e.Path) : DebugDay ? DebugProjectDocuments() :
        DiagnosticWorkspace ? Terminal.Entries(DiagnosticRoot).Where(path => !Terminal.DirectoryExists(path)) :
        Terminal.Entries(EditorProjectRoot)
        .Concat(Terminal.Entries(EditorProjectRoot + "/src"))
        .Where(path => !Terminal.DirectoryExists(path));
    private object WindowSnapshot(LabWindow w)
    {
        w.Tab?.Notebook?.SynchronizeWorkspace(NotebookRead);
        var paths = w.App == "code" ? ProjectDocuments().ToArray() : [];
        string Relative(string path) => DebugDay ? DebugRelative(path) : path[(EditorProjectRoot.Length + 1)..];
        var changes = paths.Where(path => !originalFiles.TryGetValue(path, out var original) || original != Terminal.ReadFile(path)).Select(Relative);
        var results = w.App == "code" && w.View == "project-search" ? ProjectSearchSidebar(w) : [];
        var activePath = w.Tab == null || w.App != "code" ? "" : EditorPath(w.Tab);
        var projectName = w.App != "code" ? "" :
            NeutralWorkspace && EditorProjectRoot == Project && !DebugDay ? NeutralProject.Name : NameOf(DebugDay ? debugDay.Root : EditorProjectRoot);
        var projectRelative = w.App != "code" ? "" : w.Tab == null ? "" :
            DebugDay && activePath.StartsWith(debugDay.Root + "/", StringComparison.Ordinal) ? DebugRelative(activePath) : activePath.StartsWith(EditorProjectRoot + "/", StringComparison.Ordinal)
                ? Relative(activePath) : NameOf(activePath);
        return w.Snapshot(w.Id == ActiveWindow, Entries(w), paths.Select(Relative), changes, results, projectName, projectRelative,
            WindowsDesktop && w.App == "files" ? WindowsPath(w.Folder) : null, BrowserPracticeActive, EditorProjectRoot.Count(ch => ch == '/'),
            w.App == "code" ? ProjectTree(EditorProjectRoot, w.ExpandedProjectFolders).ToArray() : null, CanUndoProjectFile,
            CanPasteProjectFile, CanPasteNotebookCell,
            tabSearch: ChromeTabsSnapshot(w), sourceReview: ChromeReviewSnapshot(w));
    }

    private static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
    private static string EditorPath(LabTab tab) => tab.Url.StartsWith('/') ? tab.Url : Project + "/" + tab.Url;
    private LabEntry PageEntry(string url) => (TabReviewPage(url) ?? (BrowserPracticeActive ? BrowserPracticePage(url) : null)) is { } page
        ? new(page.Title, url, "page") : Links.FirstOrDefault(item => item.Path == url) ?? new(url, url, "page");
    private void OpenDocument(string path)
    {
        Ensure("code");
        var w = Active;
        int i = w.Tabs.FindIndex(tab => EditorPath(tab) == path);
        if (i < 0)
        {
            w.Tabs.Add(new LabTab(NameOf(path), path, Terminal.ReadFile(path) ?? ""));
            i = w.Tabs.Count - 1;
        }
        w.ActiveTab = i; w.Focus = "body";
    }

    private void Navigate(LabWindow w, string url)
    {
        w.Focus = "url"; w.Input = url; Text("Enter", "");
    }

    private bool FindMatch(LabWindow w, bool next)
    {
        if (w.Find.Length == 0 || w.Tab == null) return false;
        var pos = w.Tab.Position;
        int index = w.Body.IndexOf(w.Find, next ? Math.Clamp(Math.Max(pos.Caret, pos.Anchor), 0, w.Body.Length) : 0, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && next) index = w.Body.IndexOf(w.Find, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { w.Status = "Совпадений нет"; return false; }
        pos.Anchor = index; pos.Caret = index + w.Find.Length; w.Status = "Найдено: " + w.Find;
        RememberSourceMatch(w, index); return true;
    }

    internal void PrepareCase(int variant, string app)
    {
        string[] names = ["baseline", "experiment", "review"];
        browserCaseVariant = variant;
        BrowserPracticeQuery = new[] { "touch_typing github", "veitnemed touch_typing", "github touch typing" }[variant];
        if (!NeutralWorkspace)
        {
            Terminal.StoreFile(Project + "/" + names[variant] + ".md", "# " + names[variant] + "\n");
            originalFiles[Project + "/" + names[variant] + ".md"] = Terminal.ReadFile(Project + "/" + names[variant] + ".md");
        }
        if (app == "chrome" && variant == 1) Active.ActiveTab = 1;
        if (!WindowsDesktop && (app is "ubuntu" or "workday")) Workspace = variant % 2;
        if (app == "code" && !NeutralWorkspace)
        {
            var example = TypingRun.Project[variant];
            Active.Tab.Title = NameOf(example.Path); Active.Tab.Url = Project + "/" + example.Path;
            Active.Tab.Body = Active.Tab.SavedBody = Terminal.ReadFile(Active.Tab.Url);
        }
    }

    internal void PrepareHistory(IEnumerable<string> samples)
    {
        commands.InsertRange(0, samples.Distinct());
        commandIndex = commands.Count;
    }

    private bool ReverseSearch(LabWindow w)
    {
        int index = commands.Take(w.SearchIndex).ToList().FindLastIndex(command => command.Contains(w.Query, StringComparison.Ordinal));
        if (index >= 0) { w.SearchIndex = index; TerminalInput = commands[index]; }
        return true;
    }

    private bool FindTerminalHistory(LabWindow w, bool older = false)
    {
        int previous = w.SearchIndex;
        int end = older ? Math.Clamp(previous, 0, commands.Count) : commands.Count;
        int match = -1;
        if (w.Query.Length > 0)
            for (int i = end - 1; i >= 0; --i)
                if (commands[i].Contains(w.Query, StringComparison.Ordinal)) { match = i; break; }
        if (match < 0 && older && previous >= 0) match = previous;
        w.SearchIndex = match;
        TerminalInput = match >= 0 ? commands[match] : "";
        w.Cursor = w.Query.Length;
        w.Status = w.Query.Length == 0 ? "Введите часть команды из истории." :
            match < 0 ? "В истории нет совпадений. Измените запрос или нажмите Esc." : "";
        Version++;
        return true;
    }

    private bool HasTerminalHistoryMatch(LabWindow w) =>
        w.Query.Length > 0 && w.SearchIndex >= 0 && w.SearchIndex < commands.Count &&
        string.Equals(TerminalInput, commands[w.SearchIndex], StringComparison.Ordinal) &&
        TerminalInput.Contains(w.Query, StringComparison.Ordinal);

    internal bool CanExecuteHistoryMatch => Active is LabWindow w && HasTerminalHistoryMatch(w);

    private bool SearchTerminalInput(LabWindow w, string key, string text)
    {
        if (key is "Enter" or "Esc" or "Left" or "Right")
        {
            bool match = HasTerminalHistoryMatch(w);
            if (key == "Enter" && !match) return FindTerminalHistory(w);
            w.Focus = w.SearchFrom == "terminal" || w.App != "terminal" ? "terminal" : "body";
            if (key == "Esc" || !match)
            {
                TerminalInput = w.SearchDraft;
                w.Select(w.SearchAnchor, w.SearchCaret);
            }
            else
            {
                w.Cursor = TerminalInput.Length;
                if (key == "Left") w.Cursor = PreviousBoundary(TerminalInput, w.Cursor);
            }
            w.Query = ""; w.SearchIndex = -1; w.Status = "";
            Version++;
            return key != "Enter" || Text("Enter", "");
        }
        if (key == "Backspace" && w.Query.Length > 0) w.Query = w.Query[..PreviousBoundary(w.Query, w.Query.Length)];
        else if (text.Length > 0 && !text.Any(char.IsControl) && key != "Backspace") w.Query += text;
        else return key == "Backspace";
        return FindTerminalHistory(w);
    }

    private bool? ApplyWorkspace(string action, LabWindow w)
    {
        if (w == null) return null;
        switch (action)
        {
            case "terminal-search":
                bool alreadySearching = w.Focus == "reverse-search";
                if (!alreadySearching)
                {
                    w.SearchFrom = w.Focus; w.SearchDraft = TerminalInput;
                    w.SearchAnchor = w.Anchor; w.SearchCaret = w.Cursor;
                    historyDraft = TerminalInput;
                    w.Query = ""; w.SearchIndex = commands.Count; w.Focus = "reverse-search";
                }
                if (ReplayLegacyTerminalSearch) return ReverseSearch(w);
                return FindTerminalHistory(w, alreadySearching && w.SearchIndex >= 0);
            case "word-left": case "word-right":
                return Text(action == "word-left" ? "Left" : "Right", "", word: true);
            case "move-line-up": case "move-line-down":
                if (!ReplayLegacyPythonEditing) return MoveEditorBlock(w, action == "move-line-up");
                string body = w.Body;
                int row = body[..Math.Clamp(w.Cursor, 0, body.Length)].Count(ch => ch == '\n');
                var lines = body.Split('\n'); int target = row + (action == "move-line-up" ? -1 : 1);
                if (target < 0 || target >= lines.Length) return false;
                int column = w.Cursor - LineStart(body, w.Cursor);
                Remember(w); (lines[row], lines[target]) = (lines[target], lines[row]); w.Body = string.Join("\n", lines);
                w.Cursor = lines.Take(target).Sum(line => line.Length + 1) + column; return true;
            case "document-end": w.Cursor = w.Body.Length; w.Tab.ExtraMatches.Clear(); return true;
            case "document-start": w.Cursor = 0; w.Tab.ExtraMatches.Clear(); return true;
            case "occurrence":
                if (!w.Selected)
                {
                    int lo = w.Cursor, hi = lo;
                    while (lo > 0 && IsIdentifier(w.Body[lo - 1])) lo--;
                    while (hi < w.Body.Length && IsIdentifier(w.Body[hi])) hi++;
                    if (hi == lo && !ReplayLegacyLineEditing)
                    {
                        // A corrected line comment can leave Ctrl+Home on '#'.
                        // Only skip a leading comment prefix, not arbitrary
                        // punctuation elsewhere in the current line.
                        int prefix = LineStart(w.Body, lo);
                        int limit = LineEnd(w.Body, lo);
                        while (prefix < limit && w.Body[prefix] is ' ' or '\t') prefix++;
                        if (prefix < limit && w.Body[prefix] == '#' && lo <= prefix + 1)
                        {
                            int start = prefix + 1;
                            while (start < limit && w.Body[start] is ' ' or '\t') start++;
                            if (start < limit && IsIdentifier(w.Body[start]))
                            {
                                lo = hi = start;
                                while (hi < limit && IsIdentifier(w.Body[hi])) hi++;
                            }
                        }
                    }
                    w.Select(lo, hi); return hi > lo;
                }
                string selection = Slice(w, w.Body);
                int from = w.Tab.ExtraMatches.Count == 0 ? w.End : w.Tab.ExtraMatches[^1] + selection.Length;
                int match = w.Body.IndexOf(selection, from, StringComparison.Ordinal);
                if (match < 0) return false; w.Tab.ExtraMatches.Add(match); return true;
            case "replace":
                string term = w.Focus == "body" && w.Selected ? Slice(w, w.Body) : w.Find;
                w.Focus = "replace-find"; w.Input = term; w.Select(0, term.Length); return true;
            case "replace-field":
                w.Find = w.Input; w.Focus = "replace-with"; w.Input = ""; w.Cursor = 0; return true;
            case "replace-one":
                string replacement = w.Input;
                if (w.Find.Length == 0 || !FindMatch(w, false)) return false;
                w.Focus = "body"; Insert(w, replacement); return true;
            case "symbols":
                w.Focus = "symbols"; w.OptionIndex = 0; w.Input = ""; w.Cursor = 0; return true;
            case "symbol-next": w.OptionIndex = (w.OptionIndex + 1) % Math.Max(1, Entries(w).Length); return true;
            case "symbol-open":
                var symbol = Entries(w).ElementAtOrDefault(w.OptionIndex);
                if (symbol == null) return false;
                w.Focus = "body"; w.Cursor = int.Parse(symbol.Path); return true;
            case "changes": w.Sidebar = true; w.View = "changes"; w.Focus = "changes"; return true;
            case "focus-editor": case "focus-second":
                int group = action == "focus-editor" ? 0 : 1;
                if (group != w.Group && w.Split)
                {
                    int other = w.Tabs.FindIndex(t => t.Id == w.OtherTab);
                    if (other >= 0) { w.OtherTab = w.Tab.Id; w.ActiveTab = other; w.Group = group; }
                }
                w.Focus = "body"; return true;
            case "history": case "downloads": case "bookmark-manager":
                string internalUrl = "chrome://" + (action == "bookmark-manager" ? "bookmarks" : action);
                int existing = w.Tabs.FindIndex(tab => tab.Url == internalUrl);
                if (existing >= 0) { w.ActiveTab = existing; w.Focus = "body"; w.Item = -1; }
                else { ApplyWindow("new-tab", w); Navigate(w, internalUrl); }
                if (!ReplayLegacyChromeHistory && action == "history") FocusChromeHistory(w, clear: true);
                return true;
            case "item-next": case "item-prev":
                int length = Entries(w).Length;
                if (length == 0) return false;
                w.Item = (w.Item + (action == "item-next" ? 1 : length - 1) + length) % length;
                return true;
            case "item-open":
                var entry = Entries(w).ElementAtOrDefault(w.Item);
                if (entry == null) return false;
                switch (entry.Kind)
                {
                    case "reveal": return RevealDownload(entry.Path, w);
                    case "folder":
                        if (w.App == "code")
                        {
                            if (!w.ExpandedProjectFolders.Add(entry.Path)) w.ExpandedProjectFolders.Remove(entry.Path);
                            SelectPath(w, entry.Path);
                        }
                        else ChangeFolder(w, entry.Path);
                        break;
                    case "download":
                        string path = "/home/student/Downloads/" + entry.Path;
                        for (int copy = 1; Terminal.Exists(path); copy++)
                            path = "/home/student/Downloads/" + System.IO.Path.GetFileNameWithoutExtension(entry.Path) + " (" + copy + ")" + System.IO.Path.GetExtension(entry.Path);
                        Terminal.StoreFile(path, BulletinDownload(entry.Path) ?? BrowserDownloadBody(entry.Path) ?? (NeutralWorkspace ? NeutralProject.ReportBody : ReportBody)); downloads.Insert(0, path);
                        w.Status = "Загружен " + entry.Path; break;
                    case "file":
                        string content = Terminal.ReadFile(entry.Path);
                        if (content == null) { w.Status = "Файл был перемещён или переименован"; return false; }
                        if (content?.StartsWith("data:image/png;base64,") == true)
                        { w.Focus = "image-preview"; w.ImagePreview = content; }
                        else OpenDocument(entry.Path);
                        break;
                    default: Navigate(w, entry.Path); break;
                }
                return true;
            case "file-name-start": return BeginFileName(w);
            case "file-name-cancel": return EndFileName(w, true);
            case "file-name-commit": return EndFileName(w, false);
            case "file-home": ChangeFolder(w, "/home/student"); return true;
            case "file-parent": ChangeFolder(w, w.Folder[..Math.Max(1, w.Folder.LastIndexOf('/'))]); return true;
            case "file-back": case "file-forward":
                var source = action == "file-back" ? w.FolderBack : w.FolderForward;
                if (!source.TryPop(out var dest)) return false;
                (action == "file-back" ? w.FolderForward : w.FolderBack).Push(w.Folder);
                w.Folder = dest; w.Item = -1; return true;
            case "file-copy": case "file-cut":
                var file = Entries(w).ElementAtOrDefault(w.Item);
                if (file == null) return false;
                Clipboard = fileTransfer = file.Path; fileCut = action == "file-cut";
                w.Status = (fileCut ? "К переносу: " : "К копированию: ") + file.Title; return true;
            case "file-paste": return PasteProjectFile(w);
            case "file-undo": return w.App == "code" && w.Focus == "explorer" && UndoProjectFile(w);
            case "trash":
                var selected = Entries(w).ElementAtOrDefault(w.Item);
                if (selected == null) return false;
                // Replay the same path after a restart; never overwrite a file already in the bin.
                string bin;
                long id = trashSequence;
                do { bin = "/home/student/.Trash/" + (++id).ToString("x") + "-" + selected.Title; }
                while (Terminal.Exists(bin) || trash.ContainsKey(bin));
                Terminal.Transfer(selected.Path, bin, true);
                trashSequence = id;
                trash.Add(bin, selected.Path); w.Item = -1; return true;
            case "restore-trash":
                if (trash.Count == 0) return false;
                var last = trash.Last();
                if (Terminal.Exists(last.Value)) return false;
                Terminal.Transfer(last.Key, last.Value, true); trash.Remove(last.Key); return true;
        }
        return null;
    }

    private static void ChangeFolder(LabWindow w, string path)
    {
        if (path != w.Folder) { w.FolderBack.Push(w.Folder); w.FolderForward.Clear(); w.Folder = path; }
        w.Item = -1;
    }
}
