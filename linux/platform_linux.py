"""GTK input and the local C# practice engine."""
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
        names = {"space": "Space", "iso_left_tab": "Tab", "esc": "Esc", "escape": "Esc"}
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

    def dock(self):
        shell = self.settings.get("org.gnome.shell")
        if shell is None:
            return ["chrome", "code", "terminal", "files"]
        known = {"org.gnome.Nautilus": "files", "google-chrome": "chrome", "chromium": "chrome",
                 "org.gnome.Ptyxis": "terminal", "org.gnome.Terminal": "terminal",
                 "org.gnome.Console": "terminal", "code": "code", "com.microsoft.VSCode": "code"}
        return [known.get(name.removesuffix(".desktop"), name.removesuffix(".desktop")) for name in shell.get_strv("favorite-apps")[:9]]

    def guess(self):
        settings = self.settings.get(self.languages)
        if settings:
            for index in settings.get_value("mru-sources").unpack():
                if index[0] == "xkb":
                    return "RU" if index[1].split("+")[0] == "ru" else "EN"
        return "EN"
