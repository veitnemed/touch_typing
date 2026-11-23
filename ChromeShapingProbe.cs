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
                    report.WriteLine("PASS shared shaped caret/selection/scroll, Unicode cluster edges, real engine mixed text and Delete in both Chrome fields.");
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
            report.WriteLine("WPF shaped-line diagnostic; explicit pixelsPerDip, not live monitor DPI.");
            foreach (double scale in new[] { 1d, 1.25d, 1.5d })
            {
                foreach (string value in new[] { Sample, "To Ё ", "office Ё ", "A\u030AV Ё ", "\uD83D\uDE42AV Ё " })
                {
                    const double size = 12;
                    using (var line = new ChromeTextLine(value, face, size, Brushes.Black, scale))
                    {
                        double end = line.Distance(value.Length);
                        report.WriteLine(string.Format(Format, "scale={0:0.00} text=[{1}] full={2:0.000} with-trailing={3:0.000}",
                            scale, value, line.Width, line.WidthWithTrailingWhitespace));
                        if (end <= line.Width || Math.Abs(end - line.WidthWithTrailingWhitespace) > .1)
                            throw new InvalidOperationException("Trailing space is not included in the final caret hit.");
                        var space = line.Bounds(value.Length - 1, value.Length).ToArray();
                        if (space.Length == 0 || Math.Abs(space.Min(rect => rect.Left) - line.Distance(value.Length - 1)) > .1 ||
                            Math.Abs(space.Max(rect => rect.Right) - end) > .1)
                            throw new InvalidOperationException("Trailing-space selection differs from the two caret hits.");

                        const double viewport = 16;
                        double shift = ChromeTextLine.VisibleShift(end, viewport, 0);
                        if (end - shift > viewport - 2.9 || end - shift < 0 ||
                            ChromeTextLine.VisibleShift(line.Distance(0), viewport, shift) != 0)
                            throw new InvalidOperationException("The same shaped caret is not exposed by horizontal scrolling.");

                        int[] boundaries = StringInfo.ParseCombiningCharacters(value);
                        if (boundaries[1] > 1 && (Math.Abs(line.Distance(1) - line.Distance(0)) > .1 ||
                            !line.Bounds(0, boundaries[1]).SequenceEqual(line.Bounds(1, boundaries[1]))))
                            throw new InvalidOperationException("Combining/surrogate interiors must use visible cluster edges.");

                        double previous = line.Distance(0);
                        for (int caret = 1; caret < value.Length; caret++)
                        {
                            double current = line.Distance(caret);
                            if (double.IsNaN(current) || current < previous - .01 || current > end + .01)
                                throw new InvalidOperationException("Invalid left-to-right shaped caret sequence.");
                            double isolated = Line(value.Substring(0, caret), face, size, scale).WidthIncludingTrailingWhitespace;
                            report.WriteLine(string.Format(Format, "  caret={0} old-isolated={1:0.000} shaped={2:0.000}",
                                caret, isolated, current));
                            previous = current;
                        }

                        if (value == Sample)
                        {
                            // AV is a kerning pair but not a ligature. The left selection edge
                            // for V must be the caret between A and V on this actual TextLine.
                            var v = line.Bounds(1, 2).ToArray();
                            if (v.Length == 0 || Math.Abs(v.Min(rect => rect.Left) - line.Distance(1)) > .1 ||
                                line.Distance(1) >= Line("A", face, size, scale).WidthIncludingTrailingWhitespace - .15)
                                throw new InvalidOperationException("The AV boundary did not preserve full-line kerning.");
                        }
                    }
                }
            }
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
