"""Shared finger colors and diagrams for the GTK views."""
from functools import lru_cache
from pathlib import Path
import io
import math

import cairo
from gi.repository import Gdk, GLib, Graphene, Gtk, Pango

def color(ctx, value, alpha=1):
    ctx.set_source_rgba(*(int(value[start:start + 2], 16) / 255 for start in (1, 3, 5)), alpha)


def texture(surface):
    content = io.BytesIO()
    surface.write_to_png(content)
    return Gdk.Texture.new_from_bytes(GLib.Bytes.new(content.getvalue()))


def fingers(picture, chosen, side, compact):
    relevant = tuple(sorted(set(chosen) & ({0, 1, 2, 3, 8} if side == "left" else {4, 5, 6, 7, 9})))
    picture.surface = hand_texture(relevant, side)
    picture.queue_draw()


@lru_cache(maxsize=1)
def hand_sprite():
    return cairo.ImageSurface.create_from_png(str(Path(__file__).with_name("hands.png")))


@lru_cache(maxsize=96)
def hand_texture(chosen, side):
    right = side == "right"
    width, height = (272, 296)
    canvas = cairo.ImageSurface(cairo.FORMAT_ARGB32, width, height)
    ctx = cairo.Context(canvas)
    ctx.scale(2 / 3, 2 / 3)
    ctx.set_source_surface(hand_sprite(), -408 if right else 0, 0)
    ctx.paint()
    ctx.scale(3, 3)
    tips = ((11, 44), (37, 21), (64, 12), (92, 22), (123, 82))
    codes = (7, 6, 5, 4, 9) if right else (0, 1, 2, 3, 8)
    for code, (x, y) in zip(codes, tips):
        if code in chosen:
            ctx.arc(136 - x if right else x, y, 4.5, 0, math.tau)
            color(ctx, "#ea1736")
            ctx.fill_preserve()
            color(ctx, "#fff3df")
            ctx.set_line_width(1)
            ctx.stroke()
    return texture(canvas)


class HandView(Gtk.Widget):
    def do_snapshot(self, snapshot):
        w, h = self.get_width(), self.get_height()
        rect = Graphene.Rect()
        rect.init(0, 0, w, h)
        snapshot.append_texture(self.surface, rect)


class ChartView(Gtk.Widget):
    """Constrain the chart's natural size regardless of backing texture resolution."""
    def __init__(self, picture, description):
        super().__init__()
        self.picture = picture
        self.set_halign(Gtk.Align.CENTER)
        self.update_property([Gtk.AccessibleProperty.LABEL], [description])

    def do_measure(self, orientation, _for_size):
        return (320, 620, -1, -1) if orientation == Gtk.Orientation.HORIZONTAL else (145, 180, -1, -1)

    def do_snapshot(self, snapshot):
        width, height = self.get_width(), self.get_height()
        if width <= 0 or height <= 0:
            return
        scale = min(width / 620, height / 180)
        bounds = Graphene.Rect()
        bounds.init((width - 620 * scale) / 2, (height - 180 * scale) / 2,
                    620 * scale, 180 * scale)
        snapshot.append_texture(self.picture, bounds)


def hand_picture(side):
    picture = HandView()
    picture.set_valign(Gtk.Align.CENTER)
    picture.update_property([Gtk.AccessibleProperty.LABEL],
                            ["Левая рука" if side == "left" else "Правая рука"])
    fingers(picture, (), side, False)
    return picture


def rgba(value):
    result = Gdk.RGBA()
    result.parse(value)
    return result


class PracticeLine(Gtk.Widget):
    """A clipped text stream with a fixed insertion point."""
    __gtype_name__ = "TouchTypingPracticeLine"

    def __init__(self):
        super().__init__()
        self.set_size_request(320, 70)
        self.set_hexpand(True)
        self.before = ""
        self.after = ""
        self.correct = True
        self.command = ""
        self.placeholder = "Пробел или Enter — начать набор"

    def update(self, before, after, correct, command=""):
        values = (before[-160:], after[:160], correct, command)
        if values != (self.before, self.after, self.correct, self.command):
            self.before, self.after, self.correct, self.command = values
            self.update_property([Gtk.AccessibleProperty.LABEL], [
                ("Сочетание: " + command.strip("⟦⟧")) if command else
                "Следующий символ — " + self.printable(self.after[:1], current=True)])
            self.queue_draw()

    @staticmethod
    def printable(value, current=False):
        return value.replace("\n", "↵").replace("\t", "⇥")

    def layout(self, value, font):
        layout = self.create_pango_layout(value)
        layout.set_font_description(font)
        layout.set_single_paragraph_mode(True)
        return layout

    def do_snapshot(self, snapshot):
        width, height = self.get_width(), self.get_height()
        if width <= 0 or height <= 0:
            return
        pivot = width / 2
        font = Pango.FontDescription.from_string("Roboto")
        font.set_absolute_size(min(49, max(35, width / 22)) * Pango.SCALE)

        def rect(x, y, w, h):
            value = Graphene.Rect()
            value.init(x, y, w, h)
            return value

        def draw(label, x, y, ink):
            point = Graphene.Point()
            point.init(x, y)
            snapshot.save()
            snapshot.translate(point)
            snapshot.append_layout(label, rgba(ink))
            snapshot.restore()

        snapshot.push_clip(rect(0, 0, width, height))
        snapshot.append_color(rgba("#1e1d25"), rect(0, 0, pivot, height))
        snapshot.append_color(rgba("#28272e"), rect(pivot, 0, width - pivot, height))
        if not self.before and not self.after:
            if self.placeholder:
                font.set_absolute_size(15 * Pango.SCALE)
                hint = self.layout(self.placeholder, font)
                hint_width, hint_height = hint.get_pixel_size()
                draw(hint, (width - hint_width) / 2, (height - hint_height) / 2, "#cac9cf")
        else:
            # Shape one layout so the caret, surrounding glyphs and baseline
            # all use the same kerning and fallback font metrics.
            before, after = self.printable(self.before), self.printable(self.after)
            if self.command:
                token_width = self.layout(self.command, font).get_pixel_size()[0]
                room = max(80, width / 2 - 16)
                if token_width > room:
                    font.set_absolute_size(font.get_size() * room / token_width)
            layout = self.layout(before + after, font)
            before_end = len(before.encode("utf-8"))
            caret, _ = layout.get_cursor_pos(before_end)
            attrs = Pango.AttrList()
            ranges = [(0, before_end, (0x9696, 0x9191, 0x9e9e))]
            if after and (self.command or not self.correct):
                count = len((self.command or after[0]).encode("utf-8"))
                tone = (0xcbcb, 0xbaba, 0xeaea) if self.correct else (0xffff, 0x6868, 0x7b7b)
                ranges.append((before_end, before_end + count, tone))
            for start_index, end_index, (red, green, blue) in ranges:
                attr = Pango.attr_foreground_new(red, green, blue)
                attr.start_index, attr.end_index = start_index, end_index
                attrs.insert(attr)
            layout.set_attributes(attrs)
            _, line_height = layout.get_pixel_size()
            draw(layout, pivot - caret.x / Pango.SCALE, (height - line_height) / 2, "#e4e0e8")
        snapshot.pop()


def series_chart(points, peak_label, footer, accessibility):
    """Render a compact result chart with a rose dot for each recorded miss."""
    width, height = 620, 180
    canvas = cairo.ImageSurface(cairo.FORMAT_ARGB32, width, height)
    ctx = cairo.Context(canvas)
    start_x, end_x, top, bottom = 49, 601, 20, 140
    maximum = max((value for _, value, _ in points if value is not None), default=0) or 1
    end = max((position for position, _, _ in points), default=1) or 1

    def label(value, x, y):
        color(ctx, "#a9aaa9")
        ctx.move_to(x, y)
        ctx.show_text(value)

    ctx.select_font_face("Noto Sans")
    ctx.set_font_size(11)
    for index in range(5):
        y = top + index * (bottom - top) / 4
        color(ctx, "#353c39")
        ctx.set_line_width(1)
        ctx.move_to(start_x, y)
        ctx.line_to(end_x, y)
        ctx.stroke()
        label(str(round(maximum * (4 - index) / 4)), 12, y + 4)
    label(peak_label, start_x, 14)
    label("0", start_x, bottom + 22)
    label(footer(end), end_x - 75, bottom + 22)
    controls = []
    error_points = []
    for position, value, missed in points:
        x = start_x + position / end * (end_x - start_x)
        if value is not None:
            y = bottom - value / maximum * (bottom - top)
            controls.append((x, y))
        if missed:
            error_points.append((x, bottom - 4))
    if controls:
        color(ctx, "#388df8")
        ctx.set_line_width(2.4)
        ctx.move_to(*controls[0])
        for x, y in controls[1:]:
            ctx.line_to(x, y)
        ctx.stroke()
        for x, y in controls:
            ctx.arc(x, y, 3, 0, math.tau)
            ctx.fill()
    color(ctx, "#e44c53")
    for x, y in error_points:
        ctx.arc(x, y, 3.8, 0, math.tau)
        ctx.fill()
    return ChartView(texture(canvas), accessibility)
