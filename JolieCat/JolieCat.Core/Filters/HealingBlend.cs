using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// "Healing": pastes <c>source</c> into a region of <c>target</c> so it keeps the
    /// source's texture but takes on the colors and brightness around the region's edge
    /// - no visible seam (Poisson / seamless cloning). The difference target - source
    /// along the edge is spread smoothly across the region (a solution of Laplace's
    /// equation) and added to the source.
    /// </summary>
    public static class HealingBlend
    {
        /// <param name="target">The image, width x height (not changed).</param>
        /// <param name="source">What to put into the region, same size as the image
        /// (only its pixels inside the region and on its edge are used).</param>
        /// <param name="region">Which pixels to replace.</param>
        /// <returns>The healed image.</returns>
        public static SKColor[] Blend(SKColor[] target, SKColor[] source, int width, int height, bool[] region, int iterations = 0)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(region);
            var n = width * height;
            if (target.Length != n || source.Length != n || region.Length != n) throw new ArgumentException("Sizes don't match.");

            var result = (SKColor[])target.Clone();

            // Bounding box of the region, so the work scales with its size, not the image's.
            int x0 = width, y0 = height, x1 = -1, y1 = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (region[y * width + x])
                    {
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                        y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    }
            if (x1 < 0) return result;

            // Work on the box plus a 1-pixel border (the edge values).
            var bx = Math.Max(0, x0 - 1);
            var by = Math.Max(0, y0 - 1);
            var bw = Math.Min(width - 1, x1 + 1) - bx + 1;
            var bh = Math.Min(height - 1, y1 + 1) - by + 1;

            // d = target - source, per channel (r, g, b, a). Fixed outside the region.
            var d = new float[bw * bh * 4];
            var inside = new bool[bw * bh];
            double sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            var edgeCount = 0;
            for (var y = 0; y < bh; y++)
                for (var x = 0; x < bw; x++)
                {
                    var i = (by + y) * width + bx + x;
                    var j = y * bw + x;
                    inside[j] = region[i];
                    if (inside[j]) continue;
                    var t = target[i];
                    var s = source[i];
                    d[j * 4] = t.Red - s.Red;
                    d[j * 4 + 1] = t.Green - s.Green;
                    d[j * 4 + 2] = t.Blue - s.Blue;
                    d[j * 4 + 3] = t.Alpha - s.Alpha;
                    sumR += d[j * 4]; sumG += d[j * 4 + 1]; sumB += d[j * 4 + 2]; sumA += d[j * 4 + 3];
                    edgeCount++;
                }

            // Start from the average edge difference: converges much faster.
            if (edgeCount > 0)
            {
                float mr = (float)(sumR / edgeCount), mg = (float)(sumG / edgeCount), mb = (float)(sumB / edgeCount), ma = (float)(sumA / edgeCount);
                for (var j = 0; j < inside.Length; j++)
                {
                    if (!inside[j]) continue;
                    d[j * 4] = mr; d[j * 4 + 1] = mg; d[j * 4 + 2] = mb; d[j * 4 + 3] = ma;
                }
            }

            // Red-black Gauss-Seidel with over-relaxation on Laplace's equation.
            if (iterations <= 0) iterations = Math.Clamp(Math.Max(bw, bh) * 3, 60, 1500);
            const float omega = 1.9f;
            for (var it = 0; it < iterations; it++)
            {
                for (var color = 0; color < 2; color++)
                {
                    var parity = color;
                    Parallel.For(0, bh, y =>
                    {
                        for (var x = (y + parity) % 2; x < bw; x += 2)
                        {
                            var j = y * bw + x;
                            if (!inside[j]) continue;
                            // Neighbours off the box behave like a mirror (no pull).
                            var l = x > 0 ? j - 1 : j;
                            var r = x < bw - 1 ? j + 1 : j;
                            var u = y > 0 ? j - bw : j;
                            var dn = y < bh - 1 ? j + bw : j;
                            for (var c = 0; c < 4; c++)
                            {
                                var avg = (d[l * 4 + c] + d[r * 4 + c] + d[u * 4 + c] + d[dn * 4 + c]) * 0.25f;
                                d[j * 4 + c] += omega * (avg - d[j * 4 + c]);
                            }
                        }
                    });
                }
            }

            for (var y = 0; y < bh; y++)
                for (var x = 0; x < bw; x++)
                {
                    var j = y * bw + x;
                    if (!inside[j]) continue;
                    var i = (by + y) * width + bx + x;
                    var s = source[i];
                    result[i] = new SKColor(
                        Clamp(s.Red + d[j * 4]), Clamp(s.Green + d[j * 4 + 1]),
                        Clamp(s.Blue + d[j * 4 + 2]), Clamp(s.Alpha + d[j * 4 + 3]));
                }

            return result;
        }

        private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    }
}
