using System;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// An editable text layer's content. The layer's pixels are always just this
    /// drawn by <see cref="Render"/>, so the text can be re-opened and changed at any
    /// time. Immutable - an edit swaps in a new instance, which keeps undo simple.
    /// </summary>
    public sealed record TextContent(
        string Text,
        string FontFamily,
        float FontSize,
        bool Bold,
        bool Italic,
        SKColor Color,
        SKPoint Origin,
        bool Vertical)
    {
        /// <summary>Lines (Horizontal) or columns (Vertical), split on newlines.</summary>
        private string[] Lines => Text.Replace("\r\n", "\n").Split('\n');

        public SKTypeface CreateTypeface() => SKTypeface.FromFamilyName(FontFamily,
            Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        /// <summary>Draws the text with its top-left corner at <see cref="Origin"/>.
        /// Vertical text stacks characters top to bottom, one column per line.</summary>
        public void Render(SKCanvas canvas)
        {
            using var typeface = CreateTypeface();
            using var font = new SKFont(typeface, FontSize, 1f, 0f);
            using var paint = new SKPaint { Color = Color, IsAntialias = true };

            // Ascent is negative (above the baseline), so this puts the text's top at Origin.Y.
            var firstBaseline = Origin.Y - font.Metrics.Ascent;
            var lines = Lines;

            if (!Vertical)
            {
                for (var i = 0; i < lines.Length; i++)
                    canvas.DrawText(lines[i], Origin.X, firstBaseline + i * font.Spacing, font, paint);
                return;
            }

            for (var column = 0; column < lines.Length; column++)
            {
                var x = Origin.X + column * font.Spacing;
                var y = firstBaseline;
                foreach (var character in lines[column])
                {
                    canvas.DrawText(character.ToString(), x, y, font, paint);
                    y += font.Spacing;
                }
            }
        }

        /// <summary>The area the text covers (document pixels) - what a click with a
        /// Text tool must hit to re-open this text.</summary>
        public SKRect Bounds
        {
            get
            {
                using var typeface = CreateTypeface();
                using var font = new SKFont(typeface, FontSize, 1f, 0f);
                var lines = Lines;
                var lineHeight = font.Spacing;

                if (Vertical)
                {
                    var longest = 0;
                    foreach (var line in lines) longest = Math.Max(longest, line.Length);
                    return new SKRect(Origin.X, Origin.Y, Origin.X + lines.Length * lineHeight, Origin.Y + Math.Max(1, longest) * lineHeight);
                }

                var width = 0f;
                foreach (var line in lines) width = Math.Max(width, font.MeasureText(line));
                return new SKRect(Origin.X, Origin.Y, Origin.X + Math.Max(width, FontSize / 2), Origin.Y + lines.Length * lineHeight);
            }
        }

        public TextContent MovedBy(float dx, float dy) => this with { Origin = new SKPoint(Origin.X + dx, Origin.Y + dy) };
    }
}
