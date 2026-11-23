using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchTypingStudio
{
    internal static class LiveRenderChecks
    {
        private static Data Chord(PracticeEngineClient engine, params string[] keys)
        {
            Data state = Data.Empty;
            foreach (string key in keys) engine.Call("lab-key", "key", key, "pressed", true, "text", key.Length == 1 ? key.ToLowerInvariant() : "");
            foreach (string key in keys.Reverse()) state = engine.Call("lab-key", "key", key, "pressed", false, "text", "");
            return state;
        }

        private static byte[] Draw(string directory, string app, string name, Data state, int dpi = 96)
        {
            var view = new LabSceneView();
            view.Update(state.D("scene"), app);
            var size = new Size(960, 470);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();
            int width = (int)Math.Ceiling(size.Width * dpi / 96), height = (int)Math.Ceiling(size.Height * dpi / 96);
            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(file);
            var bytes = new byte[width * height * 4];
            bitmap.CopyPixels(bytes, width * 4, 0);
            return bytes;
        }

        private static int Highlight(byte[] pixels)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if ((pixels[i] == 155 && pixels[i + 1] == 98 && pixels[i + 2] == 53) ||
                    (pixels[i] == 240 && pixels[i + 1] == 200 && pixels[i + 2] == 173) ||
                    CodeSelectionPixel(pixels, i)) count++;
            return count;
        }

        private static byte[] DrawEditorSelection(string directory, string label, int anchor, int caret,
            string body = "яe\u0301🙂\nб")
        {
            var editor = new Dictionary<string, object>
            {
                { "app", "code" }, { "active", true }, { "focus", "body" }, { "sidebar", false },
                { "body", body }, { "bodyAnchor", anchor }, { "bodyCursor", caret }
            };
            return Draw(directory, "code", label, new Data(new Dictionary<string, object>
            {
                { "scene", new Dictionary<string, object> { { "windows", new object[] { editor } } } }
            }));
        }

        // Primer inherits the VS Code dark selection color (no alpha when focused).
        private static bool CodeSelectionPixel(byte[] pixels, int index)
        {
            return NearPixel(pixels, index, 38, 79, 120);
        }

        private static bool NearPixel(byte[] pixels, int index, int r, int g, int b)
        {
            return Math.Abs(pixels[index + 2] - r) <= 2 &&
                Math.Abs(pixels[index + 1] - g) <= 2 && Math.Abs(pixels[index] - b) <= 2;
        }

        private static int[] EditorBlueColumns(byte[] pixels, int row)
        {
            // A code scene at 960 DIP is rendered at native editor scale.
            // Activity rail 48 + line-number gutter 49; rows start at 92, pitch 20.
            int top = 92 + row * 20;
            return Enumerable.Range(96, 110).Where(x => Enumerable.Range(top, 19)
                .Any(y => CodeSelectionPixel(pixels, (y * 960 + x) * 4))).ToArray();
        }

        private static void CheckEditorNewlineSelection(string directory)
        {
            var plain = DrawEditorSelection(directory, "code-unicode-caret", 5, 5);
            var backward = DrawEditorSelection(directory, "code-newline-backward", 6, 5);
            var forward = DrawEditorSelection(directory, "code-newline-forward", 5, 6);
            var spanning = DrawEditorSelection(directory, "code-graphemes-and-newline", 1, 6);
            var twoRows = DrawEditorSelection(directory, "code-emoji-newline-and-cyrillic", 3, 7);
            var crlf = DrawEditorSelection(directory, "code-crlf", 5, 7, "яe\u0301🙂\r\nб");
            var eof = DrawEditorSelection(directory, "code-no-marker-at-eof", 6, 7);
            int[] glyphless = EditorBlueColumns(forward, 0);
            int[] withCaret = EditorBlueColumns(backward, 0);
            if (EditorBlueColumns(plain, 0).Length != 0 || glyphless.Length < 3 ||
                // The backward caret can cover the marker's first pixel.
                withCaret.Length < 2 || withCaret.Last() != glyphless.Last() ||
                Math.Abs(withCaret.First() - glyphless.First()) > 2 ||
                !glyphless.SequenceEqual(EditorBlueColumns(crlf, 0)) ||
                EditorBlueColumns(backward, 1).Length != 0 || EditorBlueColumns(forward, 1).Length != 0)
                throw new InvalidOperationException("Selected LF/CRLF must mark only the end of its own editor row, in both directions");
            var firstSpan = EditorBlueColumns(spanning, 0);
            var firstTwoRows = EditorBlueColumns(twoRows, 0);
            if (firstSpan.Length <= glyphless.Length || firstTwoRows.Length <= glyphless.Length ||
                firstSpan.First() >= firstTwoRows.First() || firstTwoRows.First() >= glyphless.First() ||
                !glyphless.All(x => firstSpan.Contains(x) && firstTwoRows.Contains(x)) ||
                EditorBlueColumns(spanning, 1).Length != 0 || EditorBlueColumns(twoRows, 1).Length < 2 ||
                !EditorBlueColumns(twoRows, 1).SequenceEqual(EditorBlueColumns(eof, 1)) ||
                EditorBlueColumns(eof, 0).Length != 0)
                throw new InvalidOperationException("Rendered selection must include exactly the intended graphemes, line break and next-row glyph");
        }

        private static void CheckEditorDpiRenders(string directory)
        {
            // This is a WPF render-target regression, not a desktop-scale
            // session. The native -CodeTheme test separately asserts 96 DPI.
            const string body = "def function():\n    return 1234";
            var editor = new Dictionary<string, object>
            {
                { "app", "code" }, { "active", true }, { "focus", "body" }, { "sidebar", false },
                { "projectName", "task-list" }, { "projectRelative", "app.py" },
                { "body", body }, { "bodyAnchor", 4 }, { "bodyCursor", 12 },
                { "syntax", new object[] {
                    new Dictionary<string, object> { {"start", 0}, {"end", 3}, {"kind", "keyword"} },
                    new Dictionary<string, object> { {"start", 4}, {"end", 12}, {"kind", "function"} },
                    new Dictionary<string, object> { {"start", 27}, {"end", 31}, {"kind", "number"} }
                } }
            };
            var state = new Data(new Dictionary<string, object>
            {
                { "scene", new Dictionary<string, object> { { "windows", new object[] { editor } } } }
            });
            foreach (int dpi in new[] { 96, 120, 144 })
            {
                var pixels = Draw(directory, "code", "code-themed-render-" + dpi + "dpi", state, dpi);
                int width = 960 * dpi / 96;
                int x0 = 96 * dpi / 96, x1 = 212 * dpi / 96, y0 = 92 * dpi / 96, y1 = 112 * dpi / 96;
                int keywords = 0, functions = 0, selection = 0, caret = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int index = (y * width + x) * 4;
                        if (NearPixel(pixels, index, 255, 123, 114)) keywords++;
                        if (NearPixel(pixels, index, 210, 168, 255)) functions++;
                        if (CodeSelectionPixel(pixels, index)) selection++;
                        if (NearPixel(pixels, index, 47, 129, 247)) caret++;
                    }
                if (keywords < 1 || functions < 1 || selection < 45 || caret < 2)
                    throw new InvalidOperationException(string.Format("WPF themed code pixels at {0} DPI: keyword {1}, function {2}, selection {3}, caret {4}",
                        dpi, keywords, functions, selection, caret));
            }
        }

        internal static int Run(string executable, string directory)
        {
            Directory.CreateDirectory(directory);
            try
            {
                CheckEditorNewlineSelection(directory);
                CheckEditorDpiRenders(directory);
                using (var engine = new PracticeEngineClient(executable, true))
                foreach (string app in new[] { "chrome", "code", "terminal" })
                {
                    engine.Call("lab-start", "app", app, "mode", "sandbox", "level", 0, "seconds", 0, "seed", 9);
                    engine.Call("lab-resume");
                    if (app == "terminal")
                    {
                        foreach (string key in new[] { "A", "B", "C", "D", "Left", "Left" }) Chord(engine, key);
                        var before = Draw(directory, app, "terminal-caret", engine.Call("lab-state"));
                        var removed = Chord(engine, "Delete");
                        var after = Draw(directory, app, "terminal-delete", removed);
                        if (before.SequenceEqual(after) || removed.D("scene").S("terminalInput") != "abd")
                            throw new InvalidOperationException("Delete at ab|cd");
                    }
                    else
                    {
                        var selected = Draw(directory, app, app + "-selection", Chord(engine, "RCtrl", "L"));
                        var plain = Draw(directory, app, app + "-caret", Chord(engine, "Left"));
                        if (Highlight(selected) < Highlight(plain) + 30) throw new InvalidOperationException("Visible selection in " + app);
                        if (app == "chrome")
                        {
                            Chord(engine, "RCtrl", "F");
                            foreach (string key in new[] { "A", "B", "C", "D" }) Chord(engine, key);
                            var findSelected = Draw(directory, app, "chrome-find-selection", Chord(engine, "RCtrl", "A"));
                            var findCaret = Draw(directory, app, "chrome-find-caret", Chord(engine, "Left"));
                            if (Highlight(findSelected) < Highlight(findCaret) + 30)
                                throw new InvalidOperationException("Visible Chrome find selection");
                        }
                        if (app == "code")
                        {
                            var opened = Chord(engine, "RCtrl", "J");
                            if (!opened.D("scene").Items("windows").First().B("panel") ||
                                plain.SequenceEqual(Draw(directory, app, "code-panel", opened)))
                                throw new InvalidOperationException("VS Code bottom panel");
                        }
                    }
                    engine.Call("lab-stop");
                }
                File.WriteAllText(Path.Combine(directory, "result.txt"), "PASS WPF selection, caret, Chrome Ctrl+F, editor LF/CRLF geometry and theme at 96/120/144 render DPI, Delete and Ctrl+J");
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(directory, "result.txt"), error.ToString());
                return 1;
            }
        }
    }
}
