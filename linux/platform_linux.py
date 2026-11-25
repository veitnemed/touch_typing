"""GTK input and the local C# practice engine."""
import ctypes
from functools import lru_cache
import json
import os
from pathlib import Path
import re
import subprocess

APP_ID = "com.veitnemed.TouchTyping"
ROOT = Path(__file__).resolve().parent

# Wayland/evdev positions. An X11 server may use quite different keycodes for
# the same hardware (e.g. both XFree86 and evdev use code 104, for different keys).
ROWS = (
    (9, ["Esc"]),
    (10, list("1234567890-=")),
    (22, ["Backspace", "Tab", *"QWERTYUIOP", "[", "]", "Enter", "LCtrl"]),
    (38, [*"ASDFGHJKL", ";", "'", "`", "LShift", "\\", *"ZXCVBNM", ",", ".", "/", "RShift"]),
    (63, ["NumMultiply", "LAlt", "Space", "CapsLock"]),
    (67, [f"F{i + 1}" for i in range(10)]),
    (77, ["NumLock", "ScrollLock", "NumPad7", "NumPad8", "NumPad9", "NumSubtract",
          "NumPad4", "NumPad5", "NumPad6", "NumAdd", "NumPad1", "NumPad2", "NumPad3",
          "NumPad0", "NumDecimal"]),
    (95, ["F11", "F12"]),
    (104, ["NumEnter", "RCtrl", "NumDivide", "Print", "RAlt"]),
    (110, ["Home", "Up", "PageUp", "Left", "Right", "End", "Down", "PageDown", "Insert", "Delete"]),
    (127, ["Pause"]),
    (133, ["LSuper", "RSuper", "Menu"]),
)
PHYSICAL = {index + offset: key for index, keys in ROWS for offset, key in enumerate(keys)}
MODIFIERS = frozenset(f"{side}{family}" for side in "LR" for family in ("Ctrl", "Alt", "Shift", "Super"))
FINGERS = (
    "Левый мизинец", "Левый безымянный", "Левый средний", "Левый указательный",
    "Правый указательный", "Правый средний", "Правый безымянный", "Правый мизинец",
    "Левый большой", "Правый большой",
)
KEY_LABELS = {
    "Space": "Пробел", "Up": "↑", "Down": "↓", "Left": "←", "Right": "→", "Print": "PrtSc",
    "LCtrl": "Ctrl лев.", "RCtrl": "Ctrl прав.", "LShift": "Shift лев.", "RShift": "Shift прав.",
    "LAlt": "Alt лев.", "RAlt": "Alt прав.", "Super": "⊞", "LSuper": "⊞", "RSuper": "⊞ прав.",
}


def key_label(key):
    return KEY_LABELS.get(key, key)


def chord_label(chord):
    return " → ".join(" + ".join(key_label(token) for token in part.split("+"))
                      for part in chord.split(" → "))


# XkbStateRec, X11/extensions/XKBstr.h. Only the effective group byte is
# needed, but XkbGetState writes the complete record.
class _XkbState(ctypes.Structure):
    _fields_ = [("group", ctypes.c_ubyte), ("locked_group", ctypes.c_ubyte),
                ("base_group", ctypes.c_ushort), ("latched_group", ctypes.c_ushort),
                ("modifier_fields", ctypes.c_ubyte * 9), ("ptr_buttons", ctypes.c_ushort)]


# Stable XKB key names describe positions in a PC keyboard, not typed symbols.
# In particular <AB04> is V in EN and М in RU, <KP7> remains independent
# of NumLock, and <COMP> is the evdev spelling for <MENU>.
_XKB_POSITIONS = {
    **{f"{row}{i:02}".encode(): key for row, keys in (
        ("AE", "1234567890-="), ("AD", "QWERTYUIOP[]"),
        ("AC", "ASDFGHJKL;'"), ("AB", "ZXCVBNM,./"),
    ) for i, key in enumerate(keys, 1)},
    **{f"FK{i:02}".encode(): f"F{i}" for i in range(1, 13)},
    **{f"KP{i}".encode(): f"NumPad{i}" for i in range(10)},
    b"TLDE": "`", b"BKSL": "\\", b"SPCE": "Space", b"ESC": "Esc", b"TAB": "Tab",
    b"BKSP": "Backspace", b"RTRN": "Enter", b"CAPS": "CapsLock", b"SCLK": "ScrollLock",
    b"NMLK": "NumLock", b"PAUS": "Pause", b"PRSC": "Print", b"MENU": "Menu", b"COMP": "Menu",
    b"UP": "Up", b"DOWN": "Down", b"LEFT": "Left", b"RGHT": "Right", b"PGUP": "PageUp",
    b"PGDN": "PageDown", b"HOME": "Home", b"END": "End", b"INS": "Insert", b"DELE": "Delete",
    b"KPDL": "NumDecimal", b"KPEN": "NumEnter", b"KPDV": "NumDivide", b"KPMU": "NumMultiply",
    b"KPSU": "NumSubtract", b"KPAD": "NumAdd", b"LCTL": "LCtrl", b"RCTL": "RCtrl",
    b"LALT": "LAlt", b"RALT": "RAlt", b"LFSH": "LShift", b"RTSH": "RShift",
    b"LWIN": "LSuper", b"RWIN": "RSuper",
}

# If a server has no XKB names, only service keys with unambiguous keysyms
# and the common PC text positions are supported. Never guess an extended
# key from its evdev number, or turn KP_Home into the separate Home key.
_X11_SERVICE_KEYS = {
    **{key: key for key in ("Home", "End", "Left", "Right", "Up", "Down", "Delete", "Insert",
                            "Pause", "Print", "Menu", "Tab")},
    **{f"F{i}": f"F{i}" for i in range(1, 13)},
    **{f"KP_{i}": f"NumPad{i}" for i in range(10)},
    **{f"KP_{name}": f"NumPad{i}" for i, name in enumerate(
        ("Insert", "End", "Down", "Page_Down", "Left", "Begin", "Right", "Home", "Up", "Page_Up"))},
    "KP_Prior": "NumPad9", "KP_Next": "NumPad3", "KP_Delete": "NumDecimal", "KP_Decimal": "NumDecimal",
    "KP_Separator": "NumDecimal", "KP_Enter": "NumEnter", "KP_Divide": "NumDivide",
    "KP_Multiply": "NumMultiply", "KP_Subtract": "NumSubtract", "KP_Add": "NumAdd",
    "BackSpace": "Backspace", "Return": "Enter", "Escape": "Esc", "ISO_Left_Tab": "Tab", "space": "Space",
    "Page_Up": "PageUp", "Page_Down": "PageDown", "Prior": "PageUp", "Next": "PageDown",
    "Sys_Req": "Print", "Break": "Pause", "Caps_Lock": "CapsLock", "Scroll_Lock": "ScrollLock",
    "Num_Lock": "NumLock", "Control_L": "LCtrl", "Control_R": "RCtrl", "Alt_L": "LAlt", "Alt_R": "RAlt",
    "Shift_L": "LShift", "Shift_R": "RShift", "Super_L": "LSuper", "Super_R": "RSuper",
}
_X11_FALLBACK_TEXT = {code: key for code, key in PHYSICAL.items() if len(key) == 1}
_XKB_CORE_KEYBOARD = 0x0100
_XKB_KEY_NAMES = 1 << 9


# Public libX11 ABI in XKBstr.h. These prefixes end at the members we read;
# Xlib owns the complete allocations and frees them via XkbFreeKeyboard.
class _XkbKeyName(ctypes.Structure):
    _fields_ = [("name", ctypes.c_char * 4)]


class _XkbNames(ctypes.Structure):
    _fields_ = [(field, ctypes.c_ulong) for field in ("keycodes", "geometry", "symbols", "types", "compat")] + [
        ("vmods", ctypes.c_ulong * 16), ("indicators", ctypes.c_ulong * 32),
        ("groups", ctypes.c_ulong * 4), ("keys", ctypes.POINTER(_XkbKeyName)),
    ]


class _XkbDescription(ctypes.Structure):
    _fields_ = [("display", ctypes.c_void_p), ("flags", ctypes.c_ushort), ("device_spec", ctypes.c_ushort),
                ("min_key_code", ctypes.c_ubyte), ("max_key_code", ctypes.c_ubyte), ("ctrls", ctypes.c_void_p),
                ("server", ctypes.c_void_p), ("map", ctypes.c_void_p), ("indicators", ctypes.c_void_p),
                ("names", ctypes.POINTER(_XkbNames))]


@lru_cache(maxsize=1)
def _x11_library():
    try:
        x11 = ctypes.CDLL("libX11.so.6")
        x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x11.XOpenDisplay.restype = ctypes.c_void_p
        x11.XkbGetState.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.POINTER(_XkbState)]
        x11.XkbGetState.restype = ctypes.c_int
        description = ctypes.POINTER(_XkbDescription)
        x11.XkbAllocKeyboard.argtypes = []
        x11.XkbAllocKeyboard.restype = description
        x11.XkbGetNames.argtypes = [ctypes.c_void_p, ctypes.c_uint, description]
        x11.XkbGetNames.restype = ctypes.c_int
        x11.XkbFreeKeyboard.argtypes = [description, ctypes.c_uint, ctypes.c_int]
        x11.XkbFreeKeyboard.restype = None
        x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
        x11.XCloseDisplay.restype = ctypes.c_int
        return x11
    except (OSError, AttributeError):
        return None


def _x11_active_group(display_name):
    # On GTK < 4.18 a Tab, Space or number event's effective group need not
    # be the actual XKB group. Read the server behind the app's display
    # without changing its layout; alphabetic input doesn't need this call.
    if not display_name or (x11 := _x11_library()) is None:
        return None
    connection = x11.XOpenDisplay(os.fsencode(display_name))
    if not connection:
        return None
    try:
        state = _XkbState()
        return state.group if x11.XkbGetState(connection, _XKB_CORE_KEYBOARD, ctypes.byref(state)) == 0 else None
    finally:
        x11.XCloseDisplay(connection)



def _x11_physical_codes(display_name):
    """Read the server's current key names without modifying global input settings."""
    if not display_name or (x11 := _x11_library()) is None:
        return None
    connection = x11.XOpenDisplay(os.fsencode(display_name))
    if not connection:
        return None
    try:
        descriptor = x11.XkbAllocKeyboard()  # defaults to XkbUseCoreKbd
        if not descriptor:
            return None
        try:
            if x11.XkbGetNames(connection, _XKB_KEY_NAMES, descriptor) != 0:
                return None
            data = descriptor.contents
            keys = data.names.contents.keys if data.names else None
            if not keys or not 8 <= data.min_key_code <= data.max_key_code:
                return None
            return {code: key for code in range(data.min_key_code, data.max_key_code + 1)
                    if (key := _XKB_POSITIONS.get(keys[code].name))} or None
        finally:
            x11.XkbFreeKeyboard(descriptor, 0, 1)  # free_all ignores the component mask
    finally:
        x11.XCloseDisplay(connection)


class PhysicalKeyboard:
    """Resolve native keyboard input before dispatching it to any training mode."""
    def __init__(self, Gdk, display):
        self.gdk = Gdk
        self.display = display
        self.x11 = display is not None and display.__gtype__.name == "GdkX11Display"
        self.down = {}
        self.refresh()

    def refresh(self, *_):
        self.codes = _x11_physical_codes(self.display.get_name()) if self.x11 else PHYSICAL
        self.layout_probe = next((code for code, key in (self.codes or {}).items() if key == "A"), _LAYOUT_PROBE)

    def watch_x11(self):
        if not self.x11:
            return
        seat = self.display.get_default_seat()
        keyboard = seat.get_keyboard() if seat else None
        if keyboard is not None:
            keyboard.connect("changed", self.refresh)
            if keyboard.find_property("layout-names") is not None:  # GTK 4.18+
                keyboard.connect("notify::layout-names", self.refresh)

    def _resolve(self, keyval, keycode):
        if self.codes is not None:
            return self.codes.get(keycode, "")
        keysym = self.gdk.keyval_name(keyval)
        if keysym == "ISO_Level3_Shift" and keycode in (108, 113):
            return "RAlt"  # PC AltGr for evdev and XFree86
        return _X11_SERVICE_KEYS.get(keysym, _X11_FALLBACK_TEXT.get(keycode, ""))

    def right_alt_mode(self):
        """Describe the installed on-screen position without changing the OS map."""
        code = next((code for code, key in (self.codes or {}).items() if key == "RAlt"), None)
        if code is None or self.display is None:
            return "unknown"
        found, entries, keyvals = self.display.map_keycode(code)
        if not found:
            return "unknown"
        # A neutral key may report the effective group 0 even with another
        # group active. Ask the device/XKB only if this position varies.
        groups = {entry.group for entry in entries}
        group = None
        if len(groups) > 1:
            seat = self.display.get_default_seat()
            keyboard = seat.get_keyboard() if seat else None
            index = getattr(keyboard, "get_active_layout_index", None)
            group = index() if index else None
            if group not in groups and self.x11:
                group = _x11_active_group(self.display.get_name())
        names = [(entry.level, self.gdk.keyval_name(value)) for entry, value in zip(entries, keyvals)
                 if group not in groups or group == entry.group]
        if any(name and (name.startswith(("ISO_Level3", "ISO_Level5")) or name == "Mode_switch") for _, name in names):
            return "altgr"
        base = {name for level, name in names if level == 0 and name}
        return "alt" if base and base <= {"Alt_R", "Alt_L"} else "unknown"

    def press(self, keyval, keycode):
        if keycode not in self.down:
            self.down[keycode] = self._resolve(keyval, keycode)
        return self.down[keycode]

    def release(self, keyval, keycode):
        if keycode in self.down:
            return self.down.pop(keycode)
        return self._resolve(keyval, keycode)

    def clear(self):
        self.down.clear()


# The same physical ANSI position from which GDK reports an A or Ф event.
_LAYOUT_PROBE = next(code for code, key in PHYSICAL.items() if key == "A")


def layout_for_group(Gdk, display, group, probe=_LAYOUT_PROBE):
    if group is None or group < 0:
        return None
    success, keyval, *_ = display.translate_key(probe, Gdk.ModifierType(0), group)
    if success and (symbol := Gdk.keyval_to_unicode(keyval)):
        return {"a": "EN", "ф": "RU"}.get(chr(symbol).lower())
    return None


def active_keyboard_layout(Gdk, display, probe=_LAYOUT_PROBE):
    """Read the actual layout at a user action, independent of its key or glyph."""
    if display is None:
        return None
    seat = display.get_default_seat()
    keyboard = seat.get_keyboard() if seat else None
    index = getattr(keyboard, "get_active_layout_index", None)
    if index and (layout := layout_for_group(Gdk, display, index(), probe)):
        return layout
    if display.__gtype__.name == "GdkX11Display":
        return layout_for_group(Gdk, display, _x11_active_group(display.get_name()), probe)

    # Older Wayland may supply a fresh, single-layout keymap for each source.
    # Never select the first of several groups as if it were the active one.
    success, entries, _ = display.map_keycode(probe)
    if success:
        groups = {entry.group for entry in entries}
        if len(groups) == 1:
            return layout_for_group(Gdk, display, groups.pop(), probe)
    return None


def keyboard_layout_for_event(Gdk, display, character, effective_group, probe=_LAYOUT_PROBE):
    """Resolve physical input language, not a shared glyph's effective group."""
    if character and character.isascii() and character.isalpha():
        return "EN"
    if character and ("а" <= character.lower() <= "я" or character.lower() == "ё"):
        return "RU"
    # X11 Space, digits, navigation and modifiers may all be rendered from
    # group zero while the active group remains Russian. If active state
    # can't be read, keep the prior layout instead of guessing from the key.
    active = active_keyboard_layout(Gdk, display, probe)
    if active or display is None or display.__gtype__.name == "GdkX11Display":
        return active
    # A legacy Wayland compositor may expose the group only on the event.
    return layout_for_group(Gdk, display, effective_group, probe)


class EngineError(Exception):
    pass


class Engine:
    def __init__(self, verify=False):
        command = [str(ROOT / "engine" / "TouchTyping.Engine")]
        if override := os.environ.get("TOUCH_TYPING_ENGINE"):
            command[0] = override
        elif not Path(command[0]).is_file():
            command[0] = str(ROOT / "engine/bin/Debug/net10.0/TouchTyping.Engine")
        if verify:
            command.append("--verify")
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        text=True, encoding="utf-8")
        self.storage_error = ""

    def call(self, operation, **payload):
        try:
            self.process.stdin.write(json.dumps({"command": operation, **payload}, ensure_ascii=False) + "\n")
            self.process.stdin.flush()
            line = self.process.stdout.readline()
        except (BrokenPipeError, OSError) as exc:
            raise EngineError("Связь с учебным движком потеряна.") from exc
        if not line:
            raise EngineError("Учебный движок завершился.")
        reply = json.loads(line)
        if not reply["ok"]:
            raise EngineError(reply["error"])
        self.storage_error = reply.get("storageError")
        return reply["value"]

    def close(self):
        if self.process.stdin and not self.process.stdin.closed:
            self.process.stdin.close()
        try:
            self.process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            self.process.wait(timeout=2)
        self.process.stdout.close()


class InputSources:
    def __init__(self, Gio):
        schema_source = Gio.SettingsSchemaSource.get_default()
        self.wm = "org.gnome.desktop.wm.keybindings"
        self.languages = "org.gnome.desktop.input-sources"
        self.settings = {name: Gio.Settings.new(name) for name in (self.wm, self.languages, "org.gnome.shell")
                         if schema_source and schema_source.lookup(name, True)}

    @staticmethod
    def as_chord(binding):
        modifiers = {"primary": "Ctrl", "ctrl": "Ctrl", "control": "Ctrl", "shift": "Shift",
                     "super": "Super", "alt": "Alt", "meta": "Super"}
        tokens = [modifiers.get(name.lower(), "") for name in re.findall(r"<([^>]+)>", binding)]
        key = re.sub(r"<[^>]+>", "", binding).strip()
        names = {"space": "Space", "iso_left_tab": "Tab", "esc": "Esc", "escape": "Esc",
                 "page_up": "PageUp", "page_down": "PageDown", "prior": "PageUp", "next": "PageDown",
                 "above_tab": "`", "grave": "`", "return": "Enter", "minus": "-", "equal": "="}
        key = names.get(key.lower(), key)
        if not key or key.startswith("XF86") or not all(tokens):
            return ""
        return "+".join([*tokens, key])

    def bindings(self):
        wm = self.settings.get(self.wm)
        if not wm:
            return "", ""
        def first(key):
            return next((chord for raw in wm.get_strv(key) if (chord := self.as_chord(raw))), "")
        return first("switch-input-source"), first("switch-input-source-backward")

    def desktop_bindings(self):
        wm = self.settings.get(self.wm)
        if wm is None:
            return {}
        # Only read the current profile; missing/unsupported bindings are
        # left out so the simulator can use its documented defaults.
        available = set(wm.list_keys())
        keys = {
            "workspace-next": ("switch-to-workspace-right",),
            "workspace-prev": ("switch-to-workspace-left",),
            "move-next": ("move-to-workspace-right",),
            "move-prev": ("move-to-workspace-left",),
            "switch-next": ("switch-windows", "switch-applications"),
            "switch-prev": ("switch-windows-backward", "switch-applications-backward"),
            "switch-group-next": ("switch-group",),
            "switch-group-prev": ("switch-group-backward",),
        }
        supported = re.compile(r"(?:[A-Z0-9]|F(?:1[0-2]|[1-9])|Page(?:Up|Down)|Up|Down|Left|Right|"
                               r"Home|End|Insert|Delete|Esc|Tab|Space|Enter|Backspace|[`=\-])", re.I)
        result = {}
        for action, schemas in keys.items():
            for key in schemas:
                if key not in available:
                    continue
                for raw in wm.get_strv(key):
                    chord = self.as_chord(raw)
                    modifiers, sep, trigger = chord.rpartition("+")
                    if (sep and supported.fullmatch(trigger)
                            and any(mod in modifiers.split("+") for mod in ("Ctrl", "Alt", "Super"))):
                        result[action] = chord
                        break
                if action in result:
                    break
        return result

    def dock(self):
        shell = self.settings.get("org.gnome.shell")
        if shell is None:
            return ["chrome", "code", "terminal", "files"]
        known = {"org.gnome.Nautilus": "files", "google-chrome": "chrome", "chromium": "chrome",
                 "org.gnome.Ptyxis": "terminal", "org.gnome.Terminal": "terminal",
                 "org.gnome.Console": "terminal", "code": "code", "com.microsoft.VSCode": "code"}
        return [known.get(name.removesuffix(".desktop"), name.removesuffix(".desktop")) for name in shell.get_strv("favorite-apps")[:9]]

    def guess(self, fallback="EN"):
        settings = self.settings.get(self.languages)
        if settings:
            for index in settings.get_value("mru-sources").unpack():
                if index[0] == "xkb":
                    return "RU" if index[1].split("+")[0] == "ru" else "EN"
        return fallback
