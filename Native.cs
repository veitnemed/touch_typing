using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace TouchTypingStudio
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowEnumerator callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint vk, uint scanCode,
            byte[] state, StringBuilder result, int count, uint flags, IntPtr layout);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int hook, Hook procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out PhysicalPoint point);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

        internal delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        private delegate bool WindowEnumerator(IntPtr window, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PhysicalPoint { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRectangle Monitor;
            public NativeRectangle Work;
            public uint Flags;
        }

        internal static IntPtr Handle(Window window) { return new WindowInteropHelper(window).Handle; }

        internal static Layout ActiveLayout() { return Language(GetKeyboardLayout(0)); }

        internal static bool IsCapsLock() { return (GetKeyState(0x14) & 1) != 0; }

        internal static char? TypedLetter(IntPtr hwnd, uint vk, uint scan, bool shift, bool caps)
        {
            uint pid;
            uint thread = GetWindowThreadProcessId(hwnd, out pid);
            if (thread == 0) return null;
            var keyboardState = new byte[256];
            keyboardState[0x10] = shift ? (byte)0x80 : (byte)0;
            keyboardState[0x14] = caps ? (byte)1 : (byte)0;
            var text = new StringBuilder(4);
            int length = ToUnicodeEx(vk, scan, keyboardState, text, text.Capacity, 4, GetKeyboardLayout(thread));
            return length == 1 ? (char?)text[0] : null;
        }

        internal static Layout LayoutAt(IntPtr window)
        {
            uint pid;
            uint thread = GetWindowThreadProcessId(window, out pid);
            return thread == 0 ? Layout.Other : Language(GetKeyboardLayout(thread));
        }

        private static Layout Language(IntPtr hkl)
        {
            // Match the actual standard keyboard layouts, not every English or Russian IME.
            long id = hkl.ToInt64() & 0xffffffff;
            if (id == 0x04090409) return Layout.English;
            if (id == 0x04190419) return Layout.Russian;
            return Layout.Other;
        }

        internal static string LayoutStatus()
        {
            int count = GetKeyboardLayoutList(0, null);
            var ids = new IntPtr[count];
            GetKeyboardLayoutList(count, ids);
            var available = ids.Select(Language).ToArray();
            return (available.Contains(Layout.English) ? "EN QWERTY ✓" : "EN QWERTY не найдена")
                + "   ·   " + (available.Contains(Layout.Russian) ? "RU ЙЦУКЕН ✓" : "RU ЙЦУКЕН не найдена");
        }

        internal static string ClassName(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        internal static string Title(IntPtr hwnd)
        {
            var name = new StringBuilder(512);
            GetWindowText(hwnd, name, name.Capacity);
            return name.ToString();
        }

        internal static bool IsProcess(IntPtr hwnd, string processName)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                    return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static bool IsExplorer(IntPtr hwnd)
        {
            var name = ClassName(hwnd);
            return name == "CabinetWClass" || name == "ExploreWClass";
        }

        internal static bool IsSettings(IntPtr hwnd)
        {
            if (IsProcess(hwnd, "SystemSettings")) return true;
            if (!IsProcess(hwnd, "ApplicationFrameHost")) return false;

            bool found = false;
            EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr data)
            {
                found = IsProcess(child, "SystemSettings");
                return !found;
            }, IntPtr.Zero);
            return found;
        }

        internal static bool IsRunDialog(IntPtr hwnd)
        {
            return ClassName(hwnd) == "#32770" && IsProcess(hwnd, "explorer");
        }

        internal static bool AtScreenHalf(IntPtr window, bool left)
        {
            NativeRectangle bounds;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetWindowRect(window, out bounds) || !GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return false;
            int half = (info.Work.Right - info.Work.Left) / 2;
            int edge = left ? info.Work.Left : info.Work.Right;
            return Math.Abs((left ? bounds.Left : bounds.Right) - edge) <= 28 &&
                Math.Abs((bounds.Right - bounds.Left) - half) <= 35 &&
                Math.Abs(bounds.Top - info.Work.Top) <= 28 && Math.Abs(bounds.Bottom - info.Work.Bottom) <= 28;
        }

        internal static void NoActivate(IntPtr hwnd)
        {
            const int index = -20;
            const int noActivateToolWindow = 0x08000080;
            if (IntPtr.Size == 8) SetLongPtr(hwnd, index, new IntPtr(GetLongPtr(hwnd, index).ToInt64() | noActivateToolWindow));
            else SetLong(hwnd, index, GetLong(hwnd, index) | noActivateToolWindow);
        }

        internal static void PlaceCard(IntPtr window, bool lowerRight, int shiftX = 0, int shiftY = 0)
        {
            NativeRectangle rect;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            var monitor = MonitorFromWindow(window, 2);
            if (!GetWindowRect(window, out rect) || !GetMonitorInfo(monitor, ref info)) return;

            int right = info.Work.Right - (rect.Right - rect.Left);
            int bottom = info.Work.Bottom - (rect.Bottom - rect.Top);
            int x = lowerRight ? right - 14 : rect.Left + shiftX;
            int y = lowerRight ? bottom - 16 : rect.Top + shiftY;
            SetWindowPos(window, IntPtr.Zero, Math.Max(info.Work.Left, Math.Min(x, right)),
                Math.Max(info.Work.Top, Math.Min(y, bottom)), 0, 0, 0x15);
        }

        internal static bool NearWindow(IntPtr window, PhysicalPoint point)
        {
            NativeRectangle r;
            return GetWindowRect(window, out r) && point.X >= r.Left && point.X < r.Right &&
                point.Y >= r.Top && point.Y < r.Bottom;
        }

        internal static PhysicalPoint Cursor()
        {
            PhysicalPoint point;
            GetCursorPos(out point);
            return point;
        }

        internal static void MoveCard(IntPtr window, int x, int y)
        { SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x15); }

        internal static PhysicalPoint Corner(IntPtr window)
        {
            NativeRectangle r;
            GetWindowRect(window, out r);
            return new PhysicalPoint { X = r.Left, Y = r.Top };
        }

        internal static string ExplorerFolder(IntPtr hwnd)
        {
            object shell = null, collection = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic application = shell;
                collection = application.Windows();
                dynamic windows = collection;
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = windows.Item(i);
                        dynamic window = item;
                        if (window == null || (long)window.HWND != hwnd.ToInt64()) continue;
                        string url = window.LocationURL;
                        Uri parsed;
                        if (Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.IsFile) return parsed.LocalPath;
                        object document = null, folder = null, self = null;
                        try
                        {
                            document = window.Document;
                            folder = ((dynamic)document).Folder;
                            self = ((dynamic)folder).Self;
                            return ((dynamic)self).Path as string;
                        }
                        finally { Release(self); Release(folder); Release(document); }
                    }
                    catch (COMException) { }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                    finally { Release(item); }
                }
            }
            catch (COMException) { }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            finally { Release(collection); Release(shell); }
            return null;
        }

        private static void Release(object rcw) { if (rcw != null && Marshal.IsComObject(rcw)) Marshal.ReleaseComObject(rcw); }
    }

    internal sealed class RuntimeTools
    {
        public static readonly RuntimeTools Current = new RuntimeTools();
        public readonly string Code, Python;
        private RuntimeTools()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programs = Path.Combine(local, "Programs");
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            Code = Existing(Path.Combine(programs, "Microsoft VS Code", "Code.exe"),
                Path.Combine(pf, "Microsoft VS Code", "Code.exe"), Path.Combine(pf86, "Microsoft VS Code", "Code.exe"), AppPath("Code.exe"));
            Python = Existing(RegisteredPython(), LocalPython(programs), AppPath("python.exe"));
        }

        private static string Existing(params string[] paths) { return paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)); }

        private static string AppPath(string name)
        {
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name))
            {
                string path = key == null ? null : (key.GetValue(null) as string);
                if (File.Exists(path)) return path;
            }
            foreach (string candidate in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var dir = candidate.Trim().Trim('"');
                if (dir.Length == 0 || dir.EndsWith("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                string full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        private static string RegisteredPython()
        {
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            using (var root = hive.OpenSubKey(@"SOFTWARE\Python\PythonCore"))
            {
                if (root == null) continue;
                foreach (string version in root.GetSubKeyNames().OrderByDescending(PythonVersion))
                using (var install = root.OpenSubKey(version + @"\InstallPath"))
                {
                    if (install == null) continue;
                    string path = install.GetValue("ExecutablePath") as string;
                    if (File.Exists(path)) return path;
                    string dir = install.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "python.exe")))
                        return Path.Combine(dir, "python.exe");
                }
            }
            return null;
        }

        private static Version PythonVersion(string name)
        {
            Version version;
            return Version.TryParse(name.Split('-')[0], out version) ? version : new Version(0, 0);
        }

        private static string LocalPython(string programs)
        {
            string root = Path.Combine(programs, "Python");
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root, "Python*").OrderByDescending(dir => dir)
                .Select(dir => Path.Combine(dir, "python.exe")).FirstOrDefault(File.Exists);
        }

        internal static void OpenApp(string path, string arguments, string dir = null)
        {
            using (Process.Start(new ProcessStartInfo
            {
                FileName = path, Arguments = arguments, UseShellExecute = false, WorkingDirectory = dir ?? "",
                WindowStyle = ProcessWindowStyle.Normal
            })) { }
        }

        internal static string Quote(string value) { return "\"" + value + "\""; }
    }

    internal enum Chord
    {
        None, SwitchWindow, SwitchLayout, OpenExplorer, Address, NewFolder, Rename, Copy, Paste, Arrow,
        Find, Undo, Save, QuickOpen, Palette, Confirm, ExerciseCharacter, StartApp, CloseWindow, SelectAll, MoveWord,
        ParentFolder, ForwardFolder, BackFolder, Cut, SnapLeft, SnapRight, GoLine, Replace, Comment, MoveLine,
        CloseEditor, ReopenEditor, SwitchEditor, ReplaceAll,
        RunDialog, WindowsSettings, TaskManager, Dismiss
    }

    internal struct InputChord
    {
        public Chord Chord;
        public IntPtr Window;
        public Layout Layout;
        public char Character;
    }

    internal sealed class RouteInput : IDisposable
    {
        private readonly Native.Hook keyboard, mouse;
        private IntPtr keyHook, mouseHook;
        private readonly HashSet<int> modifiers = new HashSet<int>();
        private readonly Func<IntPtr, bool> layoutTarget;
        private bool caps;
        private Chord repeatedChord;
        public readonly Queue<InputChord> Events = new Queue<InputChord>();
        public IntPtr IgnoreMain, IgnoreCard;
        public int MouseClicks { get; private set; }

        public RouteInput(Func<IntPtr, bool> layoutTarget = null)
        {
            this.layoutTarget = layoutTarget;
            caps = Native.IsCapsLock();
            keyboard = ObserveKey;
            mouse = ObserveMouse;
            var module = Native.GetModuleHandle(null);
            keyHook = Native.SetWindowsHookEx(13, keyboard, module, 0);
            mouseHook = Native.SetWindowsHookEx(14, mouse, module, 0);
            if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException("Windows не разрешила наблюдение за сочетаниями. Запустите тренажёр заново.");
            }
            foreach (int vk in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) modifiers.Add(vk);
        }

        private IntPtr ObserveKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int vk = Marshal.ReadInt32(data), m = message.ToInt32();
                bool released = m == 0x101 || m == 0x105;
                bool modifier = vk >= 0xA0 && vk <= 0xA5 || vk == 0x5B || vk == 0x5C;
                if (modifier)
                {
                    if (released) modifiers.Remove(vk); else modifiers.Add(vk);
                }
                if (released) repeatedChord = Chord.None;
                else if (m == 0x100 || m == 0x104)
                {
                    if (vk == 0x14 && layoutTarget != null) caps = !caps;
                    bool ctrl = Either(0xA2), alt = Either(0xA4), shift = Either(0xA0), win = Either(0x5B);
                    var combo = Recognize(vk, ctrl, alt, shift, win);
                    if (combo != Chord.None && combo != repeatedChord)
                    {
                        repeatedChord = combo;
                        var target = Native.GetForegroundWindow();
                        var layout = combo == Chord.SwitchLayout || combo == Chord.Save ? Native.LayoutAt(target) : Layout.Neutral;
                        Enqueue(new InputChord { Chord = combo, Window = target, Layout = layout });
                    }
                    if (layoutTarget != null && !ctrl && !alt && !win)
                        ObserveExerciseCharacter(vk, Marshal.ReadInt32(data, 4), shift);
                }
            }
            return Native.CallNextHookEx(keyHook, code, message, data);
        }

        private bool Either(int first) { return modifiers.Contains(first) || modifiers.Contains(first + 1); }

        private void ObserveExerciseCharacter(int vk, int scan, bool shift)
        {
            if (vk != 8 && vk != 13 && (vk < 'A' || vk > 'Z')) return;
            IntPtr target = Native.GetForegroundWindow();
            if (!layoutTarget(target)) return;
            Layout layout = Native.LayoutAt(target);
            if (layout != Layout.English) return;
            char? value = vk == 8 ? '\b' : vk == 13 ? '\n' : Native.TypedLetter(target, (uint)vk, (uint)scan, shift, caps);
            if (value.HasValue)
                Enqueue(new InputChord { Chord = Chord.ExerciseCharacter, Window = target, Layout = layout, Character = value.Value });
        }

        private void Enqueue(InputChord value)
        {
            if (Events.Count >= 48) Events.Dequeue();
            Events.Enqueue(value);
        }

        internal static Chord Recognize(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            if (win && !ctrl && !alt && !shift)
            {
                if (vk == 0x45) return Chord.OpenExplorer;
                if (vk == 'R') return Chord.RunDialog;
                if (vk == 'I') return Chord.WindowsSettings;
                if (vk == 0x20) return Chord.SwitchLayout;
                if (vk == 0x25) return Chord.SnapLeft;
                if (vk == 0x27) return Chord.SnapRight;
            }
            if (win && !ctrl && !alt && !shift && (vk == 0x5B || vk == 0x5C)) return Chord.StartApp;
            if (vk == 0x0D && ctrl && alt && !shift && !win) return Chord.ReplaceAll;
            if (vk == 0x09 && alt && !ctrl && !win) return Chord.SwitchWindow;
            if (vk == 0x73 && alt && !ctrl && !shift && !win) return Chord.CloseWindow;
            if (alt && !ctrl && !shift && !win)
            {
                if (vk == 'D') return Chord.Address;
                if (vk == 0x26) return Chord.ParentFolder;
                if (vk == 0x28) return Chord.MoveLine;
                if (vk == 0x25) return Chord.BackFolder;
                if (vk == 0x27) return Chord.ForwardFolder;
            }
            if (alt && shift && !ctrl && !win && vk >= 0xA0 && vk <= 0xA5) return Chord.SwitchLayout;
            if (alt || win) return Chord.None;
            if (ctrl)
            {
                if (shift)
                {
                    if (vk == 0x1B) return Chord.TaskManager;
                    if (vk == 'N') return Chord.NewFolder;
                    if (vk == 'P') return Chord.Palette;
                    if (vk == 'T') return Chord.ReopenEditor;
                    if (vk == 0x09) return Chord.SwitchEditor;
                    return Chord.None;
                }
                switch (vk)
                {
                    case 0x1B: return Chord.StartApp;
                    case 'L': return Chord.Address;
                    case 'C': return Chord.Copy;
                    case 'V': return Chord.Paste;
                    case 'F': return Chord.Find;
                    case 'Z': return Chord.Undo;
                    case 'S': return Chord.Save;
                    case 'P': return Chord.QuickOpen;
                    case 'A': return Chord.SelectAll;
                    case 'X': return Chord.Cut;
                    case 'G': return Chord.GoLine;
                    case 'H': return Chord.Replace;
                    case 'W': case 0x73: return Chord.CloseEditor;
                    case 0x09: return Chord.SwitchEditor;
                    case 0xBF: return Chord.Comment;
                }
                if (vk == 0x25 || vk == 0x27) return Chord.MoveWord;
            }
            else if (!shift)
            {
                if (vk == 0x1B) return Chord.Dismiss;
                if (vk == 0x71) return Chord.Rename;
                if (vk == 0x0D) return Chord.Confirm;
                if (vk >= 0x25 && vk <= 0x28) return Chord.Arrow;
                if (vk == 0x08) return Chord.BackFolder;
            }
            return Chord.None;
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
        {
            int m = message.ToInt32();
            if (code >= 0 && (m == 0x201 || m == 0x204 || m == 0x207 || m == 0x20B))
            {
                var point = (Native.PhysicalPoint)Marshal.PtrToStructure(data, typeof(Native.PhysicalPoint));
                if (!Native.NearWindow(IgnoreCard, point) &&
                    !(Native.GetForegroundWindow() == IgnoreMain && Native.NearWindow(IgnoreMain, point)))
                    MouseClicks++;
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        public void Dispose()
        {
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            keyHook = mouseHook = IntPtr.Zero;
            Events.Clear();
        }
    }
}
