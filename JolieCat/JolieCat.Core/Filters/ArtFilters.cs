using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Spherize, Ripple, Oil Paint, Smart Sharpen, Clouds, Lens Flare and Lens Correction.
    /// </summary>
    public static class ArtFilters
    {
        /// <summary>Wraps the image around a ball (positive amount, the middle bulges
        /// out) or the inside of one (negative), within the ellipse that fills the image.</summary>
        public static SKBitmap Spherize(SKBitmap source, float amount)
        {
            float cx = source.Width / 2f, cy = source.Height / 2f;
            var t = Math.Clamp(amount, -100, 100) / 100f;
            var power = 1 + 0.8f * t;
            return DistortFilters.Remap(source, (x, y) =>
            {
                float nx = (x - cx) / cx, ny = (y - cy) / cy;
                var r = MathF.Sqrt(nx * nx + ny * ny);
                if (r >= 1 || r < 1e-4f) return (x, y);
                var factor = MathF.Pow(r, power - 1);
                return (cx + nx * factor * cx, cy + ny * factor * cy);
            });
        }

        /// <summary>Small, regular ripples like the surface of a pond.</summary>
        /// <param name="amount">-999..999: how far the ripples push pixels (sign flips them).</param>
        /// <param name="size">Wavelength of the ripples in pixels.</param>
        public static SKBitmap Ripple(SKBitmap source, float amount, float size)
        {
            var wavelength = Math.Max(2f, size);
            var a = Math.Clamp(amount, -999, 999) / 100f * wavelength * 0.12f;
            var k = 2 * MathF.PI / wavelength;
            return DistortFilters.Remap(source, (x, y) =>
                (x + a * MathF.Sin(y * k + 0.7f * MathF.Sin(x * k * 0.37f)),
                 y + a * MathF.Sin(x * k + 0.7f * MathF.Sin(y * k * 0.41f))));
        }

        /// <summary>
        /// Oil painting: each pixel takes the average color of the most common brightness
        /// band in its neighborhood, which turns detail into flat, brush-like patches.
        /// </summary>
        /// <param name="radius">Brush size, 1..12 px.</param>
        /// <param name="levels">How many brightness bands (fewer = bolder strokes), 4..64.</param>
        public static SKBitmap OilPaint(SKBitmap source, int radius, int levels)
        {
            radius = Math.Clamp(radius, 1, 12);
            levels = Math.Clamp(levels, 4, 64);
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var band = new byte[src.Length];
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                band[i] = (byte)Math.Min(levels - 1, (c.Red + c.Green + c.Blue) / 3 * levels / 256);
            }

            Parallel.For(0, h, y =>
            {
                var count = new int[levels];
                var sumR = new int[levels];
                var sumG = new int[levels];
                var sumB = new int[levels];
                int y0 = Math.Max(0, y - radius), y1 = Math.Min(h - 1, y + radius);

                void Column(int x, int sign)
                {
                    if (x < 0 || x >= w) return;
                    for (var yy = y0; yy <= y1; yy++)
                    {
                        var i = yy * w + x;
                        var c = src[i];
                        if (c.Alpha == 0) continue;
                        var b = band[i];
                        count[b] += sign; sumR[b] += sign * c.Red; sumG[b] += sign * c.Green; sumB[b] += sign * c.Blue;
                    }
                }

                for (var x = -radius; x < radius; x++) Column(x, +1);
                for (var x = 0; x < w; x++)
                {
                    Column(x + radius, +1);
                    var best = 0;
                    for (var b = 1; b < levels; b++) if (count[b] > count[best]) best = b;
                    var i = y * w + x;
                    var n = count[best];
                    dst[i] = n == 0 || src[i].Alpha == 0
                        ? src[i]
                        : new SKColor((byte)(sumR[best] / n), (byte)(sumG[best] / n), (byte)(sumB[best] / n), src[i].Alpha);
                    Column(x - radius, -1);
                }
            });

            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>
        /// Smart Sharpen: sharpens brightness only (no color fringes), and leaves
        /// differences smaller than a noise threshold alone (so grain isn't sharpened).
        /// </summary>
        /// <param name="amountPercent">0..500.</param>
        /// <param name="radius">0.1..64 px.</param>
        public static SKBitmap SmartSharpen(SKBitmap source, float amountPercent, float radius)
        {
            var amount = Math.Clamp(amountPercent, 0, 500) / 100f;
            using var blurred = BlurFilter.Apply(source, BlurType.Gaussian, Math.Clamp(radius, 0.1f, 64f));
            var src = source.Pixels;
            var soft = blurred.Pixels;
            var dst = new SKColor[src.Length];
            const float noise = 3f;

            Parallel.For(0, source.Height, y =>
            {
                for (var i = y * source.Width; i < (y + 1) * source.Width; i++)
                {
                    var c = src[i];
                    if (c.Alpha == 0) { dst[i] = c; continue; }
                    var b = soft[i];
                    var detail = (0.299f * (c.Red - b.Red) + 0.587f * (c.Green - b.Green) + 0.114f * (c.Blue - b.Blue));
                    var magnitude = MathF.Abs(detail);
                    var gate = Math.Clamp((magnitude - noise) / noise, 0, 1);
                    var delta = detail * amount * gate;
                    dst[i] = new SKColor(Clamp(c.Red + delta), Clamp(c.Green + delta), Clamp(c.Blue + delta), c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Soft cloud pattern between two colors (fractal noise), filling the
        /// whole layer. The same seed gives the same clouds.</summary>
        public static SKBitmap Clouds(SKBitmap source, SKColor foreground, SKColor background, int seed)
        {
            int w = source.Width, h = source.Height;
            var random = new Random(seed);
            const int grid = 256;
            var lattice = new float[grid * grid];
            for (var i = 0; i < lattice.Length; i++) lattice[i] = (float)random.NextDouble();
            float ox = (float)random.NextDouble() * grid, oy = (float)random.NextDouble() * grid;

            float Noise(float x, float y)
            {
                var xi = (int)MathF.Floor(x);
                var yi = (int)MathF.Floor(y);
                float fx = x - xi, fy = y - yi;
                fx = fx * fx * (3 - 2 * fx);
                fy = fy * fy * (3 - 2 * fy);
                float L(int a, int b) => lattice[((b & (grid - 1)) * grid) + (a & (grid - 1))];
                var top = L(xi, yi) + (L(xi + 1, yi) - L(xi, yi)) * fx;
                var bottom = L(xi, yi + 1) + (L(xi + 1, yi + 1) - L(xi, yi + 1)) * fx;
                return top + (bottom - top) * fy;
            }

            var scale = 4f / Math.Max(w, h) * 2;
            var dst = new SKColor[w * h];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float value = 0, weight = 0.5f, total = 0, f = 1;
                    for (var octave = 0; octave < 7; octave++)
                    {
                        value += Noise(ox + x * scale * f, oy + y * scale * f) * weight;
                        total += weight;
                        weight *= 0.5f;
                        f *= 2;
                    }
                    var t = Math.Clamp((value / total - 0.5f) * 1.8f + 0.5f, 0, 1);
                    dst[y * w + x] = new SKColor(
                        (byte)(foreground.Red + (background.Red - foreground.Red) * t),
                        (byte)(foreground.Green + (background.Green - foreground.Green) * t),
                        (byte)(foreground.Blue + (background.Blue - foreground.Blue) * t),
                        255);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>The light of a bright source shining into a camera lens: a glow at
        /// (<paramref name="centerX"/>, <paramref name="centerY"/>) (0..1 of the image),
        /// a halo ring and colored reflections along the line through the image's center.</summary>
        /// <param name="brightness">10..300 %.</param>
        /// <param name="lens">0 = 50-300mm zoom, 1 = 35mm prime, 2 = 105mm prime.</param>
        public static SKBitmap LensFlare(SKBitmap source, float centerX, float centerY, float brightness, int lens)
        {
            var result = source.Copy();
            int w = source.Width, h = source.Height;
            var size = Math.Max(w, h);
            var strength = Math.Clamp(brightness, 10, 300) / 100f;
            var c = new SKPoint(centerX * w, centerY * h);
            var mid = new SKPoint(w / 2f, h / 2f);

            using var canvas = new SKCanvas(result);
            void Glow(SKPoint at, float radius, SKColor color, float alpha, bool ring = false)
            {
                var a = (byte)Math.Clamp(alpha * strength * 255, 0, 255);
                var colors = ring
                    ? new[] { color.WithAlpha(0), color.WithAlpha(0), color.WithAlpha(a), color.WithAlpha(0) }
                    : new[] { color.WithAlpha(a), color.WithAlpha((byte)(a / 3)), color.WithAlpha(0) };
                var stops = ring ? new[] { 0f, 0.82f, 0.9f, 1f } : new[] { 0f, 0.35f, 1f };
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    BlendMode = SKBlendMode.Screen,
                    Shader = SKShader.CreateRadialGradient(at, radius, colors, stops, SKShaderTileMode.Clamp),
                };
                canvas.DrawCircle(at, radius, paint);
            }

            // Hot center and broad glow.
            Glow(c, size * 0.04f, SKColors.White, 1f);
            Glow(c, size * (lens == 2 ? 0.12f : 0.2f), new SKColor(255, 220, 170), 0.55f);
            if (lens != 2) Glow(c, size * 0.16f, new SKColor(255, 200, 150), 0.35f, ring: true);

            // Streak.
            using (var streak = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Screen,
                Shader = SKShader.CreateLinearGradient(new SKPoint(c.X - size * 0.35f, c.Y), new SKPoint(c.X + size * 0.35f, c.Y),
                    new[] { SKColors.Transparent, new SKColor(255, 230, 200, (byte)Math.Clamp(160 * strength, 0, 255)), SKColors.Transparent },
                    null, SKShaderTileMode.Clamp),
            })
                canvas.DrawRect(c.X - size * 0.35f, c.Y - size * 0.003f, size * 0.7f, size * 0.006f, streak);

            // Reflections along the line from the source through the center.
            var ghosts = lens switch
            {
                1 => new (float T, float R, SKColor Color)[] { (0.5f, 0.02f, new(120, 255, 160)), (1.3f, 0.05f, new(140, 160, 255)), (1.8f, 0.03f, new(255, 140, 120)) },
                2 => new (float T, float R, SKColor Color)[] { (0.7f, 0.015f, new(180, 200, 255)), (1.5f, 0.035f, new(255, 200, 140)) },
                _ => new (float T, float R, SKColor Color)[]
                {
                    (0.35f, 0.012f, new(255, 240, 160)), (0.8f, 0.03f, new(140, 255, 180)), (1.2f, 0.018f, new(160, 180, 255)),
                    (1.55f, 0.06f, new(120, 140, 255)), (2.0f, 0.025f, new(255, 150, 120)),
                },
            };
            foreach (var (t, r, color) in ghosts)
            {
                var at = new SKPoint(c.X + (mid.X - c.X) * t, c.Y + (mid.Y - c.Y) * t);
                Glow(at, size * r, color, 0.55f);
            }

            canvas.Flush();
            // Keep the layer's transparency: the flare only lights up existing pixels.
            var before = source.Pixels;
            var after = result.Pixels;
            for (var i = 0; i < after.Length; i++)
                if (before[i].Alpha < 255) after[i] = after[i].WithAlpha(before[i].Alpha);
            result.Pixels = after;
            return result;
        }

        /// <summary>
        /// Lens Correction: straightens barrel (positive) or pincushion (negative)
        /// distortion, shifts the red and blue channels to remove color fringes at the
        /// edges, and lightens (positive) or darkens (negative) the corners.
        /// </summary>
        public static SKBitmap LensCorrection(SKBitmap source, float distortion, float redCyan, float blueYellow, float vignette, float midpoint)
        {
            int w = source.Width, h = source.Height;
            float cx = w / 2f, cy = h / 2f;
            var diag = MathF.Sqrt(cx * cx + cy * cy);
            var k = -Math.Clamp(distortion, -100, 100) / 100f * 0.35f;
            var redScale = 1 + Math.Clamp(redCyan, -100, 100) / 100f * 0.01f;
            var blueScale = 1 + Math.Clamp(blueYellow, -100, 100) / 100f * 0.01f;
            var v = Math.Clamp(vignette, -100, 100) / 100f;
            var mid = Math.Clamp(midpoint, 0, 100) / 100f;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];

            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    var r = MathF.Sqrt(dx * dx + dy * dy) / diag;
                    var f = 1 + k * r * r;
                    var g = DistortFilters.Sample(src, w, h, cx + dx * f - 0.5f, cy + dy * f - 0.5f);
                    var red = redScale == 1 ? g : DistortFilters.Sample(src, w, h, cx + dx * f * redScale - 0.5f, cy + dy * f * redScale - 0.5f);
                    var blue = blueScale == 1 ? g : DistortFilters.Sample(src, w, h, cx + dx * f * blueScale - 0.5f, cy + dy * f * blueScale - 0.5f);

                    var gain = 1f;
                    if (v != 0)
                    {
                        var start = mid * 0.9f;
                        var t = Math.Clamp((r - start) / Math.Max(0.05f, 1 - start), 0, 1);
                        gain = 1 + v * t * t;
                    }
                    dst[y * w + x] = new SKColor(Clamp(red.Red * gain), Clamp(g.Green * gain), Clamp(blue.Blue * gain), g.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>
        /// Lighting Effects: lights the image with a spot (an oval pool of light) or an
        /// omni light (a round one), over a dimmer ambient light. With
        /// <paramref name="texture"/> above 0, the image's own brightness acts as a
        /// relief map, so its details catch the light like embossed paper.
        /// </summary>
        /// <param name="kind">0 = spot, 1 = omni, 2 = infinite (even light from one side).</param>
        /// <param name="centerX">0..1 of the width.</param>
        /// <param name="centerY">0..1 of the height.</param>
        /// <param name="radius">0..1 of the image's larger side.</param>
        /// <param name="intensity">0..200 %.</param>
        /// <param name="ambient">0..100 %: how bright it is outside the light.</param>
        /// <param name="texture">0..100: relief height.</param>
        public static SKBitmap LightingEffects(SKBitmap source, int kind, float centerX, float centerY, float radius,
            float intensity, float ambient, SKColor light, float texture)
        {
            int w = source.Width, h = source.Height;
            var size = Math.Max(w, h);
            float cx = centerX * w, cy = centerY * h;
            var reach = Math.Max(1f, radius * size);
            var gain = Math.Clamp(intensity, 0, 200) / 100f;
            var floor = Math.Clamp(ambient, 0, 100) / 100f;
            var relief = Math.Clamp(texture, 0, 100) / 100f * 4f;
            float lr = light.Red / 255f, lg = light.Green / 255f, lb = light.Blue / 255f;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];

            float Height(int x, int y)
            {
                var c = src[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
                return (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) / 255f;
            }

            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    var c = src[i];
                    if (c.Alpha == 0) { dst[i] = c; continue; }

                    // How much light reaches this pixel.
                    float amount;
                    float lx, ly; // direction toward the light, on the image plane
                    if (kind == 2)
                    {
                        amount = 1;
                        lx = cx - w / 2f; ly = cy - h / 2f;
                    }
                    else
                    {
                        float dx = x - cx, dy = y - cy;
                        if (kind == 0) dy *= 1.6f; // spot: an oval pool, longer across
                        var d = MathF.Sqrt(dx * dx + dy * dy) / reach;
                        amount = Math.Clamp(1 - d * d, 0, 1);
                        lx = cx - x; ly = cy - y;
                    }

                    // Relief: tilt the surface by the brightness slope.
                    var shade = 1f;
                    if (relief > 0)
                    {
                        var gx = (Height(x + 1, y) - Height(x - 1, y)) * relief;
                        var gy = (Height(x, y + 1) - Height(x, y - 1)) * relief;
                        var len = MathF.Sqrt(lx * lx + ly * ly + reach * reach * 0.25f);
                        float nx = -gx, ny = -gy, nz = 1;
                        var nl = MathF.Sqrt(nx * nx + ny * ny + 1);
                        shade = Math.Clamp((nx * lx + ny * ly + nz * reach * 0.5f) / (nl * len), 0, 1.5f);
                    }

                    var lit = amount * gain * shade;
                    float fr = floor + lit * lr, fg = floor + lit * lg, fb = floor + lit * lb;
                    dst[i] = new SKColor(Clamp(c.Red * fr), Clamp(c.Green * fg), Clamp(c.Blue * fb), c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    }
}
