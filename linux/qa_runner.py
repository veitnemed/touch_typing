#!/usr/bin/python3
"""Run isolated engine and GNOME UI regressions and retain a reviewable report."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description="Автоматическая проверка учебного тренажёра")
    parser.add_argument("--output", type=Path, default=Path.cwd() / "typing-check-results")
    parser.add_argument("--timed", action="store_true", help="Дополнительно пройти настоящий подход на 180 секунд")
    parser.add_argument("--full", action="store_true", help="Запустить C# стресс-тесты из исходников, требуется SDK .NET 10")
    parser.add_argument("--terminal-only", action="store_true", help="Проверить ввод в окно терминала на 1366×768")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--inside", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.inside:
        required = [name for name in ("dbus-run-session", "gnome-shell", "gsettings") if not shutil.which(name)]
        if required:
            report = {"status": "blocked", "missing": required, "checks": [],
                      "message": "Для проверки настоящего окна нужен GNOME с поддержкой headless Wayland."}
            (args.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
            print(report["message"], "Отсутствуют", ", ".join(required), flush=True)
            return 2
        with tempfile.TemporaryDirectory(prefix="typing-qa-") as temporary:
            isolated = Path(temporary)
            environment = os.environ.copy()
            environment.pop("TOUCH_TYPING_QA_DATA_ROOT", None)
            for name in ("DISPLAY", "WAYLAND_DISPLAY", "SESSION_MANAGER", "DBUS_SESSION_BUS_ADDRESS"):
                environment.pop(name, None)
            for variable, folder in (("XDG_RUNTIME_DIR", "runtime"), ("XDG_DATA_HOME", "data"),
                                     ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache")):
                directory = isolated / folder
                directory.mkdir(mode=0o700)
                environment[variable] = str(directory)
            environment.update(GSETTINGS_BACKEND="keyfile", GDK_BACKEND="wayland", GTK_A11Y="none",
                               GSK_RENDERER="cairo", PYTHONDONTWRITEBYTECODE="1")
            command = ["dbus-run-session", "--", sys.executable, str(Path(__file__).resolve()),
                       "--inside", "--output", str(args.output), "--seed", str(args.seed)]
            if args.timed: command.append("--timed")
            if args.terminal_only: command.append("--terminal-only")
            if args.full: command.append("--full")
            return subprocess.run(command, env=environment).returncode
    return run_checks(args)


def run_checks(args):
    tests = ROOT / ("tests" if (ROOT / "tests" / "test_practice.py").exists() else "qa")
    environment = os.environ.copy()
    environment.update(PYTHONPATH=os.pathsep.join((str(ROOT), str(tests))), WAYLAND_DISPLAY="training-qa",
                       TOUCH_TYPING_QA_OUTPUT=str(args.output), TOUCH_TYPING_QA_SEED=str(args.seed))
    report = {"status": "running", "started": datetime.now(timezone.utc).isoformat(), "seed": args.seed,
              "isolated": True, "platform": "linux", "windowsNative": "requires Test-Live-UI.ps1 on Windows", "checks": [], "coverage": ["physical EN/RU keys", "four application classes and integrated workday", "51 complete missions", "selection pixels and caret", "untimed and count-limited sessions",
              "forms and confirmations", "held modifiers", "capture lifecycle", "single left Windows key",
              "connected scenarios", "shortcut-only Chrome and VS Code", "F11 versus Print",
              "Space resumes and Enter stays in the scene", "independent wrong-key highlight"]}
    if args.terminal_only:
        report["coverage"] = ["native terminal RU/EN", "caret, deletion, history, focus and frame bounds"]
    def save():
        temporary = args.output / "report.json.tmp"
        temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(args.output / "report.json")
    def check(name, command, timeout=180):
        start = time.monotonic()
        destination = args.output / (name + ".log")
        print("Проверка " + name, flush=True)
        try:
            with destination.open("w", encoding="utf-8") as output:
                completed = subprocess.run(command, env=environment, stdout=output,
                                           stderr=subprocess.STDOUT, timeout=timeout)
            code = completed.returncode
            if code == 0 and "Traceback (most recent call last)" in destination.read_text(errors="replace"):
                code = 1
        except subprocess.TimeoutExpired:
            code = 124
        entry = {"name": name, "status": "passed" if code == 0 else "failed", "exitCode": code,
                 "seconds": round(time.monotonic() - start, 2), "log": destination.name}
        if destination.exists():
            for line in destination.read_text(encoding="utf-8", errors="replace").splitlines():
                if line.startswith("{"):
                    try:
                        entry["summary"] = json.loads(line)
                    except json.JSONDecodeError:
                        pass
        report["checks"].append(entry)
        save()
        print(entry["status"] + " " + name, flush=True)
        return code == 0
    save()
    shell = None
    shell_output = None
    try:
        if args.full:
            dotnet = os.environ.get("TOUCH_TYPING_DOTNET") or shutil.which("dotnet")
            projects = ("SharedRegression", "LabRegression", "LabStressTests", "TerminalRegression")
            if not dotnet or not all((tests / (name + ".csproj")).exists() for name in projects):
                raise RuntimeError("Полная проверка требует исходники и SDK .NET 10.")
            for name in projects:
                check(name, [dotnet, "run", "--project", str(tests / (name + ".csproj"))], timeout=300)
        if not args.terminal_only:
            check("missions", [sys.executable, str(tests / "test_live_missions.py")], timeout=300)
            check("protocol", [sys.executable, str(tests / "test_practice.py")])
        subprocess.run(["gsettings", "set", "org.gnome.desktop.input-sources", "sources",
                        "[('xkb', 'us'), ('xkb', 'ru')]"], env=environment, check=True)
        shell_output = (args.output / "compositor.log").open("w", encoding="utf-8")
        shell = subprocess.Popen(["gnome-shell", "--headless", "--virtual-monitor", "1440x960",
                                  "--wayland-display=training-qa", "--no-x11"], env=environment,
                                 stdout=shell_output, stderr=subprocess.STDOUT)
        socket = Path(environment["XDG_RUNTIME_DIR"]) / "training-qa"
        deadline = time.monotonic() + 25
        while not socket.exists():
            if shell.poll() is not None or time.monotonic() >= deadline:
                raise RuntimeError("Тестовый GNOME не запустился. См. compositor.log.")
            time.sleep(.1)
        time.sleep(1)
        if args.terminal_only:
            check("native-terminal", [sys.executable, str(tests / "test_lab_window.py"), "--terminal-only"])
        else:
            check("native-small", [sys.executable, str(tests / "test_lab_window.py"), "--small"])
            check("ui-controls", [sys.executable, str(tests / "test_ui_controls.py"), "--small"])
            check("capture-denied", [sys.executable, str(tests / "test_ui_controls.py"), "--small", "--deny-capture"])
            check("legacy-window", [sys.executable, str(tests / "test_wayland_window.py")])
            if args.timed:
                check("native-180-seconds", [sys.executable, str(tests / "test_lab_window.py"), "--timed"], timeout=300)
        report["status"] = "passed" if all(c["status"] == "passed" for c in report["checks"]) else "failed"
    except Exception as error:
        report["status"] = "failed"
        report["error"] = str(error)
        print(str(error), file=sys.stderr, flush=True)
    finally:
        if shell:
            shell.terminate()
            try:shell.wait(timeout=10)
            except subprocess.TimeoutExpired:
                shell.kill()
                shell.wait()
        if shell_output: shell_output.close()
        report["finished"] = datetime.now(timezone.utc).isoformat()
        save()
    print("Отчёт " + str(args.output / "report.json"), flush=True)
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
