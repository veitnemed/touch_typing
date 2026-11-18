using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal sealed class DesktopDock : IDisposable
    {
        private const int New = 0, Remove = 1, QueryPosition = 2, SetPosition = 3, PositionChanged = 1;
        private const int ExStyle = -20, ToolWindow = 0x80, NoActivate = 0x08000000;
        private const uint NoActivateMove = 0x0010, ShowWindowFlag = 0x0040;
        private readonly Window window;
        private readonly IntPtr hwnd, monitor;
        private readonly int notification;
        private int height;
        private Rectangle placed;
        private bool registered, queued, disposed;

        public bool Reserved { get { return registered; } }

        public void SetHeight(int value)
        {
            if (value == height || value < 1 || disposed) return;
            height = value;
            Reposition();
        }

        public DesktopDock(Window window, IntPtr hwnd, IntPtr anchor, int height)
        {
            this.window = window;
            this.hwnd = hwnd;
            this.height = height;
            monitor = MonitorFromWindow(anchor == IntPtr.Zero ? hwnd : anchor, 2);
            SetWindowLong(hwnd, ExStyle, GetWindowLong(hwnd, ExStyle) | ToolWindow | NoActivate);
            notification = RegisterWindowMessage("TouchTypingStudio.DesktopDock.Position");
            var data = Data();
            registered = notification != 0 && SHAppBarMessage(New, ref data) != UIntPtr.Zero;
            Reposition();
        }

        public bool HandleMessage(int message, IntPtr wParam)
        {
            if (message == notification && wParam.ToInt64() == PositionChanged || message == 0x007E || message == 0x02E0)
            {
                if (!queued && !disposed)
                {
                    queued = true;
                    window.Dispatcher.BeginInvoke(new Action(delegate { queued = false; Reposition(); }), DispatcherPriority.Background);
                }
            }
            // Opening menus and clicking controls must leave the work window active.
            return message == 0x0021;
        }

        private void Reposition()
        {
            if (disposed) return;
            var info = new MonitorInformation { Size = Marshal.SizeOf(typeof(MonitorInformation)) };
            if (!GetMonitorInfo(monitor, ref info)) return;
            var data = Data();
            data.Bounds = registered ? info.Monitor : info.Work;
            if (registered) SHAppBarMessage(QueryPosition, ref data);
            double scale = 1.0;
            var source = HwndSource.FromHwnd(hwnd);
            if (source != null && source.CompositionTarget != null)
                scale = source.CompositionTarget.TransformToDevice.M22;
            data.Bounds.Top = data.Bounds.Bottom - Math.Max(1, (int)Math.Round(height * scale));
            if (data.Bounds.Equals(placed)) return;
            if (registered) SHAppBarMessage(SetPosition, ref data);
            placed = data.Bounds;
            SetWindowPos(hwnd, new IntPtr(-1), placed.Left, placed.Top, placed.Right - placed.Left,
                placed.Bottom - placed.Top, NoActivateMove | ShowWindowFlag);
        }

        public void KeepVisible()
        {
            if (disposed || placed.Right <= placed.Left || IsWindowVisible(hwnd) && !IsIconic(hwnd)) return;
            ShowWindow(hwnd, 4);
            SetWindowPos(hwnd, new IntPtr(-1), placed.Left, placed.Top, placed.Right - placed.Left,
                placed.Bottom - placed.Top, NoActivateMove | ShowWindowFlag);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (registered)
            {
                var data = Data();
                SHAppBarMessage(Remove, ref data);
                registered = false;
            }
        }

        private AppBarData Data()
        {
            return new AppBarData { Size = Marshal.SizeOf(typeof(AppBarData)), Window = hwnd,
                Callback = notification, Edge = 3 };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public int Callback, Edge;
            public Rectangle Bounds;
            public IntPtr Parameter;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInformation
        {
            public int Size;
            public Rectangle Monitor, Work;
            public uint Flags;
        }

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(int message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, int option);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation info);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
    }
}
