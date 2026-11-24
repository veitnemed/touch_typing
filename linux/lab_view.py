"""Native snapshots of simulated Ubuntu and Windows desktops and application windows."""
import base64
from functools import partial

import gi
gi.require_version("Gsk", "4.0")
from gi.repository import Gdk, Gio, GLib, Graphene, Gsk, Gtk, Pango

# GitHub Dark Default role mapping: primer/github-vscode-theme, src/theme.js.
CODE_BG = "#0d1117"
CODE_INSET = "#010409"
CODE_FG = "#e6edf3"
CODE_MUTED = "#7d8590"
CODE_LINE_NO = "#6e7681"
CODE_LIST_SELECTION = "#2d3239"
CODE_BORDER = "#30363d"
CODE_ROW = "#171b22"
CODE_FOCUS = "#2f81f7"
CODE_SELECTION = "#264f78"
CODE_SYNTAX = {"keyword": "#ff7b72", "operator": "#ff7b72", "string": "#a5d6ff",
               "comment": "#8b949e", "number": "#79c0ff", "function": "#d2a8ff", "variable": "#ffa657"}


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
            choices = (" " + "; ".join(e.get("title", "") for e in active.get("entries", ()))
                       if active.get("focus") in ("code-problems", "code-quick-fix") else "")
            self.update_property([Gtk.AccessibleProperty.LABEL], [
                ("Учебный рабочий стол Windows 10. " if scene.get("desktopStyle") == "win10" else "Учебная сцена. ")
                + {"start": "Пуск. ", "task-view": "Представление задач. ", "snap": "Выберите второе окно. "}
                  .get(scene.get("desktopOverlay"), "")
                + active.get("title", "Рабочий стол") + ". " + active.get("body", "")[:300] + choices])

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
              mono=True, scroll_key=None, *, family="Noto Sans", line_break=0,
              syntax=(), selection_color="#35629b", caret_color=None):
        """Draw one line using the same shaped layout for its text, selection and caret."""
        if width <= 0:
            return
        layout = self.text_layout(value, size, mono=mono, family=family)
        encoded = value.encode("utf-16-le")
        if syntax:
            attrs = Pango.AttrList()
            # Pango uses UTF-8 byte indices on the original layout; resolving
            # UTF-16 spans here doesn't split text into separate shaping runs.
            boundaries = {0: 0}
            units = count = 0
            for ch in value:
                units += 2 if ord(ch) > 0xFFFF else 1
                count += len(ch.encode())
                boundaries[units] = count
            for token in syntax:
                first = max(0, token["start"] - offset)
                last = min(units, token["end"] - offset)
                tone = CODE_SYNTAX.get(token["kind"])
                if first < last and first in boundaries and last in boundaries and tone:
                    color16 = (int(tone[i:i + 2], 16) * 257 for i in (1, 3, 5))
                    attr = Pango.attr_foreground_new(*color16)
                    attr.start_index, attr.end_index = boundaries[first], boundaries[last]
                    attrs.insert(attr)
            layout.set_attributes(attrs)
        def edge(position):
            length = min(len(encoded), max(0, (position - offset) * 2))
            byte = len(encoded[:length].decode("utf-16-le", errors="ignore").encode())
            return layout.get_cursor_pos(byte)[0].x / Pango.SCALE
        caret = window.get("cursor", 0)
        anchor = window.get("anchor", caret)
        left, right = sorted((edge(caret), edge(anchor)))
        line_end = offset + len(encoded) // 2
        # The newline is represented by the gap after this line, not a
        # standalone glyph. Keep the caret at the same Pango boundary.
        selected_break = (line_break > 0 and min(anchor, caret) < line_end + line_break
                          and max(anchor, caret) > line_end)
        draw_caret = focused and offset <= caret <= line_end
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
            self.fill(selection_color, x + left, y, right - left, size * 1.45)
        if focused and selected_break:
            end = edge(line_end)
            space_width = self.text_layout(" ", size, mono=mono, family=family).get_size()[0] / Pango.SCALE
            self.fill(selection_color, x + end, y, space_width, size * 1.45)
        point = Graphene.Point()
        point.init(x, y)
        self.snapshot.save()
        self.snapshot.translate(point)
        self.snapshot.append_layout(layout, ink(color))
        self.snapshot.restore()
        if draw_caret:
            self.fill(caret_color or color, x + caret_x, y + 1, 1.5, size * 1.35)
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

    def capture_png(self, active_only=False):
        if active_only:
            active = next((w for w in self.scene.get("windows", []) if w.get("active") and
                           w.get("app") == "terminal" and w.get("state") != "minimized"), None)
            if not active:
                return ""
            width, height = self.get_width(), self.get_height()
            windows = [w for w in self.scene.get("windows", []) if w.get("workspace", 0) == self.scene.get("workspace", 0)
                       and w.get("state") != "minimized"]
            # A normal active terminal is the topmost of at most two visible
            # windows. Render the actual terminal content only, at its size.
            win10 = self.scene.get("desktopStyle") == "win10"
            index = max(0, min(1, len(windows) - 1))
            _, _, w, h = (self.win10_window_bounds(active, index, width, height - min(36, height * .15))
                          if win10 else self.window_bounds(active, index, width, height))
            if w < 90 or h < 50:
                return ""
            scale = min(1, 500 / w)
            snap = Gtk.Snapshot()
            snap.scale(scale, scale)
            old = self.snapshot
            try:
                self.snapshot = snap
                self.draw_window(active, 0, 0, w, h, win10=win10)
            finally:
                self.snapshot = old
            node = snap.to_node()
            renderer = self.get_native().get_renderer()
            if node is None or renderer is None:
                return ""
            texture = renderer.render_texture(node, self.rect(0, 0, w * scale, h * scale))
            return base64.b64encode(texture.save_to_png_bytes().get_data()).decode("ascii")
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

    def gradient(self, x, y, w, h, start, end, palette):
        points = []
        for relative in (start, end):
            point = Graphene.Point()
            point.init(x + relative[0] * w, y + relative[1] * h)
            points.append(point)
        stops = []
        for offset, color in palette:
            stop = Gsk.ColorStop()
            stop.offset = offset
            stop.color = ink(color)
            stops.append(stop)
        self.snapshot.append_linear_gradient(self.rect(x, y, w, h), *points, stops)

    def ubuntu_wallpaper(self, width, height):
        # Original charcoal facets inspired by the classic Ubuntu greyscale.
        self.gradient(0, 0, width, height, (0, 0), (1, 1),
                      ((0, "#53525a"), (.46, "#33343d"), (1, "#202027")))
        for i in range(4):
            self.gradient(0, i * height / 4, width, height / 4 + 1, (0, 0), (1, 1),
                          ((0, "rgba(255,255,255,.035)"), (.6, "rgba(0,0,0,0)"),
                           (1, "rgba(0,0,0,.07)")))

    def win10_mark(self, x, y, size, color="#ffffff"):
        gap = max(1, size * .075)
        pane = (size - gap) / 2
        for row in range(2):
            for col in range(2):
                self.fill(color, x + col * (pane + gap), y + row * (pane + gap), pane, pane)

    def win10_wallpaper(self, width, height):
        # Original four-pane light study; no system wallpaper or trademark asset imported.
        self.gradient(0, 0, width, height, (0, .2), (1, .64),
                      ((0, "#06264c"), (.47, "#064b87"), (.72, "#087cba"), (1, "#063e82")))
        self.gradient(0, 0, width, height, (.5, 0), (.5, 1),
                      ((0, "rgba(1,9,37,.40)"), (.49, "rgba(4,16,41,0)"), (1, "rgba(0,5,28,.38)")))
        size = min(width * .22, height * .65)
        cx, cy = width * .74, height * .48
        for i in range(5, 0, -1):
            inset = i * size * .036
            self.fill("rgba(55,221,255,.055)", cx - size / 2 - inset, cy - size / 2 - inset,
                      size + 2 * inset, size + 2 * inset, inset)
        pane = size * .478
        for row in range(2):
            for col in range(2):
                x = cx - size / 2 + col * size * .522
                y = cy - size / 2 + row * size * .522
                self.gradient(x, y, pane, pane, (0, 0), (1, 1),
                              ((0, "#43dcfb"), (.042, "#1cb3e7"), (.57, "#1093c8"), (1, "#167dbc")))
                self.fill("rgba(208,255,255,.42)", x, y, pane, 1)

    @staticmethod
    def win10_caption(app):
        return {"files": "Проводник", "chrome": "Chrome", "code": "VS Code",
                "terminal": "Терминал", "settings": "Параметры"}.get(app, app)

    def win10_app_icon(self, app, x, y, size):
        if app == "files":
            self.fill("#ffc951", x, y + size * .19, size * .51, size * .35, 2)
            self.fill("#f9db79", x, y + size * .32, size, size * .54, 2)
            self.fill("#2f83c5", x + size * .19, y + size * .59, size * .62, size * .2)
        elif app == "terminal":
            self.fill("#0c1520", x, y + size * .12, size, size * .77, 2)
            self.label(">_", x + size * .14, y + size * .12, size * .58, "#dceafe", mono=True)
        else:
            self.app_icon(app, x, y, size)

    def win10_taskbar(self, width, top, height, windows, workspace, overlay):
        self.fill("#101a29", 0, top, width, height)
        mid = top + height / 2
        if overlay == "start":
            self.fill("#30425a", 0, top, 44, height)
        self.win10_mark(14, mid - 8, 16, "#74caff" if overlay == "start" else "#f2f5fa")
        # Search icon and a two-window Task View glyph, without depending on symbol fonts.
        self.fill("#e9f1ff", 56, mid - 6, 11, 11, 6)
        self.fill("#101a29", 58, mid - 4, 7, 7, 4)
        self.fill("#e9f1ff", 65, mid + 4, 5, 2)
        if overlay == "task-view":
            self.fill("#30425a", 76, top, 37, height)
        for x, y in ((89, mid - 5), (94, mid - 8)):
            self.fill("#d3e0ed", x, y, 12, 10)
            self.fill("#192b3f", x + 1, y + 1, 10, 8)
        for i, app in enumerate(("files", "chrome", "code", "terminal")):
            x = 116 + i * 37
            if x + 32 > width - 94:
                break
            opened = [v for v in windows if v.get("app") == app and v.get("workspace", 0) == workspace]
            if any(v.get("active") and v.get("state") != "minimized" for v in opened):
                self.fill("#263f57", x, top, 33, height)
            if opened:
                self.fill("#4cb9fa", x + 7, top + height - 2, 19, 2)
            self.win10_app_icon(app, x + 7, mid - 9, 19)
        self.label(self.scene.get("layout", "EN"), width - 80, mid - 8, 11, "#e2eaf3", width=25)
        self.label("10:00", width - 49, mid - 8, 11, "#e2eaf3", width=44)
        self.fill("#748395", width - 4, top, 1, height)

    def win10_start(self, width, top):
        wm = min(500, width - 2)
        hm = min(326, top - 2)
        y = top - hm
        self.fill("rgba(0,0,0,.25)", 5, y - 2, wm, hm + 2)
        self.fill("#202832", 0, y, wm, hm)
        self.fill("#19212b", 0, y, 45, hm)
        self.label("≡", 14, y + 8, 17)
        self.label("⊙", 14, top - 29, 16)
        self.label("Пуск", 57, y + 11, 14, "#f3f6f9", bold=True, width=wm - 65)
        apps = self.scene.get("desktopStartApps") or ("chrome", "code", "terminal", "files")
        selection = self.scene.get("desktopOverlaySelection", 0)
        query = self.scene.get("search", "")
        has_tiles = wm >= 355
        list_width = 169 if has_tiles else wm - 58
        show_query = hm >= 146
        row = max(18, min(34, (hm - 32 - (26 if show_query else 0)) / max(1, len(apps))))
        for i, app in enumerate(apps):
            ty = y + 31 + i * row
            if ty + row > top:
                break
            if i == selection:
                self.fill("#34485b", 50, ty, list_width, row - 1)
                self.fill("#63c3ff", 50, ty, 2, row - 1)
            size = min(19, row - 4)
            self.win10_app_icon(app, 59, ty + (row - size) / 2, size)
            self.label(self.win10_caption(app), 85, ty + (row - 15) / 2, 11,
                       "#e9f0f8", width=list_width - 40)
        if show_query:
            self.fill("#f3f5f7", 50, top - 26, list_width, 22)
            self.label(str(query or "Поиск приложения"), 57, top - 23, 11, "#253648", width=list_width - 12)
        if has_tiles:
            self.label("Закреплённые", 235, y + 12, 11, "#d2deea", width=wm - 242)
            unit = min(70, (wm - 243) / 2, (hm - 38) / 2)
            for i, app in enumerate(apps[:4]):
                x, ty = 237 + i % 2 * (unit + 4), y + 31 + i // 2 * (unit + 4)
                self.fill(("#1762a1", "#1d4c77", "#176197", "#28405b")[i], x, ty, unit, unit)
                size = min(22, unit * .36)
                self.win10_app_icon(app, x + (unit - size) / 2, ty + (unit - size) * .37, size)
                if unit >= 42:
                    self.label(self.win10_caption(app), x + 4, ty + unit - 15, 9, width=unit - 8)

    def win10_cards(self, width, top, windows, title, selection, workspaces=False, x=0):
        area = width - x
        self.fill("#10243b" if workspaces else "rgba(7,20,38,.91)", x, 0, area, top)
        self.label(title, x + 16, 10, 15 if area >= 250 else 12, bold=True, width=area - 32)
        start = 41
        if workspaces:
            count = self.scene.get("workspaceCount", 1)
            current = self.scene.get("workspace", 0)
            labels = [("Рабочий стол " + str(i + 1), i == current) for i in range(count)]
            sw = min(116, max(70, (area - 35) / max(1, len(labels)) - 7))
            for i, (label, active) in enumerate(labels):
                left = x + 16 + i * (sw + 7)
                if left + sw > width - 9:
                    break
                self.fill("#286090" if active else "#20354b", left, 36, sw, 25)
                self.label(label, left + 5, 41, 10, width=sw - 10)
            start = 73
        if not windows:
            self.label("На этом рабочем столе нет окон", x + 16, start + 5, 11, "#d1e1f3", width=area - 28)
            return
        columns = min(len(windows), 3 if area >= 615 else 2 if area >= 365 else 1)
        cw = (area - 32 - 8 * (columns - 1)) / columns
        rows = (len(windows) + columns - 1) // columns
        ch = min(114, max(31, (top - start - 9 - (rows - 1) * 6) / rows))
        for i, window in enumerate(windows):
            left, y = x + 16 + (i % columns) * (cw + 8), start + (i // columns) * (ch + 6)
            if y + ch > top:
                break
            selected = i == selection
            self.fill("#9cdaff" if selected else "#4f6883", left, y, cw, ch)
            self.fill("#152a42", left + 2, y + 2, cw - 4, ch - 4)
            self.win10_app_icon(window["app"], left + 9, y + 7, 15)
            self.label(window.get("title", self.win10_caption(window["app"])), left + 31, y + 7, 10,
                       "#e1ecf9", width=cw - 39)
            if ch >= 54:
                self.fill("#24517b", left + 8, y + 26, cw - 16, ch - 34)
                self.fill("#c2dbed", left + 8, y + 26, cw - 16, 5)
                self.fill("#5e819e", left + 13, y + 35, (cw - 28) * .65, 2)

    @staticmethod
    def win10_window_bounds(window, index, width, top):
        state = window.get("state")
        if state in ("maximized", "fullscreen"):
            return 0, 0, width, top
        if state in ("left", "right", "snapped-left", "snapped-right"):
            return (width / 2 if state in ("right", "snapped-right") else 0), 0, width / 2, top
        slot = max(0, min(1, window.get("normalSlot", index)))
        w, h = width * .74, top * .75
        return (width - w) * (.21 + .6 * slot), (top - h) * (.22 + .64 * slot), w, h

    def draw_win10(self, width, height):
        workspace = self.scene.get("workspace", 0)
        overlay = self.scene.get("desktopOverlay", "")
        state_windows = self.scene.get("windows", [])
        current = [v for v in state_windows if v.get("workspace", 0) == workspace]
        windows = [v for v in current if v.get("state") != "minimized" and not self.scene.get("showDesktop")]
        windows.sort(key=lambda item: bool(item.get("active")))
        bar = min(36, height * .15)
        top = height - bar
        self.win10_wallpaper(width, top)
        active = next((window for window in windows if window.get("active")), None)
        snapped = {"snapped-left", "snapped-right", "left", "right"}
        right = {"snapped-right", "right"}
        if active and active.get("state") in ("maximized", "fullscreen"):
            windows = [active]
        elif active and active.get("state") in snapped:
            other = [v for v in windows if v is not active and v.get("state") in snapped
                     and (v["state"] in right) != (active["state"] in right)]
            windows = other[-1:] + [active]
        else:
            windows = windows[-2:]
        for i, window in enumerate(windows):
            self.draw_window(window, *self.win10_window_bounds(window, i, width, top), win10=True)
        if overlay in ("task-view", "snap"):
            by_id = {v["id"]: v for v in current}
            ids = self.scene.get("desktopOverlayWindows")
            candidates = list(current) if ids is None else [by_id[i] for i in ids if i in by_id]
        if overlay == "start":
            self.win10_start(width, top)
        elif overlay == "task-view":
            self.win10_cards(width, top, candidates, "Представление задач",
                             self.scene.get("desktopOverlaySelection", -1), workspaces=True)
        elif overlay == "snap":
            x = width / 2 if active and active.get("state") not in right else 0
            limit = width if x else width / 2
            self.win10_cards(limit, top, candidates, "Выберите второе окно",
                             self.scene.get("desktopOverlaySelection", -1), x=x)
        elif self.scene.get("switcher"):
            candidates = current
            ids = self.scene.get("switchChoices")
            if ids:
                by_id = {v["id"]: v for v in candidates}
                candidates = [by_id[i] for i in ids if i in by_id]
            target = self.scene.get("switchTarget")
            self.win10_cards(width, top, candidates, "Переключение окон",
                             next((i for i, v in enumerate(candidates) if v["id"] == target), -1))
        self.win10_taskbar(width, top, bar, state_windows, workspace, overlay)

    def do_snapshot(self, snapshot):
        self.snapshot = snapshot
        width, height = self.get_width(), self.get_height()
        if self.scene.get("desktopStyle") == "win10" and (self.presentation_app not in ("chrome", "code")
                or self.scene.get("desktopOverlay") or self.scene.get("switcher") or self.scene.get("showDesktop")
                or not any(v.get("active") and v.get("app") == self.presentation_app for v in self.scene.get("windows", []))):
            self.draw_win10(width, height)
            self.draw_capture_feedback(width, height)
            self.snapshot = None
            return
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
        self.ubuntu_wallpaper(width, height)
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
            elif self.scene.get("gnomeWindows"):
                windows = windows[-2:]
            elif state in ("left", "right"):
                other_side = "right" if state == "left" else "left"
                windows = [v for v in windows[:-1] if v.get("state") == other_side][-1:] + [active]
            else:
                windows = windows[-2:]
        for index, window in enumerate(windows):
            x, y, w, h = self.window_bounds(window, index, width, height, self.scene.get("gnomeWindows", False))
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
            ids = self.scene.get("switchChoices")
            if ids:
                by_id = {entry["id"]: entry for entry in choices}
                choices = [by_id[identity] for identity in ids if identity in by_id]
            same_app = bool(ids) and self.scene.get("gnomePractice") == "same-app"
            for i, candidate in enumerate(choices):
                left = width / 2 + (i - len(choices) / 2) * 75 + 15
                if candidate["id"] == self.scene.get("switchTarget"):
                    self.fill("#818498", left - 6, height * .35 + 29, 47, 47, 8)
                self.app_icon(candidate["app"], left, height * .35 + 35, 35)
                if same_app:
                    # Same icons are not sufficient to choose the intended window.
                    caption = candidate.get("url") or candidate.get("title", "")
                    self.label(caption.rsplit("/", 1)[-1], left - 12, height * .35 + 68, 10, width=74)
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
    def window_bounds(window, index, width, height, stable_normal=False):
        index = min(index, 3)
        state = window.get("state", "normal")
        if state in ("maximized", "fullscreen"):
            return 52, 27, width - 54, height - 29
        if state in ("left", "right"):
            w = (width - 54) / 2
            return 52 + (w if state == "right" else 0), 27, w, height - 29
        if stable_normal:
            slot = max(0, min(1, window.get("normalSlot", 0)))
            w, h = (width - 54) * .72, (height - 29) * .8
            return 52 + (width - 54 - w) * (.15 + .7 * slot), 27 + (height - 29 - h) * (.24 + .52 * slot), w, h
        return 67 + index * 18, 39 + index * 16, width - 105 - index * 18, height - 60 - index * 16

    def draw_window(self, window, x, y, w, h, *, win10=False):
        if h < 50 or w < 90:
            return
        radius = 0 if win10 else 7
        self.fill("#16151b", x + 3, y + 4, w, h, radius)
        self.fill("#569ace" if win10 and window.get("active") else "#303036", x, y, w, h, radius)
        self.snapshot.push_clip(self.rect(x + 1, y + 1, w - 2, h - 2))
        app = window.get("app", "terminal")
        purple = self.reference_chrome and self.presentation_app == app == "chrome"
        if win10:
            self.fill("#f4f7fa", x + 1, y + 1, w - 2, 26)
            self.win10_app_icon(app, x + 8, y + 7, 13)
            self.label(window.get("title", self.win10_caption(app)), x + 27, y + 6, 11,
                       "#1c2937", width=w - 111)
            for i, glyph in enumerate(("−", "□", "×")):
                bx = x + w - 77 + i * 24
                self.label(glyph, bx + 6, y + 3, 14, "#263648")
        else:
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
            self.files(window, x, y + 27, w, h - 27, win10=win10)
        elif app == "code":
            self.code(window, x, y + 27, w, h - 27)
        else:
            self.terminal(window, x, y + 27, w, h - 27)
        self.snapshot.pop()

    def files(self, window, x, y, w, h, *, win10=False):
        if window.get("focus") == "image-preview" and window.get("imagePreview"):
            data = base64.b64decode(window["imagePreview"].split(",", 1)[1])
            texture = Gdk.Texture.new_from_bytes(GLib.Bytes.new(data))
            self.fill("#242427", x, y, w, h)
            scale = min((w - 20) / texture.get_width(), (h - 20) / texture.get_height())
            iw, ih = texture.get_width() * scale, texture.get_height() * scale
            self.snapshot.append_texture(texture, self.rect(x + (w - iw) / 2, y + (h - ih) / 2, iw, ih))
            return
        sidebar = min(178, w * .23)
        self.fill("#ffffff" if win10 else "#f7f6f5", x, y, w, h)
        self.fill("#f0f4fa" if win10 else "#eeecea", x, y, sidebar, h)
        self.label("Быстрый доступ" if win10 else "Места", x + 16, y + 16, 13, "#57504d", bold=True)
        names = [("Домашняя папка", "/home/student"), ("Загрузки", "/home/student/Downloads"),
                 ("Изображения", "/home/student/Pictures"), ("Проект", "/home/student/project")]
        for i, (name, path) in enumerate(names):
            ty = y + 53 + i * 35
            if window.get("folder") == path:
                self.fill("#cce8ff" if win10 else "#e2d6cf", x + 7, ty - 6, sidebar - 14, 31, 0 if win10 else 6)
            self.label(name, x + 16, ty, 12, "#37302c", width=sidebar - 28)
        left = x + sidebar + 22
        folder = (window.get("windowsPath") if win10 else
                  window.get("folder", "").replace("/home/student", "Домашняя папка", 1).replace("/", "  ›  "))
        self.label(folder or ("Проводник" if win10 else ""), left, y + 13, 14, "#302c29",
                   width=w - sidebar - 38, bold=True)

        self.fill("#dedad6", x + sidebar, y + 41, w - sidebar, 1)
        entries = window.get("entries", [])
        first = max(0, window.get("item", -1) - max(1, int((h - 52) / 37)) + 1)
        for i, entry in enumerate(entries[first:], first):
            ty = y + 51 + (i - first) * 37
            if ty + 34 > y + h:
                break
            if i == window.get("item"):
                self.fill("#cce8ff" if win10 else "#f1d9c9", left - 9, ty - 2, w - sidebar - 26, 33, 0 if win10 else 6)
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
        if window.get("browserPractice"):
            saved = window.get("url") in window.get("savedBookmarks", [])
            self.fill("#222039" if purple else "#303134", x + w - 53, y + 33, 23, 22, 4)
            label("★" if saved else "☆", x + w - 49, y + 35, 15, "#bda8ff" if saved else "#d6dde9")
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
        url = window.get("url", "")
        search_home = url == "https://www.google.com/"
        search_results = url.startswith("https://www.google.com/search?q=")
        if search_home or search_results:
            bar_width = min(435, available)
            sx = x + (w - bar_width) / 2 if search_home else left
            label("Google", x + w / 2 - 54 if search_home else sx, top, 32 if search_home else 24,
                  "#4285f4", width=available, bold=True)
            self.fill("#e4edf8", sx, top + 38, bar_width, 29, 14)
            label("⌕  " + ("Введите запрос в адресной строке" if search_home else body),
                  sx + 12, top + 45, 12, "#234975", width=bar_width - 24)
            body = ""
            top += 34
        else:
            label(tab.get("title", "Новая вкладка"), left, top, (26 if purple else 23) * scale,
                  "#211834" if purple else "#222936", width=available, bold=True)
            if url == "https://github.com/veitnemed/touch_typing":
                label("Code   ·   Issues   ·   README", left, top + 31, 11, "#205496", width=available)
                top += 10
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
        if window.get("browserPractice") and window.get("status", "").startswith("Сохранено в закладки"):
            self.fill("#e5edf8", x, y + h - 24, w, 24)
            label(window["status"], x + 9, y + h - 19, 11, "#284571", width=w - 18)

    def python_file_icon(self, x, y):
        """Two-tone Python badge at the Explorer's file-icon size."""
        self.fill("#58a6ff", x + 1, y + 1, 9, 7, 2)
        self.fill("#e3b341", x + 4, y + 7, 9, 7, 2)
        self.fill(CODE_BG, x + 6, y + 3, 1, 1)
        self.fill(CODE_BG, x + 9, y + 11, 1, 1)

    def code(self, window, x, y, w, h):
        # Only an immersive, comfortably wide editor gets the desktop-style
        # title bar. The editor embedded in lessons keeps its compact chrome.
        wide = self.presentation_app == "code" and w >= 870
        rail, tab_h, line_h = (48, 34, 20) if wide else (35, 28, 22)
        self.fill(CODE_BG, x, y, w, h)
        if wide:
            self.app_icon("code", x + 9, y + 7, 16)
            self.label("File  Edit  Selection  View  Go  Run  Terminal  Help", x + 35, y + 6, 12, CODE_FG, width=385)
            box_w = min(380, w * .34)
            box_x = x + (w - box_w) / 2 + 65
            self.fill(CODE_ROW, box_x, y + 4, box_w, 21, 5)
            self.label("⌕   " + (window.get("projectName") or "Visual Studio Code"),
                       box_x + 11, y + 6, 11, CODE_MUTED, width=box_w - 22)
            self.label("−      □      ×", x + w - 92, y + 5, 12, CODE_MUTED)
            self.fill(CODE_BORDER, x, y + 28, w, 1)
            y, h = y + 29, h - 29
        self.fill(CODE_BG, x, y, rail, max(1, h - 19))
        self.fill(CODE_BORDER, x + rail - 1, y, 1, h - 19)
        step = 44 if wide else 33
        for i, icon in enumerate(("▤", "⌕", "⑂", "▷", "▦")):
            if y + 28 + i * step >= y + h - 19:
                break
            active_icon = {"explorer": 0, "project-search": 1, "changes": 2, "extensions": 4}.get(window.get("view"), 0)
            if i == active_icon and window.get("sidebar", True):
                self.fill(CODE_FG if wide else "#f78166", x, y + i * step, 2, step - 1)
            self.label(icon, x + (12 if wide else 8), y + 9 + i * step, 21 if wide else 18,
                       CODE_FG if i == active_icon else CODE_MUTED)
        sidebar = (min(225, int(w * .24)) if wide else 155) if window.get("sidebar", True) and w > 450 else 0
        if sidebar:
            self.fill(CODE_INSET, x + rail, y, sidebar, h - 19)
            self.fill(CODE_BORDER, x + rail - 1 + sidebar, y, 1, h - 19)
            self.label({"extensions": "РАСШИРЕНИЯ", "project-search": "ПОИСК", "changes": "ИЗМЕНЕНИЯ"}.get(
                window.get("view"), "ПРОВОДНИК"), x + rail + 12, y + 10, 10, CODE_FG)
            if wide:
                self.label("···", x + rail + sidebar - 23, y + 8, 14, CODE_MUTED)
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
            project = window.get("projectName", "")
            self.label(("⌄  " + project.upper()) if view == "explorer" and project else "",
                       x + rail + 9, y + 35, 10, CODE_FG, bold=True, width=sidebar - 14)
            for i, name in enumerate(shown):
                if y + 80 + 22 * i >= y + h - 18:
                    break
                if view == "explorer" and name == window.get("projectRelative"):
                    self.fill(CODE_LIST_SELECTION, x + rail, y + 55 + 22 * i, sidebar - 2, 22)
                left = x + rail + 14
                if view == "explorer" and name.lower().endswith(".py"):
                    self.python_file_icon(left, y + 57 + 22 * i)
                    left += 17
                self.label(name, left, y + 57 + 22 * i, 12,
                           CODE_FG if name == window.get("projectRelative") else CODE_MUTED,
                           width=x + rail + sidebar - left - 4)
        editor_x = x + rail + sidebar
        editor_w = w - rail - sidebar
        self.fill(CODE_INSET, editor_x, y, editor_w, tab_h)
        self.fill(CODE_BORDER, editor_x, y + tab_h - 1, editor_w, 1)
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
            self.fill(CODE_BG if active else CODE_INSET, left, y, tab_width - 1, tab_h)
            self.fill(CODE_BORDER, left + tab_width - 1, y, 1, tab_h)
            if active:
                self.fill("#f78166", left, y, tab_width - 2, 2)
            tab_title = tab.get("title", "")
            text_x = left + 9
            if tab_title.lower().endswith(".py"):
                self.python_file_icon(text_x, y + (9 if wide else 6))
                text_x += 17
            self.label(tab_title, text_x, y + (8 if wide else 7), 12, CODE_FG if active else CODE_MUTED,
                       width=left + tab_width - text_x - 21)
            if active or tab.get("modified"):
                self.label("●" if tab.get("modified") else "×", left + tab_width - 18,
                           y + (7 if wide else 5), 12, CODE_MUTED)
        name = tabs[active_index]["title"] if tabs else ""
        split = window.get("split", False)
        pane = editor_w / 2 if split else editor_w
        current = editor_x + (pane if split and window.get("group") == 1 else 0)
        body_top = tab_h + 29
        lines_visible = max(1, int(((h * .54 if window.get("panel") else h - 20) - body_top + 1) / line_h))
        def document(path, body, left, active, syntax):
            self.label("  ›  ".join(path.split("/")), left + 12, y + tab_h + 5, 11, CODE_MUTED, width=pane - 22)
            cursor = window.get("bodyCursor", 0) if active else 0
            line_num = body.encode("utf-16-le")[:cursor * 2].decode("utf-16-le", "ignore").count("\n")
            first = max(0, line_num - lines_visible + 1)
            offset = 0
            field = dict(window, cursor=cursor, anchor=window.get("bodyAnchor", cursor) if active else cursor)
            lines = body.split("\n")
            for i, raw in enumerate(lines):
                line_break = 1 if i + 1 < len(lines) else 0
                line = raw
                if line_break and raw.endswith("\r"):
                    line_break = 2
                    line = raw[:-1]
                if i >= first + lines_visible:
                    break
                if i >= first:
                    ly = y + body_top + (i - first) * line_h
                    if i == line_num and active:
                        self.fill(CODE_ROW, left, ly - 2, pane, line_h)
                    self.label(str(i + 1).rjust(2), left + 9, ly, 11,
                               CODE_FG if active and i == line_num else CODE_LINE_NO, mono=True)
                    self.field(line, field, left + 43, ly, pane - 56, 14, CODE_FG, offset=offset,
                               line_break=line_break, syntax=syntax, selection_color=CODE_SELECTION, caret_color=CODE_FOCUS,
                               focused=active and (window.get("focus") == "body" or field["cursor"] != field["anchor"]))
                offset += self.units(line) + line_break
        document(window.get("projectRelative") or name, window.get("body", ""), current, True, window.get("syntax", ()))
        if split:
            other = window.get("otherTab") or {}
            document(other.get("title", ""), other.get("body", ""), editor_x if current != editor_x else editor_x + pane, False, other.get("syntax", ()))
            self.fill(CODE_BORDER, editor_x + pane, y + tab_h + 1, 1, max(1, (h * .54 if window.get("panel") else h - 19) - tab_h - 1))
        if window.get("panel"):
            self.fill(CODE_INSET, editor_x, y + h * .54, editor_w, h * .46 - 19)
            if self.scene.get("diagnosticWorkspace") and window.get("focus") != "terminal":
                self.label("ПРОБЛЕМЫ · F8", editor_x + 10, y + h * .54 + 6, 10, "#f3cc73", bold=True)
                problems = [item["title"] for item in window.get("entries", ()) if item.get("kind") == "problem"]
                if window.get("focus") != "code-quick-fix":
                    self.label(problems[0] if problems else "Нет диагностик", editor_x + 10, y + h * .54 + 26, 11, "#d1dceb", width=editor_w-20)
            else:
                self.label("ТЕРМИНАЛ     ВЫВОД     ПРОБЛЕМЫ", editor_x + 10, y + h * .54 + 6, 9)
                self.terminal(window, editor_x, y + h * .54 + 27, editor_w, h * .46 - 46, embedded=True)
        focus = window.get("focus", "editor")
        if focus == "code-quick-fix":
            choices = [item.get("title", "") for item in window.get("entries", ())]
            pw = min(w - 24, 480)
            px, py = x + (w - pw) / 2, y + 37
            self.fill(CODE_ROW, px, py, pw, 110, 7)
            self.label("Быстрые исправления", px + 10, py + 8, 13, "#eef1f8", width=pw - 20, bold=True)
            for index, title in enumerate(choices):
                row = py + 32 + 23 * index
                if index == window.get("optionIndex"):
                    self.fill(CODE_SELECTION, px + 5, row - 2, pw - 10, 22, 4)
                self.label(title, px + 10, row, 12, "#eef1f8", width=pw - 20)
            self.label("↑↓ — выбрать   Enter — применить   Esc — закрыть", px + 10, py + 84, 10, "#b4bfcd", width=pw-20)
        elif focus in ("symbol-references", "symbol-rename", "project-results"):
            search_results = focus == "project-results"
            references = focus == "symbol-references" or search_results
            choices = [item.get("title", "") for item in window.get("entries", [])] if references else []
            selected = min(max(0, window.get("optionIndex", 0)), max(0, len(choices) - 1))
            visible = min(6, len(choices), max(1, int((h - 100) / 25)))
            first = max(0, selected - visible + 1)
            pw = min(620, w - 24)
            px = x + (w - pw) / 2
            py = y + 34
            self.fill("#121419", px - 1, py + 2, pw + 2, 76 + visible * 25, 8)
            self.fill(CODE_ROW, px, py, pw, 76 + visible * 25, 8)
            title = (f"Поиск «{window.get('projectSearchQuery', '')}» · {len(choices)}" if search_results else
                     f"Использования · {len(choices)}" if references else "Переименовать символ")
            self.label(title, px + 12, py + 8, 13, "#eef1f8", width=pw - 24, bold=True)
            if references:
                for row, index in enumerate(range(first, min(len(choices), first + visible))):
                    iy = py + 34 + row * 25
                    if index == selected:
                        self.fill(CODE_SELECTION, px + 6, iy - 2, pw - 12, 23, 4)
                    self.label(choices[index], px + 13, iy, 12, "#eef1f8", width=pw - 26)
                if not choices:
                    self.label("Совпадений нет" if search_results else "Использования не найдены", px + 12, py + 32, 11, "#b4bfcd", width=pw - 24)
                tip = "↑↓ — выбрать   Enter — открыть   Esc — закрыть"
            else:
                self.fill(CODE_INSET, px + 7, py + 28, pw - 14, 22, 4)
                self.field(window.get("input", ""), window, px + 12, py + 30, pw - 24, 13, "#eef1f8")
                tip = "Enter — переименовать   Esc — отменить"
            self.label(tip, px + 12, py + 55 + visible * 25, 10, "#b4bfcd", width=pw - 24)
        elif focus in ("palette", "clone-url", "quick-open", "symbols", "find", "replace-find", "replace-with", "project-search", "goto-line"):
            titles = {"palette": "Палитра команд", "clone-url": "Git: Clone · HTTPS-ссылка (учебная копия)", "quick-open": "Открыть файл", "symbols": "Символы файла",
                      "find": "Найти", "replace-find": "Найти для замены", "replace-with": "Заменить на",
                      "project-search": "Поиск по проекту", "goto-line": "Перейти к строке"}
            px, pw = x + w * .17, w * .66
            choices = [e["title"] for e in window.get("entries", [])] if focus in ("quick-open", "symbols") else []
            if focus == "palette":
                choices = ["Git: Clone"] if window.get("browserPractice") else ["Форматировать документ", "Переключить боковую панель", "Разделить редактор"]
            selected = window.get("optionIndex", 0)
            visible = min(6, len(choices), max(1, int((h - 72) / 25)))
            first = max(0, selected - visible + 1)
            self.fill("#141519", px - 1, y + 6, pw + 2, 52 + visible * 25, 7)
            self.fill(CODE_ROW, px, y + 5, pw, 51 + visible * 25, 7)
            title = titles[focus]
            if focus == "find" and window.get("status", "").startswith("Совпадение "):
                title += " · " + window["status"]
            self.label(title, px + 12, y + 10, 10, "#b4bfcd")
            self.field(window.get("input", ""), window, px + 12, y + 26, pw - 24, 13, "#eef1f8", mono=False)
            for index in range(first, min(len(choices), first + visible)):
                iy = y + 52 + (index - first) * 25
                if index == selected:
                    self.fill(CODE_SELECTION, px + 5, iy - 2, pw - 10, 24, 4)
                self.label(choices[index], px + 14, iy, 13, width=pw - 28)
        self.fill(CODE_BG, x, y + h - 19, w, 19)
        self.fill(CODE_BORDER, x, y + h - 19, w, 1)
        self.label("⑂ main    ✓ 0   ⚠ " + str(self.scene.get("diagnosticProblemCount", 0)), x + 9, y + h - 16, 9, CODE_MUTED)
        before = window.get("body", "").encode("utf-16-le")[:window.get("bodyCursor", 0) * 2].decode("utf-16-le", "ignore")
        line, col = before.count("\n") + 1, self.units(before.rsplit("\n", 1)[-1]) + 1
        support = "Python" if name.lower().endswith(".py") else "Текст"
        status = f"Стр {line}, стлб {col}    UTF-8    {support}    {self.scene.get('layout', 'EN')}"
        self.label(status, x + max(105, w - 265), y + h - 16, 9, CODE_MUTED, width=min(w - 110, 260))

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
