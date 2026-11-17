using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Filter &gt; Vanishing Point: a flat surface in the photo (a wall, a floor, a screen)
    /// given by its four corners, and a picture laid onto it in perspective - placed and
    /// sized in the surface's own coordinates, or repeated over all of it like tiles.
    /// </summary>
    public static class VanishingPoint
    {
        /// <summary>
        /// The perspective matrix taking the unit square (0,0)-(1,1) to the four corners,
        /// in order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public static SKMatrix SquareToQuad(SKPoint[] q)
        {
            if (q.Length != 4) throw new ArgumentException("Four corners.", nameof(q));
            double x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
            var sx = x0 - x1 + x2 - x3;
            var sy = y0 - y1 + y2 - y3;
            double a, b, c, d, e, f, g, h;
            if (Math.Abs(sx) < 1e-9 && Math.Abs(sy) < 1e-9)
            {
                // A parallelogram: no perspective.
                a = x1 - x0; b = x3 - x0; c = x0;
                d = y1 - y0; e = y3 - y0; f = y0;
                g = 0; h = 0;
            }
            else
            {
                double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
                var den = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(den) < 1e-12) den = 1e-12;
                g = (sx * dy2 - dx2 * sy) / den;
                h = (dx1 * sy - sx * dy1) / den;
                a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0;
                d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
            }
            return new SKMatrix((float)a, (float)b, (float)c, (float)d, (float)e, (float)f, (float)g, (float)h, 1);
        }

        /// <summary>The surface's width over its height, guessed from its edges' average lengths.</summary>
        public static float Aspect(SKPoint[] q)
        {
            static float L(SKPoint p, SKPoint r) => (p - r).Length;
            var width = (L(q[0], q[1]) + L(q[3], q[2])) / 2;
            var height = (L(q[0], q[3]) + L(q[1], q[2])) / 2;
            return height < 1e-3f ? 1 : width / height;
        }

        /// <summary>
        /// A <paramref name="width"/> x <paramref name="height"/> layer with
        /// <paramref name="picture"/> laid onto the surface <paramref name="corners"/>.
        /// <paramref name="u"/>, <paramref name="v"/>: where the picture's middle sits on the
        /// surface (0..1 across and down); <paramref name="size"/>: its width as a part of
        /// the surface's width; <paramref name="tile"/>: repeat it over the whole surface.
        /// Nothing is drawn outside the surface.
        /// </summary>
        public static SKBitmap Render(int width, int height, SKPoint[] corners, SKBitmap picture,
            float u, float v, float size, bool tile, float opacity = 1)
        {
            var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            Draw(canvas, corners, picture, u, v, size, tile, opacity);
            return result;
        }

        /// <summary>Draws as <see cref="Render"/> does, onto any canvas (a preview at another scale).</summary>
        public static void Draw(SKCanvas canvas, SKPoint[] corners, SKBitmap picture, float u, float v, float size, bool tile, float opacity = 1)
        {
            using var outline = new SKPath();
            outline.AddPoly(corners, close: true);
            canvas.Save();
            canvas.ClipPath(outline, antialias: true);
            canvas.Concat(SquareToQuad(corners));

            // Picture size in surface units: its width is `size` of the surface's width; its
            // height follows its own shape and the surface's (so it isn't squashed).
            var w = Math.Max(1e-4f, size);
            var h = w * picture.Height / Math.Max(1, picture.Width) * Aspect(corners);
            var place = SKMatrix.CreateScale(w / picture.Width, h / picture.Height).PostConcat(SKMatrix.CreateTranslation(u - w / 2, v - h / 2));

            using var image = SKImage.FromBitmap(picture);
            var tileMode = tile ? SKShaderTileMode.Repeat : SKShaderTileMode.Decal;
            using var shader = image.ToShader(tileMode, tileMode, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), place);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255, 0, 255)) };
            canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);
            canvas.Restore();
        }
    }
}
