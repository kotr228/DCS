using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Finding parts of a photo without AI: the sky (Select &gt; Sky, Sky Replacement)
    /// and the in-focus area (Select &gt; Focus Area). Masks are 0..255 per pixel.
    /// </summary>
    public static class SceneDetect
    {
        private static float[] Luma(SKColor[] px)
        {
            var l = new float[px.Length];
            for (var i = 0; i < px.Length; i++) l[i] = 0.299f * px[i].Red + 0.587f * px[i].Green + 0.114f * px[i].Blue;
            return l;
        }

        /// <summary>Box blur of a mask/value plane (two passes, running sums).</summary>
        internal static float[] BoxBlur(float[] v, int w, int h, int r)
        {
            if (r <= 0) return (float[])v.Clone();
            var tmp = new float[v.Length];
            var outp = new float[v.Length];
            Parallel.For(0, h, y =>
            {
                float sum = 0;
                for (var x = -r; x <= r; x++) sum += v[y * w + Math.Clamp(x, 0, w - 1)];
                for (var x = 0; x < w; x++)
                {
                    tmp[y * w + x] = sum / (2 * r + 1);
                    sum += v[y * w + Math.Min(w - 1, x + r + 1)] - v[y * w + Math.Max(0, x - r)];
                }
            });
            Parallel.For(0, w, x =>
            {
                float sum = 0;
                for (var y = -r; y <= r; y++) sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
                for (var y = 0; y < h; y++)
                {
                    outp[y * w + x] = sum / (2 * r + 1);
                    sum += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
                }
            });
            return outp;
        }

        /// <summary>
        /// The sky: smooth, bright or bluish pixels reachable from the top edge without
        /// crossing a strong edge. Returns a soft mask (255 = sky).
        /// </summary>
        public static byte[] SkyMask(SKBitmap image)
        {
            int w = image.Width, h = image.Height;
            var px = image.Pixels;
            var luma = Luma(px);

            // Local texture: average gradient size around each pixel.
            var grad = new float[px.Length];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    var gx = luma[y * w + Math.Min(w - 1, x + 1)] - luma[y * w + Math.Max(0, x - 1)];
                    var gy = luma[Math.Min(h - 1, y + 1) * w + x] - luma[Math.Max(0, y - 1) * w + x];
                    grad[i] = MathF.Abs(gx) + MathF.Abs(gy);
                }
            });
            var texture = BoxBlur(grad, w, h, Math.Max(1, Math.Max(w, h) / 300));

            bool SkyLike(int i)
            {
                var c = px[i];
                if (c.Alpha < 128 || texture[i] > 14) return false;
                var bluish = c.Blue >= c.Red && c.Blue > 90;
                var bright = luma[i] > 150;
                var warmSky = c.Red > 150 && c.Red >= c.Green && luma[i] > 110; // sunset
                return bluish || bright || warmSky;
            }

            // Flood from the top row, stepping only between similar colors.
            var sky = new bool[px.Length];
            var stack = new Stack<int>();
            for (var x = 0; x < w; x++)
                if (SkyLike(x)) { sky[x] = true; stack.Push(x); }
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                int x = i % w, y = i / w;
                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    var j = ny * w + nx;
                    if (sky[j] || !SkyLike(j)) return;
                    var a = px[i]; var b = px[j];
                    if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 30) return;
                    sky[j] = true;
                    stack.Push(j);
                }
                Visit(x + 1, y); Visit(x - 1, y); Visit(x, y + 1); Visit(x, y - 1);
            }

            // Near a sharp edge the texture test fails a few pixels early: let the sky
            // grow up to the edge over pixels of a similar color, a short distance only.
            var reach = Math.Max(1, Math.Max(w, h) / 300) * 2 + 3;
            var frontier = new List<int>();
            for (var i = 0; i < sky.Length; i++)
            {
                if (!sky[i]) continue;
                int x = i % w, y = i / w;
                if ((x > 0 && !sky[i - 1]) || (x < w - 1 && !sky[i + 1]) || (y > 0 && !sky[i - w]) || (y < h - 1 && !sky[i + w]))
                    frontier.Add(i);
            }
            for (var step = 0; step < reach && frontier.Count > 0; step++)
            {
                var next = new List<int>();
                foreach (var i in frontier)
                {
                    int x = i % w, y = i / w;
                    void Grow(int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                        var j = ny * w + nx;
                        if (sky[j] || px[j].Alpha < 128) return;
                        var a = px[i]; var b = px[j];
                        if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 45) return;
                        sky[j] = true;
                        next.Add(j);
                    }
                    Grow(x + 1, y); Grow(x - 1, y); Grow(x, y + 1); Grow(x, y - 1);
                }
                frontier = next;
            }

            // Fill small holes (birds, specks) and soften the edge a little.
            var soft = new float[px.Length];
            for (var i = 0; i < soft.Length; i++) soft[i] = sky[i] ? 1 : 0;
            var r = Math.Max(1, Math.Max(w, h) / 400);
            soft = BoxBlur(soft, w, h, r * 2);
            var mask = new byte[px.Length];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (byte)Math.Clamp((soft[i] - 0.35f) / 0.3f * 255, 0, 255);
            return mask;
        }

        /// <summary>
        /// The in-focus area: where the image has fine detail. <paramref name="sensitivity"/>
        /// 0..100: higher also takes in slightly softer parts. Returns 255 = in focus.
        /// </summary>
        public static byte[] FocusMask(SKBitmap image, float sensitivity)
        {
            int w = image.Width, h = image.Height;
            var luma = Luma(image.Pixels);
            var sharp = new float[luma.Length];
            Parallel.For(0, h, y =>
            {
                for (var x = 0; x < w; x++)
                {
                    float L(int xx, int yy) => luma[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)];
                    sharp[y * w + x] = MathF.Abs(4 * L(x, y) - L(x - 1, y) - L(x + 1, y) - L(x, y - 1) - L(x, y + 1));
                }
            });
            var r = Math.Max(2, Math.Max(w, h) / 60);
            var energy = BoxBlur(sharp, w, h, r);

            // Automatic threshold (Otsu) on the detail energy, eased by sensitivity.
            float max = 1e-3f;
            foreach (var e in energy) max = Math.Max(max, e);
            var hist = new int[256];
            foreach (var e in energy) hist[Math.Min(255, (int)(e / max * 255))]++;
            var total = energy.Length;
            double sumAll = 0;
            for (var t = 0; t < 256; t++) sumAll += t * (double)hist[t];
            double sumB = 0, wB = 0, best = -1;
            var threshold = 128;
            for (var t = 0; t < 256; t++)
            {
                wB += hist[t];
                if (wB == 0) continue;
                var wF = total - wB;
                if (wF == 0) break;
                sumB += t * (double)hist[t];
                var mB = sumB / wB; var mF = (sumAll - sumB) / wF;
                var between = wB * wF * (mB - mF) * (mB - mF);
                if (between > best) { best = between; threshold = t; }
            }
            var cut = threshold / 255f * max * (1.5f - Math.Clamp(sensitivity, 0, 100) / 100f);

            var on = new float[energy.Length];
            for (var i = 0; i < on.Length; i++) on[i] = energy[i] > cut ? 1 : 0;
            on = BoxBlur(on, w, h, r / 2 + 1); // close small gaps
            var mask = new byte[on.Length];
            for (var i = 0; i < mask.Length; i++) mask[i] = (byte)Math.Clamp((on[i] - 0.3f) / 0.4f * 255, 0, 255);
            return mask;
        }

        /// <summary>The built-in skies of Sky Replacement, by name.</summary>
        public static readonly string[] SkyPresets = { "Blue Sky", "Light Clouds", "Sunset", "Dusk", "Overcast" };

        /// <summary>A sky picture of the given size: a gradient, with clouds for some.</summary>
        public static SKBitmap RenderSky(string preset, int width, int height)
        {
            var (top, bottom, clouds) = preset switch
            {
                "Light Clouds" => (new SKColor(70, 130, 210), new SKColor(185, 215, 240), 0.45f),
                "Sunset" => (new SKColor(70, 60, 130), new SKColor(250, 150, 70), 0.25f),
                "Dusk" => (new SKColor(20, 30, 70), new SKColor(200, 110, 120), 0.15f),
                "Overcast" => (new SKColor(150, 155, 165), new SKColor(205, 208, 212), 0.6f),
                _ => (new SKColor(40, 110, 210), new SKColor(160, 205, 245), 0f),
            };
            var sky = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(sky))
            using (var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), new[] { top, bottom }, null, SKShaderTileMode.Clamp),
            })
                canvas.DrawRect(0, 0, width, height, paint);
            if (clouds <= 0) return sky;

            using var cloud = ArtFilters.Clouds(sky, SKColors.Black, SKColors.White, preset.Length * 31);
            var s = sky.Pixels;
            var c = cloud.Pixels;
            for (var i = 0; i < s.Length; i++)
            {
                var t = Math.Clamp((c[i].Red / 255f - 0.45f) * 2.2f, 0, 1) * clouds;
                var light = preset == "Sunset" || preset == "Dusk" ? new SKColor(255, 200, 170) : SKColors.White;
                s[i] = new SKColor((byte)(s[i].Red + (light.Red - s[i].Red) * t), (byte)(s[i].Green + (light.Green - s[i].Green) * t),
                    (byte)(s[i].Blue + (light.Blue - s[i].Blue) * t), 255);
            }
            sky.Pixels = s;
            return sky;
        }

        /// <summary>
        /// Sky Replacement: puts <paramref name="sky"/> (any size; it's scaled to cover)
        /// where the photo's sky is, with the edge moved by <paramref name="shiftEdge"/>
        /// (-50..50 px, positive grows the sky) and softened by <paramref name="fadeEdge"/>
        /// px, and tints the rest toward the new sky's color by <paramref name="lighting"/> (0..100).
        /// </summary>
        public static SKBitmap ReplaceSky(SKBitmap image, SKBitmap sky, float shiftEdge, float fadeEdge, float lighting)
        {
            int w = image.Width, h = image.Height;
            var mask = SkyMask(image);
            var m = new float[mask.Length];
            for (var i = 0; i < m.Length; i++) m[i] = mask[i] / 255f;

            // Shift: blur then re-threshold moves the edge in or out.
            var shift = (int)Math.Clamp(shiftEdge, -50, 50);
            if (shift != 0)
            {
                var b = BoxBlur(m, w, h, Math.Abs(shift));
                var level = shift > 0 ? 0.15f : 0.85f;
                for (var i = 0; i < m.Length; i++) m[i] = b[i] > level ? 1 : 0;
            }
            // The soft edge still holds some of the old sky's color: push the transition
            // onto the foreground side so no light halo of the old sky is left behind.
            for (var i = 0; i < m.Length; i++) m[i] = Math.Min(1, m[i] * 2.5f);
            var fade = (int)Math.Clamp(fadeEdge, 0, 100);
            if (fade > 0)
            {
                m = BoxBlur(m, w, h, fade);
                for (var i = 0; i < m.Length; i++) m[i] = Math.Min(1, m[i] * 1.6f);
            }

            using var scaled = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(scaled))
            using (var skyImage = SKImage.FromBitmap(sky))
            {
                var cover = Math.Max((float)w / sky.Width, (float)h / sky.Height);
                float sw = sky.Width * cover, sh = sky.Height * cover;
                canvas.DrawImage(skyImage, new SKRect((w - sw) / 2, 0, (w + sw) / 2, sh), new SKSamplingOptions(SKFilterMode.Linear));
            }

            var src = image.Pixels;
            var s = scaled.Pixels;
            // Foreground tint: the new sky's average color relative to gray.
            double sr = 0, sg = 0, sb = 0, n = 0;
            for (var i = 0; i < s.Length; i += 7) { sr += s[i].Red; sg += s[i].Green; sb += s[i].Blue; n++; }
            double avg = (sr + sg + sb) / (3 * n);
            var k = Math.Clamp(lighting, 0, 100) / 100f * 0.35f;
            float tr = (float)(1 + k * (sr / n / avg - 1)), tg = (float)(1 + k * (sg / n / avg - 1)), tb = (float)(1 + k * (sb / n / avg - 1));

            var dst = new SKColor[src.Length];
            Parallel.For(0, h, y =>
            {
                for (var i = y * w; i < (y + 1) * w; i++)
                {
                    var c = src[i];
                    var t = m[i] < 0.002f ? 0 : m[i];
                    if (t == 0 && k == 0) { dst[i] = c; continue; }
                    float fr = c.Red * tr, fg = c.Green * tg, fb = c.Blue * tb;
                    dst[i] = new SKColor(
                        (byte)Math.Clamp(MathF.Round(fr + (s[i].Red - fr) * t), 0, 255),
                        (byte)Math.Clamp(MathF.Round(fg + (s[i].Green - fg) * t), 0, 255),
                        (byte)Math.Clamp(MathF.Round(fb + (s[i].Blue - fb) * t), 0, 255),
                        c.Alpha);
                }
            });
            return EnhanceFilters.FromPixels(image, dst);
        }
    }
}
