using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    // Suppresses input only while the foreground lesson owns it. While paused, the
    // hook only observes release of previously captured keys. The callback stays short,
    // and all engine work runs on the UI queue after the native event has returned.
    internal sealed class LabKeyboardCapture : IDisposable
    {
        private readonly IntPtr window;
        private readonly Dispatcher dispatcher;
        private readonly Action<int, int, bool, bool, bool, string, IntPtr> receive;
        private readonly Hook callback;
        private readonly byte[] keys = new byte[256];
        private readonly HashSet<int> held = new HashSet<int>();
        private readonly HashSet<int> carried = new HashSet<int>();
        private IntPtr handle;
        private bool disposed, suspended;
        private int generation;

        internal LabKeyboardCapture(IntPtr window, Dispatcher dispatcher, Action<int, int, bool, bool, bool, string, IntPtr> receive)
        {
            this.window = window;
            this.dispatcher = dispatcher;
            this.receive = receive;
            callback = Capture;
            // A hook installed on resume did not see the original presses.
            // Ignore any release/repeat sequence already held when it started.
            for (int vk = 8; vk < keys.Length; vk++)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) carried.Add(vk);
            handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private IntPtr Capture(int code, IntPtr message, IntPtr pointer)
        {
            if (code < 0 || disposed)
                return CallNextHookEx(handle, code, message, pointer);
            int kind = message.ToInt32();
            if (kind != 0x100 && kind != 0x101 && kind != 0x104 && kind != 0x105)
                return CallNextHookEx(handle, code, message, pointer);
            bool down = kind == 0x100 || kind == 0x104;
            bool passive = suspended || GetForegroundWindow() != window;
            if (passive && down) return CallNextHookEx(handle, code, message, pointer);
            var stroke = (Stroke)Marshal.PtrToStructure(pointer, typeof(Stroke));
            int vk = (int)stroke.VirtualKey;
            if (passive)
            {
                held.Remove(vk);
                carried.Remove(vk);
                return CallNextHookEx(handle, code, message, pointer);
            }
            if (carried.Contains(vk))
            {
                if (!down) carried.Remove(vk);
                return new IntPtr(1);
            }
            bool repeat = down && !held.Add(vk);
            if (!down) held.Remove(vk);
            if (vk < keys.Length) keys[vk] = down ? (byte)0x80 : (byte)0;
            keys[0x10] = (byte)(keys[0xa0] | keys[0xa1]);
            keys[0x11] = (byte)(keys[0xa2] | keys[0xa3]);
            keys[0x12] = (byte)(keys[0xa4] | keys[0xa5]);
            string text = "";
            IntPtr inputLayout = GetKeyboardLayout(0);
            if (down)
            {
                var output = new StringBuilder(8);
                int count = ToUnicodeEx(stroke.VirtualKey, stroke.ScanCode, keys, output, output.Capacity, 4, inputLayout);
                if (count > 0 && count <= output.Length) text = output.ToString(0, count);
            }
            int scan = (int)stroke.ScanCode;
            bool extended = (stroke.Flags & 1) != 0;
            int receivedIn = generation;
            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (!disposed && !suspended && receivedIn == generation && GetForegroundWindow() == window) receive(vk, scan, extended, down, repeat, text, inputLayout);
            }));
            return new IntPtr(1);
        }

        internal void Suspend()
        {
            if (suspended || disposed) return;
            suspended = true;
            generation++;
            carried.UnionWith(held);
            held.Clear();
            Array.Clear(keys, 0, keys.Length);
        }

        internal void Resume()
        {
            if (!suspended || disposed) return;
            // The OS async state omits keydowns suppressed by our hook. Keep the
            // carried set from the prior session and add only genuinely visible keys.
            for (int vk = 8; vk < keys.Length; vk++)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) carried.Add(vk);
            suspended = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (handle != IntPtr.Zero) UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
            held.Clear();
            carried.Clear();
        }

        private delegate IntPtr Hook(int code, IntPtr message, IntPtr pointer);
        [StructLayout(LayoutKind.Sequential)]
        private struct Stroke
        {
            public uint VirtualKey, ScanCode, Flags, Time;
            public UIntPtr Extra;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr pointer);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder output, int capacity, uint flags, IntPtr layout);
    }
}
