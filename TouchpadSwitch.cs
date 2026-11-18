using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TouchTypingStudio
{
    internal sealed class TouchpadSwitch : IDisposable
    {
        public const string HotkeyLabel = "Ctrl+Alt+F10";
        private const int HotkeyMessage = 0x0312;
        private const int HotkeyId = 810;
        private const int ReleaseTimeoutMs = 1200;
        private const int ConfirmTimeoutMs = 900;
        private readonly IntPtr window;
        private readonly IDriver driver;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch elapsed = new Stopwatch();
        private enum Phase { Idle, Release, Confirm }
        private Phase phase;
        private bool registered, disposed, beforeToggle;
        // Keep ownership only as long as the setting still matches our last confirmed change.
        private bool? original, lastSet, known;

        public string Status { get; private set; }
        public bool RestoreFailed { get; private set; }
        public event Action StatusChanged;

        public TouchpadSwitch(IntPtr window) : this(window, new WindowsDriver()) { }

        internal TouchpadSwitch(IntPtr window, IDriver driver)
        {
            this.window = window;
            this.driver = driver;
            known = driver.ReadEnabled();
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (sender, args) => Advance();
            if (!known.HasValue)
            {
                Status = "Переключатель тачпада недоступен";
                return;
            }
            registered = window != IntPtr.Zero && driver.Register(window, HotkeyId);
            Status = registered ? FormatState(known.Value) : HotkeyLabel + " недоступно";
            if (registered) timer.Start();
        }

        public bool ProcessMessage(int message, IntPtr wParam)
        {
            if (message != HotkeyMessage || wParam != new IntPtr(HotkeyId) || !registered || disposed) return false;
            if (phase != Phase.Idle) return true;
            phase = Phase.Release;
            elapsed.Restart();
            timer.Interval = TimeSpan.FromMilliseconds(35);
            SetStatus("Отпустите клавиши для переключения тачпада");
            return true;
        }

        internal void Advance()
        {
            if (disposed || !registered) return;
            if (phase == Phase.Release)
            {
                if (driver.HasHeldKeys())
                {
                    if (elapsed.ElapsedMilliseconds >= ReleaseTimeoutMs)
                        EndSwitch("Отпустите клавиши и повторите " + HotkeyLabel);
                    return;
                }

                var state = driver.ReadEnabled();
                ObserveExternalChange(state);
                if (!state.HasValue) EndSwitch("Windows не сообщает состояние тачпада");
                else if (!driver.SendToggle()) EndSwitch("Windows не приняла команду тачпада");
                else
                {
                    beforeToggle = state.Value;
                    phase = Phase.Confirm;
                    elapsed.Restart();
                    SetStatus("Проверяю состояние тачпада…");
                }
                return;
            }

            if (phase == Phase.Confirm)
            {
                var state = driver.ReadEnabled();
                if (state.HasValue && state.Value != beforeToggle)
                {
                    RememberChange(state.Value);
                    EndSwitch(FormatState(state.Value));
                }
                else if (elapsed.ElapsedMilliseconds >= ConfirmTimeoutMs)
                    EndSwitch("Windows не подтвердила переключение тачпада");
                return;
            }

            var current = driver.ReadEnabled();
            if (current != known)
            {
                ObserveExternalChange(current);
                SetStatus(current.HasValue ? FormatState(current.Value) : "Состояние тачпада недоступно");
            }
        }

        private void ObserveExternalChange(bool? state)
        {
            if (lastSet.HasValue && state.HasValue && state.Value != lastSet.Value)
                original = lastSet = null;
            known = state;
        }

        private void RememberChange(bool state)
        {
            if (!original.HasValue) original = beforeToggle;
            known = state;
            lastSet = state;
            if (original == lastSet) original = lastSet = null;
        }

        private void EndSwitch(string text)
        {
            phase = Phase.Idle;
            timer.Interval = TimeSpan.FromMilliseconds(400);
            elapsed.Reset();
            SetStatus(text);
        }

        private static string FormatState(bool enabled)
        {
            return (enabled ? "Тачпад включён" : "Тачпад выключен") + " · " + HotkeyLabel;
        }

        private void SetStatus(string text)
        {
            if (text == Status) return;
            Status = text;
            var changed = StatusChanged;
            if (changed != null) changed();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            try
            {
                // A stop during confirmation can race with the shell's registry update.
                if (phase == Phase.Confirm && WaitForState(!beforeToggle, ConfirmTimeoutMs))
                    RememberChange(!beforeToggle);
                var current = driver.ReadEnabled();
                if (!original.HasValue || (current.HasValue && current != lastSet)) return;
                // Do not synthesize a chord over a held Ctrl/Alt/Esc from the stop action.
                if (!current.HasValue || !WaitForKeys() || !driver.SendToggle() || !WaitForState(original.Value, ConfirmTimeoutMs))
                {
                    RestoreFailed = true;
                    SetStatus("Не удалось восстановить тачпад. Откройте параметры сенсорной панели.");
                }
            }
            finally
            {
                if (registered) driver.Unregister(window, HotkeyId);
                registered = false;
            }
        }

        private bool WaitForKeys()
        {
            var wait = Stopwatch.StartNew();
            while (driver.HasHeldKeys())
            {
                if (wait.ElapsedMilliseconds >= ReleaseTimeoutMs) return false;
                Thread.Sleep(20);
            }
            return true;
        }

        private bool WaitForState(bool expected, int timeoutMs)
        {
            var wait = Stopwatch.StartNew();
            do
            {
                if (driver.ReadEnabled() == expected) return true;
                Thread.Sleep(25);
            } while (wait.ElapsedMilliseconds < timeoutMs);
            return false;
        }

        internal interface IDriver
        {
            bool? ReadEnabled();
            bool HasHeldKeys();
            bool SendToggle();
            bool Register(IntPtr window, int id);
            void Unregister(IntPtr window, int id);
        }

        private sealed class WindowsDriver : IDriver
        {
            private const string StatusKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
            private const ushort LeftControl = 0xa2, LeftWindows = 0x5b, F24 = 0x87;
            private const uint KeyUp = 0x0002, Extended = 0x0001;

            public bool? ReadEnabled()
            {
                // Windows exposes the value under HKLM in the driver contract and under
                // HKCU on builds with a per-user toggle. Both reads leave the setting alone.
                return ReadEnabled(Registry.CurrentUser) ?? ReadEnabled(Registry.LocalMachine);
            }

            private static bool? ReadEnabled(RegistryKey root)
            {
                try
                {
                    using (var key = root.OpenSubKey(StatusKey, false))
                    {
                        object value = key == null ? null : key.GetValue("Enabled");
                        return value is int ? (bool?)((int)value != 0) : null;
                    }
                }
                catch (UnauthorizedAccessException) { return null; }
                catch (SecurityException) { return null; }
                catch (System.IO.IOException) { return null; }
            }

            public bool Register(IntPtr hwnd, int id)
            {
                const uint ctrlAltNoRepeat = 0x4003, f10 = 0x79;
                return Native.RegisterHotKey(hwnd, id, ctrlAltNoRepeat, f10);
            }

            public void Unregister(IntPtr hwnd, int id)
            {
                Native.UnregisterHotKey(hwnd, id);
            }

            public bool HasHeldKeys()
            {
                return Down(0x10) || Down(0x11) || Down(0x12) || Down(0x1b) ||
                    Down(LeftWindows) || Down(0x5c) || Down(0x79) || Down(F24);
            }

            private static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }

            public bool SendToggle()
            {
                // Documented Windows Precision Touchpad command: Ctrl+Win+F24.
                var stroke = new[]
                {
                    Keyboard(LeftControl, 0), Keyboard(LeftWindows, Extended), Keyboard(F24, 0),
                    Keyboard(F24, KeyUp), Keyboard(LeftWindows, Extended | KeyUp), Keyboard(LeftControl, KeyUp)
                };
                int size = Marshal.SizeOf(typeof(Input));
                uint sent = SendInput((uint)stroke.Length, stroke, size);
                if (sent == stroke.Length) return true;
                // Release every key-down that really entered the queue if SendInput only sent a prefix.
                for (int index = 2; index >= 0; index--)
                {
                    int release = stroke.Length - 1 - index;
                    if (index < sent && sent <= release) SendInput(1, new[] { stroke[release] }, size);
                }
                // F24 may already have triggered the setting even when its release failed.
                return sent >= 3;
            }

            private static Input Keyboard(ushort key, uint flags)
            {
                return new Input { Type = 1, Value = new InputValue { Keyboard = new KeyboardInput { Key = key, Flags = flags } } };
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

            [StructLayout(LayoutKind.Sequential)]
            private struct Input { public uint Type; public InputValue Value; }

            [StructLayout(LayoutKind.Explicit)]
            private struct InputValue
            {
                [FieldOffset(0)] public KeyboardInput Keyboard;
                [FieldOffset(0)] public MouseInput Mouse;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct KeyboardInput
            {
                public ushort Key, Scan;
                public uint Flags, Time;
                public IntPtr ExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MouseInput
            {
                public int X, Y;
                public uint Data, Flags, Time;
                public IntPtr ExtraInfo;
            }
        }
    }
}
