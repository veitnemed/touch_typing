#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if [[ $# != 1 ]]; then
    echo "Использование: $0 ПАПКА_СБОРКИ" >&2
    exit 2
fi
target=$(realpath -m -- "$1")
if [[ -e "$target" ]]; then
    echo "Укажите новую папку сборки." >&2
    exit 2
fi

dotnet_exe=${TOUCH_TYPING_DOTNET:-dotnet}
mkdir -p -- "$target"
"$dotnet_exe" build "$root/windows/TouchTyping.Windows.csproj" -c Release -p:DebugType=none -p:DebugSymbols=false -o "$target"
# Fail packaging rather than silently launching the redesigned screens with a fallback family.
for font in Roboto-Regular.ttf Roboto-Medium.ttf Roboto-Bold.ttf OFL.txt ROBOTO-SOURCE.txt; do
    cmp "$root/assets/fonts/$font" "$target/fonts/$font"
done
"$dotnet_exe" publish "$root/linux/engine/TouchTyping.Engine.csproj" -c Release -r win-x64 \
    --self-contained -p:DebugType=none -p:DebugSymbols=false -o "$target/engine"
runtime_version=$(python3 - "$target/engine/TouchTyping.Engine.deps.json" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as file:
    libraries = json.load(file)["libraries"]
prefix = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/"
print(next(name.removeprefix(prefix) for name in libraries if name.startswith(prefix)))
PY
)
packages=${NUGET_PACKAGES:-"$HOME/.nuget/packages"}
runtime_package="$packages/microsoft.netcore.app.runtime.win-x64/$runtime_version"
cp -- "$runtime_package/LICENSE.TXT" "$runtime_package/THIRD-PARTY-NOTICES.TXT" "$target/engine/"
install -m 644 -- "$root/START-RU.txt" "$root/windows/Start-Live.cmd" "$root/windows/Start-Workday.cmd" "$root/windows/Test-Live-UI.ps1" "$root/windows/LiveUiSmoke.cs" "$target/"
install -m 644 -- "$root/windows/Test-TypingReview.ps1" "$root/windows/TypingReviewWindowSmoke.cs" "$target/"
install -m 644 -- "$root/windows/README.md" "$target/README.md"
mkdir -p -- "$target/code-lesson"
install -m 644 -- "$root/code-lesson/lesson.js" "$root/code-lesson/package.json" "$target/code-lesson/"
