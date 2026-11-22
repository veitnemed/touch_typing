"""Native input regression. Run on an isolated GNOME compositor named training-qa.

TOUCH_TYPING_QA_OUTPUT selects an existing directory for the preview images.
--small verifies 960x600, --timed verifies an actual 180-second run and record.
"""
import os
from pathlib import Path
import sys
import traceback
from gi.repository import Gio, GLib
from touch_typing import Studio
from platform_linux import PHYSICAL

class LabWindowTest(Studio):
    def __init__(self):
        self.received = {}
        super().__init__(smoke='-', smoke_size=(960,600) if '--small' in sys.argv else (1380,900))
    def key_pressed(self, controller, keyval, keycode, modifiers):
        from gi.repository import Gdk
        self.last_character = chr(Gdk.keyval_to_unicode(keyval)) if Gdk.keyval_to_unicode(keyval) else ""
        result = super().key_pressed(controller,keyval,keycode,modifiers)
        self.received[(keycode, True)] = self.received.get((keycode, True), 0) + 1
        return result
    def key_released(self, controller, keyval, keycode, modifiers):
        result = super().key_released(controller, keyval, keycode, modifiers)
        self.received[(keycode, False)] = self.received.get((keycode, False), 0) + 1
        return result
    def call(self, operation, **payload):
        if operation == "lab-start":
            payload["seed"] = int(os.environ.get("TOUCH_TYPING_QA_SEED", "7"))
        return super().call(operation, **payload)
    def failure_image(self):
        from gi.repository import Gtk
        output = Path(os.environ.get("TOUCH_TYPING_QA_OUTPUT", "."))
        paintable = Gtk.WidgetPaintable.new(self.window)
        snapshot = Gtk.Snapshot()
        paintable.snapshot(snapshot, paintable.get_intrinsic_width(), paintable.get_intrinsic_height())
        self.window.get_renderer().render_texture(snapshot.to_node(), None).save_to_png(str(output / "failure-window.png"))
    def smoke_test(self):
        self.steps=self.exercise()
        GLib.timeout_add(12,self.advance)
        return GLib.SOURCE_REMOVE
    def advance(self):
        try:
            next(self.steps)
            return GLib.SOURCE_CONTINUE
        except StopIteration:
            print('PASS native GTK Live classes, capture, task state, RU/EN, pause, results and screenshots',flush=True)
        except Exception:
            traceback.print_exc()
            self.smoke_failure=True
            try:self.failure_image()
            except Exception:traceback.print_exc()
        self.quit()
        return GLib.SOURCE_REMOVE
    def exercise(self):
        bus=Gio.bus_get_sync(Gio.BusType.SESSION)
        api='org.gnome.Mutter.RemoteDesktop'
        def call(path,interface,method,args=None):
            return bus.call_sync(api,path,interface,method,args,None,Gio.DBusCallFlags.NONE,3000,None)
        session,=call('/org/gnome/Mutter/RemoteDesktop',api,'CreateSession').unpack()
        call(session,api+'.Session','Start')
        keycodes={key: code-8 for code,key in PHYSICAL.items()}
        def event(key,down):
            call(session,api+'.Session','NotifyKeyboardKeycode',GLib.Variant('(ub)',(keycodes[key],down)))
        def chord(label):
            parts=label.split('+')
            parts=[{'Ctrl':'LCtrl','Shift':'LShift','Alt':'LAlt','Super':'LSuper'}.get(p,p) for p in parts]
            for key, down in [(k, True) for k in parts] + [(k, False) for k in reversed(parts)]:
                observed = (keycodes[key] + 8, down)
                count = self.received.get(observed, 0)
                captured = self.lab and self.lab.active and not self.lab.state["paused"]
                event(key, down)
                yield
                if captured:
                    for _ in range(100):
                        if self.received.get(observed, 0) != count or not self.lab.active or self.lab.state["paused"]:
                            break
                        yield
                    assert self.received.get(observed, 0) != count or not self.lab.active or self.lab.state["paused"], ("undelivered key", key, down)
        def capture_png(path, widget=None):
            paintable=__import__('gi').repository.Gtk.WidgetPaintable.new(widget or self.window)
            snapshot=__import__('gi').repository.Gtk.Snapshot()
            paintable.snapshot(snapshot,paintable.get_intrinsic_width(),paintable.get_intrinsic_height())
            self.window.get_renderer().render_texture(snapshot.to_node(),None).save_to_png(str(path))
        try:
            if not self.window.is_active():
                yield from chord('Esc')
                self.window.present()
                yield
            self.navigate('lab')
            for index in ([1] if '--timed' in sys.argv else range(5)):
                desired = "RU" if index in (1, 2) else "EN"
                for _ in range(20):yield
                yield from chord('A')
                print('PROBE', index, self.window.is_active(), self.layout, getattr(self,'last_character',''),flush=True)
                if self.layout != desired:
                    yield from chord('Super+Space')
                    for _ in range(25): yield
                    yield from chord('A')
                    if self.layout != desired:
                        yield from chord('Super+Space')
                        for _ in range(40):yield
                        yield from chord('A')
                print("LAYOUT",index,self.layout,desired,getattr(self,"last_character",""),flush=True)
                assert self.layout == desired, (self.layout, desired)
                self.lab.picker.set_selected(index)
                self.lab.level.set_selected(1)
                self.lab.limit.set_selected(4 if '--timed' in sys.argv else 0)
                self.lab.start()
                assert 'RSuper' not in self.lab.guide.board.cells
                assert self.lab.guide.board.cells['LSuper'][0].get_label() == '⊞'
                assert self.lab.guide.board.cells['F11'][0].get_label() == 'F11\nPrtSc'
                assert not self.lab.guide.board.cells['Print'][0].get_visible()
                assert self.lab.desktop.presentation_app == self.lab.state['app']
                for _ in range(45):yield
                if self.lab.state['paused'] and not self.window.is_active():
                    yield from chord('Enter')
                for _ in range(100):
                    if not self.lab.state['paused']:break
                    yield
                assert not self.lab.state['paused'], self.lab.status.get_label()
                if index in (1, 2) and '--timed' not in sys.argv:
                    from gi.repository import GdkPixbuf
                    def selection(name):
                        path = Path(os.environ['TOUCH_TYPING_QA_OUTPUT']) / (name + '.png')
                        capture_png(path, self.lab.desktop)
                        pixbuf = GdkPixbuf.Pixbuf.new_from_file(str(path))
                        pixels = pixbuf.get_pixels()
                        channels = pixbuf.get_n_channels()
                        return sum(pixels[i:i+3] == bytes((53, 98, 155))
                                   for i in range(0, len(pixels) - 2, channels))
                    yield from chord('Ctrl+L')
                    for _ in range(3):yield
                    highlighted = selection('chrome-address' if index == 1 else 'code-line')
                    assert highlighted > 30, ('missing visible selection', index, highlighted)
                    yield from chord('Left')
                    for _ in range(3):yield
                    assert selection('chrome-caret' if index == 1 else 'code-caret') < highlighted
                    if index == 1:
                        before = self.lab.state['scene']['windows'][0]['input']
                        clip = self.lab.state['scene']['clipboard']
                        yield from chord('Ctrl+X')
                        assert self.lab.state['scene']['windows'][0]['input'] == before
                        assert self.lab.state['scene']['clipboard'] == clip
                        yield from chord('Ctrl+L' if self.lab.state['task']['id'] == 'copy' else 'Esc')
                    else:
                        yield from chord('Ctrl+J')
                        assert self.lab.state['scene']['windows'][0]['panel']
                        yield from chord('Ctrl+J')
                for _ in range(100000 if '--timed' in sys.argv else 24):
                    lab=self.lab
                    if not lab.active:break
                    task=lab.state['task']
                    old=lab.state['solved']
                    if task['keys']:
                        for _ in range(28):
                            yield from chord(task['keys'])
                            if not lab.active or lab.state['solved'] != old:
                                break
                    else:
                        if task['id'].startswith('open-') and not lab.state['scene']['overview']:
                            yield from chord('Super')
                        layout=self.lab.state['scene']['layout']
                        mapping={' ':('Space',False),'\n':('Enter',False)}
                        for items in self.library['keyboard']:
                            for cap in items:
                                base=cap['ru' if layout=='RU' else 'en']
                                shifted=cap['ruShift' if layout=='RU' else 'enShift']
                                if len(base)==1:mapping[base]=(cap['key'],False)
                                if len(shifted)==1:mapping[shifted]=(cap['key'],True)
                        for char in task['text']:
                            key,shift=mapping[char]
                            if shift:
                                yield from chord('Shift+'+key)
                            else:
                                yield from chord(key)
                        if task['id']!='type-code':
                            yield from chord('Enter')
                    if not lab.active:break
                    assert self.lab.state['solved']==old+1, (index,task,self.lab.state['status'],self.lab.state['scene'])
                yield
                if '--timed' in sys.argv:
                    assert self.lab.result and self.lab.state['seconds']==180
                    results=self.call('lab-result')
                    assert results['completed'] and results['record']>=results['solved']
                    panel=self.lab.modal_layer.get_first_child()
                    found,bounds=panel.compute_bounds(self.window)
                    assert found and bounds.get_width()<=710, bounds
                    assert abs(bounds.get_x()+bounds.get_width()/2-self.window.get_width()/2)<18, bounds
                    capture_png(Path(os.environ.get('TOUCH_TYPING_QA_OUTPUT',str(Path(__file__).parent)))/'lab-result.png')
                    print('PASS full180s record',results['solved'],results['errors'],results['record'],flush=True)
                    break
                capture_png(Path(os.environ.get('TOUCH_TYPING_QA_OUTPUT',str(Path(__file__).parent)))/('lab-'+self.lab.state['app']+('-small' if '--small' in sys.argv else '')+'.png'))
                lab=self.lab
                for widget in (lab.desktop,lab.guide.board,*lab.guide.hands.values()):
                    if widget.get_visible():
                        found,bounds=widget.compute_bounds(self.window)
                        assert found and bounds.get_y()+bounds.get_height()<=self.window.get_height(), (index,bounds,self.window.get_height())
                if index == 3:
                    yield from chord('Ctrl+C')
                    for key in ('A','B','C','D','Left','Left'):
                        yield from chord(key)
                    assert lab.state['scene']['terminalInput'] == 'abcd'
                    yield from chord('Delete')
                    assert lab.state['scene']['terminalInput'] == 'abd'
                    capture_png(Path(os.environ['TOUCH_TYPING_QA_OUTPUT']) / 'terminal-delete.png', lab.desktop)
                    yield from chord('Backspace')
                    assert lab.state['scene']['terminalInput'] == 'ad'
                if index == 1:
                    yield from chord('Print')
                    yield from chord('Enter')
                    scene = lab.state['scene']
                    assert scene['clipboardMime'] == 'image/png'
                    payload = next(f['content'] for f in scene['terminal']['files'] if f['path'].endswith('/'+scene['clipboard']))
                    import base64, struct
                    width, height = struct.unpack('>II', base64.b64decode(payload.split(',')[1])[16:24])
                    assert 120 < width <= 501 and height > 50, (width, height)
                    yield from chord('Alt+F4')
                    assert self.window.get_visible() and lab.active and not lab.state['scene']['windows']
                yield from chord('F8')
                assert self.lab.state['paused']
                self.lab.stop()
                assert self.lab.result
                self.navigate('lab')
            if '--timed' not in sys.argv:
                self.navigate('shortcuts')
                yield from chord('A')
                if self.layout != 'RU':
                    yield from chord('Super+Space')
                    for _ in range(40):yield
                    yield from chord('A')
                assert self.layout == 'RU'
                self.set_group('terminal-paste')
                self.toggle_shortcut()
                for k in ('RCtrl','RShift','V'):
                    event(k,True)
                    yield
                assert self.state['solved']==1 and self.state['score']['verifiedResults']==1
                event('V',False);yield
                event('V',True);yield
                assert self.state['solved']==2
                for k in ('V','RShift','RCtrl'):
                    event(k,False);yield
                assert self.state['solved']==2
                self.stop()
                assert self.result_kind=='shortcuts'
        finally:call(session,api+'.Session','Stop')

if __name__=='__main__':
    if os.environ.get('WAYLAND_DISPLAY')!='training-qa':raise SystemExit('Private test compositor required')
    bus=Gio.bus_get_sync(Gio.BusType.SESSION)
    pid,=bus.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','GetConnectionUnixProcessID',GLib.Variant('(s)',('org.gnome.Mutter.RemoteDesktop',)),None,Gio.DBusCallFlags.NONE,3000,None).unpack()
    assert b'--headless' in Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
    app=LabWindowTest();status=app.run([sys.argv[0]]);sys.exit(1 if app.smoke_failure else status)
