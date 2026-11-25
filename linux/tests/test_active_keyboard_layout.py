"""Keyboard group selection without requiring GTK or an X11 connection."""
import ctypes as C
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from platform_linux import _XkbState, _x11_active_group, active_keyboard_layout

Gdk = SimpleNamespace(ModifierType=lambda bits: bits, keyval_to_unicode=lambda value: value)


def display(groups, backend="GdkWaylandDisplay", active=None):
    keyboard = SimpleNamespace() if active is None else SimpleNamespace(get_active_layout_index=lambda: active)
    seat = SimpleNamespace(get_keyboard=lambda: keyboard)
    return SimpleNamespace(__gtype__=SimpleNamespace(name=backend), get_name=lambda: ":29", get_default_seat=lambda: seat,
                           map_keycode=Mock(return_value=(True, [SimpleNamespace(group=g) for g in groups], [])),
                           translate_key=Mock(side_effect=lambda k, m, g: (g in groups, ord(groups.get(g, "?")), g, 0, 0)))


class ActiveKeyboardLayoutTest(unittest.TestCase):
    def test_current_gdk_group_uses_physical_a_and_is_not_the_first_group(self):
        primary = display({0: "a", 1: "ф"}, active=1)
        self.assertEqual(active_keyboard_layout(Gdk, primary), "RU")
        primary.translate_key.assert_called_once_with(38, 0, 1)

    def test_legacy_x11_queries_server_group_instead_of_space_or_tab_effective_group(self):
        primary = display({0: "a", 1: "ф"}, backend="GdkX11Display")
        with patch("platform_linux._x11_active_group", return_value=1) as server:
            self.assertEqual(active_keyboard_layout(Gdk, primary), "RU")
            server.assert_called_once_with(":29")
        primary.translate_key.assert_called_once_with(38, 0, 1)

    def test_gdk_invalid_group_falls_through_but_never_assumes_ambiguous_first_group(self):
        self.assertIsNone(active_keyboard_layout(Gdk, display({0: "a", 1: "ф"}, active=-1)))
        self.assertEqual(active_keyboard_layout(Gdk, display({0:"ф"}, active=-1)), "RU")
        self.assertIsNone(active_keyboard_layout(Gdk, display({0:"q"}, active=0)))
        self.assertIsNone(active_keyboard_layout(Gdk, None))

    def test_xkb_probe_returns_active_not_locked_byte_and_closes_connection(self):
        def fill(connection, device, record):
            self.assertEqual((connection, device), (7, 0x0100))
            row = C.cast(record, C.POINTER(_XkbState)).contents
            row.locked_group, row.group = 0, 1
            return 0  # Success, unlike Xlib Bool

        x11 = SimpleNamespace(XOpenDisplay=Mock(return_value=7),
                              XkbGetState=Mock(side_effect=fill),
                              XCloseDisplay=Mock())
        with patch("platform_linux._x11_library", return_value=x11):
            self.assertEqual(_x11_active_group(":29"), 1)
            x11.XOpenDisplay.assert_called_once_with(b":29")
            x11.XCloseDisplay.assert_called_once_with(7)
            x11.XkbGetState.side_effect = None
            x11.XkbGetState.return_value = 1
            self.assertIsNone(_x11_active_group(":29"))
            self.assertEqual(x11.XCloseDisplay.call_count, 2)
            x11.XOpenDisplay.return_value = None
            self.assertIsNone(_x11_active_group(":29"))
            self.assertEqual(x11.XCloseDisplay.call_count, 2)


if __name__ == "__main__":
    unittest.main()
