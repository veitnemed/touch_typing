using System;

namespace TouchTypingStudio;

// Names used by the shared keyboard map and workbench parser.
internal enum LinuxKey
{
    None, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Escape, Back, Tab, Return, Enter = Return, Space, PageUp, PageDown, End, Home, Left, Up, Right, Down,
    Insert, Delete, PrintScreen, CapsLock, LeftShift, RightShift, LeftCtrl, RightCtrl, LWin, RWin, LeftAlt, RightAlt,
    Oem1, Oem2, Oem3, Oem4, Oem5, Oem6, Oem7, OemPlus, OemComma, OemMinus, OemPeriod,
    System, DeadCharProcessed, ImeProcessed,
}

[Flags]
internal enum LinuxModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
