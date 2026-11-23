"""Capture the real GTK application's client window, from an isolated Wayland session."""
import json
import os
from pathlib import Path
import sys
import traceback
from gi.repository import GLib, Gtk
from touch_typing import Studio

WIDTH, HEIGHT = 1006, 863

class ScreenshotApplication(Studio):
    def __init__(self):
        super().__init__(smoke="-", smoke_size=(WIDTH, HEIGHT))
        self.measurements = []
        self.count = 0

    def smoke_test(self):
        try:
            self.navigate("home")
            GLib.timeout_add(50, self.capture_after_layout)
        except Exception:
            traceback.print_exc()
            self.smoke_failure = True
            self.quit()
        return GLib.SOURCE_REMOVE

    def capture_after_layout(self):
        self.count += 1
        try:
            paintable = Gtk.WidgetPaintable.new(self.window)
            width = paintable.get_intrinsic_width()
            height = paintable.get_intrinsic_height()
            self.measurements.append((width,height))
            if len(self.measurements) < 4 or self.measurements[-4:] != [(WIDTH,HEIGHT)]*4:
                if self.count < 80:
                    return GLib.SOURCE_CONTINUE
                raise RuntimeError(f"expected settled {WIDTH}×{HEIGHT} GTK window, got {self.measurements[-12:]}")
            assert self.mode == "home", self.mode
            assert self.window.get_mapped(), "GTK window was not mapped"
            snapshot = Gtk.Snapshot()
            paintable.snapshot(snapshot,width,height)
            texture = self.window.get_renderer().render_texture(snapshot.to_node(),None)
            assert (texture.get_width(),texture.get_height()) == (WIDTH,HEIGHT)
            output = Path(os.environ["TOUCH_TYPING_QA_OUTPUT"])
            texture.save_to_png(str(output/"real-home-gtk.png"))
            report = {"screen": "home", "platform": "GTK", "native_window": True,
                      "capture": "mapped Gtk.ApplicationWindow widget texture; excludes compositor shadow",
                      "pixels": [width,height], "scale_factor": self.window.get_scale_factor(),
                      "layout": self.layout, "isolated_data_root": str(Path(os.environ["XDG_DATA_HOME"]))}
            (output/"home-gtk.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
            print(json.dumps(report,ensure_ascii=False),flush=True)
        except Exception:
            traceback.print_exc()
            self.smoke_failure = True
        self.quit()
        return GLib.SOURCE_REMOVE

def main():
    # Invoked inside qa_runner's disposable environment, never captured from a
    # user profile. Require the private XDG path and reject an override.
    root = Path(os.environ.get("XDG_DATA_HOME",""))
    if not root.is_absolute() or not root.is_dir() or os.environ.get("TOUCH_TYPING_QA_DATA_ROOT"):
        raise SystemExit("run inside private GNOME QA session with an isolated XDG_DATA_HOME")
    app=ScreenshotApplication()
    code=app.run([])
    return 1 if app.smoke_failure else code

if __name__ == "__main__":
    sys.exit(main())
