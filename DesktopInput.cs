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
        public uint ClipboardSequence, ProcessId;
        public string WindowTitle;
        public bool MarkedWindow;
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
        private readonly WinEvent switchCallback;
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last,
            IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private const uint ForegroundEvent = 0x03, SwitchStart = 0x14, SwitchEnd = 0x15;
        private IntPtr switchHook, foregroundHook, foregroundApp;
        private readonly DesktopSwitchGesture systemGesture = new DesktopSwitchGesture();
        private DispatcherTimer switchGrace;
        private IntPtr releasedOrigin;
        private int releasedClicks;
        private readonly HashSet<int> modifiersDown = new HashSet<int>();
        private readonly HashSet<int> targetsDown = new HashSet<int>();
        private readonly Queue<DesktopStroke> matches = new Queue<DesktopStroke>();
        private readonly DesktopSwitchGesture switching = new DesktopSwitchGesture();
        private ExpectedChord[] expected = new ExpectedChord[0];
        private IntPtr keyboardHook, mouseHook, bareWindow, altOrigin, titleWindow;
        private int bareKey;
        private string windowMarker;
        private bool disposed, watchSwitch;
        private int mouseClicks;

        public IntPtr IgnoreWindow { get; set; }
        public int MouseClicks { get { return Volatile.Read(ref mouseClicks); } }

        public DesktopInput(bool allowAutomation = false)
        {
            this.allowAutomation = allowAutomation;
            keyboardCallback = ObserveKey;
            mouseCallback = ObserveMouse;
            switchCallback = OnSystemSwitch;
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
            WatchWindow(IntPtr.Zero, null, chords);
        }

        public void WatchWindow(IntPtr observedWindow, string marker, params string[] chords)
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
                titleWindow = observedWindow;
                windowMarker = marker;
                expected = watch.ToArray();
                watchSwitch = watch.Exists(c => c.Key == 0x09 && c.Modifiers == ModifierKeys.Alt);
                switching.Cancel();
                altOrigin = IntPtr.Zero;
                StopSystemSwitchObserver();
                if (watchSwitch)
                {
                    foregroundApp = Native.GetForegroundWindow();
                    ArmSystemSwitch(foregroundApp);
                    foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero, switchCallback, 0, 0, 0);
                    switchHook = SetWinEventHook(SwitchStart, SwitchEnd, IntPtr.Zero, switchCallback, 0, 0, 0);
                    switchGrace = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                        { Interval = TimeSpan.FromMilliseconds(150) };
                    switchGrace.Tick += FlushReleasedSwitch;
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
                {
                    ClearReleasedSwitch();
                    altOrigin = Native.GetForegroundWindow();
                }
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
                if (key == 0x1B)
                {
                    switching.Cancel();
                    systemGesture.Cancel();
                    ClearReleasedSwitch();
                }
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
                    // SWITCHEND carries the selected HWND and can arrive just after Alt-up.
                    // On shells which omit the event, fall back to the raw gesture.
                    releasedOrigin = origin;
                    releasedClicks = MouseClicks;
                    switchGrace.Start();
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

        private void OnSystemSwitch(IntPtr hook, uint kind, IntPtr hwnd, int childObject, int child, uint thread, uint time)
        {
            if (hook == foregroundHook && kind == ForegroundEvent)
            {
                if (hwnd != IntPtr.Zero && !IsSwitcher(hwnd) && hwnd != foregroundApp)
                {
                    foregroundApp = hwnd;
                    // Some shells send SWITCHEND alone. Preserve the prior Task Manager
                    // origin until the selected foreground's SWITCHEND has been delivered.
                    if (IsTaskManager(hwnd)) systemGesture.Restart(hwnd, MouseClicks);
                }
                return;
            }
            if (hook != switchHook || !watchSwitch) return;
            if (kind == SwitchStart)
            {
                // Task Manager may run elevated, so its raw keys can be unavailable.
                // Re-evaluate the origin and mouse baseline for each selection attempt.
                ArmSystemSwitch(foregroundApp);
                return;
            }
            if (kind != SwitchEnd || hwnd == IntPtr.Zero) return;
            IntPtr from = switching.Release(MouseClicks);
            if (from == IntPtr.Zero && releasedClicks == MouseClicks) from = releasedOrigin;
            IntPtr elevated = systemGesture.Release(MouseClicks);
            if (from == IntPtr.Zero) from = elevated;
            if (from != IntPtr.Zero)
            {
                ClearReleasedSwitch();
                if (hwnd != from) Record("Alt+Tab", from, hwnd);
            }
            if (IsTaskManager(hwnd)) systemGesture.Restart(hwnd, MouseClicks);
        }

        private void ArmSystemSwitch(IntPtr origin)
        {
            systemGesture.Restart(IsTaskManager(origin) ? origin : IntPtr.Zero, MouseClicks);
        }

        private static bool IsTaskManager(IntPtr window)
        {
            if (Native.ClassName(window) == "TaskManagerWindow") return true;
            try { return Native.IsProcess(window, "Taskmgr"); }
            catch (Win32Exception) { return false; }
        }

        private void FlushReleasedSwitch(object sender, EventArgs args)
        {
            IntPtr from = releasedOrigin;
            bool keyboardOnly = releasedClicks == MouseClicks;
            ClearReleasedSwitch();
            systemGesture.Cancel();
            if (from == IntPtr.Zero || !keyboardOnly) return;
            IntPtr to = Native.GetForegroundWindow();
            if (to == from || IsSwitcher(to)) to = IntPtr.Zero;
            Record("Alt+Tab", from, to);
        }

        private static bool IsSwitcher(IntPtr window)
        {
            string kind = Native.ClassName(window);
            return kind == "MultitaskingViewFrame" || kind == "TaskSwitcherWnd" || kind == "XamlExplorerHostIslandWindow";
        }

        private void ClearReleasedSwitch()
        {
            releasedOrigin = IntPtr.Zero;
            if (switchGrace != null) switchGrace.Stop();
        }

        private void StopSystemSwitchObserver()
        {
            ClearReleasedSwitch();
            if (switchGrace != null) switchGrace.Tick -= FlushReleasedSwitch;
            switchGrace = null;
            systemGesture.Cancel();
            if (switchHook != IntPtr.Zero) UnhookWinEvent(switchHook);
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            switchHook = foregroundHook = foregroundApp = IntPtr.Zero;
        }

        private void Record(string chord, IntPtr target, IntPtr destination = default(IntPtr))
        {
            lock (matches)
            {
                if (matches.Count == MaxMatches) matches.Dequeue();
                matches.Enqueue(new DesktopStroke
                {
                    Chord = chord, Window = target, SwitchDestination = destination,
                    ProcessId = chord == "Alt+F4" ? Native.WindowProcessId(target) : 0,
                    WindowTitle = target != IntPtr.Zero && target == titleWindow ? Native.Title(target) : null,
                    MarkedWindow = target != IntPtr.Zero && target == titleWindow && windowMarker != null &&
                        Native.HasWindowMark(target, windowMarker),
                    ClipboardSequence = chord == "Alt" || chord == "Ctrl+Shift+C" || chord == "Ctrl+C" || chord == "Ctrl+V" ? Native.GetClipboardSequenceNumber() : 0,
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
            if (origin == IntPtr.Zero) Restart(window, clicks);
        }

        public void Restart(IntPtr window, int clicks)
        {
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
