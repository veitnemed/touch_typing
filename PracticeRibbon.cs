using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed class PracticeRibbon : FrameworkElement
    {
        private string target = "";
        private string repairedPrefix;
        private int position;
        private bool error, single;
        private char practiceSymbol;
        private static readonly Brush Typed = Ui.Brush("#8D919E");
        private static readonly Brush Upcoming = Ui.Brush("#ECEAF0");

        public PracticeRibbon() { ClipToBounds = true; }

        internal static string VisibleText(string text)
        {
            return text.Replace('\n', '↵').Replace('\t', '⇥');
        }

        public void Update(string text, int at, bool failed, bool singleSymbol = false, string repaired = null, char practice = '\0')
        {
            target = text;
            repairedPrefix = repaired;
            practiceSymbol = practice;
            position = at;
            error = failed;
            single = singleSymbol;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext draw)
        {
            double width = ActualWidth, height = ActualHeight;
            if (width < 2 || height < 2) return;
            double middle = Math.Floor(width / 2);
            draw.DrawRectangle(Ui.Brush("#181A1E"), null, new Rect(0, 0, middle, height));
            draw.DrawRectangle(Ui.Brush("#232731"), null, new Rect(middle, 0, width - middle, height));
            double size = Math.Min(44, height * .68);
            if (single && position < target.Length)
            {
                var symbol = Format(VisibleText(target[position].ToString()), size, error ? Ui.Brush("#FFA6AE") : Upcoming);
                double x = (width - symbol.WidthIncludingTrailingWhitespace) / 2;
                draw.DrawRectangle(error ? Ui.Brush("#642F39") : Ui.Brush("#353E4D"), null, new Rect(x - 14, 0, symbol.Width + 28, height));
                draw.DrawText(symbol, new Point(x, (height - symbol.Height) / 2));
                return;
            }
            double cell = Format("0", size, Typed).WidthIncludingTrailingWhitespace;
            int columns = Math.Max(1, (int)Math.Ceiling(middle / cell));
            int prefixLength = repairedPrefix == null ? position : repairedPrefix.Length;
            int past = Math.Min(prefixLength, columns);
            if (past > 0)
            {
                string prefix = (repairedPrefix ?? target).Substring(prefixLength - past, past);
                var text = Format(VisibleText(prefix), size, Typed);
                draw.PushClip(new RectangleGeometry(new Rect(0, 0, middle, height)));
                draw.DrawText(text, new Point(middle - text.WidthIncludingTrailingWhitespace, (height - text.Height) / 2));
                draw.Pop();
            }
            if (position < target.Length)
            {
                draw.PushClip(new RectangleGeometry(new Rect(middle, 0, width - middle, height)));
                char symbol = target[position];
                var current = Format(VisibleText(symbol.ToString()), size, Upcoming);
                string tail = target.Substring(position + 1, Math.Min(columns, target.Length - position - 1));
                var next = Format(VisibleText(tail), size, Upcoming);
                if (practiceSymbol != '\0')
                    for (int index = 0; index < tail.Length; index++)
                        if (tail[index] == practiceSymbol) next.SetForegroundBrush(Ui.Brush("#FFD28E"), index, 1);
                double top = (height - current.Height) / 2;
                var accent = error ? Ui.Brush("#EB868C") : Ui.Brush("#B7CED6");
                double glyphWidth = current.WidthIncludingTrailingWhitespace;
                if (error) draw.DrawRectangle(Ui.Brush("#642F39"), null, new Rect(middle, 0, glyphWidth, height));
                draw.DrawRectangle(accent, null, new Rect(middle + 2, height - 4, Math.Max(3, glyphWidth - 4), 2));
                draw.DrawText(current, new Point(middle, top));
                draw.DrawText(next, new Point(middle + glyphWidth, top));
                draw.Pop();
            }
            else draw.DrawText(Format("✓", size * .77, Ui.Brush("#9FD5AC")), new Point(middle + 8, height * .15));
        }

        private FormattedText Format(string text, double size, Brush ink)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Ui.Mono, size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}
