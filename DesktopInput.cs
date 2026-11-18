using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal struct DesktopStroke
    {
        public string Chord;
        public IntPtr Window;
        public IntPtr SwitchDestination;
        public DateTime Timestamp;
        public int MouseClicks;
    }

    // The worker panel polls this observer on its own dispatcher. Hooks only
    // retain a match for an explicitly requested combination; they never stop
    // Windows from processing a key or turn key codes into typed text.
    internal sealed class DesktopInput : IDisposable
    {
        private sealed class ExpectedChord
        {
            public string Label;
            public int Key;
            public ModifierKeys Modifiers;
            public ModifierKeys BareModifier;
        }

        private const int MaxMatches = 16;
        private static readonly int[] ModifierKeysToTrack =
            { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
        private Dispatcher dispatcher;
        private readonly bool allowAutomation;
        private readonly Native.Hook keyboardCallback, mouseCallback;
        private delegate void WinEvent(IntPtr hook, uint kind, IntPtr hwnd, int childObject, int child, uint thread, uint time);
        private readonly WinEvent switchEndCallback;
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last,
            IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private IntPtr switchEndHook, systemSwitchOrigin;
        private int systemSwitchClicks;
        private readonly HashSet<int> modifiersDown = new HashSet<int>();
        private readonly HashSet<int> targetsDown = new HashSet<int>();
        private readonly Queue<DesktopStroke> matches = new Queue<DesktopStroke>();
        private readonly DesktopSwitchGesture switching = new DesktopSwitchGesture();
        private ExpectedChord[] expected = new ExpectedChord[0];
        private IntPtr keyboardHook, mouseHook, bareWindow, altOrigin;
        private int bareKey;
        private bool disposed, watchSwitch;
        private int mouseClicks;

        public IntPtr IgnoreWindow { get; set; }
        public int MouseClicks { get { return Volatile.Read(ref mouseClicks); } }

        public DesktopInput(bool allowAutomation = false)
        {
            this.allowAutomation = allowAutomation;
            keyboardCallback = ObserveKey;
            mouseCallback = ObserveMouse;
            switchEndCallback = OnSystemSwitchEnd;
            Exception failure = null;
            // Shell and UI Automation calls on the WPF thread may take seconds.
            // Windows silently removes low-level hooks if their owner stops pumping.
            using (var ready = new ManualResetEventSlim())
            {
                var worker = new Thread(delegate()
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    try { InstallHooks(); }
                    catch (Exception error) { RemoveHooks(); failure = error; }
                    finally { ready.Set(); }
                    if (failure == null) Dispatcher.Run();
                });
                worker.IsBackground = true;
                worker.Name = "Desktop shortcut observer";
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                ready.Wait();
            }
            if (failure != null) throw failure;
        }

        private void InstallHooks()
        {
            var module = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, module, 0);
            if (keyboardHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows не разрешила наблюдать за учебными сочетаниями.");
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
            if (mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows не разрешила наблюдать за щелчками во время упражнения.");
            RefreshHeldKeys();
        }

        public static bool IsSupported(string chord)
        {
            ExpectedChord parsed;
            return TryParse(chord, out parsed);
        }

        public void Watch(params string[] chords)
        {
            if (disposed) throw new ObjectDisposedException("DesktopInput");
            var watch = new List<ExpectedChord>();
            foreach (string chord in chords ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(chord)) continue;
                ExpectedChord parsed;
                if (!TryParse(chord, out parsed))
                    throw new ArgumentException("Неподдерживаемое учебное сочетание: " + chord, "chords");
                watch.Add(parsed);
            }
            dispatcher.Invoke(new Action(delegate
            {
                expected = watch.ToArray();
                watchSwitch = watch.Exists(c => c.Key == 0x09 && c.Modifiers == ModifierKeys.Alt);
                switching.Cancel();
                altOrigin = IntPtr.Zero;
                StopSystemSwitchObserver();
                if (watchSwitch)
                {
                    IntPtr origin = Native.GetForegroundWindow();
                    // Task Manager can run at a higher integrity level than the trainer.
                    // Windows itself reports the Alt+Tab destination even when keystrokes
                    // from that window are unavailable to a normal user process.
                    if (Native.IsProcess(origin, "Taskmgr"))
                    {
                        systemSwitchOrigin = origin;
                        systemSwitchClicks = MouseClicks;
                        const uint switchEnd = 0x0015;
                        switchEndHook = SetWinEventHook(switchEnd, switchEnd, IntPtr.Zero, switchEndCallback, 0, 0, 0);
                    }
                }
                targetsDown.Clear();
                bareKey = 0;
                lock (matches) matches.Clear();
                RefreshHeldKeys();
            }));
        }

        public bool TryRead(out DesktopStroke stroke)
        {
            lock (matches)
            {
                if (matches.Count > 0)
                {
                    stroke = matches.Dequeue();
                    return true;
                }
            }
            stroke = default(DesktopStroke);
            return false;
        }

        private void RefreshHeldKeys()
        {
            // GetAsyncKeyState is reliable here, outside the low-level callback.
            modifiersDown.Clear();
            foreach (int key in ModifierKeysToTrack)
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) modifiersDown.Add(key);
            foreach (var chord in expected)
                if (chord.Key != 0 && (Native.GetAsyncKeyState(chord.Key) & 0x8000) != 0) targetsDown.Add(chord.Key);
        }

        private static bool TryParse(string input, out ExpectedChord chord)
        {
            chord = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string[] parts = input.Trim().Split('+');
            ModifierKeys mods = ModifierKeys.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                ModifierKeys modifier = ParseModifier(parts[i]);
                if (modifier == ModifierKeys.None || (mods & modifier) != 0) return false;
                mods |= modifier;
            }

            string last = parts[parts.Length - 1].Trim();
            ModifierKeys bare = ParseModifier(last);
            if (bare != ModifierKeys.None)
            {
                if (mods != ModifierKeys.None) return false;
                chord = new ExpectedChord { Label = input.Trim(), BareModifier = bare };
                return true;
            }

            int key = VirtualKey(last);
            if (key < 1 || key > 254 || KeyModifier(key) != ModifierKeys.None) return false;
            chord = new ExpectedChord { Label = input.Trim(), Key = key, Modifiers = mods };
            return true;
        }

        private static ModifierKeys ParseModifier(string token)
        {
            switch (token.Trim().ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": return ModifierKeys.Control;
                case "ALT": return ModifierKeys.Alt;
                case "SHIFT": return ModifierKeys.Shift;
                case "WIN": case "WINDOWS": return ModifierKeys.Windows;
                default: return ModifierKeys.None;
            }
        }

        private static int VirtualKey(string token)
        {
            string upper = token.ToUpperInvariant();
            if (upper.Length == 1 && (upper[0] >= 'A' && upper[0] <= 'Z' || upper[0] >= '0' && upper[0] <= '9'))
                return upper[0];
            switch (upper)
            {
                case "ENTER": case "RETURN": return 0x0D;
                case "ESC": return 0x1B;
                case "DEL": return 0x2E;
                case "BACKSPACE": return 0x08;
                case "PGUP": return 0x21;
                case "PGDN": return 0x22;
                case "SPACEBAR": return 0x20;
                case ";": return 0xBA;
                case "=": case "PLUS": return 0xBB;
                case ",": return 0xBC;
                case "-": case "−": return 0xBD;
                case ".": return 0xBE;
                case "/": return 0xBF;
                case "`": return 0xC0;
                case "[": return 0xDB;
                case "\\": return 0xDC;
                case "]": return 0xDD;
                case "'": return 0xDE;
                default:
                    Key key;
                    if (token.Length == 0 || char.IsDigit(token[0]) || !Enum.TryParse(token, true, out key) ||
                        !Enum.IsDefined(typeof(Key), key)) return 0;
                    return KeyInterop.VirtualKeyFromKey(key);
            }
        }

        private static ModifierKeys KeyModifier(int key)
        {
            switch (key)
            {
                case 0x10: case 0xA0: case 0xA1: return ModifierKeys.Shift;
                case 0x11: case 0xA2: case 0xA3: return ModifierKeys.Control;
                case 0x12: case 0xA4: case 0xA5: return ModifierKeys.Alt;
                case 0x5B: case 0x5C: return ModifierKeys.Windows;
                default: return ModifierKeys.None;
            }
        }

        private ModifierKeys ModifiersDown()
        {
            ModifierKeys value = ModifierKeys.None;
            foreach (int key in modifiersDown) value |= KeyModifier(key);
            return value;
        }

        private IntPtr ObserveKey(int code, IntPtr message, IntPtr data)
        {
            // Production watches physical keys. The isolated QA profile can also
            // receive the same Windows input events from the accessibility test driver.
            if (code == 0 && (allowAutomation || (Marshal.ReadInt32(data, 8) & 0x10) == 0))
            {
                int kind = message.ToInt32();
                int key = Marshal.ReadInt32(data);
                if (kind == 0x100 || kind == 0x104) KeyDown(key, (Marshal.ReadInt32(data, 8) & 0x20) != 0);
                else if (kind == 0x101 || kind == 0x105) KeyUp(key);
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }

        private void KeyDown(int key, bool altContext)
        {
            ModifierKeys modifier = KeyModifier(key);
            if (modifier != ModifierKeys.None)
            {
                if (!modifiersDown.Add(key)) return;
                if (watchSwitch && modifier == ModifierKeys.Alt && altOrigin == IntPtr.Zero)
                    altOrigin = Native.GetForegroundWindow();
                bareKey = 0;
                // A modifier activated by itself is counted on release, once
                // we know that it was not part of a different combination.
                if (modifiersDown.Count == 1)
                    foreach (var chord in expected)
                        if (chord.BareModifier == modifier)
                        {
                            bareKey = key;
                            bareWindow = Native.GetForegroundWindow();
                            break;
                        }
                return;
            }

            bareKey = 0;
            if (watchSwitch)
            {
                if (key == 0x1B) switching.Cancel();
                if (key == 0x09)
                {
                    ModifierKeys pressed = ModifiersDown();
                    if ((altContext || (pressed & ModifierKeys.Alt) != 0) &&
                        (pressed & (ModifierKeys.Windows | ModifierKeys.Control)) == 0)
                    {
                        switching.Start(altOrigin == IntPtr.Zero ? Native.GetForegroundWindow() : altOrigin, MouseClicks);
                        return;
                    }
                }
            }
            foreach (var chord in expected)
            {
                if (chord.Key != key) continue;
                if (!targetsDown.Add(key)) return;
                ModifierKeys pressed = ModifiersDown();
                foreach (var possible in expected)
                    if (possible.Key == key && possible.Modifiers == pressed)
                    {
                        Record(possible.Label, Native.GetForegroundWindow());
                        return;
                    }
                return;
            }
        }

        private void KeyUp(int key)
        {
            modifiersDown.Remove(key);
            targetsDown.Remove(key);
            if (watchSwitch && KeyModifier(key) == ModifierKeys.Alt && (ModifiersDown() & ModifierKeys.Alt) == 0)
            {
                IntPtr origin = switching.Release(MouseClicks);
                altOrigin = IntPtr.Zero;
                if (origin != IntPtr.Zero)
                {
                    systemSwitchOrigin = IntPtr.Zero;
                    Record("Alt+Tab", origin);
                }
            }
            if (key != bareKey) return;
            bareKey = 0;
            ModifierKeys modifier = KeyModifier(key);
            foreach (var chord in expected)
                if (chord.BareModifier == modifier)
                {
                    Record(chord.Label, bareWindow);
                    return;
                }
        }

        private void OnSystemSwitchEnd(IntPtr hook, uint kind, IntPtr hwnd, int childObject, int child, uint thread, uint time)
        {
            if (hook != switchEndHook || systemSwitchOrigin == IntPtr.Zero) return;
            IntPtr from = systemSwitchOrigin;
            systemSwitchOrigin = IntPtr.Zero;
            if (systemSwitchClicks != MouseClicks || hwnd == IntPtr.Zero || hwnd == from) return;
            switching.Cancel();
            Record("Alt+Tab", from, hwnd);
        }

        private void StopSystemSwitchObserver()
        {
            if (switchEndHook != IntPtr.Zero) UnhookWinEvent(switchEndHook);
            switchEndHook = systemSwitchOrigin = IntPtr.Zero;
        }

        private void Record(string chord, IntPtr target, IntPtr destination = default(IntPtr))
        {
            lock (matches)
            {
                if (matches.Count == MaxMatches) matches.Dequeue();
                matches.Enqueue(new DesktopStroke
                {
                    Chord = chord, Window = target, SwitchDestination = destination,
                    Timestamp = DateTime.UtcNow, MouseClicks = MouseClicks
                });
            }
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code == 0 && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B))
            {
                var point = new Native.PhysicalPoint { X = Marshal.ReadInt32(data), Y = Marshal.ReadInt32(data, 4) };
                if (!Native.NearWindow(IgnoreWindow, point)) Interlocked.Increment(ref mouseClicks);
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            dispatcher.Invoke(new Action(delegate { RemoveHooks(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }));
        }

        private void RemoveHooks()
        {
            StopSystemSwitchObserver();
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = IntPtr.Zero;
            lock (matches) matches.Clear();
            expected = new ExpectedChord[0];
        }
    }

    // Repeated Tab (or Shift+Tab) changes the highlighted thumbnail inside
    // the same gesture. Only releasing Alt commits the user's choice.
    internal sealed class DesktopSwitchGesture
    {
        private IntPtr origin;
        private int clicksAtStart;

        public void Start(IntPtr window, int clicks)
        {
            if (origin != IntPtr.Zero) return;
            origin = window;
            clicksAtStart = clicks;
        }

        public void Cancel() { origin = IntPtr.Zero; }

        public IntPtr Release(int clicks)
        {
            IntPtr result = clicksAtStart == clicks ? origin : IntPtr.Zero;
            Cancel();
            return result;
        }
    }
}
