using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;

namespace TouchTypingStudio
{
    internal static class DesktopCourseWindows
    {
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint relationship);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        public static bool Minimized(IntPtr hwnd) { return hwnd != IntPtr.Zero && IsIconic(hwnd); }

        public static bool OnDesktop(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (hwnd == GetShellWindow()) return true;
            string kind = Native.ClassName(hwnd);
            return (kind == "Progman" || kind == "WorkerW") && Native.IsProcess(hwnd, "explorer");
        }

        public static bool StartMenu(IntPtr hwnd)
        {
            return Native.IsProcess(hwnd, "StartMenuExperienceHost") || Native.IsProcess(hwnd, "SearchApp") ||
                Native.IsProcess(hwnd, "SearchHost") || Native.IsProcess(hwnd, "SearchUI") ||
                Native.IsProcess(hwnd, "ShellExperienceHost");
        }

        public static bool InFileApp(IntPtr hwnd, string process, string file)
        {
            if (hwnd == IntPtr.Zero || !Native.IsProcess(hwnd, process)) return false;
            string title = Native.Title(hwnd);
            string name = Path.GetFileName(file);
            if (!string.Equals(process, "notepad", StringComparison.OrdinalIgnoreCase))
                return title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

            // Classic Notepad can use the Explorer display name with .txt hidden.
            // A full caption segment also avoids passing a similarly named note.
            string display = title.TrimStart('*', ' ', '●');
            return DocumentCaption(display, name) ||
                (string.Equals(Path.GetExtension(name), ".txt", StringComparison.OrdinalIgnoreCase) &&
                 DocumentCaption(display, Path.GetFileNameWithoutExtension(name)));
        }

        private static bool DocumentCaption(string caption, string name)
        {
            return string.Equals(caption, name, StringComparison.OrdinalIgnoreCase) ||
                caption.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase) ||
                caption.StartsWith(name + " – ", StringComparison.OrdinalIgnoreCase) ||
                caption.StartsWith(name + " — ", StringComparison.OrdinalIgnoreCase);
        }

        public static string PracticeConsoleCommand(string workingDirectory, string title)
        {
            var console = PracticeConsoleStart(workingDirectory, title);
            var directory = new DirectoryInfo(console.WorkingDirectory);

            // The shell link carries the path as a working directory, separate from
            // command syntax. Spaces, Cyrillic and &, %, ! in a profile path stay literal.
            string shortcut = Path.Combine(directory.Parent.FullName, "cmd-" + title + ".lnk");
            if (File.Exists(shortcut)) throw new IOException("Учебный ярлык уже существует.");
            object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                link = ((dynamic)shell).CreateShortcut(shortcut);
                dynamic properties = link;
                properties.TargetPath = console.FileName;
                properties.WorkingDirectory = directory.FullName;
                properties.Arguments = console.Arguments;
                properties.Description = "Командная строка в папке этого учебного задания";
                properties.Save();
            }
            catch (COMException error)
            {
                throw new IOException("Windows не удалось создать ярлык учебной командной строки.", error);
            }
            finally { Release(link); Release(shell); }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\') + "\\";
            if (shortcut.StartsWith(local, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(local.TrimEnd('\\'), Environment.GetEnvironmentVariable("LOCALAPPDATA"), StringComparison.OrdinalIgnoreCase))
                shortcut = @"%LOCALAPPDATA%\" + shortcut.Substring(local.Length);
            return "\"" + shortcut + "\"";
        }

        public static IDisposable OpenPracticeConsole(string workingDirectory, string title)
        {
            return new DesktopConsoleSession(Process.Start(PracticeConsoleStart(workingDirectory, title)), title);
        }

        private static ProcessStartInfo PracticeConsoleStart(string workingDirectory, string title)
        {
            if (string.IsNullOrEmpty(title) || title != title.Trim() ||
                !Regex.IsMatch(title, @"\A[a-zA-Z0-9 ._-]{1,72}\z"))
                throw new ArgumentException("Недопустимое имя учебного окна.", "title");

            var directory = new DirectoryInfo(workingDirectory);
            if (directory.Parent == null || !directory.Exists ||
                (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                (directory.Parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Учебная папка недоступна.");
            return new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /e:on /v:off /k prompt CMD$G$S&title " + title,
                WorkingDirectory = directory.FullName, UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        public static bool IsPracticeConsole(IntPtr hwnd, string expectedTitle)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(expectedTitle)) return false;
            string kind = Native.ClassName(hwnd);
            if (kind != "ConsoleWindowClass" &&
                (kind != "CASCADIA_HOSTING_WINDOW_CLASS" || !Native.IsProcess(hwnd, "WindowsTerminal")))
                return false;
            string title = Native.Title(hwnd);
            // cmd can append a running external program to the title.
            return string.Equals(title, expectedTitle, StringComparison.Ordinal) ||
                title.StartsWith(expectedTitle + " - ", StringComparison.Ordinal);
        }

        public static bool WindowTitle(IntPtr hwnd, string process, string expected)
        {
            return hwnd != IntPtr.Zero && Native.IsProcess(hwnd, process) &&
                Native.Title(hwnd).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsChromeCaption(string caption, string expected)
        {
            return caption == expected || caption == expected + " - Google Chrome" ||
                caption == expected + " – Google Chrome" || caption == expected + " - Chromium";
        }

        public static bool EditorDialog(IntPtr active, IntPtr editor)
        {
            if (active == IntPtr.Zero || editor == IntPtr.Zero || active == editor || Native.ClassName(active) != "#32770")
                return false;
            const uint owner = 4;
            const uint rootOwner = 3;
            return GetWindow(active, owner) == editor || GetAncestor(active, rootOwner) == editor;
        }

        public static bool InExplorerSurface(IntPtr active, IntPtr explorer)
        {
            return active != IntPtr.Zero && explorer != IntPtr.Zero &&
                (active == explorer || GetAncestor(active, 3) == explorer);
        }

        public static bool HasTextFocus()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null) return false;
                var type = focused.Current.ControlType;
                if (type == ControlType.Edit) return true;
                object value;
                return focused.TryGetCurrentPattern(ValuePattern.Pattern, out value) && !((ValuePattern)value).Current.IsReadOnly;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        public static bool FocusedValueIs(string expected)
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                object value;
                return focused != null && focused.TryGetCurrentPattern(ValuePattern.Pattern, out value) &&
                    string.Equals(((ValuePattern)value).Current.Value, expected, StringComparison.Ordinal);
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        // The Windows-wide UIA focus can lag behind Chrome's tab or search box.
        // Read focus from this training window's own accessibility tree.
        public static bool ChromeEditFocused(IntPtr hwnd, bool addressBar, string expected = null)
        {
            if (!Native.IsProcess(hwnd, "chrome")) return false;
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                var edit = window.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)));
                if (edit == null) return false;
                // A page input, the omnibox and the Find widget are all UIA Edit controls.
                // Chrome exposes Ctrl+L on its omnibox even in a translated interface.
                bool location = string.Equals(edit.Current.AcceleratorKey, "Ctrl+L", StringComparison.OrdinalIgnoreCase);
                for (var node = edit; node != null && !node.Equals(window); node = TreeWalker.RawViewWalker.GetParent(node))
                {
                    if (node.Current.ControlType == ControlType.Document) return false;
                    string kind = node.Current.ClassName ?? "";
                    if (kind.IndexOf("Omnibox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        kind.IndexOf("LocationBar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        node.Current.ControlType == ControlType.ComboBox) location = true;
                }
                if (location != addressBar) return false;
                if (expected == null) return true;
                object pattern;
                return edit.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) &&
                    string.Equals(((ValuePattern)pattern).Current.Value, expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        public static bool CodeExplorerRowFocused(IntPtr hwnd, string file)
        {
            return CodeFocusMatches(hwnd, (focused, node) =>
            {
                if (node.Current.ControlType != ControlType.Tree) return false;
                string identity = (node.Current.ClassName ?? "") + " " + (node.Current.AutomationId ?? "");
                if (identity.IndexOf("explorer", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !string.Equals(node.Current.Name, "Files Explorer", StringComparison.Ordinal)) return false;
                if (focused.Current.ControlType != ControlType.TreeItem) return false;
                if (file == null) return true;
                return string.Equals(focused.Current.Name, file, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static bool CodeEditorFocused(IntPtr hwnd)
        {
            return CodeFocusMatches(hwnd, (focused, node) =>
                (node.Current.ClassName ?? "").Split(' ').Contains("monaco-editor"));
        }

        public static bool CodeAccessibleViewFocused(IntPtr hwnd)
        {
            return CodeFocusMatches(hwnd, (focused, node) =>
                (node.Current.ClassName ?? "").Split(' ').Contains("accessible-view") ||
                (node.Current.ControlType == ControlType.Edit &&
                 (node.Current.Name ?? "").StartsWith("Accessible View", StringComparison.Ordinal)));
        }

        public static bool CodeTerminalFocused(IntPtr hwnd)
        {
            return CodeFocusMatches(hwnd, (focused, node) =>
                (node.Current.ClassName ?? "").Split(' ').Any(name => name == "xterm" || name == "terminal-wrapper"));
        }

        private static bool CodeFocusMatches(IntPtr hwnd, Func<AutomationElement, AutomationElement, bool> matches)
        {
            if (Native.GetForegroundWindow() != hwnd || !Native.IsProcess(hwnd, "Code")) return false;
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                var focused = AutomationElement.FocusedElement;
                for (var node = focused; node != null; node = TreeWalker.RawViewWalker.GetParent(node))
                {
                    if (!node.Equals(window)) continue;
                    for (node = focused; node != null && !node.Equals(window); node = TreeWalker.RawViewWalker.GetParent(node))
                        if (matches(focused, node)) return true;
                    return false;
                }
                return false;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        public static bool CodeQuickInputFocused(IntPtr hwnd)
        {
            if (!Native.IsProcess(hwnd, "Code")) return false;
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                // Go to Line can expose its selected suggestion as focused instead of the text field.
                var focused = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
                for (var node = focused; node != null && !node.Equals(window); node = TreeWalker.RawViewWalker.GetParent(node))
                    if ((node.Current.ClassName ?? "").IndexOf("quick-input", StringComparison.Ordinal) >= 0) return true;
                return false;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        public static bool ChromePageFocused(IntPtr hwnd, string pageTitle)
        {
            if (!Native.IsProcess(hwnd, "chrome")) return false;
            try
            {
                var window = AutomationElement.FromHandle(hwnd);
                var page = window.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.NameProperty, pageTitle)));
                return page != null && (page.Current.HasKeyboardFocus || page.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true)) != null);
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();

        internal static bool FreshClipboardFrom(IntPtr window, uint before)
        {
            uint after = Native.GetClipboardSequenceNumber();
            return before != 0 && after != 0 && before != after &&
                CanReadNewClipboard(before, after, Native.GetForegroundWindow() == window,
                    Native.SameWindowProcess(window, GetClipboardOwner()));
        }

        internal static bool CanReadNewClipboard(uint before, uint after, bool foreground, bool sameOwner)
        {
            return before != 0 && after != 0 && before != after && foreground && sameOwner;
        }

        public static bool ClipboardContainsOnly(params string[] expected)
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return false;
                var files = Clipboard.GetFileDropList();
                return files.Count == expected.Length &&
                    new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase).SetEquals(files.Cast<string>());
            }
            catch (ExternalException) { return false; }
        }

        public static bool SelectionContains(IntPtr hwnd, params string[] expected)
        {
            object shell = null, collection = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                collection = ((dynamic)shell).Windows();
                int count = ((dynamic)collection).Count;
                for (int i = 0; i < count; i++)
                {
                    object child = null, document = null, selection = null;
                    try
                    {
                        child = ((dynamic)collection).Item(i);
                        if (child == null || (long)((dynamic)child).HWND != hwnd.ToInt64()) continue;
                        document = ((dynamic)child).Document;
                        selection = ((dynamic)document).SelectedItems();
                        if ((int)((dynamic)selection).Count != expected.Length) return false;
                        var wanted = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
                        for (int j = 0; j < expected.Length; j++)
                        {
                            object file = ((dynamic)selection).Item(j);
                            try { if (file == null || !wanted.Remove((string)((dynamic)file).Path)) return false; }
                            finally { Release(file); }
                        }
                        return wanted.Count == 0;
                    }
                    finally { Release(selection); Release(document); Release(child); }
                }
            }
            catch (COMException) { }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            finally { Release(collection); Release(shell); }
            return false;
        }

        public static string FindChrome()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (string prefix in new[] { local, common, x86 })
            {
                if (string.IsNullOrEmpty(prefix)) continue;
                string candidate = Path.Combine(prefix, "Google", "Chrome", "Application", "chrome.exe");
                if (File.Exists(candidate)) return candidate;
            }
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"))
            {
                string candidate = key == null ? "" : key.GetValue(null) as string;
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static void Release(object instance)
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }
}
