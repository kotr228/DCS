using System;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// A second batch of Filter menu entries: Distort (ZigZag, Shear), Blur (Spin Blur),
    /// Noise (Reduce Noise), Sharpen (Sharpen Edges), Render (Fibers) and Stylize /
    /// Artistic looks (Glowing Edges, Cutout). Each returns a new bitmap.
    /// </summary>
    public static class MoreFilters2
    {
        private static byte B(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

        /// <summary>Distort &gt; ZigZag: ripples running out from the middle, like a stone
        /// dropped in a pond. <paramref name="amount"/> -100..100, <paramref name="ridges"/> 1..20.</summary>
        public static SKBitmap ZigZag(SKBitmap source, float amount, float ridges)
        {
            float cx = source.Width / 2f, cy = source.Height / 2f;
            var radius = MathF.Min(cx, cy);
            var a = Math.Clamp(amount, -100, 100) / 100f * radius * 0.08f;
            var k = Math.Clamp(ridges, 1, 20) * 2 * MathF.PI / radius;
            return DistortFilters.Remap(source, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                var r = MathF.Sqrt(dx * dx + dy * dy);
                if (r < 1e-3f || r > radius) return (x, y);
                var fade = 1 - r / radius;                              // calm at the edge
                var shift = a * MathF.Sin(r * k) * fade;
                return (x + dx / r * shift, y + dy / r * shift);
            });
        }

        /// <summary>Distort &gt; Shear: each row slid sideways along a curve - a bend
        /// <paramref name="amount"/> (-100..100) percent of the width at the middle row.</summary>
        public static SKBitmap Shear(SKBitmap source, float amount)
        {
            var h = Math.Max(1, source.Height - 1);
            var shift = Math.Clamp(amount, -100, 100) / 100f * source.Width * 0.5f;
            return DistortFilters.Remap(source, (x, y) =>
            {
                var t = y / h;
                return (x - shift * 4 * t * (1 - t), y);              // 0 at top and bottom, most at the middle
            });
        }

        /// <summary>Blur &gt; Spin Blur: the picture smeared round in circles about its middle,
        /// by up to <paramref name="degrees"/> (like a spinning wheel).</summary>
        public static SKBitmap SpinBlur(SKBitmap source, float degrees)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            float cx = w / 2f, cy = h / 2f;
            var angle = Math.Clamp(degrees, 0, 90) * MathF.PI / 180;
            const int samples = 16;
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = 0, g = 0, b = 0, al = 0;
                    for (var i = 0; i < samples; i++)
                    {
                        var t = (i / (float)(samples - 1) - 0.5f) * angle;
                        var (sin, cos) = MathF.SinCos(t);
                        var c = DistortFilters.Sample(src, w, h, cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
                        var a = c.Alpha / 255f;
                        r += c.Red * a; g += c.Green * a; b += c.Blue * a; al += c.Alpha;
                    }
                    var alpha = al / samples;
                    var k = alpha > 0 ? 255f / al : 0;
                    dst[y * w + x] = new SKColor(B(r * k), B(g * k), B(b * k), B(alpha));
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Noise &gt; Reduce Noise: grain smoothed away where the picture is flat,
        /// detail kept where it changes. <paramref name="strength"/> 1..10;
        /// <paramref name="preserveDetails"/> 0..100 percent.</summary>
        public static SKBitmap ReduceNoise(SKBitmap source, float strength, float preserveDetails)
        {
            var radius = (int)Math.Clamp(MathF.Round(strength / 2 + 1), 1, 6);
            var threshold = Math.Clamp(strength, 1, 10) * 6 * (1.2f - Math.Clamp(preserveDetails, 0, 100) / 100f);
            return DistortFilters.SurfaceBlur(source, radius, threshold);
        }

        /// <summary>Sharpen &gt; Sharpen Edges: sharpens only where there are edges, so flat
        /// areas (skin, sky) don't get grainy.</summary>
        public static SKBitmap SharpenEdges(SKBitmap source)
        {
            using var sharp = EnhanceFilters.UnsharpMask(source, 1.2f, 120);
            using var edges = StylizeFilters.FindEdges(source);
            var s = source.Pixels;
            var t = sharp.Pixels;
            var e = edges.Pixels;
            var dst = new SKColor[s.Length];
            Parallel.For(0, s.Length, i =>
            {
                // FindEdges draws edges dark on white: darker = more edge.
                var edge = Math.Clamp((255 - (e[i].Red + e[i].Green + e[i].Blue) / 3f) / 96f, 0, 1);
                var a = s[i];
                var b = t[i];
                dst[i] = new SKColor(B(a.Red + (b.Red - a.Red) * edge), B(a.Green + (b.Green - a.Green) * edge), B(a.Blue + (b.Blue - a.Blue) * edge), a.Alpha);
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Render &gt; Fibers: woven-looking vertical fibers between the foreground and
        /// background colors. <paramref name="variance"/> 1..64 (how much the fibers change
        /// along their length), <paramref name="strength"/> 1..64 (how long they are).</summary>
        public static SKBitmap Fibers(SKBitmap source, SKColor foreground, SKColor background, float variance, float strength, int seed)
        {
            int w = source.Width, h = source.Height;
            var dst = new SKColor[w * h];
            var rnd = new Random(seed);
            var columns = new float[w];
            for (var x = 0; x < w; x++) columns[x] = (float)rnd.NextDouble();
            var change = Math.Clamp(variance, 1, 64) / 64f;
            var length = Math.Clamp(strength, 1, 64);
            for (var x = 0; x < w; x++)
            {
                var v = columns[x];
                var fiberRnd = new Random(seed * 31 + x);
                for (var y = 0; y < h; y++)
                {
                    if (fiberRnd.NextDouble() < 1 / length) v = Math.Clamp(v + ((float)fiberRnd.NextDouble() - 0.5f) * change * 2, 0, 1);
                    // A little blur across neighbouring fibers.
                    var n = (v * 2 + columns[Math.Max(0, x - 1)] + columns[Math.Min(w - 1, x + 1)]) / 4;
                    dst[y * w + x] = new SKColor(
                        B(background.Red + (foreground.Red - background.Red) * n),
                        B(background.Green + (foreground.Green - background.Green) * n),
                        B(background.Blue + (foreground.Blue - background.Blue) * n));
                }
                columns[x] = v;
            }
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Stylize &gt; Glowing Edges: the edges glow in their own colors on black,
        /// like neon. <paramref name="brightness"/> 1..20.</summary>
        public static SKBitmap GlowingEdges(SKBitmap source, float brightness)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var gain = Math.Clamp(brightness, 1, 20) / 4f;
            Parallel.For(0, h, y =>
            {
                // One small buffer per row (a stackalloc inside the pixel loop would grow the stack with every pixel).
                Span<float> o = stackalloc float[3];
                for (var x = 0; x < w; x++)
                {
                    SKColor P(int xx, int yy) => src[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)];
                    float Ch(SKColor c, int k) => k == 0 ? c.Red : k == 1 ? c.Green : c.Blue;
                    for (var k = 0; k < 3; k++)
                    {
                        var gx = Ch(P(x + 1, y - 1), k) + 2 * Ch(P(x + 1, y), k) + Ch(P(x + 1, y + 1), k) - Ch(P(x - 1, y - 1), k) - 2 * Ch(P(x - 1, y), k) - Ch(P(x - 1, y + 1), k);
                        var gy = Ch(P(x - 1, y + 1), k) + 2 * Ch(P(x, y + 1), k) + Ch(P(x + 1, y + 1), k) - Ch(P(x - 1, y - 1), k) - 2 * Ch(P(x, y - 1), k) - Ch(P(x + 1, y - 1), k);
                        o[k] = MathF.Sqrt(gx * gx + gy * gy) / 4 * gain;
                    }
                    dst[y * w + x] = new SKColor(B(o[0]), B(o[1]), B(o[2]), src[y * w + x].Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Artistic &gt; Cutout: the picture as a few flat colors with smooth edges,
        /// like shapes cut from colored paper. <paramref name="levels"/> 2..8.</summary>
        public static SKBitmap Cutout(SKBitmap source, int levels)
        {
            using var smooth = EnhanceFilters.Median(source, 3);
            return StylizeFilters.Posterize(smooth, Math.Clamp(levels, 2, 8));
        }

        /// <summary>
        /// Other &gt; Custom: each pixel becomes its neighbourhood weighted by
        /// <paramref name="kernel"/> (rows of numbers, odd size, middle = the pixel itself),
        /// divided by <paramref name="scale"/>, plus <paramref name="offset"/>. Alpha is kept.
        /// </summary>
        public static SKBitmap Custom(SKBitmap source, float[,] kernel, float scale, float offset)
        {
            int w = source.Width, h = source.Height;
            int kh = kernel.GetLength(0), kw = kernel.GetLength(1);
            int cy = kh / 2, cx = kw / 2;
            if (Math.Abs(scale) < 1e-6f) scale = 1;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0;
                    for (var j = 0; j < kh; j++)
                        for (var i = 0; i < kw; i++)
                        {
                            var k = kernel[j, i];
                            if (k == 0) continue;
                            var c = src[Math.Clamp(y + j - cy, 0, h - 1) * w + Math.Clamp(x + i - cx, 0, w - 1)];
                            r += c.Red * k; g += c.Green * k; b += c.Blue * k;
                        }
                    dst[y * w + x] = new SKColor(B(r / scale + offset), B(g / scale + offset), B(b / scale + offset), src[y * w + x].Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Reads a kernel like "0 -1 0; -1 5 -1; 0 -1 0" (rows split by ; or new
        /// lines). Null if the rows aren't all the same odd length (up to 7).</summary>
        public static float[,]? ParseKernel(string text)
        {
            var rows = text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Where(r => r.Length > 0).ToList();
            if (rows.Count == 0 || rows.Count % 2 == 0 || rows.Count > 7 || rows.Any(r => r.Length != rows[0].Length) || rows[0].Length % 2 == 0 || rows[0].Length > 7) return null;
            var kernel = new float[rows.Count, rows[0].Length];
            for (var j = 0; j < rows.Count; j++)
                for (var i = 0; i < rows[0].Length; i++)
                {
                    if (!float.TryParse(rows[j][i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
                    kernel[j, i] = v;
                }
            return kernel;
        }

        /// <summary>
        /// Distort &gt; Displace: every pixel is shifted by the brightness of <paramref name="map"/>
        /// (stretched to the same size) - middle gray stays, white moves by
        /// +<paramref name="horizontal"/> / +<paramref name="vertical"/> pixels, black by minus that.
        /// </summary>
        public static SKBitmap Displace(SKBitmap source, SKBitmap map, float horizontal, float vertical)
        {
            int w = source.Width, h = source.Height;
            using var fitted = map.Width == w && map.Height == h ? map.Copy()
                : map.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
            var m = fitted.Pixels;
            return DistortFilters.Remap(source, (x, y) =>
            {
                var c = m[Math.Clamp((int)y, 0, h - 1) * w + Math.Clamp((int)x, 0, w - 1)];
                // Red moves sideways and green up/down, like Photoshop; a gray map moves both alike.
                var dx = (c.Red - 128) / 128f * horizontal;
                var dy = (c.Green - 128) / 128f * vertical;
                return (x - dx, y - dy);
            });
        }
    }
}
