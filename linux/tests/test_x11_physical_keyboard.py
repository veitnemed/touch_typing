"""Physical names remain stable across the installed X.Org PC keycode sets."""
import ctypes as C
import re
import shutil
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from platform_linux import (
    PHYSICAL, PhysicalKeyboard, _XkbDescription, _XkbKeyName, _XkbNames, _x11_physical_codes,
    active_keyboard_layout,
)


Gdk = SimpleNamespace(keyval_name=lambda value: value, keyval_to_unicode=lambda value: value,
                      ModifierType=lambda bits: bits)


def display(kind="X11"):
    keyboard = SimpleNamespace(get_active_layout_index=lambda: 1)
    return SimpleNamespace(__gtype__=SimpleNamespace(name=f"Gdk{kind}Display"), get_name=lambda: ":29",
                           get_default_seat=lambda: SimpleNamespace(get_keyboard=lambda: keyboard),
                           translate_key=Mock(return_value=(True, ord("ф"), 1, 0, 0)))


def names_reply(entries):
    keys = (_XkbKeyName * 256)()
    for code, name in entries:
        keys[code].name = name.encode("ascii")
    return C.pointer(_XkbDescription(min_key_code=8, max_key_code=255,
                                     names=C.pointer(_XkbNames(keys=keys))))


class X11PhysicalKeyboardTest(unittest.TestCase):
    @unittest.skipUnless(shutil.which("xkbcomp"), "X.Org keymap compiler not installed")
    def test_installed_xfree86_and_evdev_maps_include_exactly_our_pc_physical_keys(self):
        for keycodes, down, keypad_enter, home, right_ctrl, left_super in (
            ("xfree86", 104, 108, 97, 109, 115), ("evdev", 116, 104, 110, 105, 133),
        ):
            with self.subTest(keycodes=keycodes):
                xkb = f'''xkb_keymap {{
                  xkb_keycodes {{ include "{keycodes}+aliases(qwerty)" }};
                  xkb_types {{ include "complete" }};
                  xkb_compat {{ include "complete" }};
                  xkb_symbols {{ include "pc+us+ru:2" }};
                }};'''
                compiled = subprocess.run(["xkbcomp", "-w", "0", "-xkb", "-", "-"], input=xkb, text=True,
                                          capture_output=True, check=True, timeout=10).stdout
                entries = re.findall(r"<([A-Z0-9]{1,4})>\s*=\s*(\d+)\s*;", compiled)
                descriptor = names_reply((int(code), name) for name, code in entries if int(code) <= 255)
                x11 = SimpleNamespace(XOpenDisplay=Mock(return_value=7), XkbAllocKeyboard=Mock(return_value=descriptor),
                                      XkbGetNames=Mock(return_value=0), XkbFreeKeyboard=Mock(), XCloseDisplay=Mock())
                with patch("platform_linux._x11_library", return_value=x11):
                    keys = PhysicalKeyboard(Gdk, display())
                self.assertEqual(set(keys.codes.values()), set(PHYSICAL.values()))
                x11.XkbGetNames.assert_called_once_with(7, 1 << 9, descriptor)
                x11.XkbFreeKeyboard.assert_called_once_with(descriptor, 0, 1)
                x11.XCloseDisplay.assert_called_once_with(7)
                for symbol, code, expected in (
                    ("Down", down, "Down"), ("KP_Enter", keypad_enter, "NumEnter"),
                    ("Home", home, "Home"), ("Control_R", right_ctrl, "RCtrl"), ("Super_L", left_super, "LSuper"),
                    ("KP_Home", 79, "NumPad7"), ("KP_7", 79, "NumPad7"), ("7", 16, "7"),
                    ("Cyrillic_em", 55, "V"), ("Cyrillic_i", 56, "B"), ("numerosign", 12, "3"),
                ):
                    self.assertEqual(keys.press(symbol, code), expected)
                    self.assertEqual(keys.release(symbol, code), expected)

    def test_layout_refresh_preserves_held_identity_and_uses_actual_a_position(self):
        native = display()
        with patch("platform_linux._x11_physical_codes", side_effect=[{104: "Down", 40: "A"},
                                                                    {104: "NumEnter", 38: "A"}]):
            keys = PhysicalKeyboard(Gdk, native)
            self.assertEqual(active_keyboard_layout(Gdk, native, keys.layout_probe), "RU")
            native.translate_key.assert_called_once_with(40, 0, 1)
            self.assertEqual(keys.press("Down", 104), "Down")
            keys.refresh()
            self.assertEqual(keys.press("KP_Enter", 104), "Down")  # held repeat, not a new action
            self.assertEqual(keys.release("KP_Enter", 104), "Down")
            self.assertEqual(keys.press("KP_Enter", 104), "NumEnter")
            keys.clear()  # lost focus before release
            self.assertFalse(keys.down)
            self.assertEqual(keys.layout_probe, 38)
            self.assertEqual(keys.press("Escape", 117), "")  # unknown key is not an evdev PageDown

    def test_unavailable_names_use_only_unambiguous_service_fallback(self):
        with patch("platform_linux._x11_physical_codes", return_value=None):
            keys = PhysicalKeyboard(Gdk, display())
        for symbol, code, expected in (("Down", 104, "Down"), ("KP_Enter", 108, "NumEnter"),
                                       ("Home", 97, "Home"), ("KP_Home", 79, "NumPad7"),
                                       ("Cyrillic_em", 55, "V"), ("XF86Sleep", 116, "")):
            self.assertEqual(keys.press(symbol, code), expected)
            self.assertEqual(keys.release(symbol, code), expected)

    def test_wayland_never_uses_x11_or_keysyms_for_physical_keys(self):
        with patch("platform_linux._x11_physical_codes") as load_x11:
            keys = PhysicalKeyboard(Gdk, display("Wayland"))
            self.assertEqual(keys.press("Cyrillic_em", 55), "V")
            self.assertEqual(keys.press("KP_Home", 79), "NumPad7")
            self.assertEqual(keys.press("Down", 116), "Down")
            keys.refresh()
            load_x11.assert_not_called()

    def test_protocol_failure_still_frees_descriptor_and_display(self):
        descriptor = names_reply([])
        x11 = SimpleNamespace(XOpenDisplay=Mock(return_value=7), XkbAllocKeyboard=Mock(return_value=descriptor),
                              XkbGetNames=Mock(return_value=1), XkbFreeKeyboard=Mock(), XCloseDisplay=Mock())
        with patch("platform_linux._x11_library", return_value=x11):
            self.assertIsNone(_x11_physical_codes(":29"))
        x11.XkbFreeKeyboard.assert_called_once_with(descriptor, 0, 1)
        x11.XCloseDisplay.assert_called_once_with(7)


if __name__ == "__main__":
    unittest.main()
