using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>The color statistics Match Color transfers: mean and spread of
    /// brightness (Y) and the two color axes (Cb, Cr), on 0..255.</summary>
    public readonly record struct ColorStats(double MeanY, double SpreadY, double MeanCb, double SpreadCb, double MeanCr, double SpreadCr)
    {
        public static ColorStats Of(SKBitmap image)
        {
            double sy = 0, sb = 0, sr = 0, qy = 0, qb = 0, qr = 0;
            long n = 0;
            foreach (var c in image.Pixels)
            {
                if (c.Alpha < 8) continue;
                var (y, cb, cr) = PhotoFilters.ToYcc(c);
                sy += y; sb += cb; sr += cr;
                qy += y * y; qb += cb * cb; qr += cr * cr;
                n++;
            }
            if (n == 0) return new ColorStats(128, 1, 0, 1, 0, 1);
            double My = sy / n, Mb = sb / n, Mr = sr / n;
            double S(double q, double m) => Math.Max(1, Math.Sqrt(Math.Max(0, q / n - m * m)));
            return new ColorStats(My, S(qy, My), Mb, S(qb, Mb), Mr, S(qr, Mr));
        }
    }

    /// <summary>Match Color and Lens Blur.</summary>
    public static class PhotoFilters
    {
        internal static (double Y, double Cb, double Cr) ToYcc(SKColor c) =>
            (0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue,
             -0.168736 * c.Red - 0.331264 * c.Green + 0.5 * c.Blue,
             0.5 * c.Red - 0.418688 * c.Green - 0.081312 * c.Blue);

        private static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

        /// <summary>
        /// Match Color: gives <paramref name="target"/> the overall brightness and color
        /// of an image with statistics <paramref name="source"/> (or, with
        /// <paramref name="neutralize"/> and no source, just removes its color cast).
        /// </summary>
        /// <param name="luminance">1..200 %: brighter or darker afterwards.</param>
        /// <param name="intensity">0..200 %: color saturation afterwards.</param>
        /// <param name="fade">0..100 %: how much of the original shows through.</param>
        public static SKBitmap MatchColor(SKBitmap target, ColorStats? source, double luminance, double intensity, double fade, bool neutralize)
        {
            var t = ColorStats.Of(target);
            var s = source ?? t;
            // Neutralize: the colors are centered on gray, which removes a color cast.
            if (neutralize) s = s with { MeanCb = 0, MeanCr = 0 };

            var ky = s.SpreadY / t.SpreadY;
            var kb = s.SpreadCb / t.SpreadCb;
            var kr = s.SpreadCr / t.SpreadCr;
            if (source is null) { ky = 1; kb = 1; kr = 1; }
            var lum = Math.Clamp(luminance, 1, 200) / 100;
            var sat = Math.Clamp(intensity, 0, 200) / 100;
            var keep = Math.Clamp(fade, 0, 100) / 100;

            var pixels = target.Pixels;
            var dst = new SKColor[pixels.Length];
            Parallel.For(0, target.Height, row =>
            {
                for (var i = row * target.Width; i < (row + 1) * target.Width; i++)
                {
                    var c = pixels[i];
                    if (c.Alpha == 0) { dst[i] = c; continue; }
                    var (y, cb, cr) = ToYcc(c);
                    var ny = ((y - t.MeanY) * ky + s.MeanY) * lum;
                    var nb = ((cb - t.MeanCb) * kb + s.MeanCb) * sat;
                    var nr = ((cr - t.MeanCr) * kr + s.MeanCr) * sat;
                    double r = ny + 1.402 * nr, g = ny - 0.344136 * nb - 0.714136 * nr, b = ny + 1.772 * nb;
                    dst[i] = new SKColor(
                        B(r + (c.Red - r) * keep), B(g + (c.Green - g) * keep), B(b + (c.Blue - b) * keep), c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(target, dst);
        }

        /// <summary>
        /// Lens Blur: an out-of-focus camera blur. Each point spreads into a disc (not a
        /// soft Gaussian blob), and highlights brighter than <paramref name="threshold"/>
        /// bloom into bright bokeh circles by <paramref name="brightness"/> (0..100).
        /// </summary>
        public static SKBitmap LensBlur(SKBitmap source, float radius, float brightness, float threshold)
        {
            int w = source.Width, h = source.Height;
            var r = (int)Math.Clamp(MathF.Round(radius), 0, 100);
            if (r == 0) return source.Copy();

            // Premultiplied floats, highlights boosted.
            var boost = Math.Clamp(brightness, 0, 100) / 100f * 6f;
            var level = Math.Clamp(threshold, 0, 255);
            var px = source.Pixels;
            var ch = new float[4][];
            for (var k = 0; k < 4; k++) ch[k] = new float[w * h];
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                var a = c.Alpha / 255f;
                var luma = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
                var gain = boost > 0 && luma > level ? 1 + boost * (luma - level) / Math.Max(1f, 255 - level) : 1f;
                ch[0][i] = c.Red * a * gain; ch[1][i] = c.Green * a * gain; ch[2][i] = c.Blue * a * gain; ch[3][i] = a;
            }

            // Running sums along each row, so a row's slice of the disc costs one subtraction.
            var prefix = new float[4][];
            for (var k = 0; k < 4; k++) prefix[k] = new float[(w + 1) * h];
            Parallel.For(0, h, y =>
            {
                for (var k = 0; k < 4; k++)
                {
                    var p = prefix[k];
                    var src = ch[k];
                    var o = y * (w + 1);
                    for (var x = 0; x < w; x++) p[o + x + 1] = p[o + x] + src[y * w + x];
                }
            });

            var half = new int[2 * r + 1];
            for (var dy = -r; dy <= r; dy++) half[dy + r] = (int)MathF.Floor(MathF.Sqrt(r * r - dy * dy + 0.25f));

            var dst = new SKColor[w * h];
            Parallel.For(0, h, y =>
            {
                var sums = new float[4];
                for (var x = 0; x < w; x++)
                {
                    sums[0] = sums[1] = sums[2] = sums[3] = 0;
                    var count = 0;
                    for (var dy = -r; dy <= r; dy++)
                    {
                        var yy = Math.Clamp(y + dy, 0, h - 1);
                        var hw = half[dy + r];
                        int x0 = x - hw, x1 = x + hw;
                        int c0 = Math.Max(0, x0), c1 = Math.Min(w - 1, x1);
                        var o = yy * (w + 1);
                        for (var k = 0; k < 4; k++)
                        {
                            var p = prefix[k];
                            var inside = p[o + c1 + 1] - p[o + c0];
                            // Past the image's edge, repeat the edge pixel.
                            if (x0 < 0) inside += -x0 * ch[k][yy * w];
                            if (x1 > w - 1) inside += (x1 - (w - 1)) * ch[k][yy * w + w - 1];
                            sums[k] += inside;
                        }
                        count += x1 - x0 + 1;
                    }
                    var alpha = sums[3] / count;
                    if (alpha <= 1e-4f) { dst[y * w + x] = SKColors.Transparent; continue; }
                    var div = sums[3];
                    dst[y * w + x] = new SKColor(B(sums[0] / div), B(sums[1] / div), B(sums[2] / div), B(alpha * 255));
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }
    }
}
