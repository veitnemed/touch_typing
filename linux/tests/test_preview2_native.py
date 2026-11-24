"""Exercise GTK in an isolated Xvfb server via real XTest key events.

X11 has no Wayland shortcut-inhibition prompt; emulate consent only.
Keyboard input, widget state, scene and resulting screenshots are native.
"""
import ctypes as C
import json
import os
from pathlib import Path
import gi
gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("GdkX11", "4.0")
from gi.repository import Gdk, GdkX11, GLib
from test_lab_window import LabWindowTest
from platform_linux import PHYSICAL

out = Path(os.environ['TOUCH_TYPING_QA_OUTPUT'])
x = C.CDLL('libX11.so.6')
xt = C.CDLL('libXtst.so.6')
x.XOpenDisplay.restype = C.c_void_p
ptr = x.XOpenDisplay(None)
if not ptr or os.environ.get("GDK_BACKEND") != "x11" or not os.environ.get("TOUCH_TYPING_QA_DATA_ROOT"):
    raise SystemExit("Use a disposable X11 display and profile.")
x.XFlush.argtypes = [C.c_void_p]
x.XSetInputFocus.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_ulong]
xt.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
codes = {key: code for code, key in PHYSICAL.items()}
alias = dict(Ctrl='LCtrl', Shift='LShift', Alt='LAlt', Super='LSuper')

class CaptureConsent:
    def get_property(self, name):
        assert name == "shortcuts-inhibited"
        return True
    def restore_system_shortcuts(self):
        pass
    def disconnect(self, _):
        pass


class QA(LabWindowTest):
    def __init__(self):
        super().__init__()
        self.smoke_size = (1024,600)
        self.witness = []
    def focus_window(self):
        surface = self.window.get_surface()
        xid = GdkX11.X11Surface.get_xid(surface)
        x.XSetInputFocus(ptr,xid,1,0)
        x.XFlush(ptr)
    def key_pressed(self, controller, keyval, keycode, modifiers):
        if keycode in (79, 110):
            self.witness.append([keycode, Gdk.keyval_name(keyval)])
        return super().key_pressed(controller,keyval,keycode,modifiers)
    def chord(self, label):
        keys = [codes[alias.get(k,k)] for k in label.split('+')]
        for val in keys:
            xt.XTestFakeKeyEvent(ptr,val,1,0)
        for val in keys[::-1]:
            xt.XTestFakeKeyEvent(ptr,val,0,0)
        x.XFlush(ptr)
        for _ in range(4): yield
    def starter(self, app, mission, level=0):
        if getattr(self,'lab',None) and self.lab.active:
            self.lab.stop(show=False)
        self.navigate('lab')
        lab=self.lab
        def x11_only():
            lab.surface = CaptureConsent()
            state=self.call('lab-resume')
            lab.render(state)
            lab.desktop.grab_focus()
        lab.acquire=x11_only
        lab.picker.set_selected(next(i for i,r in enumerate(lab.classes) if r['id']==app))
        lab.level.set_selected(level)
        lab.limit.set_selected(0)
        lab.mission_picker.set_selected(next(i for i,r in enumerate(lab.choices) if r[0]==mission))
        lab.start()
        self.focus_window()
        return lab
    def typed(self, task):
        if task['id'].startswith('open-'):
            yield from self.chord('Super')
        plain = '1234567890-=[]\\;,./\''
        shift = '!@#$%^&*()_+{}|:<>?"'
        for character in task['text']:
            if character == ' ': label='Space'
            elif character in shift: label='Shift+'+plain[shift.index(character)]
            else: label=('Shift+' if character.isupper() else '')+character.upper()
            yield from self.chord(label)
        if task['id'] != 'type-code':
            yield from self.chord('Enter')
    def advance_goal(self, lab):
        t = {**lab.state['task'], 'action': lab.state['task'].get('action','')}
        old=lab.state['solved']
        if t['keys']:
            for _ in range(24):
                yield from self.chord(t['keys'])
                if lab.state['solved'] > old: break
        else:
            yield from self.typed(t)
        assert lab.state['solved']==old+1,(t,lab.state['status'],old,lab.state['solved'])
    def exercise(self):
        self.navigate('home')
        for _ in range(35): yield
        self.focus_window()
        self.capture_widget_png(out/'01-live-ubuntu.png')
        self.live_os_choices['windows'].emit('clicked')
        for _ in range(15): yield
        assert 'windows' in self.home_cards and 'ubuntu' not in self.home_cards
        self.capture_widget_png(out/'02-live-windows-profile.png')
        self.live_os_choices['ubuntu'].emit('clicked')
        for _ in range(15): yield
        self.home_cards['chrome'].emit('clicked')
        # The isolated X11 server cannot offer a Wayland consent dialog.
        for _ in range(70):
            if self.lab and self.lab.active: break
            yield
        lab = self.lab
        lab.release_capture()
        lab.waiting_capture = False
        lab.surface = CaptureConsent()
        lab.render(self.call('lab-resume'))
        self.focus_window()
        lab.desktop.grab_focus()
        for _ in range(20): yield
        assert lab.state['mission']=='chrome-search-clone'
        for _ in range(45):
            if not lab.active: break
            task=lab.state['task']['id']
            active=lambda:next((w for w in lab.state['scene']['windows'] if w['active']),{})
            before=active()
            if task in ('url',) and before.get('url')=='https://www.google.com/':
                self.capture_widget_png(out/'03-google.png')
            yield from self.advance_goal(lab)
            after=active()
            if task in ('browser-search','back','bookmark','code-clone-confirm'):
                self.capture_widget_png(out/('04-'+task+'.png'))
            if not lab.active:
                self.capture_widget_png(out/'05-result.png')
                break
        assert not lab.active and lab.state['finished']
        for keypad in ('off','on','dedicated'):
            lab=self.starter('workday','workday-report')
            for _ in range(20): yield
            for _ in range(45):
                if lab.state['task']['id']=='file-home': break
                yield from self.advance_goal(lab)
            assert lab.state['task']['id']=='file-home'
            expected=lab.state['solved']
            yield from self.chord('Alt+7')
            assert lab.state['solved']==expected
            if keypad=='on': yield from self.chord('NumLock')
            yield from self.chord('Alt+'+('Home' if keypad=='dedicated' else 'NumPad7'))
            assert lab.state['solved']==expected+1,(keypad,lab.state['status'])
            if keypad=='on': yield from self.chord('NumLock')
            expected_keyval = {"off": "KP_Home", "on": "KP_7", "dedicated": "Home"}[keypad]
            assert self.witness[-1][1] == expected_keyval, (keypad, self.witness[-1])
            print('PASS native', keypad, self.witness[-1], flush=True)
        lab=self.starter('terminal','terminal-essentials-locate')
        for _ in range(20): yield
        yield from self.advance_goal(lab)
        yield from self.advance_goal(lab)
        self.capture_widget_png(out/'06-terminal.png')
        print('PASS Gtk native workflow and keypad',json.dumps(self.witness),flush=True)

app=QA()
result=app.run([])
raise SystemExit(1 if app.smoke_failure else result)
