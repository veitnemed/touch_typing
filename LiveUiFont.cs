using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal static class LiveUiFont
    {
        private static readonly string FontDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
        private static readonly string[] Files = { "Roboto-Regular.ttf", "Roboto-Medium.ttf", "Roboto-Bold.ttf" };
        private static readonly FontWeight[] Weights = { FontWeights.Normal, FontWeights.Medium, FontWeights.Bold };
        private static readonly FontFamily SystemFallback = new FontFamily("Segoe UI");
        private static readonly FontFamily[] LocalFamilies = LoadFamilies();
        internal static readonly FontFamily Sans = LocalFamilies[0] ?? SystemFallback;

        private static FontFamily[] LoadFamilies()
        {
            var resolved = new FontFamily[Files.Length];
            if (!Directory.Exists(FontDirectory)) return resolved;
            // WPF resolves these application-local file URIs; fonts aren't installed system-wide.
            var directory = new Uri(Path.GetFullPath(FontDirectory) + Path.DirectorySeparatorChar, UriKind.Absolute);
            var common = new FontFamily(directory, "./#Roboto");
            for (int index = 0; index < Files.Length; index++)
            {
                if (!File.Exists(Path.Combine(FontDirectory, Files[index]))) continue;
                if (Matches(common, index)) resolved[index] = common;
                else if (index == 1)
                {
                    // Upstream's static Medium has a legacy family name (name ID 1) of
                    // "Roboto Medium"; some WPF font resolvers use it instead of the typo family.
                    var legacyMedium = new FontFamily(directory, "./#Roboto Medium");
                    if (Matches(legacyMedium, index)) resolved[index] = legacyMedium;
                }
            }
            return resolved;
        }

        private static bool Matches(FontFamily candidate, int index)
        {
            try
            {
                GlyphTypeface glyph;
                return new Typeface(candidate, FontStyles.Normal, Weights[index], FontStretches.Normal).TryGetGlyphTypeface(out glyph) &&
                    glyph.FontUri.IsFile && string.Equals(Path.GetFullPath(Path.Combine(FontDirectory, Files[index])),
                        Path.GetFullPath(glyph.FontUri.LocalPath), StringComparison.OrdinalIgnoreCase) &&
                    glyph.Weight == Weights[index] && glyph.StyleSimulations == StyleSimulations.None;
            }
            catch (IOException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (ArgumentException) { return false; }
        }

        internal static FontFamily ForWeight(FontWeight weight)
        {
            int index = weight >= FontWeights.Bold ? 2 : weight >= FontWeights.Medium ? 1 : 0;
            return LocalFamilies[index] ?? SystemFallback;
        }

        internal static Typeface Typeface(FontWeight weight)
        {
            return new Typeface(ForWeight(weight), FontStyles.Normal, weight, FontStretches.Normal);
        }

        internal static int Check(string output)
        {
            var evidence = new StringBuilder();
            try
            {
                if (Array.Exists(LocalFamilies, family => family == null))
                    throw new InvalidOperationException("A bundled Roboto face is missing or has not resolved to its native file.");
                const string required = "ЁёЙйЪъЫыЭэПрактика без мышиChromeVSCode0123456789";
                for (int index = 0; index < Weights.Length; index++)
                {
                    GlyphTypeface resolved;
                    if (!Typeface(Weights[index]).TryGetGlyphTypeface(out resolved))
                        throw new InvalidOperationException("WPF did not resolve the requested font role.");
                    string expected = Path.GetFullPath(Path.Combine(FontDirectory, Files[index]));
                    if (!resolved.FontUri.IsFile ||
                        !string.Equals(expected, Path.GetFullPath(resolved.FontUri.LocalPath), StringComparison.OrdinalIgnoreCase) ||
                        resolved.Weight != Weights[index] || resolved.StyleSimulations != StyleSimulations.None)
                        throw new InvalidOperationException("WPF selected a fallback/synthetic face for " + Files[index] + ": " +
                            resolved.FontUri + "; " + resolved.Weight + "; " + resolved.StyleSimulations);
                    foreach (char c in required)
                        if (!resolved.CharacterToGlyphMap.ContainsKey(c))
                            throw new InvalidOperationException("Missing physical glyph U+" +
                                ((int)c).ToString("X4", CultureInfo.InvariantCulture) + " in " + Files[index]);
                    evidence.Append(Files[index]).Append(' ').Append(Weights[index].ToOpenTypeWeight()).AppendLine(" native Cyrillic/Latin");
                }
                File.WriteAllText(output, evidence.ToString());
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(output, evidence + error.ToString());
                return 1;
            }
        }
    }
}
