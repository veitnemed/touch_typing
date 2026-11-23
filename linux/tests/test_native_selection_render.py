"""Render deterministic editor and browser scene states in a real GTK window (no engine/input claims).

Run with the Linux GTK dependencies and a desktop display:
  PYTHONPATH=linux /usr/bin/python3 linux/tests/test_native_selection_render.py OUTPUT_DIR
"""
from pathlib import Path
import sys
import time
import traceback

import gi
gi.require_version("Gtk", "4.0")
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("Graphene", "1.0")
gi.require_version("Pango", "1.0")
from gi.repository import GdkPixbuf, GLib, Graphene, Gtk, Pango

from lab_view import DesktopView


def verify(window, view, output):
    selection = bytes((53, 98, 155))
    caret_color = bytes((210, 220, 230))
    first = "е\u0301🙂"
    end = view.units(first)
    assert end == 4
    layout = view.text_layout(first, 14, mono=True)
    after_cluster = int(layout.get_cursor_pos(len("е\u0301".encode()))[0].x / Pango.SCALE)
    after_emoji = int(layout.get_cursor_pos(len(first.encode()))[0].x / Pango.SCALE)
    assert 0 < after_cluster < after_emoji
    origin = 35 + 43  # Activity rail + line-number gutter, sidebar disabled.
    def capture(name, anchor, cursor, separator="\n"):
        view.update({"windows": [{"id": "code-test", "app": "code", "active": True,
                     "sidebar": False, "focus": "body", "title": "notes.txt",
                     "body": first + separator + "Я", "bodyAnchor": anchor, "bodyCursor": cursor}]})
        return capture_frame(name, start=origin)

    def capture_frame(name, start=0):
        paintable = Gtk.WidgetPaintable.new(view)
        ctx = GLib.MainContext.default()
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline:
            for _ in range(60):
                if not ctx.iteration(False):
                    break
            frame = Gtk.Snapshot()
            paintable.snapshot(frame, view.get_width(), view.get_height())
            node = frame.to_node()
            if node and view.get_mapped() and window.get_renderer():
                rect = Graphene.Rect()
                rect.init(0, 0, view.get_width(), view.get_height())
                path = output / (name + ".png")
                assert window.get_renderer().render_texture(node, rect).save_to_png(str(path))
                image = GdkPixbuf.Pixbuf.new_from_file(str(path))
                data = image.get_pixels()
                stride, channels = image.get_rowstride(), image.get_n_channels()
                def row(y, color, tolerance=0):
                    matches = []
                    for x in range(start, image.get_width()):
                        i = y * stride + x * channels
                        if all(abs(actual - expected) <= tolerance
                               for actual, expected in zip(data[i:i+3], color)):
                            matches.append(x)
                    return matches
                return row
            view.queue_draw()
            time.sleep(.015)
        raise AssertionError("No actual GTK rendered frame for " + name)
    before_emoji = capture("caret-after-combining-mark", 2, 2)
    assert any(abs(x - origin - after_cluster) <= 1 for x in before_emoji(73, caret_color, 25))
    assert before_emoji(73, selection) == []
    after_first = capture("caret-after-emoji", end, end)
    assert any(abs(x - origin - after_emoji) <= 1 for x in after_first(73, caret_color, 25))
    assert after_first(73, selection) == []
    newline = capture("selected-line-break", end, end + 1)
    first_row = newline(73, selection)
    assert first_row and abs(first_row[0] - origin - after_emoji) <= 1, first_row
    assert newline(95, selection) == [], "next glyph was not selected"
    backward = capture("selected-line-break-backward", end + 1, end)
    assert backward(73, selection) and backward(95, selection) == []
    across = capture("selected-across-lines", end + 2, 2)
    assert across(73, selection)[-1] == first_row[-1]
    assert across(95, selection)[0] == origin
    eof = capture("caret-at-file-end", end + 2, end + 2)
    assert eof(95, selection) == [], "EOF must not show a nonexistent line break"
    crlf = capture("selected-crlf", end, end + 2, "\r\n")
    assert crlf(73, selection) == first_row
    assert crlf(95, selection) == []
    assert any(abs(x - origin) <= 1 for x in crlf(95, caret_color, 25)), "CRLF advances two UTF-16 units"
    # Separate native browser state: if drawing aborts inside the body loop,
    # the shell/title alone can leave a nonempty render node. Require actual
    # text ink and selection coverage from the second body row as well.
    view.presentation_app = "chrome"
    browser_first, browser_second = "Русский абзац", "Вторая строка"
    browser_offset = view.units(browser_first) + 1
    browser = {"id": "chrome-test", "app": "chrome", "active": True, "focus": "body",
               "url": "https://docs.example.test/lesson", "body": browser_first + "\n" + browser_second, "activeTab": 0,
               "tabs": [{"title": "Учебная страница"}]}
    view.update({"windows": [dict(browser, bodyAnchor=0, bodyCursor=0)]})
    ordinary = capture_frame("chrome-multiline-body")
    def body_ink(frame, top):
        return sum(len(frame(y, bytes((51, 65, 84)), 12)) for y in range(top, top + 20))
    assert body_ink(ordinary, 131) > 20
    assert body_ink(ordinary, 154) > 20, "Chrome rendered shell but not the second paragraph"
    assert ordinary(170, selection) == []

    view.update({"windows": [dict(browser, bodyAnchor=browser_offset,
                                 bodyCursor=browser_offset + view.units("Вторая"))]})
    selected = capture_frame("chrome-second-line-selection")
    assert selected(147, selection) == [], "Chrome highlight leaked to the first paragraph"
    assert len(selected(170, selection)) >= 10, "Chrome second-line selection was not rendered"
    print(f"PASS real GTK editor and multi-line Chrome selection, combining mark and emoji offsets {after_cluster}/{after_emoji}px, "
          f"newline marker {len(first_row)}px; frames={output}", flush=True)


def main():
    output = Path(sys.argv[1]).resolve()
    output.mkdir(parents=True, exist_ok=True)
    app = Gtk.Application(application_id="com.touchtyping.SelectionRenderTest")
    status = 1
    def activate(application):
        nonlocal status
        window = Gtk.ApplicationWindow(application=application, default_width=640, default_height=360)
        view = DesktopView()
        view.presentation_app = "code"
        window.set_child(view)
        window.present()
        def run():
            nonlocal status
            try:
                verify(window, view, output)
                status = 0
            except Exception:
                traceback.print_exc()
            application.quit()
            return GLib.SOURCE_REMOVE
        GLib.timeout_add(100, run)
    app.connect("activate", activate)
    app.run([sys.argv[0]])
    return status


if __name__ == "__main__":
    sys.exit(main())
