using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace TouchTypingStudio
{
    internal sealed partial class LabSceneView
    {
        private const string FilesHome = "/home/student";

        private static bool HasFilesSearch(Data window)
        {
            string focus = window.S("focus");
            return window.S("app") == "files" && (focus == "file-search" || focus == "file-results");
        }

        private static string FilesSearchDescription(Data window)
        {
            bool editing = window.S("focus") == "file-search";
            var entries = window.Items("entries").ToArray();
            int index = window.I("item", -1);
            string scope = window.S("windowsPath");
            if (scope.Length == 0) scope = window.S("folder");
            return (editing ? "Поле поиска" : "Результаты поиска") + " в «" + scope + "». Запрос «" +
                window.S(editing ? "input" : "query") + "». Найдено: " + entries.Length + "." +
                (index >= 0 && index < entries.Length ? " Выбран «" + entries[index].S("title") + "»." : "") +
                (entries.Length > 0 ? " Первые результаты: " + string.Join("; ", entries.Take(5)
                    .Select(entry => entry.S("title") + " («" + entry.S("path") + "»)")) + "." : "");
        }

        private static string FilesLocationDescription(Data window)
        {
            string folder = window.S("windowsPath");
            if (folder.Length == 0) folder = window.S("folder", FilesHome);
            var selected = window.Items("entries").ElementAtOrDefault(Math.Max(-1, window.I("item", -1)));
            return "Папка «" + folder + "»." + (selected == null ? "" : " Выбран объект «" + selected.S("title") + "».");
        }

        private static string FilesCaption(Data window, bool windows)
        {
            if (window.S("focus") == "image-preview") return windows ? "Просмотр фотографий" : "Просмотр изображения";
            string folder = window.S("folder", FilesHome);
            if (folder == FilesHome) return windows ? "Student" : "Домашняя папка";
            if (folder == "/") return windows ? "Учебный диск" : "Файловая система";
            return folder.Split('/').Last();
        }

        private void DrawFilesCaption(Data window, double width)
        {
            var label = new FormattedText(FilesCaption(window, false), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, LiveUiFont.Typeface(FontWeights.Bold), 12, Ui.Brush("#252525"),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double captionWidth = Math.Max(1, width - 170);
            label.MaxTextWidth = captionWidth;
            label.MaxLineCount = 1;
            label.Trimming = TextTrimming.CharacterEllipsis;
            label.TextAlignment = TextAlignment.Center;
            context.DrawText(label, new Point((width - captionWidth) / 2, 7));
        }

        private void DrawFiles(Data window, double width, double height)
        {
            const double top = 29;
            if (window.S("focus") == "image-preview" && window.S("imagePreview").StartsWith("data:image/png;base64,"))
            {
                Rectangle(0, top, width, height - top, "#242427");
                var bytes = Convert.FromBase64String(window.S("imagePreview").Substring("data:image/png;base64,".Length));
                using (var stream = new MemoryStream(bytes))
                {
                    var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    double scale = Math.Max(0, Math.Min((width - 20) / bitmap.PixelWidth, (height - 48) / bitmap.PixelHeight));
                    double iw = bitmap.PixelWidth * scale, ih = bitmap.PixelHeight * scale;
                    context.DrawImage(bitmap, new Rect((width - iw) / 2, (height + top - ih) / 2, iw, ih));
                }
                return;
            }

            bool windows = scene.S("desktopStyle") == "win10";
            const string home = FilesHome;
            string folder = window.S("folder", home);
            string leaf = FilesCaption(window, windows);
            string focus = window.S("focus");
            bool search = focus == "file-search" || focus == "file-results";
            string query = window.S(focus == "file-search" ? "input" : "query");
            bool inlineSearch = windows && width > 650;
            string ink = windows ? "#222222" : "#252525";
            string muted = windows ? "#5B6470" : "#5D5C59";
            string edge = windows ? "#DEDFE2" : "#DDD9D5";
            string highlight = windows ? "#CCE8FF" : "#F4E0D4";
            double bar = windows ? top + 27 : top;
            double searchTop = bar + (windows ? 39 : 46);
            double listTop = searchTop + (search && !inlineSearch ? 37 : 0);
            double side = Math.Min(182, width * .29);
            double addressX = windows ? 82 : 81;
            double addressRight = width - (inlineSearch ? Math.Max(184, Math.Min(229, width * .27)) : 12);
            var entries = window.Items("entries").ToArray();
            int selected = window.I("item", -1);

            Rectangle(0, top, width, height - top, "#FFFFFF");
            Rectangle(0, top, width, listTop - top, windows ? "#FCFCFC" : "#F5F3F1");
            if (windows)
            {
                Rectangle(0, top, 56, 25, "#0067B8");
                Text("Файл", 13, top + 4, 12, "#FFFFFF");
                Text("Главная", 69, top + 4, 12, ink);
                Text("Поделиться", 139, top + 4, 12, ink);
                Text("Вид", 232, top + 4, 12, ink);
                Rectangle(56, top + 25, width - 56, 1, edge);
            }
            FilesNav("←", 10, bar + 3, window.B("folderCanBack"), ink);
            FilesNav("→", 34, bar + 3, window.B("folderCanForward"), ink);
            FilesNav(windows ? "↑" : "⌃", 58, bar + 3, folder != "/", ink);

            Rectangle(addressX, bar + 6, addressRight - addressX, 28, edge);
            Rectangle(addressX + 1, bar + 7, addressRight - addressX - 2, 26, "#FFFFFF");
            FilesGlyph(windows ? "folder" : "home", addressX + 7, bar + 13, windows);
            string location = windows
                ? window.S("windowsPath", folder).Replace(@"\", "   ›   ")
                : folder == "/" ? "Файловая система"
                : folder == home || folder.StartsWith(home + "/", StringComparison.Ordinal)
                    ? "Домашняя папка" + folder.Substring(home.Length).Replace("/", "   ›   ")
                    : "Файловая система   ›   " + folder.Trim('/').Replace("/", "   ›   ");
            Text(location, addressX + 27, bar + 11, 12, ink, addressRight - addressX - 35);
            if (inlineSearch || search)
            {
                bool editing = focus == "file-search";
                double x = inlineSearch ? addressRight + 8 : 12, y = inlineSearch ? bar + 6 : searchTop + 1;
                double fieldWidth = width - x - 12, fieldHeight = inlineSearch ? 28 : 29;
                Rounded(x, y, fieldWidth, fieldHeight, editing ? (windows ? "#0067B8" : "#A34F23") : edge, windows ? 0 : 5);
                Rounded(x + 1, y + 1, fieldWidth - 2, fieldHeight - 2, "#FFFFFF", windows ? 0 : 4);
                if (search)
                {
                    int prefix = inlineSearch ? 57 : 121;
                    Text(inlineSearch ? "Поиск" : "Поиск по имени", x + 10, y + 6, 12, muted, prefix - 14);
                    Field(query, window, x + prefix, y + 5, fieldWidth - prefix - 10, 13, ink, 0, editing, face: regular);
                }
                else Text("Поиск: " + leaf, x + 9, y + 5, 11, muted, fieldWidth - 18);
            }

            const double bottom = 23;
            double panelHeight = Math.Max(0, height - listTop - bottom);
            Rectangle(0, listTop, side, panelHeight, windows ? "#FAFAFA" : "#F5F3F1");
            Rectangle(side, listTop, 1, panelHeight, edge);
            Rectangle(0, listTop, width, 1, edge);

            string[] names = windows
                ? new[] { "Быстрый доступ", "Рабочий стол", "Загрузки", "Документы", "Изображения", "Этот компьютер", "Student", "project" }
                : new[] { "Недавние", "Домашняя папка", "Рабочий стол", "Документы", "Загрузки", "Изображения", "Проект", "Корзина" };
            string[] paths = windows
                ? new[] { "", home + "/Desktop", home + "/Downloads", home + "/Documents", home + "/Pictures", "/", home, home + "/project" }
                : new[] { "", home, home + "/Desktop", home + "/Documents", home + "/Downloads", home + "/Pictures", home + "/project", home + "/.Trash" };
            string[] icons = windows
                ? new[] { "star", "desktop", "download", "file", "picture", "desktop", "home", "folder" }
                : new[] { "clock", "home", "desktop", "file", "download", "picture", "folder", "bin" };
            for (int i = 0; i < names.Length; i++)
            {
                double ty = listTop + 7 + i * 27 + (windows && i >= 5 ? 10 : 0);
                if (ty + 23 > height - bottom) break;
                if (windows && i == 5) Rectangle(10, ty - 5, side - 20, 1, edge);
                bool current = paths[i].Length > 0 && (folder == paths[i] || (paths[i] != "/" && paths[i] != home && folder.StartsWith(paths[i] + "/", StringComparison.Ordinal)));
                if (current) Rounded(5, ty - 1, side - 10, 24, highlight, windows ? 0 : 5);
                FilesGlyph(icons[i], 13, ty + 5, windows);
                Text(names[i], 35, ty + 3, 12, ink, side - 41);
            }

            double kindX = side + (width - side) * (search ? .59 : .72);
            double start = listTop + 31;
            double available = Math.Max(0, height - bottom - start);
            double row = windows ? 27 : 30;
            int visible = Math.Max(0, (int)(available / row));
            int first = Math.Max(0, selected - visible + 1);
            Text("Имя", side + 15, listTop + 7, 12, muted, kindX - side - 25);
            Text(search ? "Папка" : "Тип", kindX + 8, listTop + 7, 12, muted, width - kindX - 14);
            Rectangle(side + 1, start - 3, width - side - 1, 1, edge);
            Rectangle(kindX, listTop + 3, 1, 23, edge);
            for (int i = first; i < Math.Min(first + visible, entries.Length); i++)
            {
                double y = start + (i - first) * row;
                bool directory = entries[i].S("kind") == "folder";
                string name = entries[i].S("title");
                if (i == selected) Rounded(side + 5, y, width - side - 10, row - 1, highlight, windows ? 0 : 5);
                else if (!windows && (i - first) % 2 == 1) Rectangle(side + 1, y, width - side - 1, row - 1, "#FAF9F8");
                FilesGlyph(directory ? "folder" : name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "picture" : "file",
                    side + 15, y + (row - 14) / 2, windows);
                if (i == selected && window.S("focus") == "file-name")
                {
                    Rounded(side + 34, y + 1, Math.Max(1, kindX - side - 39), row - 3, "#FFFFFF", 2);
                    Field(window.S("input"), window, side + 37, y + (row - 16)/2,
                        kindX - side - 46, 13, ink, 0, true, face: regular);
                }
                else Text(name, side + 37, y + (row - 16) / 2, 13, ink, kindX - side - 46);
                string kind = FilesType(name, directory, windows);
                if (search)
                {
                    string prefix = folder.TrimEnd('/') + "/", relative = entries[i].S("path");
                    if (relative.StartsWith(prefix, StringComparison.Ordinal)) relative = relative.Substring(prefix.Length);
                    int separator = relative.LastIndexOf('/');
                    kind = separator >= 0 ? relative.Substring(0, separator) : "Здесь";
                }
                Text(kind, kindX + 8, y + (row - 15) / 2, 12, muted, width - kindX - 14);
            }
            if (entries.Length == 0 && available >= 26)
                Text(!search ? "Папка пуста" : string.IsNullOrWhiteSpace(query) ? "Введите часть имени файла" : "Нет совпадений",
                    side + 15, start + 10, 13, muted, width - side - 25);

            Rectangle(0, height - bottom, width, bottom, windows ? "#F8F8F8" : "#F7F5F3");
            Rectangle(0, height - bottom, width, 1, edge);
            string status = (search ? "Найдено: " + entries.Length + "   |   В папке: " + leaf : "Объектов: " + entries.Length) +
                (selected >= 0 && selected < entries.Length ? "   |   Выбран: " + entries[selected].S("title") : "");
            string actionStatus = window.S("status");
            if (focus == "file-name" || actionStatus.StartsWith("Создана копия:", StringComparison.Ordinal) ||
                actionStatus.StartsWith("К копированию:", StringComparison.Ordinal) || actionStatus.StartsWith("Перемещён:", StringComparison.Ordinal))
                status = actionStatus;
            Text(status, 12, height - bottom + 4, 11, muted, width - 24);
        }

        private void FilesNav(string glyph, double x, double y, bool available, string ink)
        { Text(glyph, x, y + 2, 17, available ? ink : "#9A9A9A", 23); }

        private static string FilesType(string name, bool folder, bool windows)
        {
            if (folder) return windows ? "Папка с файлами" : "Папка";
            string extension = Path.GetExtension(name).ToLowerInvariant();
            switch (extension)
            {
                case ".py": return "Файл Python";
                case ".txt": return "Текстовый документ";
                case ".md": return "Документ Markdown";
                case ".csv": return "Таблица CSV";
                case ".json": return "Файл JSON";
                case ".png": return "Изображение PNG";
                default: return extension.Length > 1 ? "Файл " + extension.Substring(1).ToUpperInvariant() : "Файл";
            }
        }

        private void FilesGlyph(string kind, double x, double y, bool windows)
        {
            string icon = windows ? "#337AAE" : "#67645F";
            switch (kind)
            {
                case "folder":
                    Rounded(x + 1, y, 7, 5, windows ? "#DCAE47" : "#967568", 1.5);
                    Rounded(x, y + 3, 16, 10, windows ? "#F3CA5E" : "#AF8A79", 1.5);
                    Rectangle(x + 1, y + 5, 14, 1, windows ? "#FFE391" : "#CCAA9A");
                    break;
                case "file": case "picture":
                    Rounded(x + 2, y, 11, 14, icon, 1.5);
                    Rectangle(x + 3, y + 1, 9, 12, "#FFFFFF");
                    if (kind == "picture")
                    {
                        Rectangle(x + 4, y + 6, 7, 5, "#7BB29B");
                        Rounded(x + 9, y + 3, 2, 2, "#CB933F", 1);
                    }
                    else
                    {
                        Rectangle(x + 5, y + 5, 5, 1, icon);
                        Rectangle(x + 5, y + 8, 5, 1, icon);
                    }
                    break;
                case "download":
                    DrawChromeMark("download", x, y - 1, 15, icon);
                    break;
                default:
                    Text(kind == "home" ? "⌂" : kind == "desktop" ? "▣" :
                        kind == "star" ? "★" : kind == "clock" ? "◷" : "▤", x, y - 3, 15, icon, 18);
                    break;
            }
        }
    }
}
