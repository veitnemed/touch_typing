"""Collect step-by-step real GTK frames via private GNOME keyboard injection.

Put this file beside test_lab_window.py. Run with its private headless GNOME
(training-qa), fresh absolute XDG_DATA_HOME, and TOUCH_TYPING_QA_OUTPUT.
This is a time-labelled key demonstration, not a compositor screencast.
"""
import os
from pathlib import Path
import sys
from gi.repository import Gio, GLib
from test_lab_window import LabWindowTest

class Recording(LabWindowTest):
    def exercise(self):
        sys.argv.append('--terminal-only')
        try:
            yield from super().exercise()
        finally:
            sys.argv.pop()

    def exercise_terminal(self, chord, hold, capture_png, output):
        for _ in range(3):
            yield from chord('A')
            for _ in range(6):
                yield
            if getattr(self, 'last_character', '').lower() == 'a' and self.layout == 'EN':
                break
            yield from chord('Super+Space')
            for _ in range(40):
                yield
        else:
            raise AssertionError('Could not verify physical English layout')
        lab = self.lab
        lab.picker.set_selected(next(i for i, row in enumerate(lab.classes) if row['id'] == 'terminal'))
        lab.level.set_selected(1)
        lab.limit.set_selected(0)
        lab.start()
        for _ in range(45):
            yield
        if lab.state['paused'] and not self.window.is_active():
            yield from chord('Enter')
        for _ in range(140):
            if lab.active and not lab.state['paused']:
                break
            yield

        def field():
            scene = lab.state['scene']
            view = next(w for w in scene['windows'] if w['app']=='terminal' and w['active'])
            return scene['terminalInput'], view['cursor']

        def capture(name, expected):
            for _ in range(10):
                yield
            assert lab.active and not lab.state['paused'], lab.status.get_label()
            assert self.window.is_active() and self.window.get_focus() == lab.desktop
            assert field() == expected, (name, field(), expected)
            path = output / name
            capture_png(path)
            assert path.read_bytes().startswith(b'\x89PNG\r\n\x1a\n') and path.stat().st_size > 1024

        yield from chord('Ctrl+C')
        for key in 'ABCDE':
            yield from chord(key)
        yield from capture('01-abcde.png', ('abcde', 5))
        yield from chord('Home')
        yield from capture('02-home.png', ('abcde', 0))
        yield from chord('Right')
        yield from capture('03-right.png', ('abcde', 1))
        yield from chord('Right')
        yield from capture('04-before-delete.png', ('abcde', 2))
        yield from chord('Delete')
        yield from capture('05-after-delete.png', ('abde', 2))
        assert lab.stop()

if __name__ == '__main__':
    if os.environ.get('WAYLAND_DISPLAY') != 'training-qa':
        raise SystemExit('Private test compositor required')
    root = Path(os.environ['XDG_DATA_HOME'])
    if (not root.is_absolute() or not root.is_dir() or root == Path.home() / '.local/share'
            or (root / 'TouchTypingStudio-QA/progress.xml').exists()
            or os.environ.get('TOUCH_TYPING_QA_DATA_ROOT')):
        raise SystemExit('Fresh disposable absolute XDG_DATA_HOME required')
    bus = Gio.bus_get_sync(Gio.BusType.SESSION)
    pid, = bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus',
                         'org.freedesktop.DBus', 'GetConnectionUnixProcessID',
                         GLib.Variant('(s)', ('org.gnome.Mutter.RemoteDesktop',)),
                         None, Gio.DBusCallFlags.NONE, 3000, None).unpack()
    assert b'--headless' in Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
    app = Recording()
    status = app.run([sys.argv[0]])
    sys.exit(1 if app.smoke_failure else status)
