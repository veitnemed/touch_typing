"""Exercise a keyboard-only theme change and measure focus on the two real header tabs."""
import gi
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("Gtk", "4.0")
from gi.repository import GdkPixbuf, Gtk
from test_preview2_native import QA, out


def luminance(rgb):
    channels = [value / 255 for value in rgb]
    linear = [(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4) for v in channels]
    return sum(v * k for v, k in zip(linear, (.2126, .7152, .0722)))


def contrast(a, b):
    high, low = sorted((luminance(a), luminance(b)), reverse=True)
    return (high + .05) / (low + .05)


def exercise_header_focus(self):
    self.navigate('home')
    self.focus_window()
    for _ in range(20):
        yield
    selected = self.navigation['home']
    other = self.navigation['typing']
    # Reach the theme control by keyboard and toggle it with Space.
    selected.grab_focus()
    yield from self.chord('Tab')
    assert other.has_focus()
    yield from self.chord('Tab')
    theme = self.window.get_focus()
    assert theme.has_css_class('reference-icon-control')
    yield from self.chord('Space')
    assert self.window.has_css_class('reference-light')
    frames = {}
    for button, name in ((other, 'unselected'), (selected, 'selected')):
        yield from self.chord('Shift+Tab')
        assert button.has_focus() and button.has_visible_focus(), name
        assert not (button.get_state_flags() & Gtk.StateFlags.PRELIGHT), name
        for _ in range(25):
            yield
        path = out / ('home-light-focus-' + name + '.png')
        self.capture_widget_png(path)
        img = GdkPixbuf.Pixbuf.new_from_file(str(path))
        frames[name] = img.get_pixels(), img.get_rowstride(), img.get_n_channels()
    # Compare each tab against the same pixels when the other tab has
    # focus. This excludes the dark fill/border of the selected tab.
    def rgb(frame, x, y):
        raw, stride, channels = frames[frame]
        start = y * stride + x * channels
        return tuple(raw[start:start + 3])

    contrasts = []
    for button, name, off in ((other, 'unselected', 'selected'), (selected, 'selected', 'unselected')):
        mapped, rect = button.compute_bounds(self.window)
        assert mapped
        x = round(rect.get_x() + rect.get_width() / 2)
        y = round(rect.get_y())
        ratios = [contrast(rgb(name, x, y + i), rgb(off, x, y + i)) for i in range(12)]
        # At least two consecutive, legible pixels of focus outline.
        contrasts.append((name, max(min(a, b) for a, b in zip(ratios, ratios[1:]))))
    assert all(ratio >= 3 for _, ratio in contrasts), contrasts
    print('PASS GTK tab keyboard focus:', contrasts, flush=True)
    # Restore the initial theme before the caller continues its course run.
    yield from self.chord('Tab')
    yield from self.chord('Tab')
    assert self.window.get_focus() is theme
    yield from self.chord('Space')
    assert not self.window.has_css_class('reference-light')


class FocusTest(QA):
    exercise = exercise_header_focus


if __name__ == '__main__':
    app = FocusTest()
    result = app.run([])
    raise SystemExit(1 if app.smoke_failure else result)
