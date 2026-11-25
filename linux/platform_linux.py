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

# GDK uses XKB keycodes for both its Wayland and X11 backends. These are physical positions.
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


@lru_cache(maxsize=1)
def _x11_library():
    try:
        x11 = ctypes.CDLL("libX11.so.6")
        x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x11.XOpenDisplay.restype = ctypes.c_void_p
        x11.XkbGetState.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.POINTER(_XkbState)]
        x11.XkbGetState.restype = ctypes.c_int
        x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
        x11.XCloseDisplay.restype = ctypes.c_int
        return x11
    except (OSError, AttributeError):
        return None


def _x11_active_group(display_name):
    # On GTK < 4.18 a Tab, Space or number event's effective group need not
    # be the actual XKB group. Read the same already-open app display without
    # changing its layout. Only queried at session start, not per keystroke.
    if not display_name or (x11 := _x11_library()) is None:
        return None
    connection = x11.XOpenDisplay(os.fsencode(display_name))
    if not connection:
        return None
    try:
        state = _XkbState()
        return state.group if x11.XkbGetState(connection, 0x0100, ctypes.byref(state)) == 0 else None
    finally:
        x11.XCloseDisplay(connection)


# The same physical ANSI position from which GDK reports an A or Ф event.
_LAYOUT_PROBE = next(code for code, key in PHYSICAL.items() if key == "A")


def layout_for_group(Gdk, display, group):
    if group is None or group < 0:
        return None
    success, keyval, *_ = display.translate_key(_LAYOUT_PROBE, Gdk.ModifierType(0), group)
    if success and (symbol := Gdk.keyval_to_unicode(keyval)):
        return {"a": "EN", "ф": "RU"}.get(chr(symbol).lower())
    return None


def active_keyboard_layout(Gdk, display):
    """Read the actual layout at a user action, independent of its key or glyph."""
    if display is None:
        return None
    seat = display.get_default_seat()
    keyboard = seat.get_keyboard() if seat else None
    index = getattr(keyboard, "get_active_layout_index", None)
    if index and (layout := layout_for_group(Gdk, display, index())):
        return layout
    if display.__gtype__.name == "GdkX11Display":
        return layout_for_group(Gdk, display, _x11_active_group(display.get_name()))

    # Older Wayland may supply a fresh, single-layout keymap for each source.
    # Never select the first of several groups as if it were the active one.
    success, entries, _ = display.map_keycode(_LAYOUT_PROBE)
    if success:
        groups = {entry.group for entry in entries}
        if len(groups) == 1:
            return layout_for_group(Gdk, display, groups.pop())
    return None


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
