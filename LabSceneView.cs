using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Automation.Peers;

namespace TouchTypingStudio
{
    internal sealed class LabSceneView : FrameworkElement
    {
        private Data scene = Data.Empty;
        private string classApp = "ubuntu";
        private DrawingContext context;
        private readonly Typeface regular = new Typeface("Segoe UI");
        private readonly Typeface mono = new Typeface("Consolas");
        private const double WidthUnits = 1200, HeightUnits = 520;

        protected override AutomationPeer OnCreateAutomationPeer()
        { return new FrameworkElementAutomationPeer(this); }

        internal LabSceneView() { ClipToBounds = true; MinHeight = 180; }
        internal void Update(Data next, string app = "ubuntu")
        {
            scene = next;
            classApp = app;
            System.Windows.Automation.AutomationProperties.SetName(this,
                (app == "chrome" ? "Учебный Chrome" : app == "code" ? "Учебный VS Code" : "Учебный рабочий стол Ubuntu") +
                ". Раскладка " + scene.S("layout") + ". " +
                string.Join(". ", scene.Items("windows").Where(w => w.B("active")).Select(w => w.S("title") + ". " + w.S("status"))));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 1 || ActualHeight < 1) return;
            context = dc;
            if (classApp == "chrome" || classApp == "code")
            {
                DrawImmersive(dc);
                context = null;
                return;
            }
            double scale = Math.Min(ActualWidth / WidthUnits, ActualHeight / HeightUnits);
            dc.PushTransform(new TranslateTransform((ActualWidth - WidthUnits * scale) / 2, (ActualHeight - HeightUnits * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, WidthUnits, HeightUnits), 10, 10));
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(44, 22, 56), Color.FromRgb(96, 37, 60), 30), null,
                new Rect(0, 0, WidthUnits, HeightUnits));
            dc.DrawGeometry(Ui.Brush("#732E4B"), null, Geometry.Parse("M0,520 L500,70 L900,520Z"));
            dc.DrawGeometry(Ui.Brush("#4A2340"), null, Geometry.Parse("M1200,50 L580,520 L1200,520Z"));
            Rectangle(0, 0, 1200, 28, "#151519");
            Text("●  Обзор", 15, 6, 11, "#EEEEF0");
            Text("Ubuntu  ·  учебный рабочий стол", 469, 6, 11, "#CCCCD4");
            Text(scene.S("layout", "EN").ToLowerInvariant() + "    ◉    ◖)    ▰", 1080, 6, 11, "#E5E5E8");
            var windows = scene.Items("windows").Where(w => w.S("state") != "closed" && w.S("state") != "minimized" &&
                w.I("workspace") == scene.I("workspace")).OrderBy(w => w.B("active") ? 1 : 0).ToArray();
            if (scene.B("showDesktop")) windows = new Data[0];
            else windows = ForegroundWindows(windows);
            int index = 0;
            foreach (var window in windows)
            {
                Rect bounds = Bounds(window, index++);
                DrawWindow(window, bounds);
            }
            if (windows.Length == 0)
            {
                Text("Рабочий стол " + (scene.I("workspace") + 1), 472, 218, 26, "#F7E9F4");
                Text("⊞ — обзор приложений", 485, 260, 14, "#CBA9C3");
            }
            DrawDock();
            if (scene.B("overview")) DrawOverview();
            if (scene.B("switcher")) DrawSwitcher();
            if (scene.B("screenshot")) DrawScreenshot();
            dc.Pop(); dc.Pop(); dc.Pop();
            context = null;
        }

        private void DrawImmersive(DrawingContext dc)
        {
            double scale = ActualWidth / WidthUnits;
            double height = ActualHeight / scale;
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, WidthUnits, height)));
            Rectangle(0, 0, WidthUnits, height, classApp == "chrome" ? "#F8FAFC" : "#1E1E1E");
            var candidates = scene.Items("windows").Where(w =>
                (w.S("app") == classApp || classApp == "code" && w.S("app") == "vscode") &&
                w.S("state") != "closed" && w.S("state") != "minimized").ToArray();
            var window = candidates.FirstOrDefault(w => w.B("active")) ?? candidates.FirstOrDefault();
            if (window != null)
            {
                // Hide the simulated Ubuntu title bar while preserving the app's own tabs and tools.
                DrawWindow(window, new Rect(0, -29, WidthUnits, height + 29));
            }
            else
            {
                Text(classApp == "chrome" ? "Chrome закрыт" : "VS Code закрыт", 75, height * .35, 30,
                    classApp == "chrome" ? "#2F4055" : "#E2E2E2", 1050);
                Text("Откройте приложение сочетанием из задания.", 75, height * .35 + 48, 16,
                    classApp == "chrome" ? "#5F6C7C" : "#A8B0BB", 1050);
            }
            if (scene.B("switcher")) DrawSwitcher();
            if (scene.B("screenshot")) DrawScreenshot(height);
            dc.Pop();
            dc.Pop();
        }

        private static Data[] ForegroundWindows(Data[] windows)
        {
            if (windows.Length < 3) return windows;
            var active = windows.LastOrDefault(w => w.B("active"));
            if (active == null) return windows.Skip(windows.Length - 2).ToArray();
            string state = active.S("state");
            if (state == "fullscreen" || state == "full" || state == "maximized" || state == "max")
                return new[] { active };
            if (state == "left" || state == "right" || state.StartsWith("snapped-", StringComparison.Ordinal))
            {
                string otherSide = state == "left" || state == "snapped-left" ? "right" : "left";
                var counterpart = windows.LastOrDefault(w => w != active &&
                    (w.S("state") == otherSide || w.S("state") == "snapped-" + otherSide));
                return counterpart == null ? new[] { active } : new[] { counterpart, active };
            }
            var behind = windows.LastOrDefault(w => w != active);
            return behind == null ? new[] { active } : new[] { behind, active };
        }

        private static Rect Bounds(Data window, int index)
        {
            string state = window.S("state");
            if (state == "fullscreen" || state == "full") return new Rect(0, 28, 1200, 492);
            if (state == "maximized" || state == "max") return new Rect(57, 34, 1135, 477);
            if (state == "left" || state == "snapped-left") return new Rect(61, 36, 565, 472);
            if (state == "right" || state == "snapped-right") return new Rect(627, 36, 565, 472);
            return window.B("active") ? new Rect(112, 50, 1035, 438) : new Rect(79 + index * 18, 40 + index * 12, 980, 430);
        }

        private void DrawDock()
        {
            Rounded(6, 122, 49, 278, "#E819171F", 13);
            string[] apps = { "chrome", "code", "terminal", "files", "apps" };
            for (int i = 0; i < apps.Length; i++)
            {
                Icon(apps[i], 16, 134 + i * 51, 29);
                if (scene.Items("windows").Any(w => w.S("app") == apps[i] && w.S("state") != "closed"))
                    Rectangle(9, 145 + i * 51, 3, 8, "#F78D4D");
            }
        }

        private void DrawWindow(Data window, Rect bounds)
        {
            context.PushTransform(new TranslateTransform(bounds.X, bounds.Y));
            context.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height), 8, 8));
            Rounded(0, 0, bounds.Width, bounds.Height, "#252527", 8);
            string app = window.S("app");
            Rectangle(0, 0, bounds.Width, 29, window.B("active") ? "#303034" : "#28282C");
            Text(window.S("title", AppTitle(app)), 14, 7, 11, "#D8D8DF");
            Text("—       □", bounds.Width - 89, 5, 12, "#C6C6CE");
            Rounded(bounds.Width - 26, 5, 18, 18, "#D75636", 9);
            Text("×", bounds.Width - 22, 3, 15, "#FFFFFF");
            double w = bounds.Width, h = bounds.Height;
            if (app == "chrome") DrawChrome(window, w, h);
            else if (app == "code" || app == "vscode") DrawCode(window, w, h);
            else if (app == "terminal") DrawTerminal(window, w, h);
            else DrawFiles(window, w, h);
            context.Pop(); context.Pop();
        }

        private void DrawChrome(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#F8FAFC");
            Rectangle(0, 29, w, 35, "#292C34");
            var tabs = window.Items("tabs").ToArray();
            double tabWidth = Math.Min(200, (w - 65) / Math.Max(1, tabs.Length));
            for (int i = 0; i < tabs.Length; i++)
            {
                Rounded(7 + i * tabWidth, 35, tabWidth - 4, 29,
                    i == window.I("activeTab") ? "#414652" : "#2C3039", 7);
                Text("◉", 16 + i * tabWidth, 42, 10, "#80BCDF");
                Text(tabs[i].S("title", "Новая вкладка"), 33 + i * tabWidth, 42, 11, "#E7EAF1", tabWidth - 53);
                Text("×", tabWidth - 15 + i * tabWidth, 40, 12, "#A4ACBC");
            }
            Text("+", 17 + tabs.Length * tabWidth, 38, 20, "#C2C9D6");
            Rectangle(0, 64, w, 38, "#414652");
            Text("‹   ›   ⟳", 13, 71, 17, "#CDD5E2");
            Rounded(95, 70, w - 142, 26, window.S("focus") == "url" ? "#D8E9FD" : "#252A34", 13);
            Text("◈   " + (window.S("focus") == "url" ? window.S("input", window.S("url")) : window.S("url", "https://practice.local")),
                109, 76, 11, window.S("focus") == "url" ? "#1F3E5F" : "#D4DCEB", w - 180);
            Text("⋮", w - 24, 68, 21, "#D6DDE9");
            double left = Math.Max(28, w * .075);
            double pageScale = window.N("zoom", 100) / 100;
            string body = window.S("body");
            var active = tabs.ElementAtOrDefault(window.I("activeTab"));
            if (body.Length == 0 && active != null) body = active.S("body");
            Text(active == null ? "Рабочие материалы" : active.S("title", "Рабочие материалы"), left, 122, 25 * pageScale, "#202A3B", w - left * 2);
            Text(window.S("url"), left, 159, 11, "#52657C", w - left * 2);
            Rectangle(left, 185, Math.Max(10, w - left * 2), 1, "#E1E7EF");
            Text(body.Length > 0 ? body : "Документация проекта\n\nОткройте нужную вкладку или страницу.\nВсе действия выполняются внутри учебного рабочего стола.",
                left, 200, 14 * pageScale, "#34445D", w - left * 2, 7);
            if (window.B("bookmarks"))
            {
                Rectangle(0, 101, w, 25, "#E5EAF0");
                Text("☆  Python    ☆  piling-main    ☆  Ubuntu", 15, 107, 10, "#43566D");
            }
            if (window.S("find").Length > 0 || window.S("focus") == "find")
            {
                Rounded(w - 337, 104, 323, 41, "#343A45", 5);
                Text("⌕  " + (window.S("focus") == "find" ? window.S("input") : window.S("find")), w - 321, 116, 12, "#FFFFFF", 240);
                Text("↑  ↓  ×", w - 79, 115, 13, "#CFD7E8");
            }
            if (window.S("status").Length > 0)
            {
                Rectangle(0, h - 23, w, 23, "#ECF0F6");
                Text(window.S("status"), 12, h - 18, 10, "#4B607B", w - 30);
            }
        }

        private void DrawCode(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#1E1E1E");
            Rectangle(0, 29, 44, h - 50, "#333333");
            Text("▤\n\n⌕\n\n⑂\n\n▷\n\n▦", 12, 49, 21, "#A7ACB3", 30);
            Text("⚙", 11, h - 62, 22, "#A7ACB3");
            bool sidebar = window.B("sidebar", true);
            double x = sidebar ? Math.Min(219, w * .25) : 44;
            if (sidebar)
            {
                Rectangle(44, 29, x - 44, h - 50, "#252526");
                Text("EXPLORER", 60, 44, 10, "#B4BAC4");
                Text("⌄  PILING-MAIN\n  ⌄  src\n       ◇  main.py\n       ◇  signals.py\n       ◇  config.py\n  ›  tests\n     README.md\n     requirements.txt", 55, 76, 12, "#C6CDD5", x - 62, 12, true);
            }
            var tabs = window.Items("tabs").ToArray();
            if (tabs.Length == 0) tabs = new[] { new Data(new System.Collections.Generic.Dictionary<string, object> { { "title", "main.py" } }) };
            double tabWidth = Math.Min(155, (w - x - 12) / tabs.Length);
            for (int i = 0; i < tabs.Length; i++)
            {
                Rectangle(x + i * tabWidth, 29, tabWidth - 1, 31, i == window.I("activeTab") ? "#1E1E1E" : "#2D2D2D");
                Text("◇  " + tabs[i].S("title"), x + i * tabWidth + 10, 39, 11, "#C1D3E2", tabWidth - 25);
            }
            Text("piling-main  ›  src  ›  " + tabs[Math.Min(Math.Max(0, window.I("activeTab")), tabs.Length - 1)].S("title"),
                x + 15, 69, 10, "#8F98A7", w - x - 25);
            string code = window.S("body");
            if (window.S("focus") == "editor" && window.S("input").Length > 0) code = window.S("input");
            if (code.Length == 0) code = "import numpy as np\n\ndef fft(signal):\n    fft_values = np.fft.fft(signal)\n    return np.fft.fftshift(fft_values)\n\nresult = fft(signal)";
            string[] lines = code.Split('\n');
            double editorWidth = window.B("split") ? (w - x) / 2 : w - x;
            for (int i = 0; i < Math.Min(lines.Length, 13); i++)
            {
                Text((i + 1).ToString(), x + 10, 94 + i * 20, 11, "#707987", 24, 1, true);
                string line = lines[i];
                string ink = line.TrimStart().StartsWith("#") ? "#6A9955" : line.Contains("def ") || line.Contains("import ") ? "#C586C0" : "#C7D9E7";
                Text(line, x + 42, 94 + i * 20, 12, ink, editorWidth - 48, 1, true);
            }
            if (window.B("selected"))
                Rectangle(x + 40, 93, Math.Max(20, Math.Min(editorWidth - 50, 420)), 20, "#664F7099");
            if (window.S("focus") == "body")
            {
                int cursor = window.I("cursor");
                int before = Math.Min(cursor, code.Length);
                string prefix = code.Substring(0, before);
                int line = prefix.Count(character => character == '\n');
                int column = prefix.Length - prefix.LastIndexOf('\n') - 1;
                Rectangle(x + 42 + Math.Min(70, column) * 7.2, 95 + Math.Min(12, line) * 20, 1, 15, "#F0F0F0");
            }
            if (window.S("focus") == "terminal")
            {
                double terminalY = Math.Max(155, h * .55);
                Rectangle(x, terminalY, w - x, h - terminalY - 21, "#181818");
                Text("TERMINAL      OUTPUT      PROBLEMS", x + 15, terminalY + 8, 10, "#AAB0BD");
                string output = string.Join("\n", scene.D("terminal").Items("output").Select(line => line.Value()).Reverse().Take(4).Reverse());
                Text(output, x + 15, terminalY + 30, 11, "#B7C5D4", w - x - 30, 4, true);
                Text("student@ubuntu:~$ " + scene.S("terminalInput") + "▌", x + 15, h - 47, 12, "#E9EFF6", w - x - 30, 1, true);
            }
            if (window.B("split"))
            {
                Rectangle(x + editorWidth, 60, 1, h - 81, "#414854");
                Text("◇  main.py", x + editorWidth + 13, 76, 11, "#BFD0DF");
                Text(code, x + editorWidth + 20, 105, 12, "#C7D9E7", editorWidth - 35, 11, true);
            }
            Rectangle(0, h - 21, w, 21, "#0879B9");
            Text("⑂ main    ◉ 0   ⚠ 0      " + window.S("status", "Готово"), 13, h - 17, 10, "#FFFFFF", w - 205);
            Text("UTF-8    Python    ◉", w - 164, h - 17, 10, "#FFFFFF");
            string focus = window.S("focus");
            if (focus == "palette" || focus == "quick-open" || focus == "goto-line")
            {
                double pw = Math.Min(570, w - 105), px = (w - pw) / 2;
                Rounded(px, 40, pw, 166, "#252526", 5);
                Rounded(px + 7, 47, pw - 14, 31, "#3C3C3C", 3);
                Text((focus == "palette" ? "> " : "⌕  ") + window.S("input"), px + 16, 57, 13, "#EEEEF3", pw - 40);
                Rectangle(px + 7, 84, pw - 14, 26, "#094771");
                Text(focus == "quick-open" ? "◇  main.py          src" : "Форматировать документ", px + 17, 91, 12, "#FFFFFF");
                Text(focus == "quick-open" ? "◇  signals.py\n◇  config.py\nREADME.md" : "Разделить редактор\nПоказать терминал\nПереключить боковую панель", px + 17, 122, 12, "#C8CED8", pw - 35, 3);
            }
            else if (focus == "find" || focus == "project-search" || window.S("find").Length > 0)
            {
                Rounded(Math.Max(x + 4, w - 362), 63, Math.Min(345, w - x - 8), 36, "#252526", 3);
                Text("⌕  " + (focus == "find" ? window.S("input") : window.S("find")), Math.Max(x + 15, w - 348), 74, 12, "#DDE2EB", 260);
            }
        }

        private void DrawTerminal(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#2B1028");
            string output = string.Join("\n", scene.D("terminal").Items("output").Select(line => line.Value()).Reverse().Take(13).Reverse());
            Text(output, 16, 45, 13, "#E4DCE6", w - 35, 14, true);
            string input = scene.S("terminalInput");
            string cwd = scene.D("terminal").S("cwd", "~/piling-main");
            double y = Math.Min(h - 45, 72 + Math.Min(11, output.Split('\n').Length) * 20);
            Text("learner@ubuntu", 16, y, 13, "#8EE087", 180, 1, true);
            Text(":" + cwd + "$ " + input + "▌", 146, y, 13, "#F2E8F1", w - 162, 1, true);
            if (window.S("status").Length > 0) Text(window.S("status"), 16, h - 25, 11, "#C9B3C5", w - 35);
        }

        private void DrawFiles(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#F7F7F8");
            Rectangle(0, 29, 145, h - 29, "#E7E7EA");
            Text("Недавние\nИзбранное\nДомашняя папка\nДокументы\nЗагрузки\nКорзина", 14, 57, 12, "#494B54", 126, 10);
            Text("⌂  Домашняя папка  ›  piling-main", 164, 48, 13, "#424550", w - 179);
            string[] files = { "src", "tests", "README.md", "main.py", "report.txt", "requirements.txt" };
            for (int i = 0; i < files.Length; i++)
            {
                double x = 180 + (i % 3) * 190, y = 100 + i / 3 * 110;
                Rounded(x, y, 52, 42, i < 2 ? "#D89357" : "#E0E7F0", 5);
                Text(i < 2 ? "▰" : "≡", x + 15, y + 5, 24, i < 2 ? "#F5C591" : "#6B8FB6");
                Text(files[i], x - 14, y + 55, 11, "#454C59", 160);
            }
        }

        private void DrawOverview()
        {
            Rectangle(0, 28, 1200, 492, "#EF17202B");
            Rounded(360, 51, 480, 38, "#3C4455", 18);
            Text("⌕  Введите запрос для поиска", 389, 61, 14, "#BEC9DA");
            int i = 0;
            foreach (var window in scene.Items("windows").Where(w => w.S("state") != "closed"))
            {
                double x = 125 + i % 3 * 347, y = 132 + i / 3 * 165;
                Rounded(x, y, 312, 134, "#333A49", 10);
                Icon(window.S("app"), x + 132, y + 25, 40);
                Text(window.S("title"), x + 22, y + 90, 12, "#E4EBF5", 270);
                i++;
            }
            int count = Math.Max(2, scene.I("workspaceCount", 2));
            for (int workspace = 0; workspace < count; workspace++)
            {
                Rounded(510 + workspace * 55, 462, 42, 28, workspace == scene.I("workspace") ? "#8C77A3" : "#495062", 4);
                Text((workspace + 1).ToString(), 525 + workspace * 55, 467, 11, "#FFFFFF");
            }
        }

        private void DrawScreenshot(double height = HeightUnits)
        {
            Rectangle(0, 0, 1200, height, "#66000000");
            context.DrawRoundedRectangle(null, new Pen(Ui.Brush("#F3F0F6"), 2),
                new Rect(115, 62, 970, Math.Max(150, height - 160)), 5, 5);
            Rounded(412, height - 77, 380, 53, "#F4252530", 14);
            Text(scene.B("screenshotCopied") ? "✓  Снимок скопирован в учебный буфер" : "▣  Учебный снимок готов   ·   Ctrl+C",
                436, height - 59, 14, "#FFFFFF", 340);
        }

        private void DrawSwitcher()
        {
            Rounded(338, 185, 524, 143, "#F4181B23", 15);
            var windows = scene.Items("windows").Where(w => w.S("state") != "closed").Take(4).ToArray();
            for (int i = 0; i < windows.Length; i++)
            {
                double x = 354 + i * 124;
                if (windows[i].B("active")) Rounded(x, 200, 111, 111, "#4F485E", 9);
                Icon(windows[i].S("app"), x + 35, 216, 41);
                Text(AppTitle(windows[i].S("app")), x + 9, 280, 11, "#EAE5F0", 101);
            }
        }

        private static string AppTitle(string app)
        { return app == "chrome" ? "Google Chrome" : app == "code" || app == "vscode" ? "Visual Studio Code" : app == "terminal" ? "Терминал" : "Файлы"; }

        private void Icon(string app, double x, double y, double size)
        {
            if (app == "chrome")
            {
                context.DrawEllipse(Ui.Brush("#E9B840"), null, new Point(x + size / 2, y + size / 2), size / 2, size / 2);
                context.DrawGeometry(Ui.Brush("#D65B52"), null, Triangle(x, y, size));
                context.DrawEllipse(Ui.Brush("#3883BC"), new Pen(Ui.Brush("#E9EDF1"), size * .06), new Point(x + size / 2, y + size / 2), size * .23, size * .23);
            }
            else
            {
                Rounded(x, y, size, size, app == "code" ? "#137AB3" : app == "terminal" ? "#372039" : app == "files" ? "#D89557" : "#41404D", size * .15);
                Text(app == "code" ? "❯" : app == "terminal" ? ">_" : app == "apps" ? "⠿" : "▰", x + size * .16, y + size * .1, size * .55, "#F4F1F5");
            }
        }
        private static Geometry Triangle(double x, double y, double size)
        {
            var figure = new PathFigure { StartPoint = new Point(x, y + size * .2), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(x + size, y + size * .2), true));
            figure.Segments.Add(new LineSegment(new Point(x + size * .5, y + size * .5), true));
            return new PathGeometry(new[] { figure });
        }
        private void Rectangle(double x, double y, double w, double h, string fill)
        { context.DrawRectangle(Ui.Brush(fill), null, new Rect(x, y, Math.Max(0, w), Math.Max(0, h))); }
        private void Rounded(double x, double y, double w, double h, string fill, double radius)
        { context.DrawRoundedRectangle(Ui.Brush(fill), null, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), radius, radius); }
        private void Text(string value, double x, double y, double size, string ink, double width = 1200, int lines = 1, bool code = false)
        {
            if (width <= 0 || string.IsNullOrEmpty(value)) return;
            var label = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, code ? mono : regular,
                size, Ui.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            label.MaxTextWidth = Math.Max(1, width);
            label.MaxLineCount = lines;
            label.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(label, new Point(x, y));
        }
    }
}
