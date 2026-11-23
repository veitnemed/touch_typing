"""Native snapshots of the simulated Ubuntu desktop and application windows."""
import base64
from functools import partial
from pathlib import Path

import gi
gi.require_version("Gsk", "4.0")
from gi.repository import Gdk, GdkPixbuf, Gio, GLib, Graphene, Gsk, Gtk, Pango


def ink(value):
    result = Gdk.RGBA()
    result.parse(value)
    return result


class DesktopView(Gtk.Widget):
    __gtype_name__ = "TouchTypingLabDesktop"

    def __init__(self):
        super().__init__()
        self.scene = {}
        self.icons = {}
        self.field_scroll = {}
        self.wallpaper = None
        for candidate in ("ubuntu-default-greyscale-wallpaper.png", "warty-final-ubuntu.png"):
            path = Path("/usr/share/backgrounds") / candidate
            if path.is_file():
                try:
                    pixbuf = GdkPixbuf.Pixbuf.new_from_file_at_scale(str(path), 1480, 900, True)
                    self.wallpaper = Gdk.Texture.new_for_pixbuf(pixbuf)
                    break
                except GLib.Error:
                    continue
        self.presentation_app = "ubuntu"
        self.reference_chrome = False
        self.set_overflow(Gtk.Overflow.HIDDEN)
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

    @property
    def chrome_font(self):
        # Only the reference browser shares the lesson shell's bundled font.
        # Keep the older desktop presentation and all monospace surfaces intact.
        if self.reference_chrome and self.presentation_app == "chrome":
            return "Roboto,Noto Sans,sans-serif"
        return "Noto Sans"

    def text_layout(self, value, size, *, mono=False, bold=False, family="Noto Sans"):
        layout = self.create_pango_layout(str(value))
        font = Pango.FontDescription.from_string("DejaVu Sans Mono" if mono else family)
        font.set_absolute_size(size * Pango.SCALE)
        if bold:
            font.set_weight(Pango.Weight.BOLD)
        layout.set_font_description(font)
        return layout

    def label(self, value, x, y, size=13, color="#eeeeef", width=None, mono=False, bold=False,
              *, family="Noto Sans"):
        layout = self.text_layout(value, size, mono=mono, bold=bold, family=family)
        if width is not None:
            layout.set_width(max(1, int(width * Pango.SCALE)))
            layout.set_ellipsize(Pango.EllipsizeMode.END)
        point = Graphene.Point()
        point.init(x, y)
        self.snapshot.save()
        self.snapshot.translate(point)
        self.snapshot.append_layout(layout, ink(color))
        self.snapshot.restore()

    @staticmethod
    def units(value):
        return len(value.encode("utf-16-le")) // 2

    def field(self, value, window, x, y, width, size, color, offset=0, focused=True,
              mono=True, scroll_key=None, *, family="Noto Sans"):
        """Draw one line using the same shaped layout for its text, selection and caret."""
        if width <= 0:
            return
        layout = self.text_layout(value, size, mono=mono, family=family)
        encoded = value.encode("utf-16-le")
        def edge(position):
            length = min(len(encoded), max(0, (position - offset) * 2))
            byte = len(encoded[:length].decode("utf-16-le", errors="ignore").encode())
            return layout.get_cursor_pos(byte)[0].x / Pango.SCALE
        caret = window.get("cursor", 0)
        anchor = window.get("anchor", caret)
        left, right = sorted((edge(caret), edge(anchor)))
        draw_caret = focused and offset <= caret <= offset + len(encoded) // 2
        caret_x = edge(caret) if draw_caret else 0
        x_shift = max(0, caret_x - width + 3)
        if draw_caret and scroll_key is not None:
            # Keep enough of the command on screen to see a one-character edit;
            # scrolling back should not snap the caret to the right edge.
            prior = self.field_scroll.get(scroll_key, 0)
            x_shift = max(x_shift, min(prior, max(0, caret_x - 3)))
            if scroll_key not in self.field_scroll and len(self.field_scroll) >= 32:
                self.field_scroll.pop(next(iter(self.field_scroll)))
            self.field_scroll[scroll_key] = x_shift
        self.snapshot.push_clip(self.rect(x, y, width, size * 1.7))
        x -= x_shift
        if focused and offset >= 0:
            for match in window.get("matches", []):
                start, end = edge(match), edge(match + abs(anchor - caret))
                if end > start:
                    self.fill("#385677", x + start, y, end - start, size * 1.45)
        if focused and right > left:
            self.fill("#35629b", x + left, y, right - left, size * 1.45)
        point = Graphene.Point()
        point.init(x, y)
        self.snapshot.save()
        self.snapshot.translate(point)
        self.snapshot.append_layout(layout, ink(color))
        self.snapshot.restore()
        if draw_caret:
            self.fill(color, x + caret_x, y + 1, 1.5, size * 1.35)
        self.snapshot.pop()

    def app_icon(self, app, x, y, size=30):
        if app not in self.icons:
            names = {"chrome": ["google-chrome", "chromium", "web-browser"], "code": ["code", "com.microsoft.VSCode"],
                     "files": ["org.gnome.Nautilus", "system-file-manager"], "terminal": ["org.gnome.Ptyxis", "utilities-terminal"]}
            theme = Gtk.IconTheme.get_for_display(self.get_display())
            candidates = names.get(app, [app])
            try:
                desktop = Gio.DesktopAppInfo.new(app + ".desktop")
            except TypeError:
                desktop = None
            if desktop and desktop.get_icon():
                self.icons[app] = theme.lookup_by_gicon(desktop.get_icon(), 48, 1, Gtk.TextDirection.LTR, Gtk.IconLookupFlags(0))
            else:
                name = next((n for n in candidates if theme.has_icon(n)), None)
                self.icons[app] = theme.lookup_icon(name, None, 48, 1, Gtk.TextDirection.LTR, Gtk.IconLookupFlags(0)) if name else None
        paintable = self.icons[app]
        if paintable:
            point = Graphene.Point()
            point.init(x, y)
            self.snapshot.save()
            self.snapshot.translate(point)
            paintable.snapshot(self.snapshot, size, size)
            self.snapshot.restore()
            return
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

    def capture_png(self):
        paintable = Gtk.WidgetPaintable.new(self)
        snap = Gtk.Snapshot()
        scale = min(1, 500 / max(1, self.get_width()))
        snap.scale(scale, scale)
        paintable.snapshot(snap, self.get_width(), self.get_height())
        node = snap.to_node()
        if node is None:
            return ""
        texture = self.get_native().get_renderer().render_texture(node, None)
        return base64.b64encode(texture.save_to_png_bytes().get_data()).decode("ascii")

    def do_snapshot(self, snapshot):
        self.snapshot = snapshot
        width, height = self.get_width(), self.get_height()
        if self.presentation_app in ("chrome", "code") and not self.scene.get("overview") and not self.scene.get("showDesktop") and not self.scene.get("switcher") and any(v.get("active") and v.get("app") == self.presentation_app for v in self.scene.get("windows", [])):
            self.fill("#202124" if self.presentation_app == "chrome" else "#1f1f1f", 0, 0, width, height)
            windows = [window for window in self.scene.get("windows", [])
                       if window.get("app") == self.presentation_app and window.get("state") != "minimized"]
            current = next((window for window in windows if window.get("active")), windows[-1] if windows else None)
            if current:
                if self.reference_chrome and self.presentation_app == "chrome":
                    self.draw_window(current, 6, 4, width - 12, height - 8)
                else:
                    self.draw_window(current, 0, -27, width, height + 27)
            else:
                self.label("Окно закрыто. Откройте приложение сочетанием из задания.",
                           24, height * .48, 15, width=width - 48)
            self.draw_capture_feedback(width, height)
            self.snapshot = None
            return
        self.fill("#351d32", 0, 0, width, height)
        if self.wallpaper:
            scale = max(width / self.wallpaper.get_width(), height / self.wallpaper.get_height())
            ww, wh = self.wallpaper.get_width() * scale, self.wallpaper.get_height() * scale
            self.snapshot.append_texture(self.wallpaper, self.rect((width - ww) / 2, (height - wh) / 2, ww, wh))
        self.fill("#261f2b", 0, 0, width, 25)
        workspace = self.scene.get("workspace", 0)
        self.label("Деятельность", 12, 4, 11, bold=True)
        self.label("Рабочий стол " + str(workspace + 1), width / 2 - 55, 4, 11)
        self.label(self.scene.get("layout", "EN").lower() + "    ◉    ◖    ⏻", width - 125, 4, 11)
        self.fill("#241e2c", 0, 25, 51, height - 25)
        for index, app in enumerate(self.scene.get("dock", [])):
            spacing = min(43, (height - 40) / max(1, len(self.scene.get("dock", []))))
            self.app_icon(app, 12, 32 + index * spacing, min(30, spacing - 4))
        windows = [window for window in self.scene.get("windows", [])
                   if window.get("workspace", 0) == workspace and window.get("state") != "minimized"
                   and not self.scene.get("showDesktop")]
        windows.sort(key=lambda item: bool(item.get("active")))
        if windows and not self.scene.get("overview"):
            active = windows[-1]
            state = active.get("state")
            if state in ("maximized", "fullscreen"):
                windows = [active]
            elif state in ("left", "right"):
                other_side = "right" if state == "left" else "left"
                windows = [v for v in windows[:-1] if v.get("state") == other_side][-1:] + [active]
            else:
                windows = windows[-2:]
        for index, window in enumerate(windows):
            x, y, w, h = self.window_bounds(window, index, width, height)
            if self.scene.get("overview"):
                rows = (len(windows) + 1) // 2
                w, h = (width - 115) / 2, (height - 83) / max(1, rows) - 10
                x = 65 + index % 2 * (w + 12)
                y = 66 + (index // 2) * (h + 9)
            self.draw_window(window, x, y, w, h)
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
            choices = [v for v in self.scene.get("windows", []) if v.get("workspace") == workspace]
            for i, candidate in enumerate(choices):
                left = width / 2 + (i - len(choices) / 2) * 75 + 15
                if candidate["id"] == self.scene.get("switchTarget"):
                    self.fill("#818498", left - 6, height * .35 + 29, 47, 47, 8)
                self.app_icon(candidate["app"], left, height * .35 + 35, 35)
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
        purple = self.reference_chrome and self.presentation_app == app == "chrome"
        self.fill("#282541" if purple else "#303036", x, y, w, 27)
        if purple:
            self.app_icon("chrome", x + 10, y + 5, 17)
        self.label(window.get("title", app), x + (34 if purple else 10), y + 6,
                   12 if purple else 11, width=w - 105, bold=True,
                   family=self.chrome_font if purple else "Noto Sans")
        self.label("−    □", x + w - 79, y + 5, 12)
        self.fill("#e95420" if window.get("active") else "#66616c", x + w - 25, y + 5, 17, 17, 9)
        self.label("×", x + w - 21, y + 3, 15)
        if app == "chrome":
            self.chrome(window, x, y + 27, w, h - 27)
        elif app == "files":
            self.files(window, x, y + 27, w, h - 27)
        elif app == "code":
            self.code(window, x, y + 27, w, h - 27)
        else:
            self.terminal(window, x, y + 27, w, h - 27)
        self.snapshot.pop()

    def files(self, window, x, y, w, h):
        if window.get("focus") == "image-preview" and window.get("imagePreview"):
            data = base64.b64decode(window["imagePreview"].split(",", 1)[1])
            texture = Gdk.Texture.new_from_bytes(GLib.Bytes.new(data))
            self.fill("#242427", x, y, w, h)
            scale = min((w - 20) / texture.get_width(), (h - 20) / texture.get_height())
            iw, ih = texture.get_width() * scale, texture.get_height() * scale
            self.snapshot.append_texture(texture, self.rect(x + (w - iw) / 2, y + (h - ih) / 2, iw, ih))
            return
        sidebar = min(178, w * .23)
        self.fill("#f7f6f5", x, y, w, h)
        self.fill("#eeecea", x, y, sidebar, h)
        self.label("Места", x + 16, y + 16, 13, "#57504d", bold=True)
        names = [("Домашняя папка", "/home/student"), ("Загрузки", "/home/student/Downloads"),
                 ("Изображения", "/home/student/Pictures"), ("Проект", "/home/student/project")]
        for i, (name, path) in enumerate(names):
            ty = y + 53 + i * 35
            if window.get("folder") == path:
                self.fill("#e2d6cf", x + 7, ty - 6, sidebar - 14, 31, 6)
            self.label(name, x + 16, ty, 12, "#37302c", width=sidebar - 28)
        left = x + sidebar + 22
        self.label(window.get("folder", "").replace("/home/student", "Домашняя папка", 1).replace("/", "  ›  "),
                   left, y + 13, 14, "#302c29", width=w - sidebar - 38, bold=True)
        self.fill("#dedad6", x + sidebar, y + 41, w - sidebar, 1)
        entries = window.get("entries", [])
        first = max(0, window.get("item", -1) - max(1, int((h - 52) / 37)) + 1)
        for i, entry in enumerate(entries[first:], first):
            ty = y + 51 + (i - first) * 37
            if ty + 34 > y + h:
                break
            if i == window.get("item"):
                self.fill("#f1d9c9", left - 9, ty - 2, w - sidebar - 26, 33, 6)
            folder = entry["kind"] == "folder"
            if folder:
                self.fill("#bc7439", left + 1, ty + 4, 10, 7, 2)
            self.fill("#d99a56" if folder else "#9dadbd", left, ty + 8, 22 if folder else 16, 15, 3)
            self.label(entry["title"], left + 34, ty + 4, 14, "#35302d", width=w - sidebar - 76)
        if not entries:
            self.label("Папка пуста", left, y + 62, 14, "#817872")

    def chrome(self, window, x, y, w, h):
        purple = self.reference_chrome and self.presentation_app == "chrome"
        label = partial(self.label, family=self.chrome_font)
        field = partial(self.field, family=self.chrome_font, mono=False)
        self.fill("#201d36" if purple else "#202124", x, y, w, h)
        tabs = window.get("tabs", [])
        tab_w = min(169 if purple else 150, max(48, (w - 48) / max(1, len(tabs))))
        for i, tab in enumerate(tabs):
            active_tab = i == window.get("activeTab", 0)
            self.fill(("#41375f" if active_tab else "#25213d") if purple else
                      ("#3c4043" if active_tab else "#292a2d"),
                      x + 6 + tab_w * i, y + 2, tab_w - 3, 26, 7)
            label(tab["title"], x + 15 + tab_w * i, y + 8, 10, width=tab_w - 33)
            label("×", x + tab_w * (i + 1) - 12, y + 7, 12)
        label("+", x + min(w - 22, 12 + tab_w * len(tabs)), y + 6, 16)
        self.fill("#222039" if purple else "#303134", x, y + 29, w, 33)
        label("‹   ›   ↻", x + 9, y + 35, 15)
        if purple:
            self.fill("#67558d", x + 89, y + 32, w - 149, 27, 14)
            self.fill("#201c36", x + 90, y + 33, w - 151, 25, 13)
            label("◈", x + 99, y + 36, 12, "#cfc0f6")
            label("☆", x + w - 49, y + 34, 16)
        else:
            self.fill("#202124", x + 79, y + 32, w - 112, 27, 14)
        editing = window["focus"] == "url"
        address = window["input"] if editing else window["url"]
        field(address, window, x + (118 if purple else 94), y + 36,
                   w - (181 if purple else 144), 13, "#f6f0ff" if purple else "#e6e8ec", focused=editing)
        label("⋮", x + w - 21, y + 35, 16)
        body_top = 62
        if window.get("bookmarks"):
            self.fill("#303134", x, y + 62, w, 23)
            label("    ".join(window.get("savedBookmarks", [])), x + 15, y + 65, 10, width=w - 30)
            body_top += 23
        self.fill("#f8f6ff" if purple else "#f8f9fb", x, y + body_top, w, max(1, h - body_top))
        body = window.get("body", "")
        tab = tabs[window["activeTab"]] if tabs else {}
        scale = window.get("zoom", 100) / 100
        left = x + min(w * (.059 if purple else .075), 72)
        available = w - (left - x) * 2
        top = y + body_top + (15 if purple else 24)
        label(tab.get("title", "Новая вкладка"), left, top, (26 if purple else 23) * scale,
                   "#211834" if purple else "#222936", width=available, bold=True)
        body_field = dict(window, cursor=window.get("bodyCursor", 0), anchor=window.get("bodyAnchor", 0))
        offset = 0
        top += (42 if purple else 45) * scale
        for line in body.splitlines():
            if top + 20 >= y + h:
                break
            field(line, body_field, left, top, available, 14 * scale, "#334154", offset=offset,
                       focused=body_field["cursor"] != body_field["anchor"])
            top += 23 * scale
            offset += self.units(line) + 1
        top += 12
        entries = window.get("entries", [])
        visible = max(1, int((y + h - body_top - y - 18) / (33 * scale)))
        if window.get("item", -1) >= 0:
            top = min(top, max(y + body_top + 8, y + h - min(len(entries), visible) * 33 * scale))
        start = max(0, window.get("item", -1) - visible + 1)
        if entries and top < y + body_top + 85:
            self.fill("#f8f9fb", x, top - 4, w, y + h - top + 4)
        for i, entry in enumerate(entries[start:], start):
            if top + 30 > y + h:
                break
            selected = i == window.get("item") and window.get("focus") == "body"
            if selected:
                self.fill("#d8e8ff", left - 8, top - 4, min(available + 8, 440), 31, 5)
            label(entry["title"], left, top, 14 * scale, "#205496", width=available)
            top += 34 * scale
        if window.get("zoom", 100) != 100:
            self.fill("#303134", x + w - 160, y + body_top + 2, 150, 25, 5)
            label("Масштаб  " + str(window["zoom"]) + "%", x + w - 147, y + body_top + 8, 10)
        if window.get("focus") == "find":
            left = x + max(10, w - 300)
            box_width = min(290, w - 20)
            self.fill("#303134", left, y + 64, box_width, 38, 5)
            label("⌕", left + 9, y + 73, 14)
            field(window.get("input", ""), window, left + 29, y + 74, box_width - 42,
                       13, "#ffffff", scroll_key=("find", window.get("id")))
        elif window.get("focus") in ("downloads", "history", "bookmarks"):
            self.fill("#303134", x + max(10, w - 300), y + 64, min(290, w - 20), 38, 5)
            label({"find": "Поиск", "downloads": "Загрузки", "history": "История", "bookmarks": "Закладки"}.get(window["focus"], "")
                       + "  " + window.get("input", ""), x + max(20, w - 290), y + 74, 11, width=min(270, w - 40))

    def code(self, window, x, y, w, h):
        self.fill("#1f1f1f", x, y, w, h)
        self.fill("#181818", x, y, 35, max(1, h - 19))
        for i, icon in enumerate(("▣", "⌕", "⑂", "▷", "▧")):
            self.label(icon, x + 8, y + 13 + i * 33, 18, "#b1b1b1")
        sidebar = 155 if window.get("sidebar", True) and w > 450 else 0
        if sidebar:
            self.fill("#181818", x + 35, y, sidebar, h - 19)
            self.label({"extensions": "РАСШИРЕНИЯ", "project-search": "ПОИСК" , "changes": "ИЗМЕНЕНИЯ"}.get(window.get("view"), "ПРОВОДНИК"), x + 46, y + 10, 9, "#b9b9b9")
            view = window.get("view", "explorer")
            tabs = window.get("tabs", [])
            if view == "changes":
                shown = window.get("changedFiles", [])
                if not shown:
                    shown = ["Изменений нет"]
            elif view == "extensions":
                shown = ["Python", "Pylance"]
            elif view == "project-search":
                shown = window.get("searchResults", [])
            else:
                shown = window.get("projectFiles", [])
            self.label("PILING-MAIN" if view == "explorer" else "", x + 46, y + 36, 10, "#aeb1b8")
            for i, name in enumerate(shown):
                self.label(name, x + 47, y + 60 + 23 * i, 12, "#c6ccd4", width=sidebar - 15)
        editor_x = x + 35 + sidebar
        editor_w = w - 35 - sidebar
        self.fill("#181818", editor_x, y, editor_w, 28)
        tabs = window.get("tabs", [])
        active_index = min(max(0, window.get("activeTab", 0)), max(0, len(tabs) - 1))
        capacity = max(1, int((editor_w - 42) / 98))
        first_tab = max(0, active_index - capacity + 1)
        visible_tabs = tabs[first_tab:first_tab + capacity]
        tab_start = editor_x + (20 if first_tab else 0)
        if first_tab:
            self.label("‹", editor_x + 6, y + 5, 15, "#c6ccd4")
        if first_tab + capacity < len(tabs):
            self.label("›", editor_x + editor_w - 16, y + 5, 15, "#c6ccd4")
        tab_width = min(115, max(1, (editor_w - 42) / max(1, len(visible_tabs))))
        for i, tab in enumerate(visible_tabs):
            active = first_tab + i == active_index
            left = tab_start + i * tab_width
            self.fill("#252526" if active else "#181818", left, y, tab_width - 2, 28)
            if active:
                self.fill("#007acc", left, y, tab_width - 2, 2)
            self.label(tab.get("title", "fft.py"), left + 8, y + 8, 10,
                       "#ffffff" if active else "#d5bd7c", width=tab_width - 16)
        name = tabs[active_index]["title"] if tabs else ""
        split = window.get("split", False)
        pane = editor_w / 2 if split else editor_w
        current = editor_x + (pane if split and window.get("group") == 1 else 0)
        lines_visible = max(1, int(((h * .54 if window.get("panel") else h - 20) - 56) / 22))
        def document(name, body, left, active):
            self.label(name, left + 12, y + 35, 11, "#a2a2a2", width=pane - 22)
            cursor = window.get("bodyCursor", 0) if active else 0
            line_num = body.encode("utf-16-le")[:cursor * 2].decode("utf-16-le", "ignore").count("\n")
            first = max(0, line_num - lines_visible + 1)
            offset = 0
            field = dict(window, cursor=cursor, anchor=window.get("bodyAnchor", cursor) if active else cursor)
            for i, line in enumerate(body.split("\n")):
                if i >= first + lines_visible:
                    break
                if i >= first:
                    ly = y + 57 + (i - first) * 22
                    self.label(str(i + 1), left + 9, ly, 11, "#90949b", mono=True)
                    self.field(line, field, left + 43, ly, pane - 56, 14,
                               "#93b484" if line.lstrip().startswith("#") else "#d2dce6", offset=offset,
                               focused=active and (window.get("focus") == "body" or field["cursor"] != field["anchor"]))
                offset += self.units(line) + 1
        document(name, window.get("body", ""), current, True)
        if split:
            other = window.get("otherTab") or {}
            document(other.get("title", ""), other.get("body", ""), editor_x if current != editor_x else editor_x + pane, False)
            self.fill("#464646", editor_x + pane, y + 29, 1, max(1, (h * .54 if window.get("panel") else h - 19) - 29))
        if window.get("panel"):
            self.fill("#181818", editor_x, y + h * .54, editor_w, h * .46 - 19)
            self.label("ТЕРМИНАЛ     ВЫВОД     ПРОБЛЕМЫ", editor_x + 10, y + h * .54 + 6, 9)
            self.terminal(window, editor_x, y + h * .54 + 27, editor_w, h * .46 - 46, embedded=True)
        focus = window.get("focus", "editor")
        if focus in ("palette", "quick-open", "symbols", "find", "replace-find", "replace-with", "project-search", "goto-line"):
            titles = {"palette": "Палитра команд", "quick-open": "Открыть файл", "symbols": "Символы файла",
                      "find": "Найти", "replace-find": "Найти для замены", "replace-with": "Заменить на",
                      "project-search": "Поиск по проекту", "goto-line": "Перейти к строке"}
            px, pw = x + w * .17, w * .66
            choices = [e["title"] for e in window.get("entries", [])] if focus in ("quick-open", "symbols") else []
            if focus == "palette":
                choices = ["Форматировать документ", "Переключить боковую панель", "Разделить редактор"]
            selected = window.get("optionIndex", 0)
            visible = min(6, len(choices), max(1, int((h - 72) / 25)))
            first = max(0, selected - visible + 1)
            self.fill("#141519", px - 1, y + 6, pw + 2, 52 + visible * 25, 7)
            self.fill("#2a2c31", px, y + 5, pw, 51 + visible * 25, 7)
            self.label(titles[focus], px + 12, y + 10, 10, "#b4bfcd")
            self.field(window.get("input", ""), window, px + 12, y + 26, pw - 24, 13, "#eef1f8", mono=False)
            for index in range(first, min(len(choices), first + visible)):
                iy = y + 52 + (index - first) * 25
                if index == selected:
                    self.fill("#234d72", px + 5, iy - 2, pw - 10, 24, 4)
                self.label(choices[index], px + 14, iy, 13, width=pw - 28)
        self.fill("#007acc", x, y + h - 19, w, 19)
        self.label("⑂ main    ✓ 0   ⚠ 0", x + 9, y + h - 16, 9)
        self.label("UTF-8    Python    " + self.scene.get("layout", "EN"), x + max(170, w - 170), y + h - 16, 9)

    def terminal(self, window, x, y, w, h, embedded=False):
        self.fill("#181818" if embedded else "#291726", x, y, w, h)
        terminal = self.scene.get("terminal", {})
        visible = max(0, int((h - 38) / 21))
        output = "\n".join(terminal.get("output", [])).splitlines()
        recent = output[-visible:] if visible else []
        for i, line in enumerate(recent):
            self.label(line, x + 16, y + 9 + i * 21, 13, "#dccfdc", width=w - 32, mono=True)
        value = self.scene.get("terminalInput", "")
        prefix = "student@ubuntu:" + terminal.get("cwd", "~").replace("/home/student", "~", 1) + "$ "
        if window.get("focus") == "reverse-search":
            prefix = "Поиск «" + window.get("query", "") + "»  ›  "
        shift = self.units(prefix)
        field = dict(window, cursor=shift + window.get("cursor", 0), anchor=shift + window.get("anchor", 0))
        self.field(prefix + value, field, x + 16, y + 10 + len(recent) * 21, w - 32, 13, "#e5efe4",
                   focused=(not embedded or window.get("focus") == "terminal") and window.get("active", True),
                   scroll_key=("terminal", window.get("id")))
