using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LabCatalog
{
    internal static bool Available(LabTask task, LabScene scene)
    {
        if (task.Id.StartsWith("scm-", StringComparison.Ordinal)) return scene.GitActionAvailable(task.Action);
        if (task.Id.StartsWith("win-", StringComparison.Ordinal)) return scene.WindowsActionAvailable(task.Action);
        if (scene.ChromeTabActionAvailable(task.Action) is bool tabAvailable) return tabAvailable;
        var window = scene.Active;
        bool terminal = window != null && (window.App == "terminal" || window.Focus == "terminal");
        if (terminal && scene.Terminal.FollowingLog) return task.Id == "terminal-interrupt";
        bool fileList = window?.App == "files" && window.Focus == "body" || window?.App == "code" && window.Focus == "explorer";
        bool fileResults = window?.App == "files" && window.Focus == "file-results";
        return task.Id switch
        {
            "file-search-open" or "file-search-win-alias" => !scene.ReplayLegacyDesktopFileSearch &&
                (task.Id != "file-search-win-alias" || scene.WindowsDesktop) &&
                window?.App == "files" && (window.Focus == "body" || LabScene.IsFileSearch(window)),
            "file-search-close" => LabScene.IsFileSearch(window),
            "file-search-to-results" => window?.App == "files" && window.Focus == "file-search",
            "file-search-to-field" => fileResults,
            "file-name-commit" or "file-name-cancel" => window?.Focus == "file-name",
            "file-name-start" => fileList && scene.Entries(window).ElementAtOrDefault(window.Item)?.Kind == "file",
            "file-next" or "file-prev" => fileList || LabScene.IsFileSearch(window),
            "file-paste" => fileList,
            "file-undo" => window?.App == "code" && window.Focus == "explorer" && scene.CanUndoProjectFile && !scene.ReplayLegacyNotebookActions,
            "code-clone-command" => scene.BrowserPracticeActive && window?.Focus == "palette" && window.Input.Trim().Equals("Git: Clone", StringComparison.OrdinalIgnoreCase),
            "code-clone-confirm" => scene.BrowserPracticeActive && window?.Focus == "clone-url",
            "code-problem-next" or "code-fix-open" or "code-fix-next" or "code-fix-prev" or
            "code-fix-apply" or "code-fix-cancel" => scene.DiagnosticActionAvailable(task.Action),
            "code-document-end" => scene.ContextHandoffActive && window?.App == "code" && window.Focus == "body",
            "document-start" or "move-line-up" or "move-line-down" or "occurrence" => window?.App == "code" && window.Focus == "body",
            "exact-find-submit" or "exact-find-next" or "exact-find-prev" => scene.ExactEdit && window?.Focus == "find",
            "exact-call-home" => scene.ExactEdit && window?.App == "code" && window.Focus == "body",
            "project-search-submit" => window?.Focus == "project-search" && !string.IsNullOrWhiteSpace(window.Input),
            "project-search-next" or "project-search-prev" or "project-search-open" => window?.Focus == "project-results",
            "project-search-repair" => scene.SearchRepairExercise && window?.App == "code" && window.Focus == "body" && window.Tab?.SavedBody != window.Body,
            "code-definition" or "code-references" or "code-rename" => window?.App == "code" && window.Focus == "body",
            "code-back" => window?.App == "code" && window.Focus == "body" && window.SymbolBack.Count > 0,
            "code-reference-prev" or "code-reference-next" or "code-reference-open" => window?.Focus == "symbol-references",
            "code-rename-cancel" or "code-rename-confirm" => window?.Focus == "symbol-rename",
            "replace-field" => window?.Focus == "replace-find",
            "replace-one" => window?.Focus == "replace-with",
            "symbol-next" or "symbol-prev" or "symbol-open" => window?.Focus == "symbols",
            "file-open" or "file-copy" or "file-cut" => (fileList || fileResults) && window.Item >= 0,
            "trash" => window?.App == "files" && window.Focus == "body" && window.Item >= 0,
            "link-open" => window?.App == "chrome" && (window.Focus == "body" || LabScene.OnChromeHistory(window) && window.Focus == "history")
                && window.Item >= 0 && window.Item < scene.Entries(window).Length,
            "link-next" => window?.App == "chrome" && (window.Focus == "body" || LabScene.OnChromeHistory(window) && window.Focus == "history")
                && (!LabScene.OnChromeHistory(window) || scene.Entries(window).Length > 0),
            "link-prev" => window?.App == "chrome" && (window.Focus == "body" || LabScene.OnChromeHistory(window) &&
                window.Focus == "history" && window.Item > 0),
            "switch-next" or "switch-prev" => scene.Windows.Count(item => item.Workspace == scene.Workspace)
                >= (scene.GnomeWindows && window == null ? 1 : 2),
            "switch-group-next" or "switch-group-prev" => scene.GnomePractice == "same-app" && scene.GnomeGroupSize >= 2,
            "workspace-next" when scene.GnomePractice == "workspaces" => scene.Workspace + 1 < scene.WorkspaceCount,
            "workspace-prev" when scene.GnomePractice == "workspaces" => scene.Workspace > 0,
            "move-next" when scene.GnomePractice == "workspaces" => window != null && scene.Workspace + 1 < scene.WorkspaceCount,
            "move-prev" when scene.GnomePractice == "workspaces" => window != null && scene.Workspace > 0,
            "close-window" => window != null,
            "open-chrome" => window?.App != "chrome",
            "open-code" => window?.App != "code",
            "open-terminal" => window?.App != "terminal",
            "open-files" => window?.App != "files",
            "maximize" => window != null && window.State != "maximized",
            "restore" => window != null && window.State != "normal",
            "tile-left" => window != null && (scene.GnomeWindows || window.State != "left"),
            "tile-right" => window != null && (scene.GnomeWindows || window.State != "right"),
            "minimize" => window != null && (scene.GnomeWindows || scene.Windows.Any(
                item => item.Workspace == scene.Workspace && item.Id != window.Id && item.State != "minimized")),
            "move-next" or "move-prev" => window != null,
            "copy-shot" => scene.ScreenshotReady && !scene.ScreenshotCopied,
            "screenshot-confirm" => scene.Screenshot,
            "debug-window-shot" => scene.DebugDay && window?.App == "terminal" && scene.DebugTestCurrent,
            "debug-next-line" or "debug-report-end" => scene.DebugDay && window?.App == "code" && window.Focus == "body",
            "escape" => scene.Overview || scene.Screenshot || scene.ScreenshotReady || window?.Focus is not (null or "body"),
            "new-tab" => window?.App == "chrome" && window.Tabs.Count < scene.BrowserTabLimit,
            "url" => window?.App == "chrome" && !scene.Overview && !scene.Switcher && !scene.Screenshot,
            "navigate-pasted" => window?.App == "chrome" && window.Focus == "url" && !string.IsNullOrWhiteSpace(window.Input),
            "refresh" or "fullscreen" or "bookmarks" => window?.App == "chrome",
            "zoom-in" => window?.App == "chrome" && window.Zoom < 200,
            "zoom-out" => window?.App == "chrome" && window.Zoom > 50,
            "bookmark" => window?.App == "chrome" && !window.SavedBookmarks.Contains(window.Url),
            "new-file" => window?.App == "code" && window.Tabs.Count < 6,
            "close-tab" or "next-tab" or "prev-tab" => window?.Tabs.Count > 1,
            "first-tab" => window?.ActiveTab > 0,
            "last-tab" => window != null && window.ActiveTab < window.Tabs.Count - 1,
            "restore-tab" => window?.ClosedTabs.Count > 0,
            "back" => window?.Tab?.HistoryIndex > 0,
            "forward" => window?.Tab != null && window.Tab.HistoryIndex < window.Tab.History.Count - 1,
            "zoom-reset" => window?.Zoom != 100,
            "find" => window != null && !terminal && window.Focus != "find",
            "find-next" => window?.Find.Length > 0,
            "panel" or "extensions" or "quick-open" or "palette" or "goto-line" or "explorer" or "project-search" or "sidebar" or "split" or "terminal-toggle" => window?.App == "code",
            "quick-next" or "quick-prev" or "quick-choose" => window?.App == "code" && window.Focus == "quick-open" && scene.Entries(window).Length > 0,
            "palette-next" or "palette-prev" or "palette-choose" => window?.App == "code" && window.Focus == "palette",
            "save" => window?.App == "code" && window.Tab?.Body != window.Tab?.SavedBody,
            "undo" => window?.Tab?.Undo.Count > 0,
            "redo" => window?.Tab?.Redo.Count > 0,
            "select-line" or "comment" or "delete-line" => window?.App == "code" && window.Body.Length > 0 && window.Focus == "body",
            "select-all" => window != null && !window.Selected && (window.Focus == "body" ? window.Body.Length > 0 : window.Input.Length > 0),
            "copy" => window != null && (window.Selected || window.App == "code" && window.Focus == "body" && window.Body.Length > 0),
            "cut" => window != null && (window.Focus == "body" ? window.App == "code" && window.Body.Length > 0 : window.Selected),
            "paste" => window != null && scene.Clipboard.Length > 0 && (window.App == "code" || window.App == "chrome" && window.Focus != "body" || window.App == "files" && window.Focus == "file-search"),
            "terminal-copy" => terminal && window.Selected,
            "terminal-interrupt" => false, // only an active foreground log reader can be interrupted
            "terminal-clear" or "terminal-yank" => terminal,
            "terminal-paste" => terminal && scene.Clipboard.Length > 0,
            "terminal-cancel" or "terminal-erase" or "terminal-kill" or "terminal-home" or "terminal-end" => terminal && scene.TerminalInput.Length > 0,
            "terminal-submit" => terminal && window.Focus != "reverse-search" && scene.TerminalInput.Length > 0,
            "terminal-history-up" => terminal && scene.CanHistoryUp,
            "terminal-history-down" => terminal && scene.CanHistoryDown,
            _ => true
        };
    }

    internal static LabTask Resolve(string key, IEnumerable<string> held, LabScene scene)
    {
        if (scene.WindowsDesktop)
        {
            var windowsTask = WindowsTasks.Skip(1).Where(task => Available(task, scene)).FirstOrDefault(task =>
            {
                var chord = Gesture.Parse(task.Keys).Chords[0];
                return chord.Trigger == key && chord.FitsModifiers(held);
            });
            if (windowsTask != null || scene.DesktopOverlay.Length > 0 || scene.ShowDesktop) return windowsTask;
        }
        string app = scene.Active?.App ?? "ubuntu";
        bool terminal = app == "terminal" || scene.Active?.Focus is "terminal" or "reverse-search";
        HashSet<string> context = terminal ? TerminalIds : app switch
        { "chrome" => ChromeIds, "code" => CodeIds, "files" => FilesIds, _ => UbuntuIds };
        int Priority(LabTask task)
        {
            if (scene.ChromeTabActionAvailable(task.Action) == true) return 2;
            if ((scene.Active?.Focus is "file-name" or "explorer" or "file-search" or "file-results") &&
                task.Id.StartsWith("file-", StringComparison.Ordinal)) return 3;
            if (task.Id.StartsWith("code-fix-", StringComparison.Ordinal) && scene.DiagnosticActionAvailable(task.Action))
                return 2;
            if (task.Id == "copy-shot" && scene.ScreenshotReady && !scene.ScreenshotCopied) return 2;
            if (task.Id == "screenshot-confirm" && scene.Screenshot) return 2;
            if (scene.GnomePractice is "workspaces" or "same-app" && LabScene.GnomeProfileActions.Contains(task.Id) &&
                scene.DesktopProfile.Any(entry => entry.StartsWith(task.Id + "=", StringComparison.Ordinal))) return 3;
            return context.Contains(task.Id) ? 1 : 0;
        }
        if (app == "chrome")
        {
            string alias = key == "D" && Gesture.Parse("Alt+D").Chords[0].FitsModifiers(held) ? "url" :
                (key is "PageUp" or "PageDown") && Gesture.Parse("Ctrl+Tab").Chords[0].FitsModifiers(held)
                    ? (key == "PageUp" ? "prev-tab" : "next-tab") : null;
            if (alias != null)
            {
                var task = Shortcuts.FirstOrDefault(task => task.Id == alias && Available(task, scene));
                return task == null ? null : task with { Keys = alias == "url" ? "Alt+D" : "Ctrl+" + key };
            }
        }
        var ordered = Shortcuts.Select(scene.WithBinding).Where(task => task.Keys.Length > 0).Where(task => task.Id != "copy-shot" || scene.ScreenshotReady && !scene.ScreenshotCopied)
            .Where(task => task.Id != "screenshot-confirm" || scene.Screenshot)
            .OrderByDescending(Priority);
        return ordered.FirstOrDefault(task =>
        {
            var chord = Gesture.Parse(task.Keys).Chords[0];
            bool trigger = chord.Trigger == null ? KeyChord.IsModifier(key) : chord.Trigger == key;
            if (!trigger || !chord.FitsModifiers(held) || !Available(task, scene)) return false;
            if (scene.Active?.Focus == "file-name") return task.Id is "file-name-commit" or "file-name-cancel" or "select-all" or "copy" or "cut" or "paste";
            if (scene.Active?.Focus == "file-search" && task.Id is "select-all" or "copy" or "cut" or "paste") return true;
            if (scene.DebugDay && task.Id.StartsWith("debug-", StringComparison.Ordinal)) return true;
            if (scene.ExactEdit && task.Id.StartsWith("exact-", StringComparison.Ordinal)) return true;
            if (task.Id == "escape" && (scene.BrowserResearchActive || scene.ContextHandoffActive) && context.Contains(task.Id))
                return true; // Esc closes the app's find bar when no Windows overlay has focus.
            if (UbuntuIds.Contains(task.Id) || task.Id is "switch-group-next" or "switch-group-prev") return !scene.WindowsDesktop;
            if (app == "code" && !terminal && task.Id == "restore-tab" && scene.GuideNoteActive && scene.Active.Focus == "body")
                return true;
            return context.Contains(task.Id) || terminal && app == "code" && task.Id is "terminal-toggle" or "panel" or "escape";
        });
    }
}
