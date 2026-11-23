"""Optional GTK/Wayland end-to-end release proof for Workday source note.

Run beside test_lab_window.py, in the same isolated headless GNOME and
disposable absolute XDG_DATA_HOME configured by qa_runner.py. Sources can be
used with PYTHONPATH=linux:linux/tests and TOUCH_TYPING_ENGINE set to the
compiled engine; use PYTHONPATH=<package>:<package>/qa for a packaged run.
"""
from pathlib import Path
import json
import os
import sys

from gi.repository import Gio, GLib
from test_lab_window import LabWindowTest

MISSION = 'workday-source-note'
SOURCE = 'https://practice.local/spectrum'
TARGET = '/home/student/project/notes.txt'
ORIGINAL = 'spectrum\nsignal\nvector\nsignal\n'
NOTE = ('Спектр сигнала\n'
        'Прямое преобразование возвращает комплексный спектр.\n'
        'Центрированный спектр позволяет сопоставить частоты.\n'
        'Материалы и отчёт доступны ниже.')


class SourceNoteWindow(LabWindowTest):
    def call(self, operation, **payload):
        if operation == 'configure':
            payload['dock'] = ['chrome', 'code', 'terminal', 'files']
        return super().call(operation, **payload)

    def exercise(self):
        # Reuse the already validated owned GNOME event transport, focus recovery,
        # and window renderer without editing the shared test or sending RPC key events.
        sys.argv.append('--terminal-only')
        try:
            yield from super().exercise()
        finally:
            sys.argv.pop()

    def exercise_terminal(self, chord, hold, capture_png, output):
        lab = self.lab
        lab.picker.set_selected(next(i for i, row in enumerate(lab.classes) if row['id'] == 'workday'))
        lab.level.set_selected(0)
        lab.limit.set_selected(0)
        lab.mission_picker.set_selected(next(i for i, row in enumerate(lab.choices) if row[0] == MISSION))
        lab.start()
        for _ in range(45):
            yield
        if lab.state['paused'] and not self.window.is_active():
            yield from chord('Enter')
        for _ in range(100):
            if not lab.state['paused']:
                break
            yield
        assert lab.active and not lab.state['paused'], lab.status.get_label()
        assert lab.state['mission'] == MISSION

        def active(state):
            return next((w for w in state['scene']['windows'] if w['active']), {})

        def saved(state):
            return next(f['content'] for f in state['scene']['terminal']['files'] if f['path'] == TARGET)

        def type_goal(task):
            if task['id'].startswith('open-') and not lab.state['scene']['overview']:
                yield from chord('Super')
            layout = lab.state['scene']['layout']
            mapping = {' ': 'Space', '\n': 'Enter'}
            for row in self.library['keyboard']:
                for cap in row:
                    for name, shift in ((layout.lower(), ''), (layout.lower() + 'Shift', 'Shift+')):
                        value = cap[name]
                        if len(value) == 1:
                            mapping[value] = shift + cap['key']
            for character in task['text']:
                yield from chord(mapping[character])
            if task['id'] != 'type-code':
                yield from chord('Enter')

        def frame(name):
            # Give GTK time to draw the state and assert a complete native window PNG.
            for _ in range(8):
                yield
            path = output / ('source-note-' + name + '.png')
            capture_png(path)
            png = path.read_bytes()
            assert png.startswith(b'\x89PNG\r\n\x1a\n') and len(png) > 1024

        seen = []
        for _ in range(lab.state['scenarioLength']):
            if not lab.active:
                break
            before = lab.state
            task = before['task']
            old = before['solved']
            if task['id'] == 'command/cat notes.txt':
                yield from frame('06-terminal-before-readback')
            if task['keys']:
                for _ in range(28):
                    yield from chord(task['keys'])
                    if not lab.active or lab.state['solved'] != old:
                        break
            else:
                yield from type_goal(task)
            after = lab.state
            assert after['solved'] == old + 1 and after['errors'] == before['errors'], (
                task, after['status'], after['solved'], after['errors'])
            assert not after.get('saveError'), after['saveError']

            if task['id'] == 'copy':
                w = active(after)
                assert (w['app'], w['url'], w['body']) == ('chrome', SOURCE, NOTE), w
                assert after['scene']['clipboard'] == NOTE
                seen.append('copy')
                yield from frame('01-browser-copy')
            elif task['id'] == 'paste':
                w = active(after)
                assert (w['app'], w['url'], w['body']) == ('code', TARGET, NOTE), w
                assert saved(after) == ORIGINAL
                seen.append('paste')
                yield from frame('02-editor-paste')
            elif task['id'] == 'save':
                assert saved(after) == NOTE
                seen.append('save')
                yield from frame('03-editor-saved')
            elif task['id'] == 'file-open' and active(after).get('app') == 'code':
                # Before this Enter, the active window is the file manager.
                assert active(before).get('app') == 'files'
                w = active(after)
                assert (w['url'], w['body']) == (TARGET, NOTE), w
                seen.append('reopen')
                yield from frame('05-editor-reopened')
            elif task['id'] == 'file-open' and active(after).get('app') == 'files':
                yield from frame('04-file-manager')
            elif task['id'] == 'command/cat notes.txt':
                assert active(after).get('app') == 'terminal'
                assert after['scene']['terminal']['lastSuccess']
                assert after['scene']['terminal']['output'][-4:] == NOTE.splitlines()
                assert saved(after) == NOTE
                seen.append('readback')
        assert lab.result and lab.state['finished'] and lab.state['saved'], lab.state
        result = self.call('lab-result')
        assert result['completed'] and result['errors'] == 0, result
        assert seen == ['copy', 'paste', 'save', 'reopen', 'readback'], seen
        yield from frame('07-result')
        print(json.dumps({'mission': MISSION, 'verified': seen, 'solved': result['solved'],
                          'errors': result['errors'], 'input': 'native GTK via GNOME Wayland'}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    if os.environ.get('WAYLAND_DISPLAY') != 'training-qa':
        raise SystemExit('Private test compositor required')
    root = Path(os.environ['XDG_DATA_HOME'])
    if not root.is_absolute() or not root.is_dir() or root == Path.home() / '.local/share':
        raise SystemExit('Fresh disposable absolute XDG_DATA_HOME required')
    if (root / 'TouchTypingStudio-QA/progress.xml').exists() or os.environ.get('TOUCH_TYPING_QA_DATA_ROOT'):
        raise SystemExit('Refusing existing test progress or path override')
    bus = Gio.bus_get_sync(Gio.BusType.SESSION)
    pid, = bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus',
                         'org.freedesktop.DBus', 'GetConnectionUnixProcessID',
                         GLib.Variant('(s)', ('org.gnome.Mutter.RemoteDesktop',)),
                         None, Gio.DBusCallFlags.NONE, 3000, None).unpack()
    assert b'--headless' in Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
    app = SourceNoteWindow()
    status = app.run([sys.argv[0]])
    sys.exit(1 if app.smoke_failure else status)
