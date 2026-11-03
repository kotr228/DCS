using System;
using System.Globalization;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    public enum FillKind
    {
        SolidColor,
        Gradient,
        Pattern,
    }

    /// <summary>
    /// A fill layer's content (Layer > New Fill Layer): a solid color, a gradient or a
    /// pattern covering the whole canvas, usually limited by the layer's mask. The
    /// pixels are always this drawn by <see cref="Render"/>, so it can be changed later.
    /// </summary>
    /// <param name="Angle">Gradient direction, degrees (90 = bottom to top).</param>
    /// <param name="Radial">Gradient from the center outward instead of along a line.</param>
    /// <param name="Scale">Pattern scale, %.</param>
    public sealed record FillContent(
        FillKind Kind,
        SKColor Color,
        SKColor Color2,
        float Angle = 90,
        bool Radial = false,
        string PatternName = "Checker",
        float Scale = 100)
    {
        public void Render(SKCanvas canvas, int width, int height)
        {
            canvas.Clear(SKColors.Transparent);
            var area = new SKRect(0, 0, width, height);
            using var paint = new SKPaint { IsAntialias = true, IsDither = true };

            switch (Kind)
            {
                case FillKind.Gradient:
                    paint.Shader = Radial
                        ? SKShader.CreateRadialGradient(new SKPoint(area.MidX, area.MidY), Math.Max(area.Width, area.Height) / 2,
                            new[] { Color, Color2 }, SKShaderTileMode.Clamp)
                        : LinearGradient(area);
                    break;

                case FillKind.Pattern:
                {
                    using var tile = Effects.PatternTiles.Create(PatternName, Color);
                    var s = Math.Max(0.05f, Scale / 100f);
                    // Built-in patterns are drawn in Color on a Color2 background.
                    if (!Effects.PatternTiles.IsCustom(PatternName))
                        canvas.DrawColor(Color2);
                    paint.Shader = SKShader.CreateBitmap(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKMatrix.CreateScale(s, s));
                    break;
                }

                default:
                    paint.Color = Color;
                    break;
            }

            canvas.DrawRect(area, paint);
        }

        private SKShader LinearGradient(SKRect area)
        {
            var radians = Angle * Math.PI / 180;
            var dx = (float)Math.Cos(radians);
            var dy = (float)-Math.Sin(radians);
            var half = (Math.Abs(dx) * area.Width + Math.Abs(dy) * area.Height) / 2;
            var start = new SKPoint(area.MidX - dx * half, area.MidY - dy * half);
            var end = new SKPoint(area.MidX + dx * half, area.MidY + dy * half);
            return SKShader.CreateLinearGradient(start, end, new[] { Color, Color2 }, SKShaderTileMode.Clamp);
        }

        public string Title => Kind switch
        {
            FillKind.Gradient => "Gradient Fill",
            FillKind.Pattern => "Pattern Fill",
            _ => "Color Fill",
        };

        // ----- Saving: "kind;#color;#color2;angle;radial;pattern;scale" -----

        public string Serialize() => string.Create(CultureInfo.InvariantCulture,
            $"{Kind};{Color};{Color2};{Angle};{(Radial ? 1 : 0)};{PatternName};{Scale}");

        public static FillContent? Deserialize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var p = text.Split(';');
            if (p.Length < 7 || !Enum.TryParse<FillKind>(p[0], out var kind)) return null;
            try
            {
                static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                return new FillContent(kind, SKColor.Parse(p[1]), SKColor.Parse(p[2]), F(p[3]), p[4] == "1", p[5], F(p[6]));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
