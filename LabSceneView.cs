using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Automation.Peers;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView : FrameworkElement
    {
        private Data scene = Data.Empty;
        private string classApp = "ubuntu";
        private DrawingContext context;
        private readonly Dictionary<string, double> fieldScroll = new Dictionary<string, double>();
        private readonly Typeface regular = LiveUiFont.Typeface(FontWeights.Normal);
        private readonly Typeface mono = new Typeface("Consolas");
        private const double WidthUnits = 1200, HeightUnits = 520;

        protected override AutomationPeer OnCreateAutomationPeer()
        { return new FrameworkElementAutomationPeer(this); }

        internal LabSceneView() { ClipToBounds = true; MinHeight = 180; }
        internal bool WitnessPythonEditor { get; set; }

        private static string QaCodeHash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)))
                    .Replace("-", "").ToLowerInvariant();
        }

        internal string PythonEditorWitness(Data snapshot, Data editor)
        {
            if (!WitnessPythonEditor) return "";
            if (editor == null) return "";
            var tabs = editor.Items("tabs").ToArray();
            var active = tabs.ElementAtOrDefault(Math.Max(0, editor.I("activeTab"))) ?? Data.Empty;
            var disk = snapshot.D("terminal").Items("files").ToDictionary(item => item.S("path"), item => item.S("content"));
            string file, content;
            string tabState = string.Join("|", tabs.Select(item => {
                string title = item.S("title");
                string url = item.S("url");
                string saved;
                var found = disk.TryGetValue(url, out saved);
                if (!found && !url.StartsWith("/", StringComparison.Ordinal))
                    found = disk.TryGetValue("/home/student/project/" + url, out saved);
                return title + "," + QaCodeHash(item.S("body")) + "," + QaCodeHash(item.S("savedBody")) +
                    "," + (found ? QaCodeHash(saved) : "-");
            }));
            file = active.S("title");
            content = QaCodeHash(editor.S("body"));
            return editor.S("focus") + ";" + file + ";" + editor.I("bodyAnchor") + ";" + editor.I("bodyCursor") + ";" +
                content + ";" + QaCodeHash(active.S("savedBody")) + ";" + editor.S("input") + ";" +
                string.Join(",", editor.Items("matches").Select(item => item.Integer().ToString(CultureInfo.InvariantCulture))) +
                ";" + tabState;
        }

        internal void Update(Data next, string app = "ubuntu")
        {
            scene = next;
            classApp = app;
            System.Windows.Automation.AutomationProperties.SetName(this,
                (scene.S("desktopStyle") == "win10" ? "Учебный рабочий стол Windows 10. " + Windows10StateDescription() :
                app == "chrome" ? "Учебный Chrome" : app == "code" ? "Учебный VS Code" : "Учебный рабочий стол Ubuntu") +
                ". Раскладка " + scene.S("layout") + ". " +
                string.Join(". ", scene.Items("windows").Where(w => w.B("active")).Select(w => w.S("title") + ". " + w.S("status"))) +
                (scene.B("gnomeWindows") ? ". Окна: " + string.Join("; ", scene.Items("windows").Select(w =>
                    w.S("app") + "=" + w.S("state") + (w.B("active") ? "*" : ""))) : "") +
                (scene.S("gnomePractice") == "workspaces" ? ". Рабочий стол " + (scene.I("workspace") + 1) +
                    " из " + scene.I("workspaceCount") + ". Размещение: " + string.Join("; ", scene.Items("windows").Select(w =>
                        w.S("app") + "@" + (w.I("workspace") + 1))) : "") +
                (scene.S("gnomePractice") == "same-app" && scene.B("switcher") ? ". Переключатель: " +
                    string.Join("; ", scene.Items("switchChoices").Select(item =>
                        scene.Items("windows").FirstOrDefault(w => w.S("id") == item.Value())).Where(w => w != null)
                        .Select(w => w.S("url").Split('/').Last())) : ""));
            System.Windows.Automation.AutomationProperties.SetHelpText(this, scene.S("terminalInput"));
            var terminal = scene.Items("windows").FirstOrDefault(w => w.B("active") &&
                (w.S("app") == "terminal" || w.S("focus") == "terminal"));
            var editor = scene.Items("windows").FirstOrDefault(w => w.B("active") && w.S("app") == "code");
            System.Windows.Automation.AutomationProperties.SetItemStatus(this, terminal != null ?
                "cursor:" + terminal.I("cursor", -1).ToString(CultureInfo.InvariantCulture) : editor == null ? "" :
                "editor:" + editor.S("focus") + ":" + editor.I("bodyCursor", -1).ToString(CultureInfo.InvariantCulture));
            if (terminal == null && editor != null)
            {
                string focus = editor.S("focus");
                string description = focus == "code-problems" || focus == "code-quick-fix" ?
                    (focus == "code-problems" ? "Проблемы: " : "Быстрые исправления: ") +
                    string.Join("; ", editor.Items("entries").Select(item => item.S("title"))) : focus == "symbol-references" ?
                    "Использования: " + string.Join("; ", editor.Items("entries").Select(item => item.S("title"))) :
                    focus == "project-search" ? "Поиск по проекту: " + editor.S("input") :
                    focus == "project-results" ? "Поиск «" + editor.S("projectSearchQuery") + "»: " +
                    string.Join("; ", editor.Items("entries").Select(item => item.S("title"))) :
                    focus == "symbol-rename" ? "Переименовать символ: " + editor.S("input") :
                    editor.Items("tabs").Skip(Math.Max(0, editor.I("activeTab"))).Select(item => item.S("title")).FirstOrDefault();
                System.Windows.Automation.AutomationProperties.SetHelpText(this, description ?? "");
            }
            if (WitnessPythonEditor)
                System.Windows.Automation.AutomationProperties.SetItemType(this, PythonEditorWitness(scene, editor));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 1 || ActualHeight < 1) return;
            context = dc;
            if (scene.S("desktopStyle") == "win10")
            {
                DrawSimulatedWindows10(dc);
                context = null;
                return;
            }
            if ((classApp == "chrome" || classApp == "code") && !scene.B("overview") && !scene.B("showDesktop") && !scene.B("switcher") &&
                scene.Items("windows").Any(item => item.B("active") && item.S("app") == classApp && item.S("state") != "minimized"))
            {
                DrawImmersive(dc);
                context = null;
                return;
            }
            double scale = Math.Min(ActualWidth / WidthUnits, ActualHeight / HeightUnits);
            dc.PushTransform(new TranslateTransform((ActualWidth - WidthUnits * scale) / 2, (ActualHeight - HeightUnits * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, WidthUnits, HeightUnits), 10, 10));
            DrawUbuntuGrayWallpaper(dc);
            Rectangle(0, 0, 1200, 28, "#151519");
            Text("●  Обзор", 15, 6, 11, "#EEEEF0");
            Text(scene.S("gnomePractice") == "workspaces" ? "Ubuntu  ·  рабочий стол " +
                (scene.I("workspace") + 1) + " из " + scene.I("workspaceCount") : "Ubuntu  ·  учебный рабочий стол",
                469, 6, 11, "#CCCCD4");
            Text(scene.S("layout", "EN").ToLowerInvariant() + "    ◉    ◖)    ▰", 1080, 6, 11, "#E5E5E8");
            var windows = scene.Items("windows").Where(w => w.S("state") != "closed" && w.S("state") != "minimized" &&
                w.I("workspace") == scene.I("workspace")).OrderBy(w => w.B("active") ? 1 : 0).ToArray();
            if (scene.B("showDesktop")) windows = new Data[0];
            else if (scene.B("gnomeWindows"))
            {
                var front = windows.LastOrDefault(w => w.B("active"));
                windows = front != null && (front.S("state") == "maximized" || front.S("state") == "fullscreen")
                    ? new[] { front } : windows.Skip(Math.Max(0, windows.Length - 2)).ToArray();
            }
            else windows = ForegroundWindows(windows);
            int index = 0;
            foreach (var window in windows)
            {
                Rect bounds = Bounds(window, index++, scene.B("gnomeWindows"));
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
            double width = classApp == "chrome" ? Math.Min(900, Math.Max(610, ActualWidth)) : Math.Max(420, ActualWidth);
            double scale = ActualWidth / width;
            double height = ActualHeight / scale;
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 10, 10));
            Rectangle(0, 0, width, height, classApp == "chrome" ? "#F7F6FD" : CodeCanvas);
            var candidates = scene.Items("windows").Where(w =>
                (w.S("app") == classApp || classApp == "code" && w.S("app") == "vscode") &&
                w.S("state") != "closed" && w.S("state") != "minimized").ToArray();
            var window = candidates.FirstOrDefault(w => w.B("active")) ?? candidates.FirstOrDefault();
            if (window != null)
            {
                DrawWindow(window, new Rect(0, 0, width, height));
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

        private static Rect Bounds(Data window, int index, bool stableNormal = false)
        {
            string state = window.S("state");
            if (state == "fullscreen" || state == "full") return new Rect(0, 28, 1200, 492);
            if (state == "maximized" || state == "max") return new Rect(57, 34, 1135, 477);
            if (state == "left" || state == "snapped-left") return new Rect(61, 36, 565, 472);
            if (state == "right" || state == "snapped-right") return new Rect(627, 36, 565, 472);
            if (stableNormal)
            {
                int slot = Math.Max(0, Math.Min(1, window.I("normalSlot")));
                return new Rect(61 + 1130 * .28 * (.15 + .7 * slot), 36 + 472 * .2 * (.24 + .52 * slot),
                    1130 * .72, 472 * .8);
            }
            return window.B("active") ? new Rect(112, 50, 1035, 438) : new Rect(79 + index * 18, 40 + index * 12, 980, 430);
        }

        private void DrawDock()
        {
            Rounded(6, 122, 49, 278, "#E819171F", 13);
            string[] apps = scene.Items("dock").Select(item => item.Value()).ToArray();
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
            if (app == "code" || app == "vscode")
            {
                DrawCodeTitle(window, bounds.Width);
                DrawCode(window, bounds.Width, bounds.Height);
                context.Pop(); context.Pop();
                return;
            }
            Rectangle(0, 0, bounds.Width, 29, app == "chrome" ? "#24213B" : window.B("active") ? "#303034" : "#28282C");
            if (app == "chrome") Icon("chrome", 13, 5, 19);
            Text(window.S("title", AppTitle(app)), app == "chrome" ? 38 : 14, 7, app == "chrome" ? 14 : 11, "#F6F1FF");
            Text("—       □", bounds.Width - 89, 5, 12, "#C6C6CE");
            Rounded(bounds.Width - 26, 5, 18, 18, "#D75636", 9);
            Text("×", bounds.Width - 22, 3, 15, "#FFFFFF");
            double w = bounds.Width, h = bounds.Height;
            if (app == "chrome") DrawChrome(window, w, h);
            else if (app == "terminal") DrawTerminal(window, w, h);
            else DrawFiles(window, w, h);
            context.Pop(); context.Pop();
        }

        private void DrawChrome(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#F7F6FD");
            Rectangle(0, 29, w, 35, "#1D1A35");
            var tabs = window.Items("tabs").ToArray();
            double tabWidth = Math.Min(200, (w - 65) / Math.Max(1, tabs.Length));
            for (int i = 0; i < tabs.Length; i++)
            {
                Rounded(7 + i * tabWidth, 35, tabWidth - 4, 29,
                    i == window.I("activeTab") ? "#3A345C" : "#26223E", 7);
                Text("◉", 16 + i * tabWidth, 42, 10, "#80BCDF");
                Text(tabs[i].S("title", "Новая вкладка"), 33 + i * tabWidth, 42, 11, "#E7EAF1", tabWidth - 53);
                Text("×", tabWidth - 15 + i * tabWidth, 40, 12, "#A4ACBC");
            }
            Text("+", 17 + tabs.Length * tabWidth, 38, 20, "#C2C9D6");
            Rectangle(0, 64, w, 38, "#3A345C");
            Text("‹   ›   ⟳", 13, 71, 17, "#CDD5E2");
            Rounded(95, 70, w - 142, 26, window.S("focus") == "url" ? "#D8E9FD" : "#242039", 13);
            Field(window.S("focus") == "url" ? window.S("input") : window.S("url"), window, 109, 76, w - 180, 12,
                window.S("focus") == "url" ? "#1F3E5F" : "#D4DCEB", 0, window.S("focus") == "url",
                scrollKey: "address:" + window.S("id"), face: regular);
            Text("⋮", w - 24, 68, 21, "#D6DDE9");
            double left = Math.Max(28, w * .065);
            double pageScale = window.N("zoom", 100) / 100;
            string body = window.S("body");
            var active = tabs.ElementAtOrDefault(window.I("activeTab"));
            if (body.Length == 0 && active != null) body = active.S("body");
            Text(active == null ? "Рабочие материалы" : active.S("title", "Рабочие материалы"), left, 124, 30 * pageScale, "#211B37", w - left * 2);
            int offset = 0;
            double lineY = 172;
            foreach (string line in body.Split('\n'))
            {
                Field(line, window, left, lineY, w - left * 2, 14 * pageScale, "#302745", offset,
                    window.I("bodyAnchor") != window.I("bodyCursor"), true, face: regular);
                lineY += 22 * pageScale; offset += line.Length + 1;
            }
            lineY += 15;
            int index = 0;
            foreach (var entry in window.Items("entries"))
            {
                if (lineY + 22 > h - 24) break;
                if (index == window.I("item", -1) && window.S("focus") == "body")
                    Rounded(left - 7, lineY - 4, Math.Min(440, w - left * 2), 28, "#D8E8FF", 5);
                Text(entry.S("title"), left, lineY, 14 * pageScale, "#205496", w - left * 2);
                lineY += 33 * pageScale; index++;
            }
            if (window.B("bookmarks"))
            {
                Rectangle(0, 101, w, 25, "#E5EAF0");
                var bookmarks = window.Items("savedBookmarks").Select(item => item.Value())
                    .Where(url => !string.IsNullOrWhiteSpace(url)).OrderBy(url => url, StringComparer.Ordinal)
                    .Select(url => BrowserBookmarkTitle(url, tabs, window.Items("entries"))).ToArray();
                Text(bookmarks.Length == 0 ? "Нет сохранённых закладок" : string.Join("    ", bookmarks.Select(label => "☆  " + label)),
                    15, 107, 10, "#43566D", w - 30);
            }
            if (window.S("find").Length > 0 || window.S("focus") == "find")
            {
                Rounded(w - 337, 104, 323, 41, "#343A45", 5);
                bool editingFind = window.S("focus") == "find";
                Text("⌕", w - 321, 115, 14, "#CFD7E8", 23);
                Field(editingFind ? window.S("input") : window.S("find"), window, w - 297, 116, 211,
                    12, "#FFFFFF", 0, editingFind && window.B("active"), scrollKey: "find:" + window.S("id"), face: regular);
                Text("↑  ↓  ×", w - 79, 115, 13, "#CFD7E8");
            }
            if (window.S("status").Length > 0)
            {
                Rectangle(0, h - 23, w, 23, "#ECF0F6");
                Text(window.S("status"), 12, h - 18, 10, "#4B607B", w - 30);
            }
        }

        private static string BrowserBookmarkTitle(string url, IEnumerable<Data> tabs, IEnumerable<Data> entries)
        {
            var page = tabs.FirstOrDefault(tab => tab.S("url") == url);
            if (page != null && page.S("title").Length > 0) return page.S("title");
            var entry = entries.FirstOrDefault(item => item.S("path") == url);
            if (entry != null && entry.S("title").Length > 0) return entry.S("title");
            Uri parsed;
            if (Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Host.Length > 0)
            {
                string path = parsed.AbsolutePath.Trim('/');
                return path.Length > 0 ? parsed.Host + "/" + path : parsed.Host;
            }
            return url;
        }

        private void DrawCode(Data window, double w, double h)
        {
            const double tabsTop = 29, tabsHeight = 35, breadcrumbY = 70, firstRowY = 92, rowPitch = 20;
            const double activityWidth = 48;
            Rectangle(0, tabsTop, w, h - tabsTop, CodeCanvas);
            bool sidebar = window.B("sidebar", true);
            double x = sidebar ? activityWidth + Math.Min(226, Math.Max(116, w * .20)) : activityWidth;
            DrawCodeActivity(window, activityWidth, h);
            if (sidebar) DrawCodeSidebar(window, activityWidth, x - activityWidth, h);

            var tabs = window.Items("tabs").ToArray();
            int active = Math.Min(Math.Max(0, window.I("activeTab")), Math.Max(0, tabs.Length - 1));
            string relative = CodeRelativePath(window);
            string file = relative.Split('/').Last();
            Rectangle(x, tabsTop, w - x, tabsHeight, CodeInset);
            Rectangle(x, tabsTop + tabsHeight - 1, w - x, 1, CodeBorder);
            double tabX = x;
            for (int i = 0; i < tabs.Length && tabX < w - 18; i++)
            {
                Data tab = tabs[i];
                string title = tab.S("title", file);
                double tabWidth = Math.Min(w - tabX, Math.Max(103, Math.Min(177, 48 + title.Length * 7)));
                bool selected = i == active;
                Rectangle(tabX, tabsTop, tabWidth, tabsHeight - (selected ? 0 : 1), selected ? CodeCanvas : CodeInset);
                Rectangle(tabX + tabWidth - 1, tabsTop, 1, tabsHeight, CodeBorder);
                if (selected) Rectangle(tabX, tabsTop, tabWidth - 1, 2, CodeAccent);
                DrawCodeFileIcon(title, tabX + 11, tabsTop + 9);
                Text(title, tabX + 31, tabsTop + 8, 13, selected ? CodeForeground : CodeMuted, tabWidth - 50);
                if (selected && tabWidth > 67)
                    Text(tab.B("modified") ? "●" : "×", tabX + tabWidth - 19, tabsTop + 7, 13, CodeMuted, 14);
                tabX += tabWidth;
            }
            string path = string.Join("  ›  ", relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            Text(path, x + 16, breadcrumbY - 3, 12, CodeMuted, w - x - 24);

            string code = window.S("body");
            if (window.S("focus") == "editor" && window.S("input").Length > 0) code = window.S("input");
            string[] lines = code.Split('\n');
            double editorWidth = window.B("split") ? (w - x) / 2 : w - x;
            double primary = x + (window.B("split") && window.I("group") == 1 ? editorWidth : 0);
            int caret = Math.Max(0, Math.Min(window.I("bodyCursor"), code.Length));
            int anchor = Math.Max(0, Math.Min(window.I("bodyAnchor"), code.Length));
            int caretLine = code.Substring(0, caret).Count(c => c == '\n');
            int caretColumn = caret - (caret == 0 ? -1 : code.LastIndexOf('\n', caret - 1));
            bool bodyFocused = window.B("active") && window.S("focus") == "body";
            double bottom = window.B("panel") ? Math.Max(155, h * .55) : h - 22;
            int visible = Math.Max(1, (int)((bottom - firstRowY) / rowPitch));
            int first = bodyFocused ? Math.Max(0, caretLine - visible + 1) : 0;
            double gutter = Math.Max(49, 21 + lines.Length.ToString(CultureInfo.InvariantCulture).Length * 8);
            int offset = 0;
            var syntax = window.Items("syntax").ToArray();
            for (int i = 0; i < Math.Min(lines.Length, first + visible); i++)
            {
                if (i >= first)
                {
                    double row = firstRowY + (i - first) * rowPitch;
                    if (bodyFocused && i == caretLine) Rectangle(primary, row - 1, editorWidth, rowPitch, CodeActiveLine);
                    CodeRightText((i + 1).ToString(CultureInfo.InvariantCulture),
                        primary + 5, row, gutter - 19, 13, bodyFocused && i == caretLine ? CodeForeground : CodeGutter);
                    bool hasLineBreak = i + 1 < lines.Length;
                    string line = hasLineBreak && lines[i].EndsWith("\r", StringComparison.Ordinal)
                        ? lines[i].Substring(0, lines[i].Length - 1) : lines[i];
                    DrawStyledCodeLine(line, window, syntax, primary + gutter, row, editorWidth - gutter - 10,
                        offset, caret, anchor, bodyFocused, hasLineBreak);
                }
                offset += lines[i].Length + 1;
            }
            if (window.B("panel"))
            {
                double terminalY = Math.Max(155, h * .55);
                Rectangle(x, terminalY, w - x, h - terminalY - 21, CodeInset);
                Rectangle(x, terminalY, w - x, 1, CodeBorder);
                if (scene.B("diagnosticWorkspace") && window.S("focus") != "terminal")
                {
                    Text("ПРОБЛЕМЫ · F8", x + 15, terminalY + 8, 10, "#F3CC73");
                    if (window.S("focus") != "code-quick-fix")
                        Text(window.Items("entries").Where(item => item.S("kind") == "problem")
                            .Select(item => item.S("title")).FirstOrDefault() ?? "Нет диагностик",
                            x + 15, terminalY + 30, 12, "#D1DCEB", w - x - 30);
                }
                else
                {
                    Text("TERMINAL      OUTPUT      PROBLEMS", x + 15, terminalY + 8, 10, "#AAB0BD");
                    string output = string.Join("\n", scene.D("terminal").Items("output").Select(line => line.Value()).Reverse().Take(4).Reverse());
                    Text(output, x + 15, terminalY + 30, 11, "#B7C5D4", w - x - 30, 4, true);
                    TerminalField(window, x + 15, h - 47, w - x - 30);
                }
            }
            if (window.B("split"))
            {
                double secondary = primary == x ? x + editorWidth : x;
                Rectangle(x + editorWidth, 60, 1, Math.Max(1, (window.B("panel") ? h * .55 : h - 21) - 60), "#414854");
                Data other = window.D("otherTab");
                Text(other.S("title"), secondary + 14, 73, 11, "#BFD0DF", editorWidth - 28);
                Text(other.S("body"), secondary + 18, 94, 12, "#C7D9E7", editorWidth - 32, visible, true);
            }
            DrawCodeStatus(window, w, h, caretLine + 1, caretColumn, file);
            string focus = window.S("focus");
            if (focus == "code-quick-fix")
            {
                var options = window.Items("entries").Select(item => item.S("title")).ToArray();
                double pw = Math.Min(500, w - 24), px = (w - pw) / 2, py = 66;
                Rounded(px, py, pw, 110, "#292B30", 7);
                Text("Быстрые исправления", px + 12, py + 8, 14, "#EEF1F8", pw - 24);
                for (int i = 0; i < options.Length; i++)
                {
                    double row = py + 32 + i * 23;
                    if (i == window.I("optionIndex")) Rounded(px + 5, row - 2, pw - 10, 22, "#234D72", 4);
                    Text(options[i], px + 12, row, 12, "#EEF1F8", pw - 24);
                }
                Text("↑↓ — выбрать   Enter — применить   Esc — закрыть", px + 12, py + 84, 10, "#B4BFCD", pw - 24);
            }
            else if (focus == "symbol-references" || focus == "symbol-rename" || focus == "project-results")
            {
                bool searchResults = focus == "project-results";
                bool references = focus == "symbol-references" || searchResults;
                var choices = references ? window.Items("entries").Select(item => item.S("title")).ToArray() : new string[0];
                int index = Math.Min(Math.Max(0, window.I("optionIndex")), Math.Max(0, choices.Length - 1));
                int shown = Math.Min(6, Math.Min(choices.Length, Math.Max(1, (int)((h - 135) / 25))));
                int start = Math.Max(0, index - shown + 1);
                double pw = Math.Min(650, w - 25), px = (w - pw) / 2, py = 66;
                Rounded(px - 1, py + 2, pw + 2, 80 + shown * 25, "#121419", 8);
                Rounded(px, py, pw, 80 + shown * 25, "#292B30", 8);
                Text(searchResults ? "Поиск «" + window.S("projectSearchQuery") + "» · " + choices.Length :
                    references ? "Использования · " + choices.Length : "Переименовать символ",
                    px + 13, py + 8, 14, "#EEF1F8", pw - 26);
                if (references)
                {
                    if (choices.Length == 0) Text(searchResults ? "Совпадений нет" : "Использования не найдены", px + 13, py + 34, 12, "#B4BFCD", pw - 26);
                    for (int i = start; i < Math.Min(start + shown, choices.Length); i++)
                    {
                        double row = py + 35 + (i - start) * 25;
                        if (i == index) Rounded(px + 6, row - 2, pw - 12, 23, "#234D72", 4);
                        Text(choices[i], px + 13, row, 13, "#EEF1F8", pw - 26);
                    }
                }
                else
                {
                    Rounded(px + 7, py + 29, pw - 14, 24, "#1A1D23", 4);
                    Field(window.S("input"), window, px + 13, py + 32, pw - 26, 14, "#EEF1F8", 0, true);
                }
                Text(references ? "↑↓ — выбрать   Enter — открыть   Esc — закрыть" : "Enter — переименовать   Esc — отменить",
                    px + 13, py + 57 + shown * 25, 11, "#B4BFCD", pw - 26);
            }
            else if (new[] { "palette", "quick-open", "goto-line", "symbols", "find", "project-search", "replace-find", "replace-with" }.Contains(focus))
            {
                string title = focus == "palette" ? "Палитра команд" : focus == "symbols" ? "Символы файла" :
                    focus == "replace-find" ? "Найти для замены" : focus == "replace-with" ? "Заменить на" :
                    focus == "quick-open" ? "Открыть файл" : focus == "goto-line" ? "Перейти к строке" : "Поиск";
                if (focus == "find" && window.S("status").StartsWith("Совпадение ", StringComparison.Ordinal))
                    title += " · " + window.S("status");
                string[] options = focus == "palette" ? new[] { "Форматировать документ", "Переключить боковую панель", "Разделить редактор" } :
                    window.Items("entries").Select(item => item.S("title")).ToArray();
                int index = window.I("optionIndex"), shown = Math.Min(options.Length, Math.Min(6, Math.Max(1, (int)((h - 115) / 25))));
                int start = Math.Max(0, index - shown + 1);
                double pw = Math.Min(570, w - 105), px = (w - pw) / 2;
                Rounded(px, 38, pw, 60 + shown * 25, "#292B30", 6);
                Text(title, px + 13, 45, 10, "#C0C4CD", pw - 26);
                Field(window.S("input"), window, px + 13, 64, pw - 26, 14, "#FFFFFF", 0, true);
                for (int i = start; i < Math.Min(start + shown, options.Length); i++)
                {
                    double row = 94 + (i - start) * 25;
                    if (i == index) Rounded(px + 5, row - 3, pw - 10, 24, "#234D72", 3);
                    Text(options[i], px + 15, row, 13, "#EEEEF3", pw - 30);
                }
            }
        }

        private void DrawTerminal(Data window, double w, double h)
        {
            Rectangle(0, 29, w, h - 29, "#2B1028");
            string output = string.Join("\n", scene.D("terminal").Items("output").Select(line => line.Value()).Reverse().Take(13).Reverse());
            Text(output, 16, 45, 13, "#E4DCE6", w - 35, 14, true);
            string input = scene.S("terminalInput");
            string cwd = scene.D("terminal").S("cwd", "~");
            double y = Math.Min(h - 45, 72 + Math.Min(11, output.Split('\n').Length) * 20);
            TerminalField(window, 16, y, w - 32);
            if (window.S("status").Length > 0) Text(window.S("status"), 16, h - 25, 11, "#C9B3C5", w - 35);
        }

        internal string CapturePng(bool activeOnly = false)
        {
            if (ActualWidth < 1 || ActualHeight < 1) return "";
            if (activeOnly)
            {
                var terminal = scene.Items("windows").FirstOrDefault(w => w.B("active") && w.S("app") == "terminal" &&
                    w.S("state") != "minimized");
                if (terminal == null) return "";
                Rect bounds = Bounds(terminal, 1);
                double scaleWindow = Math.Min(1, 500 / bounds.Width);
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(scaleWindow, scaleWindow));
                    var original = context;
                    try { context = dc; DrawWindow(terminal, new Rect(0, 0, bounds.Width, bounds.Height)); }
                    finally { context = original; }
                    dc.Pop();
                }
                var windowImage = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * scaleWindow),
                    (int)Math.Ceiling(bounds.Height * scaleWindow), 96, 96, PixelFormats.Pbgra32);
                windowImage.Render(drawing);
                return EncodePng(windowImage);
            }
            double scale = Math.Min(1, 500 / ActualWidth);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(this);
            return EncodePng(bitmap);
        }

        private static string EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private void DrawFiles(Data window, double w, double h)
        {
            if (window.S("focus") == "image-preview" && window.S("imagePreview").StartsWith("data:image/png;base64,"))
            {
                var bytes = Convert.FromBase64String(window.S("imagePreview").Substring("data:image/png;base64,".Length));
                using (var stream = new MemoryStream(bytes))
                {
                    var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    double scale = Math.Min((w - 20) / bitmap.PixelWidth, (h - 48) / bitmap.PixelHeight);
                    double iw = bitmap.PixelWidth * scale, ih = bitmap.PixelHeight * scale;
                    context.DrawImage(bitmap, new Rect((w - iw) / 2, (h + 29 - ih) / 2, iw, ih));
                }
                return;
            }
            Rectangle(0, 29, w, h - 29, "#F7F7F8");
            Rectangle(0, 29, 145, h - 29, "#E7E7EA");
            Text("Недавние\nИзбранное\nДомашняя папка\nДокументы\nЗагрузки\nКорзина", 14, 57, 12, "#494B54", 126, 10);
            Text(window.S("folder").Replace("/home/student", "Домашняя папка").Replace("/", "  ›  "), 164, 48, 13, "#424550", w - 179);
            var entries = window.Items("entries").ToArray();
            int visible = Math.Max(1, (int)((h - 90) / 35));
            int first = Math.Max(0, window.I("item", -1) - visible + 1);
            for (int i = first; i < Math.Min(first + visible, entries.Length); i++)
            {
                double y = 87 + (i - first) * 35;
                if (i == window.I("item", -1)) Rounded(154, y - 4, w - 166, 31, "#F1D9C9", 5);
                Rounded(166, y + 3, 22, 16, entries[i].S("kind") == "folder" ? "#D99A56" : "#9DADBD", 3);
                Text(entries[i].S("title"), 200, y, 14, "#37302C", w - 212);
            }
            if (entries.Length == 0) Text("Папка пуста", 166, 90, 14, "#817872");
        }

        private void DrawOverview()
        {
            Rectangle(0, 28, 1200, 492, "#EF17202B");
            Rounded(360, 51, 480, 38, "#3C4455", 18);
            Text(scene.S("search").Length == 0 ? "Поиск приложений" : scene.S("search"), 389, 61, 14, "#BEC9DA");
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
            var candidates = scene.Items("windows").Where(w => w.I("workspace") == scene.I("workspace")).ToArray();
            var ids = scene.Items("switchChoices").Select(item => item.Value()).ToArray();
            var windows = ids.Length == 0 ? candidates.Take(4).ToArray() :
                ids.Select(id => candidates.FirstOrDefault(window => window.S("id") == id))
                    .Where(window => window != null).Take(4).ToArray();
            for (int i = 0; i < windows.Length; i++)
            {
                double x = 354 + i * 124;
                if (windows[i].S("id") == scene.S("switchTarget")) Rounded(x, 200, 111, 111, "#4F485E", 9);
                Icon(windows[i].S("app"), x + 35, 216, 41);
                string label = ids.Length > 0 && scene.S("gnomePractice") == "same-app"
                    ? windows[i].S("url", AppTitle(windows[i].S("app"))).Split('/').Last() : AppTitle(windows[i].S("app"));
                Text(label, x + 9, 280, 11, "#EAE5F0", 101);
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
        private void TerminalField(Data window, double x, double y, double width)
        {
            string prompt = "student@ubuntu:" + scene.D("terminal").S("cwd", "~").Replace("/home/student", "~") + "$ ";
            Field(prompt + scene.S("terminalInput"), window, x, y, width, 13, "#E9EFF6", -prompt.Length,
                window.B("active") && (window.S("app") == "terminal" || window.S("focus") == "terminal"),
                scrollKey: "terminal:" + window.S("id"));
        }

        private void Field(string value, Data window, double x, double y, double width, double size, string ink,
            int offset, bool focused, bool document = false, string scrollKey = null, Typeface face = null, bool hasLineBreak = false)
        {
            if (width <= 0) return;
            if (face != null)
            {
                ProportionalField(value, window, x, y, width, size, ink, offset, focused, document, scrollKey, face);
                return;
            }
            var typeface = mono;
            var line = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                size, Ui.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            int caret = window.I(document ? "bodyCursor" : "cursor") - offset;
            int anchor = window.I(document ? "bodyAnchor" : "anchor") - offset;
            int start = Math.Max(0, Math.Min(value.Length, Math.Min(caret, anchor)));
            int end = Math.Max(0, Math.Min(value.Length, Math.Max(caret, anchor)));
            bool drawCaret = focused && caret >= 0 && caret <= value.Length;
            double cx = 0;
            if (drawCaret && caret > 0)
            {
                var prefix = new FormattedText(value.Substring(0, caret), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, size, Ui.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                cx = prefix.WidthIncludingTrailingWhitespace;
            }
            context.PushClip(new RectangleGeometry(new Rect(x, y, width, size * 1.7)));
            bool selectLineBreak = focused && hasLineBreak &&
                Math.Min(caret, anchor) <= value.Length && Math.Max(caret, anchor) > value.Length;
            double lineBreakWidth = Math.Ceiling(size * .6);
            double caretMargin = selectLineBreak && caret == value.Length ? lineBreakWidth + 1 : 3;
            double shift = Math.Max(0, cx - width + caretMargin);
            if (drawCaret && scrollKey != null)
            {
                double prior;
                if (fieldScroll.TryGetValue(scrollKey, out prior))
                    shift = Math.Max(shift, Math.Min(prior, Math.Max(0, cx - 3)));
                else if (fieldScroll.Count >= 32)
                    fieldScroll.Remove(fieldScroll.Keys.First());
                fieldScroll[scrollKey] = shift;
            }
            x -= shift;
            var origin = new Point(x, y);
            if (focused && document)
            {
                foreach (Data match in window.Items("matches"))
                {
                    int mark = match.Integer() - offset, length = Math.Abs(anchor - caret);
                    int lo = Math.Max(0, mark), hi = Math.Min(value.Length, mark + length);
                    if (hi > lo) context.DrawGeometry(Ui.Brush("#385677"), null, line.BuildHighlightGeometry(origin, lo, hi - lo));
                }
            }
            if (focused && end > start)
            {
                context.DrawGeometry(Ui.Brush("#35629B"), null, line.BuildHighlightGeometry(origin, start, end - start));
                line.SetForegroundBrush(Brushes.White, start, end - start);
            }
            // A newline owns no glyph in this per-line renderer. Give the
            // selected break a cell immediately after the last visible glyph
            // (including trailing spaces), on this row only.
            if (selectLineBreak)
                Rectangle(origin.X + line.WidthIncludingTrailingWhitespace, y,
                    lineBreakWidth, Math.Max(size, line.Height), "#35629B");
            context.DrawText(line, origin);
            if (drawCaret) Rectangle(x + cx, y + 1, 1.5, size * 1.3, ink);
            context.Pop();
        }

        private void ProportionalField(string value, Data window, double x, double y, double width, double size, string ink,
            int offset, bool focused, bool document, string scrollKey, Typeface face)
        {
            int caret = window.I(document ? "bodyCursor" : "cursor") - offset;
            int anchor = window.I(document ? "bodyAnchor" : "anchor") - offset;
            bool drawCaret = focused && caret >= 0 && caret <= value.Length;
            using (var shaped = new ChromeTextLine(value, face, size, Ui.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip))
            {
                double cx = drawCaret ? shaped.Distance(caret) : 0;
                double prior = 0;
                if (drawCaret && scrollKey != null) fieldScroll.TryGetValue(scrollKey, out prior);
                double shift = ChromeTextLine.VisibleShift(cx, width, prior);
                if (drawCaret && scrollKey != null)
                {
                    if (!fieldScroll.ContainsKey(scrollKey) && fieldScroll.Count >= 32)
                        fieldScroll.Remove(fieldScroll.Keys.First());
                    fieldScroll[scrollKey] = shift;
                }
                var origin = new Point(x - shift, y);
                context.PushClip(new RectangleGeometry(new Rect(x, y, width, size * 1.7)));
                try
                {
                    // Keep a single foreground run so selecting text never changes the shaping.
                    // Use a light highlight behind dark address/document text and retain the existing
                    // dark highlight behind the white Find text.
                    var selection = Ui.Brush(ink == "#FFFFFF" ? "#35629B" : "#ADC8F0");
                    if (focused && document)
                    {
                        int length = Math.Abs(anchor - caret);
                        foreach (Data match in window.Items("matches"))
                        {
                            int mark = match.Integer() - offset;
                            DrawProportionalBounds(shaped, origin, mark, mark + length, Ui.Brush("#C6D8F1"));
                        }
                    }
                    if (focused && caret != anchor)
                        DrawProportionalBounds(shaped, origin, Math.Min(caret, anchor), Math.Max(caret, anchor), selection);
                    shaped.Draw(context, origin);
                    if (drawCaret) Rectangle(origin.X + cx, y + 1, 1.5, size * 1.3, ink);
                }
                finally { context.Pop(); }
            }
        }

        private void DrawProportionalBounds(ChromeTextLine shaped, Point origin, int first, int end, Brush brush)
        {
            foreach (Rect bound in shaped.Bounds(first, end))
            {
                Rect shifted = bound;
                shifted.Offset(origin.X, origin.Y);
                context.DrawRectangle(brush, null, shifted);
            }
        }

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
