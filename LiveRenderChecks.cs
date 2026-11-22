using System;
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

        private static byte[] Draw(string directory, string app, string name, Data state)
        {
            var view = new LabSceneView();
            view.Update(state.D("scene"), app);
            var size = new Size(960, 470);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 470, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(file);
            var bytes = new byte[960 * 470 * 4];
            bitmap.CopyPixels(bytes, 960 * 4, 0);
            return bytes;
        }

        private static int Highlight(byte[] pixels)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i] == 155 && pixels[i + 1] == 98 && pixels[i + 2] == 53) count++;
            return count;
        }

        internal static int Run(string executable, string directory)
        {
            Directory.CreateDirectory(directory);
            try
            {
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
                File.WriteAllText(Path.Combine(directory, "result.txt"), "PASS WPF selection, caret, Chrome Ctrl+F, Delete and Ctrl+J");
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
