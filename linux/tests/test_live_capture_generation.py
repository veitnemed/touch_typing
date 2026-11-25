"""Deterministic test of product callbacks with a timer scheduler and GDK surface spies."""
import ast
from pathlib import Path
from types import SimpleNamespace as S
import sys

class Scheduler:
    SOURCE_REMOVE = False
    def __init__(self):
        self.callbacks = {}
        self.n = 0
    def timeout_add(self, delay, callback, *args):
        self.n += 1
        self.callbacks[self.n] = (delay, callback, args)
        return self.n
    def timeout_add_seconds(self, delay, callback, *args):
        return self.timeout_add(1000 * delay, callback, *args)
    def idle_add(self, callback, *args):
        return self.timeout_add(0, callback, *args)
    def source_remove(self, token):
        assert self.callbacks.pop(token, None) is not None
    def run(self, token):
        _, f, args = self.callbacks.pop(token)
        f(*args)
    def latest(self, delay):
        return max(k for k, (d, *_rest) in self.callbacks.items() if d == delay)

class Surface:
    inhibited = False
    def connect(self, signal, notify):
        assert signal == "notify::shortcuts-inhibited"
        self.notify = notify
        return 42
    def disconnect(self, token):
        assert token == 42
    def restore_system_shortcuts(self):
        self.inhibited = False
    def inhibit_system_shortcuts(self, unused):
        assert unused is None
    def get_property(self, name):
        assert name == "shortcuts-inhibited"
        return self.inhibited

def lab_for(path, backend="GdkWaylandDisplay"):
    scheduler = Scheduler()
    namespace = dict(GLib=scheduler, Gdk=S(Toplevel=Surface,
                     ModifierType=S(CONTROL_MASK=1, SHIFT_MASK=2, ALT_MASK=4, SUPER_MASK=8)))
    methods = {"acquire", "resolve_capture", "check_denied", "capture_expired",
               "release_capture", "clear_capture_timers", "schedule_capture_denial", "focus", "resume_with_capture"}
    lab_ast = next(c for c in ast.parse(path.read_text()).body if isinstance(c, ast.ClassDef) and c.name == "LabPage")
    lab_ast.body = [f for f in lab_ast.body if isinstance(f,ast.FunctionDef) and f.name in methods]
    lab_ast.bases = []
    tree = ast.Module(body=[lab_ast],type_ignores=[])
    exec(compile(tree,str(path),"exec"),namespace)
    lab = namespace["LabPage"]()
    surface=Surface()
    result={"paused": False}
    lab.host=S(window=S(is_active=lambda:True,get_surface=lambda:surface,
        get_display=lambda:S(__gtype__=S(name=backend),
            get_default_seat=lambda:S(get_keyboard=lambda:S(get_modifier_state=lambda:0)))),call=lambda operation:result)
    lab.active=True
    lab.waiting_capture=False
    lab.save_pending=lab.limit_resume_wait=False
    lab.state={"paused": True}
    lab.status=S(set_label=lambda value:setattr(lab,"notice",value),set_visible=lambda value:None)
    lab.surface=lab.handler=None
    lab.window_only_keys=False
    lab.pressed=set()
    lab.physical_down=set()
    lab.capture_timeout=lab.capture_denied_timer=lab.capture_generation=0
    lab.modal_layer=S(set_visible=lambda value:None)
    lab.desktop=S(grab_focus=lambda:None)
    lab.render=lambda state:setattr(lab,"state",state)
    lab.show_pause_modal=lambda:setattr(lab,"modal_shown",True)
    return lab,surface,scheduler

def repro(path):
    lab,s,sch=lab_for(path)
    lab.acquire()
    first_deny=sch.latest(700)
    first_idle=sch.latest(0)
    s.inhibited=True
    # Simulate an observed first grant, while its old optional denial
    # callback still exists or has been queued to dispatch.
    stale_deny=sch.callbacks[first_deny]
    stale_idle=sch.callbacks[first_idle]
    sch.run(first_idle)
    assert not lab.waiting_capture and not lab.state["paused"]
    lab.release_capture()
    lab.state={"paused":True}
    lab.acquire()
    second_deny=sch.latest(700)
    assert second_deny != first_deny and lab.waiting_capture
    stale_idle[1](*stale_idle[2])
    if first_deny in sch.callbacks:
        sch.run(first_deny)
    else:
        stale_deny[1](*stale_deny[2]) # already-dispatched stale callback
    assert lab.waiting_capture and lab.surface is s, "old denial cancelled live second request"
    # The actual second denial remains effective; no consent bypass.
    sch.run(second_deny)
    assert not lab.waiting_capture and lab.surface is None and "отклонён" in lab.notice
    print("OK old timers isolated from new request; true current denial remains effective")

def regain_focus(path):
    lab,s,sch=lab_for(path)
    focused=True
    lab.host.window.is_active=lambda: focused
    lab.acquire()
    existing=sch.latest(700)
    lab.focus(True)
    assert not any(delay==300 for delay,*_ in sch.callbacks.values()), "duplicate current denial timer"
    focused=False
    sch.run(existing)  # no active focus, permission not yet determined
    assert lab.waiting_capture and lab.capture_denied_timer==0
    focused=True
    lab.focus(True)
    next_denial=sch.latest(300)
    lab.focus(True)
    assert [k for k,value in sch.callbacks.items() if value[0]==300]==[next_denial]
    s.inhibited=True
    sch.run(sch.latest(0))
    assert not lab.waiting_capture and not lab.state["paused"] and not lab.capture_denied_timer
    assert next_denial not in sch.callbacks
    print("OK focus regain queues only one current denial, real grant cancels it")

def focus_local_x11(path):
    lab, s, sch = lab_for(path, "GdkX11Display")
    def forbidden(_):
        raise AssertionError("X11 cannot request Wayland keyboard inhibition")
    s.inhibit_system_shortcuts = forbidden
    lab.host.window.is_active = lambda: False
    lab.acquire()
    assert lab.state["paused"] and not lab.window_only_keys and not sch.callbacks
    lab.host.window.is_active = lambda: True
    lab.acquire()
    assert not lab.state["paused"] and lab.window_only_keys and lab.surface is None and not sch.callbacks
    lab.release_capture()
    assert not lab.window_only_keys
    print("OK focused X11 resumes without a global grab; background X11 cannot resume")


if __name__ == "__main__":
    path=Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "touch_typing.py"
    repro(path)
    regain_focus(path)
    focus_local_x11(path)
