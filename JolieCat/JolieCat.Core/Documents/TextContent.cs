using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>Warp Text styles (Photoshop's).</summary>
    public enum TextWarpStyle
    {
        Arc,
        Arch,
        Bulge,
        Wave,
        Flag,
        Rise,
        Fish,
    }

    public enum TextAlignment
    {
        Left,
        Center,
        Right,
    }

    /// <summary>
    /// An editable text layer's content. The layer's pixels are always just this
    /// drawn by <see cref="Render"/>, so the text can be re-opened and changed at any
    /// time. Immutable - an edit swaps in a new instance, which keeps undo simple.
    /// </summary>
    /// <param name="Leading">Line spacing, % of the font's normal line height.</param>
    /// <param name="Tracking">Extra space between characters, 1/1000 em (Photoshop's units).</param>
    /// <param name="Warp">Warp Text bend: -100..100 (0 = no warp).</param>
    /// <param name="WarpStyle">How the text bends.</param>
    /// <param name="PathData">Type on a path: the curve (SVG path data) the letters follow,
    /// or null for ordinary text. Alignment places the text along it.</param>
    /// <param name="BoxWidth">Paragraph text: words wrap to lines this wide (pixels), and
    /// alignment is within it. 0: point text (lines only break at Enter).</param>
    public sealed record TextContent(
        string Text,
        string FontFamily,
        float FontSize,
        bool Bold,
        bool Italic,
        SKColor Color,
        SKPoint Origin,
        bool Vertical,
        TextAlignment Alignment = TextAlignment.Left,
        float Leading = 100,
        float Tracking = 0,
        bool Underline = false,
        bool Strikethrough = false,
        float Warp = 0,
        TextWarpStyle WarpStyle = TextWarpStyle.Arc,
        string? PathData = null,
        float BoxWidth = 0)
    {
        /// <summary>Lines (Horizontal) or columns (Vertical), split on newlines.</summary>
        private string[] Lines => Text.Replace("\r\n", "\n").Split('\n');

        public SKTypeface CreateTypeface() => SKTypeface.FromFamilyName(FontFamily,
            Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        private float ExtraPerCharacter => Tracking / 1000f * FontSize;

        private float LineHeight(SKFont font) => font.Spacing * Math.Max(0.1f, Leading / 100f);

        private float LineWidth(SKFont font, string line) =>
            line.Length == 0 ? 0 : font.MeasureText(line) + ExtraPerCharacter * (line.Length - 1);

        /// <summary>The horizontal lines as drawn: split at Enter, and (paragraph text) each
        /// wrapped at spaces to fit <see cref="BoxWidth"/>; a word too long for the box
        /// is broken where it doesn't fit.</summary>
        private List<string> LinesFor(SKFont font)
        {
            var result = new List<string>();
            foreach (var paragraph in Lines)
            {
                if (BoxWidth <= 0 || Vertical || LineWidth(font, paragraph) <= BoxWidth)
                {
                    result.Add(paragraph);
                    continue;
                }
                var line = string.Empty;
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (LineWidth(font, candidate) <= BoxWidth) { line = candidate; continue; }
                    if (line.Length > 0) result.Add(line);
                    // A word longer than the box: break it into pieces that fit.
                    var rest = word;
                    while (rest.Length > 1 && LineWidth(font, rest) > BoxWidth)
                    {
                        var take = rest.Length - 1;
                        while (take > 1 && LineWidth(font, rest[..take]) > BoxWidth) take--;
                        result.Add(rest[..take]);
                        rest = rest[take..];
                    }
                    line = rest;
                }
                result.Add(line);
            }
            return result;
        }

        /// <summary>Each horizontal line's left x and baseline y, honouring alignment and leading.</summary>
        private List<(string Line, float X, float Baseline, float Width)> Layout(SKFont font)
        {
            var lines = LinesFor(font);
            var widths = new float[lines.Count];
            var block = BoxWidth > 0 ? BoxWidth : 0f;     // paragraph text aligns within its box
            for (var i = 0; i < lines.Count; i++)
            {
                widths[i] = LineWidth(font, lines[i]);
                if (BoxWidth <= 0) block = Math.Max(block, widths[i]);
            }

            var result = new List<(string, float, float, float)>();
            var firstBaseline = Origin.Y - font.Metrics.Ascent; // ascent is negative
            for (var i = 0; i < lines.Count; i++)
            {
                var offset = Alignment switch
                {
                    TextAlignment.Center => (block - widths[i]) / 2,
                    TextAlignment.Right => block - widths[i],
                    _ => 0,
                };
                result.Add((lines[i], Origin.X + offset, firstBaseline + i * LineHeight(font), widths[i]));
            }
            return result;
        }

        /// <summary>Draws the text with its top-left corner at <see cref="Origin"/>.
        /// Vertical text stacks characters top to bottom, one column per line.</summary>
        public void Render(SKCanvas canvas)
        {
            using var typeface = CreateTypeface();
            using var font = new SKFont(typeface, FontSize, 1f, 0f);
            using var paint = new SKPaint { Color = Color, IsAntialias = true };

            if (PathData is not null)
            {
                using var onPath = BuildOnPath(font);
                canvas.DrawPath(onPath, paint);
                return;
            }

            if (Vertical)
            {
                var lines = Lines;
                var firstBaseline = Origin.Y - font.Metrics.Ascent;
                for (var column = 0; column < lines.Length; column++)
                {
                    var x = Origin.X + column * LineHeight(font);
                    var y = firstBaseline;
                    foreach (var character in lines[column])
                    {
                        canvas.DrawText(character.ToString(), x, y, font, paint);
                        y += font.Spacing + ExtraPerCharacter;
                    }
                }
                return;
            }

            if (Warp != 0)
            {
                // Warped text is drawn as one outline bent along an arc.
                using var path = BuildPath(font);
                using var warped = WarpPath(path);
                canvas.DrawPath(warped, paint);
                return;
            }

            foreach (var (line, x, baseline, width) in Layout(font))
            {
                if (Tracking == 0)
                {
                    canvas.DrawText(line, x, baseline, font, paint);
                }
                else
                {
                    var cx = x;
                    foreach (var character in line)
                    {
                        var s = character.ToString();
                        canvas.DrawText(s, cx, baseline, font, paint);
                        cx += font.MeasureText(s) + ExtraPerCharacter;
                    }
                }
                foreach (var rect in Decorations(font, x, baseline, width))
                    canvas.DrawRect(rect, paint);
            }
        }

        /// <summary>Underline / strikethrough bars for one line.</summary>
        private IEnumerable<SKRect> Decorations(SKFont font, float x, float baseline, float width)
        {
            if (width <= 0) yield break;
            var thickness = Math.Max(1f, font.Metrics.UnderlineThickness ?? FontSize / 15f);
            if (Underline)
            {
                var y = baseline + (font.Metrics.UnderlinePosition ?? FontSize / 10f);
                yield return new SKRect(x, y, x + width, y + thickness);
            }
            if (Strikethrough)
            {
                var y = baseline + (font.Metrics.StrikeoutPosition ?? -FontSize * 0.3f);
                yield return new SKRect(x, y, x + width, y + thickness);
            }
        }

        /// <summary>Type on a path: every letter stood on the curve, turned to follow it.
        /// Lines are joined with spaces; letters past the curve's end are left out.</summary>
        private SKPath BuildOnPath(SKFont font)
        {
            var outline = new SKPath();
            using var curve = SKPath.ParseSvgPathData(PathData) ?? new SKPath();
            using var measure = new SKPathMeasure(curve, false);
            var length = measure.Length;
            if (length <= 0) return outline;

            var text = string.Join(" ", Lines);
            var widths = text.Select(c => font.MeasureText(c.ToString())).ToArray();
            var total = widths.Sum() + ExtraPerCharacter * Math.Max(0, text.Length - 1);
            var distance = Alignment switch
            {
                TextAlignment.Center => (length - total) / 2,
                TextAlignment.Right => length - total,
                _ => 0f,
            };
            for (var i = 0; i < text.Length; i++)
            {
                var w = widths[i];
                var middle = distance + w / 2;
                distance += w + ExtraPerCharacter;
                if (middle < 0) continue;
                if (middle > length) break;
                if (!measure.GetPositionAndTangent(middle, out var at, out var tangent)) continue;
                using var glyph = font.GetTextPath(text[i].ToString(), new SKPoint(-w / 2, 0));
                var turn = SKMatrix.CreateRotation(MathF.Atan2(tangent.Y, tangent.X)).PostConcat(SKMatrix.CreateTranslation(at.X, at.Y));
                glyph.Transform(turn);
                outline.AddPath(glyph);
            }
            return outline;
        }

        /// <summary>The (horizontal) text as one outline, before warping.</summary>
        private SKPath BuildPath(SKFont font)
        {
            var path = new SKPath();
            foreach (var (line, x, baseline, width) in Layout(font))
            {
                var cx = x;
                foreach (var character in line)
                {
                    var s = character.ToString();
                    using var glyph = font.GetTextPath(s, new SKPoint(cx, baseline));
                    path.AddPath(glyph);
                    cx += font.MeasureText(s) + ExtraPerCharacter;
                }
                foreach (var rect in Decorations(font, x, baseline, width)) path.AddRect(rect);
            }
            return path;
        }

        /// <summary>Warp "Arc": bends the outline so its middle rises (positive) or sinks
        /// (negative), up to a half circle at +-100.</summary>
        private SKPath WarpPath(SKPath path)
        {
            var bounds = path.Bounds;
            if (bounds.Width <= 0) return new SKPath(path);
            var bend = Math.Clamp(Warp, -100, 100) / 100f;
            if (WarpStyle != TextWarpStyle.Arc) return MapPath(path, p => Shape(p, bounds, bend));
            var totalAngle = MathF.PI * Math.Abs(bend);            // the arc's sweep
            var radius = bounds.Width / totalAngle;
            var cx = bounds.MidX;
            var up = bend > 0;
            // The arc goes through the text's baseline-ish middle row.
            var midY = up ? bounds.Bottom : bounds.Top;
            var centerY = up ? midY + radius : midY - radius;

            return MapPath(path, Map);

            SKPoint Map(SKPoint p)
            {
                var theta = (p.X - cx) / radius;
                var distance = up ? radius + (midY - p.Y) : radius + (p.Y - midY);
                return up
                    ? new SKPoint(cx + distance * MathF.Sin(theta), centerY - distance * MathF.Cos(theta))
                    : new SKPoint(cx + distance * MathF.Sin(theta), centerY + distance * MathF.Cos(theta));
            }
        }

        /// <summary>The non-Arc warps: every point moves up or down by a smooth function
        /// of where it sits in the text (u across, v down, both 0..1). Positive bend
        /// moves points up.</summary>
        private SKPoint Shape(SKPoint p, SKRect bounds, float bend)
        {
            var u = (p.X - bounds.Left) / bounds.Width;
            var v = bounds.Height > 0 ? (p.Y - bounds.Top) / bounds.Height : 0.5f;
            var h = bounds.Height;
            var hump = MathF.Sin(MathF.PI * u);               // 0 at the ends, 1 in the middle
            float dy = WarpStyle switch
            {
                // The whole text rises in the middle (it doesn't turn, unlike Arc).
                TextWarpStyle.Arch => -hump * h,
                // Top goes up and bottom goes down in the middle: the text swells.
                TextWarpStyle.Bulge => -hump * h * 0.6f * (1 - 2 * v),
                // A wave whose top and bottom are a little out of step.
                TextWarpStyle.Wave => -MathF.Sin(2 * MathF.PI * u + v * 0.8f) * h * 0.5f,
                // A flag: one full wave, flapping more toward the right.
                TextWarpStyle.Flag => -MathF.Sin(2 * MathF.PI * u) * h * 0.5f * (0.4f + 0.6f * u),
                // Climbs from left to right.
                TextWarpStyle.Rise => -(u - 0.5f) * h * 1.2f,
                // Swollen at the left, pinched at the right - a fish.
                TextWarpStyle.Fish => -MathF.Sin(MathF.PI * MathF.Min(1, u * 1.4f)) * (1 - u) * h * 0.9f * (1 - 2 * v),
                _ => 0,
            };
            return new SKPoint(p.X, p.Y + bend * dy);
        }

        /// <summary>Copies <paramref name="path"/> with every point moved by
        /// <paramref name="map"/>; straight edges are split so they can bend.</summary>
        private static SKPath MapPath(SKPath path, Func<SKPoint, SKPoint> map)
        {
            SKPoint Map(SKPoint p) => map(p);
            var result = new SKPath { FillType = path.FillType };
            using var iterator = path.CreateRawIterator();
            var points = new SKPoint[4];
            SKPathVerb verb;
            var last = SKPoint.Empty;
            while ((verb = iterator.Next(points)) != SKPathVerb.Done)
            {
                switch (verb)
                {
                    case SKPathVerb.Move:
                        result.MoveTo(Map(points[0]));
                        last = points[0];
                        break;
                    case SKPathVerb.Line:
                        // Straight edges (underlines, glyph stems) must bend too: split them.
                        const int steps = 12;
                        for (var i = 1; i <= steps; i++)
                        {
                            var t = i / (float)steps;
                            result.LineTo(Map(new SKPoint(last.X + (points[1].X - last.X) * t, last.Y + (points[1].Y - last.Y) * t)));
                        }
                        last = points[1];
                        break;
                    case SKPathVerb.Quad:
                        result.QuadTo(Map(points[1]), Map(points[2]));
                        last = points[2];
                        break;
                    case SKPathVerb.Conic:
                        result.ConicTo(Map(points[1]), Map(points[2]), iterator.ConicWeight());
                        last = points[2];
                        break;
                    case SKPathVerb.Cubic:
                        result.CubicTo(Map(points[1]), Map(points[2]), Map(points[3]));
                        last = points[3];
                        break;
                    case SKPathVerb.Close:
                        result.Close();
                        break;
                }
            }
            return result;
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

                if (PathData is not null)
                {
                    using var onPath = BuildOnPath(font);
                    var b = onPath.Bounds;
                    return b.IsEmpty ? new SKRect(Origin.X, Origin.Y, Origin.X + FontSize / 2, Origin.Y + FontSize) : b;
                }

                if (Vertical)
                {
                    var longest = 0;
                    foreach (var line in lines) longest = Math.Max(longest, line.Length);
                    return new SKRect(Origin.X, Origin.Y, Origin.X + lines.Length * LineHeight(font),
                        Origin.Y + Math.Max(1, longest) * (font.Spacing + ExtraPerCharacter));
                }

                if (Warp != 0)
                {
                    using var path = BuildPath(font);
                    using var warped = WarpPath(path);
                    var b = warped.Bounds;
                    return b.IsEmpty ? new SKRect(Origin.X, Origin.Y, Origin.X + FontSize / 2, Origin.Y + FontSize) : b;
                }

                var wrapped = LinesFor(font);
                var width = BoxWidth;
                foreach (var line in wrapped) width = Math.Max(width, LineWidth(font, line));
                var height = (wrapped.Count - 1) * LineHeight(font) + font.Spacing;
                return new SKRect(Origin.X, Origin.Y, Origin.X + Math.Max(width, FontSize / 2), Origin.Y + height);
            }
        }

        public TextContent MovedBy(float dx, float dy)
        {
            var moved = this with { Origin = new SKPoint(Origin.X + dx, Origin.Y + dy) };
            if (PathData is null) return moved;
            using var curve = SKPath.ParseSvgPathData(PathData) ?? new SKPath();
            curve.Transform(SKMatrix.CreateTranslation(dx, dy));
            return moved with { PathData = curve.ToSvgPathData() };
        }
    }
}
