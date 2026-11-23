using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace TouchTypingStudio
{
    // The same WPF line formats and draws Chrome's proportional fields, supplies selection
    // rectangles, and yields the caret position. Do not measure a differently shaped substring.
    internal sealed class ChromeTextLine : IDisposable
    {
        private readonly TextFormatter formatter;
        private readonly TextLine line;
        private readonly string text;
        private int[] elements;

        internal ChromeTextLine(string value, Typeface face, double em, Brush foreground, double pixelsPerDip)
        {
            // Browser fields are single visual lines. Keep original UTF-16 offsets even if pasted
            // text contains a line terminator; the engine's text and selection are not changed.
            text = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\u0085', ' ')
                .Replace('\u2028', ' ').Replace('\u2029', ' ');
            var properties = new ChromeRunProperties(face, em, foreground, pixelsPerDip);
            var source = new ChromeTextSource(text, properties) { PixelsPerDip = pixelsPerDip };
            formatter = TextFormatter.Create(TextFormattingMode.Ideal);
            try { line = formatter.FormatLine(source, 0, 0, new ChromeParagraphProperties(properties), null); }
            catch { formatter.Dispose(); throw; }
        }

        internal double Width { get { return line.Width; } }
        internal double WidthWithTrailingWhitespace { get { return line.WidthIncludingTrailingWhitespace; } }

        internal double Distance(int index)
        {
            if (text.Length == 0) return 0;
            int offset = Boundary(index, false);
            if (offset == text.Length)
            {
                // Use the last logical character's trailing hit. This includes trailing spaces
                // without treating the invisible end-of-paragraph marker as an editable glyph.
                int start = Boundary(offset - 1, false);
                return line.GetDistanceFromCharacterHit(new CharacterHit(start, offset - start));
            }
            return line.GetDistanceFromCharacterHit(new CharacterHit(offset, 0));
        }

        internal IEnumerable<Rect> Bounds(int start, int end)
        {
            if (end <= start) yield break;
            int first = Boundary(start, false), last = Boundary(end, true);
            if (last <= first) yield break;
            foreach (var bound in line.GetTextBounds(first, last - first))
                if (!bound.Rectangle.IsEmpty && bound.Rectangle.Width > 0) yield return bound.Rectangle;
        }

        internal void Draw(DrawingContext context, Point origin)
        {
            line.Draw(context, origin, InvertAxes.None);
        }

        internal static double VisibleShift(double caret, double width, double prior)
        {
            return Math.Max(Math.Max(0, caret - width + 3), Math.Min(prior, Math.Max(0, caret - 3)));
        }

        private int Boundary(int index, bool forward)
        {
            if (index <= 0) return 0;
            if (index >= text.Length) return text.Length;
            // Engine offsets are UTF-16. If a synthetic/existing index splits a surrogate or
            // combining sequence, draw at its text-element edge, never between raw code units.
            // Ordinary ligature letters remain legitimate separate indices for TextLine.
            if (elements == null) elements = StringInfo.ParseCombiningCharacters(text);
            int found = Array.BinarySearch(elements, index);
            if (found >= 0) return index;
            int next = ~found;
            return forward ? next < elements.Length ? elements[next] : text.Length : elements[next - 1];
        }

        public void Dispose()
        {
            line.Dispose();
            formatter.Dispose();
        }

        private sealed class ChromeTextSource : TextSource
        {
            private readonly string value;
            private readonly TextRunProperties properties;

            internal ChromeTextSource(string value, TextRunProperties properties)
            { this.value = value; this.properties = properties; }

            public override TextRun GetTextRun(int index)
            {
                return index < value.Length
                    ? (TextRun)new TextCharacters(value, index, value.Length - index, properties)
                    : new TextEndOfParagraph(1);
            }

            public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit)
            {
                int length = Math.Max(0, Math.Min(limit, value.Length));
                return new TextSpan<CultureSpecificCharacterBufferRange>(length,
                    new CultureSpecificCharacterBufferRange(properties.CultureInfo, new CharacterBufferRange(value, 0, length)));
            }

            public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index)
            { return index; }
        }

        private sealed class ChromeRunProperties : TextRunProperties
        {
            private readonly Typeface face;
            private readonly double em;
            private readonly Brush foreground;

            internal ChromeRunProperties(Typeface face, double em, Brush foreground, double pixelsPerDip)
            { this.face = face; this.em = em; this.foreground = foreground; PixelsPerDip = pixelsPerDip; }

            public override Typeface Typeface { get { return face; } }
            public override double FontRenderingEmSize { get { return em; } }
            public override double FontHintingEmSize { get { return em; } }
            public override TextDecorationCollection TextDecorations { get { return null; } }
            public override Brush ForegroundBrush { get { return foreground; } }
            public override Brush BackgroundBrush { get { return null; } }
            public override CultureInfo CultureInfo { get { return CultureInfo.CurrentUICulture; } }
            public override TextEffectCollection TextEffects { get { return null; } }
        }

        private sealed class ChromeParagraphProperties : TextParagraphProperties
        {
            private readonly TextRunProperties properties;

            internal ChromeParagraphProperties(TextRunProperties properties)
            { this.properties = properties; }

            public override FlowDirection FlowDirection { get { return FlowDirection.LeftToRight; } }
            public override TextAlignment TextAlignment { get { return TextAlignment.Left; } }
            public override double LineHeight { get { return 0; } }
            public override bool FirstLineInParagraph { get { return true; } }
            public override TextRunProperties DefaultTextRunProperties { get { return properties; } }
            public override TextWrapping TextWrapping { get { return TextWrapping.NoWrap; } }
            public override TextMarkerProperties TextMarkerProperties { get { return null; } }
            public override double Indent { get { return 0; } }
            public override double DefaultIncrementalTab { get { return properties.FontRenderingEmSize * 4; } }
        }
    }
}
