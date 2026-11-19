using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace TouchTypingStudio
{
    internal static class ExplorerPathTests
    {
        public static void Run(string root)
        {
            string path = Path.Combine(root, "Отчёт для смены.txt");
            var files = new DataObject(DataFormats.FileDrop, new[] { path });
            if (DesktopCourse.IsCopiedFilePath(100, 101, files, path)) throw new Exception("A file drop list passed as a pathname");
            foreach (string value in new[] { path, "\"" + path + "\"" })
            {
                var text = new DataObject(DataFormats.UnicodeText, value);
                if (DesktopCourse.IsCopiedFilePath(100, 100, text, path) || !DesktopCourse.IsCopiedFilePath(100, 101, text, path))
                    throw new Exception("Clipboard freshness or quoted path was mishandled");
            }
            foreach (string value in new[] { Path.GetDirectoryName(path), Path.GetFileName(path), path + ".bak", path + "\r\n" + path })
                if (DesktopCourse.IsCopiedFilePath(100, 101, new DataObject(DataFormats.UnicodeText, value), path))
                    throw new Exception("Incomplete or multiple paths were accepted");

            using (var course = new DesktopCourse(root))
            {
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(DesktopCourse);
                var steps = (IList)type.GetField("steps", fields).GetValue(course);
                var step = steps.Cast<object>().First(s => (bool)s.GetType().GetField("CopiesFilePath").GetValue(s));
                var source = new IntPtr(454);
                var other = new IntPtr(455);
                var stepType = step.GetType();
                var next = type.GetField("index", fields);
                next.SetValue(course, steps.IndexOf(step));
                type.GetField("explorer", fields).SetValue(course, source);
                stepType.GetField("Source").SetValue(step, new Func<IntPtr, bool>(hwnd => hwnd == source));
                stepType.GetField("Result").SetValue(step, new Func<IntPtr, bool>(hwnd => hwnd == source));
                Action<IntPtr, string> key = (window, chord) => course.Key(new DesktopStroke {
                    Window = window, Chord = chord, Timestamp = DateTime.UtcNow, ClipboardSequence = 100 });
                key(other, course.Shortcut);
                if (course.Poll(source)) throw new Exception("Armed path copying from unrelated window");
                key(source, course.Shortcut);
                course.RejectMouseAction();
                if (course.Poll(source)) throw new Exception("Mouse-based ribbon command was credited");
                key(source, course.Shortcut);
                key(source, "Esc");
                if (course.Poll(source)) throw new Exception("Cancelled ribbon action remained armed");
                key(source, course.Shortcut);
                key(source, "Ctrl+C");
                if (course.Poll(source)) throw new Exception("Standard file copy credited after ribbon");
                key(source, course.Shortcut);
                if (course.Poll(other) || course.Poll(source)) throw new Exception("Unrelated foreground preserved ribbon credit");
                key(source, course.Shortcut);
                if (!course.Poll(source) || course.Shortcut != "Ctrl+End" || !course.WaitingForWindow)
                    throw new Exception("Copy Path did not hand clipboard contents to the real note");
            }
            Console.WriteLine("PASS: Explorer Copy Path, fresh text, quoted Unicode names, file list, folder, cancellation, mouse and handoff.");
        }
    }
}
