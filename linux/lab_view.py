"""Native snapshots of the simulated Ubuntu desktop and application windows."""
import gi
gi.require_version("Gsk", "4.0")
from gi.repository import Gdk, Graphene, Gsk, Gtk, Pango


def ink(value):
    result = Gdk.RGBA()
    result.parse(value)
    return result


class DesktopView(Gtk.Widget):
    __gtype_name__ = "TouchTypingLabDesktop"

    def __init__(self):
        super().__init__()
        self.scene = {}
        self.presentation_app = "ubuntu"
        self.set_size_request(400, 200)
        self.set_hexpand(True)
        self.set_vexpand(True)
        self.set_focusable(True)
        self.update_property([Gtk.AccessibleProperty.LABEL], ["Учебный рабочий стол Ubuntu"])

    def update(self, scene):
        if self.scene != scene:
            self.scene = scene
            self.queue_draw()
            active = next((w for w in scene.get("windows", []) if w.get("active")), {})
            self.update_property([Gtk.AccessibleProperty.LABEL], [
                "Учебная сцена. " + active.get("title", "Рабочий стол") + ". " + active.get("body", "")[:300]])

    @staticmethod
    def rect(x, y, width, height):
        result = Graphene.Rect()
        result.init(x, y, max(0, width), max(0, height))
        return result

    def fill(self, value, x, y, width, height, radius=0):
        rect = self.rect(x, y, width, height)
        if radius:
            rounded = Gsk.RoundedRect()
            rounded.init_from_rect(rect, radius)
            self.snapshot.push_rounded_clip(rounded)
        self.snapshot.append_color(ink(value), rect)
        if radius:
            self.snapshot.pop()

    def label(self, value, x, y, size=13, color="#eeeeef", width=None, mono=False, bold=False):
        layout = self.create_pango_layout(str(value))
        font = Pango.FontDescription.from_string("DejaVu Sans Mono" if mono else "Noto Sans")
        font.set_absolute_size(size * Pango.SCALE)
        if bold:
            font.set_weight(Pango.Weight.BOLD)
        layout.set_font_description(font)
        if width is not None:
            layout.set_width(max(1, int(width * Pango.SCALE)))
            layout.set_ellipsize(Pango.EllipsizeMode.END)
        point = Graphene.Point()
        point.init(x, y)
        self.snapshot.save()
        self.snapshot.translate(point)
        self.snapshot.append_layout(layout, ink(color))
        self.snapshot.restore()

    def app_icon(self, app, x, y, size=30):
        colors = {"chrome": "#4285f4", "code": "#007acc", "terminal": "#4a2347", "ubuntu": "#e95420"}
        self.fill(colors.get(app, "#555763"), x, y, size, size, 7)
        if app == "chrome":
            self.fill("#ea4335", x + 2, y + 2, size - 4, (size - 4) / 2, 5)
            self.fill("#fbbc05", x + size / 2, y + size / 2, size / 2 - 2, size / 2 - 2, 4)
            self.fill("#34a853", x + 2, y + size / 2, size / 2 - 2, size / 2 - 2, 4)
            self.fill("#e8f0fe", x + size * .27, y + size * .27, size * .46, size * .46, size)
            self.fill("#4285f4", x + size * .34, y + size * .34, size * .32, size * .32, size)
        else:
            self.label({"code": "‹›", "terminal": ">_", "ubuntu": "●"}.get(app, "□"),
                       x + 4, y + size * .13, size * .55, bold=True)

    def do_snapshot(self, snapshot):
        self.snapshot = snapshot
        width, height = self.get_width(), self.get_height()
        if self.presentation_app in ("chrome", "code"):
            self.fill("#202124" if self.presentation_app == "chrome" else "#1f1f1f", 0, 0, width, height)
            windows = [window for window in self.scene.get("windows", [])
                       if window.get("app") == self.presentation_app and window.get("state") != "minimized"]
            current = next((window for window in windows if window.get("active")), windows[-1] if windows else None)
            if current:
                self.draw_window(current, 0, 0, width, height)
            else:
                self.label("Окно закрыто. Откройте приложение сочетанием из задания.",
                           24, height * .48, 15, width=width - 48)
            self.draw_capture_feedback(width, height)
            self.snapshot = None
            return
        self.fill("#31152e", 0, 0, width, height, 8)
        self.fill("#492443", width * .45, 0, width * .55, height)
        self.fill("#5d2c4c", width * .7, height * .45, width * .3, height * .55)
        self.fill("#261f2b", 0, 0, width, 25)
        workspace = self.scene.get("workspace", 0)
        self.label("Деятельность", 12, 4, 11, bold=True)
        self.label("Рабочий стол " + str(workspace + 1), width / 2 - 55, 4, 11)
        self.label(self.scene.get("layout", "EN").lower() + "    ◉    ◖    ⏻", width - 125, 4, 11)
        self.fill("#241e2c", 0, 25, 51, height - 25)
        for index, app in enumerate(("chrome", "code", "terminal", "files")):
            self.app_icon(app, 10, 39 + index * 45, 31)
        windows = [window for window in self.scene.get("windows", [])
                   if window.get("workspace", 0) == workspace and window.get("state") != "minimized"
                   and not self.scene.get("showDesktop")]
        for index, window in enumerate(sorted(windows, key=lambda item: bool(item.get("active")))):
            x, y, w, h = self.window_bounds(window, index, width, height)
            if self.scene.get("overview"):
                w, h = (width - 115) / 2, (height - 90) * .65
                x = 65 + index % 2 * (w + 12)
                y = 55 + min(index // 2, 2) * 20
            self.draw_window(window, x, y, w, h)
        if not windows:
            self.label("Рабочий стол " + str(workspace + 1), width * .37, height * .47, 21, "#d9c7d5")
        if self.scene.get("overview"):
            self.fill("#24232e", width * .27, 30, width * .48, 27, 13)
            self.label("Поиск приложений  " + str(self.scene.get("search", "")), width * .29, 35, 11)
            count = self.scene.get("workspaceCount", 3)
            for i in range(count):
                self.fill("#e1d1e0" if i == workspace else "#7c5b75", width - 24, 65 + i * 18, 12, 10, 3)
        switcher = self.scene.get("switcher")
        if switcher:
            self.fill("#292833", width * .25, height * .35, width * .5, 90, 12)
            self.label("Переключение окон", width * .28, height * .35 + 8, 12)
            selected_app = next((window.get("app") for window in windows if window.get("active")), "")
            for i, app in enumerate(("chrome", "code", "terminal")):
                if app == selected_app:
                    self.fill("#818498", width * .3 + i * 75 - 6, height * .35 + 29, 47, 47, 8)
                self.app_icon(app, width * .3 + i * 75, height * .35 + 35, 35)
        self.draw_capture_feedback(width, height)
        self.snapshot = None

    def draw_capture_feedback(self, width, height):
        if self.scene.get("screenshot"):
            self.fill("rgba(12, 13, 20, .55)", 0, 25, width, height - 25)
            self.fill("#f1f1f1", width * .17, height * .17, width * .66, 2)
            self.fill("#f1f1f1", width * .17, height * .76, width * .66, 2)
            self.fill("#f1f1f1", width * .17, height * .17, 2, height * .59)
            self.fill("#f1f1f1", width * .83, height * .17, 2, height * .59)
            self.fill("#28282e", width * .25, height * .8, width * .5, 38, 12)
            self.label("Область  ·  Экран  ·  Окно     Enter — снимок", width * .27, height * .8 + 11, 11)
        elif self.scene.get("screenshotReady"):
            self.fill("#32333d", width - min(320, width - 70), 31, min(310, width - 80), 30, 7)
            self.label("Снимок в учебном буфере" if self.scene.get("screenshotCopied") else "Снимок сделан  ·  Ctrl+C — скопировать", width - min(310, width - 80), 40, 10)

    @staticmethod
    def window_bounds(window, index, width, height):
        index = min(index, 3)
        state = window.get("state", "normal")
        if state in ("maximized", "fullscreen"):
            return 52, 27, width - 54, height - 29
        if state in ("left", "right"):
            w = (width - 54) / 2
            return 52 + (w if state == "right" else 0), 27, w, height - 29
        return 67 + index * 18, 39 + index * 16, width - 105 - index * 18, height - 60 - index * 16

    def draw_window(self, window, x, y, w, h):
        if h < 50 or w < 90:
            return
        self.fill("#16151b", x + 3, y + 4, w, h, 7)
        self.fill("#303036", x, y, w, h, 7)
        self.snapshot.push_clip(self.rect(x + 1, y + 1, w - 2, h - 2))
        app = window.get("app", "terminal")
        self.fill("#303036", x, y, w, 27)
        self.label(window.get("title", app), x + 10, y + 6, 11, width=w - 105, bold=True)
        self.label("−    □", x + w - 79, y + 5, 12)
        self.fill("#e95420" if window.get("active") else "#66616c", x + w - 25, y + 5, 17, 17, 9)
        self.label("×", x + w - 21, y + 3, 15)
        if app == "chrome":
            self.chrome(window, x, y + 27, w, h - 27)
        elif app == "code":
            self.code(window, x, y + 27, w, h - 27)
        else:
            self.terminal(window, x, y + 27, w, h - 27)
        self.snapshot.pop()

    def chrome(self, window, x, y, w, h):
        self.fill("#202124", x, y, w, h)
        tabs = window.get("tabs", [])
        tab_w = min(150, max(48, (w - 48) / max(1, len(tabs))))
        for i, tab in enumerate(tabs):
            self.fill("#3c4043" if i == window.get("activeTab", 0) else "#292a2d",
                      x + 6 + tab_w * i, y + 2, tab_w - 3, 26, 7)
            self.label(tab.get("title", "Новая вкладка"), x + 15 + tab_w * i, y + 8, 10,
                       width=tab_w - 34)
            self.label("×", x + tab_w * (i + 1) - 12, y + 7, 12)
        self.label("+", x + min(w - 25, 12 + tab_w * len(tabs)), y + 6, 16)
        self.fill("#303134", x, y + 29, w, 33)
        self.label("‹   ›   ↻", x + 9, y + 35, 15)
        self.fill("#202124", x + 83, y + 34, w - 121, 24, 12)
        address = window.get("input", "") if window.get("focus") in ("address", "url") else window.get("url", "")
        self.label("⌕  " + address + ("│" if window.get("focus") in ("address", "url") else ""),
                   x + 96, y + 39, 10, width=w - 146)
        self.label("⋮", x + w - 21, y + 35, 18)
        if window.get("bookmarks"):
            self.fill("#303134", x, y + 62, w, 20)
            self.label("★ Документация Python      ★ piling-main", x + 12, y + 65, 10)
        body_top = 82 if window.get("bookmarks") else 62
        self.fill("#f7f9fc", x, y + body_top, w, max(1, h - body_top))
        body = window.get("body", "")
        active = window.get("activeTab", 0)
        tab = tabs[active] if isinstance(active, int) and 0 <= active < len(tabs) else {}
        self.label(tab.get("title", "Учебная страница"), x + 28, y + 87, 22, "#202124", width=w - 60, bold=True)
        self.label(window.get("url", ""), x + 29, y + 121, 10, "#68717f", width=w - 65)
        self.fill("#dce6f5", x + 28, y + 145, w - 58, 2)
        for i, line in enumerate((body or tab.get("body", "")).splitlines()[:12]):
            if y + 162 + i * 21 > y + h - 10:
                break
            self.label(line, x + 30, y + 162 + i * 21, 12, "#263143", width=w - 66)
        if window.get("zoom", 100) != 100:
            self.fill("#303134", x + w - 160, y + body_top + 2, 150, 25, 5)
            self.label("Масштаб  " + str(window["zoom"]) + "%", x + w - 147, y + body_top + 8, 10)
        if window.get("focus") in ("find", "downloads", "history", "bookmarks"):
            self.fill("#303134", x + max(10, w - 300), y + 64, min(290, w - 20), 38, 5)
            self.label({"find": "Поиск", "downloads": "Загрузки", "history": "История", "bookmarks": "Закладки"}.get(window["focus"], "")
                       + "  " + window.get("input", ""), x + max(20, w - 290), y + 74, 11, width=min(270, w - 40))

    def code(self, window, x, y, w, h):
        self.fill("#1f1f1f", x, y, w, h)
        self.fill("#181818", x, y, 35, max(1, h - 19))
        for i, icon in enumerate(("▣", "⌕", "⑂", "▷", "▧")):
            self.label(icon, x + 8, y + 13 + i * 33, 18, "#b1b1b1")
        sidebar = 155 if window.get("sidebar", True) and w > 450 else 0
        if sidebar:
            self.fill("#181818", x + 35, y, sidebar, h - 19)
            self.label("ПРОВОДНИК", x + 46, y + 10, 9, "#b9b9b9")
            self.label("⌄ PILING-MAIN", x + 43, y + 39, 10, bold=True)
            for i, name in enumerate(("⌄ src", "   fft.py", "   signal.py", "   vectors.py", "  README.md")):
                self.label(name, x + 47, y + 65 + 22 * i, 11, "#c0c5ce", width=sidebar - 15)
        editor_x = x + 35 + sidebar
        editor_w = w - 35 - sidebar
        self.fill("#181818", editor_x, y, editor_w, 28)
        tabs = window.get("tabs", [])
        for i, tab in enumerate(tabs[:4]):
            self.fill("#252526" if i == window.get("activeTab", 0) else "#181818", editor_x + i * 115, y, 113, 28)
            self.label("Py  " + tab.get("title", "fft.py"), editor_x + 10 + i * 115, y + 8, 10, "#d5bd7c", width=100)
        self.label("src  ›  " + window.get("title", "fft.py"), editor_x + 12, y + 35, 10, "#a2a2a2", width=editor_w - 20)
        for i, line in enumerate(window.get("body", "").splitlines()[:18]):
            line_y = y + 59 + i * 19
            if line_y > y + h - 25:
                break
            self.label(str(i + 1), editor_x + 9, line_y, 11, "#707070", mono=True)
            shade = "#6a9955" if line.strip().startswith("#") else "#c586c0" if line.strip().startswith(("def ", "return ", "import ", "from ")) else "#d4d4d4"
            self.label(line, editor_x + 35, line_y, 12, shade, width=editor_w - 45, mono=True)
        if window.get("selected"):
            self.fill("rgba(52, 103, 168, .28)", editor_x + 31, y + 57, editor_w - 40, min(h - 80, 74))
        cursor = max(0, window.get("cursor", 0))
        prefix = window.get("body", "")[:cursor]
        row_number = prefix.count("\n")
        column = len(prefix.rsplit("\n", 1)[-1])
        if window.get("focus", "body") == "body":
            self.fill("#e9e9e9", editor_x + 35 + min(column, 65) * 7.2, y + 59 + min(row_number, 12) * 19, 1, 15)
        if window.get("split"):
            self.fill("#464646", editor_x + editor_w / 2, y + 29, 1, h - 48)
        focus = window.get("focus", "editor")
        if focus in ("palette", "quickopen", "quick-open", "find", "replace", "search", "project-search", "goto-line"):
            self.fill("#303031", x + w * .2, y + 7, w * .65, 76, 5)
            self.fill("#202020", x + w * .2 + 6, y + 13, w * .65 - 12, 24, 2)
            self.label(("> " if focus == "palette" else "") + window.get("input", "") + "│", x + w * .2 + 12, y + 18, 12, width=w * .65 - 30)
            self.label({"palette": "Палитра команд", "quickopen": "Перейти к файлу", "quick-open": "Перейти к файлу", "project-search": "Поиск по проекту", "goto-line": "Перейти к строке", "find": "Найти", "replace": "Найти и заменить", "search": "Поиск в файлах"}[focus],
                       x + w * .2 + 12, y + 51, 11, "#aeb7c4")
        if focus == "terminal":
            self.fill("#181818", editor_x, y + h * .54, editor_w, h * .46 - 19)
            self.label("ТЕРМИНАЛ     ВЫВОД     ПРОБЛЕМЫ", editor_x + 10, y + h * .54 + 6, 9)
            self.label("student@ubuntu:~/project$ " + self.scene.get("terminalInput", "") + "│", editor_x + 10, y + h * .54 + 30, 11, "#a9d4a1", width=editor_w - 20, mono=True)
        self.fill("#007acc", x, y + h - 19, w, 19)
        self.label("⑂ main    ✓ 0   ⚠ 0", x + 9, y + h - 16, 9)
        self.label("UTF-8    Python    " + self.scene.get("layout", "EN"), x + max(170, w - 170), y + h - 16, 9)

    def terminal(self, window, x, y, w, h):
        self.fill("#300a24", x, y, w, h)
        terminal = self.scene.get("terminal", {})
        output = terminal.get("output", [])
        if isinstance(output, str):
            output = output.splitlines()
        lines = list(output) + ["user@ubuntu:" + terminal.get("cwd", "~") + "$ " + self.scene.get("terminalInput", window.get("input", "")) + "▏"]
        visible = max(1, int((h - 15) / 18))
        for i, line in enumerate(lines[-visible:]):
            self.label(str(line), x + 12, y + 9 + i * 18, 11, "#f0e9ed", width=w - 24, mono=True)
