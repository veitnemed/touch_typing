#!/usr/bin/python3
import argparse
import ctypes
import math

import cairo
from datetime import datetime
import os
from pathlib import Path
import sys

import gi
gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("Graphene", "1.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import Gdk, Gio, GLib, Graphene, Gtk, Pango, PangoCairo

from platform_linux import APP_ID, Engine, EngineError, FINGERS, InputSources, MODIFIERS, PHYSICAL, ROOT, chord_label, key_label
from lab_view import DesktopView
from visuals import PracticeLine, fingers as paint_fingers, hand_picture, series_chart


_reference_fonts_loaded = False


def load_reference_fonts():
    """Register shipped UI faces for this process only; don't install system fonts."""
    global _reference_fonts_loaded
    if _reference_fonts_loaded:
        return
    _reference_fonts_loaded = True
    candidates = (ROOT / "fonts", ROOT.parent / "assets" / "fonts")
    directory = next((path for path in candidates if path.is_dir()), None)
    files = [directory / f"Roboto-{weight}.ttf" for weight in ("Regular", "Medium", "Bold")] if directory else []
    if not files:
        return
    try:
        # Ubuntu 24.04 Pango 1.52 has no add_font_file. Its Cairo font map
        # consumes the current process's Fontconfig application set.
        if Pango.version() < 15600:
            fontconfig = ctypes.CDLL("libfontconfig.so.1")
            add = fontconfig.FcConfigAppFontAddFile
            add.argtypes = (ctypes.c_void_p, ctypes.c_char_p)
            add.restype = ctypes.c_int
            for path in files:
                if not add(None, os.fsencode(path)):
                    print(f"Не удалось загрузить шрифт {path.name}.", file=sys.stderr)
        else:
            fontmap = PangoCairo.FontMap.get_default()
            for path in files:
                if not fontmap.add_font_file(str(path)):
                    print(f"Не удалось загрузить шрифт {path.name}.", file=sys.stderr)
    except (OSError, AttributeError, GLib.Error) as exc:
        print(f"Не удалось загрузить шрифты оформления: {exc}", file=sys.stderr)


def box(direction=Gtk.Orientation.VERTICAL, gap=8, *styles):
    widget = Gtk.Box(orientation=direction, spacing=gap)
    for style in styles:
        widget.add_css_class(style)
    return widget


def row(gap=8, *styles):
    return box(Gtk.Orientation.HORIZONTAL, gap, *styles)


def text(value="", *styles, wrap=False):
    widget = Gtk.Label(label=value, xalign=0)
    widget.set_wrap(wrap)
    widget.set_wrap_mode(Pango.WrapMode.WORD_CHAR)
    for style in styles:
        widget.add_css_class(style)
    return widget


def button(name, callback, *styles):
    widget = Gtk.Button(label=name)
    for style in styles:
        widget.add_css_class(style)
    widget.connect("clicked", lambda *_: callback())
    return widget


def add(parent, *children):
    for child in children:
        parent.append(child)
    return parent


def expand(widget, vertical=False):
    widget.set_vexpand(True) if vertical else widget.set_hexpand(True)
    return widget


def clear(parent):
    while child := parent.get_first_child():
        parent.remove(child)


def dropdown(names, change=None):
    widget = Gtk.DropDown.new_from_strings(names)
    widget.set_enable_search(len(names) > 8)
    if change:
        widget.connect("notify::selected", lambda *_: change())
    return widget


def scroll(widget, height=0):
    scroller = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.NEVER)
    scroller.set_min_content_height(height)
    scroller.set_child(widget)
    return scroller


def card(*styles, gap=9):
    return box(Gtk.Orientation.VERTICAL, gap, "card", *styles)


def metric(parent, title, value="—"):
    widget = expand(card(gap=4))
    number = text(value, "metric-value")
    add(widget, text(title, "muted"), number)
    parent.append(widget)
    return number


def stamina_modal(title, subtitle, metrics):
    """A compact, keyboard-first result and pause surface shared by lessons."""
    panel = box(Gtk.Orientation.VERTICAL, 11, "stamina-modal")
    panel.set_size_request(664, -1)
    panel.set_halign(Gtk.Align.CENTER)
    panel.set_valign(Gtk.Align.CENTER)
    heading = text(title, "stamina-modal-title")
    description = text(subtitle, "stamina-modal-subtitle", wrap=True)
    add(panel, heading, Gtk.Separator(orientation=Gtk.Orientation.HORIZONTAL), description)
    if metrics:
        strip = Gtk.Grid(column_homogeneous=True, column_spacing=7)
        strip.add_css_class("stamina-metrics")
        for index, (icon, name, value, tone) in enumerate(metrics):
            item = row(7, "stamina-metric", tone)
            asset = ROOT / f'result-{tone.removeprefix("tone-")}.png'
            if asset.is_file():
                glyph = Gtk.Picture.new_for_filename(str(asset))
                glyph.set_content_fit(Gtk.ContentFit.CONTAIN)
                glyph.set_can_shrink(True)
                glyph.set_size_request(40, 40)
                glyph.add_css_class("stamina-metric-icon")
            else:
                glyph = text(icon, "stamina-metric-icon")
                glyph.set_xalign(.5)
            words = box(gap=1)
            label = text(name, "stamina-metric-label")
            number = text(str(value), "stamina-metric-value")
            add(words, label, number)
            add(item, glyph, words)
            strip.attach(item, index, 0, 1, 1)
        panel.append(strip)
    return panel


def needs_fn(key, profile):
    return (profile.get("keyboardProfile") == "matebook-d16" and
            profile.get("fnMode") == "hotkeys" and key.startswith("F") and key[1:].isdigit() and
            1 <= int(key[1:]) <= 12)


def physical_key_hint(key, profile):
    return ("Fn + " if needs_fn(key, profile) else "") + key_label(key)


def display_chord(chord, profile):
    parts = []
    for part in chord.split(" → "):
        keys = part.split("+") if part else []
        labels = [key_label(key) for key in keys]
        if any(needs_fn(key, profile) for key in keys):
            labels.insert(0, "Fn")
        parts.append(" + ".join(labels))
    return " → ".join(parts)


def bind_name(widget, name):
    widget.update_property([Gtk.AccessibleProperty.LABEL], [name])
    return widget


def inside_widget(widget, ancestor):
    while widget:
        if widget is ancestor:
            return True
        widget = widget.get_parent()
    return False


class KeyboardView(Gtk.Box):
    def __init__(self, rows, function_row=True):
        super().__init__(orientation=Gtk.Orientation.VERTICAL, spacing=1)
        self.add_css_class("keyboard")
        self.cells = {}
        functions = [{"key": k, "units": 1, "en": k, "ru": k} for k in
                     ("Esc", *[f"F{i}" for i in range(1, 13)], "Print")]
        def extra(key, label):
            return {"key": key, "units": 1.25, "en": label, "ru": label}
        for items in ([functions] if function_row else []) + list(rows):
            caps = []
            for cap in items:
                if not function_row and cap["key"] in ("Up", "Down", "Left", "Right"):
                    continue
                caps.append(cap)
                if cap["key"] == "RAlt":
                    if not function_row:
                        caps.append(extra("Menu", "Menu"))
            line = Gtk.Grid(column_homogeneous=True)
            units = sum(item["units"] for item in caps)
            start, used = 0, 0
            for item in caps:
                key = item["key"]
                special = len(key) > 1
                cell = text("", "cap")
                if special and key != "Space":
                    cell.add_css_class("special")
                if key == "LSuper":
                    cell.add_css_class("windows-key")
                if item.get("anchor"):
                    cell.add_css_class("anchor")
                cell.set_xalign(.5)
                cell.set_ellipsize(Pango.EllipsizeMode.END)
                used += item["units"]
                end = round(60 * used / units)
                line.attach(cell, start, 0, end - start, 1)
                start = end
                self.cells[key] = (cell, item)
            self.append(line)

    def paint(self, layout, keys=(), held=(), miss=None, finger_map=None, profile=None):
        finger_map = finger_map or {}
        profile = profile or {}
        matebook = profile.get("keyboardProfile") == "matebook-d16"
        for key, (cell, cap) in self.cells.items():
            if key == "Print":
                cell.set_visible(not matebook)
            color_class = f'finger-{finger_map.get(key, cap.get("finger", ""))}'
            for style in cell.get_css_classes():
                if style.startswith("finger-") and style != color_class:
                    cell.remove_css_class(style)
            if color_class != "finger-":
                cell.add_css_class(color_class)
            caption = cap["ru" if layout == "RU" else "en"]
            label = {"LSuper": "⊞", "RSuper": "⊞", "Space": "", "Print": "PrtSc",
                     "CapsLock": "Caps Lock"}.get(key, caption)
            if key == "F11" and matebook:
                label = "F11\nPrtSc"
                cell.add_css_class("dual-function")
            else:
                cell.remove_css_class("dual-function")
            cell.set_label(label)
            if needs_fn(key, profile):
                cell.set_tooltip_text(physical_key_hint(key, profile) +
                                      (" · полноэкранный режим" if key == "F11" else ""))
            elif key == "Print" and profile.get("keyboardProfile") == "matebook-d16" and profile.get("fnMode") == "hotkeys":
                cell.set_tooltip_text("PrtSc · снимок экрана без Fn")
            else:
                cell.set_tooltip_text(key_label(key))
            same_key = ("F11", "Print") if key == "F11" and matebook else (key,)
            for name, enabled in (("target", any(item in keys for item in same_key)),
                                  ("held", any(item in held for item in same_key)),
                                  ("miss", miss in same_key)):
                if enabled:
                    cell.add_css_class(name)
                else:
                    cell.remove_css_class(name)


class LessonGuide(Gtk.Box):
    def __init__(self, rows, function_row=True):
        super().__init__(orientation=Gtk.Orientation.VERTICAL, spacing=4)
        self.add_css_class("lesson-guide")
        self.board = KeyboardView(rows, function_row)
        self.board.add_css_class("key-deck")
        self.board.set_hexpand(False)
        self.board.set_valign(Gtk.Align.CENTER)
        self.hands = {side: hand_picture(side) for side in ("left", "right")}
        self.panel = row(24)
        self.panel.set_halign(Gtk.Align.CENTER)
        add(self.panel, self.hands["left"], self.board, self.hands["right"])
        self.label = text("", "dim", "finger-caption")
        self.label.set_ellipsize(Pango.EllipsizeMode.END)
        self.label.set_xalign(.5)
        self.append(self.panel)
        self.append(self.label)
        self.compact = None
        self.show_keys = self.show_hands = True
        self.chosen = ()
        self.reflow(1140)

    def reflow(self, width):
        compact = width < 1040
        if compact == self.compact:
            return
        self.compact = compact
        self.board.set_size_request(590 if compact else 636, -1)
        self.panel.set_spacing(18 if compact else 28)
        self.update_hands()

    def update_hands(self):
        for side, picture in self.hands.items():
            picture.set_size_request(108 if self.compact else 134, 118 if self.compact else 146)
            paint_fingers(picture, self.chosen, side, self.compact)

    def paint(self, profile, layout, targets=(), held=(), miss=None, notes="", chosen=()):
        self.show_keys, self.show_hands = profile["showKeys"], profile["showHands"]
        self.board.paint(layout, targets, held, miss, targets if isinstance(targets, dict) else None, profile)
        self.board.set_visible(self.show_keys)
        for picture in self.hands.values():
            picture.set_visible(profile["showHands"])
        if self.chosen != tuple(chosen):
            self.chosen = tuple(chosen)
            self.update_hands()
        self.label.set_label(notes)
        self.label.set_tooltip_text(notes or None)
        self.label.set_visible(bool(notes) and profile["showHands"])

        self.set_visible(profile["showKeys"] or profile["showHands"])


def draw_reference_wave(_widget, cr, width, height):
    # The decorative sweep is generated as vector paths; it does not carry
    # any text/controls or make the supplied mock part of the application.
    scale = width / 1006
    cr.save()
    cr.scale(scale, 1)
    def ribbon(line=0):
        cr.move_to(502, 204 + line)
        cr.curve_to(602, 151 + line, 656, 158 + line, 716, 133 + line)
        cr.curve_to(789, 101 + line, 807, 65 + line, 878, 42 + line)
        cr.curve_to(920, 23 + line, 966, 26 + line, 1006, 42 + line)

    def solid_shape():
        ribbon()
        cr.line_to(1006, 128)
        cr.curve_to(943, 92, 881, 125, 842, 152)
        cr.curve_to(768, 207, 716, 173, 644, 203)
        cr.curve_to(570, 226, 542, 220, 502, 204)
        cr.close_path()

    solid_shape()
    upper = cairo.LinearGradient(0, 30, 0, 205)
    upper.add_color_stop_rgba(0, .24, .13, .53, .33)
    upper.add_color_stop_rgba(.6, .19, .10, .49, .40)
    upper.add_color_stop_rgba(1, .16, .09, .42, .13)
    cr.set_source(upper)
    cr.fill()

    # Additive thin translucent copies keep the highlight close to the curve
    # rather than creating a diagonal band across the whole coloured region.
    cr.save()
    cr.rectangle(0, 0, 1006, height)
    cr.clip()
    cr.set_operator(cairo.OPERATOR_ADD)
    for line in range(-72, 77):
        thickness = 24 if line < 0 else 34
        a = math.exp(-(line / thickness)**2)
        glow = cairo.LinearGradient(510, 0, 1006, 0)
        for stop, strength in ((0, 0), (.23, .14), (.43, .36), (.61, .97), (.74, .8), (1, .045)):
            glow.add_color_stop_rgba(stop, .60, .31, .98, a * strength * .17)
        cr.set_source(glow)
        ribbon(line)
        cr.set_line_width(3.0)
        cr.stroke()
    cr.restore()

    crest = cairo.LinearGradient(502, 204, 1006, 42)
    crest.add_color_stop_rgba(0, .28, .12, .64, 0)
    crest.add_color_stop_rgba(.5, .58, .24, 1., .45)
    crest.add_color_stop_rgba(.76, .74, .46, 1., .92)
    crest.add_color_stop_rgba(1, .44, .25, .81, .33)
    cr.set_source(crest)
    ribbon()
    cr.set_line_width(1.45)
    cr.stroke()
    cr.restore()


class ReferenceCanvas(Gtk.Widget):
    """Snapshot authored Cairo vectors without a cairo GI callback dependency."""
    def __init__(self, draw, size=0, max_height=0):
        super().__init__()
        self.draw_vector, self.vector_size, self.max_height = draw, size, max_height
        self.cached_key = self.cached_texture = None
        self.set_can_target(False)

    def do_measure(self, _orientation, _constraint):
        return self.vector_size, self.vector_size, -1, -1

    def do_snapshot(self, snapshot):
        width, height = self.get_width(), self.get_height()
        if self.max_height:
            height = min(height, self.max_height)
        if not width or not height:
            return
        color = self.get_style_context().get_color().to_string()
        key = width, height, self.get_scale_factor(), color
        if key != self.cached_key:
            scale = key[2]
            surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, width * scale, height * scale)
            context = cairo.Context(surface)
            context.scale(scale, scale)
            self.draw_vector(self, context, width, height)
            surface.flush()
            fmt = (Gdk.MemoryFormat.B8G8R8A8_PREMULTIPLIED if sys.byteorder == "little"
                   else Gdk.MemoryFormat.A8R8G8B8_PREMULTIPLIED)
            self.cached_texture = Gdk.MemoryTexture.new(width * scale, height * scale, fmt,
                GLib.Bytes.new(bytes(surface.get_data())), surface.get_stride())
            self.cached_key = key
        snapshot.append_texture(self.cached_texture, Graphene.Rect().init(0, 0, width, height))


class ReferenceGlyph(ReferenceCanvas):
    """Small native vector glyph. Names and focus belong to the owning button."""
    def __init__(self, name, size):
        self.name = name
        super().__init__(self.draw, size)

    def draw(self, widget, cr, width, height):
        size = min(width, height)
        cr.translate((width - size) / 2, (height - size) / 2)
        cr.scale(size / 48, size / 48)
        cr.set_line_width(4.3)
        cr.set_line_cap(cairo.LINE_CAP_ROUND)
        cr.set_line_join(cairo.LINE_JOIN_ROUND)
        def rgb(red, green, blue):
            cr.set_source_rgb(red, green, blue)
        def polygon(*points):
            cr.move_to(*points[0])
            for point in points[1:]:
                cr.line_to(*point)
            cr.close_path()
            cr.fill()
        def line(*points):
            cr.move_to(*points[0])
            for point in points[1:]:
                cr.line_to(*point)
            cr.stroke()
        def circle(x, y, radius, solid=True):
            cr.arc(x, y, radius, 0, math.tau)
            cr.fill() if solid else cr.stroke()
        def rounded(x, y, w, h, r):
            cr.new_sub_path()
            for cx, cy, a in ((x+w-r, y+r, -90), (x+w-r, y+h-r, 0),
                              (x+r, y+h-r, 90), (x+r, y+r, 180)):
                cr.arc(cx, cy, r, math.radians(a), math.radians(a+90))
            cr.close_path()

        color = widget.get_style_context().get_color()
        cr.set_source_rgba(color.red, color.green, color.blue, color.alpha)
        name = self.name
        if name == "windows":
            rgb(.44, .79, 1)
            for row in range(2):
                for col in range(2):
                    cr.rectangle(10 + col * 15, 10 + row * 15, 13, 13)
                    cr.fill()
        elif name == "ubuntu":
            rgb(1, .47, 0)
            cr.set_line_width(5)
            for angle in (23, 143, 263):
                cr.arc(24, 24, 12.4, math.radians(angle+12), math.radians(angle+106))
                cr.stroke()
                theta = math.radians(angle)
                circle(24+18*math.cos(theta), 24+18*math.sin(theta), 4)
        elif name == "chrome":
            for (r, g, b), begin, end in (((.93, .24, .14), 210, 335), ((.99, .75, .04), 335, 450),
                                          ((.01, .62, .27), 90, 210)):
                rgb(r,g,b)
                cr.move_to(24,24)
                cr.arc(24, 24, 24, math.radians(begin), math.radians(end))
                cr.close_path()
                cr.fill()
            rgb(.98,.99,1); circle(24,24,11.2)
            rgb(.15,.50,.94); circle(24,24,9.4)
        elif name == "code":
            rgb(0, .48, .83)
            polygon((3,13),(9,10),(34,29),(34,12),(42,6),(42,42),(34,39),(4,19))
            rgb(0, .65, .92)
            polygon((34,8),(43,4),(47,8),(47,39),(43,43),(34,39),(34,21),(9,36),(3,31))
            rgb(.15, .77, .97)
            polygon((43,4),(47,8),(47,39),(43,43))
        elif name == "terminal":
            rgb(.99,.97,1)
            line((7,11),(19,24),(7,37))
            line((24,36),(42,36))
        elif name == "workday":
            rgb(.42,1,.51)
            cr.set_line_width(4)
            rounded(6,16,36,25,4); cr.stroke()
            line((16,16),(16,11),(19,8),(29,8),(32,11),(32,16))
        elif name == "bars":
            for x, y in ((4,28),(19,17),(34,6)):
                cr.rectangle(x,y,9,42-y)
            cr.fill()
        elif name == "home":
            line((5,22),(24,6),(43,22),(38,22),(38,43),(29,43),(29,30),(19,30),(19,43),(10,43),(10,22),(5,22))
        elif name == "practice":
            circle(24,24,19,False); circle(24,24,11,False)
            line((24,24),(38,11),(41,5))
        elif name == "book":
            cr.move_to(4,9); cr.curve_to(12,5,19,6,24,11); cr.curve_to(30,6,38,5,44,9)
            cr.line_to(44,40); cr.curve_to(36,36,29,36,24,41); cr.curve_to(19,36,11,36,4,40)
            cr.close_path(); cr.stroke()
            line((24,12),(24,40))
        elif name == "time":
            circle(24,27,14,False)
            line((24,26),(24,17))
            line((22,7),(26,7))
            line((24,8),(24,12))
        elif name == "count":
            rounded(4,10,40,28,4); cr.stroke()
            for y in (17,24,31):
                for x in (12,20,28,36):
                    circle(x,y,1.65)
        elif name == "speed":
            cr.arc(24,29,16,math.radians(146),math.radians(393)); cr.stroke()
            line((24,29),(33,21))
            for angle in (175,220,270,319):
                t=math.radians(angle)
                line((24+11*math.cos(t),29+11*math.sin(t)),(24+13*math.cos(t),29+13*math.sin(t)))
        elif name == "check":
            rgb(.36, 1., .46)
            line((8,25),(20,37),(41,14))
        elif name == "moon":
            cr.move_to(33,5); cr.curve_to(18,5,11,13,11,25); cr.curve_to(11,39,28,46,41,36)
            cr.curve_to(25,37,18,20,33,5); cr.close_path(); cr.fill()


class Studio(Gtk.Application):
    def __init__(self, smoke=None, smoke_size=None, smoke_live=False):
        load_reference_fonts()
        flags = Gio.ApplicationFlags.NON_UNIQUE if smoke else Gio.ApplicationFlags.DEFAULT_FLAGS
        super().__init__(application_id=APP_ID, flags=flags)
        self.smoke = smoke
        self.window_size = smoke_size or ((1508, 850) if smoke else (1140, 710))
        self.smoke_live = smoke_live
        self.smoke_failure = False
        self.engine = None
        self.lab = None
        self.check_process = None
        self.window = None
        self.workout = None
        self.result_kind = None
        self.state = None
        self.mode = "shortcuts"
        self.group = "Терминал"
        self.skill_id = "terminal-paste"
        self.recording = False
        self.pressed = set()
        self.capture_keys = []
        self.capture_surface = None
        self.capture_handler = None
        self.capture_requested = False
        self.capture_timeout = 0
        self.capture_dialog_seen = False
        self.request_exact = False
        self.capture_note = ""
        self.allow_close = False
        self.close_dialog = None
        self.guide = None
        self.paused = False
        self.typing_choice = "Russian"

    def do_activate(self):
        if self.window:
            self.window.present()
            return
        self.engine = Engine(bool(self.smoke))
        self.sources = InputSources(Gio)
        self.layout = self.sources.guess()
        forward, backward = self.sources.bindings()
        self.library = self.engine.call("configure", layout=self.layout, forward=forward, backward=backward,
                                        dock=self.sources.dock(), desktopBindings=self.sources.desktop_bindings())
        provider = Gtk.CssProvider()
        provider.load_from_path(str(ROOT / "style.css"))
        Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        self.window = Gtk.ApplicationWindow(application=self, title="Код вслепую — Linux")
        self.window.set_default_size(*self.window_size)
        self.window.add_css_class("reference-window")
        self.window.set_size_request(960, 600)
        self.build_header()
        self.window.connect("close-request", self.close_request)
        self.window.connect("notify::is-active", self.focus_changed)
        controller = Gtk.EventControllerKey()
        controller.set_propagation_phase(Gtk.PropagationPhase.CAPTURE)
        controller.connect("key-pressed", self.key_pressed)
        controller.connect("key-released", self.key_released)
        self.window.add_controller(controller)

        main = box(gap=0)
        self.banner = text("", "bad", wrap=True)
        self.banner.set_margin_top(6)
        self.banner.set_margin_start(24)
        self.banner.set_visible(False)
        self.body = expand(box(gap=0), True)
        add(main, self.banner, self.body)
        main.add_css_class("shell")
        self.window.set_child(main)
        self.navigate("shortcuts" if self.smoke else "home")
        if not self.smoke:
            self.window.maximize()
        self.window.present()
        if self.smoke:
            self.start_button.grab_focus()
        GLib.timeout_add(200, self.tick)
        if self.smoke:
            GLib.timeout_add(450, self.smoke_test)

    # Visible header order follows the design. Retain the existing Alt
    # bindings (especially Alt+1 for Live) independently of visual order.
    NAV_SHORTCUTS = {"0": "home", "1": "lab", "2": "typing", "3": "editor", "4": "progress"}
    HOME_MODULES = (
        ("ubuntu", "Ubuntu", "Работа в Linux: окна, файлы,\nприложения, система."),
        ("chrome", "Chrome", "Эффективная работа\nв браузере без мыши."),
        ("code", "VS Code", "Навигация, редактирование\nи управление проектом."),
        ("terminal", "Терминал", "Быстрая работа в консоли:\nкоманды, файлы, процессы."),
        ("workday", "Рабочий день", "Реальные сценарии:\nпереключение, поиск, организация."),
    )

    def build_header(self):
        handle = Gtk.WindowHandle()
        bar = row(7, "reference-header")
        handle.set_child(bar)
        brand = button("", lambda: self.navigate("home"), "reference-brand")
        brand.set_child(add(row(14), text("</>", "reference-mark"), text("КОД ВСЛЕПУЮ", "reference-wordmark")))
        bind_name(brand, "Код вслепую, главная")
        brand.set_valign(Gtk.Align.CENTER)
        bar.append(brand)
        self.tabs = row(9, "reference-navigation")
        self.navigation = {}
        for page, glyph, title, key in (
            ("home", "home", "Главная", "0"), ("lab", "practice", "Практика", "1"),
            ("typing", "book", "Обучение", "2"), ("progress", "bars", "Прогресс", "4"),
        ):
            nav = button("", lambda dest=page: self.navigate(dest), "reference-nav")
            nav.set_child(add(row(7), ReferenceGlyph(glyph, 19), text(title)))
            bind_name(nav, f"{title} Alt+{key}")
            nav.set_tooltip_text("Alt+" + key)
            self.tabs.append(nav)
            self.navigation[page] = nav
        self.tabs.set_valign(Gtk.Align.CENTER)
        bar.append(self.tabs)
        bar.append(expand(box()))
        self.layout_tag = row(4, "reference-layouts")
        self.layout_chips = {lang: text(lang, "reference-layout") for lang in ("EN", "RU")}
        add(self.layout_tag, *self.layout_chips.values())
        self.show_layout()
        self.layout_tag.set_valign(Gtk.Align.CENTER)
        self.layout_tag.set_tooltip_text("Активная раскладка клавиатуры. Переключите её системным сочетанием.")
        bar.append(self.layout_tag)
        theme = button("", self.toggle_reference_theme, "reference-icon-control")
        theme.set_child(ReferenceGlyph("moon", 18))
        bind_name(theme, "Сменить оформление")
        theme.set_tooltip_text("Переключить тёмное и светлое оформление")
        theme.set_valign(Gtk.Align.CENTER)
        bar.append(theme)
        controls = row(1, "reference-window-controls")
        controls.set_valign(Gtk.Align.CENTER)
        maximize = lambda: self.window.unmaximize() if self.window.is_maximized() else self.window.maximize()
        for caption, name, action in (("−", "Свернуть", self.window.minimize), ("□", "Развернуть", maximize),
                                      ("×", "Закрыть", self.window.close)):
            control = button(caption, action)
            bind_name(control, name)
            control.set_tooltip_text(name)
            controls.append(control)
        bar.append(controls)
        self.reference_title_handle = handle
        self.reference_home_header = bar
        self.window.set_titlebar(handle)

    def set_lesson_header(self, active=False):
        self.window.set_size_request(*((606, 455) if active else (960, 600)))
        self.reference_title_handle.set_visible(not active)

    def show_layout(self):
        for name, chip in self.layout_chips.items():
            if name == self.layout:
                chip.add_css_class("active")
            else:
                chip.remove_css_class("active")

    def toggle_reference_theme(self):
        if self.window.has_css_class("reference-light"):
            self.window.remove_css_class("reference-light")
        else:
            self.window.add_css_class("reference-light")

    def lab_desktop_profile(self):
        return "windows" if self.library.get("desktopProfile") == "windows" else "ubuntu"

    def open_reference_module(self, module):
        self.navigate("lab")
        if self.lab is None:
            return
        index = next((i for i, item in enumerate(self.lab.classes) if item["id"] == module), None)
        if index is not None:
            self.lab.picker.set_selected(index)
            self.lab.begin.grab_focus()

    @staticmethod
    def module_progress(module, classes, skills):
        shortcuts = {task["id"] for item in classes if item["id"] == module for task in item["shortcuts"]}
        prefix = f"lab/{module}/"
        confirmed = {item["key"][len(prefix):] for item in skills if item.get("confirmed") and item["key"].startswith(prefix)}
        return (len(shortcuts & confirmed), len(shortcuts))

    def build_home(self):
        scene = Gtk.Overlay()
        scene.add_css_class("reference-home")
        scene.set_vexpand(True)
        backdrop = ReferenceCanvas(draw_reference_wave, max_height=245)
        scene.set_child(backdrop)
        board = box(Gtk.Orientation.VERTICAL, 26, "reference-board")
        board.set_valign(Gtk.Align.START)
        hero = box(Gtk.Orientation.VERTICAL, 6, "reference-hero")
        add(hero, text("Практика без мыши", "reference-hero-title"),
            text("Управляйте системой, приложениями и рабочими задачами\nтолько с клавиатуры.",
                 "reference-hero-subtitle", wrap=True))
        board.append(hero)
        modules = Gtk.Grid(column_homogeneous=True, column_spacing=0, row_spacing=23)
        modules.add_css_class("reference-modules")
        catalog = self.call("lab-catalog") or {}
        classes = catalog.get("classes", ())
        skills = self.library.get("liveSkills", ())
        self.home_cards = {}
        for index, (module, title, description) in enumerate(self.HOME_MODULES):
            if module == "ubuntu" and self.lab_desktop_profile() == "windows":
                module, title = "windows", "Windows 10"
                description = "Работа в Windows: Пуск,\nокна, рабочие столы."
            tile = button("", lambda dest=module: self.open_reference_module(dest), "reference-card", "module-" + module)
            if index == 0:
                tile.add_css_class("reference-selected")
            if index > 2:
                tile.add_css_class("reference-card-lower")
            tile.set_tooltip_text(f"Открыть класс «{title}». Прогресс: подтверждённые сочетания из каталога для текущей раскладки.")
            inner = box(Gtk.Orientation.VERTICAL, 0, "reference-card-inner")
            picture = ReferenceGlyph(module, 43 if index > 2 else 48)
            picture.add_css_class("reference-app-icon")
            picture.set_halign(Gtk.Align.START)
            tally, total = self.module_progress(module, classes, skills)
            fraction = tally / total if total else 0
            bind_name(tile, f"{title}. Прогресс: {fraction:.0%}. Открыть класс.")
            label = text(f"Прогресс: {fraction:.0%}", "reference-progress-label")
            status = add(row(12, "reference-progress"), ReferenceGlyph("bars", 19), label)
            meter = Gtk.ProgressBar(fraction=fraction)
            meter.add_css_class("reference-meter")
            details = text(description, "reference-card-description", wrap=True)
            details.set_max_width_chars(34)
            add(inner, picture, text(title, "reference-card-title"), details, expand(box(), True), status, meter)
            tile.set_child(inner)
            self.home_cards[module] = tile
            x, y, span, left, right = [(0, 0, 16, 0, 20), (16, 0, 15, 0, 20), (31, 0, 14, 0, 0),
                                       (0, 1, 17, 9, 8), (17, 1, 19, 20, 23)][index]
            tile.set_margin_start(left)
            tile.set_margin_end(right)
            tile.set_vexpand(False)
            modules.attach(tile, x, y, span, 1)
        modules.set_vexpand(False)
        board.append(modules)
        scene.add_overlay(board)
        scene.set_measure_overlay(board, True)
        self.body.append(expand(scroll(scene), True))

    def run_checks(self, control, status):
        if self.check_process:
            status.set_label("Автопроверка уже выполняется.")
            return
        data = Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local/share")
        output = data / "TouchTypingStudio/checks" / datetime.now().strftime("%Y%m%d-%H%M%S")
        runner = ROOT / "check.sh"
        try:
            self.check_process = Gio.Subprocess.new([str(runner), "--timed", "--output", str(output)],
                                                   Gio.SubprocessFlags.STDOUT_SILENCE | Gio.SubprocessFlags.STDERR_SILENCE)
        except GLib.Error as error:
            status.set_label(str(error))
            return
        control.set_sensitive(False)
        status.set_label("Автопроверка выполняется в отдельном сеансе. Полный прогон занимает около четырёх минут.")
        def completed(process, response):
            try:
                success = process.wait_check_finish(response)
            except GLib.Error:
                success = False
            self.check_process = None
            control.set_sensitive(True)
            status.set_label(("Проверки пройдены. " if success else "Обнаружен сбой. ") + "Отчёт " + str(output / "report.json"))
        self.check_process.wait_check_async(None, completed)

    def message(self, value=""):
        self.banner.set_label(value or "")
        self.banner.set_visible(bool(value))

    def call(self, operation, **payload):
        try:
            result = self.engine.call(operation, **payload)
            if self.engine.storage_error:
                self.message(self.engine.storage_error)
            return result
        except EngineError as exc:
            self.message(str(exc))
            return None

    def refresh(self):
        forward, backward = self.sources.bindings()
        result = self.call("configure", layout=self.layout, forward=forward, backward=backward,
                           dock=self.sources.dock(), desktopBindings=self.sources.desktop_bindings())
        if result:
            self.library = result

    def navigate(self, page, manual=True):
        if self.lab:
            if self.lab.active:
                self.lab.stop(show=False, on_saved=lambda: self.navigate(page, manual))
                return
            self.lab = None
        if self.workout:
            self.stop(show_result=False)
        self.set_lesson_header()
        self.recording = False
        self.result_kind = None
        self.mode = page
        self.tabs.set_visible(True)
        self.guide = None
        if manual:
            self.message()
        self.refresh()
        for name, nav in self.navigation.items():
            if name == page:
                nav.add_css_class("active")
            else:
                nav.remove_css_class("active")
        clear(self.body)
        if page == "lab":
            self.lab = LabPage(self)
            self.body.append(expand(self.lab, True))
            return
        if page == "home":
            self.build_home()
            return
        practice = page in ("shortcuts", "typing")
        self.page = box(Gtk.Orientation.VERTICAL, 9 if practice else 14, "lesson-page" if practice else "page")
        layout = scroll(self.page)
        self.body.append(expand(layout, True))
        getattr(self, f"build_{page}")()
        if page == "shortcuts":
            self.start_button.grab_focus()
        elif page == "typing":
            self.typing_start.grab_focus()

    def title(self, line, subtitle=None):
        heading = add(box(gap=4), text(line, "h1"))
        if subtitle:
            heading.append(text(subtitle, "muted", wrap=True))
        self.page.append(heading)
        return heading

    def visible_skills(self):
        return [item for item in self.library["skills"] if not item["skill"]["hidden"]]

    def build_shortcuts(self):
        groups = list(dict.fromkeys(item["skill"]["group"] for item in self.visible_skills()))
        preferred = ["Проблемы с ⊞", "Окна и система", "Терминал", "Вкладки", "Модификаторы", "Цифровой ряд", "Спецсимволы"]
        self.groups = [group for group in preferred if group in groups] + [group for group in groups if group not in preferred]
        if self.group not in self.groups:
            self.group = self.groups[0] if self.groups else ""
        self.group_picker = bind_name(dropdown(self.groups), "Группа сочетаний")
        if self.group:
            self.group_picker.set_selected(self.groups.index(self.group))
        self.skill_picker = bind_name(dropdown([]), "Упражнение Live")
        self.run_picker = bind_name(dropdown(["Одно сочетание", "Случайная серия группы", "Серия из изученных"],
                                             self.update_preview), "Формат тренировки")
        self.tier_picker = bind_name(dropdown(["Авто", "Базовый", "Средний", "Сложный"]), "Сложность")
        self.limit_picker = bind_name(dropdown(["10 заданий", "20 заданий", "30 заданий"]), "Размер серии")
        self.technique = bind_name(dropdown(["Ctrl и Shift справа", "Ctrl справа, Shift слева"]), "Постановка для Ctrl и Shift")
        self.technique.set_selected(0 if self.library["rightHandChord"] else 1)
        self.technique.set_tooltip_text("Выберите вариант, при котором кисть остаётся расслабленной.")
        self.technique.connect("notify::selected", lambda *_: self.change_technique())
        self.start_button = button("Начать · F5", self.toggle_shortcut, "primary")
        self.preparation = add(box(gap=7), add(row(7), text("Live", "lesson-title"), expand(box()),
                         self.technique), add(row(7), self.group_picker, expand(self.skill_picker), self.run_picker),
                         add(row(7), self.tier_picker, self.limit_picker, expand(box()), self.start_button))
        self.page.append(self.preparation)
        self.hero = box(gap=3)
        self.hero.set_focusable(True)
        self.hero_line = text("", "eyebrow")
        self.prompt = text("", "lesson-title")
        self.prompt.set_ellipsize(Pango.EllipsizeMode.END)
        self.tiles = row(9)
        self.tiles.set_halign(Gtk.Align.CENTER)
        self.tiles.set_valign(Gtk.Align.CENTER)
        self.command_strip = Gtk.CenterBox()
        self.command_strip.add_css_class("command-strip")
        self.command_strip.set_center_widget(self.tiles)
        self.start_on_click(self.command_strip, self.toggle_shortcut)
        self.detail = text("", "muted", wrap=True)
        self.detail.set_xalign(.5)
        self.substitute = text("", "warning", wrap=True)
        self.substitute.set_xalign(.5)
        self.scene_box = box(gap=3)
        self.scene_box.add_css_class("scene")
        self.feedback = text("", "muted", wrap=True)
        self.exit_hint = text("", "key-hint")
        self.live_timer = text("00:00", "lesson-timer")
        self.live_status = self.live_timer
        self.hero_line.set_visible(False)
        self.live_controls = self.lesson_controls(self.pause_live, self.stop, self.prompt)
        self.live_controls.append(self.exit_hint)
        self.live_progress = self.lesson_progress()
        self.prompt.remove_css_class("lesson-title")
        self.prompt.add_css_class("crumb")
        self.live_timer.set_halign(Gtk.Align.CENTER)
        self.substitute_button = button("Учебная замена  ·  F6", self.switch_capture, "capture-button")
        self.live_controls.append(self.substitute_button)
        add(self.hero, self.live_timer, self.live_controls, self.command_strip, self.live_progress,
            self.detail, self.substitute, self.scene_box, self.feedback)
        self.guide = LessonGuide(self.library["keyboard"])
        self.guide.reflow(self.window.get_width() or self.window_size[0])
        add(self.page, self.hero, self.guide)
        self.group_picker.connect("notify::selected", lambda *_: self.change_group())
        self.skill_picker.connect("notify::selected", lambda *_: self.select_skill())
        self.change_group()

    def change_group(self):
        if self.workout:
            return
        selected = self.group_picker.get_selected()
        self.group = self.groups[selected] if selected < len(self.groups) else ""
        self.entries = [item for item in self.visible_skills() if item["skill"]["group"] == self.group]
        if self.skill_id not in {item["skill"]["id"] for item in self.entries}:
            self.skill_id = self.entries[0]["skill"]["id"] if self.entries else ""
        self.updating_skill = True
        try:
            self.skill_picker.set_model(Gtk.StringList.new([
                item["skill"]["title"] + (f'  ·  {chord_label(item["skill"]["keys"])}' if item["skill"]["keys"] else "")
                for item in self.entries]))
            for index, item in enumerate(self.entries):
                if item["skill"]["id"] == self.skill_id:
                    self.skill_picker.set_selected(index)
                    break
        finally:
            self.updating_skill = False
        self.update_preview()

    def select_skill(self, key=None):
        if self.updating_skill or self.workout:
            return
        if key is not None:
            for index, item in enumerate(self.entries):
                if item["skill"]["id"] == key:
                    self.skill_picker.set_selected(index)
                    break
        selected = self.skill_picker.get_selected()
        if selected < len(self.entries):
            self.skill_id = self.entries[selected]["skill"]["id"]
            self.run_picker.set_selected(0)
        self.update_preview()

    def set_group(self, key):
        found = next((item["skill"]["group"] for item in self.visible_skills() if item["skill"]["id"] == key), None)
        if found and found in self.groups:
            self.skill_id = key
            self.group_picker.set_selected(self.groups.index(found))
            self.select_skill(key)

    def update_preview(self):
        if self.workout or not hasattr(self, "entries"):
            return
        chosen = next((item for item in self.entries if item["skill"]["id"] == self.skill_id), None)
        self.start_button.set_sensitive(chosen is not None)
        if chosen is None:
            self.prompt.set_label("Выберите упражнение")
            return
        skill = chosen["skill"]
        series = self.run_picker.get_selected()
        prompt = skill["title"] if not series else ("Случайная серия" if series == 1 else "Изученные сочетания")
        self.render_skill(skill, None, 0, prompt=prompt, preview=True)
        self.guide.paint(self.library, self.layout)
        self.live_progress.set_visible(False)
        self.exit_hint.set_visible(False)
        self.preparation.set_visible(True)
        self.live_status.set_visible(False)
        self.live_controls.set_visible(True)
        self.pause_button.set_visible(False)
        self.finish_button.set_visible(False)
        self.hint_titles("F4", "F7")
        for widget in (self.feedback, self.scene_box, self.substitute, self.substitute_button):
            widget.set_visible(False)

    def render_skill(self, skill, guide, step, prompt=None, held=(), miss=None, blind=False, waiting=False, preview=False):
        self.hero_line.set_label(skill["group"].upper())
        self.prompt.set_label("Live  ›  " + skill["group"] + "  ›  " + (prompt or skill["title"]))
        self.detail.set_label(skill["note"] or "")
        self.detail.set_visible(bool(skill["note"]))
        clear(self.tiles)
        actions = guide["steps"][min(step, len(guide["steps"]) - 1)] if guide and guide["steps"] else []
        if preview:
            self.tiles.append(text("Пробел — СТАРТ", "ready-line"))
            self.detail.set_visible(False)
        elif waiting:
            self.tiles.append(text("Ожидание разрешения…", "ready-line"))
        elif skill["action"] in ("layout", "layout-back") and self.state["activity"] != "layout-after":
            self.tiles.append(text("A", "keytile"))
            actions = [{"token": "A", "key": "A", "finger": 0}]
        elif skill["action"] in ("layout", "layout-back") and not actions:
            self.tiles.append(text("Системная раскладка", "keytile"))
        elif skill["action"] == "reboot":
            self.tiles.append(text("reboot", "keytile"))
            self.tiles.append(text("Enter", "keytile"))
        elif blind:
            self.tiles.append(text("Нажмите по памяти", "keytile"))
        elif skill["character"]:
            self.tiles.append(text(skill["character"], "keytile"))
        else:
            if any(needs_fn(item["token"], self.library) for item in actions):
                self.tiles.append(text("Fn", "keytile"))
                self.tiles.append(text("+", "plus"))
            for index, item in enumerate(actions):
                if index:
                    self.tiles.append(text("+", "plus"))
                self.tiles.append(text(key_label(item["token"]), "keytile"))
        targets = {} if blind or waiting else {item["key"]: item["finger"] for item in actions}
        hints = "   ·   ".join(f'{key_label(item["key"])} — {FINGERS[item["finger"]]}' for item in actions
                             if item["key"] in targets)
        self.guide.paint(self.library, self.layout, targets, held, miss, hints, targets.values())
        self.technique.set_visible(preview and skill["id"] in ("terminal-copy", "terminal-paste", "terminal-cut", "terminal-right-paste"))

    def change_technique(self):
        updated = self.call("settings", paired=self.technique.get_selected() == 0)
        if updated:
            self.library = updated
            self.update_preview()

    def toggle_shortcut(self):
        if self.workout:
            self.stop()
            return
        self.refresh()
        choice = self.run_picker.get_selected()
        ids = ([item["skill"]["id"] for item in self.visible_skills()] if choice == 2 else
               [item["skill"]["id"] for item in self.entries] if choice == 1 else [self.skill_id])
        tier = self.tier_picker.get_selected()
        state = self.call("shortcut-start", ids=ids, level=max(tier - 1, 0), auto=tier == 0,
                          studied=choice == 2, limit=[10, 20, 30][self.limit_picker.get_selected()])
        if state is None:
            return
        self.message()
        self.workout = "shortcuts"
        self.capture_note = ""
        self.request_exact = False
        self.pressed.clear()
        self.paused = False
        self.preparation.set_visible(False)
        self.page.add_css_class("engaged")
        self.tabs.set_visible(True)
        self.hero.grab_focus()
        self.render_shortcut_state(state)

    def render_scene(self, scene, subtitle=""):
        clear(self.scene_box)
        action = scene["action"]
        show = action not in ("keys", "digit", "digits", "symbol", "layout", "layout-back")
        self.scene_box.set_visible(show)
        if not show:
            return
        description = row(8)
        self.scene_box.append(description)
        if scene["tabs"]:
            tabs = row(5)
            for index, item in enumerate(scene["tabs"]):
                styles = ("tag", "tab-active") if index == scene["active"] else ("tag",)
                tabs.append(text(item, *styles))
            description.append(expand(tabs))
        elif action == "reboot":
            prompt = "$ " + scene["entry"] + " ▌" if not scene["rebooted"] else "$ reboot   ✓"
            description.append(expand(text(prompt, "mono", "good")))
        elif action.startswith("window"):
            view = ("Учебное окно закрыто" if not scene["window"] else
                    "Учебное окно · полный экран" if scene["fullscreen"] else
                    "Учебное окно · развёрнуто" if scene["maximized"] else "Учебное окно · отчёт")
            description.append(expand(text(view, "tag")))
        elif action.startswith("screenshot"):
            description.append(expand(text("▣  Отчёт · 17 проверок  ✓" if scene["captured"] else
                                       "□  Учебная область для снимка", "mono", "lavender")))
        elif action == "terminal-open":
            description.append(expand(text("$ учебный терминал" if scene["terminal"] else "Учебный рабочий стол", "mono")))
        else:
            description.append(expand(text(f'$ {scene["body"] or "▌"}', "mono", "good")))
        if scene["clipboard"]:
            clip = text("Учебный буфер  ·  " + scene["clipboard"], "muted")
            clip.set_ellipsize(Pango.EllipsizeMode.END)
            description.append(clip)
        if subtitle:
            self.scene_box.append(text(subtitle, "muted", wrap=True))

    def render_shortcut_state(self, state):
        self.state = state
        if state["finished"]:
            self.stop(completed=True)
            return
        if state["inputMode"] == "waiting" and not self.request_exact:
            self.set_input_mode("substitute")
            return
        mode = state["inputMode"]
        waiting = mode == "waiting"
        self.update_capture(state["needsSystem"] and mode != "substitute" and not self.paused)
        blind = self.tier_picker.get_selected() in (0, 3) and state["tier"] == 2 and mode == "exact"
        self.render_skill(state["skill"], state["guide"], state["step"],
                          held=state["held"], miss=(state["mistakeKey"] if state["feedback"] == "error" and
                                                    self.library.get("showMistakeKey", True) else None),
                          blind=blind and not self.paused, waiting=waiting)
        if state["activity"] == "layout-after":
            self.detail.set_visible(True)
            self.detail.set_label("Переключите системную раскладку, затем снова нажмите физическую A.")
        elif state["activity"] in ("layout", "layout-back"):
            self.detail.set_visible(True)
            self.detail.set_label("Нажмите физическую A (Ф), затем переключите раскладку.")
        label = ""
        if waiting:
            label = "В диалоге GNOME выберите «Разрешить». Затем вернитесь к упражнению."
        elif mode == "substitute":
            label = self.capture_note or f'Учебная замена для {chord_label(state["targetKeys"])}'
        self.substitute.set_label(label)
        self.substitute.set_visible(bool(label))
        if mode in ("substitute", "waiting"):
            self.detail.set_visible(False)
        self.substitute_button.set_visible(state["needsSystem"] and bool(state["substituteKeys"]))
        self.substitute_button.set_label(("Исходные клавиши" if mode == "substitute" else "Учебная замена") + " · " + state["previewKey"])
        self.render_scene(state.get("verifiedScene") or state["scene"])
        self.live_status.set_visible(True)
        self.live_controls.set_visible(True)
        self.pause_button.set_visible(True)
        self.finish_button.set_visible(True)
        feedback = "Пауза. Нажмите пробел, чтобы продолжить." if self.paused else state["status"]
        self.feedback.set_label(feedback.replace("LSuper", "⊞").replace("RSuper", "⊞ прав.").replace("Super", "⊞"))
        self.feedback.set_visible(bool(feedback))
        self.feedback.remove_css_class("bad")
        self.feedback.remove_css_class("good")
        if not self.paused and state["feedback"] in ("error", "good"):
            self.feedback.add_css_class("bad" if state["feedback"] == "error" else "good")
        self.exit_hint.set_label(f'{state["solved"]}/{state["limit"]}')
        self.live_timer.set_label(self.clock_text(state["seconds"]))
        key_hint, hands_hint, pause_hint = self.shortcut_control_keys()
        self.hint_titles(key_hint, hands_hint)
        self.pause_button.set_label("▶" if self.paused else "Ⅱ")
        self.pause_button.set_tooltip_text("Продолжить · Пробел" if self.paused else
                                           "Пауза · " + physical_key_hint(pause_hint, self.library))
        self.finish_button.set_tooltip_text("Остановить · " + state["exitKey"])
        self.exit_hint.set_visible(True)
        self.live_progress.set_visible(True)
        self.live_progress.set_fraction(state["solved"] / state["limit"])
        if state["held"] and not self.paused:
            self.feedback.set_visible(True)
            self.feedback.set_label("Нажато: " + " + ".join(key_label(k) for k in state["held"]))

    def switch_capture(self):
        if self.state["inputMode"] == "substitute":
            self.capture_note = ""
            self.request_exact = True
            self.set_input_mode("waiting")
        else:
            self.choose_substitute()

    def update_capture(self, enabled):
        if not enabled:
            if self.capture_timeout:
                GLib.source_remove(self.capture_timeout)
                self.capture_timeout = 0
            surface, handler = self.capture_surface, self.capture_handler
            self.capture_surface = self.capture_handler = None
            self.capture_requested = False
            self.capture_dialog_seen = False
            if surface is not None:
                surface.disconnect(handler)
                surface.restore_system_shortcuts()
            return

        if self.capture_requested:
            if self.state["inputMode"] == "waiting":
                if not self.capture_timeout:
                    self.capture_timeout = GLib.timeout_add_seconds(30, self.capture_expired)
                GLib.idle_add(self.resolve_capture)
            return
        surface = self.window.get_surface()
        if not isinstance(surface, Gdk.Toplevel):
            GLib.idle_add(self.choose_substitute)
            return
        self.capture_surface = surface
        self.capture_requested = True
        self.capture_handler = surface.connect("notify::shortcuts-inhibited", self.capture_changed)
        surface.inhibit_system_shortcuts(None)
        self.capture_timeout = GLib.timeout_add_seconds(4, self.capture_expired)
        GLib.idle_add(self.resolve_capture)

    def capture_changed(self, *_):
        GLib.idle_add(self.resolve_capture)

    def resolve_capture(self):
        if not self.capture_surface or self.workout != "shortcuts" or self.paused:
            return GLib.SOURCE_REMOVE
        if self.capture_surface.get_property("shortcuts-inhibited") and self.window.is_active():
            if self.capture_timeout:
                GLib.source_remove(self.capture_timeout)
                self.capture_timeout = 0
            if self.state["inputMode"] == "waiting":
                self.set_input_mode("exact")
        elif self.state["inputMode"] == "exact":
            self.choose_substitute()
        return GLib.SOURCE_REMOVE

    def capture_after_dialog(self, token):
        if (self.workout == "shortcuts" and self.state["stepToken"] == token and
                self.state["inputMode"] == "waiting" and self.window.is_active()):
            self.resolve_capture()
            if self.state["inputMode"] == "waiting":
                self.capture_unavailable()
        return GLib.SOURCE_REMOVE

    def capture_expired(self):
        self.capture_timeout = 0
        if self.workout == "shortcuts" and self.state["inputMode"] == "waiting" and not self.paused:
            if self.capture_dialog_seen and not self.window.is_active():
                return GLib.SOURCE_REMOVE
            self.capture_unavailable()
        return GLib.SOURCE_REMOVE

    def capture_unavailable(self):
        self.capture_note = "Система оставила сочетание за собой. Выполните учебную замену в строке."
        self.choose_substitute()

    def set_input_mode(self, mode):
        if self.state["inputMode"] != mode:
            self.pressed.clear()
        state = self.call("shortcut-input-mode", mode=mode, stepToken=self.state["stepToken"])
        if state:
            self.render_shortcut_state(state)

    def choose_substitute(self):
        self.request_exact = False
        if self.workout == "shortcuts" and self.state["needsSystem"] and self.state["substituteKeys"]:
            self.set_input_mode("substitute")
            if self.window.is_active():
                self.hero.grab_focus()
        return GLib.SOURCE_REMOVE

    def close_request(self, *_):
        if self.allow_close:
            return False
        active = bool(self.lab and self.lab.active) or bool(self.workout) or self.recording
        if not active:
            return False
        if self.lab and self.lab.active:
            if self.lab.save_pending:
                return True
            self.lab.pause_run("Пауза. Нажмите пробел, чтобы продолжить.")
        elif self.workout == "shortcuts":
            if not self.paused:
                self.pause_live()
            self.update_capture(False)
        elif self.workout == "typing" and not self.paused:
            self.typing_pause()
        if self.close_dialog is not None:
            self.close_dialog.present()
            return True
        dialog = Gtk.MessageDialog(transient_for=self.window, modal=True,
                                   message_type=Gtk.MessageType.QUESTION, text="Закрыть тренажёр?")
        dialog.add_button("Остаться", Gtk.ResponseType.CANCEL)
        dialog.add_button("Закрыть", Gtk.ResponseType.ACCEPT)
        dialog.set_default_response(Gtk.ResponseType.ACCEPT)
        def answer(widget, response):
            widget.destroy()
            self.close_dialog = None
            if response == Gtk.ResponseType.ACCEPT:
                def close_saved():
                    self.allow_close = True
                    self.window.close()
                if self.lab and self.lab.active:
                    self.lab.stop(show=False, on_saved=close_saved)
                else:
                    close_saved()
        dialog.connect("response", answer)
        self.close_dialog = dialog
        dialog.present()
        return True

    def focus_changed(self, *_):
        if self.lab:
            self.lab.focus(self.window.is_active())
            return
        if self.window.is_active():
            if self.workout == "shortcuts" and self.state["inputMode"] == "waiting":
                GLib.idle_add(self.resolve_capture)
                if self.capture_dialog_seen:
                    GLib.timeout_add(250, self.capture_after_dialog, self.state["stepToken"])
            return
        self.pressed.clear()
        if self.recording:
            self.capture_keys = []
        if self.workout == "shortcuts":
            if self.state["inputMode"] == "waiting":
                self.capture_dialog_seen = True
                return
            if self.state["activity"] not in ("layout", "layout-back", "layout-after") and not self.paused:
                self.pause_live()
        elif self.workout == "typing":
            self.typing_pause()

    def pause_live(self):
        if self.paused:
            self.paused = False
            state = self.call("shortcut-resume")
            if state:
                self.render_shortcut_state(state)
            self.hero.grab_focus()
        else:
            self.paused = True
            self.pressed.clear()
            if self.state["needsSystem"] and self.state["inputMode"] == "exact":
                self.call("shortcut-pause")
                self.choose_substitute()
            else:
                state = self.call("shortcut-pause")
                self.render_shortcut_state(state or self.state)

    @staticmethod
    def clock_text(seconds):
        minutes, seconds = divmod(int(seconds), 60)
        return f"{minutes:02}:{seconds:02}"

    def stop(self, completed=False, show_result=True):
        previous = self.workout
        self.workout = None
        self.paused = False
        self.tabs.set_visible(True)
        self.update_capture(False)
        self.pressed.clear()
        if previous == "shortcuts":
            result = self.call("shortcut-result")
            settings = (self.run_picker.get_selected(), self.tier_picker.get_selected(), self.limit_picker.get_selected())
            if show_result and result:
                self.build_result(previous, result, settings, completed)
            else:
                self.change_group()
        elif previous == "typing":
            self.call("typing-stop", layout=self.layout)
            result = self.call("typing-result", layout=self.layout)
            settings = (self.typing_level.get_selected(), self.typing_time.get_selected())
            if show_result and result:
                self.build_result(previous, result, settings, completed)
            else:
                self.typing_prep.set_visible(True)
                self.typing_controls.set_visible(False)

    def start_on_click(self, surface, start):
        gesture = Gtk.GestureClick()
        gesture.connect("released", lambda *_: start() if not self.workout else None)
        surface.add_controller(gesture)

    def lesson_progress(self):
        bar = Gtk.ProgressBar()
        bar.add_css_class("lesson-progress")
        return bar

    def lesson_controls(self, pause, finish, crumb):
        self.pause_button = button("Ⅱ", pause, "mini")
        self.pause_button.set_tooltip_text("Пауза · " + physical_key_hint("F8", self.library))
        self.finish_button = button("ОСТАНОВИТЬ", finish, "stop")
        self.finish_button.set_tooltip_text("Остановить · Esc")
        controls = add(row(5), crumb, self.finish_button, expand(box()))
        self.hint_buttons = {}
        for key, icon in (("keys", "⌨"), ("hands", "👐"), ("mistakes", "●")):
            toggle = Gtk.ToggleButton(label=icon)
            toggle.add_css_class("hint-toggle")
            if key == "mistakes":
                toggle.add_css_class("mistake-toggle")
            setting = {"keys": "showKeys", "hands": "showHands", "mistakes": "showMistakeKey"}[key]
            toggle.set_active(self.library.get(setting, True))
            toggle.connect("toggled", lambda widget, key=key: self.apply_hint(key, widget.get_active()))
            self.hint_buttons[key] = toggle
            controls.append(toggle)
        self.hint_titles("F4", "F7")
        bind_name(self.hint_buttons["mistakes"], "Подсветка ошибочной клавиши")
        self.hint_buttons["mistakes"].set_tooltip_text("Подсветка ошибочной клавиши")
        controls.append(self.pause_button)
        return controls

    def hint_titles(self, keys, hands):
        for key, label, binding in (("keys", "Клавиатура", keys), ("hands", "Руки", hands)):
            hint = label + " · " + physical_key_hint(binding, self.library)
            self.hint_buttons[key].set_tooltip_text(hint)
            bind_name(self.hint_buttons[key], hint)

    def apply_hint(self, key, enabled):
        updated = self.call("settings", **{("mistakeKeys" if key == "mistakes" else key): enabled})
        if not updated:
            return
        self.library = updated
        if self.mode == "shortcuts":
            if self.workout:
                self.render_shortcut_state(self.state)
            else:
                self.update_preview()
        elif self.workout == "typing":
            self.render_typing_state(self.state)
        elif self.mode == "typing":
            self.guide.paint(self.library, self.layout)

    def hint_key(self, key):
        toggle = self.hint_buttons[key]
        toggle.set_active(not toggle.get_active())

    def shortcut_control_keys(self):
        state = self.state
        step = state["step"]
        guide = state["guide"]
        tokens = {item["token"] for item in guide["steps"][step]} if guide and step < len(guide["steps"]) else set()
        return ("F9" if "F4" in tokens else "F4", "F10" if "F7" in tokens else "F7", "F11" if "F8" in tokens else "F8")

    def update_layout(self, controller, character):
        candidate = None
        if character and character.isascii() and character.isalpha():
            candidate = "EN"
        elif character and ("а" <= character.lower() <= "я" or character.lower() == "ё"):
            candidate = "RU"
        elif controller is not None:
            # GNOME can replace the Wayland keymap when switching sources. Ask GDK
            # how it translates physical A in this event, independent of source-list order.
            translated, probe, *_ = self.window.get_display().translate_key(
                38, Gdk.ModifierType(0), controller.get_group())
            if translated and (codepoint := Gdk.keyval_to_unicode(probe)):
                candidate = {"a": "EN", "ф": "RU"}.get(chr(codepoint).lower())
        if candidate and candidate != self.layout:
            self.layout = candidate
            self.show_layout()
            if not self.workout and not self.recording and not self.result_kind and self.mode == "shortcuts":
                self.refresh()
                self.change_group()

    def key_pressed(self, controller, keyval, keycode, modifiers):
        key = PHYSICAL.get(keycode, "")
        character = chr(value) if (value := Gdk.keyval_to_unicode(keyval)) else ""
        self.update_layout(controller, character)
        if self.lab:
            handled = self.lab.key_down(key, character, modifiers, keycode)
            if not self.lab.active and not handled and modifiers & Gdk.ModifierType.ALT_MASK and key in self.NAV_SHORTCUTS:
                self.navigate(self.NAV_SHORTCUTS[key])
                return True
            return handled
        if self.recording:
            return self.capture_press(key)
        if self.workout == "shortcuts":
            if key == self.state["exitKey"] and not (self.pressed & MODIFIERS):
                self.stop()
                return True
            key_hint, hands_hint, pause_hint = self.shortcut_control_keys()
            if key in (key_hint, hands_hint) and not (self.pressed & MODIFIERS):
                self.hint_key("keys" if key == key_hint else "hands")
                return True
            if (key == pause_hint and not (self.pressed & MODIFIERS)) or (self.paused and key == "Space"):
                self.pause_live()
                return True
            if self.paused and key == "Enter":
                return self.window.get_focus() is not self.finish_button
            if (key == self.state["previewKey"] and not (self.pressed & MODIFIERS)
                    and self.state["needsSystem"]):
                self.switch_capture()
                return True
            if self.paused or key in self.pressed or self.state["inputMode"] == "waiting":
                return True
            if self.capture_was_revoked():
                return True
            if self.state["skill"]["action"] == "symbol" and self.state["skill"]["id"].split("-")[1] != self.layout:
                self.feedback.set_label("Вернитесь в раскладку этого упражнения. Можно завершить и выбрать текущую.")
                return True
            self.pressed.add(key)
            if (self.state["activity"] in ("reboot", "layout", "layout-back", "layout-after")
                    and modifiers & (Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.ALT_MASK | Gdk.ModifierType.SUPER_MASK)):
                character = ""
            self.send_shortcut_key(key or "Неизвестная клавиша", True, character)
            return True
        if self.workout == "typing":
            if key == "Esc":
                self.stop()
            elif key in ("F4", "F7"):
                self.hint_key("keys" if key == "F4" else "hands")
            elif key == "F8" or self.paused and key == "Space":
                self.typing_pause() if not self.paused else self.typing_resume()
            elif self.paused and key == "Enter":
                return self.window.get_focus() is not self.finish_button
            elif not self.paused and not (modifiers & (Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.ALT_MASK |
                                                         Gdk.ModifierType.SUPER_MASK)):
                typed = {"Enter": "\n", "NumEnter": "\n", "Backspace": "\b", "Tab": "\t"}.get(key, character)
                if typed and (typed in ("\n", "\t", "\b") or typed.isprintable()):
                    state = self.call("typing-key", text=typed, layout=self.layout)
                    if state:
                        self.render_typing_state(state)
                        if state["finished"]:
                            self.stop(completed=True)
            return True
        if key == "F5" and not self.result_kind and self.mode in ("shortcuts", "typing"):
            self.toggle_shortcut() if self.mode == "shortcuts" else self.start_typing()
            return True
        if self.result_kind and key == "Esc":
            self.navigate(self.result_kind)
            return True
        if self.result_kind and key == "Space" and not (modifiers & (Gdk.ModifierType.ALT_MASK |
                                                                       Gdk.ModifierType.CONTROL_MASK |
                                                                       Gdk.ModifierType.SHIFT_MASK |
                                                                       Gdk.ModifierType.SUPER_MASK)):
            if inside_widget(self.window.get_focus(), self.page):
                return False
            self.replay_result(self.result_kind, self.result_settings)
            return True
        if (not self.result_kind and self.mode in ("shortcuts", "typing") and key in ("F4", "F7")
                and not (modifiers & (Gdk.ModifierType.ALT_MASK | Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SUPER_MASK))):
            self.hint_key("keys" if key == "F4" else "hands")
            return True
        if modifiers & Gdk.ModifierType.ALT_MASK and key in self.NAV_SHORTCUTS:
            self.navigate(self.NAV_SHORTCUTS[key])
            return True
        if (not self.result_kind and self.mode in ("shortcuts", "typing") and key == "Space"
                and not (modifiers & (Gdk.ModifierType.ALT_MASK | Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SUPER_MASK))):
            focus = self.window.get_focus()
            if focus is None or not any(focus.get_ancestor(t) or isinstance(focus, t)
                                        for t in (Gtk.DropDown, Gtk.Button, Gtk.Popover)):
                self.toggle_shortcut() if self.mode == "shortcuts" else self.start_typing()
                return True
        return False

    def key_released(self, _controller, _keyval, keycode, _modifiers):
        key = PHYSICAL.get(keycode, "")
        if self.lab:
            self.lab.key_up(key)
            return
        if self.recording:
            self.capture_release(key)
        elif self.workout == "shortcuts" and key in self.pressed:
            self.pressed.remove(key)
            if not self.paused and not self.capture_was_revoked():
                self.send_shortcut_key(key or "Неизвестная клавиша", False, "")

    def capture_was_revoked(self):
        if self.state["needsSystem"] and self.state["inputMode"] == "exact" and not (
                self.capture_surface and self.capture_surface.get_property("shortcuts-inhibited")):
            self.choose_substitute()
            return True
        return False

    def send_shortcut_key(self, key, pressed, character):
        state = self.call("shortcut-key", key=key, pressed=pressed, text=character)
        if state:
            self.render_shortcut_state(state)

    def tick(self):
        if self.lab:
            self.lab.tick()
            return GLib.SOURCE_CONTINUE
        if self.guide:
            if self.window.get_height() <= 665:
                self.page.add_css_class("compact-height")
            else:
                self.page.remove_css_class("compact-height")
            width = self.window.get_width()
            self.guide.reflow(width)
            inset = max(12, round(width * .019))
            self.page.set_margin_start(inset)
            self.page.set_margin_end(inset)
            progress = self.live_progress if self.mode == "shortcuts" else self.typing_progress
            progress.set_margin_start(round(width * .093))
            progress.set_margin_end(round(width * .093))
        if self.workout == "shortcuts" and not self.paused and self.state["inputMode"] != "waiting":
            state = self.call("shortcut-state")
            if state:
                self.live_timer.set_label(self.clock_text(state["seconds"]))
        if self.workout == "typing" and not self.paused:
            state = self.call("typing-state", layout=self.layout)
            if state:
                self.render_typing_state(state)
                if state["finished"]:
                    self.stop(completed=True)
        return GLib.SOURCE_CONTINUE

    def build_typing(self):
        catalog = self.library["typing"]
        self.typing_options = [
            ("Russian", "Русские фразы"), ("English", "Английские фразы"),
            ("WordsRU", "Русский словарь"), ("WordsEN", "Английский словарь"),
            ("Project", "Python · task-list"), ("Symbols", "Спецсимволы"),
            ("WeakRU", "Работа над промахами · RU"), ("WeakEN", "Работа над промахами · EN"),
        ]
        self.typing_options += [(item["id"], "Python · " + item["title"]) for item in catalog["projects"]]
        self.typing_options += [(item["id"], "Свой набор · " + item["title"]) for item in catalog["sets"]]
        self.typing_options += [(item["id"], "Урок · " + item["title"]) for item in catalog["lessons"]
                                if item["language"] == "RU" or item["id"] in ("lesson:qwop", "lesson:xc") or
                                "motion" in item["id"] or item["id"] == "lesson:home-rest"]
        self.typing_pick = bind_name(dropdown([item[1] for item in self.typing_options]), "Урок печати")
        current = next((index for index, item in enumerate(self.typing_options) if item[0] == self.typing_choice), 0)
        self.typing_pick.set_selected(current)
        self.typing_level = bind_name(dropdown(["Базовый", "Средний", "Сложный"]), "Сложность")
        self.typing_time = bind_name(dropdown(["Без таймера", "1 минута", "3 минуты", "5 минут", "10 минут"]), "Длительность")
        self.typing_start = button("Начать · F5", self.start_typing, "primary")
        self.typing_prep = add(box(gap=7), text("Печать", "lesson-title"),
                              add(row(8), expand(self.typing_pick), self.typing_level, self.typing_time, self.typing_start))
        self.page.append(self.typing_prep)
        self.typing_hero = box(gap=3)
        self.typing_hero.set_focusable(True)
        self.typing_tag = text("Уроки  ›  Печать", "crumb")
        self.typing_tag.set_ellipsize(Pango.EllipsizeMode.END)
        self.typing_timer = text("00:00", "lesson-timer")
        self.typing_text = PracticeLine()
        self.start_on_click(self.typing_text, self.start_typing)
        self.typing_detail = text("", "muted", wrap=True)
        self.typing_detail.set_xalign(.5)
        self.typing_detail.set_visible(False)
        self.typing_timer.set_halign(Gtk.Align.CENTER)
        self.typing_text.placeholder = "Пробел — СТАРТ"
        self.typing_controls = self.lesson_controls(self.toggle_typing_pause, self.stop, self.typing_tag)
        self.typing_progress = self.lesson_progress()
        tape_frame = add(box(Gtk.Orientation.VERTICAL, 0, "tape-frame"), self.typing_text)
        add(self.typing_hero, self.typing_timer, self.typing_controls, tape_frame, self.typing_progress, self.typing_detail)
        self.guide = LessonGuide(self.library["keyboard"], function_row=False)
        self.guide.reflow(self.window.get_width() or self.window_size[0])
        self.pause_button.set_visible(False)
        self.finish_button.set_visible(False)
        add(self.page, self.typing_hero, self.guide)
        self.guide.paint(self.library, self.layout)

    def start_typing(self):
        if self.workout:
            self.stop()
            return
        self.typing_choice = self.typing_options[self.typing_pick.get_selected()][0]
        state = self.call("typing-start", mode=self.typing_choice, level=self.typing_level.get_selected(),
                          seconds=[0, 60, 180, 300, 600][self.typing_time.get_selected()], layout=self.layout)
        if state is None:
            return
        self.message()
        self.workout = "typing"
        self.paused = False
        self.typing_prep.set_visible(False)
        self.page.add_css_class("engaged")
        self.typing_controls.set_visible(True)
        self.pause_button.set_visible(True)
        self.finish_button.set_visible(True)
        self.tabs.set_visible(True)
        self.typing_hero.grab_focus()
        self.render_typing_state(state)

    def render_typing_state(self, state):
        self.state = state
        heading = state["title"]
        if state.get("source"):
            heading += "  ·  " + state["source"]
        if state["rounds"]:
            heading += f'  ·  подход {state["round"]}/{state["rounds"]}'
        language = "Русская раскладка" if state["wantedLayout"] == "RU" else "Английская раскладка"
        self.typing_tag.set_label("Уроки  ›  " + language + "  ›  " + heading)
        self.typing_tag.set_tooltip_text(heading)
        self.typing_tag.set_max_width_chars(76)
        self.typing_progress.set_fraction(state["fragmentProgress"])
        self.typing_text.update(state["before"], state["after"], state["correct"])
        timer = self.clock_text(state["seconds"])
        if state["timeLimit"]:
            timer += " / " + self.clock_text(state["timeLimit"])
        self.typing_timer.set_label(timer)
        detail = "Пауза. Нажмите пробел, чтобы продолжить." if self.paused else ""
        if not detail and state["wantedLayout"] != self.layout and state["key"]:
            detail = "Смените раскладку на " + state["wantedLayout"]
        if not detail and state["weak"]:
            detail = "Повтор промаха: " + state["weak"]
        self.typing_detail.set_label(detail)
        self.typing_detail.set_visible(bool(detail))
        self.pause_button.set_label("▶" if self.paused else "Ⅱ")
        self.pause_button.set_tooltip_text("Продолжить · Пробел" if self.paused else
                                           "Пауза · " + physical_key_hint("F8", self.library))
        hints = []
        targets = {}
        if state["finger"] is not None:
            targets[state["key"]] = state["finger"]
        if state["modifier"]:
            targets[state["modifier"]] = 0 if state["modifier"] == "LShift" else 7
        for key, finger in targets.items():
            hints.append(f"{key_label(key)} — {FINGERS[finger]}")
        self.guide.paint(self.library, state["wantedLayout"], targets, notes="  ·  ".join(hints), chosen=targets.values())

    def toggle_typing_pause(self):
        self.typing_resume() if self.paused else self.typing_pause()

    def typing_pause(self):
        self.paused = True
        state = self.call("typing-pause", layout=self.layout)
        if state:
            self.render_typing_state(state)

    def typing_resume(self):
        self.paused = False
        state = self.call("typing-resume", layout=self.layout)
        if state:
            self.render_typing_state(state)
        self.typing_hero.grab_focus()

    def build_editor(self):
        self.title("Редактор", "Соберите личные сочетания и тексты для тренировки.")
        self.editor_host = box(gap=10)
        self.page.append(add(row(8), button("Горячие клавиши", self.edit_shortcuts), button("Тексты и символы", self.edit_texts)))
        self.page.append(self.editor_host)
        self.edit_shortcuts()

    def edit_shortcuts(self, selected_id=""):
        self.recording = False
        clear(self.editor_host)
        self.editor_kind = "shortcut"
        self.edit_data = [None, *self.library["skills"]]
        names = ["Новое сочетание",
                 *[f'{item["skill"]["group"]} · {item["skill"]["title"]}' + (" · скрыто" if item["skill"]["hidden"] else "")
                   for item in self.edit_data[1:]]]
        self.editor_choice = bind_name(dropdown(names), "Сочетание для редактирования")
        self.editor_host.append(self.editor_choice)
        form = card()
        self.edit_title = bind_name(Gtk.Entry(), "Название сочетания")
        self.edit_title.set_placeholder_text("Например, вставить в терминал")
        self.edit_group = bind_name(Gtk.Entry(), "Группа сочетания")
        self.edit_group.set_placeholder_text("Название группы")
        labels = row(10)
        add(labels, expand(add(box(gap=4), text("Название", "muted"), self.edit_title)),
            expand(add(box(gap=4), text("Группа", "muted"), self.edit_group)))
        self.edit_keys = bind_name(Gtk.Entry(), "Клавиши сочетания")
        self.edit_keys.set_placeholder_text("RCtrl+Shift+V или Ctrl+T → Ctrl+W")
        self.capture_button = button("Записать нажатием", self.capture_start)
        self.finger_options = box(gap=5)
        self.finger_widgets = {}
        self.editor_notice = text("", "muted", wrap=True)
        self.editor_actions = row(8)
        add(form, labels, text("Сочетание или последовательность", "muted"),
            add(row(8), expand(self.edit_keys), self.capture_button, button("Подсказки для пальцев", self.load_fingers)),
            self.finger_options, self.editor_notice, self.editor_actions)
        self.editor_host.append(form)
        if self.library["typing"]["oldShortcuts"]:
            for item in self.library["typing"]["oldShortcuts"]:
                self.editor_host.append(button("Импортировать набор «" + item["title"] + "»",
                                               lambda id=item["id"]: self.apply_editor("shortcut-import", id=id)))
        self.editor_choice.connect("notify::selected", lambda *_: self.load_shortcut_form())
        if selected_id:
            found = next((index for index, item in enumerate(self.edit_data) if item and item["skill"]["id"] == selected_id), 0)
            self.editor_choice.set_selected(found)
        self.load_shortcut_form()

    def load_shortcut_form(self, copy=False):
        self.recording = False
        item = self.edit_data[self.editor_choice.get_selected()]
        skill = item["skill"] if item else {}
        self.edit_id = "" if copy else skill.get("id", "")
        self.edit_title.set_text(skill.get("title", ""))
        self.edit_group.set_text("Мои сочетания" if copy or not skill else skill["group"])
        self.edit_keys.set_text(skill.get("keys", ""))
        self.editor_notice.set_label("")
        self.load_fingers(skill.get("fingers") or "")
        editable = not skill or copy or item["editable"]
        for widget in (self.edit_title, self.edit_group, self.edit_keys, self.capture_button):
            widget.set_sensitive(editable)
        self.finger_options.set_sensitive(editable)
        clear(self.editor_actions)
        if editable:
            self.editor_actions.append(button("Сохранить сочетание", self.save_shortcut, "primary"))
        if self.edit_id:
            self.editor_actions.append(button("Показать" if skill["hidden"] else "Скрыть",
                lambda: self.apply_editor("shortcut-hide", id=self.edit_id, hide=not skill["hidden"])))
        if editable and self.edit_id:
            self.editor_actions.append(button("Удалить", lambda: self.confirm_delete("shortcut-delete", self.edit_id)))
        elif not editable:
            if skill["keys"]:
                self.editor_actions.append(button("Сделать личную копию", lambda: self.load_shortcut_form(copy=True)))
            self.editor_notice.set_label("Встроенные сочетания можно скрыть. Для своей техники создайте личную копию.")

    def load_fingers(self, source=None):
        keys = self.edit_keys.get_text().strip()
        if source is None:
            source = self.finger_values()
        clear(self.finger_options)
        self.finger_widgets = {}
        if not keys:
            return
        inspected = self.call("inspect", keys=keys, fingers=source)
        if not inspected:
            return
        self.message()
        self.finger_options.append(text("Пальцы", "h3"))
        options = row(8)
        for index, candidate in enumerate(inspected["choices"]):
            key = candidate["key"]
            widget = bind_name(dropdown(["Авто", *FINGERS]), f"Палец для {key_label(key)}")
            widget.set_selected(candidate["finger"] + 1 if candidate["custom"] else 0)
            self.finger_widgets[key] = widget
            if index % 4 == 0:
                options = row(8)
                self.finger_options.append(options)
            options.append(expand(add(box(gap=4), text(key_label(key), "key-hint"), widget)))

    def finger_values(self):
        return "\n".join(f"{key}={widget.get_selected() - 1}" for key, widget in self.finger_widgets.items()
                         if 0 < widget.get_selected() <= len(FINGERS))

    def save_shortcut(self):
        self.apply_editor("shortcut-save", id=self.edit_id, title=self.edit_title.get_text(),
                          group=self.edit_group.get_text(), keys=self.edit_keys.get_text(), fingers=self.finger_values())

    def capture_start(self):
        self.recording = True
        self.pressed.clear()
        self.capture_keys = []
        self.editor_notice.set_label("Нажмите сочетание и отпустите все клавиши. Esc — отменить. Для серии можно дописать « → » и следующий шаг.")

    def capture_press(self, key):
        if key == "Esc" and not self.capture_keys:
            self.recording = False
            self.editor_notice.set_label("Запись отменена. Для Esc можно ввести название клавиши вручную.")
            return True
        if key and key not in self.pressed:
            self.pressed.add(key)
            self.capture_keys.append(key)
        return True

    def capture_release(self, key):
        if key not in self.pressed:
            return
        self.pressed.remove(key)
        if self.pressed or not self.capture_keys:
            return
        self.recording = False
        keys = "+".join(self.capture_keys)
        result = self.call("inspect", keys=keys, fingers="")
        if result:
            self.edit_keys.set_text(result["keys"])
            self.editor_notice.set_label("Сочетание записано.")
            self.load_fingers("")
        else:
            self.editor_notice.set_label("Запишите сочетание заново или введите клавиши текстом.")

    def edit_texts(self, selected_id=""):
        self.recording = False
        self.editor_kind = "text"
        clear(self.editor_host)
        self.text_sets = [None, *self.library["typing"]["sets"]]
        self.text_choice = bind_name(dropdown(["Новый набор", *[item["title"] for item in self.text_sets[1:]]]), "Свой набор")
        self.editor_host.append(self.text_choice)
        form = card()
        self.set_title = bind_name(Gtk.Entry(), "Название набора печати")
        self.set_kind = bind_name(dropdown(["Текст или код", "Символы"]), "Тип набора")
        self.set_body = Gtk.TextView(wrap_mode=Gtk.WrapMode.WORD_CHAR)
        bind_name(self.set_body, "Содержимое набора печати")
        self.set_body.add_css_class("mono")
        self.set_actions = row()
        add(form, add(row(), expand(self.set_title), self.set_kind),
            text("Вставьте текст или код. Пустая строка разделяет фрагменты. Для символов используйте одну группу на строку.", "muted", wrap=True),
            scroll(self.set_body, 240), self.set_actions)
        self.set_title.set_placeholder_text("Название набора")
        self.editor_host.append(form)
        self.text_choice.connect("notify::selected", lambda *_: self.load_text_form())
        if selected_id:
            found = next((index for index, item in enumerate(self.text_sets) if item and item["id"] == selected_id), 0)
            self.text_choice.set_selected(found)
        self.load_text_form()

    def load_text_form(self):
        item = self.text_sets[self.text_choice.get_selected()] or {}
        self.set_id = item.get("id", "").removeprefix("set:")
        self.set_title.set_text(item.get("title", ""))
        self.set_kind.set_selected(int(item.get("kind") == "Symbols"))
        self.set_body.get_buffer().set_text(item.get("content", ""))
        clear(self.set_actions)
        self.set_actions.append(button("Сохранить набор", self.save_text, "primary"))
        if self.set_id:
            self.set_actions.append(button("Начать печать", self.open_text_set))
            self.set_actions.append(button("Удалить", lambda: self.confirm_delete("typing-delete-set", self.set_id)))

    def open_text_set(self):
        self.typing_choice = "set:" + self.set_id
        self.navigate("typing")
        self.start_typing()

    def save_text(self):
        buffer = self.set_body.get_buffer()
        self.apply_editor("typing-save-set", id=self.set_id, title=self.set_title.get_text(),
                          symbols=self.set_kind.get_selected() == 1, content=buffer.get_text(*buffer.get_bounds(), False))

    def confirm_delete(self, operation, item_id):
        dialog = Gtk.MessageDialog(transient_for=self.window, modal=True, text="Удалить личный набор или сочетание?")
        dialog.add_button("Отмена", Gtk.ResponseType.CANCEL)
        dialog.add_button("Удалить", Gtk.ResponseType.OK)
        dialog.set_default_response(Gtk.ResponseType.CANCEL)
        def response(_dialog, result):
            dialog.destroy()
            if result == Gtk.ResponseType.OK:
                self.apply_editor(operation, id=item_id)
        dialog.connect("response", response)
        dialog.present()

    def apply_editor(self, operation, **payload):
        old = self.library
        result = self.call(operation, **payload)
        if not result:
            return
        self.library = result
        selection = payload.get("id", "") if not operation.endswith("-delete") and operation != "typing-delete-set" else ""
        if self.editor_kind == "shortcut":
            if operation == "shortcut-save" and not selection:
                prior = {item["skill"]["id"] for item in old["skills"]}
                selection = next((item["skill"]["id"] for item in result["skills"] if item["skill"]["id"] not in prior), "")
            self.edit_shortcuts(selection)
        else:
            if operation == "typing-save-set":
                prior = {item["id"] for item in old["typing"]["sets"]}
                selection = "set:" + selection if selection else next(
                    (item["id"] for item in result["typing"]["sets"] if item["id"] not in prior), "")
            self.edit_texts(selection)
        if not self.engine.storage_error:
            self.message()
            if self.editor_kind == "shortcut":
                self.editor_notice.set_label("Изменения сохранены.")
        self.editor_choice.grab_focus() if self.editor_kind == "shortcut" else self.text_choice.grab_focus()

    def build_result(self, kind, result, settings, completed):
        self.result_kind = kind
        self.result_settings = settings
        self.guide = None
        backdrop = self.body.get_first_child()
        if backdrop:
            self.body.remove(backdrop)
        self.result_overlay = Gtk.Overlay()
        self.result_overlay.set_child(backdrop or box())
        self.page = box(Gtk.Orientation.VERTICAL, 0, "lab-modal-layer")
        self.page.set_halign(Gtk.Align.FILL)
        self.page.set_valign(Gtk.Align.FILL)
        self.page.set_hexpand(True)
        self.page.set_vexpand(True)
        self.result_overlay.add_overlay(self.page)
        self.body.append(expand(self.result_overlay, True))
        mistakes = result.get("topMistakes", [])
        if kind == "shortcuts":
            score = result["score"]
            seconds = result["seconds"]
            errors = score["count"] - score["correct"]
            summary = f'{result["solved"]} из {result["limit"]} заданий'
            count = result["solved"]
            speed = f'{count * 60 / max(1, seconds):.1f}'
            speed_label = "Действий/мин"
            accuracy = f'{score["accuracy"]:g}%' if score["count"] else "—"
            points = [(index + 1, elapsed, elapsed is None) for index, elapsed in enumerate(result["series"])]
            unit = "Реакция, мс"
            footer = lambda count: f"{count} попыток"
            description = "Время реакции по попыткам. Розовые отметки показывают ошибки."
            if score["verifiedResults"]:
                summary += f' · проверено по результату: {score["verifiedResults"]}'
            if score.get("substituteCount"):
                substitute_summary = f'Учебные замены: {score["substituteCorrect"]}/{score["substituteCount"]} верно'
                if score["substituteVerifiedResults"]:
                    substitute_summary += f' · проверено сцен: {score["substituteVerifiedResults"]}'
                summary += " · " + substitute_summary
                substitute_mistakes = ", ".join(
                    f'{key_label(item["key"])} ×{item["count"]}' for item in result.get("substituteTopMistakes", []))
                if substitute_mistakes:
                    summary += " · Промахи в заменах: " + substitute_mistakes
                if not points and result.get("substituteSeries"):
                    points = [(index + 1, elapsed, elapsed is None)
                              for index, elapsed in enumerate(result["substituteSeries"])]
                    unit = "Реакция на замену, мс"
                    description = "Время реакции на учебные замены."
        else:
            state = result["state"]
            seconds = state["seconds"]
            mistakes = state["topMistakes"]
            summary = f'{state["title"]}  ·  {state["count"]} знаков'
            count = state["count"]
            speed = f'{state["cpm"]:g}'
            speed_label = "Знаков/мин"
            accuracy = f'{state["accuracy"]:g}%' if state["count"] + state["errors"] else "—"
            errors = state["errors"]
            points = []
            prior_errors = 0
            for point in result["series"]:
                points.append((point["seconds"], point["speed"], point["errors"] > prior_errors))
                prior_errors = point["errors"]
            unit = "Скорость, зн/мин"
            footer = lambda count: f"{count} с"
            description = "Скорость набора по времени. Розовые отметки показывают ошибки."
        panel = stamina_modal("Результат", summary, [
            ("◷", "Время", f'{seconds // 60:02}:{seconds % 60:02}', "tone-time"),
            ("⌨", "Действия" if kind == "shortcuts" else "Знаки", count, "tone-count"),
            ("↗", speed_label, speed, "tone-speed"),
            ("◎", "Точность", accuracy, "tone-accuracy"),
        ])
        summary_line = text(f'Ошибки: {errors}' + ("    ·    Упражнение завершено" if completed else ""),
                            "stamina-summary")
        summary_line.set_xalign(.5)
        panel.append(summary_line)
        if points:
            panel.append(series_chart(points, unit, footer, description))
            legend = row(17, "stamina-legend")
            legend.set_halign(Gtk.Align.CENTER)
            add(legend, text("● " + unit, "stamina-legend-speed"),
                text("● Ошибки", "stamina-legend-error"))
            panel.append(legend)
        else:
            hint = text("График появится после первых попыток.", "muted")
            hint.set_xalign(.5)
            panel.append(hint)
        if mistakes:
            details = Gtk.Expander(label="Частые промахи")
            details.set_child(text("  ·  ".join(f'{key_label(item["key"])} ×{item["count"]}' for item in mistakes),
                                   "muted", wrap=True))
            panel.append(details)
        actions = row(8, "stamina-actions")
        actions.set_halign(Gtk.Align.CENTER)
        again = button("Начать сначала · Пробел", lambda: self.replay_result(kind, settings), "stamina-blue")
        add(actions, button("Завершить", lambda: self.navigate(kind), "stamina-blue"),
            again, button("Прогресс", lambda: self.navigate("progress"), "primary"))
        panel.append(actions)
        self.page.append(panel)
        again.grab_focus()

    def replay_result(self, kind, settings):
        self.navigate(kind)
        if kind == "shortcuts":
            for widget, value in zip((self.run_picker, self.tier_picker, self.limit_picker), settings):
                widget.set_selected(value)
            self.toggle_shortcut()
        else:
            for widget, value in zip((self.typing_level, self.typing_time), settings):
                widget.set_selected(value)
            self.start_typing()

    def build_progress(self):
        self.title("Прогресс", "Точность, скорость и изменение результата по каждому навыку.")
        attempted = [item for item in self.library["skills"] if item["score"]["count"] or item["score"].get("substituteCount")]
        counts = row(9)
        metric(counts, "Сочетаний в практике", str(len(attempted)))
        metric(counts, "Попыток", str(sum(item["score"]["count"] for item in attempted)))
        metric(counts, "Устойчивые навыки", str(sum(item["score"]["level"] == 2 for item in attempted)))
        self.page.append(counts)
        for skill in self.library.get("liveSkills", []):
            tile = card(gap=3)
            tile.append(text(skill["title"] + ("  ✓" if skill["confirmed"] else ""), "h3"))
            tile.append(text(f'{skill["accuracy"]:g}% · {skill["milliseconds"]} мс · самостоятельно {skill["independent"]} · с подсказкой {skill["hints"]}', "muted"))
            self.page.append(tile)
        hotkeys = add(card(gap=7), text("Отдельные сочетания", "h3"))
        if not attempted:
            hotkeys.append(text("После первой попытки здесь появятся точность и время реакции.", "muted", wrap=True))
        for item in sorted(attempted, key=lambda item: (item["score"]["accuracy"], item["skill"]["title"])):
            skill, score = item["skill"], item["score"]
            trend = f' · {score["change"]:+g} п.п.' if score["change"] is not None else ""
            speed = f' · {score["median"]} мс' if score["correct"] else ""
            spread = f' · ±{score["deviation"]} мс' if score["correct"] > 1 else ""
            details = f'{score["correct"]}/{score["count"]} верно · {score["accuracy"]:g}%{trend}{speed}{spread}'
            if not score["count"]:
                details = "Исходное сочетание ожидает проверки"
            if score.get("substituteCount"):
                details += f' · замены {score["substituteCorrect"]}/{score["substituteCount"]}'
            line = add(row(12), expand(add(box(gap=2), text(skill["title"], "h3", wrap=True), text(details, "muted", wrap=True))))
            if not skill["hidden"]:
                line.append(button("Повторить", lambda id=skill["id"]: self.from_progress(id)))
            hotkeys.append(line)
            progress = Gtk.ProgressBar()
            progress.set_fraction(score["accuracy"] / 100)
            hotkeys.append(progress)
        self.page.append(hotkeys)
        typing = add(card(gap=8), text("Печать", "h3"))
        if not self.library["typingHistory"]:
            typing.append(text("Здесь сохраняются результаты занятий печатью.", "muted"))
        for item in self.library["typingHistory"]:
            when = item["date"][:10].split("-")
            label = f'{when[2]}.{when[1]}.{when[0]}  ·  {item["speed"]} сим/мин  ·  {item["accuracy"]:.1f}%  ·  ошибок {item["errors"]}'
            typing.append(add(row(8), expand(text(item["lesson"], wrap=True)), text(label, "muted")))
        self.page.append(typing)

    def from_progress(self, key):
        self.navigate("shortcuts")
        self.set_group(key)
        self.start_button.grab_focus()

    def smoke_test(self):
        try:
            assert self.mode == "shortcuts" and "Вставить в терминал" in self.prompt.get_label()
            self.toggle_shortcut()
            for key, pressed in (("RCtrl", True), ("LShift", True), ("V", True),
                                 ("V", False), ("LShift", False), ("RCtrl", False)):
                self.send_shortcut_key(key, pressed, "v" if key == "V" else "")
            assert self.state["solved"] == 1 and self.state["score"]["verifiedResults"] == 1
            self.hint_key("keys")
            assert not self.guide.board.get_visible() and all(h.get_visible() for h in self.guide.hands.values())
            self.hint_key("keys")
            self.hint_key("hands")
            assert self.guide.board.get_visible() and all(not h.get_visible() for h in self.guide.hands.values())
            self.hint_key("hands")
            self.stop()
            assert self.result_kind == "shortcuts"
            self.navigate("shortcuts")
            self.set_group("screenshot")
            self.toggle_shortcut()
            assert self.state["inputMode"] == "substitute"
            for key, pressed in (("LCtrl", True), ("RShift", True), ("P", True),
                                 ("P", False), ("RShift", False), ("LCtrl", False)):
                self.send_shortcut_key(key, pressed, "")
            assert self.state["solved"] == 1
            assert self.state["score"]["substituteCorrect"] == 1 and self.state["score"]["count"] == 0
            self.stop()
            self.navigate("editor")
            assert self.edit_title.get_sensitive()
            self.edit_keys.set_text("RCtrl+Shift+V")
            self.load_fingers("")
            assert "RCtrl" in self.finger_widgets
            self.navigate("typing")
            self.typing_choice = "Project"
            options = [item[0] for item in self.typing_options]
            self.typing_pick.set_selected(options.index("Project"))
            self.start_typing()
            assert self.state["title"] == "Python · task-list" and self.state["key"] == "F"
            self.stop()
            assert self.result_kind == "typing"
            if self.smoke_live:
                self.navigate("shortcuts")
                self.set_group("screenshot-copy")
                self.toggle_shortcut()
                self.choose_substitute()
            else:
                self.navigate("typing")
                self.typing_pick.set_selected([item[0] for item in self.typing_options].index("WordsEN"))
                self.start_typing()
            print("PASS: GTK Live, substitute capture, separate aids, results, editor and project typing", flush=True)
            if self.smoke != "-":
                GLib.timeout_add(350, self.save_preview)
            else:
                self.quit()
        except Exception:
            import traceback
            traceback.print_exc()
            self.smoke_failure = True
            self.quit()
        return GLib.SOURCE_REMOVE

    def save_preview(self):
        try:
            self.capture_preview()
        except Exception:
            import traceback
            traceback.print_exc()
            self.smoke_failure = True
        self.quit()
        return GLib.SOURCE_REMOVE

    def capture_preview(self):
        self.guide.reflow(self.window.get_width())
        paintable = Gtk.WidgetPaintable.new(self.window)
        snapshot = Gtk.Snapshot()
        width, height = paintable.get_intrinsic_width(), paintable.get_intrinsic_height()
        paintable.snapshot(snapshot, width, height)
        node = snapshot.to_node()
        texture = self.window.get_renderer().render_texture(node, None)
        texture.save_to_png(self.smoke)
        child = self.window.get_child()
        for widget in (self.guide.board, *self.guide.hands.values()):
            found, bounds = widget.compute_bounds(child)
            bottom = bounds.get_y() + bounds.get_height()
            assert found and 0 < bottom <= child.get_height(), f"Keyboard or hands below viewport ({bottom}/{child.get_height()})"
        print(f"PASS: guide in {self.window.get_width()} × {self.window.get_height()} logical pixels", flush=True)

    def do_shutdown(self):
        if self.lab:
            self.lab.stop(show=False)
        if self.workout == "typing":
            self.call("typing-stop", layout=self.layout)
        if self.engine:
            self.engine.close()
        Gtk.Application.do_shutdown(self)


class LabPage(Gtk.Box):
    """Three-minute classes and a keyboard-operated simulated desktop."""
    def __init__(self, host):
        super().__init__(orientation=Gtk.Orientation.VERTICAL, spacing=0)
        self.host = host
        self.active = False
        self.save_pending = False
        self.retry_save = None
        self.state = None
        self.pressed = set()
        # Actual GUI key state is separate from keys already sent to the
        # engine. Capture resets on pause, but a held F-key is not a new press.
        self.physical_down = set()
        self.surface = None
        self.handler = None
        self.capture_timeout = 0
        self.waiting_capture = False
        self.limit_modal_signature = None
        self.limit_resume_wait = False
        self.limit_actions = ()
        self.result = False
        self.show_diagnostic = False
        self.last_physical = ("", None)
        self.add_css_class("lab-page")
        self.canvas = Gtk.Overlay()
        self.canvas.set_hexpand(True)
        self.canvas.set_vexpand(True)
        self.append(self.canvas)
        self.content = box(Gtk.Orientation.VERTICAL, 3, "lab-content")
        self.canvas.set_child(self.content)
        self.modal_layer = box(Gtk.Orientation.VERTICAL, 0, "lab-modal-layer")
        self.modal_layer.set_halign(Gtk.Align.FILL)
        self.modal_layer.set_valign(Gtk.Align.FILL)
        self.modal_layer.set_hexpand(True)
        self.modal_layer.set_vexpand(True)
        self.modal_layer.set_visible(False)
        self.canvas.add_overlay(self.modal_layer)
        catalog = host.call("lab-catalog") or {"classes": []}
        self.all_classes = catalog["classes"]
        self.all_missions = catalog.get("missions", [])
        self.desktop_profile = host.lab_desktop_profile()
        self.classes = []
        self.missions = []
        self.rebuilding_profile = False
        self.mission_picker = dropdown(["Подобрать поручение"])
        self.mission_picker.set_hexpand(True)
        self.picker = dropdown([])
        self.picker.set_visible(False)
        self.mode = dropdown(["По шагам", "Самостоятельно"])
        self.action_limits = (0, 20, 40, 60, 100)
        self.time_limits = (0, 180, 600)
        self.limit = bind_name(dropdown(["Без лимита действий", "20 действий", "40 действий",
                                         "60 действий", "100 действий"]), "Лимит действий")
        self.limit.set_tooltip_text("Действие — зачтённый шаг упражнения.")
        self.time_limit = bind_name(dropdown(["Без таймера", "3 минуты", "10 минут"]), "Таймер")
        self.time_limit.set_tooltip_text("Таймер считает только активное время занятия.")
        self.mission_picker.connect("notify::selected", self.update_budget_controls)
        self.level_titles = ("Основы", "Рабочие приёмы", "Сложный")
        self.level = dropdown(self.level_titles)
        self.begin = button("Начать · Пробел", self.start, "primary")
        self.prep = box(Gtk.Orientation.VERTICAL, 12, "lab-prep")
        self.prep.append(text("Практика без мыши", "h2"))
        self.os_selector = bind_name(dropdown(["Ubuntu", "Windows 10"]), "Учебная операционная система")
        self.os_selector.set_selected(self.desktop_profile == "windows")
        self.os_selector.set_tooltip_text("Меняет учебные сочетания, сценарии и рабочий стол, а не текущую систему.")
        self.os_selector.connect("notify::selected", self.change_desktop_profile)
        self.prep.append(add(row(8), text("Учебная операционная система", "muted"), self.os_selector))
        self.class_cards = row(10)
        self.class_buttons = []
        self.prep.append(self.class_cards)
        self.prep.append(self.mission_picker)
        self.prep.append(add(row(8), self.picker, self.mode, self.level, expand(box()), self.begin))
        self.prep.append(add(row(8), self.limit, self.time_limit))
        self.resume_budget_note = text("Продолжатся лимиты сохранённого занятия.", "muted", wrap=True)
        self.resume_budget_note.set_visible(False)
        self.prep.append(self.resume_budget_note)
        self.summary = text("", "muted", wrap=True)
        self.prep.append(self.summary)
        self.keyboard_profile = dropdown(["Huawei MateBook D16", "Обычная клавиатура"])
        self.keyboard_profile.set_selected(0 if host.library.get("keyboardProfile") == "matebook-d16" else 1)
        self.fn_mode = dropdown(["Fn + F1–F12", "F1–F12 без Fn"])
        self.fn_mode.set_selected(0 if host.library.get("fnMode") == "hotkeys" else 1)
        self.keyboard_profile.connect("notify::selected", lambda *_: self.hardware_setting())
        self.fn_mode.connect("notify::selected", lambda *_: self.hardware_setting())
        self.hardware = Gtk.Expander(label="Клавиатура и режим Fn")
        self.hardware.set_child(add(row(8), self.keyboard_profile, self.fn_mode,
                                    text("Фактически полученную клавишу видно во время занятия по кнопке «i».", "muted", wrap=True)))
        self.prep.append(self.hardware)
        self.check_status = text("", "muted", wrap=True)
        check_control = button("Автопроверка", lambda: host.run_checks(check_control, self.check_status))
        utilities = add(row(9), button("Клавиши", lambda: host.navigate("shortcuts")),
                        button("Редактор упражнений", lambda: host.navigate("editor")), check_control)
        self.prep.append(utilities)
        self.prep.append(self.check_status)
        for selector in (self.mode, self.picker, self.level):
            selector.connect("notify::selected", lambda *_: self.describe())
        self.content.append(self.prep)
        self.toolbar = row(12, "reference-goal")
        self.action_icon = ReferenceGlyph("practice", 30)
        self.action_icon.add_css_class("reference-practice-icon")
        self.step_heading = text("", "reference-step-heading")
        self.task = text("", "reference-goal-title", wrap=True)
        self.task.set_max_width_chars(39)
        self.task.set_lines(3)
        self.task.set_ellipsize(Pango.EllipsizeMode.END)
        self.gesture = row(5, "reference-gesture")
        self.gesture.set_valign(Gtk.Align.CENTER)
        self.timer = text("", "reference-step-heading")
        self.status = text("", "reference-live-feedback")
        self.status.set_ellipsize(Pango.EllipsizeMode.END)
        self.status.set_valign(Gtk.Align.CENTER)
        self.status.connect("notify::label", lambda label, _: label.set_tooltip_text(label.get_label() or None))
        self.pause = button("Ⅱ", self.toggle_pause, "reference-round-control")
        bind_name(self.pause, "Пауза")
        self.restart = button("↻", self.replay, "reference-round-control")
        bind_name(self.restart, "Начать сначала")
        self.restart.set_tooltip_text("Начать сначала после сохранения")
        self.finish = button("Завершить и посмотреть результат", lambda: (self.settings_menu.popdown(), self.stop()))
        self.capture = button("Разрешить клавиши", self.acquire)
        self.aids = {}
        aids_menu = box(gap=7)
        for name, title in (("keys", "Экранная клавиатура"), ("hands", "Подсказки по пальцам"),
                            ("mistakes", "Подсветка ошибки")):
            control = Gtk.CheckButton(label=title)
            setting = {"keys": "showKeys", "hands": "showHands", "mistakes": "showMistakeKey"}[name]
            control.set_active(host.library.get(setting, True))
            control.connect("toggled", lambda widget, key=name: self.aid(key, widget.get_active()))
            self.aids[name] = control
            aids_menu.append(control)
        self.force_guide = False
        self.guide_override = Gtk.CheckButton(label="Подсказки в компактном окне")
        self.guide_override.connect("toggled", self.toggle_compact_guide)
        aids_menu.append(self.guide_override)
        self.hint_button = button("Подсказка · F1", lambda: self.render(self.host.call("lab-hint")), "reference-hint")
        self.diagnostic_button = button("Показать коды клавиш", self.toggle_diagnostic)
        self.details = Gtk.Revealer()
        self.details.set_transition_type(Gtk.RevealerTransitionType.NONE)
        self.details_text = text("", "reference-long-help", wrap=True)
        details_scroll = scroll(self.details_text)
        details_scroll.set_propagate_natural_height(True)
        details_scroll.set_max_content_height(82)
        details_scroll.set_overlay_scrolling(False)
        self.details.set_child(details_scroll)
        options = Gtk.Popover()
        maximize = lambda: host.window.unmaximize() if host.window.is_maximized() else host.window.maximize()
        def window_action(action):
            options.popdown()
            action()

        options_content = add(box(gap=8), aids_menu,
                              button("Открыть описание задания", lambda: self.toggle_goal_details(options)),
                              self.diagnostic_button, self.finish,
                              button("Свернуть окно", lambda: window_action(host.window.minimize)),
                              button("Развернуть / восстановить окно", lambda: window_action(maximize)))
        options.set_child(options_content)
        self.settings_menu = Gtk.MenuButton(icon_name="view-more-symbolic")
        self.settings_menu.set_popover(options)
        bind_name(self.settings_menu, "Параметры занятия")
        self.settings_menu.set_tooltip_text("Подсказки и параметры занятия")
        add(self.toolbar, self.action_icon, expand(add(box(gap=3), self.step_heading, self.task)), self.gesture,
            self.hint_button)
        self.content.append(self.toolbar)
        self.content.append(self.details)
        self.desktop = DesktopView()
        self.desktop.reference_chrome = True
        self.stage = Gtk.Overlay()
        self.stage.add_css_class("reference-workspace")
        self.stage.set_hexpand(True)
        self.stage.set_vexpand(True)
        self.stage.set_child(self.desktop)
        self.diagnostic = text("", "lab-diagnostic")
        self.diagnostic.set_halign(Gtk.Align.END)
        self.diagnostic.set_valign(Gtk.Align.START)
        self.stage.add_overlay(self.diagnostic)
        self.diagnostic.set_visible(False)
        self.content.append(self.stage)
        self.lesson_meter = Gtk.ProgressBar()
        self.lesson_meter.add_css_class("reference-meter")
        self.lesson_meter.set_valign(Gtk.Align.CENTER)
        self.lesson_meter.set_hexpand(True)
        progress = Gtk.Overlay()
        progress.set_hexpand(True)
        progress.set_child(self.lesson_meter)
        progress.add_overlay(self.status)
        self.goal_progress = text("", "reference-step-footer")
        back = button("‹", lambda: host.navigate("lab"), "reference-footer-control")
        close = button("×", host.window.close, "reference-footer-control")
        for control, name in ((back, "Вернуться к каталогу"), (close, "Закрыть тренажёр")):
            bind_name(control, name)
            control.set_tooltip_text(name)
        self.settings_menu.add_css_class("reference-footer-settings")
        self.footer = add(row(8, "reference-lesson-footer"), back, self.goal_progress, progress,
                          self.timer, self.pause, self.restart, self.settings_menu, close)
        self.content.append(self.footer)
        self.guide = LessonGuide(host.library["keyboard"])
        self.guide.add_css_class("lab-guide")
        self.guide_scroll = Gtk.ScrolledWindow(hscrollbar_policy=Gtk.PolicyType.AUTOMATIC,
                                               vscrollbar_policy=Gtk.PolicyType.NEVER)
        self.guide_scroll.set_propagate_natural_height(True)
        self.guide_scroll.set_child(self.guide)
        self.content.append(self.guide_scroll)
        self.guide.paint(host.library, host.layout)
        self.set_desktop_classes()
        for widget in (self.toolbar, self.stage, self.footer, self.guide_scroll, self.details):
            widget.set_visible(False)
        self.os_selector.grab_focus()

    def set_desktop_classes(self):
        selected = self.picker.get_selected()
        previous = self.classes[selected]["id"] if selected < len(self.classes) else None
        if previous in ("ubuntu", "windows"):
            previous = self.desktop_profile
        self.rebuilding_profile = True
        clear(self.class_cards)
        self.class_buttons = []
        self.classes = [item for item in self.all_classes
                        if item["id"] not in ("ubuntu", "windows") or item["id"] == self.desktop_profile]
        self.missions = [item for item in self.all_missions
                         if (item.get("desktopProfile") or self.desktop_profile) == self.desktop_profile]
        for i, item in enumerate(self.classes):
            control = button(item["title"], lambda i=i: self.picker.set_selected(i), "class-button")
            control.set_tooltip_text(item["description"])
            self.class_cards.append(expand(control))
            self.class_buttons.append(control)
        self.picker.set_model(Gtk.StringList.new([item["title"] for item in self.classes]))
        if self.classes:
            self.picker.set_selected(next((i for i, item in enumerate(self.classes) if item["id"] == previous), 0))
        self.rebuilding_profile = False
        self.begin.set_sensitive(bool(self.classes))
        self.describe()

    def change_desktop_profile(self, *_):
        chosen = "windows" if self.os_selector.get_selected() == 1 else "ubuntu"
        if chosen == self.desktop_profile or self.active:
            return
        catalog = self.host.call("lab-catalog", desktopProfile=chosen)
        updated = self.host.call("settings", desktopProfile=chosen) if catalog else None
        if not updated or updated.get("desktopProfile") != chosen:
            self.os_selector.set_selected(self.desktop_profile == "windows")
            return
        self.desktop_profile = chosen
        self.host.library = updated
        self.all_classes = catalog["classes"]
        self.all_missions = catalog.get("missions", [])
        self.set_desktop_classes()

    def toggle_goal_details(self, options):
        options.popdown()
        self.details.set_reveal_child(not self.details.get_reveal_child())
        self.details.set_visible(self.details.get_reveal_child())
        self.desktop.grab_focus()

    def toggle_compact_guide(self, control):
        self.force_guide = control.get_active()
        self.layout_guide()

    def layout_guide(self):
        window = self.host.window
        spacious = window.get_width() >= 865 and window.get_height() >= 645
        self.guide_scroll.set_visible(self.active and (spacious or self.force_guide)
                                     and (self.guide.show_keys or self.guide.show_hands))

    def describe(self):
        index = self.picker.get_selected()
        if not self.rebuilding_profile and index < len(self.classes):
            selected = self.classes[index]
            for index, item in enumerate(self.class_buttons):
                item.remove_css_class("selected")
                if index == self.picker.get_selected():
                    item.add_css_class("selected")
            app = selected["id"]
            level = self.level.get_selected()
            guidance = "guided" if self.mode.get_selected() == 0 else "independent"
            catalog = self.host.call("lab-catalog") or {}
            description = selected["description"]
            advice = next((r for r in catalog.get("recommendations", []) if r.get("app") == app
                           and (r.get("desktopProfile") or self.desktop_profile) == self.desktop_profile), None)
            if advice and advice.get("reason", "").strip():
                suggested = advice.get("level")
                title = (f"Рекомендуемый уровень: {self.level_titles[suggested]}. "
                         if type(suggested) is int and 0 <= suggested < len(self.level_titles) else "")
                description += "\n" + title + advice["reason"]
            self.summary.set_label(description)
            self.choices = [("", "Подобрать поручение")]
            # The engine keeps direct app drafts shared between simulations.
            draft_profile = self.desktop_profile if app in ("workday", "windows") else "ubuntu"
            if any(d["app"] == app and d["level"] == level and d["guidance"] == guidance
                   and (d.get("desktopProfile") or "ubuntu") == draft_profile
                   for d in catalog.get("drafts", [])):
                self.choices.append(("@resume", "Продолжить с сохранённого места"))
            self.choices.extend((m["id"], m["title"]) for m in self.missions if m["app"] == app and m["level"] == level)
            self.mission_picker.set_model(Gtk.StringList.new([title for _, title in self.choices]))
            self.mission_picker.set_selected(0)
            self.update_budget_controls()

    def update_budget_controls(self, *_):
        index = self.mission_picker.get_selected()
        resume = index < len(self.choices) and self.choices[index][0] == "@resume"
        self.limit.set_sensitive(not resume)
        self.time_limit.set_sensitive(not resume)
        self.resume_budget_note.set_visible(resume)

    def hardware_setting(self):
        profile = "matebook-d16" if self.keyboard_profile.get_selected() == 0 else "standard"
        mode = "hotkeys" if self.fn_mode.get_selected() == 0 else "functions"
        updated = self.host.call("settings", keyboardProfile=profile, fnMode=mode)
        if updated:
            self.host.library = updated
            if self.state:
                self.render(self.state)

    def toggle_diagnostic(self):
        self.show_diagnostic = not self.show_diagnostic
        self.diagnostic.set_visible(self.show_diagnostic and self.active)
        self.update_diagnostic()

    def update_diagnostic(self):
        key, code = self.last_physical
        self.diagnostic.set_label("Получено: " + key_label(key or "—") +
                                  (f" · код {code}" if code is not None else ""))

    def reference_lesson_panel(self, title, subtitle, result=None, completed=False):
        panel = box(gap=14)
        panel.add_css_class("reference-modal-panel")
        panel.set_valign(Gtk.Align.CENTER)
        panel.set_margin_top(11)
        panel.set_margin_bottom(11)
        panel.set_margin_start(14)
        panel.set_margin_end(14)
        headline = text(title, "reference-modal-title")
        summary = expand(add(box(gap=3), headline, text(subtitle, "reference-modal-subtitle", wrap=True)))
        if completed:
            icon = ReferenceGlyph("check", 34)
            icon.add_css_class("reference-result-check")
            panel.append(add(row(13), icon, summary))
        else:
            panel.append(summary)
        if result is not None:
            seconds = max(0, int(result.get("seconds", 0)))
            solved = result.get("solved", 0)
            rate = f"{solved * 60 / max(1, seconds):.0f} д/мин"
            metrics = Gtk.Grid(column_homogeneous=True, column_spacing=8)
            for i, (name, value, tone) in enumerate((("Время", f"{seconds//60:02}:{seconds%60:02}", "time"),
                     ("Действия", str(solved), "count"), ("Темп", rate, "speed"))):
                line = row(8, "reference-stat")
                image = ReferenceGlyph(tone, 28)
                image.add_css_class("reference-stat-icon")
                image.add_css_class("reference-stat-" + tone)
                unit = text(value, "reference-stat-value")
                unit.set_ellipsize(Pango.EllipsizeMode.END)
                unit.set_tooltip_text(value)
                add(line, image, expand(add(box(gap=2), text(name, "reference-stat-label"), unit)))
                metrics.attach(line, i, 0, 1, 1)
            panel.append(metrics)
        return panel

    def add_reference_modal(self, panel):
        scroller = scroll(panel)
        scroller.set_size_request(max(440, min(674, self.host.window.get_width() - 35)), -1)
        scroller.set_halign(Gtk.Align.CENTER)
        scroller.set_vexpand(True)
        scroller.set_overlay_scrolling(False)
        self.modal_layer.append(scroller)
        self.modal_layer.set_visible(True)

    @staticmethod
    def append_summary_chart(panel, result):
        duration = max(1, result.get("seconds", 0))
        stride = max(1, (duration + 599) // 600)
        per_bin = {}
        errors = set()
        for point in result.get("series", []):
            index = point["second"] // stride
            if point["correct"]:
                per_bin[index] = per_bin.get(index, 0) + 1
            else:
                errors.add(index)
        points = []
        for index in range(duration // stride + 1):
            start = max(0, index - 4 // stride)
            end = min(duration + 1, (index + 1) * stride)
            count = sum(per_bin.get(i, 0) for i in range(start, index + 1))
            points.append((end - 1, count * 60 / (end - start * stride), index in errors))
        panel.append(series_chart(points, "Действий/мин", lambda count: f"{count:g} с",
                                  "Темп и ошибки занятия"))
        legend = row(17, "stamina-legend")
        legend.set_halign(Gtk.Align.CENTER)
        add(legend, text("● Темп", "stamina-legend-speed"),
            text("● Ошибки", "stamina-legend-error"))
        panel.append(legend)

    def show_pause_modal(self):
        clear(self.modal_layer)
        result = self.host.call("lab-result") or self.state or {}
        panel = self.reference_lesson_panel("Пауза", "Пробел — продолжить занятие", result)
        controls = row(8, "reference-modal-actions")
        controls.set_halign(Gtk.Align.CENTER)
        resume = button("Продолжить", self.acquire, "reference-primary")
        resume.set_tooltip_text("Продолжить · Пробел")
        add(controls, resume, button("Начать сначала", self.replay),
            button("Завершить", self.stop))
        panel.append(controls)
        self.add_reference_modal(panel)
        if not self.host.window.get_focus() or self.host.window.get_focus() == self.desktop:
            resume.grab_focus()

    def show_limit_choice(self, error=""):
        reason = (self.state or {}).get("limitReached")
        signature = ("choice", reason, error)
        if self.limit_modal_signature == signature:
            return
        self.limit_modal_signature = signature
        self.waiting_capture = False
        self.release_capture()
        self.content.set_sensitive(False)
        clear(self.modal_layer)
        explanation = "Можно остановиться здесь или закончить текущую миссию без дополнительного лимита."
        title = {"timer": "Время вышло", "actions": "Лимит действий достигнут"}.get(reason, "Лимит достигнут")
        panel = self.reference_lesson_panel(title, explanation)
        panel.set_focusable(True)
        if error:
            panel.append(text(error, "bad", wrap=True))
        panel.append(text("Tab — выбрать действие · Enter — подтвердить", "reference-modal-subtitle", wrap=True))
        actions = row(8, "reference-modal-actions")
        actions.set_halign(Gtk.Align.CENTER)
        finish = button("Завершить занятие", lambda: self.choose_limit("finish"))
        extend = button("Доработать миссию", lambda: self.choose_limit("continue"), "reference-primary")
        self.limit_actions = (finish, extend)
        panel.append(add(actions, finish, extend))
        self.add_reference_modal(panel)
        # Focus the panel, not a choice: a key already in flight when the
        # limit arrived must not accidentally confirm a default action.
        panel.grab_focus()

    def choose_limit(self, choice):
        if not self.active or self.save_pending or not self.state.get("limitDecisionPending"):
            return
        if choice == "finish":
            self.stop(finish_limit=True)
            return
        state = self.host.call("lab-limit-choice", choice="continue")
        if not state or state.get("limitDecisionPending") or not state.get("finishingMission"):
            if state:
                self.state = state
            self.show_limit_choice((state or {}).get("saveError") or "Не удалось сохранить выбор. Повторите действие.")
            return
        self.state = state
        self.limit_resume_wait = True
        self.limit_modal_signature = ("release", "")
        self.limit_actions = ()
        self.host.message()
        clear(self.modal_layer)
        panel = self.reference_lesson_panel(
            "Доработка миссии", "Отпустите клавиши. Работа продолжится с того же места.")
        self.add_reference_modal(panel)
        GLib.timeout_add(25, self.resume_limit_after_release)

    def resume_limit_after_release(self):
        if not self.active or self.host.lab is not self or not self.limit_resume_wait or self.save_pending:
            return GLib.SOURCE_REMOVE
        if self.physical_down or not self.host.window.is_active():
            return GLib.SOURCE_CONTINUE
        self.limit_resume_wait = False
        self.limit_modal_signature = None
        self.content.set_sensitive(True)
        self.acquire()
        return GLib.SOURCE_REMOVE

    def start(self):
        if self.active or not self.classes:
            return
        self.result = False
        self.content.set_can_focus(True)
        self.host.refresh()
        app = self.classes[self.picker.get_selected()]["id"]
        mission = self.choices[self.mission_picker.get_selected()][0]
        self.state = self.host.call("lab-start", app=app, mode="sandbox", level=self.level.get_selected(), mission="" if mission == "@resume" else mission,
                                    **{"continue": mission == "@resume"}, softFinish=True, combinedLimits=True,
                                    desktopProfile=self.desktop_profile,
                                    guidance="guided" if self.mode.get_selected() == 0 else "independent",
                                    seconds=self.time_limits[self.time_limit.get_selected()],
                                    actionLimit=self.action_limits[self.limit.get_selected()])
        if not self.state:
            return
        self.active = True
        self.host.set_lesson_header(True)
        self.add_css_class("reference-live")
        self.action_icon.name = app
        self.action_icon.cached_key = None
        self.action_icon.queue_draw()
        self.prep.set_visible(False)
        for widget in (self.toolbar, self.stage, self.footer):
            widget.set_visible(True)
        self.desktop.set_size_request(380, 180)
        self.desktop.grab_focus()
        if self.state.get("startBlocked"):
            self.show_save_retry(show=False, on_saved=self.start)
            return
        self.render(self.state)
        if self.active and not self.state.get("limitDecisionPending"):
            self.acquire()

    def acquire(self):
        if (not self.active or self.waiting_capture or self.save_pending or self.limit_resume_wait
                or (self.state or {}).get("limitDecisionPending")):
            return
        keyboard = self.host.window.get_display().get_default_seat().get_keyboard()
        modifiers = keyboard.get_modifier_state() if keyboard else Gdk.ModifierType(0)
        if modifiers & (Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SHIFT_MASK |
                        Gdk.ModifierType.ALT_MASK | Gdk.ModifierType.SUPER_MASK):
            self.status.set_label("Отпустите Ctrl, Shift, Alt и ⊞, затем нажмите «Продолжить».")
            self.status.set_visible(True)
            return
        self.release_capture()
        surface = self.host.window.get_surface()
        if not isinstance(surface, Gdk.Toplevel):
            self.status.set_label("Этот оконный сервер не передаёт системные клавиши. Занятие на паузе.")
            self.status.set_visible(True)
            return
        self.waiting_capture = True
        self.modal_layer.set_visible(False)
        self.surface = surface
        self.handler = surface.connect("notify::shortcuts-inhibited", lambda *_: GLib.idle_add(self.resolve_capture))
        surface.inhibit_system_shortcuts(None)
        self.capture_timeout = GLib.timeout_add_seconds(15, self.capture_expired)
        GLib.timeout_add(700, self.check_denied)
        self.status.set_label("В диалоге GNOME разрешите перехват клавиш для учебного рабочего стола.")
        self.status.set_visible(True)
        GLib.idle_add(self.resolve_capture)

    def resolve_capture(self):
        if self.limit_resume_wait or (self.state or {}).get("limitDecisionPending"):
            return GLib.SOURCE_REMOVE
        if self.active and self.surface and self.surface.get_property("shortcuts-inhibited") and self.host.window.is_active():
            self.waiting_capture = False
            if self.capture_timeout:
                GLib.source_remove(self.capture_timeout)
                self.capture_timeout = 0
            state = self.host.call("lab-resume")
            if state:
                self.render(state)
            if self.active and not self.state.get("limitDecisionPending"):
                self.desktop.grab_focus()
        elif self.active and self.state and not self.state["paused"] and not self.waiting_capture:
            self.pause_run("Система вернула горячие клавиши. Разрешите их захват, чтобы продолжить.")
        return GLib.SOURCE_REMOVE

    def check_denied(self):
        if self.active and self.waiting_capture and self.host.window.is_active() and self.surface and not self.surface.get_property("shortcuts-inhibited"):
            self.waiting_capture = False
            self.release_capture()
            self.status.set_label("Захват клавиш отклонён. Нажмите «Разрешить клавиши», чтобы повторить.")
            self.status.set_visible(True)
            self.show_pause_modal()
        return GLib.SOURCE_REMOVE

    def capture_expired(self):
        self.capture_timeout = 0
        if self.waiting_capture and self.host.window.is_active():
            self.waiting_capture = False
            self.release_capture()
            self.status.set_label("Захват клавиш не разрешён. Нажмите «Разрешить клавиши» для продолжения.")
            self.status.set_visible(True)
            self.show_pause_modal()
        return GLib.SOURCE_REMOVE

    def release_capture(self):
        if self.capture_timeout:
            GLib.source_remove(self.capture_timeout)
            self.capture_timeout = 0
        if self.surface:
            self.surface.disconnect(self.handler)
            self.surface.restore_system_shortcuts()
        self.surface = self.handler = None
        self.pressed.clear()

    def pause_run(self, message="Пауза. Нажмите пробел, чтобы продолжить."):
        if (not self.active or self.save_pending or self.limit_resume_wait
                or (self.state or {}).get("limitDecisionPending")):
            return
        state = self.host.call("lab-pause")
        self.waiting_capture = False
        self.release_capture()
        if state:
            self.render(state)
            if self.state.get("limitDecisionPending") or not self.active:
                return
        self.status.set_label(message)
        self.pause.grab_focus()

    def focus(self, active):
        if not active:
            self.physical_down.clear()
            if self.active and self.limit_resume_wait:
                # We cannot observe releases while another window owns input.
                # Require an intentional normal-pause resume instead of
                # automatically treating the emptied local key set as safe.
                self.limit_resume_wait = False
                self.limit_modal_signature = None
                self.content.set_sensitive(True)
                self.render(self.state)
                return
        if not self.active:
            return
        if active:
            if self.waiting_capture:
                GLib.idle_add(self.resolve_capture)
                GLib.timeout_add(300, self.check_denied)
        elif not self.waiting_capture:
            self.pause_run("Пауза. Вернитесь в тренажёр и нажмите пробел.")

    def toggle_pause(self):
        if self.state and self.state["paused"]:
            self.acquire()
        else:
            self.pause_run()

    def bindings(self):
        task = (self.state or {}).get("task") or {}
        tokens = task.get("keys", "").replace(" → ", "+").split("+")
        reserve_f8 = (self.state or {}).get("scene", {}).get("diagnosticWorkspace")
        return ("F6" if reserve_f8 or "F8" in tokens else "F8", "F9" if "F10" in tokens else "F10")

    def key_down(self, key, character, modifiers=Gdk.ModifierType(0), keycode=None):
        has_modifiers = bool(modifiers & (Gdk.ModifierType.CONTROL_MASK | Gdk.ModifierType.SHIFT_MASK |
                                         Gdk.ModifierType.ALT_MASK | Gdk.ModifierType.SUPER_MASK))
        repeat = bool(key) and key in self.physical_down
        if key:
            self.physical_down.add(key)
        if repeat and (not self.active or self.state["paused"] or key not in self.pressed or key not in (
                "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown", "Backspace", "Delete")):
            return True
        if self.active and key:
            self.last_physical = (key, keycode)
            self.update_diagnostic()
        if not self.active:
            if self.result and key in ("Space", "F5") and not has_modifiers:
                if key == "Space" and inside_widget(self.host.window.get_focus(), self.modal_layer):
                    return False
                self.replay()
                return True
            if key in ("Space", "F5") and not self.result and not has_modifiers:
                if key == "Space" and isinstance(self.host.window.get_focus(), (Gtk.Button, Gtk.DropDown)):
                    return False
                self.start()
                return True
            return False
        pause_key, finish_key = self.bindings()
        if self.save_pending:
            if key == finish_key and not has_modifiers:
                self.retry_save()
                return True
            return key not in ("Enter", "Space", "Tab")
        if self.limit_resume_wait:
            return True
        if self.state.get("limitDecisionPending"):
            if key == "Tab":
                return False  # Let GTK navigate the two explicit choices.
            focused = self.host.window.get_focus()
            return not (key in ("Space", "Enter") and not has_modifiers
                        and self.physical_down <= {key} and focused in self.limit_actions)
        if key == "F1" and not has_modifiers and not self.state.get("showGuide", True) and not self.state["paused"]:
            self.render(self.host.call("lab-hint"))
            return True
        if key == pause_key and not has_modifiers:
            self.toggle_pause()
            return True
        if key == finish_key and not has_modifiers:
            self.stop()
            return True
        if self.state["paused"]:
            if key == "Space" and not has_modifiers:
                if inside_widget(self.host.window.get_focus(), self.modal_layer):
                    return False
                self.acquire()
            if key == "Enter":
                focused = self.host.window.get_focus()
                return not (inside_widget(focused, self.modal_layer) and isinstance(focused, Gtk.Button) and
                            not focused.get_label().startswith("Продолжить"))
            return key in ("Space", pause_key, finish_key)
        if not self.surface or not self.surface.get_property("shortcuts-inhibited"):
            self.pause_run("Захват клавиш прерван. Разрешите его, чтобы продолжить.")
            return True
        self.pressed.add(key)
        if key == "Print":
            active_only = (self.state.get("scene", {}).get("debugDay") and
                           bool(self.pressed & {"LAlt", "RAlt"}))
            character = self.desktop.capture_png(active_only=active_only)
        state = self.host.call("lab-key", key=key, pressed=True, text=character)
        if state:
            self.render(state)
        return True

    def key_up(self, key):
        self.physical_down.discard(key)
        if self.active and key in self.pressed:
            self.pressed.remove(key)
            if not self.state["paused"]:
                state = self.host.call("lab-key", key=key, pressed=False, text="")
                if state:
                    self.render(state)

    def aid(self, key, enabled):
        result = self.host.call("settings", **{("mistakeKeys" if key == "mistakes" else key): enabled})
        if result:
            self.host.library = result
            if self.state:
                self.render(self.state)

    def render(self, state):
        self.state = state
        if self.save_pending or self.limit_resume_wait:
            return
        if state["finished"]:
            self.stop()
            return
        task = state.get("task") or {}
        guided = state.get("showGuide", True)
        self.task.set_label(task.get("title", "") if guided else state.get("phase", ""))
        details = "\n".join(filter(None, [state.get("scenario", ""), *state.get("objectives", []),
                                           task.get("hint", "") if guided else ""]))
        self.task.set_tooltip_text(details)
        self.details_text.set_label(details)
        gesture = display_chord(task.get("keys", ""), self.host.library) or task.get("text", "")
        if guided and not task.get("keys") and task.get("id", "").startswith("open-"):
            gesture = "⊞  →  " + task["text"] + "  →  Enter"
        elif guided and task.get("id", "").startswith("command/") and len(task.get("text", "")) > 12:
            gesture = "Ctrl+R  ·  найдите команду по фрагменту  →  Enter"
        if not guided:
            gesture = ""
        self.hint_button.set_visible(not state.get("showGuide", True))
        tokens = tuple(gesture.replace(" → ", "→").split(" + ")) if gesture else ()
        if tokens != getattr(self, "gesture_tokens", ()):
            self.gesture_tokens = tokens
            clear(self.gesture)
            visible = tokens if len(tokens) <= 3 else (gesture,)
            for index, token in enumerate(visible):
                if index:
                    self.gesture.append(text("+", "reference-key-join"))
                tile = text(token, "reference-keycap")
                tile.set_ellipsize(Pango.EllipsizeMode.END)
                tile.set_max_width_chars(14)
                tile.set_tooltip_text(token)
                self.gesture.append(tile)
        self.gesture.set_visible(bool(tokens))
        step, total = state.get("scenarioStep", 0), state.get("scenarioLength", 0)
        self.step_heading.set_label(f"Задание {step} из {total}" if total else "Задание")
        self.goal_progress.set_label(f"Шаг {step} из {total}" if total else "Практика")
        self.lesson_meter.set_fraction(min(1, max(0, step / total)) if total else 0)
        remaining = max(0, state.get("remaining", 180))
        budget = []
        if state.get("finishingMission"):
            budget.append("Доработка")
        else:
            if state.get("actionLimit"):
                budget.append(f'{state["solved"]}/{state["actionLimit"]}')
            if state.get("timeLimit"):
                minutes, seconds = divmod(int(remaining), 60)
                budget.append(f"{minutes:02}:{seconds:02}")
        self.timer.set_visible(bool(budget))
        self.timer.set_label(" · ".join(budget))
        self.timer.set_tooltip_text(
            "Действия и оставшееся активное время" if len(budget) == 2 else
            "Зачтённые действия" if state.get("actionLimit") and not state.get("finishingMission") else
            "Оставшееся активное время" if state.get("timeLimit") and not state.get("finishingMission") else
            None)
        status = state.get("status", "") or task.get("hint", "")
        if state["held"]:
            status += "  ·  Нажато " + " + ".join(key_label(k) for k in state["held"])
        self.status.set_label(status.replace("LSuper", "⊞").replace("RSuper", "⊞ прав.").replace("Super", "⊞"))
        self.status.set_visible(state["paused"] or state.get("feedback") == "error")
        self.status.remove_css_class("bad")
        self.status.remove_css_class("good")
        if state.get("feedback") in ("error", "good"):
            self.status.add_css_class("bad" if state["feedback"] == "error" else "good")
        pause_key, finish_key = self.bindings()
        self.pause.set_tooltip_text(("Продолжить · Пробел" if state["paused"] else
                                     "Пауза · " + physical_key_hint(pause_key, self.host.library)))
        self.finish.set_tooltip_text("Результат · " + physical_key_hint(finish_key, self.host.library))
        show_alt_pause = not state["paused"] and state["scene"].get("diagnosticWorkspace")
        self.pause.set_label("▶" if state["paused"] else "Ⅱ F6" if show_alt_pause else "Ⅱ")
        bind_name(self.pause, "Продолжить" if state["paused"] else "Пауза · F6" if show_alt_pause else "Пауза")
        self.desktop.presentation_app = state["app"]
        self.desktop.update(state["scene"])
        self.update_diagnostic()
        self.diagnostic.set_visible(self.show_diagnostic and self.active)
        guide = state.get("guide") or {}
        steps = guide.get("steps") or [[]]
        actions = steps[0]
        targets = {item["key"]: item["finger"] for item in actions}
        notes = "  ·  ".join(f'{key_label(k)} — {FINGERS[f]}' for k, f in targets.items())
        miss = (state.get("lastKey") if state.get("feedback") == "error" and
                self.host.library.get("showMistakeKey", True) else None)
        self.guide.paint(self.host.library, state["scene"].get("layout", self.host.layout), targets,
                         state["held"], miss=miss, notes=notes, chosen=targets.values())
        self.layout_guide()
        if state.get("limitDecisionPending"):
            self.show_limit_choice(state.get("saveError") or "")
        elif state["paused"] and not self.waiting_capture:
            self.show_pause_modal()
        elif self.active:
            self.modal_layer.set_visible(False)

    def tick(self):
        self.guide.reflow(self.host.window.get_width())
        self.layout_guide()
        if self.host.window.get_height() <= 680:
            self.add_css_class("compact-height")
        else:
            self.remove_css_class("compact-height")
        if self.active and not self.save_pending and self.state and not self.state["paused"]:
            state = self.host.call("lab-state")
            if state:
                self.render(state)

    def show_save_retry(self, show, on_saved, finish_limit=False):
        self.save_pending = True
        self.retry_save = lambda: self.stop(show=show, on_saved=on_saved, retry=True,
                                            finish_limit=finish_limit)
        self.status.set_label("Не удалось сохранить занятие. Повторите сохранение.")
        self.status.set_visible(True)
        clear(self.modal_layer)
        details = "Занятие осталось на паузе. Закройте другое окно тренажёра или проверьте доступ к папке профиля."
        panel = self.reference_lesson_panel("Прогресс не сохранён", details)
        retry = button("Повторить сохранение", self.retry_save, "reference-primary")
        panel.append(retry)
        self.add_reference_modal(panel)
        retry.grab_focus()

    def stop(self, show=True, on_saved=None, retry=False, finish_limit=None):
        if not self.active or self.save_pending and not retry:
            return False
        if finish_limit is None:
            # Explicit Stop atomically distinguishes early finish from a limit
            # crossed since the last client poll. Close/navigation never choose.
            finish_limit = bool(show and not (self.state or {}).get("finished"))
        self.waiting_capture = False
        self.limit_resume_wait = False
        self.release_capture()
        # Closing or navigating (show=False) checkpoints; only an explicit
        # Finish commits a result when waiting at the limit or extending.
        state = (self.host.call("lab-limit-choice", choice="finish") if finish_limit
                 else self.host.call("lab-stop"))
        if not state or not state.get("saved", False) or (finish_limit and not state.get("finished")):
            if state:
                self.state = state
                self.desktop.update(state["scene"])
            self.show_save_retry(show, on_saved, finish_limit)
            return False
        self.state = state
        self.limit_modal_signature = None
        self.limit_actions = ()
        self.content.set_sensitive(True)
        self.save_pending = False
        self.retry_save = None
        self.active = False
        self.host.message()
        if on_saved:
            on_saved()
        if not show:
            return True
        result = self.host.call("lab-result")
        if not result:
            return True
        self.result = True
        clear(self.modal_layer)
        panel = self.reference_lesson_panel("Результат", self.state.get("scenario", "Практикум"),
                                             result, completed=result.get("completed", False))
        note = f'Ошибки: {result["errors"]}    ·    Рекорд: {result.get("record", 0)}'
        if result.get("newRecord"):
            note += "    ·    Новый рекорд"
        analysis = add(box(gap=5), text(note, "reference-modal-subtitle", wrap=True))
        weak = result.get("weak", [])[:4]
        if weak:
            analysis.append(text("  ·  ".join(f'{item["title"]} ×{item["errors"]}' for item in weak),
                                 "reference-modal-subtitle", wrap=True))
        self.append_summary_chart(analysis, result)
        details = Gtk.Expander(label="Работа над ошибками")
        details.add_css_class("reference-result-details")
        details.set_child(analysis)
        panel.append(details)
        actions = row(8, "reference-modal-actions")
        actions.set_halign(Gtk.Align.CENTER)
        restart = button("Начать сначала", self.replay)
        restart.set_tooltip_text("Начать сначала · Пробел")
        add(actions, button("Завершить", lambda: self.host.navigate("lab")),
            restart, button("К прогрессу", lambda: self.host.navigate("progress"), "reference-primary"))
        panel.append(actions)
        # The result is modal even though it is drawn in an overlay.
        # Exclude the covered lesson subtree from GTK's focus traversal.
        self.content.set_can_focus(False)
        self.add_reference_modal(panel)
        restart.grab_focus()
        self.host.refresh()
        return True

    def replay(self):
        if self.active:
            self.stop(show=False, on_saved=self.replay)
            return
        selected_class = self.classes[self.picker.get_selected()]["id"]
        desktop_profile = self.desktop_profile
        selected_mode = self.mode.get_selected()
        selected_level = self.level.get_selected()
        def selection(widget, options, key):
            value = (self.state or {}).get(key)
            return options.index(value) if value in options else widget.get_selected()
        limit = selection(self.limit, self.action_limits, "actionLimit")
        time_limit = selection(self.time_limit, self.time_limits, "timeLimit")
        self.host.navigate("lab")
        self.host.lab.os_selector.set_selected(desktop_profile == "windows")
        class_index = next((i for i, item in enumerate(self.host.lab.classes) if item["id"] == selected_class), None)
        if class_index is None:
            return
        self.host.lab.picker.set_selected(class_index)
        self.host.lab.mode.set_selected(selected_mode)
        self.host.lab.level.set_selected(selected_level)
        self.host.lab.limit.set_selected(limit)
        self.host.lab.time_limit.set_selected(time_limit)
        mission = self.state.get("mission")
        for index, (identifier, _) in enumerate(self.host.lab.choices):
            if identifier == mission:
                self.host.lab.mission_picker.set_selected(index)
                break
        self.host.lab.start()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", metavar="PNG", help=argparse.SUPPRESS)
    parser.add_argument("--smoke-small", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--smoke-live", action="store_true", help=argparse.SUPPRESS)
    options = parser.parse_args()
    studio = Studio(options.smoke, (960, 600) if options.smoke_small else None, options.smoke_live)
    status = studio.run([sys.argv[0]])
    sys.exit(1 if studio.smoke_failure else status)
