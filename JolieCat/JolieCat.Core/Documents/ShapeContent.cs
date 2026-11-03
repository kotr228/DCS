using System;
using System.Globalization;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    public enum VectorShapeKind
    {
        Rectangle,
        RoundedRectangle,
        Ellipse,
        Line,
        Polygon,
        Star,
    }

    /// <summary>
    /// An editable shape layer's content (Photoshop's shape layers): the layer's pixels
    /// are always this drawn by <see cref="Render"/>, so its size, colors, outline and
    /// corners can be changed later without losing sharpness. Immutable.
    /// </summary>
    /// <param name="Start">One corner of the shape's box (or a line's start).</param>
    /// <param name="End">The opposite corner (or a line's end).</param>
    /// <param name="Fill">Fill color; transparent = no fill.</param>
    /// <param name="Stroke">Outline color; ignored when <paramref name="StrokeWidth"/> is 0.</param>
    /// <param name="Sides">Polygon / star points.</param>
    public sealed record ShapeContent(
        VectorShapeKind Kind,
        SKPoint Start,
        SKPoint End,
        SKColor Fill,
        SKColor Stroke,
        float StrokeWidth,
        float CornerRadius = 12,
        int Sides = 5,
        bool AntiAlias = true)
    {
        public SKRect Box => new(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));

        /// <summary>The shape's outline.</summary>
        public SKPath BuildPath()
        {
            var path = new SKPath();
            var box = Box;
            switch (Kind)
            {
                case VectorShapeKind.Ellipse:
                    path.AddOval(box);
                    break;
                case VectorShapeKind.RoundedRectangle:
                    var r = Math.Min(CornerRadius, Math.Min(box.Width, box.Height) / 2);
                    path.AddRoundRect(box, r, r);
                    break;
                case VectorShapeKind.Line:
                    path.MoveTo(Start);
                    path.LineTo(End);
                    break;
                case VectorShapeKind.Polygon:
                case VectorShapeKind.Star:
                {
                    var n = Math.Clamp(Sides, 3, 64);
                    var cx = box.MidX;
                    var cy = box.MidY;
                    var star = Kind == VectorShapeKind.Star;
                    var count = star ? n * 2 : n;
                    for (var i = 0; i < count; i++)
                    {
                        // Point straight up first; stars alternate with inner points.
                        var a = -Math.PI / 2 + i * 2 * Math.PI / count;
                        var scale = star && i % 2 == 1 ? 0.45f : 1f;
                        var p = new SKPoint(cx + (float)Math.Cos(a) * box.Width / 2 * scale, cy + (float)Math.Sin(a) * box.Height / 2 * scale);
                        if (i == 0) path.MoveTo(p); else path.LineTo(p);
                    }
                    path.Close();
                    break;
                }
                default:
                    path.AddRect(box);
                    break;
            }
            return path;
        }

        public void Render(SKCanvas canvas)
        {
            using var path = BuildPath();
            using var paint = new SKPaint { IsAntialias = AntiAlias };

            if (Kind != VectorShapeKind.Line && Fill.Alpha > 0)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = Fill;
                canvas.DrawPath(path, paint);
            }

            var width = Kind == VectorShapeKind.Line ? Math.Max(1, StrokeWidth) : StrokeWidth;
            if (width > 0)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = Stroke;
                paint.StrokeWidth = width;
                paint.StrokeJoin = Kind == VectorShapeKind.RoundedRectangle ? SKStrokeJoin.Round : SKStrokeJoin.Miter;
                paint.StrokeCap = Kind == VectorShapeKind.Line ? SKStrokeCap.Round : SKStrokeCap.Butt;
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>The area the shape covers, outline included.</summary>
        public SKRect Bounds
        {
            get
            {
                var box = Box;
                var pad = Math.Max(1, StrokeWidth) / 2 + 1;
                return new SKRect(box.Left - pad, box.Top - pad, box.Right + pad, box.Bottom + pad);
            }
        }

        public ShapeContent MovedBy(float dx, float dy) =>
            this with { Start = new SKPoint(Start.X + dx, Start.Y + dy), End = new SKPoint(End.X + dx, End.Y + dy) };

        // ----- Saving: "kind;x0,y0,x1,y1;#fill;#stroke;width;radius;sides;aa" -----

        public string Serialize()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Create(c, $"{Kind};{Start.X},{Start.Y},{End.X},{End.Y};{Fill};{Stroke};{StrokeWidth};{CornerRadius};{Sides};{(AntiAlias ? 1 : 0)}");
        }

        public static ShapeContent? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Split(';');
            if (parts.Length < 8 || !Enum.TryParse<VectorShapeKind>(parts[0], out var kind)) return null;
            var xy = parts[1].Split(',');
            if (xy.Length != 4) return null;
            static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            try
            {
                return new ShapeContent(kind, new SKPoint(F(xy[0]), F(xy[1])), new SKPoint(F(xy[2]), F(xy[3])),
                    SKColor.Parse(parts[2]), SKColor.Parse(parts[3]), F(parts[4]), F(parts[5]),
                    int.Parse(parts[6], CultureInfo.InvariantCulture), parts[7] != "0");
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
