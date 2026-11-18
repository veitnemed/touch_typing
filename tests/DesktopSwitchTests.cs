using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace TouchTypingStudio
{
    internal static class DesktopSwitchTests
    {
        public static void Run()
        {
            const BindingFlags staticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
            var caption = typeof(DesktopCourseWindows).GetMethod("DocumentCaption", staticPrivate);
            Require((bool)caption.Invoke(null, new object[] { "status-devices – Блокнот", "status-devices" }),
                "Russian Windows 10 Notepad uses an en dash and can hide .txt in the caption");
            Require(!(bool)caption.Invoke(null, new object[] { "status-devices-old – Блокнот", "status-devices" }),
                "An unrelated longer filename matched");
            var first = new IntPtr(71);
            var target = new IntPtr(83);
            var switcher = new IntPtr(90);
            var gesture = new DesktopSwitchGesture();
            gesture.Start(first, 0);
            gesture.Start(switcher, 0);
            Require(gesture.Release(0) == first, "Repeated Tab must preserve the origin");
            Require(gesture.Release(0) == IntPtr.Zero, "One gesture yields one switch");
            gesture.Start(first, 3);
            Require(gesture.Release(4) == IntPtr.Zero, "A click inside the switcher cancels keyboard credit");
            gesture.Start(first, 4);
            gesture.Cancel();
            Require(gesture.Release(4) == IntPtr.Zero, "Escape cancels the gesture");
            gesture.Start(first, 4);
            gesture.Restart(switcher, 5);
            Require(gesture.Release(5) == switcher, "A fresh system switch uses its own origin and click baseline");

            string folder = Path.Combine(Path.GetTempPath(), "DesktopSwitchTests-" + Guid.NewGuid().ToString("N"));
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                // Use a live-course Alt+Tab step, substituting just the destination window.
                using (var course = new DesktopCourse(folder, DesktopTrack.Console))
                {
                    var steps = (IList)typeof(DesktopCourse).GetField("steps", fields).GetValue(course);
                    int i = 0;
                    for (; i < steps.Count; i++)
                        if ((string)steps[i].GetType().GetField("Shortcut").GetValue(steps[i]) == "Alt+Tab") break;
                    Require(i < steps.Count, "Could not find console return step");
                    var selected = steps[i];
                    selected.GetType().GetField("Result").SetValue(selected, new Func<IntPtr, bool>(hwnd => hwnd == target));
                    typeof(DesktopCourse).GetField("index", fields).SetValue(course, i);
                    Action<IntPtr, IntPtr> release = (from, to) => course.Key(new DesktopStroke {
                        Chord = "Alt+Tab", Window = from, SwitchDestination = to, Timestamp = DateTime.UtcNow
                    });
                    Action<double> elapsed = seconds => {
                        typeof(DesktopCourse).GetField("commandAt", fields).SetValue(course, DateTime.UtcNow.AddSeconds(-seconds));
                        typeof(DesktopCourse).GetField("nextCheck", fields).SetValue(course, DateTime.MinValue);
                    };
                    Require(!course.Poll(target), "Foreground alone credited");
                    release(target, IntPtr.Zero);
                    Require(!course.Poll(target), "Same-window cancellation credited");
                    release(first, IntPtr.Zero);
                    Require(!course.Poll(switcher), "Switcher overlay credited");
                    course.RejectMouseAction();
                    Require(!course.Poll(target), "Mouse-selected destination credited");
                    release(first, switcher);
                    Require(!course.Poll(target), "Unrelated Windows switch-end event credited");
                    elapsed(2);
                    int rejected = course.Rejected;
                    Require(!course.Poll(target) && course.Rejected == rejected + 1,
                        "A confirmed wrong selection must close the attempt and allow a fresh retry");
                    release(first, IntPtr.Zero);
                    Require(!course.Poll(IntPtr.Zero), "Transient empty foreground credited");
                    elapsed(2);
                    Require(!course.Poll(switcher), "Late unrelated selection credited");
                    elapsed(3);
                    Require(!course.Poll(target), "An unrelated later activation credited an expired raw gesture");
                    release(first, IntPtr.Zero);
                    elapsed(0.6);
                    Require(course.Poll(target), "A delayed activation without a shell switch-end event was lost");
                    typeof(DesktopCourse).GetField("index", fields).SetValue(course, i);
                    release(switcher, target);
                    Require(!course.Poll(first), "Selection was credited before the target became active");
                    elapsed(3);
                    Require(course.Poll(target) && course.Completed == 2, "Retry with a slow, verified target not credited");
                    Require(!course.Poll(target), "Same switch credited twice");
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            Console.WriteLine("PASS: Alt+Tab completion, repeated selection, cancellation, delayed focus, switch-end identity and retry.");
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
