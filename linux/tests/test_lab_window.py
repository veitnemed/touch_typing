"""Native input regression. Run on an isolated GNOME compositor named training-qa.

TOUCH_TYPING_QA_OUTPUT selects an existing directory for the preview images.
--small verifies 960x600, --timed verifies an actual 180-second run and record.
--terminal-only checks real terminal editing at 1366x768 (or 960x600 with --small).
"""
import json
import os
from pathlib import Path
import sys
import time
import traceback
from gi.repository import Gio, GLib, Gtk
from touch_typing import Studio
from platform_linux import PHYSICAL

class LabWindowTest(Studio):
    def __init__(self):
        self.received = {}
        super().__init__(smoke='-', smoke_size=(960,600) if '--small' in sys.argv else
                         (1366,768) if '--terminal-only' in sys.argv else (1380,900))
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
        if operation == "configure" and "--gallery" in sys.argv:
            payload["dock"] = ["chrome", "code", "terminal", "files"]
        if operation == "lab-start":
            payload["seed"] = int(os.environ.get("TOUCH_TYPING_QA_SEED", "7"))
        return super().call(operation, **payload)
    def capture_widget_png(self, path, widget=None):
        # GtkWidgetPaintable is populated on a widget's rendered frame. A mapped
        # widget can briefly produce no node just after a layout/style update.
        # Keep the paintable alive across frame dispatches rather than passing
        # None to the renderer or replacing the evidence with a synthetic image.
        target = self.window if widget is None else widget
        paintable = Gtk.WidgetPaintable.new(target)
        context = GLib.MainContext.default()
        deadline = time.monotonic() + 1.25
        attempts = 0
        while True:
            width, height = paintable.get_intrinsic_width(), paintable.get_intrinsic_height()
            renderer = self.window.get_renderer()
            if self.window.get_mapped() and target.get_mapped() and width > 0 and height > 0 and renderer:
                snapshot = Gtk.Snapshot()
                paintable.snapshot(snapshot, width, height)
                node = snapshot.to_node()
                if node is not None:
                    texture = renderer.render_texture(node, None)
                    if not texture.save_to_png(str(path)):
                        raise RuntimeError(f"Cannot write actual GTK snapshot: {path}")
                    return
            if time.monotonic() >= deadline:
                raise RuntimeError(
                    f"No rendered GTK frame for {path} after {attempts} redraw requests "
                    f"(window mapped={self.window.get_mapped()}, widget mapped={target.get_mapped()}, "
                    f"intrinsic={width}×{height})")
            attempts += 1
            target.queue_draw()
            self.window.queue_draw()
            # The exercise's own GLib timer source cannot dispatch recursively.
            # Permit frame/layout sources to run between snapshot attempts.
            following_frame = time.monotonic() + .025
            while time.monotonic() < following_frame:
                if not context.iteration(False):
                    time.sleep(.003)

    def failure_image(self):
        output = Path(os.environ.get("TOUCH_TYPING_QA_OUTPUT", "."))
        self.capture_widget_png(output / "failure-window.png")

    def smoke_test(self):
        self.steps=self.exercise()
        GLib.timeout_add(12,self.advance)
        return GLib.SOURCE_REMOVE
    def advance(self):
        try:
            next(self.steps)
            return GLib.SOURCE_CONTINUE
        except StopIteration:
            print('PASS native GTK gallery' if '--gallery' in sys.argv else
                  'PASS native GTK terminal editing' if '--terminal-only' in sys.argv else
                  'PASS native GTK Live classes, capture, task state, RU/EN, pause, results and screenshots',flush=True)
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
        def hold(key, presses=3):
            observed = (keycodes[key] + 8, True)
            count = self.received.get(observed, 0)
            event(key, True)
            try:
                for _ in range(270):
                    if self.received.get(observed, 0) >= count + presses:
                        break
                    yield
                assert self.received.get(observed, 0) >= count + presses, ("no OS repeat", key)
            finally:
                event(key, False)
            for _ in range(5):
                yield
        capture_png = self.capture_widget_png
        try:
            if not self.window.is_active():
                yield from chord('Esc')
                self.window.present()
                yield
            if "--system-typing-native" in sys.argv:
                yield from self.exercise_system_typing(chord, capture_png, Path(os.environ["TOUCH_TYPING_QA_OUTPUT"]))
                return
            self.navigate('lab')
            if '--gnome-profile' in sys.argv or '--gnome-profile-custom' in sys.argv:
                yield from self.exercise_gnome_profile(chord, event, capture_png, Path(os.environ["TOUCH_TYPING_QA_OUTPUT"]))
                return
            if '--debug-day' in sys.argv:
                from test_debug_day import exercise_native
                output = Path(os.environ['TOUCH_TYPING_QA_OUTPUT'])
                for mode in ('guided', 'independent'):
                    yield from exercise_native(self, mode, chord, capture_png, output)
                return
            if '--daily-use' in sys.argv:
                def switch_preview(frame):
                    event('LAlt', True); yield
                    event('Tab', True); yield
                    event('Tab', False); yield
                    yield from frame('gnome-04a-switcher')
                    event('LAlt', False)
                    for _ in range(10):
                        yield
                yield from self.exercise_daily_use(chord, capture_png, Path(os.environ['TOUCH_TYPING_QA_OUTPUT']),
                                                   switch_preview=switch_preview)
                return
            if '--gallery' in sys.argv:
                output = Path(os.environ['TOUCH_TYPING_QA_OUTPUT'])
                yield from self.capture_gallery(chord, capture_png, output)
                return
            if '--soft-limit' in sys.argv:
                yield from self.exercise_soft_limit(chord, hold, capture_png,
                                                   Path(os.environ['TOUCH_TYPING_QA_OUTPUT']))
                return
            if '--terminal-only' in sys.argv:
                output = Path(os.environ['TOUCH_TYPING_QA_OUTPUT'])
                yield from self.exercise_terminal(chord, hold, capture_png, output)
                return
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
                self.lab.limit.set_selected(0)
                self.lab.time_limit.set_selected(1 if '--timed' in sys.argv else 0)
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
                        # Real GDK autorepeat inside the Chrome field, not a
                        # simulated series of separate press/release pairs.
                        yield from chord('End')
                        for _ in range(3):
                            yield from chord('Left')
                        cursor = self.lab.state['scene']['windows'][0]['cursor']
                        solved = self.lab.state['solved']
                        yield from hold('Backspace')
                        after = self.lab.state['scene']['windows'][0]
                        removed = len(before) - len(after['input'])
                        assert removed >= 3 and after['cursor'] == cursor - removed, (before, after)
                        assert after['input'] == before[:cursor-removed] + before[cursor:], (before, after)
                        assert not after['selected'] and solved == self.lab.state['solved']
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
                            if not lab.active or lab.state.get('limitDecisionPending') or lab.state['solved'] != old:
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
                    if not lab.active or lab.state.get('limitDecisionPending'):break
                    assert self.lab.state['solved']==old+1, (index,task,self.lab.state['status'],self.lab.state['scene'])
                yield
                if '--timed' in sys.argv:
                    assert self.lab.state.get('limitDecisionPending') and self.lab.state['seconds']==180
                    assert self.lab.state.get('limitReached') == 'timer'
                    self.lab.limit_actions[0].grab_focus()
                    yield from chord('Enter')
                    for _ in range(30):
                        if self.lab.result:
                            break
                        yield
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

    def exercise_system_typing(self, chord, capture_png, output):
        """Use GNOME's real shortcut inhibition; never mock its permission."""
        from time import monotonic
        assert os.environ["WAYLAND_DISPLAY"] == "training-qa"
        assert self.window.get_display().__gtype__.name == "GdkWaylandDisplay"
        self.navigate("typing")
        yield
        assert self.typing_rows["SystemHotkeys"].grab_focus()
        yield from chord("Enter")

        def acquire():
            acknowledged = False
            started = monotonic()
            while monotonic() - started < 13:
                if self.system_typing_available() and self.workout == "typing" and not self.paused:
                    return
                if (not acknowledged and monotonic() - started > 0.4 and self.system_typing_pending
                        and not self.window.is_active() and not self.system_typing_available()):
                    yield from chord("Enter")
                    acknowledged = True
                yield
            raise AssertionError(("system typing capture not granted", self.banner.get_label()))

        yield from acquire()
        assert (self.state["mode"], self.state["progressTotal"]) == ("SystemHotkeys", 44)
        capture_png(output / "system-typing-native-captured.png")
        seen = set()
        for index in range(11):
            chord_name = self.state["expected"]
            assert self.state["expectedKind"] == "command"
            if chord_name == "Shift+Super+PageDown":
                yield from chord("Super+PageDown")  # wrong: switch instead of moving the window
                assert self.state["progressDone"] == index and self.system_typing_available()
            yield from chord(chord_name)
            assert self.state["progressDone"] == index + 1 and self.system_typing_available(), chord_name
            seen.add(chord_name)
        assert {"Super+PageUp", "Super+PageDown", "Super+A", "Shift+Super+PageDown", "Super+Left", "Alt+Tab", "Alt+Home"} <= seen, seen

        # Revoke actual GDK permission while the window still owns the exercise.
        self.window.get_surface().restore_system_shortcuts()
        for _ in range(180):
            if self.paused:
                break
            yield
        assert self.paused and self.system_typing_capture is None and self.state["progressDone"] == 11
        yield from chord("F8")
        yield from acquire()
        assert self.state["progressDone"] == 11
        yield from chord(self.state["expected"])
        assert self.state["progressDone"] == 12
        capture_png(output / "system-typing-native-resumed.png")
        while self.workout == "typing":
            yield from chord(self.state["expected"])
        assert self.result_kind == "typing" and self.correction_button.get_mapped()
        assert self.system_typing_capture is None
        self.correction_button.grab_focus()
        yield from chord("Enter")
        yield from acquire()
        assert self.workout == "typing" and self.state["requiresSystemCapture"] and self.state["isReview"]
        assert self.state["expected"] == "Shift+Super+PageDown"
        yield from chord(self.state["expected"])
        assert self.state["progressDone"] == 1 and self.system_typing_available()
        yield from chord("Esc")
        assert self.workout is None and self.system_typing_capture is None
        print("PASS GTK Wayland SystemHotkeys:", sorted(seen), "revocation pauses and explicit resume preserves progress", flush=True)

    def await_lesson_capture(self, chord):
        """Acknowledge GNOME in this owned remote-desktop QA session only."""
        lab = self.lab
        acknowledged_prompt = False
        for frame in range(300):
            focused = self.window.is_active()
            inhibited = bool(lab.surface and lab.surface.get_property("shortcuts-inhibited"))
            if lab.active and not lab.state["paused"] and focused and inhibited and not lab.waiting_capture:
                print("GTK lesson capture confirmed by GDK", lab.state["mission"], flush=True)
                return
            # Don't send Enter until the modal owns focus, or when training
            # is paused for a reason unrelated to the compositor permission.
            if (frame >= 45 and not acknowledged_prompt and lab.active and lab.state["paused"]
                    and lab.waiting_capture and not focused and not inhibited):
                yield from chord("Enter")
                acknowledged_prompt = True
            yield
        raise AssertionError(("GNOME shortcut capture not ready", lab.state["mission"], lab.status.get_label(),
                              "focused", focused, "GDK inhibited", inhibited, "acknowledged", acknowledged_prompt))

    def exercise_gnome_profile(self, chord, event, capture, output):
        custom = '--gnome-profile-custom' in sys.argv
        if custom:
            # An in-process memory-only settings backend for this GTK child;
            # the separate isolated compositor keeps its own QA configuration.
            assert os.environ.get("GSETTINGS_BACKEND") == "memory"
            settings = self.sources.settings[self.sources.wm]
            assert settings.set_strv("move-to-workspace-right", ["<Control><Alt><Shift>Right"])
            assert settings.set_strv("switch-to-workspace-left", ["<Control><Alt>Left"])
            assert settings.set_strv("switch-group", ["<Super>Above_Tab"])
            assert settings.set_strv("switch-group-backward", ["<Shift><Super>Above_Tab"])
            assert self.sources.desktop_bindings()["move-next"] == "Ctrl+Alt+Shift+Right"

        def start(mission):
            if self.lab.active:
                assert self.lab.stop(), self.lab.status.get_label()
            self.navigate("lab")
            lab = self.lab
            lab.picker.set_selected(next(i for i, entry in enumerate(lab.classes) if entry["id"] == "ubuntu"))
            lab.level.set_selected(1)
            lab.limit.set_selected(0)
            lab.time_limit.set_selected(0)
            lab.mission_picker.set_selected(next(i for i, (key, _) in enumerate(lab.choices) if key == mission))
            lab.start()
            yield from self.await_lesson_capture(chord)

        def state():
            return self.lab.state
        def active():
            return next(w for w in state()["scene"]["windows"] if w["active"])
        def frame(name):
            for _ in range(4):
                yield
            assert self.window.is_active() and self.window.get_mapped(), name
            capture(output / (("custom-" if custom else "default-") + name + ".png"))
        def current(action):
            assert state()["task"]["id"] == action, state()["task"]
            yield from chord(state()["task"]["keys"])

        yield from start("ubuntu-gnome-workspaces")
        code = next(w for w in state()["scene"]["windows"] if w["app"] == "code")
        original = (code["id"], code["url"], code["activeTab"], code["bodyCursor"])
        if custom:
            assert state()["task"]["keys"] == "Ctrl+Alt+Shift+Right"
        yield from current("move-next")
        assert state()["scene"]["workspace"] == 1 and active()["app"] == "terminal"
        assert {w["app"]: w["workspace"] for w in state()["scene"]["windows"]} == {"code": 0, "terminal": 1}
        if state()["task"]["id"] == "layout":
            yield from current("layout")
        assert state()["task"]["id"] == "command/pwd"
        for key in ("P", "W", "D", "Enter"):
            yield from chord(key)
        assert state()["scene"]["terminal"]["output"][-1] == "/home/student"
        yield from frame("workspace-2-terminal")
        if custom:
            assert state()["task"]["keys"] == "Ctrl+Alt+Left"
        yield from current("workspace-prev")
        code = active()
        assert state()["scene"]["workspace"] == 0
        assert (code["id"], code["url"], code["activeTab"], code["bodyCursor"]) == original
        yield from frame("workspace-1-editor")
        for key in ("G", "N", "O", "M", "E", "Enter"):
            yield from chord(key)
        assert "# gnome" in active()["body"]
        yield from current("save")
        yield from current("workspace-next")
        assert active()["app"] == "terminal"
        yield from current("workspace-prev")
        assert state()["finished"]

        yield from start("ubuntu-gnome-same-app")
        assert active()["url"] == "README.md"
        # Take the screenshot before releasing the switcher modifier, with
        # the two window identities asserted independently of the rendering.
        keys = state()["task"]["keys"].split("+")
        if custom:
            assert keys == ["Super", "`"]
        sides = {"Ctrl": "LCtrl", "Shift": "LShift", "Alt": "LAlt", "Super": "LSuper"}
        physical = [sides.get(key, key) for key in keys]
        try:
            for key in physical:
                event(key, True); yield
            for _ in range(50):
                if state()["scene"]["switcher"]: break
                yield
            assert state()["scene"]["switcher"]
            choices = set(state()["scene"]["switchChoices"])
            assert {w["url"] for w in state()["scene"]["windows"] if w["id"] in choices} == {"app.py", "README.md"}
            assert state()["solved"] == 0 and active()["url"] == "README.md"
            yield from frame("same-app-switcher")
        finally:
            for key in reversed(physical):
                event(key, False)
                yield
        assert state()["solved"] == 1 and active()["url"] == "app.py"
        yield from current("switch-group-prev")
        assert state()["finished"] and active()["url"] == "README.md"

        yield from start("ubuntu-matebook-layout")
        initial = state()["scene"]["layout"]
        for key, label in (("RSuper", "правый Super"), ("LAlt", "Это Alt"), ("Z", "Это клавиша Z")):
            yield from chord(key)
            assert state()["solved"] == 0 and label in state()["status"] and not state()["scene"]["overview"]
        yield from frame("left-super-mistake")
        yield from current("gnome-left-super")
        assert state()["scene"]["overview"]
        yield from current("escape")
        yield from current("layout")
        assert state()["scene"]["layout"] != initial and state()["scene"]["layoutIsVirtual"]
        yield from current("layout")
        assert state()["scene"]["layout"] == initial and state()["finished"] and state()["errors"] == 3
        print("PASS native GTK GNOME profile", "custom" if custom else "default", "workspaces, same-app, MateBook", flush=True)

    def exercise_daily_use(self, chord, capture, output, recording_boundary=lambda _name: None, switch_preview=None):
        """Run the catalog slices with compositor-delivered keys and real GTK frames."""
        for _ in range(4):
            yield from chord('A')
            if self.layout == 'EN' and getattr(self, 'last_character', '').lower() == 'a':
                break
            yield from chord('Super+Space')
            for _ in range(30):
                yield
        else:
            raise AssertionError("Could not verify English input for the demonstration")

        def start(app, level, mission):
            if self.lab.active:
                assert self.lab.stop(), self.lab.status.get_label()
            self.navigate('lab')
            lab = self.lab
            lab.picker.set_selected(next(i for i, item in enumerate(lab.classes) if item['id'] == app))
            lab.level.set_selected(level)
            lab.limit.set_selected(0)
            lab.time_limit.set_selected(0)
            lab.mission_picker.set_selected(next(i for i, (key, _) in enumerate(lab.choices) if key == mission))
            for _ in range(20):
                yield
            lab.start()
            yield from self.await_lesson_capture(chord)

        def window(app):
            state = self.lab.state
            found = next((w for w in state['scene']['windows'] if w['app'] == app), None)
            assert found is not None, (app, state['task'], state['status'], state['scene']['dock'])
            return found

        def position():
            view = window('code')
            return view['tabs'][view['activeTab']]['title'], view['bodyCursor']

        def frame(name):
            for _ in range(10):
                yield
            assert self.window.get_mapped() and self.window.is_active(), name
            capture(output / (name + '.png'))

        yield from start('code', 0, 'code-go-definition')
        recording_boundary('symbols')
        origin = position()
        assert origin[0].endswith('main.py'), origin
        yield from frame('symbols-01-call')
        yield from chord('F12')
        assert position()[0].endswith('signal_ops.py'), position()
        yield from frame('symbols-02-definition')
        yield from chord('Ctrl+Alt+-')
        assert position() == origin, (position(), origin)
        yield from frame('symbols-03-back')
        yield from chord('Ctrl+D')

        yield from start('code', 0, 'code-rename-symbol')
        yield from chord('Shift+F12')
        assert window('code')['focus'] == 'symbol-references'
        assert len(window('code')['entries']) >= 2
        yield from frame('symbols-04-usages')
        yield from chord('Down')
        yield from chord('Enter')
        yield from chord('F2')
        assert window('code')['focus'] == 'symbol-rename'
        yield from frame('symbols-05-rename')
        yield from chord('Esc')
        assert window('code')['focus'] == 'body' and 'scale_sample' not in window('code')['body']
        yield from frame('symbols-06-cancel')
        yield from chord('F2')
        for key in ('S','C','A','L','E','Shift+-','S','A','M','P','L','E'):
            yield from chord(key)
        assert window('code')['input'] == 'scale_sample', window('code')['input']
        yield from frame('symbols-07-new-name')
        yield from chord('Enter')
        assert 'scale_sample' in window('code')['body']
        yield from frame('symbols-08-renamed-source')
        yield from chord('F12')
        assert position()[0].endswith('signal_ops.py') and 'def scale_sample(' in window('code')['body']
        yield from frame('symbols-09-result')
        recording_boundary(None)

        yield from start('code', 0, 'code-search-error')
        assert window('terminal')['active']
        assert 'ValueError: DEFAULT_TIMEOUT must be >= 1 (got 0)' in self.lab.state['scene']['terminal']['output']
        yield from frame('search-01-traceback')
        yield from chord('Alt+Tab')
        yield from chord('Ctrl+Shift+F')
        yield from chord('Esc')
        assert not self.lab.state['finished'] and self.lab.state['task']['id'] == 'project-search-submit'
        yield from chord('Ctrl+Shift+F')
        for char in 'DEFAULT_TIMEOUT':
            yield from chord('Shift+-' if char == '_' else 'Shift+' + char)
        assert window('code')['input'] == 'DEFAULT_TIMEOUT'
        yield from chord('Enter')
        result = window('code')
        assert result['focus'] == 'project-results' and result['projectSearchQuery'] == 'DEFAULT_TIMEOUT'
        assert [entry['title'].split(' · ')[0] for entry in result['entries']] == [
            'archive.py:2:1', 'config.py:2:1', 'notes.py:1:9'], result
        yield from chord('Down')
        assert window('code')['optionIndex'] == 1
        yield from frame('search-02-results')
        yield from chord('Enter')
        view = window('code')
        assert view['url'].endswith('/src/config.py') and view['bodyCursor'] == view['body'].index('DEFAULT_TIMEOUT')
        yield from frame('search-03-config')
        for key in ('End', 'Backspace', '5'):
            yield from chord(key)
        assert 'DEFAULT_TIMEOUT = 5' in window('code')['body'] and not self.lab.state['finished']
        yield from chord('Ctrl+S')
        assert self.lab.state['finished'] and self.lab.state['solved'] == 6
        yield from frame('search-04-saved')

        yield from start('code', 0, 'code-line-edit')
        original = "import numpy as np\ndef ifft(x): return x\nprint('next')"
        commented = "import numpy as np\n# def ifft(x): return x\nprint('next')"
        deleted = "import numpy as np\nprint('next')"
        def check_line(body, anchor, cursor):
            view = window('code')
            assert (view['body'], view['bodyAnchor'], view['bodyCursor']) == (body, anchor, cursor), view
        check_line(original, 19, 19)
        for gesture, body, anchor, caret in (
            ('Ctrl+L', original, 19, 41), ('Ctrl+/', commented, 19, 43),
            ('Ctrl+Z', original, 19, 41), ('Ctrl+Shift+K', deleted, 19, 19),
            ('Ctrl+Z', original, 19, 41),
        ):
            yield from chord(gesture)
            check_line(body, anchor, caret)

        yield from start('code', 1, 'code-diagnostic-test')
        assert self.lab.pause.get_label() == 'Ⅱ F6', self.lab.pause.get_label()
        assert self.lab.state['task']['keys'] == 'F8'
        assert window('code')['focus'] == 'code-problems'
        yield from chord('F6')
        assert self.lab.state['paused'] and self.lab.state['solved'] == 0
        yield from chord('Space')
        assert not self.lab.state['paused'], self.lab.status.get_label()
        yield from chord('F8')
        assert not self.lab.state['paused'] and self.lab.state['solved'] == 1
        assert position() == ('units.py', 42), position()
        assert self.lab.pause.get_label() == 'Ⅱ F6'
        yield from chord('Ctrl+.')
        assert window('code')['focus'] == 'code-quick-fix'
        assert [entry['path'] for entry in window('code')['entries']] == ['round', 'decimal']
        yield from frame('code-diagnostics-choices')
        yield from chord('Down')
        yield from chord('Enter')
        assert 'return kelvin - 273.15' in window('code')['body']
        yield from chord('Ctrl+S')
        yield from chord('Ctrl+`')
        command = 'python -m unittest test_units.UnitsTests.test_kelvin_to_celsius'
        for char in command:
            press = ('Space' if char == ' ' else 'Shift+-' if char == '_' else
                     'Shift+' + char if char.isupper() else char.upper())
            yield from chord(press)
        assert self.lab.state['scene']['terminalInput'] == command
        yield from chord('Enter')
        assert self.lab.state['finished'] and self.lab.state['solved'] == 7
        assert 'Ran 1 test' in self.lab.state['scene']['terminal']['output']
        print('PASS real GTK F6 pause / F8 diagnosis, chooser, save and single simulated test', flush=True)

        yield from start('ubuntu', 1, 'ubuntu-gnome-windows')
        recording_boundary('gnome')
        assert len(self.lab.state['scene']['windows']) == 1
        yield from chord('Super+Left')
        assert window('chrome')['state'] == 'left'
        yield from frame('gnome-01-single-left')
        # The learner's GNOME favorites define the slot on Linux. If Code is
        # not pinned, the genuine exercise asks for Super and an overview
        # search query instead. Drive the visible route and require its credit.
        task, solved = self.lab.state['task'], self.lab.state['solved']
        assert task['id'] == 'open-code', task
        if task['keys']:
            assert task['keys'].startswith('Super+'), task
            yield from chord(task['keys'])
        else:
            assert task['text'], task
            yield from chord('Super')
            assert self.lab.state['scene']['overview'], self.lab.state['status']
            layout = self.lab.state['scene']['layout'].lower()
            physical = {letter: ('Shift+' if shifted else '') + cap['key']
                        for row in self.library['keyboard'] for cap in row
                        for shifted in (False, True)
                        for letter in [cap[layout + ('Shift' if shifted else '')]]
                        if len(letter) == 1}
            for char in task['text']:
                yield from chord(physical[char])
            yield from chord('Enter')
        assert self.lab.state['solved'] == solved + 1, (task, self.lab.state['status'])
        origin = position()
        yield from chord('Super+Right')
        assert window('code')['state'] == 'right' and window('chrome')['state'] == 'left'
        yield from frame('gnome-02-two-halves')
        yield from chord('Super+Right')
        assert window('code')['state'] == 'normal'
        yield from chord('Super+Right')
        yield from chord('Super+Up')
        assert window('code')['state'] == 'maximized'
        yield from frame('gnome-03-maximized')
        yield from chord('Super+Down')
        assert window('code')['state'] == 'normal'
        yield from chord('Super+Right')
        yield from chord('Super+H')
        assert window('code')['state'] == 'minimized' and window('chrome')['active']
        yield from frame('gnome-04-minimized')
        if switch_preview:
            yield from switch_preview(frame)
        else:
            yield from chord('Alt+Tab')
        assert window('code')['state'] == 'right' and window('code')['active']
        assert position() == origin, (position(), origin)
        yield from frame('gnome-05-restored')
        for key in 'GNOME':
            yield from chord(key)
        assert window('code')['body'].splitlines()[0] == '# gnome'
        yield from frame('gnome-06-continued-typing')
        yield from chord('Enter')
        for gesture in ('Alt+Tab', 'Super+Left', 'Super+Left', 'Alt+F4'):
            yield from chord(gesture)
        assert len(self.lab.state['scene']['windows']) == 1 and window('code')['state'] == 'right'
        yield from chord('Super+H')
        assert window('code')['state'] == 'minimized' and not window('code')['active']
        yield from chord('Alt+Tab')
        assert window('code')['state'] == 'right' and window('code')['active']
        for gesture in ('Super+Down', 'Super+Right', 'Ctrl+S'):
            yield from chord(gesture)
        assert self.lab.result, 'expected catalog mission result'
        yield from frame('gnome-07-result')
        recording_boundary(None)
        print('PASS daily-use native GTK exact shortcuts, references/rename, GNOME window state and caret', flush=True)

    def record_lesson_chrome(self, lab, app, output):
        mode = os.environ.get("TOUCH_TYPING_QA_LESSON_CHROME")
        if mode not in ("baseline", "compact"):
            return
        def bounds(widget):
            valid, rect = widget.compute_bounds(self.window)
            assert valid, widget
            return [round(getter(), 2) for getter in (rect.get_x, rect.get_y, rect.get_width, rect.get_height)]
        def footer_controls():
            control = lab.footer.get_first_child()
            while control is not None:
                yield control
                control = control.get_next_sibling()
        toolbar, scene, footer = [bounds(widget) for widget in (lab.toolbar, lab.stage, lab.footer)]
        shown = self.reference_title_handle.get_mapped()
        controls = list(footer_controls())
        settings_in_footer = lab.settings_menu in controls
        back_in_footer = any(widget.get_tooltip_text() == "Вернуться к каталогу" and widget.get_mapped()
                             for widget in controls)
        report = dict(app=app, capture=mode, platform="GTK", scale=self.window.get_scale_factor(),
                      viewport=[self.window.get_width(),self.window.get_height()], titleMapped=shown,
                      task=toolbar, scene=scene, footer=footer, footerSettings=settings_in_footer,
                      footerBack=back_in_footer, ownedNativeWindow=self.window.get_mapped())
        (output / f"lesson-layout-gtk-{app}.json").write_text(json.dumps(report, indent=2)+"\n",encoding="utf-8")
        print("GTK lesson geometry",json.dumps(report),flush=True)
        assert self.window.get_mapped() and lab.stage.get_mapped() and lab.footer.get_mapped()
        assert scene[3] > 0 and toolbar[1] + toolbar[3] <= scene[1] + 1 and scene[1] + scene[3] <= footer[1] + 1
        if mode == "compact":
            assert not shown and settings_in_footer and back_in_footer, report
            assert lab.settings_menu.get_sensitive() and lab.pause.get_mapped() and lab.restart.get_mapped()
            assert lab.settings_menu.get_popover() is not None, "settings must retain the actual popover"

    def capture_gallery(self, chord, capture_png, output):
        lab = self.lab
        for _ in range(12):
            yield
        capture_png(output / "live-catalog.png")
        for app, stage_name in (("ubuntu", "desktop"), ("terminal", "terminal"), ("chrome", "browser"),
                                ("code", "editor"), ("workday", "workday")):
            lab = self.lab
            lab.picker.set_selected(next(i for i,row in enumerate(lab.classes) if row["id"] == app))
            lab.level.set_selected(0)
            lab.limit.set_selected(0)
            if app == "ubuntu":
                lab.mission_picker.set_selected(next(i for i,row in enumerate(lab.choices)
                                                     if row[0] == "ubuntu-restore"))
            if app == "workday":
                lab.mission_picker.set_selected(next(i for i,row in enumerate(lab.choices)
                                                     if row[0] == "workday-report"))
            lab.start()
            for _ in range(45):
                yield
            if lab.state["paused"] and not self.window.is_active():
                yield from chord("Enter")
            for _ in range(100):
                if not lab.state["paused"]:
                    break
                yield
            assert lab.state["app"] == app and not lab.state["paused"], (app, lab.status.get_label())

            def active():
                return next((w["app"] for w in lab.state["scene"]["windows"] if w["active"]), None)

            if app == "ubuntu":
                if active() is not None:
                    yield from chord("Super+D")
                assert active() is None or lab.state["scene"]["showDesktop"]
            else:
                if app == "workday":
                    assert lab.state["task"]["keys"], lab.state["task"]
                    yield from chord(lab.state["task"]["keys"])
                assert active() == ("chrome" if app == "workday" else app), (app, active())
            for _ in range(8):
                yield
            capture_png(output / ("live-" + stage_name + ".png"))
            self.record_lesson_chrome(lab, app, output)
            if app == "ubuntu":
                yield from chord("Super+4")
                assert active() == "files", lab.state["scene"]
                for _ in range(8):
                    yield
                capture_png(output / "live-files.png")
            lab.stop()
            assert lab.result
            self.navigate("lab")
            for _ in range(8):
                yield
        for page in ("typing", "editor", "progress"):
            self.navigate(page)
            for _ in range(10):
                yield
            capture_png(output / ("studio-" + page + ".png"))

    def exercise_soft_limit(self, chord, hold, capture_png, output):
        for _ in range(3):
            yield from chord("A")
            for _ in range(6):
                yield
            if getattr(self, "last_character", "").lower() == "a" and self.layout == "EN":
                break
            yield from chord("Super+Space")
            for _ in range(40):
                yield
        else:
            raise AssertionError("Could not establish native English input source")
        lab = self.lab
        lab.picker.set_selected(next(i for i, row in enumerate(lab.classes) if row["id"] == "terminal"))
        lab.level.set_selected(1)
        lab.limit.set_selected(1)  # the actual 20-action picker, no mocked quota
        lab.time_limit.set_selected(1)  # both budgets are live
        lab.mission_picker.set_selected(next(i for i,row in enumerate(lab.choices) if row[0] == "terminal-access"))
        lab.start()
        for _ in range(45):
            yield
        if lab.state["paused"] and not self.window.is_active():
            yield from chord("Enter")
        for _ in range(100):
            if lab.active and not lab.state["paused"]:
                break
            yield
        assert lab.active and not lab.state["paused"]
        for _ in range(20):
            task, old = lab.state["task"], lab.state["solved"]
            if task["keys"]:
                for _ in range(28):
                    yield from chord(task["keys"])
                    if lab.state["solved"] != old:
                        break
            else:
                if task["id"].startswith("open-") and not lab.state["scene"]["overview"]:
                    yield from chord("Super")
                layout = lab.state["scene"]["layout"].lower()
                letters = {" ": "Space"}
                for group in self.library["keyboard"]:
                    for cap in group:
                        for suffix, modifier in (("", ""), ("Shift", "Shift+")):
                            letter = cap[layout + suffix]
                            if len(letter) == 1:
                                letters[letter] = modifier + cap["key"]
                for char in task["text"]:
                    yield from chord(letters[char])
                if task["id"] != "type-code":
                    yield from chord("Enter")
            assert lab.state["solved"] == old + 1, (task,lab.state["status"])
        state = lab.state
        assert (state["solved"],state["actionLimit"],state["timeLimit"]) == (20,20,180)
        assert state.get("limitReached") == "actions" and state["remaining"] > 0
        assert lab.timer.get_label().startswith("20/20 · ")
        assert state["limitDecisionPending"] and state["paused"] and not state["finished"]
        assert not lab.content.get_sensitive() and lab.modal_layer.get_mapped()
        assert self.call("lab-result") is None
        capture_png(output / "soft-limit-decision.png")
        await_scene = state["scene"]
        yield from chord("Enter")   # no automatic/default selection
        yield from chord("A")       # no leaked text into the simulated terminal
        assert lab.state["limitDecisionPending"] and lab.state["scene"] == await_scene
        for _ in range(6):
            if self.window.get_focus() == lab.limit_actions[1]:
                break
            yield from chord("Tab")
        assert self.window.get_focus() == lab.limit_actions[1]
        yield from hold("Enter")  # letting it physically release is part of reacquisition
        for _ in range(130):
            if not lab.state["paused"] and self.window.get_focus() == lab.desktop:
                break
            yield
        assert lab.state["finishingMission"] and not lab.state["limitDecisionPending"] and not lab.state["paused"]
        assert lab.state["solved"] == 20 and lab.state["scene"] == await_scene
        assert self.window.get_focus() == lab.desktop
        capture_png(output / "soft-limit-continue.png")
        yield from chord("F8")
        assert lab.state["paused"] and lab.state["finishingMission"] and lab.modal_layer.get_mapped()
        yield from chord("Space")
        for _ in range(100):
            if not lab.state["paused"]:
                break
            yield
        assert not lab.state["paused"]
        yield from chord("F10")
        assert lab.result and self.call("lab-result")["solved"] == 20
        capture_png(output / "soft-limit-result.png")
        print("PASS native GTK soft action limit: blocked input, held Enter, same scene and focus, pause and finish",
              flush=True)

    def exercise_terminal(self, chord, hold, capture_png, output):
        from gi.repository import Gtk

        def switch_layout(target):
            for attempt in range(3):
                yield from chord('A')
                if self.layout == target:
                    return
                if attempt < 2:
                    yield from chord('Super+Space')
                    for _ in range(35):
                        yield
            raise AssertionError(('OS layout not switched', self.layout, target))

        def start_terminal(layout):
            yield from switch_layout(layout)
            lab = self.lab
            lab.picker.set_selected(next(i for i, row in enumerate(lab.classes) if row['id'] == 'terminal'))
            lab.level.set_selected(1)
            lab.limit.set_selected(0)
            lab.start()
            for _ in range(45):
                yield
            if lab.state['paused'] and not self.window.is_active():
                # Only the disposable headless test session may accept its own prompt.
                yield from chord('Enter')
            for _ in range(140):
                if lab.active and not lab.state['paused']:
                    break
                yield
            assert lab.active and not lab.state['paused'], lab.status.get_label()
            assert self.window.is_active() and self.window.get_focus() == lab.desktop
            assert lab.state["scene"]["layout"] == layout

        yield from start_terminal('EN')
        lab = self.lab

        def field():
            scene = lab.state['scene']
            terminal = next(w for w in scene['windows'] if w['app'] == 'terminal' and w['active'])
            return scene['terminalInput'], terminal['cursor']

        def editing(expected, *keys):
            for key in keys:
                yield from chord(key)
            assert field() == expected, (keys, field(), expected)

        # Character index, not end of line, governs each editing key.
        yield from editing(('', 0), 'Ctrl+C')
        yield from editing(('abcd', 4), 'A', 'B', 'C', 'D', 'Right')
        yield from editing(('abcd', 2), 'Left', 'Left')
        yield from editing(('abd', 2), 'Delete')
        capture_png(output / 'terminal-en-delete.png', lab.desktop)
        yield from editing(('ad', 1), 'Backspace')
        yield from editing(('ad', 0), 'Left', 'Left', 'Backspace')
        yield from editing(('ad', 2), 'Right', 'Right', 'Right', 'Delete')

        yield from editing(('', 0), 'Ctrl+C')
        for key in 'ABCDE' * 8:
            yield from chord(key)
        original, cursor = field()
        yield from hold('Left')
        after_left, position = field()
        assert after_left == original and 0 <= position <= cursor - 3, field()
        capture_png(output / 'terminal-en-held-left.png')
        yield from hold('Delete')
        after_delete, position_after = field()
        removed = len(original) - len(after_delete)
        assert removed >= 2 and position_after == position, field()
        assert after_delete == original[:position] + original[position + removed:], field()
        capture_png(output / 'terminal-en-held-delete.png')

        # History Up then Down restores the unsubmitted line, not the command.
        yield from editing(('', 0), 'Ctrl+C')
        yield from editing(('pwd', 3), 'P', 'W', 'D')
        yield from editing(('', 0), 'Enter')
        yield from editing(('ab', 2), 'A', 'B')
        yield from editing(('pwd', 3), 'Up')
        yield from editing(('ab', 2), 'Down')

        lab.stop()
        self.navigate('lab')
        yield from start_terminal('RU')
        lab = self.lab
        yield from editing(('', 0), 'Ctrl+C')
        yield from editing(('фисв', 4), 'A', 'B', 'C', 'D')
        yield from editing(('фисв', 2), 'Left', 'Left')
        yield from editing(('фив', 2), 'Delete')
        capture_png(output / 'terminal-ru-delete.png', lab.desktop)
        yield from editing(('фв', 1), 'Backspace')

        # The actual GTK window must pause on genuine focus loss. The focused
        # other window receives the next physical key, never the lab buffer.
        decoy = Gtk.ApplicationWindow(application=self, title='QA focus probe')
        try:
            decoy.present()
            for _ in range(100):
                if decoy.is_active() and not self.window.is_active() and lab.state['paused']:
                    break
                yield
            assert decoy.is_active() and lab.state['paused']
            yield from editing(('фв', 1), 'A')
        finally:
            decoy.close()

        found, bounds = lab.desktop.compute_bounds(self.window)
        assert found and bounds.get_x() >= 0 and bounds.get_y() >= 0
        assert bounds.get_x() + bounds.get_width() <= self.window.get_width() + 1
        assert bounds.get_y() + bounds.get_height() <= self.window.get_height() + 1
        print('GTK terminal geometry', self.window.get_width(), self.window.get_height(),
              bounds.get_x(), bounds.get_y(), bounds.get_width(), bounds.get_height(), flush=True)

if __name__=='__main__':
    if os.environ.get('WAYLAND_DISPLAY')!='training-qa':raise SystemExit('Private test compositor required')
    bus=Gio.bus_get_sync(Gio.BusType.SESSION)
    pid,=bus.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','GetConnectionUnixProcessID',GLib.Variant('(s)',('org.gnome.Mutter.RemoteDesktop',)),None,Gio.DBusCallFlags.NONE,3000,None).unpack()
    assert b'--headless' in Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
    app=LabWindowTest();status=app.run([sys.argv[0]]);sys.exit(1 if app.smoke_failure else status)
