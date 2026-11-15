using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TouchTypingStudio
{
    internal static class FlightUi
    {
        public static Brush Backdrop(StudioProfile p)
        {
            var colors = TrainingProgress.Palette(p);
            return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors[0]),
                (Color)ColorConverter.ConvertFromString(colors[1]), 24);
        }
        public static Brush Accent(StudioProfile p) { return Ui.Brush(TrainingProgress.Palette(p)[2]); }
        public static TextBlock Label(string name, Brush tint = null)
        {
            var t = Ui.Text(name, 11, tint ?? Ui.Brush("#9DB1C9"), true);
            t.FontFamily = Ui.Mono.FontFamily;
            return t;
        }
        public static StackPanel Sheet()
        { return new StackPanel { Margin = new Thickness(28, 22, 28, 20), MaxWidth = 1110 }; }
    }

    internal sealed class AtlasPage : DockPanel
    {
        private const double WorldWidth = 1140, WorldHeight = 510;
        private readonly StudioWindow owner;
        private readonly StudioProfile profile;
        private readonly Grid viewport = new Grid { ClipToBounds = true, MinHeight = 190, Background = Ui.Brush("#0B1420") };
        private readonly Canvas world = new Canvas { Width = WorldWidth, Height = WorldHeight, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        private readonly ScaleTransform scale = new ScaleTransform();
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly Dictionary<Waypoint, Point> positions = new Dictionary<Waypoint, Point>();
        private readonly Dictionary<Waypoint, Button> buttons = new Dictionary<Waypoint, Button>();
        private readonly Dictionary<Waypoint, WaypointState> states;
        private readonly Dictionary<Territory, Point> areas = new Dictionary<Territory, Point>();
        private readonly TextBlock nodeTitle, nodeInfo, nodeState, zoomText;
        private readonly Button activate;
        private readonly bool motion;
        private double zoom = 1;
        private Point? drag;
        private Waypoint selected;
        public UIElement InitialFocus { get { return buttons[selected]; } }

        public AtlasPage(StudioWindow owner, StudioProfile profile, string region)
        {
            this.owner = owner;
            this.profile = profile;
            Background = FlightUi.Backdrop(profile);
            motion = !profile.Training.ReduceMotion && SystemParameters.ClientAreaAnimation;
            states = Atlas.Nodes.ToDictionary(n => n, n => Atlas.State(n, profile));
            var header = new DockPanel { Margin = new Thickness(24, 13, 24, 10) };
            SetDock(header, Dock.Top);
            var links = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 1, 0, 0) };
            SetDock(links, Dock.Right);
            Ui.Add(links, Ui.QuietButton("Режимы", delegate { owner.ShowModes(selected.Region.Id); }));
            Ui.Add(links, Ui.QuietButton("Профиль · F7", owner.ShowAnalytics));
            Ui.Add(links, Ui.QuietButton("Esc  Назад", owner.ClosePanel));
            header.Children.Add(links);
            var titles = new StackPanel();
            Ui.Add(titles, FlightUi.Label("К А М П А Н И Я   /   0 6   Т Е Р Р И Т О Р И Й", FlightUi.Accent(profile)), 2);
            Ui.Add(titles, Ui.Text("Карта навыков", 32, Ui.Ink, true), 1);
            int en = TrainingProgress.MasteryPercent(profile, Layout.English, true, DateTime.Now);
            int ru = TrainingProgress.MasteryPercent(profile, Layout.Russian, true, DateTime.Now);
            int keyboard = (en + ru) / 2;
            Ui.Add(titles, Ui.Text("Освоение клавиатуры " + keyboard + "%  ·  EN " + en + "%  ·  RU " + ru + "%   /   Уровень " +
                TrainingProgress.Level(profile) + "   •   " + TrainingProgress.Xp(profile) + " XP", 13, Ui.Muted));
            header.Children.Add(titles);
            Children.Add(header);

            var inspector = new DockPanel { LastChildFill = true };
            activate = Ui.Button("Открыть · Enter", Activate, true);
            activate.MinWidth = 150;
            activate.VerticalAlignment = VerticalAlignment.Center;
            activate.Margin = new Thickness(10, 0, 0, 0);
            SetDock(activate, Dock.Right);
            inspector.Children.Add(activate);
            var detail = new StackPanel();
            nodeState = FlightUi.Label("");
            nodeTitle = Ui.Text("", 21, Ui.Ink, true);
            nodeInfo = Ui.Text("", 13, Ui.Muted);
            nodeInfo.MaxHeight = 57;
            Ui.Add(detail, nodeState, 3);
            Ui.Add(detail, nodeTitle, 4);
            Ui.Add(detail, nodeInfo);
            inspector.Children.Add(detail);
            var selection = Ui.Panel(inspector, new Thickness(15, 10, 15, 10), Ui.Brush("#121F30"));
            selection.MinHeight = 92;
            selection.Margin = new Thickness(24, 0, 24, 14);
            SetDock(selection, Dock.Bottom);
            Children.Add(selection);

            var toolbar = new DockPanel { Margin = new Thickness(23, 3, 22, 5) };
            SetDock(toolbar, Dock.Bottom);
            var zoomBar = new StackPanel { Orientation = Orientation.Horizontal };
            SetDock(zoomBar, Dock.Right);
            Ui.Add(zoomBar, Ui.QuietButton("−", delegate { Zoom(zoom / 1.22, Center()); }));
            zoomText = Ui.Text("100%", 12, Ui.Muted);
            zoomText.VerticalAlignment = VerticalAlignment.Center;
            zoomText.TextAlignment = TextAlignment.Center;
            zoomText.Width = 42;
            Ui.Add(zoomBar, zoomText);
            Ui.Add(zoomBar, Ui.QuietButton("+", delegate { Zoom(zoom * 1.22, Center()); }));
            Ui.Add(zoomBar, Ui.QuietButton("Вписать", Fit));
            toolbar.Children.Add(zoomBar);
            var legend = Ui.Text("Любой уровень доступен     ✓  Зачтено     ◇  Ветка     ·     Стрелки — выбор", 12, Ui.Muted);
            legend.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(legend);
            Children.Add(toolbar);

            for (int i = 0; i < Atlas.Regions.Length; i++)
            {
                Territory r = Atlas.Regions[i];
                var origin = new Point(10 + i % 3 * 377, 8 + i / 3 * 247);
                areas.Add(r, origin);
                Waypoint[] nodes = Atlas.Nodes.Where(n => n.Region == r).ToArray();
                var regular = nodes.Where(n => n.Kind == WaypointKind.Lesson || n.Kind == WaypointKind.Route).ToArray();
                for (int n = 0; n < regular.Length; n++)
                {
                    int row = n / 6, col = n % 6;
                    if (row % 2 == 1) col = 5 - col;
                    positions[regular[n]] = new Point(origin.X + 44 + col * 54, origin.Y + 94 + row * 57 + ((col + i) % 3 - 1) * 5);
                }
                positions[nodes[nodes.Length - 2]] = new Point(origin.X + 69, origin.Y + 206);
                positions[nodes[nodes.Length - 1]] = new Point(origin.X + 314, origin.Y + 207);
            }
            var art = new AtlasArt(profile, areas, positions, states);
            world.Children.Add(art);
            if (motion) world.Children.Add(new Trail(positions, profile));
            foreach (var territory in Atlas.Regions)
            {
                int n = 0;
                foreach (var node in Atlas.Nodes.Where(x => x.Region == territory))
                {
                    string label = node.Kind == WaypointKind.Exam ? "⚑" : node.Kind == WaypointKind.Branch ? "◇" : (++n).ToString("00");
                    WaypointState state = states[node];
                    bool complete = state == WaypointState.Complete;
                    if (complete) label = "✓";
                    var color = Ui.Brush(territory.Color);
                    var b = Ui.Button(label, delegate { Select(node); });
                    b.FontFamily = Ui.Mono.FontFamily;
                    b.FontSize = node.Kind == WaypointKind.Branch ? 20 : 14.5;
                    b.Margin = new Thickness(0);
                    b.Padding = new Thickness(0);
                    b.Width = node.Kind == WaypointKind.Exam ? 47 : 40;
                    b.Height = 36;
                    b.Foreground = complete ? Ui.Brush("#102531") : color;
                    b.Background = complete ? color : Ui.Brush("#1A3046");
                    b.BorderBrush = color;
                    b.BorderThickness = new Thickness(complete ? 1 : 1.6);
                    string text = node.Title + " · " + StateName(state);
                    b.ToolTip = text;
                    Ui.Name(b, text);
                    b.GotKeyboardFocus += delegate { Select(node); Bring(node); };
                    b.MouseDoubleClick += delegate { Select(node); Activate(); };
                    b.PreviewKeyDown += delegate(object s, KeyEventArgs e)
                    {
                        if (e.Key == Key.Enter)
                        {
                            Select(node);
                            Activate();
                            e.Handled = true;
                        }
                        else if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                        {
                            Navigate(node, e.Key);
                            e.Handled = true;
                        }
                    };
                    Canvas.SetLeft(b, positions[node].X - b.Width / 2);
                    Canvas.SetTop(b, positions[node].Y - b.Height / 2);
                    world.Children.Add(b);
                    buttons.Add(node, b);
                    if (motion)
                        b.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(360))
                            { BeginTime = TimeSpan.FromMilliseconds(18 * n) });
                }
            }
            world.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, slide } };
            viewport.Children.Add(world);
            viewport.SizeChanged += delegate { Fit(); if (IsKeyboardFocusWithin) Bring(selected); };
            viewport.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e) { Zoom(zoom * (e.Delta > 0 ? 1.18 : 1 / 1.18), e.GetPosition(viewport)); e.Handled = true; };
            viewport.PreviewMouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Middle || !IsButton(e.OriginalSource as DependencyObject))
                {
                    drag = e.GetPosition(viewport);
                    viewport.CaptureMouse();
                    e.Handled = true;
                }
            };
            viewport.PreviewMouseUp += delegate { if (drag != null) { drag = null; viewport.ReleaseMouseCapture(); } };
            viewport.LostMouseCapture += delegate { drag = null; };
            viewport.PreviewMouseMove += delegate(object s, MouseEventArgs e)
            {
                if (drag == null) return;
                var point = e.GetPosition(viewport);
                slide.X += point.X - drag.Value.X;
                slide.Y += point.Y - drag.Value.Y;
                drag = point;
                Clamp();
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Add || e.Key == Key.OemPlus) { Zoom(zoom * 1.2, Center()); e.Handled = true; }
                else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { Zoom(zoom / 1.2, Center()); e.Handled = true; }
                else if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control) { Fit(); e.Handled = true; }
            };
            var frame = new Border { Child = viewport, Padding = new Thickness(1), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#34485E"),
                Background = Ui.Brush("#0B1420"), Margin = new Thickness(24, 0, 24, 0) };
            Children.Add(frame);

            Territory initial = Atlas.Find(region ?? "python");
            Select(Atlas.Next(profile, initial) ?? Atlas.Nodes.Last(x => x.Region == initial));
        }

        private static bool IsButton(DependencyObject item)
        {
            for (; item is Visual; item = VisualTreeHelper.GetParent(item))
                if (item is Button) return true;
            return false;
        }

        private static string StateName(WaypointState state)
        { return state == WaypointState.Complete ? "ЗАЧТЕНО" : "ДОСТУПНО"; }

        private void Select(Waypoint node)
        {
            if (node == null) return;
            selected = node;
            WaypointState state = states[node];
            nodeState.Foreground = Ui.Brush(node.Region.Color);
            nodeState.Text = node.Region.Name.ToUpperInvariant() + "   /   " + StateName(state);
            nodeTitle.Text = node.Title;
            if (node.Lesson != null)
            {
                int done = TrainingProgress.Guided(node.Lesson, profile), need = TrainingProgress.GuidedGoal(node.Lesson, profile);
                nodeInfo.Text = node.Lesson.FocusKeys + "   ·   " + (state == WaypointState.Complete ?
                    node.Lesson.GoodRounds(profile) + "/" + node.Lesson.Rounds.Length + " вариантов зачтено" :
                    Math.Min(done, need) + "/" + need + " с подсказками, затем самостоятельный вариант");
            }
            else if (node.Kind == WaypointKind.Exam)
            {
                int left = node.Region.Trial.Length - profile.Exams.Count(e => e.StartsWith(node.Region.Id + "/", StringComparison.Ordinal));
                nodeInfo.Text = node.Region.Trial.Length > 0 ?
                    (left == 0 ? "Все маршруты экзамена зачтены." : "Самостоятельные задания в настоящих окнах. Осталось: " + left + ".") :
                    "Новый фрагмент без подсказок. Порог " + profile.Training.Goal + "%; скорость не влияет.";
            }
            else
                nodeInfo.Text = node.Route != null ? node.Route.Goal + " Зачёт узла — самостоятельный проход." :
                    "Дополнительное испытание. Основной маршрут открыт независимо от него.";
            activate.Content = state == WaypointState.Complete ? "Повторить · Enter" : "Открыть · Enter";
            if (node.Kind == WaypointKind.Exam && node.Region.Trial.Length > 0 && state == WaypointState.Complete)
                activate.Content = "К маршрутам";
        }

        private void Activate()
        {
            if (selected != null) owner.OpenWaypoint(selected);
        }

        private void Navigate(Waypoint from, Key direction)
        {
            var vector = direction == Key.Left ? new Vector(-1, 0) : direction == Key.Right ? new Vector(1, 0) :
                direction == Key.Up ? new Vector(0, -1) : new Vector(0, 1);
            var start = positions[from];
            var target = positions.Where(p => Vector.Multiply(p.Value - start, vector) > 14)
                .OrderBy(p =>
                {
                    Vector d = p.Value - start;
                    double forward = Vector.Multiply(d, vector), side = Math.Abs(Vector.CrossProduct(d, vector));
                    return forward + 3.2 * side + (side / forward) * 10;
                }).Select(p => p.Key).FirstOrDefault();
            if (target != null) buttons[target].Focus();
        }

        private Point Center() { return new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2); }
        private double BaseScale
        {
            get { return Math.Max(.15, Math.Min((viewport.ActualWidth - 14) / WorldWidth, (viewport.ActualHeight - 10) / WorldHeight)); }
        }
        private void Fit()
        {
            zoom = 1;
            zoomText.Text = "100%";
            scale.ScaleX = scale.ScaleY = BaseScale;
            slide.X = slide.Y = 0;
            Clamp();
        }
        private void Zoom(double value, Point at)
        {
            zoom = Math.Max(1, Math.Min(3, value));
            double old = Math.Max(.01, scale.ScaleX), next = BaseScale * zoom;
            slide.X = at.X - (at.X - slide.X) * next / old;
            slide.Y = at.Y - (at.Y - slide.Y) * next / old;
            scale.ScaleX = scale.ScaleY = next;
            zoomText.Text = Math.Round(zoom * 100) + "%";
            Clamp();
        }
        private void Bring(Waypoint node)
        {
            if (!positions.ContainsKey(node) || viewport.ActualWidth == 0) return;
            Point pos = positions[node];
            double x = pos.X * scale.ScaleX + slide.X, y = pos.Y * scale.ScaleY + slide.Y;
            slide.X += Math.Max(0, 30 - x) - Math.Max(0, x - viewport.ActualWidth + 30);
            slide.Y += Math.Max(0, 28 - y) - Math.Max(0, y - viewport.ActualHeight + 28);
            Clamp();
        }
        private void Clamp()
        {
            double x = viewport.ActualWidth - WorldWidth * scale.ScaleX, y = viewport.ActualHeight - WorldHeight * scale.ScaleY;
            slide.X = x > 0 ? x / 2 : Math.Max(x, Math.Min(0, slide.X));
            slide.Y = y > 0 ? y / 2 : Math.Max(y, Math.Min(0, slide.Y));
        }

        private sealed class AtlasArt : FrameworkElement
        {
            private readonly StudioProfile profile;
            private readonly Dictionary<Territory, Point> areas;
            private readonly Dictionary<Waypoint, Point> positions;
            private readonly Dictionary<Waypoint, WaypointState> states;
            public AtlasArt(StudioProfile profile, Dictionary<Territory, Point> areas, Dictionary<Waypoint, Point> positions,
                Dictionary<Waypoint, WaypointState> states)
            {
                this.profile = profile; this.areas = areas; this.positions = positions; this.states = states;
                Width = WorldWidth; Height = WorldHeight; IsHitTestVisible = false;
            }

            private void Text(DrawingContext d, string text, Point point, double size, Brush color, bool mono = false)
            {
                var font = mono ? Ui.Mono : new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                d.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, size, color,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
            }
            private static Color WithAlpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }
            private static SolidColorBrush Tint(Color c, byte alpha) { return new SolidColorBrush(WithAlpha(c, alpha)); }
            protected override void OnRender(DrawingContext d)
            {
                var rng = new Random(71);
                for (int i = 0; i < 290; i++)
                {
                    bool bright = i % 11 == 0;
                    d.DrawEllipse(Ui.Brush(bright ? "#72859E" : "#33475C"), null, new Point(rng.Next(1136), rng.Next(507)),
                        bright ? 1.05 : .55, bright ? 1.05 : .55);
                }
                var guide = new Pen(Ui.Brush("#2B3C50"), 1) { DashStyle = DashStyles.Dot };
                for (int i = 0; i < areas.Count; i++)
                {
                    Territory deck = Atlas.Regions[i];
                    Point p = areas[deck];
                    var color = ((SolidColorBrush)Ui.Brush(deck.Color)).Color;
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(p.X + 8, p.Y + 32), true, true);
                        ctx.PolyLineTo(new[]
                        {
                            new Point(p.X + 28, p.Y + 7), new Point(p.X + 286 + i % 2 * 18, p.Y + 7),
                            new Point(p.X + 324, p.Y + 26), new Point(p.X + 353, p.Y + 32),
                            new Point(p.X + 359, p.Y + 189 - i % 3 * 7), new Point(p.X + 341, p.Y + 227),
                            new Point(p.X + 309, p.Y + 235), new Point(p.X + 55 + i % 3 * 12, p.Y + 235),
                            new Point(p.X + 11, p.Y + 211), new Point(p.X + 3, p.Y + 161)
                        }, true, false);
                    }
                    var ocean = Ui.Brush(TrainingProgress.Palette(profile)[1]).Color;
                    var fill = new LinearGradientBrush(WithAlpha(color, 35), WithAlpha(ocean, 230), new Point(.1, 0), new Point(.85, .82));
                    d.DrawGeometry(fill, new Pen(Tint(color, 92), 1.2), geometry);
                    d.PushClip(geometry);
                    for (int y = 0; y <= 240; y += 23)
                        d.DrawLine(new Pen(Tint(color, 15), .6), new Point(p.X, p.Y + y), new Point(p.X + 366, p.Y + y));
                    for (int x = 0; x <= 390; x += 23)
                        d.DrawLine(new Pen(Tint(color, 15), .6), new Point(p.X + x, p.Y), new Point(p.X + x - 42, p.Y + 240));
                    for (int contour = 0; contour < 8; contour++)
                        d.DrawEllipse(null, new Pen(Tint(color, 15), .8), new Point(p.X + 305, p.Y + 29), 22 + contour * 12, 11 + contour * 8);
                    d.Pop();
                    Text(d, (i + 1).ToString("00") + "  /  " + deck.Tag, p + new Vector(24, 17), 10, Ui.Brush(deck.Color), true);
                    Text(d, deck.Name, p + new Vector(23, 33), 24, Ui.Ink);
                    var core = Atlas.Nodes.Where(x => x.Region == deck && (x.Kind == WaypointKind.Lesson || x.Kind == WaypointKind.Route)).ToArray();
                    string readout = core.Count(x => states[x] == WaypointState.Complete) + " / " + core.Length;
                    Text(d, readout, p + new Vector(300, 53), 12, Ui.Brush(deck.Color), true);
                    if (i % 3 < 2) d.DrawLine(guide, p + new Vector(360, 126), p + new Vector(380, 126));
                    if (i < 3) d.DrawLine(guide, p + new Vector(183, 235), p + new Vector(183, 255));
                }
                foreach (var node in Atlas.Nodes.Where(n => n.Previous != null))
                {
                    Color color = Ui.Brush(node.Region.Color).Color;
                    Point from = positions[node.Previous], to = positions[node];
                    var road = Route(from, to, node.Kind == WaypointKind.Branch);
                    bool complete = states[node] == WaypointState.Complete;
                    var pen = new Pen(Tint(color, complete ? (byte)175 : (byte)110), complete ? 2 : 1.4);
                    if (complete) d.DrawGeometry(null, new Pen(Tint(color, 29), 7), road);
                    else pen.DashStyle = node.Kind == WaypointKind.Branch ? DashStyles.Dot : DashStyles.Dash;
                    d.DrawGeometry(null, pen, road);
                }
            }
        }

        private static PathGeometry Route(Point from, Point to, bool branch)
        {
            var bend = new Point((from.X + to.X) / 2 + (branch ? -30 : 0), (from.Y + to.Y) / 2);
            if (Math.Abs(from.Y - to.Y) < 20) bend.Y -= branch ? 12 : 5;
            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = from };
            figure.Segments.Add(new QuadraticBezierSegment(bend, to, true));
            path.Figures.Add(figure);
            return path;
        }

        private sealed class Trail : FrameworkElement
        {
            private readonly List<Tuple<PathGeometry, Brush>> trails = new List<Tuple<PathGeometry, Brush>>();
            private int frame;
            public Trail(Dictionary<Waypoint, Point> positions, StudioProfile profile)
            {
                Width = WorldWidth; Height = WorldHeight; IsHitTestVisible = false;
                foreach (var region in Atlas.Regions)
                {
                    var next = Atlas.Next(profile, region);
                    if (next == null) continue;
                    Point point = positions[next];
                    Point from = next.Previous == null ? point - new Vector(29, -25) : positions[next.Previous];
                    trails.Add(Tuple.Create(Route(from, point, false), (Brush)Ui.Brush(region.Color)));
                }
                var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(42) };
                clock.Tick += delegate { frame = (frame + 1) % 100; InvalidateVisual(); };
                Loaded += delegate { if (trails.Count > 0) clock.Start(); };
                Unloaded += delegate { clock.Stop(); };
            }
            protected override void OnRender(DrawingContext d)
            {
                int i = 0;
                foreach (var trail in trails)
                {
                    Point point, tangent;
                    trail.Item1.GetPointAtFractionLength(((frame + i++ * 17) % 100) / 100.0, out point, out tangent);
                    d.PushOpacity(.23);
                    d.DrawEllipse(trail.Item2, null, point, 7, 7);
                    d.Pop();
                    d.DrawEllipse(trail.Item2, null, point, 2, 2);
                }
            }
        }
    }
}
