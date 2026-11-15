using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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
                K(Key.D2,1,"2","@","2","\""), K(Key.D3,2,"3","#","3","№"),
                K(Key.D4,3,"4","$","4",";"), K(Key.D5,3,"5","%","5","%"),
                K(Key.D6,4,"6","^","6",":"), K(Key.D7,4,"7","&","7","?"),
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
                Control(Key.RightAlt,1.3,8,"Alt"), Control(Key.RightCtrl,1.3,7,"Ctrl"),
                Control(Key.Left,.67,7,"←"), Control(Key.Down,.69,7,"↓"), Control(Key.Up,.69,7,"↑"),
                Control(Key.Right,.67,7,"→")
            }
        };

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
            "Правый указательный", "Правый средний", "Правый безымянный", "Правый мизинец", "Большой палец" };

        public static string Finger(int finger) { return Fingers[finger]; }
    }

    internal sealed class KeyboardPicture : FrameworkElement
    {
        private static readonly Color[] Zones =
        {
            Color.FromRgb(201,147,156), Color.FromRgb(211,175,119), Color.FromRgb(160,187,134), Color.FromRgb(132,178,182),
            Color.FromRgb(146,165,185), Color.FromRgb(160,187,134), Color.FromRgb(211,175,119), Color.FromRgb(201,147,156),
            Color.FromRgb(170,147,184)
        };
        private Layout paintLayout = Layout.English;
        private Key target, shift, pressed, wrong, alternate;
        private bool caps, showHands = true, showKeys = true;

        public KeyboardPicture()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        public void Update(Layout layout, KeyHint hint, Key input, Key error, bool tab, bool capsLock, bool keys, bool hands)
        {
            paintLayout = layout;
            target = hint == null ? Key.None : hint.Cap.Key;
            shift = hint == null ? Key.None : hint.Modifier;
            pressed = input;
            wrong = error;
            alternate = tab ? Key.Tab : Key.None;
            caps = capsLock;
            showKeys = keys;
            showHands = hands;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext draw)
        {
            double full = Math.Min(1115, ActualWidth);
            double wing = showHands ? Math.Min(155, full * .155) : 0;
            double w = Math.Min(755, full - 2 * wing);
            double h = Math.Min(42, Math.Max(18, (ActualHeight - 20) / 5 - 4));
            double height = 5 * (h + 4) - 4;
            double top = 10;
            double left = (ActualWidth - w) / 2;
            if (w < 100) return;
            if (showKeys)
            {
                draw.DrawRoundedRectangle(Ui.Brush("#343A45"), null, new Rect(left - 8, top - 8, w + 16, height + 16), 8, 8);
                for (int r = 0; r < TypingKeyboard.Rows.Length; r++)
                {
                    Cap[] row = TypingKeyboard.Rows[r];
                    double gap = Math.Max(3, w / 190);
                    double unit = (w - gap * (row.Length - 1)) / row.Sum(key => key.Units);
                    double x = left;
                    foreach (Cap key in row)
                    {
                        var rect = new Rect(x, top + r * (h + 4), unit * key.Units, h);
                        PaintKey(draw, rect, key);
                        x += rect.Width + gap;
                    }
                }
            }
            if (showHands)
            {
                double scale = showKeys ? Math.Min(1.05, wing * .73 / 96) : .75;
                double spacing = showKeys ? w / 2 + wing / 2 + 4 : 58;
                PaintHands(draw, spacing, Math.Max(4, top + (height - 127 * scale) / 2), scale);
            }
        }

        private void PaintKey(DrawingContext draw, Rect rect, Cap key)
        {
            Color tint = key.Key == Key.Space || key.English.Length == 1 && key.Key != Key.Left && key.Key != Key.Right &&
                key.Key != Key.Up && key.Key != Key.Down ? Zones[key.Finger] : Color.FromRgb(167,170,174);
            bool bad = key.Key == wrong;
            bool next = key.Key == target || key.Key == shift;
            bool hit = key.Key == pressed || key.Key == Key.CapsLock && caps;
            bool secondary = key.Key == alternate;
            bool dark = key.Anchor && !bad && !hit;
            Color face = bad ? Color.FromRgb(214,102,106) : hit ? Color.FromRgb(248,220,152) : dark ? Shade(tint,.47) : tint;
            var pen = bad ? new Pen(Ui.Brush("#FE989C"), 2) : next ? new Pen(Ui.Brush("#FEF1D3"), 2.4) :
                secondary ? new Pen(Ui.Brush("#FEF1D3"), 1.3) : null;
            draw.DrawRoundedRectangle(new SolidColorBrush(face), pen, rect, 3.5, 3.5);
            string label = key.Base(paintLayout);
            string shifted = key.Shifted(paintLayout);
            bool showSecondary = shifted.Length > 0 && !char.IsLetter(label[0]);
            var ink = dark ? Brushes.White : Ui.Brush("#17202C");
            double pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Ui.Mono,
                label.Length > 2 ? Math.Min(13, rect.Height * .31) : Math.Min(20, rect.Height * .48), ink, pixels);
            double dy = showSecondary ? rect.Height * .10 : 0;
            draw.DrawText(formatted, new Point(rect.Left + (rect.Width - formatted.Width) / 2, rect.Top + (rect.Height - formatted.Height) / 2 + dy));
            if (showSecondary)
            {
                var sub = new FormattedText(shifted, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Ui.Mono,
                    Math.Min(11, rect.Height * .26), ink, pixels);
                draw.DrawText(sub, new Point(rect.Left + 5, rect.Top + 1));
            }
            if (key.Anchor)
                draw.DrawLine(new Pen(ink, 1.8), new Point(rect.Left + rect.Width * .34, rect.Bottom - 4), new Point(rect.Left + rect.Width * .66, rect.Bottom - 4));
        }

        private void PaintHands(DrawingContext draw, double spacing, double top, double scale)
        {
            Cap cap = TypingKeyboard.Rows.SelectMany(row => row).FirstOrDefault(k => k.Key == target);
            int finger = cap == null ? -1 : cap.Finger;
            int modifier = shift == Key.LeftShift ? 0 : shift == Key.RightShift ? 7 : -1;
            double center = ActualWidth / 2;
            draw.PushTransform(new TranslateTransform(center - spacing - 48 * scale, top));
            draw.PushTransform(new ScaleTransform(scale, scale));
            PaintHand(draw, true, finger, modifier);
            draw.Pop();
            draw.Pop();
            draw.PushTransform(new TranslateTransform(center + spacing + 48 * scale, top));
            draw.PushTransform(new ScaleTransform(-scale, scale));
            PaintHand(draw, false, finger, modifier);
            draw.Pop();
            draw.Pop();
        }

        private static void PaintHand(DrawingContext draw, bool left, int finger, int modifier)
        {
            double[] tops = { 34, 14, 3, 12 };
            double[] turns = { -13, -7, -1, 7 };
            var outline = new Pen(Ui.Brush("#181A20"), 2.2);
            var mark = new Pen(Ui.Brush("#F4F1F0"), 1.2);
            for (int i = 0; i < 4; i++)
            {
                int index = left ? i : 7 - i;
                double x = 8 + i * 17;
                draw.PushTransform(new RotateTransform(turns[i], x + 8, 75));
                draw.DrawRoundedRectangle(new SolidColorBrush(Zones[index]), outline, new Rect(x, tops[i], 17, 88 - tops[i]), 8.5, 8.5);
                if (finger == index || modifier == index) draw.DrawEllipse(Ui.Brush("#CB4B54"), mark, new Point(x + 8.5, tops[i] + 10), 3.6, 3.6);
                draw.Pop();
            }
            draw.PushTransform(new RotateTransform(37, 75, 102));
            draw.DrawRoundedRectangle(new SolidColorBrush(Zones[8]), outline, new Rect(69, 53, 17, 63), 8.5, 8.5);
            if (finger == 8 && left) draw.DrawEllipse(Ui.Brush("#CB4B54"), mark, new Point(77, 64), 3.6, 3.6);
            draw.Pop();
            draw.DrawRoundedRectangle(Ui.Brush("#CBCCD0"), outline, new Rect(9, 56, 68, 70), 26, 26);
        }

        private static Color Shade(Color c, double factor)
        { return Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)); }
    }
}
