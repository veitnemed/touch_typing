"""Capture the real GTK application's client window, from an isolated Wayland session."""
import json
import os
from pathlib import Path
import sys
import traceback
from gi.repository import GLib, Gtk
from touch_typing import Pango, PangoCairo, ROOT, Studio

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
            settled = len(self.measurements) >= 4 and self.measurements[-4:] == [(WIDTH,HEIGHT)]*4
            if not settled and self.count < 80:
                return GLib.SOURCE_CONTINUE
            assert self.mode == "home", self.mode
            assert self.window.get_mapped(), "GTK window was not mapped"
            snapshot = Gtk.Snapshot()
            paintable.snapshot(snapshot,width,height)
            texture = self.window.get_renderer().render_texture(snapshot.to_node(),None)
            width, height = texture.get_width(), texture.get_height()
            exact = settled and (width,height) == (WIDTH,HEIGHT)
            output = Path(os.environ["TOUCH_TYPING_QA_OUTPUT"])
            name = "real-home-gtk.png" if exact else "real-home-gtk-size-mismatch.png"
            texture.save_to_png(str(output/name))
            fontdir = ROOT / "fonts"
            if not fontdir.is_dir():
                fontdir = ROOT.parent / "assets" / "fonts"
            font_map = PangoCairo.FontMap.get_default()
            context = font_map.create_context()
            weights = []
            for weight, face in ((400, "Regular"), (500, "Medium"), (700, "Bold")):
                assert (fontdir / f"Roboto-{face}.ttf").is_file(), f"missing packaged Roboto {face}"
                requested = Pango.FontDescription()
                requested.set_family("Roboto")
                requested.set_weight(Pango.Weight(weight))
                requested.set_absolute_size(20*Pango.SCALE)
                resolved = font_map.load_font(context, requested).describe()
                assert resolved.get_family() == "Roboto" and int(resolved.get_weight()) == weight, resolved.to_string()
                label = Pango.Layout.new(context)
                label.set_font_description(requested)
                label.set_text("Практика без мыши. Ёё", -1)
                assert not label.get_unknown_glyphs_count(), "missing Cyrillic glyphs"
                weights.append(int(resolved.get_weight()))
            report = {"screen": "home", "platform": "GTK", "native_window": True,
                      "capture": "mapped Gtk.ApplicationWindow widget texture; excludes compositor shadow",
                      "pixels": [width,height], "target_pixels": [WIDTH,HEIGHT], "exact_for_compare": exact,
                      "recent_intrinsic_sizes": self.measurements[-12:], "image": name,
                      "scale_factor": self.window.get_scale_factor(), "layout": self.layout,
                      "ui_font": "Roboto", "ui_font_weights": weights,
                      "isolated_data_root": str(Path(os.environ["XDG_DATA_HOME"]))}
            (output/"home-gtk.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n")
            print(json.dumps(report,ensure_ascii=False),flush=True)
            if not exact:
                self.smoke_failure = True
                print(f"Expected settled {WIDTH}×{HEIGHT}, got {width}×{height}; "
                      "preserved actual frame for diagnosis, not for comparison.",file=sys.stderr,flush=True)
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
