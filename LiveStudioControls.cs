using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchTypingStudio
{
    internal sealed class FocusTape : FrameworkElement
    {
        private static readonly Brush Fill = Ui.Brush("#292D36");
        private static readonly Brush Edge = Ui.Brush("#333944");
        private static readonly Brush Ink = Ui.Brush("#CAC9CF");
        private static readonly Brush Previous = Ui.Brush("#898B94");
        private static readonly Brush Error = Ui.Brush("#FF5366");
        private static readonly Typeface Typeface = new Typeface("Courier New");

        public void Update(string before, string current, string after, bool error = false, bool command = false)
        {
            Left = before;
            Current = current;
            Right = after;
            Incorrect = error;
            IsCommand = command;
            AutomationPropertiesText();
            InvalidateVisual();
        }

        private string Left = "", Current = "", Right = "";
        private bool Incorrect, IsCommand;
        private static readonly Brush Command = Ui.Brush("#CBBAEA");
        private void AutomationPropertiesText()
        { System.Windows.Automation.AutomationProperties.SetName(this, Current + " " + Right); }

        // This ribbon is rendered directly; without a peer Narrator and native
        // keyboard QA see an empty window where the current target is painted.
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        { return new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this); }

        protected override Size MeasureOverride(Size availableSize)
        { return new Size(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 90); }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 2) return;
            var bounds = new Rect(3, 3, ActualWidth - 6, ActualHeight - 6);
            dc.DrawRoundedRectangle(Fill, new Pen(Edge, 6), bounds, 10, 10);
            double center = ActualWidth / 2;
            double size = Math.Min(54, Math.Max(41, ActualWidth / 19));
            var active = Make(Current, size, Incorrect ? Error : IsCommand ? Command : Ink);
            if (active.Width > center - 20)
            {
                size *= (center - 20) / active.Width;
                active = Make(Current, size, Incorrect ? Error : IsCommand ? Command : Ink);
            }
            var previous = Make(Left, size, Previous);
            var next = Make(Right, size, Ink);
            // Place all three segments on one baseline even when their glyphs have different bounds.
            double baseline = (ActualHeight - active.Height) / 2 + active.Baseline;
            dc.PushClip(new RectangleGeometry(new Rect(6, 6, ActualWidth - 12, ActualHeight - 12), 6, 6));
            dc.DrawRectangle(Ui.Brush("#1F2125"), null, new Rect(6, 6, center - 6, ActualHeight - 12));
            dc.DrawText(previous, new Point(center - previous.WidthIncludingTrailingWhitespace, baseline - previous.Baseline));
            dc.DrawText(active, new Point(center, baseline - active.Baseline));
            dc.DrawText(next, new Point(center + active.WidthIncludingTrailingWhitespace, baseline - next.Baseline));
            dc.Pop();
        }

        private FormattedText Make(string value, double size, Brush brush)
        {
            return new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }

    internal sealed class LiveKeyboard : FrameworkElement
    {
        private static readonly string[] Colors =
        {
            "#C38E9A", "#CAA56F", "#96B07C", "#82AFB2", "#8A9EB2",
            "#96B07C", "#CAA56F", "#C38E9A", "#A78FB1", "#A78FB1"
        };
        private static readonly Brush TextDark = Ui.Brush("#17171B");
        private static readonly Brush Shell = Ui.Brush("#333944");
        private static readonly Brush Muted = Ui.Brush("#AFB6C2");
        private static readonly Brush Edge = Ui.Brush("#232832");
        private static readonly Typeface Mono = new Typeface("Courier New");
        private static readonly BitmapSource[] Hands = LoadHands();
        private static BitmapSource[] LoadHands()
        {
            using (var stream = typeof(LiveKeyboard).Assembly.GetManifestResourceStream("TouchTyping.Hands.png"))
            {
                var sprite = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                int half = sprite.PixelWidth / 2;
                return new BitmapSource[] { new CroppedBitmap(sprite, new Int32Rect(0, 0, half, sprite.PixelHeight)),
                    new CroppedBitmap(sprite, new Int32Rect(half, 0, half, sprite.PixelHeight)) };
            }
        }
        private static readonly string[] FingerNames =
        {
            "левый мизинец", "левый безымянный", "левый средний", "левый указательный",
            "правый указательный", "правый средний", "правый безымянный", "правый мизинец",
            "левый большой", "правый большой"
        };
        private static readonly string[] Functions = new[] { "Esc" }.Concat(Enumerable.Range(1, 12).Select(i => "F" + i)).Concat(new[] { "Print" }).ToArray();

        private IEnumerable<Data> rows = Enumerable.Empty<Data>();
        private Data[] targets = new Data[0];
        private string[] held = new string[0];
        private int[] fingers = new int[0];
        private string layout = "EN", incorrect = "", keyboardProfile = "", fnMode = "";
        private bool keys = true, hands = true, functionRow;
        internal bool ReferenceAidRail { get; set; }
        internal string ReferenceAidDescription { get; private set; } = "";

        private double ReferenceHandScale
        {
            get { return Math.Max(.42, Math.Min(1, Math.Min((ActualHeight - 22) / 148, ActualWidth * .13 / 136))); }
        }
        private Rect ReferenceKeyboardBounds()
        {
            double side = hands ? 2 * (136 * ReferenceHandScale + 14) : 0;
            double width = Math.Max(1, Math.Min(940, ActualWidth - side - 14));
            double height = Math.Max(1, ActualHeight - (hands ? 22 : 4));
            return new Rect((ActualWidth - width) / 2, 1, width, height);
        }
        internal string ReferenceAidExtent
        {
            get
            {
                var bounds = ReferenceKeyboardBounds();
                return string.Format(CultureInfo.InvariantCulture, "keys:{0:F0}x{1:F0};hands:{2:F0}x{3:F0}",
                    keys ? bounds.Width : 0, keys ? bounds.Height : 0,
                    hands ? 136 * ReferenceHandScale : 0, hands ? 148 * ReferenceHandScale : 0);
            }
        }

        public void Update(IEnumerable<Data> rows, string layout, IEnumerable<Data> targets, IEnumerable<string> held,
            string incorrect, bool keys, bool hands, bool functionRow, string keyboardProfile = "", string fnMode = "")
        {
            bool reflow = this.keys != keys || this.hands != hands || this.functionRow != functionRow ||
                this.keyboardProfile != keyboardProfile;
            this.rows = rows;
            this.layout = layout;
            this.targets = targets.ToArray();
            this.held = held.ToArray();
            this.incorrect = incorrect;
            this.keys = keys;
            this.hands = hands;
            this.functionRow = functionRow;
            this.keyboardProfile = keyboardProfile;
            this.fnMode = fnMode;
            fingers = this.targets.Select(target => target.I("finger", -1)).Where(i => i >= 0 && i < FingerNames.Length).Distinct().ToArray();
            System.Windows.Automation.AutomationProperties.SetName(this,
                string.Join(", ", fingers.Select(i => FingerNames[i])));
            if (ReferenceAidRail || System.Windows.Automation.AutomationProperties.GetAutomationId(this) == "TypingKeyboard")
            {
                var caps = this.rows.SelectMany(row => row.Items());
                var shown = this.targets.Select(target => {
                    string id = target.S("key");
                    var cap = caps.FirstOrDefault(item => item.S("key") == id);
                    var aux = TypingKeyboard.Auxiliary.FirstOrDefault(a => a.Key == id);
                    var preferred = PreferredPad(id);
                    if (preferred != null) return AuxiliaryLabel(preferred).Replace("\n", " / ") + " (" + preferred.Key + ")";
                    string label = cap == null ? (aux == null ? Title(id) : aux.Label) : CapCaption(id, cap);
                    if (string.IsNullOrWhiteSpace(label)) label = Title(id);
                    return label.Equals(id, StringComparison.OrdinalIgnoreCase) ? label : label + " (" + id + ")";
                });
                string description = keys ? "Раскладка: " + layout + ". Подсвечены: " + string.Join(" + ", shown) :
                    "Экранная клавиатура скрыта.";
                ReferenceAidDescription = description;
                System.Windows.Automation.AutomationProperties.SetHelpText(this, description);
            }
            if (reflow) InvalidateMeasure();
            InvalidateVisual();
        }

        private string CapCaption(string id, Data cap)
        {
            string language = layout == "RU" ? "ru" : "en";
            string caption = cap.S(language, id);
            if (Array.IndexOf(held, "LShift") >= 0 || Array.IndexOf(held, "RShift") >= 0)
            {
                string shifted = cap.S(language + "Shift");
                if (shifted.Length > 0) caption = shifted;
            }
            if (id == "LSuper") return "⊞";
            if (id == "Space") return "";
            if (id == "CapsLock") return "Caps Lock";
            return caption;
        }

        private AuxiliaryCap PreferredPad(string key)
        {
            if (!functionRow || keyboardProfile != "matebook-d16") return null;
            // Keep the existing directional block for ordinary arrows. On the
            // laptop the secondary Home/Pg/Ins/Del marks live on the number pad.
            if (!TypingKeyboard.Auxiliary.Any(cap => cap.Group == "service" && cap.Key == key)) return null;
            return TypingKeyboard.Auxiliary.FirstOrDefault(cap => cap.Alias == key);
        }

        private string AuxiliaryLabel(AuxiliaryCap cap)
        {
            var nav = cap.Alias == null ? null : PreferredPad(cap.Alias);
            if (nav != cap || !targets.Any(t => t.S("key") == cap.Alias)) return cap.Label;
            return cap.Label + "\n" + TypingKeyboard.Auxiliary.First(a => a.Key == cap.Alias).Label;
        }

        private bool AuxiliaryActive(AuxiliaryCap cap)
        {
            return held.Contains(cap.Key) || incorrect == cap.Key || targets.Any(t =>
            {
                string key = t.S("key");
                var pad = PreferredPad(key);
                return pad == null ? cap.Key == key : cap == pad;
            });
        }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        { return new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this); }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? 960 : available.Width;
            if (!keys && !hands) return new Size(width, 0);
            if (ReferenceAidRail) return new Size(width, double.IsInfinity(available.Height) ? 148 : available.Height);
            return new Size(width, (keys ? (functionRow ? 212 : 184) : 146) + (hands ? 25 : 0));
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 20 || (!keys && !hands)) return;
            var bounds = ReferenceAidRail ? ReferenceKeyboardBounds() :
                new Rect((ActualWidth - Math.Min(636, ActualWidth - (hands ? 280 : 10))) / 2, 1,
                    Math.Max(1, Math.Min(636, ActualWidth - (hands ? 280 : 10))), functionRow ? 212 : 184);
            double keyHeight = bounds.Height;
            double keyWidth = bounds.Width;
            if (keys)
            {
                dc.DrawRoundedRectangle(Shell, new Pen(Edge, 1), bounds, 9, 9);
                var extra = functionRow ? TypingKeyboard.Auxiliary.GroupBy(cap => cap.Group)
                    .Where(group => group.Any(AuxiliaryActive)).ToArray() : new IGrouping<string, AuxiliaryCap>[0];
                int extraColumns = extra.Sum(group => group.Max(cap => cap.Column + cap.Width));
                double extraUnit = extraColumns == 0 ? 0 : Math.Min(35, bounds.Width * .30 / extraColumns);
                double mainWidth = keyWidth - extraColumns * extraUnit - extra.Length * 5;
                double height = (bounds.Height - 14) / (functionRow ? 6 : 5);
                double y = bounds.Y + 7;
                if (functionRow)
                {
                    var functions = keyboardProfile == "matebook-d16" ? Functions.Take(13).ToArray() : Functions;
                    double unit = (mainWidth - 14) / functions.Length;
                    for (int i = 0; i < functions.Length; i++)
                        DrawKey(dc, functions[i], functions[i] == "F11" && keyboardProfile == "matebook-d16"
                                ? (fnMode == "hotkeys" ? "PrtSc\nF11" : "F11\nPrtSc") : Title(functions[i]), -1, false,
                            new Rect(bounds.X + 7 + unit * i, y, unit - 3, height - 3));
                    y += height;
                }
                foreach (var row in rows)
                {
                    var items = row.Items().Where(cap => functionRow || !new[] { "Up", "Down", "Left", "Right" }.Contains(cap.S("key"))).ToList();
                    int alt = items.FindIndex(cap => cap.S("key") == "RAlt");
                    if (alt >= 0)
                    {
                        if (!functionRow) items.Insert(alt + 1, new Data(new Dictionary<string, object> { { "key", "Menu" }, { "units", 1.25 } }));
                    }
                    double total = items.Sum(item => item.N("units", 1));
                    if (total <= 0) continue;
                    double wanted = (mainWidth - 14) / total;
                    double x = bounds.X + 7;
                    foreach (var cap in items)
                    {
                        string id = cap.S("key");
                        string caption = CapCaption(id, cap);
                        double width = wanted * cap.N("units", 1);
                        DrawKey(dc, id, caption,
                            cap.I("finger", -1), cap.B("anchor"), new Rect(x, y, Math.Max(1, width - 3), height - 3));
                        x += width;
                    }
                    y += height;
                }
                double groupX = bounds.X + mainWidth;
                foreach (var group in extra)
                {
                    groupX += 5;
                    int columns = group.Max(cap => cap.Column + cap.Width);
                    double rows = group.Max(cap => cap.Row + cap.Height);
                    double unitHeight = Math.Min(height, (bounds.Height - 14) / rows);
                    foreach (var cap in group)
                        DrawKey(dc, cap.Key, AuxiliaryLabel(cap), -1, false,
                            new Rect(groupX + cap.Column * extraUnit, bounds.Y + 7 + cap.Row * unitHeight,
                                Math.Max(1, cap.Width * extraUnit - 3), Math.Max(1, cap.Height * unitHeight - 3)));
                    groupX += columns * extraUnit;
                }
            }
            if (!hands) return;
            double handScale = ReferenceAidRail ? ReferenceHandScale : Math.Min(1, Math.Max(.6, (ActualWidth - keyWidth - 40) / 272));
            double yHands = keys ? Math.Max(0, (keyHeight - 148 * handScale) / 2) : 0;
            double gap = ReferenceAidRail ? 12 : 18;
            double left = keys ? bounds.Left - gap - 136 * handScale : ActualWidth / 2 - 150 * handScale;
            double right = keys ? bounds.Right + gap : ActualWidth / 2 + 14 * handScale;
            DrawHand(dc, left, yHands, handScale, false);
            DrawHand(dc, right, yHands, handScale, true);
            if (fingers.Length > 0)
            {
                string instruction = string.Join("  ·  ", fingers.Select(i => FingerNames[i]));
                var labels = Make(instruction, 12, Muted);
                double captionY = keys ? keyHeight + 6 : yHands + 148 * handScale + 3;
                dc.DrawText(labels, new Point((ActualWidth - labels.Width) / 2, captionY));
            }
        }

        private void DrawKey(DrawingContext dc, string id, string label, int finger, bool anchor, Rect rectangle)
        {
            bool combinedPrint = keyboardProfile == "matebook-d16" && id == "F11";
            var target = targets.FirstOrDefault(item =>
            {
                string key = item.S("key");
                if (key == id || combinedPrint && key == "Print") return true;
                string alias = TypingKeyboard.NavigationAlias(id);
                return alias == key && PreferredPad(key) != null;
            });
            if (target != null) finger = target.I("finger", finger);
            bool assigned = finger >= 0 && finger < Colors.Length;
            bool pressed = held.Contains(id) || combinedPrint && held.Contains("Print");
            bool error = incorrect.Length > 0 && (id == incorrect || combinedPrint && incorrect == "Print");
            bool special = id.Length > 1 && id != "Space";
            Brush fill = error ? Ui.Brush("#DC566A") : special || !assigned ? Ui.Brush("#ABABAB") :
                anchor ? Ui.Brush(id == "F" ? "#669195" : "#708699") : Ui.Brush(Colors[finger]);
            Brush text = TextDark;
            var pen = new Pen(pressed ? Brushes.White : target != null ? Ui.Brush("#CE0D29") : Edge,
                pressed || target != null ? 2.5 : 1);
            dc.DrawRoundedRectangle(fill, pen, rectangle, 4, 4);
            bool twoLines = label.Contains("\n");
            double textSize = id == "LSuper" ? 18 : twoLines ? 9 : special ? 11 : 19;
            if (ReferenceAidRail)
                textSize = Math.Min(textSize, (rectangle.Height - 1) * (twoLines ? .39 : .8));
            var primary = Make(label, textSize, text);
            var offset = new Point(Math.Max(rectangle.X + 2, rectangle.X + (rectangle.Width - primary.Width) / 2),
                rectangle.Y + (rectangle.Height - primary.Height) / 2);
            dc.PushClip(new RectangleGeometry(rectangle));
            dc.DrawText(primary, offset);
            if (anchor) dc.DrawLine(new Pen(text, 1), new Point(rectangle.X + rectangle.Width / 2 - 3, rectangle.Bottom - 3),
                new Point(rectangle.X + rectangle.Width / 2 + 3, rectangle.Bottom - 3));
            dc.Pop();
        }

        private void DrawHand(DrawingContext dc, double x, double y, double scale, bool right)
        {
            dc.DrawImage(Hands[right ? 1 : 0], new Rect(x, y, 136 * scale, 148 * scale));
            var tips = new[] { new Point(11,44), new Point(37,21), new Point(64,12), new Point(92,22), new Point(123,82) };
            var codes = right ? new[] { 7, 6, 5, 4, 9 } : new[] { 0, 1, 2, 3, 8 };
            for (int i = 0; i < tips.Length; i++)
                if (fingers.Contains(codes[i]))
                    dc.DrawEllipse(Ui.Brush("#EA1736"), new Pen(Brushes.White, scale),
                        new Point(x + (right ? 136 - tips[i].X : tips[i].X) * scale, y + tips[i].Y * scale), 4 * scale, 4 * scale);
        }

        internal static string Title(string key)
        {
            switch (key)
            {
                case "Print": return "PrtSc";
                case "Space": return "Пробел";
                case "Super": return "⊞";
                case "LSuper": return "⊞";
                case "RSuper": return "⊞ прав.";
                case "LCtrl": return "Л. Ctrl";
                case "RCtrl": return "Пр. Ctrl";
                case "LShift": return "Л. Shift";
                case "RShift": return "Пр. Shift";
                case "LAlt": return "Л. Alt";
                case "RAlt": return "Пр. Alt";
                case "Up": return "↑";
                case "Down": return "↓";
                case "Left": return "←";
                case "Right": return "→";
                case "CapsLock": return "Caps";
                case "Backspace": return "⌫";
                default: return key;
            }
        }

        private FormattedText Make(string value, double size, Brush brush)
        {
            return new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Mono, size, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }

    internal sealed class LiveTrend : FrameworkElement
    {
        private readonly double?[] points;
        private readonly Brush ink;
        public LiveTrend(IEnumerable<double?> points, Brush ink)
        {
            this.points = points.ToArray();
            this.ink = ink;
            Height = 110;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (points.Length == 0 || ActualWidth < 8) return;
            double high = Math.Max(1, points.Where(point => point.HasValue).Select(point => point.Value).DefaultIfEmpty().Max());
            double left = 5, width = ActualWidth - 10, bottom = ActualHeight - 8, height = bottom - 8;
            var accent = new Pen(ink, 2);
            var lines = new Pen(Ui.Brush("#473B38"), 1);
            for (int level = 0; level <= 4; level++)
            {
                double y = bottom - level * height / 4;
                dc.DrawLine(lines, new Point(left, y), new Point(left + width, y));
            }
            Point? previous = null;
            for (int index = 0; index < points.Length; index++)
            {
                double center = left + index * width / Math.Max(1, points.Length - 1);
                if (!points[index].HasValue)
                {
                    dc.DrawEllipse(Ui.Brush("#EF8691"), null, new Point(center, bottom - 5), 3, 3);
                    previous = null;
                    continue;
                }
                var item = new Point(center, bottom - height * Math.Max(0, points[index].Value) / high);
                if (previous.HasValue) dc.DrawLine(accent, previous.Value, item);
                dc.DrawEllipse(ink, null, item, 3, 3);
                previous = item;
            }
        }
    }

    // The same compact speed/error plot is used by all three kinds of results.
    internal sealed class LiveResultChart : FrameworkElement
    {
        private readonly double?[] speed;
        private readonly double[] errors;
        private static readonly Typeface Labels = LiveUiFont.Typeface(FontWeights.Normal);

        internal LiveResultChart(double?[] speed, double[] errors)
        {
            this.speed = speed ?? new double?[0];
            this.errors = errors ?? new double[0];
            Height = 252;
            SnapsToDevicePixels = true;
            System.Windows.Automation.AutomationProperties.SetName(this, "График скорости и ошибок");
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            if (width < 60 || ActualHeight < 60) return;
            double left = 37, right = width - 12, top = 17, bottom = ActualHeight - 27;
            double plotWidth = right - left, plotHeight = bottom - top;
            double peak = Math.Max(1, speed.Where(item => item.HasValue).Select(item => item.Value).DefaultIfEmpty(0).Max());
            peak = Math.Ceiling(peak / 10) * 10;
            var grid = new Pen(Ui.Brush("#333B4B"), 1);
            for (int tick = 0; tick <= 4; tick++)
            {
                double y = bottom - plotHeight * tick / 4;
                dc.DrawLine(grid, new Point(left, y), new Point(right, y));
                DrawLabel(dc, ((int)(peak * tick / 4)).ToString(), 0, y - 7, 10, "#89909F");
            }
            int length = Math.Max(2, Math.Max(speed.Length, errors.Length));
            for (int tick = 0; tick <= 4; tick++)
            {
                double x = left + plotWidth * tick / 4;
                dc.DrawLine(grid, new Point(x, top), new Point(x, bottom));
                DrawLabel(dc, ((length - 1) * tick / 4).ToString(), x - 4, bottom + 5, 10, "#89909F");
            }

            var area = new PathFigure { StartPoint = new Point(left, bottom), IsClosed = true };
            var line = new PathFigure { StartPoint = new Point(left, bottom), IsClosed = false };
            bool started = false;
            for (int index = 0; index < speed.Length; index++)
            {
                if (!speed[index].HasValue) continue;
                var point = new Point(left + index * plotWidth / (length - 1),
                    bottom - Math.Max(0, speed[index].Value) / peak * plotHeight);
                area.Segments.Add(new LineSegment(point, true));
                if (!started) { line.StartPoint = point; started = true; }
                else line.Segments.Add(new LineSegment(point, true));
            }
            if (started)
            {
                area.Segments.Add(new LineSegment(new Point(right, bottom), true));
                dc.DrawGeometry(Ui.Brush("#5A3479A8"), null, new PathGeometry(new[] { area }));
                dc.DrawGeometry(null, new Pen(Ui.Brush("#48A7DE"), 2.4), new PathGeometry(new[] { line }));
            }
            for (int index = 0; index < errors.Length; index++)
            {
                if (errors[index] <= 0) continue;
                double x = left + index * plotWidth / (length - 1);
                double h = Math.Min(plotHeight * .32, 5 + errors[index] * 7);
                dc.DrawRectangle(Ui.Brush("#D96468"), null, new Rect(x - 2, bottom - h, 4, h));
            }
        }

        private void DrawLabel(DrawingContext dc, string value, double x, double y, double size, string color)
        {
            var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Labels, size, Ui.Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(x, y));
        }
    }
}
