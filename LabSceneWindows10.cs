using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        private const double WindowsWorkHeight = 476;

        // Original training artwork, not a system resource or a host desktop screenshot.
        private static readonly Geometry WindowsLight = Geometry.Parse("M670,131 L1010,38 L1010,419 L670,324 Z M184,31 L670,131 L670,324 L184,449 Z");
        private static readonly Geometry WindowsPanes = Geometry.Parse("M675,137 L778,118 L778,227 L675,227 Z M789,116 L919,91 L919,227 L789,227 Z M675,237 L778,237 L778,335 L675,316 Z M789,237 L919,237 L919,362 L789,337 Z");
        private static readonly Geometry UbuntuFacetA = Geometry.Parse("M0,143 L410,28 L600,255 L221,520 L0,520Z");
        private static readonly Geometry UbuntuFacetB = Geometry.Parse("M410,28 L1200,28 L955,236 L600,255Z M600,255 L955,236 L1200,520 L221,520Z");

        private void DrawUbuntuGrayWallpaper(DrawingContext dc)
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(32, 32, 39), Color.FromRgb(83, 82, 90), 27), null,
                new Rect(0, 0, WidthUnits, HeightUnits));
            dc.DrawGeometry(Ui.Brush("#393B3F"), null, UbuntuFacetA);
            dc.DrawGeometry(Ui.Brush("#474A4E"), null, UbuntuFacetB);
        }

        private string Windows10StateDescription()
        {
            string overlay = scene.S("desktopOverlay");
            var selection = scene.Items("desktopStartApps").Select(app => app.Value()).ToArray();
            int index = scene.I("desktopOverlaySelection", -1);
            var windows = scene.Items("windows").Where(w => w.S("state") != "closed").ToArray();
            var description = scene.B("showDesktop") ? "Показан рабочий стол" : "Рабочий стол " + (scene.I("workspace") + 1) +
                " из " + scene.I("workspaceCount", 1);
            var open = windows.Where(w => w.I("workspace") == scene.I("workspace")).Select(w =>
                w.S("title", AppTitle(w.S("app"))) + ": " + Windows10AccessibleState(w.S("state")));
            description += ". Окна: " + string.Join("; ", open);
            if (overlay == "start")
                return description + ". Меню Пуск." + (index >= 0 && index < selection.Length ? " Выбрано: " + AppTitle(selection[index]) + "." : "");
            if (overlay == "task-view") return description + ". Представление задач.";
            if (overlay == "snap")
            {
                var candidates = scene.Items("desktopOverlayWindows").Select(id => id.Value()).ToArray();
                var selected = index < 0 || index >= candidates.Length ? null :
                    windows.FirstOrDefault(w => w.S("id") == candidates[index]);
                return description + ". Подбор второго окна." +
                    (selected != null ? " Выбрано: " + selected.S("title", AppTitle(selected.S("app"))) + "." : "");
            }
            return description + ".";
        }

        private static string Windows10AccessibleState(string state)
        {
            switch (state)
            {
                case "snapped-left": case "left": return "слева";
                case "snapped-right": case "right": return "справа";
                case "minimized": case "min": return "свёрнуто";
                case "maximized": case "max": return "развёрнуто";
                case "fullscreen": case "full": return "полный экран";
                default: return "открыто";
            }
        }

        private void DrawSimulatedWindows10(DrawingContext dc)
        {
            double scale = Math.Min(ActualWidth / WidthUnits, ActualHeight / HeightUnits);
            NotebookStartFrame(scale, (ActualWidth - WidthUnits * scale) / 2, (ActualHeight - HeightUnits * scale) / 2);
            dc.PushTransform(new TranslateTransform((ActualWidth - WidthUnits * scale) / 2, (ActualHeight - HeightUnits * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, WidthUnits, HeightUnits), 10, 10));
            var background = new LinearGradientBrush { StartPoint = new Point(0, .1), EndPoint = new Point(1, .75) };
            background.GradientStops.Add(new GradientStop(Color.FromRgb(6, 38, 76), 0));
            background.GradientStops.Add(new GradientStop(Color.FromRgb(6, 75, 135), .35));
            background.GradientStops.Add(new GradientStop(Color.FromRgb(8, 124, 186), .71));
            background.GradientStops.Add(new GradientStop(Color.FromRgb(6, 62, 130), 1));
            var surface = new Rect(0, 0, WidthUnits, HeightUnits);
            dc.DrawRectangle(background, null, surface);
            var glow = new RadialGradientBrush(Color.FromArgb(115, 58, 197, 249), Colors.Transparent);
            glow.Center = glow.GradientOrigin = new Point(.70, .47);
            glow.RadiusX = .38; glow.RadiusY = .52;
            dc.DrawRectangle(glow, null, surface);
            dc.DrawGeometry(Ui.Brush("#244AB3F3"), null, WindowsLight);
            dc.DrawGeometry(Ui.Brush("#642DCEFF"), new Pen(Ui.Brush("#9AF3FF"), 1.4), WindowsPanes);

            Icon("files", 23, 29, 31);
            Text("Проводник", 14, 67, 12, "#F4F9FF", 85);
            var all = scene.Items("windows").Where(w => w.S("state") != "closed").ToArray();
            var current = all.Where(w => w.I("workspace") == scene.I("workspace")).ToArray();
            var visible = current.Where(w => w.S("state") != "minimized" && w.S("state") != "min")
                .OrderBy(w => w.B("active") ? 1 : 0).ToArray();
            if (!scene.B("showDesktop"))
            {
                int index = 0;
                foreach (Data window in ForegroundWindows(visible))
                    DrawWindows10Window(window, Windows10WindowBounds(window, index++));
            }
            NotebookWindow(surface);
            DrawWindows10Taskbar(current);
            string overlay = scene.S("desktopOverlay");
            if (overlay == "start") DrawWindows10Start();
            if (overlay == "task-view" || overlay == "snap") DrawWindows10Tasks(all, overlay);
            if (scene.B("switcher")) DrawSwitcher();
            if (scene.B("screenshot")) DrawScreenshot();
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        private static Rect Windows10WindowBounds(Data window, int index)
        {
            string state = window.S("state");
            if (state == "maximized" || state == "max" || state == "fullscreen" || state == "full")
                return new Rect(0, 0, WidthUnits, WindowsWorkHeight);
            if (state == "snapped-left" || state == "left") return new Rect(0, 0, WidthUnits / 2, WindowsWorkHeight);
            if (state == "snapped-right" || state == "right") return new Rect(WidthUnits / 2, 0, WidthUnits / 2, WindowsWorkHeight);
            int slot = Math.Max(0, Math.Min(1, window.I("normalSlot", index)));
            return new Rect(100 + 172 * slot, 43 + 40 * slot, 825, 374);
        }

        private void DrawWindows10Window(Data window, Rect bounds)
        {
            string app = window.S("app");
            NotebookWindow(bounds);
            context.PushTransform(new TranslateTransform(bounds.X, bounds.Y));
            context.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height)));
            Rectangle(0, 0, bounds.Width, bounds.Height, "#20252B");
            // Register in paint order: top windows block hits on anything they cover.
            AddNotebookHit(new Rect(0, 0, bounds.Width, bounds.Height), ExplorerPrefix + "desktop-focus",
                window.B("active") ? null : "Перейти в «" + window.S("title", AppTitle(app)) + "»", path: window.S("id"));
            if (app == "code" || app == "vscode") DrawCode(window, bounds.Width, bounds.Height);
            else if (app == "chrome")
            {
                DrawChrome(window, bounds.Width, bounds.Height);
                context.DrawRectangle(null, new Pen(Ui.Brush(window.B("active") ? "#42A5E2" : "#798B9F"), 1),
                    new Rect(.5, .5, bounds.Width-1, bounds.Height-1));
                context.Pop(); context.Pop();
                return;
            }
            else if (app == "terminal") DrawTerminal(window, bounds.Width, bounds.Height);
            else DrawFiles(window, bounds.Width, bounds.Height);
            Rectangle(0, 0, bounds.Width, 29, window.B("active") ? "#F4F8FC" : "#DCE4EE");
            if (app == "files") FilesGlyph("folder", 12, 8, true);
            else Icon(app, 10, 5, 18);
            Text(app == "files" ? FilesCaption(window, true) : window.S("title", AppTitle(app)),
                36, 6, 13, "#152438", Math.Max(10, bounds.Width - 170));
            for (int i = 0; i < 3; i++)
            {
                double left = bounds.Width - 108 + i * 36;
                DrawChromeMark(WindowsCaptionIcon(window, i), left + 10, 6, 16, "#152438");
                AddWindowsCaptionHit(window, i, new Rect(left, 0, 36, 29));
            }
            context.DrawRectangle(null, new Pen(Ui.Brush(window.B("active") ? "#42A5E2" : "#798B9F"), 1),
                new Rect(.5, .5, bounds.Width - 1, bounds.Height - 1));
            context.Pop(); context.Pop();
        }

        private static string WindowsCaptionIcon(Data window, int index)
        {
            return index == 0 ? "min" : index == 2 ? "close" : window.S("state") == "maximized" ? "restore" : "max";
        }

        private void AddWindowsCaptionHit(Data window, int index, Rect box)
        {
            var command = index == 0 ? "minimize" : index == 2 ? "close" : "maximize";
            string label = index == 0 ? "Свернуть" : index == 2 ? "Закрыть" :
                window.S("state") == "maximized" ? "Восстановить размер" : "Развернуть";
            AddNotebookHit(box, ExplorerPrefix + "desktop-" + command, label, path: window.S("id"));
        }

        private void DrawWindows10Taskbar(Data[] windows)
        {
            Rectangle(0, WindowsWorkHeight, WidthUnits, HeightUnits - WindowsWorkHeight, "#171F2D");
            if (scene.S("desktopOverlay") == "start") Rectangle(0, WindowsWorkHeight, 50, 44, "#36506D");
            WindowsMark(14, WindowsWorkHeight + 12, 20);
            Text("Пуск", 50, WindowsWorkHeight + 12, 12, "#EFF7FF");
            Text("▣", 111, WindowsWorkHeight + 9, 18, "#EDF7FF");
            string[] apps = scene.Items("desktopStartApps").Select(a => a.Value()).Concat(windows.Select(w => w.S("app")))
                .Distinct(StringComparer.Ordinal).Take(12).ToArray();
            for (int i = 0; i < apps.Length; i++)
            {
                double x = 153 + i * 44;
                var running = windows.Where(w => w.S("app") == apps[i]).ToArray();
                if (running.Any(w => w.B("active") && w.S("state") != "minimized" && !scene.B("showDesktop")))
                    Rectangle(x - 7, WindowsWorkHeight + 2, 40, 40, "#354860");
                Icon(apps[i], x, WindowsWorkHeight + 9, 25);
                AddNotebookHit(new Rect(x-7, WindowsWorkHeight, 40, HeightUnits-WindowsWorkHeight),
                    ExplorerPrefix + "desktop-taskbar", AppTitle(apps[i]), path: apps[i]);
                if (running.Length > 0) Rectangle(x, HeightUnits - 3, 25, 2, "#65C7FF");
            }
            Text(scene.S("layout", "EN") + "     ▰    ›", 1080, WindowsWorkHeight + 11, 13, "#FFFFFF");
            Rectangle(1193, WindowsWorkHeight, 1, 44, "#5E768A");
        }

        private void WindowsMark(double x, double y, double size)
        {
            double part = (size - 2) / 2;
            Rectangle(x, y, part, part, "#A9E6FF");
            Rectangle(x + part + 2, y, part, part, "#A9E6FF");
            Rectangle(x, y + part + 2, part, part, "#A9E6FF");
            Rectangle(x + part + 2, y + part + 2, part, part, "#A9E6FF");
        }

        private void DrawWindows10Start()
        {
            string[] apps = scene.Items("desktopStartApps").Select(a => a.Value()).ToArray();
            Rectangle(0, WindowsWorkHeight - 328, 389, 328, "#1B2636");
            WindowsMark(19, WindowsWorkHeight - 310, 18);
            Text("Пуск", 49, WindowsWorkHeight - 313, 24, "#FFFFFF");
            Text("Приложения", 19, WindowsWorkHeight - 273, 14, "#BBD1EA");
            int selected = scene.I("desktopOverlaySelection", -1);
            for (int i = 0; i < apps.Length && i < 5; i++)
            {
                double y = WindowsWorkHeight - 242 + i * 43;
                if (i == selected) Rectangle(11, y - 3, 365, 39, "#306294");
                Icon(apps[i], 21, y, 29);
                Text(AppTitle(apps[i]), 67, y + 5, 15, "#FFFFFF", 299);
            }
        }

        private void DrawWindows10Tasks(Data[] windows, string overlay)
        {
            bool snap = overlay == "snap";
            if (!snap)
            {
                Rectangle(0, 0, WidthUnits, WindowsWorkHeight, "#DB1A2735");
                Text("Представление задач", 45, 29, 25, "#EFF8FF");
                for (int desk = 0; desk < Math.Min(8, scene.I("workspaceCount", 1)); desk++)
                {
                    double x = 45 + desk * 138;
                    Rounded(x, 73, 127, 51, desk == scene.I("workspace") ? "#2D679F" : "#2E435C", 5);
                    Text("Рабочий стол " + (desk + 1), x + 9, 89, 12, "#EFF8FF", 113);
                }
            }
            else
            {
                var active = windows.LastOrDefault(w => w.B("active"));
                bool onRight = active != null && (active.S("state") == "snapped-right" || active.S("state") == "right");
                Rectangle(onRight ? 0 : WidthUnits / 2, 0, WidthUnits / 2, WindowsWorkHeight, "#E61A2735");
                Text("Выберите второе окно", (onRight ? 0 : WidthUnits / 2) + 33, 27, 22, "#EFF8FF");
            }
            var overlayIds = scene.Items("desktopOverlayWindows").Select(item => item.Value()).ToArray();
            var candidates = (snap ? overlayIds.Select(id => windows.FirstOrDefault(w => w.S("id") == id)).Where(w => w != null)
                : windows.Where(w => w.I("workspace") == scene.I("workspace"))).Take(snap ? 4 : 6).ToArray();
            var current = windows.LastOrDefault(w => w.B("active"));
            bool currentRight = current != null && (current.S("state") == "snapped-right" || current.S("state") == "right");
            int selected = scene.I("desktopOverlaySelection", -1);
            for (int i = 0; i < candidates.Length; i++)
            {
                double x = snap ? (currentRight ? 0 : WidthUnits / 2) + 28 + (i % 2) * 276 : 45 + (i % 3) * 373;
                double y = (snap ? 81 : 151) + (i / (snap ? 2 : 3)) * 135;
                double w = snap ? 257 : 337;
                Rounded(x, y, w, 114, i == selected ? "#3079AC" : "#314760", 7);
                Icon(candidates[i].S("app"), x + 17, y + 18, 28);
                Text(candidates[i].S("title"), x + 55, y + 20, 14, "#F0F8FF", w - 70);
            }
        }
    }
}
