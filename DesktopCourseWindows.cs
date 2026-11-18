using System;
using System.IO;
using System.Runtime.InteropServices;
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
            return WindowTitle(hwnd, process, Path.GetFileName(file));
        }

        public static bool WindowTitle(IntPtr hwnd, string process, string expected)
        {
            return hwnd != IntPtr.Zero && Native.IsProcess(hwnd, process) &&
                Native.Title(hwnd).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool EditorDialog(IntPtr active, IntPtr editor)
        {
            if (active == IntPtr.Zero || editor == IntPtr.Zero || active == editor || Native.ClassName(active) != "#32770")
                return false;
            const uint owner = 4;
            const uint rootOwner = 3;
            return GetWindow(active, owner) == editor || GetAncestor(active, rootOwner) == editor;
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

        public static bool ClipboardContainsOnly(string expected)
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return false;
                var files = Clipboard.GetFileDropList();
                return files.Count == 1 && string.Equals(files[0], expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (ExternalException) { return false; }
        }

        public static bool SelectionContains(IntPtr hwnd, string expected)
        {
            object shell = null, collection = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                collection = ((dynamic)shell).Windows();
                int count = ((dynamic)collection).Count;
                for (int i = 0; i < count; i++)
                {
                    object child = null, document = null, selection = null, file = null;
                    try
                    {
                        child = ((dynamic)collection).Item(i);
                        if (child == null || (long)((dynamic)child).HWND != hwnd.ToInt64()) continue;
                        document = ((dynamic)child).Document;
                        selection = ((dynamic)document).SelectedItems();
                        if ((int)((dynamic)selection).Count != 1) return false;
                        file = ((dynamic)selection).Item(0);
                        return file != null && string.Equals((string)((dynamic)file).Path, expected, StringComparison.OrdinalIgnoreCase);
                    }
                    finally { Release(file); Release(selection); Release(document); Release(child); }
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
