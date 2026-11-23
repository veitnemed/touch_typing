using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchTypingStudio
{
    // Opt-in WPF renderer/engine diagnostic, not a native-window or physical input claim.
    internal static class ChromeShapingProbe
    {
        private const string Sample = "AV Ё ";
        private const string Deleted = "A Ё ";
        private static readonly CultureInfo Format = CultureInfo.InvariantCulture;

        internal static int Run(string executable, string directory)
        {
            Directory.CreateDirectory(directory);
            using (var report = File.CreateText(Path.Combine(directory, "chrome-shaping.txt")))
            {
                try
                {
                    MeasureFacesAndBoundaries(report);
                    using (var engine = new PracticeEngineClient(executable, true))
                    {
                        engine.Call("configure", "platform", "windows", "layout", "EN");
                        engine.Call("lab-start", "app", "chrome", "mode", "sandbox", "level", 0, "seconds", 0, "seed", 9);
                        engine.Call("lab-resume");
                        InspectField(engine, report, directory, "url", "L");
                        InspectField(engine, report, directory, "find", "F");
                        engine.Call("lab-stop");
                    }
                    report.WriteLine("PASS exact mixed text, trailing-space selection, movement and Delete on both fields; see measured boundary deltas above.");
                    return 0;
                }
                catch (Exception error)
                {
                    report.WriteLine(error);
                    return 1;
                }
            }
        }

        private static void MeasureFacesAndBoundaries(TextWriter report)
        {
            Typeface face = LiveUiFont.Typeface(FontWeights.Normal);
            GlyphTypeface physical;
            if (!face.TryGetGlyphTypeface(out physical) || !physical.FontUri.IsFile ||
                !string.Equals("Roboto-Regular.ttf", Path.GetFileName(physical.FontUri.LocalPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected the exact bundled proportional Roboto face in this probe.");
            report.WriteLine("proportional-face=" + physical.FontUri);
            report.WriteLine("WPF FormattedText diagnostic; explicit pixelsPerDip, not live monitor DPI.");
            foreach (double scale in new[] { 1d, 1.25d, 1.5d })
            {
                foreach (string value in new[] { Sample, "To Ё ", "office Ё " })
                {
                    const double size = 12;
                    var line = Line(value, face, size, scale);
                    report.WriteLine(string.Format(Format, "scale={0:0.00} text=[{1}] full={2:0.000} with-trailing={3:0.000}",
                        scale, value, line.Width, line.WidthIncludingTrailingWhitespace));
                    if (line.WidthIncludingTrailingWhitespace <= line.Width)
                        throw new InvalidOperationException("Trailing space was omitted from line extent.");
                    for (int caret = 1; caret < value.Length; caret++)
                    {
                        // This models current Field()'s isolated prefix vs one position derived from the full shaped line.
                        double prior = Line(value.Substring(0, caret), face, size, scale).WidthIncludingTrailingWhitespace;
                        var followingGlyph = line.BuildHighlightGeometry(new Point(), caret, 1);
                        var preceding = line.BuildHighlightGeometry(new Point(), 0, caret);
                        report.WriteLine(string.Format(Format, "  caret={0} current-prefix={1:0.000} next-full-left={2} prior-full-right={3}",
                            caret, prior, Number(followingGlyph, true), Number(preceding, false)));
                    }
                }
            }
        }

        private static string Number(Geometry value, bool left)
        {
            if (value == null || value.Bounds.IsEmpty) return "empty";
            return (left ? value.Bounds.Left : value.Bounds.Right).ToString("0.000", Format);
        }

        private static FormattedText Line(string value, Typeface face, double size, double scale)
        {
            return new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.Black, scale);
        }

        private static void InspectField(PracticeEngineClient engine, TextWriter report, string directory, string field, string focusKey)
        {
            Chord(engine, "RCtrl", focusKey);
            Chord(engine, "RCtrl", "A");
            Stroke(engine, "A", "A");
            Stroke(engine, "V", "V");
            Stroke(engine, "Space", " ");
            Stroke(engine, "Backtick", "Ё");
            Data state = Stroke(engine, "Space", " ");
            AssertState(state, field, Sample, 5, 5);
            Capture(directory, field + "-end", state);
            state = Chord(engine, "LShift", "Left");
            AssertState(state, field, Sample, 4, 5);
            Capture(directory, field + "-trailing-selected", state);
            Stroke(engine, "Home");
            state = Stroke(engine, "Right");
            AssertState(state, field, Sample, 1, 1);
            Capture(directory, field + "-between-AV", state);
            state = Stroke(engine, "Delete");
            AssertState(state, field, Deleted, 1, 1);
            Capture(directory, field + "-deleted-V", state);
            report.WriteLine(field + ": preserved Cyrillic/trailing space; Delete removed V and kept index 1.");
            Stroke(engine, "Esc");
        }

        private static Data Stroke(PracticeEngineClient engine, string key, string text = "")
        {
            engine.Call("lab-key", "key", key, "pressed", true, "text", text);
            return engine.Call("lab-key", "key", key, "pressed", false, "text", "");
        }

        private static Data Chord(PracticeEngineClient engine, string modifier, string key)
        {
            engine.Call("lab-key", "key", modifier, "pressed", true, "text", "");
            Stroke(engine, key);
            return engine.Call("lab-key", "key", modifier, "pressed", false, "text", "");
        }

        private static void AssertState(Data state, string focus, string input, int cursor, int anchor)
        {
            var active = state.D("scene").Items("windows").FirstOrDefault(window => window.B("active") && window.S("app") == "chrome");
            if (active == null || active.S("focus") != focus || active.S("input") != input ||
                active.I("cursor", -1) != cursor || active.I("anchor", -1) != anchor)
                throw new InvalidOperationException("Unexpected real engine field state at " + focus + ": " +
                    (active == null ? "inactive" : active.S("focus") + " [" + active.S("input") + "] " +
                        active.I("cursor", -1) + "/" + active.I("anchor", -1)));
        }

        private static void Capture(string directory, string label, Data state)
        {
            var view = new LabSceneView();
            view.Update(state.D("scene"), "chrome");
            var size = new Size(580, 229);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(580, 229, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, label + ".png"))) png.Save(output);
        }
    }
}
