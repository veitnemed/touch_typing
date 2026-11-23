"""Headless renderer-boundary tests; these don't claim GTK pixel/native input QA."""
import importlib.util
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch


class Point:
    def init(self, x, y):
        self.x, self.y = x, y


class Layout:
    def __init__(self, value):
        self.value = value

    def set_font_description(self, font):
        self.font = font

    def get_cursor_pos(self, index):
        return (SimpleNamespace(x=len(self.value.encode()[:index].decode()) * 10),)


class Drawing:
    def __init__(self):
        self.calls = []

    def __getattr__(self, name):
        return lambda *args: self.calls.append((name, args))


def load_view():
    def font(family):
        return SimpleNamespace(family=family, set_absolute_size=lambda _: None,
                               set_weight=lambda _: None)

    repo = SimpleNamespace(
        Gdk=None, GdkPixbuf=None, Gio=None, GLib=None, Gsk=None,
        Gtk=SimpleNamespace(Widget=object),
        Graphene=SimpleNamespace(Point=Point),
        Pango=SimpleNamespace(SCALE=1, FontDescription=SimpleNamespace(from_string=font),
                              Weight=SimpleNamespace(BOLD=700)),
    )
    spec = importlib.util.spec_from_file_location("lab_view_test", Path(__file__).parents[1] / "lab_view.py")
    module = importlib.util.module_from_spec(spec)
    with patch.dict(sys.modules, {"gi": SimpleNamespace(require_version=lambda *_: None),
                                  "gi.repository": repo}):
        spec.loader.exec_module(module)
    module.ink = lambda value: value
    return module.DesktopView


class LiveVisualFields(unittest.TestCase):
    def setUp(self):
        view = load_view()
        self.view = object.__new__(view)
        self.view.scene = {}
        self.view.reference_chrome = False
        self.view.presentation_app = "ubuntu"
        self.view.field_scroll = {}
        self.view.create_pango_layout = Layout
        self.view.rect = lambda *values: values
        self.view.snapshot = Drawing()
        self.paints = []
        self.labels = []
        self.view.fill = lambda *args: self.paints.append(args)
        self.view.label = lambda *args, **kwargs: self.labels.append(args)

    def test_long_terminal_field_caret_and_selection_share_sticky_viewport(self):
        value = "abcdefghijklm"
        key = ("terminal", 1)
        self.view.field(value, {"cursor": 13, "anchor": 13}, 0, 0, 50, 12, "ink",
                        scroll_key=key)
        assert self.view.field_scroll[key] == 83
        assert self.paints[-1][1] == 47
        self.paints.clear()
        self.view.field(value, {"cursor": 12, "anchor": 13}, 0, 0, 50, 12, "ink",
                        scroll_key=key)
        assert self.view.field_scroll[key] == 83
        assert self.paints == [("#35629b", 37, 0, 10, 12 * 1.45),
                               ("ink", 37, 1, 1.5, 12 * 1.35)]
        self.view.field(value, {"cursor": 0, "anchor": 0}, 0, 0, 50, 12, "ink",
                        scroll_key=key)
        assert self.view.field_scroll[key] == 0

    def test_chrome_find_draws_editable_selection(self):
        for reference, background in ((False, "#202124"), (True, "#201d36")):
            with self.subTest(reference=reference):
                self.view.reference_chrome = reference
                self.view.presentation_app = "chrome"
                self.paints.clear()
                self.view.snapshot.calls.clear()
                self.view.chrome({"focus": "find", "url": "about:blank", "input": "abc",
                                  "cursor": 2, "anchor": 0}, 0, 0, 520, 360)
                assert self.paints[0][0] == background
                assert any(p[0] == "#35629b" and p[4] > 0 for p in self.paints)
                assert any(p[0] == "#ffffff" and p[3] == 1.5 for p in self.paints)
                families = {args[0].value: args[0].font.family
                            for method, args in self.view.snapshot.calls if method == "append_layout"}
                family = "Roboto,Noto Sans,sans-serif" if reference else "Noto Sans"
                assert families["abc"] == families["about:blank"] == family

    def test_reference_browser_face_does_not_replace_monospace_fields(self):
        self.view.reference_chrome = True
        self.view.presentation_app = "chrome"
        for family in (self.view.chrome_font, "Noto Sans"):
            layout = self.view.text_layout("абc", 13, mono=True, family=family)
            assert layout.font.family == "DejaVu Sans Mono"

    def test_overflowing_code_tabs_show_active_tab_not_just_its_body_name(self):
        tabs = [{"title": f"file{i}-long-python-module.py"} for i in range(12)]
        title = tabs[9]["title"]
        for width, sidebar, rail, header_top, tab_row in (
                (520, False, 35, 0, 28), (1024, True, 48, 29, 34)):
            with self.subTest(width=width):
                self.view.presentation_app = "code"
                x, y = 13, 19
                labels = []
                self.paints.clear()
                self.view.label = lambda *args, **options: labels.append((args, options))
                self.view.app_icon = lambda *_: None
                self.view.code({"tabs": tabs, "activeTab": 9, "sidebar": sidebar}, x, y, width, 360)

                top = y + header_top
                header = [(args, options) for args, options in labels if top <= args[2] < top + tab_row]
                header_files = {args[0] for args, _ in header if args[0].startswith("file")}
                self.assertIn(title, header_files)
                self.assertNotIn(tabs[0]["title"], header_files)
                self.assertNotIn(tabs[-1]["title"], header_files)
                self.assertTrue(any(args[0] == title and args[2] > top + tab_row for args, _ in labels),
                                "the same name should also occur in the editor breadcrumb")

                (active, active_options), = [(args, options) for args, options in header if args[0] == title]
                left_arrow, = [args for args, _ in header if args[0] == "‹"]
                right_arrow, = [args for args, _ in header if args[0] == "›"]
                highlight, = [p for p in self.paints if p[0] == "#f78166" and p[2] == top and p[4] == 2]
                _, active_left, _, active_width, _ = highlight
                active_right = active_left + active_width
                self.assertEqual(active[4], "#e6edf3")  # GitHub Dark active label
                self.assertGreater(active_left, max(x + rail, left_arrow[1]))
                self.assertLess(active_right, min(x + width, right_arrow[1]))
                self.assertGreater(active[1], active_left)
                self.assertGreater(active_options["width"], 0)
                self.assertLess(active[1] + active_options["width"], active_right,
                                "long active filenames need a bounded ellipsis before tab controls")


if __name__ == "__main__":
    unittest.main()
