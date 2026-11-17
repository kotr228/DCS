using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// More of Photoshop's Filter menu: Stylize (Solarize, Wind, Diffuse, Tiles),
    /// Pixelate (Crystallize, Color Halftone, Mezzotint), Distort (Polar Coordinates),
    /// Other (Maximum, Minimum, Offset), Blur (Average) and Noise (Despeckle).
    /// Each returns a new bitmap; the source is left as it is.
    /// </summary>
    public static class MoreFilters
    {
        /// <summary>A repeatable "random" number 0..1 for a pixel (same image, same result).</summary>
        private static float Noise(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

        /// <summary>Stylize &gt; Solarize: tones above the middle are turned over, like film
        /// exposed to light while developing.</summary>
        public static SKBitmap Solarize(SKBitmap source)
        {
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            static byte S(byte v) => v > 127 ? (byte)(255 - v) : v;
            Parallel.For(0, src.Length, i =>
            {
                var c = src[i];
                dst[i] = new SKColor(S(c.Red), S(c.Green), S(c.Blue), c.Alpha);
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Distort &gt; Polar Coordinates. <paramref name="toPolar"/>: the picture's
        /// top edge gathers into the middle and the sides wrap around into a circle (a
        /// panorama becomes a "little planet"); otherwise the reverse.</summary>
        public static SKBitmap PolarCoordinates(SKBitmap source, bool toPolar)
        {
            float w = source.Width, h = source.Height;
            float cx = w / 2, cy = h / 2;
            return DistortFilters.Remap(source, (x, y) =>
            {
                if (toPolar)
                {
                    // Output pixel at angle a, distance r from the middle <- source column a, row r.
                    float nx = (x - cx) / cx, ny = (y - cy) / cy;
                    var r = MathF.Sqrt(nx * nx + ny * ny);
                    var a = MathF.Atan2(nx, -ny);                     // 0 at the top, clockwise
                    if (a < 0) a += 2 * MathF.PI;
                    return (a / (2 * MathF.PI) * w, Math.Min(r, 1) * h);
                }
                else
                {
                    var a = x / w * 2 * MathF.PI;
                    var r = y / h;
                    return (cx + MathF.Sin(a) * r * cx, cy - MathF.Cos(a) * r * cy);
                }
            });
        }

        /// <summary>Stylize &gt; Wind: bright parts streak off to the right, fading over
        /// <paramref name="strength"/> pixels.</summary>
        public static SKBitmap Wind(SKBitmap source, float strength)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var fade = 255f / Math.Clamp(strength, 1, 500);
            Parallel.For(0, h, y =>
            {
                float r = 0, g = 0, b = 0;
                for (var x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    r = Math.Max(c.Red, r - fade);
                    g = Math.Max(c.Green, g - fade);
                    b = Math.Max(c.Blue, b - fade);
                    dst[y * w + x] = new SKColor(ToByte(r), ToByte(g), ToByte(b), c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Stylize &gt; Diffuse: every pixel swaps with a random one up to
        /// <paramref name="amount"/> pixels away, like frosted glass.</summary>
        public static SKBitmap Diffuse(SKBitmap source, float amount)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var reach = Math.Clamp(amount, 1, 100);
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    var sx = Math.Clamp(x + (int)MathF.Round((Noise(x, y, 1) * 2 - 1) * reach), 0, w - 1);
                    var sy = Math.Clamp(y + (int)MathF.Round((Noise(x, y, 2) * 2 - 1) * reach), 0, h - 1);
                    dst[y * w + x] = src[sy * w + sx];
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Pixelate &gt; Crystallize: the picture as flat-colored cells around
        /// scattered points, each the average color of its cell.</summary>
        public static SKBitmap Crystallize(SKBitmap source, float cellSize)
        {
            int w = source.Width, h = source.Height;
            var size = Math.Clamp(cellSize, 3, 300);
            int gw = (int)Math.Ceiling(w / size), gh = (int)Math.Ceiling(h / size);
            // One point per grid square, somewhere inside it.
            var px = new float[gw * gh];
            var py = new float[gw * gh];
            for (var j = 0; j < gh; j++)
                for (var i = 0; i < gw; i++)
                {
                    px[j * gw + i] = (i + Noise(i, j, 7)) * size;
                    py[j * gw + i] = (j + Noise(i, j, 8)) * size;
                }

            var src = source.Pixels;
            var owner = new int[src.Length];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    int gx = (int)(x / size), gy = (int)(y / size);
                    var best = 0;
                    var bestD = float.MaxValue;
                    for (var dj = -1; dj <= 1; dj++)
                        for (var di = -1; di <= 1; di++)
                        {
                            int i = gx + di, j = gy + dj;
                            if (i < 0 || j < 0 || i >= gw || j >= gh) continue;
                            var k = j * gw + i;
                            float dx = px[k] - x, dy = py[k] - y;
                            var d = dx * dx + dy * dy;
                            if (d < bestD) { bestD = d; best = k; }
                        }
                    owner[y * w + x] = best;
                }
            });

            var sum = new long[gw * gh * 5];
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                var k = owner[i] * 5;
                sum[k] += c.Red * c.Alpha; sum[k + 1] += c.Green * c.Alpha; sum[k + 2] += c.Blue * c.Alpha;
                sum[k + 3] += c.Alpha; sum[k + 4]++;
            }
            var colors = new SKColor[gw * gh];
            for (var k = 0; k < colors.Length; k++)
            {
                var a = sum[k * 5 + 3];
                var n = Math.Max(1, sum[k * 5 + 4]);
                colors[k] = a == 0 ? SKColors.Transparent
                    : new SKColor((byte)(sum[k * 5] / a), (byte)(sum[k * 5 + 1] / a), (byte)(sum[k * 5 + 2] / a), (byte)(a / n));
            }
            var dst = new SKColor[src.Length];
            Parallel.For(0, src.Length, i => dst[i] = colors[owner[i]]);
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Pixelate &gt; Color Halftone: the picture printed as cyan, magenta and
        /// yellow dots on rotated screens, up to <paramref name="maxRadius"/> pixels big.</summary>
        public static SKBitmap ColorHalftone(SKBitmap source, float maxRadius)
        {
            int w = source.Width, h = source.Height;
            var radius = Math.Clamp(maxRadius, 2, 64);
            var cell = radius * 2;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var angles = new[] { 108f, 162f, 90f };   // Photoshop's default screen angles for C, M, Y
            var sines = new float[3];
            var cosines = new float[3];
            for (var c = 0; c < 3; c++) (sines[c], cosines[c]) = MathF.SinCos(angles[c] * MathF.PI / 180);
            Parallel.For(0, h, y =>
            {
                Span<byte> ch = stackalloc byte[3];
                for (var x = 0; x < w; x++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        var sin = sines[c];
                        var cos = cosines[c];
                        // Into the rotated screen, find this pixel's cell middle, and back.
                        var u = x * cos + y * sin;
                        var v = -x * sin + y * cos;
                        var cu = (MathF.Floor(u / cell) + 0.5f) * cell;
                        var cv = (MathF.Floor(v / cell) + 0.5f) * cell;
                        var mx = Math.Clamp((int)(cu * cos - cv * sin), 0, w - 1);
                        var my = Math.Clamp((int)(cu * sin + cv * cos), 0, h - 1);
                        var sample = src[my * w + mx];
                        var value = c == 0 ? sample.Red : c == 1 ? sample.Green : sample.Blue;
                        var ink = 1 - value / 255f;
                        var dotRadius = radius * MathF.Sqrt(ink) * 1.3f;   // a full dot covers the cell
                        float du = u - cu, dv = v - cv;
                        ch[c] = du * du + dv * dv < dotRadius * dotRadius ? (byte)0 : (byte)255;
                    }
                    dst[y * w + x] = new SKColor(ch[0], ch[1], ch[2], src[y * w + x].Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Pixelate &gt; Mezzotint: every pixel and channel turned fully on or off
        /// at random, more often on where it was brighter - a grainy engraved look.</summary>
        public static SKBitmap Mezzotint(SKBitmap source)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    byte B(byte v, int s) => v / 255f > Noise(x, y, s) ? (byte)255 : (byte)0;
                    dst[y * w + x] = new SKColor(B(c.Red, 11), B(c.Green, 12), B(c.Blue, 13), c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Other &gt; Maximum (<paramref name="grow"/>: light areas spread) or
        /// Minimum (dark areas spread), over a square <paramref name="radius"/> pixels wide.</summary>
        public static SKBitmap MaximumMinimum(SKBitmap source, int radius, bool grow)
        {
            radius = Math.Clamp(radius, 1, 50);
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var pass = new SKColor[src.Length];
            var dst = new SKColor[src.Length];
            SKColor Pick(SKColor a, SKColor b) => grow
                ? new SKColor(Math.Max(a.Red, b.Red), Math.Max(a.Green, b.Green), Math.Max(a.Blue, b.Blue), Math.Max(a.Alpha, b.Alpha))
                : new SKColor(Math.Min(a.Red, b.Red), Math.Min(a.Green, b.Green), Math.Min(a.Blue, b.Blue), Math.Min(a.Alpha, b.Alpha));
            // A square is a row pass then a column pass, each in time independent of the
            // radius (van Herk / Gil-Werman: block-wise running max from both sides).
            Parallel.For(0, h, () => (new SKColor[w], new SKColor[w], new SKColor[w], new SKColor[w]), (y, _, buffers) =>
            {
                var (line, g, hh, output) = buffers;
                for (var x = 0; x < w; x++) line[x] = src[y * w + x];
                Window(line, w, radius, Pick, g, hh, output);
                for (var x = 0; x < w; x++) pass[y * w + x] = output[x];
                return buffers;
            }, _ => { });
            Parallel.For(0, w, () => (new SKColor[h], new SKColor[h], new SKColor[h], new SKColor[h]), (x, _, buffers) =>
            {
                var (line, g, hh, output) = buffers;
                for (var y = 0; y < h; y++) line[y] = pass[y * w + x];
                Window(line, h, radius, Pick, g, hh, output);
                for (var y = 0; y < h; y++) dst[y * w + x] = output[y];
                return buffers;
            }, _ => { });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>output[i] = pick over line[i - r .. i + r] (clamped to the line).</summary>
        private static void Window(SKColor[] line, int n, int r, Func<SKColor, SKColor, SKColor> pick, SKColor[] g, SKColor[] h, SKColor[] output)
        {
            var k = 2 * r + 1;
            for (var i = 0; i < n; i++) g[i] = i % k == 0 ? line[i] : pick(g[i - 1], line[i]);
            for (var i = n - 1; i >= 0; i--) h[i] = i == n - 1 || (i + 1) % k == 0 ? line[i] : pick(h[i + 1], line[i]);
            for (var i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - r), b = Math.Min(n - 1, i + r);
                // [a, b] spans at most two blocks: the tail of a's block and the head of b's.
                // Both in one block only at the ends of the line: from the block's start
                // (g is exact) or up to the line's end (h is exact, it restarts there).
                output[i] = a / k != b / k ? pick(h[a], g[b]) : a % k == 0 ? g[b] : h[a];
            }
        }

        /// <summary>Other &gt; Offset: the picture slid by (<paramref name="dx"/>, <paramref name="dy"/>)
        /// pixels; what leaves one edge comes back in at the other (for seamless tiles).</summary>
        public static SKBitmap Offset(SKBitmap source, int dx, int dy)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            dx = ((dx % w) + w) % w;
            dy = ((dy % h) + h) % h;
            Parallel.For(0, h, y =>
            {
                var sy = (y - dy + h) % h;
                for (var x = 0; x < w; x++) dst[y * w + x] = src[sy * w + (x - dx + w) % w];
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Stylize &gt; Tiles: the picture cut into <paramref name="count"/> tiles
        /// across, each nudged a little; the gaps show the picture as it was.</summary>
        public static SKBitmap Tiles(SKBitmap source, int count)
        {
            int w = source.Width, h = source.Height;
            count = Math.Clamp(count, 1, 99);
            var size = Math.Max(2, w / count);
            var shift = Math.Max(1, size / 10);
            var result = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType));
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
            using var edge = new SKPaint { Color = new SKColor(0, 0, 0, 60), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            for (var ty = 0; ty * size < h; ty++)
                for (var tx = 0; tx * size < w; tx++)
                {
                    var r = SKRect.Create(tx * size, ty * size, Math.Min(size, w - tx * size), Math.Min(size, h - ty * size));
                    var ox = (Noise(tx, ty, 21) * 2 - 1) * shift;
                    var oy = (Noise(tx, ty, 22) * 2 - 1) * shift;
                    var target = r;
                    target.Offset(ox, oy);
                    canvas.DrawBitmap(source, r, target);
                    canvas.DrawRect(target, edge);
                }
            return result;
        }

        /// <summary>Blur &gt; Average: the whole picture (or selection) filled with its average color.</summary>
        public static SKBitmap Average(SKBitmap source)
        {
            var src = source.Pixels;
            long r = 0, g = 0, b = 0, a = 0;
            foreach (var c in src) { r += c.Red * c.Alpha; g += c.Green * c.Alpha; b += c.Blue * c.Alpha; a += c.Alpha; }
            var average = a == 0 ? SKColors.Transparent : new SKColor((byte)(r / a), (byte)(g / a), (byte)(b / a));
            var dst = new SKColor[src.Length];
            for (var i = 0; i < src.Length; i++) dst[i] = average.WithAlpha(src[i].Alpha);
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Noise &gt; Despeckle: softens grain and specks but keeps edges (a small
        /// edge-aware blur).</summary>
        public static SKBitmap Despeckle(SKBitmap source) => DistortFilters.SurfaceBlur(source, 2, 24);
    }
}
