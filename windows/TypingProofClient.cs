// Proof infrastructure only. Geometry is in physical pixels under the
// per-monitor-aware probe; no display-mode changes, cropping or image scaling.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class TypingProofClient
{
    internal static void Fit(Process app, IntPtr ownedWindow)
    {
        RequireOwnedForeground(app, ownedWindow);
        var screen = Screen.FromHandle(ownedWindow);
        var workArea = screen.WorkingArea;
        Console.WriteLine("WPF proof startup: client=" + Client(ownedWindow) + "; display=" +
            screen.Bounds + "; work area=" + workArea + "; DPI=" + GetDpiForWindow(ownedWindow));
        ShowWindow(ownedWindow, 9); // SW_RESTORE; maximized custom chrome can extend off display.
        var timer = Stopwatch.StartNew();
        while (IsZoomed(ownedWindow) && timer.ElapsedMilliseconds < 3500) Thread.Sleep(40);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            RequireOwnedForeground(app, ownedWindow);
            Rectangle client = Client(ownedWindow);
            Rect frame;
            if (!GetWindowRect(ownedWindow, out frame)) throw new InvalidOperationException("Cannot read owned window bounds.");
            int dpi = (int)GetDpiForWindow(ownedWindow);
            if (dpi < 96) throw new InvalidOperationException("Cannot determine native WPF monitor DPI.");
            // Aim at a small laptop-sized logical viewport, but respect the runner's
            // actual work area and the measured non-client border on both sides.
            int width = Math.Min(workArea.Width, 1024 * dpi / 96 + Math.Max(0, frame.Right - frame.Left - client.Width));
            int height = Math.Min(workArea.Height, 640 * dpi / 96 + Math.Max(0, frame.Bottom - frame.Top - client.Height));
            int left = workArea.Left + (workArea.Width - width) / 2;
            int top = workArea.Top + (workArea.Height - height) / 2;
            if (!SetWindowPos(ownedWindow, IntPtr.Zero, left, top, width, height, 0x0214))
                throw new InvalidOperationException("Cannot fit the owned WPF HWND to the work area.");
            Thread.Sleep(140);
            RequireOwnedForeground(app, ownedWindow);
            client = Client(ownedWindow);
            if (!IsZoomed(ownedWindow) && workArea.Contains(client))
            {
                Console.WriteLine("WPF proof viewport: complete physical client=" + client +
                    "; work area=" + workArea + "; DPI=" + GetDpiForWindow(ownedWindow));
                return;
            }
        }
        throw new InvalidOperationException("Owned WPF client " + Client(ownedWindow) +
            " cannot fit work area " + workArea + " at DPI " + GetDpiForWindow(ownedWindow) +
            ". Proof must not use cropped or scaled images.");
    }

    internal static Rectangle Visible(Process app, IntPtr ownedWindow)
    {
        RequireOwnedForeground(app, ownedWindow);
        var rect = Client(ownedWindow);
        var workArea = Screen.FromHandle(ownedWindow).WorkingArea;
        if (!workArea.Contains(rect))
            throw new InvalidOperationException("WPF client " + rect + " is not wholly inside work area " + workArea + ".");
        return rect;
    }

    private static void RequireOwnedForeground(Process app, IntPtr h)
    {
        uint pid;
        if (app == null || app.HasExited || GetWindowThreadProcessId(h, out pid) == 0 || pid != (uint)app.Id ||
            GetForegroundWindow() != h)
            throw new InvalidOperationException("Refusing resize or proof capture outside the owned foreground trainer HWND.");
    }

    private static Rectangle Client(IntPtr h)
    {
        Rect size;
        var origin = new Point();
        if (!GetClientRect(h, out size) || !ClientToScreen(h, ref origin) || size.Right <= 0 || size.Bottom <= 0)
            throw new InvalidOperationException("Could not read the owner's live physical client geometry.");
        return new Rectangle(origin.X, origin.Y, size.Right, size.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
}
