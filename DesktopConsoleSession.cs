using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TouchTypingStudio
{
    // Only the cmd that the trainer started returns a lease. Consoles opened
    // manually from Win+R never go through this cleanup.
    internal sealed class DesktopConsoleSession : IDisposable
    {
        private Process shell;
        private readonly string title;

        public DesktopConsoleSession(Process shell, string title)
        {
            this.shell = shell;
            this.title = title;
        }

        public void Dispose()
        {
            var owned = shell;
            if (owned == null) return;
            shell = null;
            using (owned)
            {
                try
                {
                    if (owned.HasExited || !AttachConsole((uint)owned.Id)) return;
                    try
                    {
                        var clients = new uint[2];
                        var caption = new StringBuilder(160);
                        if (GetConsoleProcessList(clients, 2) == 2 &&
                            Array.IndexOf(clients, (uint)owned.Id) >= 0 &&
                            GetConsoleTitle(caption, caption.Capacity) > 0 && caption.ToString() == title &&
                            AtPrompt() && !owned.HasExited)
                            owned.Kill(); // Exact tracked process, never a tab, conhost or process tree.
                    }
                    finally { FreeConsole(); }
                }
                catch (InvalidOperationException) { } // Already exited.
                catch (Win32Exception) { } // Leave an unverifiable session open.
            }
        }

        private static bool AtPrompt()
        {
            using (var screen = CreateFile("CONOUT$", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                BufferInfo info;
                if (screen.IsInvalid || !GetConsoleScreenBufferInfo(screen, out info) || info.Cursor.X < 5) return false;
                var marker = new char[5];
                uint read;
                var start = new Coord { X = 0, Y = info.Cursor.Y };
                // Read just the five fixed prompt characters, never the user's command.
                return ReadConsoleOutputCharacter(screen, marker, 5, start, out read) &&
                    read == 5 && new string(marker) == "CMD> ";
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct BufferInfo
        {
            public Coord Size, Cursor;
            public ushort Attributes;
            public short Left, Top, Right, Bottom;
            public Coord Max;
        }
        [DllImport("kernel32")] private static extern bool AttachConsole(uint id);
        [DllImport("kernel32")] private static extern bool FreeConsole();
        [DllImport("kernel32")] private static extern uint GetConsoleProcessList([Out] uint[] ids, uint count);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetConsoleTitle(StringBuilder value, int capacity);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string path,
            uint access, uint sharing, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32")] private static extern bool GetConsoleScreenBufferInfo(SafeFileHandle screen, out BufferInfo info);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool ReadConsoleOutputCharacter(
            SafeFileHandle screen, [Out] char[] text, uint count, Coord position, out uint read);
    }
}
