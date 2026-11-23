"""Pass a debug-day route through a mapped GTK window on an owned X11 server.

The inherited harness supplies the shortcut-permission handshake only.
Every learner gesture, including Alt+Print, is sent through XTest.
"""
import os
import re
import sys
from pathlib import Path
from record_daily_use_x11 import Recorder
from test_debug_day import exercise_native

class DebugNative(Recorder):
    def exercise_daily_use(self, chord, capture, output, recording_boundary, switch_preview):
        yield from exercise_native(self, os.environ.get('TOUCH_TYPING_QA_DEBUG_MODE', 'guided'),
                                   chord, capture, output)

if __name__ == '__main__':
    display = re.fullmatch(r'(?:127\.0\.0\.1)?:([0-9]+)', os.environ.get('DISPLAY', ''))
    pid = int(os.environ['TOUCH_TYPING_QA_XVFB_PID'])
    command = Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
    if (not display or not command or Path(os.fsdecode(command[0])).name != 'Xvfb'
            or f':{display[1]}'.encode() not in command or b'-auth' not in command):
        raise SystemExit('Owned isolated Xvfb display with authentication required')
    profile = Path(os.environ['XDG_DATA_HOME'])
    if (not profile.is_absolute() or not profile.is_dir() or profile == Path.home() / '.local/share'
            or (profile / 'TouchTypingStudio-QA/progress.xml').exists()
            or os.environ.get('TOUCH_TYPING_QA_DATA_ROOT')):
        raise SystemExit('Fresh absolute input test profile required')
    app = DebugNative()
    status = app.run([sys.argv[0]])
    raise SystemExit(1 if app.smoke_failure else status)
