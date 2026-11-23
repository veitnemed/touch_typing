#!/usr/bin/env bash
set -euo pipefail

bundle=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ ! -x "$bundle/engine/TouchTyping.Engine" ]]; then
    echo 'Распакуйте готовый архив Linux целиком и запустите install.sh из этой папки.' >&2
    exit 2
fi
if ! /usr/bin/python3 -c 'import cairo, gi; gi.require_version("Gtk", "4.0"); from gi.repository import Gtk' 2>/dev/null; then
    echo 'Установите пакеты python3-gi, python3-cairo и gir1.2-gtk-4.0, затем снова запустите install.sh.' >&2
    exit 2
fi

data_home=${XDG_DATA_HOME:-"$HOME/.local/share"}
applications="$data_home/applications"
releases="$data_home/touch-typing/releases"
desktop=$(xdg-user-dir DESKTOP 2>/dev/null || true)
desktop=${desktop:-"$HOME/Desktop"}
mkdir -p -- "$releases" "$applications" "$desktop"
destination=$(mktemp -d "$releases/linux.XXXXXXXX")
cp -a -- "$bundle/engine" "$bundle/touch_typing.py" "$bundle/platform_linux.py" "$bundle/visuals.py" "$bundle/lab_view.py" "$bundle/style.css" "$bundle/icon.png" "$bundle/hands.png" "$bundle/README.md" "$bundle/check.sh" "$bundle/qa_runner.py" "$bundle/qa" "$destination/"
cp -a -- "$bundle"/result-*.png "$bundle/fonts" "$destination/"
chmod u+x -- "$destination/touch_typing.py" "$destination/engine/TouchTyping.Engine"
if ! printf '%s\n' '{"command":"ping"}' | "$destination/engine/TouchTyping.Engine" | /usr/bin/python3 -c \
    'import json, sys; assert json.load(sys.stdin)["value"] == "ok"'; then
    echo 'Учебный движок не запускается в этой системе.' >&2
    exit 2
fi

/usr/bin/python3 - "$applications" "$desktop" "$destination" <<'PY'
from pathlib import Path
import os
import sys
from tempfile import NamedTemporaryFile

applications, desktop, destination = map(Path, sys.argv[1:])
def exec_quote(path):
    path = str(path).replace("%", "%%")
    for character in ('\\', '"', '`', '$'):
        path = path.replace(character, "\\" + character)
    return '"' + path.replace("\\", "\\\\") + '"'

entry = (
    "[Desktop Entry]\n"
    "Type=Application\n"
    "Name=Код вслепую — Linux\n"
    "Comment=Тренировка горячих клавиш и слепой печати\n"
    f"Exec=/usr/bin/python3 {exec_quote(destination / 'touch_typing.py')}\n"
    f"Icon={str(destination / 'icon.png').replace(chr(92), chr(92) * 2)}\n"
    "Terminal=false\n"
    "StartupNotify=true\n"
    "Categories=Education;\n"
    "Keywords=typing;keyboard;shortcuts;горячие клавиши;печать;\n"
)
for directory in (applications, desktop):
    target = directory / "com.veitnemed.TouchTyping.desktop"
    with NamedTemporaryFile(dir=directory, mode="w", encoding="utf-8", delete=False) as handle:
        handle.write(entry)
        temporary = handle.name
    os.chmod(temporary, 0o755)
    os.replace(temporary, target)
PY
if command -v gio >/dev/null; then
    gio set -t string "$desktop/com.veitnemed.TouchTyping.desktop" metadata::trusted true || true
fi
printf 'Приложение установлено. Ярлык: %s\n' "$desktop/com.veitnemed.TouchTyping.desktop"
