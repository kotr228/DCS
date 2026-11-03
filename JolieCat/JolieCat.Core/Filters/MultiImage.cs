using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Working with several shots of one scene: Auto-Align (find how far each shot is
    /// shifted from the first) and exposure fusion (Merge to HDR: blend bracketed
    /// exposures into one image with detail in both shadows and highlights).
    /// </summary>
    public static class MultiImage
    {
        // ================= Auto-Align =================

        /// <summary>
        /// How far (in pixels) <paramref name="moving"/> must be shifted to line up with
        /// <paramref name="reference"/>. Compares brightness normalized per image (so
        /// differently exposed shots align too), coarse to fine; searches up to
        /// <paramref name="maxShift"/> of the image size (0..0.5).
        /// </summary>
        public static SKPointI FindOffset(SKBitmap reference, SKBitmap moving, float maxShift = 0.25f)
        {
            var a = Gray.From(reference);
            var b = Gray.From(moving);
            var levelsA = new List<Gray> { a };
            var levelsB = new List<Gray> { b };
            while (levelsA[^1].Width > 96 || levelsA[^1].Height > 96)
            {
                levelsA.Add(levelsA[^1].Half());
                levelsB.Add(levelsB[^1].Half());
            }

            var top = levelsA.Count - 1;
            var reach = Math.Max(2, (int)(Math.Max(levelsA[top].Width, levelsA[top].Height) * Math.Clamp(maxShift, 0.01f, 0.5f)));
            int dx = 0, dy = 0;
            for (var level = top; level >= 0; level--)
            {
                var range = level == top ? reach : 2;
                var (bx, by) = (dx, dy);
                var best = double.MaxValue;
                for (var oy = dy - range; oy <= dy + range; oy++)
                    for (var ox = dx - range; ox <= dx + range; ox++)
                    {
                        var cost = Difference(levelsA[level], levelsB[level], ox, oy);
                        if (cost < best) { best = cost; bx = ox; by = oy; }
                    }
                (dx, dy) = (bx, by);
                if (level > 0) { dx *= 2; dy *= 2; }
            }
            return new SKPointI(dx, dy);
        }

        /// <summary>Mean absolute difference where reference(x, y) meets moving(x - dx, y - dy).</summary>
        private static double Difference(Gray a, Gray b, int dx, int dy)
        {
            double sum = 0;
            long count = 0;
            var step = Math.Max(1, Math.Min(a.Width, a.Height) / 160);
            for (var y = Math.Max(0, dy); y < Math.Min(a.Height, b.Height + dy); y += step)
                for (var x = Math.Max(0, dx); x < Math.Min(a.Width, b.Width + dx); x += step)
                {
                    var va = a.Values[y * a.Width + x];
                    var vb = b.Values[(y - dy) * b.Width + (x - dx)];
                    if (float.IsNaN(va) || float.IsNaN(vb)) continue;
                    sum += Math.Abs(va - vb);
                    count++;
                }
            // Too little overlap isn't a real match.
            return count < (long)a.Width * a.Height / (step * step) / 4 ? double.MaxValue : sum / count;
        }

        /// <summary>Normalized brightness (mean 0, spread 1); NaN where transparent.</summary>
        private sealed class Gray
        {
            public int Width;
            public int Height;
            public float[] Values = Array.Empty<float>();

            public static Gray From(SKBitmap bitmap)
            {
                var g = new Gray { Width = bitmap.Width, Height = bitmap.Height, Values = new float[bitmap.Width * bitmap.Height] };
                var pixels = bitmap.Pixels;
                double sum = 0, sq = 0;
                long n = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    if (c.Alpha < 128) { g.Values[i] = float.NaN; continue; }
                    var v = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
                    g.Values[i] = v;
                    sum += v; sq += v * v; n++;
                }
                if (n == 0) return g;
                var mean = sum / n;
                var spread = Math.Max(1, Math.Sqrt(Math.Max(0, sq / n - mean * mean)));
                for (var i = 0; i < g.Values.Length; i++)
                    if (!float.IsNaN(g.Values[i])) g.Values[i] = (float)((g.Values[i] - mean) / spread);
                return g;
            }

            public Gray Half()
            {
                int w = Math.Max(1, Width / 2), h = Math.Max(1, Height / 2);
                var half = new Gray { Width = w, Height = h, Values = new float[w * h] };
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        float s = 0; var n = 0;
                        for (var k = 0; k < 4; k++)
                        {
                            var sx = Math.Min(Width - 1, x * 2 + (k & 1));
                            var sy = Math.Min(Height - 1, y * 2 + (k >> 1));
                            var v = Values[sy * Width + sx];
                            if (float.IsNaN(v)) continue;
                            s += v; n++;
                        }
                        half.Values[y * w + x] = n == 0 ? float.NaN : s / n;
                    }
                return half;
            }
        }

        // ================= Exposure fusion =================

        /// <summary>
        /// Merge to HDR (exposure fusion): every pixel takes most from the shots where it
        /// is well exposed, colorful and detailed; the shots are blended band by band
        /// (Laplacian pyramids) so the result has no seams or halos. All images must have
        /// the same size.
        /// </summary>
        public static SKBitmap FuseExposures(IReadOnlyList<SKBitmap> images)
        {
            if (images.Count == 0) throw new ArgumentException("No images.", nameof(images));
            int w = images[0].Width, h = images[0].Height;
            if (images.Count == 1) return images[0].Copy();

            // Weights.
            var colors = new Plane[images.Count][];
            var weights = new Plane[images.Count];
            for (var k = 0; k < images.Count; k++)
            {
                var px = images[k].Pixels;
                var r = new Plane(w, h); var g = new Plane(w, h); var b = new Plane(w, h);
                var gray = new float[w * h];
                for (var i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    r.V[i] = c.Red / 255f; g.V[i] = c.Green / 255f; b.V[i] = c.Blue / 255f;
                    gray[i] = 0.299f * r.V[i] + 0.587f * g.V[i] + 0.114f * b.V[i];
                }
                colors[k] = new[] { r, g, b };

                var weight = new Plane(w, h);
                Parallel.For(0, h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var i = y * w + x;
                        float Gr(int xx, int yy) => gray[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)];
                        var contrast = Math.Abs(4 * gray[i] - Gr(x - 1, y) - Gr(x + 1, y) - Gr(x, y - 1) - Gr(x, y + 1));
                        float rr = r.V[i], gg = g.V[i], bb = b.V[i];
                        var mean = (rr + gg + bb) / 3;
                        var saturation = MathF.Sqrt(((rr - mean) * (rr - mean) + (gg - mean) * (gg - mean) + (bb - mean) * (bb - mean)) / 3);
                        static float Good(float v) => MathF.Exp(-(v - 0.5f) * (v - 0.5f) / (2 * 0.2f * 0.2f));
                        var exposure = Good(rr) * Good(gg) * Good(bb);
                        weight.V[i] = (contrast + 0.02f) * (saturation + 0.02f) * exposure + 1e-6f;
                    }
                });
                weights[k] = weight;
            }

            for (var i = 0; i < w * h; i++)
            {
                float sum = 0;
                for (var k = 0; k < images.Count; k++) sum += weights[k].V[i];
                for (var k = 0; k < images.Count; k++) weights[k].V[i] /= sum;
            }

            // Pyramid blend.
            var levels = 1;
            for (int s = Math.Min(w, h); s > 16 && levels < 9; s /= 2) levels++;
            var result = new Plane[3][];
            Parallel.For(0, 3, ch =>
            {
                Plane[]? blended = null;
                for (var k = 0; k < images.Count; k++)
                {
                    var lap = Laplacian(colors[k][ch], levels);
                    var gw = GaussianPyramid(weights[k], levels);
                    blended ??= new Plane[levels];
                    for (var l = 0; l < levels; l++)
                    {
                        blended[l] ??= new Plane(lap[l].W, lap[l].H);
                        var dst = blended[l].V; var a = lap[l].V; var wt = gw[l].V;
                        for (var i = 0; i < dst.Length; i++) dst[i] += a[i] * wt[i];
                    }
                }
                result[ch] = blended!;
            });

            var output = new SKColor[w * h];
            var planes = new Plane[3];
            for (var ch = 0; ch < 3; ch++) planes[ch] = Collapse(result[ch]);
            var alpha = images[0].Pixels;
            for (var i = 0; i < output.Length; i++)
                output[i] = new SKColor(ToByte(planes[0].V[i]), ToByte(planes[1].V[i]), ToByte(planes[2].V[i]), alpha[i].Alpha);
            return EnhanceFilters.FromPixels(images[0], output);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);

        private sealed class Plane
        {
            public readonly int W, H;
            public readonly float[] V;
            public Plane(int w, int h) { W = w; H = h; V = new float[w * h]; }
        }

        private static readonly float[] Kernel = { 1 / 16f, 4 / 16f, 6 / 16f, 4 / 16f, 1 / 16f };

        private static Plane Blur(Plane p)
        {
            var tmp = new Plane(p.W, p.H);
            var outp = new Plane(p.W, p.H);
            Parallel.For(0, p.H, y =>
            {
                for (var x = 0; x < p.W; x++)
                {
                    float s = 0;
                    for (var k = -2; k <= 2; k++) s += Kernel[k + 2] * p.V[y * p.W + Math.Clamp(x + k, 0, p.W - 1)];
                    tmp.V[y * p.W + x] = s;
                }
            });
            Parallel.For(0, p.H, y =>
            {
                for (var x = 0; x < p.W; x++)
                {
                    float s = 0;
                    for (var k = -2; k <= 2; k++) s += Kernel[k + 2] * tmp.V[Math.Clamp(y + k, 0, p.H - 1) * p.W + x];
                    outp.V[y * p.W + x] = s;
                }
            });
            return outp;
        }

        private static Plane Down(Plane p)
        {
            var blurred = Blur(p);
            var d = new Plane((p.W + 1) / 2, (p.H + 1) / 2);
            for (var y = 0; y < d.H; y++)
                for (var x = 0; x < d.W; x++)
                    d.V[y * d.W + x] = blurred.V[Math.Min(p.H - 1, y * 2) * p.W + Math.Min(p.W - 1, x * 2)];
            return d;
        }

        /// <summary>Back up to <paramref name="w"/> x <paramref name="h"/> (bilinear).</summary>
        private static Plane Up(Plane p, int w, int h)
        {
            var u = new Plane(w, h);
            Parallel.For(0, h, y =>
            {
                var fy = Math.Clamp((y - 0.5f) / 2f + 0.25f, 0, p.H - 1);
                var y0 = (int)fy; var y1 = Math.Min(p.H - 1, y0 + 1); var ty = fy - y0;
                for (var x = 0; x < w; x++)
                {
                    var fx = Math.Clamp((x - 0.5f) / 2f + 0.25f, 0, p.W - 1);
                    var x0 = (int)fx; var x1 = Math.Min(p.W - 1, x0 + 1); var tx = fx - x0;
                    var top = p.V[y0 * p.W + x0] * (1 - tx) + p.V[y0 * p.W + x1] * tx;
                    var bottom = p.V[y1 * p.W + x0] * (1 - tx) + p.V[y1 * p.W + x1] * tx;
                    u.V[y * w + x] = top * (1 - ty) + bottom * ty;
                }
            });
            return u;
        }

        private static Plane[] GaussianPyramid(Plane p, int levels)
        {
            var pyramid = new Plane[levels];
            pyramid[0] = p;
            for (var l = 1; l < levels; l++) pyramid[l] = Down(pyramid[l - 1]);
            return pyramid;
        }

        private static Plane[] Laplacian(Plane p, int levels)
        {
            var gauss = GaussianPyramid(p, levels);
            var lap = new Plane[levels];
            for (var l = 0; l < levels - 1; l++)
            {
                var up = Up(gauss[l + 1], gauss[l].W, gauss[l].H);
                var band = new Plane(gauss[l].W, gauss[l].H);
                for (var i = 0; i < band.V.Length; i++) band.V[i] = gauss[l].V[i] - up.V[i];
                lap[l] = band;
            }
            lap[levels - 1] = gauss[levels - 1];
            return lap;
        }

        private static Plane Collapse(Plane[] lap)
        {
            var current = lap[^1];
            for (var l = lap.Length - 2; l >= 0; l--)
            {
                var up = Up(current, lap[l].W, lap[l].H);
                for (var i = 0; i < up.V.Length; i++) up.V[i] += lap[l].V[i];
                current = up;
            }
            return current;
        }
    }
}
