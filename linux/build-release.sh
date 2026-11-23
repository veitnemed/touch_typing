#!/usr/bin/env bash
set -euo pipefail

source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ $# != 1 ]]; then
    echo "Использование: $0 ПАПКА_СБОРКИ" >&2
    exit 2
fi
target=$(realpath -m -- "$1")
if [[ -e "$target" ]]; then
    echo "Укажите новую папку сборки." >&2
    exit 2
fi

runtime=${TOUCH_TYPING_RUNTIME:-linux-x64}
dotnet_exe=${TOUCH_TYPING_DOTNET:-dotnet}
mkdir -p -- "$target"
"$dotnet_exe" publish "$source_dir/engine/TouchTyping.Engine.csproj" -c Release -r "$runtime" \
    --self-contained -p:DebugType=none -p:DebugSymbols=false -o "$target/engine"
runtime_version=$(/usr/bin/python3 - "$target/engine/TouchTyping.Engine.deps.json" "$runtime" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as file:
    libraries = json.load(file)["libraries"]
prefix = "runtimepack.Microsoft.NETCore.App.Runtime." + sys.argv[2] + "/"
print(next(name.removeprefix(prefix) for name in libraries if name.startswith(prefix)))
PY
)
packages=${NUGET_PACKAGES:-"$HOME/.nuget/packages"}
runtime_package="$packages/microsoft.netcore.app.runtime.$runtime/$runtime_version"
cp -- "$runtime_package/LICENSE.TXT" "$runtime_package/THIRD-PARTY-NOTICES.TXT" "$target/engine/"
install -m 755 -- "$source_dir/touch_typing.py" "$source_dir/install.sh" "$target/"
install -m 644 -- "$source_dir/platform_linux.py" "$source_dir/visuals.py" "$source_dir/lab_view.py" "$source_dir/style.css" "$source_dir/icon.png" "$source_dir/hands.png" "$source_dir/README.md" "$target/"
install -m 644 -- "$source_dir"/result-*.png "$target/"

install -m 755 -- "$source_dir/check.sh" "$target/"
install -m 644 -- "$source_dir/qa_runner.py" "$target/"
mkdir -p -- "$target/qa/fixtures"
install -m 644 -- "$source_dir/tests/test_live_missions.py" "$source_dir/tests/test_practice.py" "$source_dir/tests/test_lab_window.py" "$source_dir/tests/test_source_note_window.py" "$source_dir/tests/test_wayland_window.py" "$source_dir/tests/test_ui_controls.py" "$target/qa/"
install -m 644 -- "$source_dir/tests/capture_home_reference.py" "$target/qa/"
install -m 644 -- "$source_dir/tests/record_terminal_window.py" "$target/qa/"
install -m 644 -- "$source_dir/tests/fixtures/v1.7.0-workday-report.xml.gz" "$target/qa/fixtures/"
