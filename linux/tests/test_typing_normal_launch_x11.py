"""External black-box smoke: launch the extracted executable, navigate and type.

Unlike native introspection harnesses this does not import Studio/platform_linux,
bind a test UI, use verification mode, drive internal RPC, write a seed profile
or replace the normal entry point. Native XTest goes exclusively to the window
owned by this process on an authorized, disposable Xvfb display.
"""
import argparse
import ctypes as C
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET


def run(application, data_root, out):
    if not Path(application).is_file() or not (Path(application).parent / "engine" / "TouchTyping.Engine").is_file():
        raise ValueError("Pass touch_typing.py from an actual extracted self-contained package")
    if not Path(data_root).is_absolute() or Path(data_root) == Path.home() / ".local/share":
        raise ValueError("Use a disposable absolute profile parent")
    display = os.environ["DISPLAY"]
    # Sandboxed builders can lack AF_UNIX; permit only authenticated loopback
    # to our own disposable Xvfb. Never target an arbitrary hostname/display.
    match = re.fullmatch(r"(?:127\.0\.0\.1)?:(\d+)(?:\.0)?", display)
    if display.startswith("127.0.0.1:"):
        auth = Path(os.environ.get("XAUTHORITY", "/dev/null"))
        if not auth.is_file() or auth.stat().st_uid != os.getuid() or auth.stat().st_mode & 0o077:
            raise ValueError("Disposable loopback Xvfb requires a private, owned Xauthority")
    if not match or os.environ.get("GDK_BACKEND") != "x11":
        raise ValueError("Disposable local X11 display required")
    process = subprocess.run(["pgrep", "-u", str(os.getuid()), "-f", "^([^ ]*/)?Xvfb :" + match[1] + "( |$)"],
                             capture_output=True, text=True)
    if process.returncode != 0 or not process.stdout.strip():
        raise ValueError("Guard: no isolated Xvfb owns the selected display")
    for name in ("xwininfo", "xprop", "ffmpeg"):
        if not shutil.which(name):
            raise ValueError("Missing GUI test tool " + name)
    output = Path(out)
    output.mkdir(exist_ok=True, parents=True)
    lib = C.CDLL("libX11.so.6")
    xt = C.CDLL("libXtst.so.6")
    lib.XOpenDisplay.restype = C.c_void_p
    dpy = lib.XOpenDisplay(None)
    if not dpy:
        raise RuntimeError("No X11 connection")
    lib.XSetInputFocus.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_ulong]
    lib.XGetInputFocus.argtypes = [C.c_void_p, C.POINTER(C.c_ulong), C.POINTER(C.c_int)]
    lib.XFlush.argtypes = [C.c_void_p]
    lib.XStringToKeysym.argtypes = [C.c_char_p]
    lib.XStringToKeysym.restype = C.c_ulong
    lib.XKeysymToKeycode.argtypes = [C.c_void_p, C.c_ulong]
    lib.XKeysymToKeycode.restype = C.c_uint
    xt.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
    lib.XInternAtom.argtypes = [C.c_void_p, C.c_char_p, C.c_int]
    lib.XInternAtom.restype = C.c_ulong
    class Data(C.Union):
        _fields_ = [("l", C.c_long * 5)]
    class Message(C.Structure):
        _fields_ = [("type", C.c_int), ("serial", C.c_ulong), ("sent", C.c_int), ("display", C.c_void_p),
                    ("window", C.c_ulong), ("message_type", C.c_ulong), ("fmt", C.c_int), ("data", Data)]
    class Event(C.Union):
        _fields_ = [("client", Message), ("pad", C.c_long * 24)]
    lib.XSendEvent.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_long, C.POINTER(Event)]
    lib.XCloseDisplay.argtypes = [C.c_void_p]
    def cmd(*args):
        return subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace",
                              env=dict(os.environ, LC_ALL="C")).stdout
    def owned(w, pid):
        return bool(re.search(r"=\s*" + str(pid) + r"\s*$", cmd("xprop", "-id", hex(w), "_NET_WM_PID")))
    def window(proc):
        until = time.monotonic()+18
        while time.monotonic()<until and proc.poll() is None:
            for handle in re.findall(r"^\s*(0x[\da-fA-F]+)\s+", cmd("xwininfo","-root","-tree"), re.MULTILINE):
                wid = int(handle,16)
                if not owned(wid, proc.pid):
                    continue
                info = cmd("xwininfo","-id",handle)
                if "Map State: IsViewable" in info and int(re.search(r"Width:\s*(\d+)", info)[1]) >= 700:
                    return wid
            time.sleep(.1)
        raise RuntimeError("No foreground application window in the owned fresh display")
    def frame(wid, name):
        # XID selects client content; captions/time are NOT painted onto the app.
        subprocess.run(["ffmpeg","-nostdin","-loglevel","error","-y","-f","x11grab","-window_id",str(wid),
                        "-i",display,"-frames:v","1","-threads","1",str(output/(name+".png"))],check=True,timeout=12)
    def press(wid, proc, *names):
        actual=C.c_ulong(); revert=C.c_int()
        lib.XGetInputFocus(dpy, C.byref(actual), C.byref(revert))
        if actual.value != wid or proc.poll() is not None or not owned(wid, proc.pid):
            raise RuntimeError("Refusing to send a key to a window outside this test")
        keys=[lib.XKeysymToKeycode(dpy, lib.XStringToKeysym(name.encode("ascii"))) for name in names]
        assert all(keys), names
        for key in keys:
            xt.XTestFakeKeyEvent(dpy, key, True, 0)
        for key in reversed(keys):
            xt.XTestFakeKeyEvent(dpy, key, False, 0)
        lib.XFlush(dpy)
        time.sleep(.14)
    def close_owned(wid, proc):
        if not owned(wid,proc.pid): raise RuntimeError("Refusing to close unrelated window")
        ev=Event()
        ev.client.type=33  # The normal WM_DELETE_WINDOW request, not a process kill.
        ev.client.display=dpy
        ev.client.window=wid
        ev.client.message_type=lib.XInternAtom(dpy,b"WM_PROTOCOLS",False)
        ev.client.fmt=32
        ev.client.data.l[0]=lib.XInternAtom(dpy,b"WM_DELETE_WINDOW",False)
        lib.XSendEvent(dpy,wid,False,0,C.byref(ev));lib.XFlush(dpy)
        assert proc.wait(timeout=12) == 0
    with tempfile.TemporaryDirectory(dir=data_root,prefix="external-gtk-") as root:
        env=dict(os.environ,XDG_DATA_HOME=root)
        for k in ("TOUCH_TYPING_ENGINE","TOUCH_TYPING_QA_DATA_ROOT","TOUCH_TYPING_QA_SEED","PYTHONPATH"): env.pop(k,None)
        with open(output/"ordinary-launch.log","w") as log:
            proc = subprocess.Popen([str(Path(application).resolve())],cwd=Path(application).resolve().parent,
                                    env=env, stdout=log, stderr=log)
            try:
                wid=window(proc)
                lib.XSetInputFocus(dpy,wid,1,0);lib.XFlush(dpy);time.sleep(.55)
                press(wid,proc,"Alt_L","2");time.sleep(.4)
                frame(wid,"ordinary-01-course")
                press(wid,proc,"F5");time.sleep(.5)
                frame(wid,"ordinary-02-classic")
                for key in ("x","q","z"): press(wid,proc,key)
                press(wid,proc,"Escape");time.sleep(.5)
                frame(wid,"ordinary-03-result")
                path=Path(root)/"TouchTypingStudio"/"progress.xml"
                doc=ET.parse(path)
                attempts=[r for r in doc.findall("./Results/TypingResult") if r.findtext("Activity")=="TypingPractice"]
                assert len(attempts)==1 and attempts[0].findtext("Task")=="Russian"
                assert attempts[0].findtext("Completed")=="false" and int(attempts[0].findtext("Errors"))>0
                assert not doc.findall("./TypingReviews/TypingReviewTicket"), "withdrawn attempt created a correction"
                close_owned(wid,proc)
                print("PASS: actual extracted GTK executable (no flags, no imported UI), empty normal profile, "
                      "real Alt+2 → F5 → three US strokes → Esc → result → clean close.",flush=True)
            finally:
                if proc.poll() is None:
                    proc.terminate();proc.wait(timeout=6)
                lib.XCloseDisplay(dpy)


if __name__=="__main__":
    cli=argparse.ArgumentParser(description=__doc__)
    cli.add_argument("application",type=Path)
    cli.add_argument("data_root",type=Path)
    cli.add_argument("output",type=Path)
    ns=cli.parse_args()
    run(ns.application,ns.data_root,ns.output)
