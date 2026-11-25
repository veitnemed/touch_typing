// Narrow native acceptance. Read the accessibility controls and the real leading
// color marks from the owned WPF process; never photograph an unrelated window.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

internal static class TypingRecordUiChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct Pt { internal int X, Y; }
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr token);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Pt point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr info);

    private static AutomationElement root;
    private static IntPtr window;
    private static AutomationElement Find(string id)
    {
        return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    }
    private static void Until(Func<bool> ok, string topic)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 15000)
        {
            try { if (ok()) return; } catch (ElementNotAvailableException) { }
            Thread.Sleep(80);
        }
        throw new Exception("Timeout: " + topic);
    }
    private static void Assert(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Own() { Assert(GetForegroundWindow() == window, "Trainer is not in foreground."); }
    private static void Key(byte vk, bool down) { Own(); keybd_event(vk, 0, down ? 0U : 2U, UIntPtr.Zero); }
    private static void EnterCourse()
    {
        Key(0x12, true); Key(0x32, true); Key(0x32, false); Key(0x12, false); // Alt+2
        Until(delegate { return Find("TypingRow-Russian") != null; }, "ordinary course row");
    }
    private static bool ColorInMark(Bitmap image, int sx, int sy, AutomationElement element, Color expected)
    {
        var box = element.Current.BoundingRectangle;
        int y = (int)(box.Top + box.Height / 2) - sy;
        int left = (int)box.Left - sx, end = (int)Math.Min(left + box.Width / 5, left + 50);
        int pixels = 0;
        for (int yy = Math.Max(0,y-4); yy < Math.Min(image.Height,y+5); yy++)
            for (int xx = Math.Max(0,left+2); xx < Math.Min(image.Width,end); xx++)
            {
                var c = image.GetPixel(xx, yy);
                if (Math.Abs(c.R - expected.R) < 12 && Math.Abs(c.G - expected.G) < 12 &&
                    Math.Abs(c.B - expected.B) < 12) pixels++;
            }
        return pixels >= 4;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Process trainer = null;
        try
        {
            if (args.Length != 2 || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new Exception("Use Test-TypingCardRecords.ps1: it prepares an isolated QA profile.");
            Assert(SetProcessDpiAwarenessContext(new IntPtr(-4)), "Cannot map UIA physical coordinates.");
            trainer = Process.Start(new ProcessStartInfo(Path.GetFullPath(args[0]), "--qa --live") {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0])) });
            Until(delegate { trainer.Refresh(); window = trainer.MainWindowHandle; return window != IntPtr.Zero; }, "own WPF window");
            root = AutomationElement.FromHandle(window);
            SetForegroundWindow(window);
            Until(delegate { return window == GetForegroundWindow() && Find("ReferenceNav-learn") != null; }, "active Live shell");
            TypingProofClient.Fit(trainer, window);
            EnterCourse();
            // This seed is deliberately shared with GTK. The 4s English attempt lost
            // on accuracy; the 60s tied attempt lost on *active duration*.
            string[,] checks = {
                {"Russian","89,99%","01:11","#EAA2AD"}, {"English","98%","00:25","#A7D4B4"},
                {"WordsRU","98%","—","#A7D4B4"}, {"WordsEN","97,49%","00:32","#ECC38F"},
                {"Digits","97,5%","00:20","#A7D4B4"}, {"TopRow","90%","00:40","#ECC38F"},
                {"Hotkeys","—","—","#AAA4AE"} };
            Until(delegate { var b = Find("TypingRow-English"); return b != null && b.Current.Name.Contains("00:25"); }, "paired record load");
            Thread.Sleep(300);
            Own();
            var frame = TypingProofClient.Visible(trainer, window);
            using (var image = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb))
            {
                using (var graphics = Graphics.FromImage(image))
                    graphics.CopyFromScreen(frame.X, frame.Y, 0, 0, image.Size, CopyPixelOperation.SourceCopy);
                Own();
                for (int i = 0; i < checks.GetLength(0); i++)
                {
                    var mode = checks[i,0];
                    var b = Find("TypingRow-" + mode);
                    Assert(b != null && !b.Current.IsOffscreen, "Missing visible row " + mode);
                    var box = b.Current.BoundingRectangle;
                    Assert(GetAncestor(WindowFromPoint(new Pt { X=(int)(box.Left + box.Width/2), Y=(int)(box.Top+box.Height/2) }),2) == window,
                        "Overlapped row " + mode);
                    var name = b.Current.Name;
                    if (mode == "Hotkeys") Assert(name.Contains("Нет завершённых"), name);
                    else Assert(name.Contains("точность: " + checks[i,1] + ".") &&
                                name.Contains("попытки: " + checks[i,2] + "."), name);
                    Assert(name.Contains("прежней версии") == (mode == "WordsRU"), name);
                    Assert(ColorInMark(image, frame.X, frame.Y, b, ColorTranslator.FromHtml(checks[i,3])),
                        "Leading mark " + checks[i,3] + " not on " + mode);
                    Console.WriteLine("PASS WPF " + mode + " " + checks[i,1] + " " + checks[i,2] + " " + checks[i,3]);
                }
                Own();
                image.Save(Path.Combine(args[1],"typing-course-records.png"),ImageFormat.Png);
            }
            Find("TypingRow-English").SetFocus();
            Key(0x0D,true); Key(0x0D,false);
            Until(delegate { return Find("TypingSessionBar") != null; }, "Enter starts the standard typing lesson");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            // Terminate only the process started with this disposable profile. The
            // GUI intentionally prompts on close, so an unattended test cannot use it.
            if (trainer != null) { if (!trainer.HasExited) { trainer.Kill(); trainer.WaitForExit(3000); } trainer.Dispose(); }
        }
    }
}
