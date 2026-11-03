using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Filter &gt; Distort (Twirl, Pinch, Wave), Filter &gt; Blur &gt; Radial Blur and
    /// Surface Blur, and Filter &gt; Other &gt; High Pass. Distortions work backwards:
    /// for every output pixel they compute where in the source it comes from and
    /// sample there (bilinear), so there are no holes.
    /// </summary>
    public static class DistortFilters
    {
        /// <summary>Swirls the image around its center; strongest in the middle,
        /// fading to nothing at the edge of the largest centered circle.</summary>
        public static SKBitmap Twirl(SKBitmap source, float angleDegrees)
        {
            var cx = source.Width / 2f;
            var cy = source.Height / 2f;
            var radius = Math.Min(cx, cy);
            var angle = angleDegrees * MathF.PI / 180f;

            return Remap(source, (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= radius) return (x, y);
                var t = 1 - d / radius;
                var a = angle * t * t;
                float cos = MathF.Cos(a), sin = MathF.Sin(a);
                return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
            });
        }

        /// <summary>Squeezes the center in (positive amount) or bulges it out
        /// (negative), within the largest centered circle. Amount -100..100.</summary>
        public static SKBitmap Pinch(SKBitmap source, float amount)
        {
            var cx = source.Width / 2f;
            var cy = source.Height / 2f;
            var radius = Math.Min(cx, cy);
            var strength = Math.Clamp(amount, -100, 100) / 100f;

            return Remap(source, (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= radius || d < 1e-3f) return (x, y);
                // Output radius r samples the source at r^p: p < 1 reaches further out
                // (content pulled toward the center), p > 1 stays closer in (bulge).
                var r = d / radius;
                var p = 1 - 0.5f * strength;
                var factor = MathF.Pow(r, p - 1);
                return (cx + dx * factor, cy + dy * factor);
            });
        }

        /// <summary>Horizontal and vertical sine waves: <paramref name="amplitude"/>
        /// pixels high, <paramref name="wavelength"/> pixels long.</summary>
        public static SKBitmap Wave(SKBitmap source, float amplitude, float wavelength)
        {
            wavelength = Math.Max(2, wavelength);
            var k = 2 * MathF.PI / wavelength;
            return Remap(source, (x, y) => (x + amplitude * MathF.Sin(y * k), y + amplitude * MathF.Sin(x * k)));
        }

        /// <summary>Zoom blur: streaks pointing away from the center, as if zooming
        /// during the exposure. Amount 1..100.</summary>
        public static SKBitmap RadialBlur(SKBitmap source, float amount)
        {
            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var cx = width / 2f;
            var cy = height / 2f;
            var spread = Math.Clamp(amount, 0, 100) / 100f * 0.35f; // how far toward the center to sample
            const int samples = 24;

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (var i = 0; i < samples; i++)
                    {
                        var t = 1 - spread * i / (samples - 1);
                        var c = Sample(src, width, height, cx + (x - cx) * t, cy + (y - cy) * t);
                        var ca = c.Alpha / 255f;
                        r += c.Red * ca; g += c.Green * ca; b += c.Blue * ca; a += ca;
                    }

                    dst[y * width + x] = a <= 0
                        ? SKColors.Transparent
                        : new SKColor(ToByte(r / a), ToByte(g / a), ToByte(b / a), ToByte(a / samples * 255));
                }
            });

            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Blurs flat areas but keeps edges: only neighbours within
        /// <paramref name="threshold"/> levels of the pixel are averaged (smooths skin
        /// and noise without softening outlines).</summary>
        public static SKBitmap SurfaceBlur(SKBitmap source, int radius, float threshold)
        {
            radius = Math.Clamp(radius, 1, 30);
            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var limit = Math.Max(1f, threshold);

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var center = src[y * width + x];
                    float r = 0, g = 0, b = 0, weight = 0;
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        var sy = y + dy;
                        if (sy < 0 || sy >= height) continue;
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            var sx = x + dx;
                            if (sx < 0 || sx >= width) continue;
                            var c = src[sy * width + sx];
                            var diff = (Math.Abs(c.Red - center.Red) + Math.Abs(c.Green - center.Green) + Math.Abs(c.Blue - center.Blue)) / 3f;
                            if (diff > limit) continue;
                            var w = 1 - diff / (2.5f * limit);
                            r += c.Red * w; g += c.Green * w; b += c.Blue * w; weight += w;
                        }
                    }

                    dst[y * width + x] = new SKColor(ToByte(r / weight), ToByte(g / weight), ToByte(b / weight), center.Alpha);
                }
            });

            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Keeps only detail finer than <paramref name="radius"/>: the image
        /// minus its blur, around mid gray. Used under an Overlay layer for sharpening.</summary>
        public static SKBitmap HighPass(SKBitmap source, float radius)
        {
            using var blurred = BlurFilter.Apply(source, BlurType.Gaussian, Math.Max(0.5f, radius));
            var src = source.Pixels;
            var blur = blurred.Pixels;
            var dst = new SKColor[src.Length];
            Parallel.For(0, src.Length, i =>
            {
                var c = src[i];
                var b = blur[i];
                dst[i] = new SKColor(
                    ToByte(128 + c.Red - b.Red), ToByte(128 + c.Green - b.Green), ToByte(128 + c.Blue - b.Blue), c.Alpha);
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        // ================= Helpers =================

        /// <summary>Every output pixel (x, y) takes the source color at map(x, y).</summary>
        internal static SKBitmap Remap(SKBitmap source, Func<float, float, (float X, float Y)> map)
        {
            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var (sx, sy) = map(x + 0.5f, y + 0.5f);
                    dst[y * width + x] = Sample(src, width, height, sx - 0.5f, sy - 0.5f);
                }
            });

            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Bilinear sample (premultiplied, so transparent edges don't darken).
        /// Outside the image the nearest edge pixel is used.</summary>
        internal static SKColor Sample(SKColor[] pixels, int width, int height, float x, float y)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var fx = x - x0;
            var fy = y - y0;

            var c00 = pixels[y0 * width + x0];
            var c10 = pixels[y0 * width + x1];
            var c01 = pixels[y1 * width + x0];
            var c11 = pixels[y1 * width + x1];

            float W(SKColor c, float w) => c.Alpha / 255f * w;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            float a00 = W(c00, w00), a10 = W(c10, w10), a01 = W(c01, w01), a11 = W(c11, w11);
            var alpha = a00 + a10 + a01 + a11;
            if (alpha <= 1e-6f) return SKColors.Transparent;

            var r = (c00.Red * a00 + c10.Red * a10 + c01.Red * a01 + c11.Red * a11) / alpha;
            var g = (c00.Green * a00 + c10.Green * a10 + c01.Green * a01 + c11.Green * a11) / alpha;
            var b = (c00.Blue * a00 + c10.Blue * a10 + c01.Blue * a01 + c11.Blue * a11) / alpha;
            return new SKColor(ToByte(r), ToByte(g), ToByte(b), ToByte(alpha * 255));
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    }
}
