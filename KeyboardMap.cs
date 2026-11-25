using System.Collections.Generic;
using System.Linq;
#if LINUX
using Key = TouchTypingStudio.LinuxKey;
#else
using System.Windows.Input;
#endif

namespace TouchTypingStudio
{
    internal sealed class Cap
    {
        public readonly Key Key;
        public readonly double Units;
        public readonly int Finger;
        public readonly string English, EnglishShift, Russian, RussianShift;
        public bool Anchor { get { return Key == Key.F || Key == Key.J; } }

        public Cap(Key key, double units, int finger, string english, string englishShift, string russian, string russianShift)
        {
            Key = key;
            Units = units;
            Finger = finger;
            English = english;
            EnglishShift = englishShift;
            Russian = russian;
            RussianShift = russianShift;
        }

        public string Base(Layout layout) { return layout == Layout.Russian ? Russian : English; }
        public string Shifted(Layout layout) { return layout == Layout.Russian ? RussianShift : EnglishShift; }
    }

    internal sealed class KeyHint
    {
        public readonly Cap Cap;
        public readonly Key Modifier;
        public readonly Layout Layout;
        public KeyHint(Cap cap, Key modifier, Layout layout) { Cap = cap; Modifier = modifier; Layout = layout; }
    }

    // Extra physical sections are drawn only when they are relevant to the current
    // hint/input. Do not put them into Rows: that map must still compose text from
    // the main ANSI deck rather than quietly prefer numeric-lock-dependent keys.
    internal sealed class AuxiliaryCap
    {
        public readonly string Key, Label, Group, Alias;
        public readonly int Column, Row, Width, Height;

        public AuxiliaryCap(string key, string label, string group, int column, int row,
            int width = 1, int height = 1, string alias = null)
        {
            Key = key; Label = label; Group = group; Column = column; Row = row;
            Width = width; Height = height; Alias = alias;
        }
    }

    internal static class TypingKeyboard
    {
        private static Cap K(Key key, int finger, string en, string ens, string ru, string rus)
        { return new Cap(key, 1, finger, en, ens, ru, rus); }

        private static Cap Letters(Key key, int finger, string en, string ru)
        { return K(key, finger, en, en.ToUpperInvariant(), ru, ru.ToUpperInvariant()); }

        private static Cap Control(Key key, double width, int finger, string name)
        { return new Cap(key, width, finger, name, "", name, ""); }

        public static readonly Cap[][] Rows =
        {
            new[] {
                K(Key.Oem3, 0, "`", "~", "ё", "Ё"), K(Key.D1,0,"1","!","1","!"),
                K(Key.D2,0,"2","@","2","\""), K(Key.D3,1,"3","#","3","№"),
                K(Key.D4,2,"4","$","4",";"), K(Key.D5,3,"5","%","5","%"),
                K(Key.D6,3,"6","^","6",":"), K(Key.D7,4,"7","&","7","?"),
                K(Key.D8,5,"8","*","8","*"), K(Key.D9,6,"9","(","9","("),
                K(Key.D0,7,"0",")","0",")"), K(Key.OemMinus,7,"-","_","-","_"),
                K(Key.OemPlus,7,"=","+","=","+"), Control(Key.Back,2,7,"Backspace")
            },
            new[] {
                Control(Key.Tab,1.55,0,"Tab"), Letters(Key.Q,0,"q","й"), Letters(Key.W,1,"w","ц"),
                Letters(Key.E,2,"e","у"), Letters(Key.R,3,"r","к"), Letters(Key.T,3,"t","е"),
                Letters(Key.Y,4,"y","н"), Letters(Key.U,4,"u","г"), Letters(Key.I,5,"i","ш"),
                Letters(Key.O,6,"o","щ"), Letters(Key.P,7,"p","з"), K(Key.Oem4,7,"[","{","х","Х"),
                K(Key.Oem6,7,"]","}","ъ","Ъ"), new Cap(Key.Oem5,1.45,7,"\\","|","\\","/")
            },
            new[] {
                Control(Key.CapsLock,1.8,0,"Caps"), Letters(Key.A,0,"a","ф"), Letters(Key.S,1,"s","ы"),
                Letters(Key.D,2,"d","в"), Letters(Key.F,3,"f","а"), Letters(Key.G,3,"g","п"),
                Letters(Key.H,4,"h","р"), Letters(Key.J,4,"j","о"), Letters(Key.K,5,"k","л"),
                Letters(Key.L,6,"l","д"), K(Key.Oem1,7,";",":","ж","Ж"),
                K(Key.Oem7,7,"'","\"","э","Э"), Control(Key.Enter,2.2,7,"Enter")
            },
            new[] {
                Control(Key.LeftShift,2.3,0,"Shift"), Letters(Key.Z,0,"z","я"), Letters(Key.X,1,"x","ч"),
                Letters(Key.C,2,"c","с"), Letters(Key.V,3,"v","м"), Letters(Key.B,3,"b","и"),
                Letters(Key.N,4,"n","т"), Letters(Key.M,4,"m","ь"), K(Key.OemComma,5,",","<","б","Б"),
                K(Key.OemPeriod,6,".",">","ю","Ю"), K(Key.Oem2,7,"/","?",".",","),
                Control(Key.RightShift,2.7,7,"Shift")
            },
            new[] {
                Control(Key.LeftCtrl,1.3,0,"Ctrl"), Control(Key.LWin,1.25,0,"Win"),
                Control(Key.LeftAlt,1.3,8,"Alt"), Control(Key.Space,6.5,8,"Пробел"),
                Control(Key.RightAlt,1.3,9,"Alt"), Control(Key.RightCtrl,1.3,7,"Ctrl"),
                Control(Key.Left,.67,7,"←"), Control(Key.Down,.69,7,"↓"), Control(Key.Up,.69,7,"↑"),
                Control(Key.Right,.67,7,"→")
            }
        };

        public static readonly AuxiliaryCap[] Auxiliary =
        {
            new AuxiliaryCap("ScrollLock", "Scroll", "service", 0, 0),
            new AuxiliaryCap("Pause", "Pause", "service", 1, 0),
            new AuxiliaryCap("Menu", "Menu", "service", 2, 0),
            new AuxiliaryCap("Insert", "Ins", "service", 0, 1),
            new AuxiliaryCap("Home", "Home", "service", 1, 1),
            new AuxiliaryCap("PageUp", "PgUp", "service", 2, 1),
            new AuxiliaryCap("Delete", "Del", "service", 0, 2),
            new AuxiliaryCap("End", "End", "service", 1, 2),
            new AuxiliaryCap("PageDown", "PgDn", "service", 2, 2),
            new AuxiliaryCap("RSuper", "Win R", "service", 2, 3),
            new AuxiliaryCap("NumLock", "Num", "pad", 0, 0),
            new AuxiliaryCap("NumDivide", "/", "pad", 1, 0),
            new AuxiliaryCap("NumMultiply", "×", "pad", 2, 0),
            new AuxiliaryCap("NumSubtract", "−", "pad", 3, 0),
            new AuxiliaryCap("NumPad7", "7", "pad", 0, 1, alias: "Home"),
            new AuxiliaryCap("NumPad8", "8", "pad", 1, 1, alias: "Up"),
            new AuxiliaryCap("NumPad9", "9", "pad", 2, 1, alias: "PageUp"),
            new AuxiliaryCap("NumAdd", "+", "pad", 3, 1, height: 2),
            new AuxiliaryCap("NumPad4", "4", "pad", 0, 2, alias: "Left"),
            new AuxiliaryCap("NumPad5", "5", "pad", 1, 2),
            new AuxiliaryCap("NumPad6", "6", "pad", 2, 2, alias: "Right"),
            new AuxiliaryCap("NumPad1", "1", "pad", 0, 3, alias: "End"),
            new AuxiliaryCap("NumPad2", "2", "pad", 1, 3, alias: "Down"),
            new AuxiliaryCap("NumPad3", "3", "pad", 2, 3, alias: "PageDown"),
            new AuxiliaryCap("NumEnter", "Enter", "pad", 3, 3, height: 2),
            new AuxiliaryCap("NumPad0", "0", "pad", 0, 4, width: 2, alias: "Insert"),
            new AuxiliaryCap("NumDecimal", ".", "pad", 2, 4, alias: "Delete"),
        };
        private static readonly Dictionary<string, string> PadAliases = Auxiliary
            .Where(cap => cap.Alias != null).ToDictionary(cap => cap.Key, cap => cap.Alias);

        public static string NavigationAlias(string key)
        {
            string alias;
            return PadAliases.TryGetValue(key, out alias) ? alias : null;
        }

        private static readonly Dictionary<char, KeyHint> English = Build(Layout.English);
        private static readonly Dictionary<char, KeyHint> Russian = Build(Layout.Russian);

        private static Dictionary<char, KeyHint> Build(Layout layout)
        {
            var lookup = new Dictionary<char, KeyHint>();
            foreach (Cap cap in Rows.SelectMany(row => row))
            {
                string normal = cap.Base(layout), shifted = cap.Shifted(layout);
                if (normal.Length == 1 && cap.Key != Key.Left && cap.Key != Key.Right && cap.Key != Key.Down && cap.Key != Key.Up)
                    lookup[normal[0]] = new KeyHint(cap, Key.None, layout);
                if (shifted.Length == 1)
                    lookup[shifted[0]] = new KeyHint(cap, cap.Finger < 4 ? Key.RightShift : Key.LeftShift, layout);
                if (cap.Key == Key.Space) lookup[' '] = new KeyHint(cap, Key.None, layout);
                if (cap.Key == Key.Enter) lookup['\n'] = new KeyHint(cap, Key.None, layout);
                if (cap.Key == Key.Back) lookup[CodeText.Erase] = new KeyHint(cap, Key.None, layout);
                if (cap.Key == Key.Tab) lookup['\t'] = new KeyHint(cap, Key.None, layout);
            }
            return lookup;
        }

        public static KeyHint Hint(char character, Layout preferred)
        {
            KeyHint hint;
            var first = preferred == Layout.Russian ? Russian : English;
            var second = preferred == Layout.Russian ? English : Russian;
            if (first.TryGetValue(character, out hint) || second.TryGetValue(character, out hint)) return hint;
            return null;
        }

        public static bool Supports(char c) { return c != CodeText.Erase && (English.ContainsKey(c) || Russian.ContainsKey(c)); }

        private static readonly string[] Fingers = { "Левый мизинец", "Левый безымянный", "Левый средний", "Левый указательный",
            "Правый указательный", "Правый средний", "Правый безымянный", "Правый мизинец", "Левый большой", "Правый большой" };

        public static string Finger(int finger) { return Fingers[finger]; }
    }
}
