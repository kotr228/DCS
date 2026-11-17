using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// Select &gt; Subject: guesses the main subject of a photo without any trained model.
    /// The colors along the picture's edges are taken as background and those in the
    /// middle as the subject; each pixel is then judged by which of the two its color is
    /// more typical of, a few times over (each time learning from the last answer and
    /// from its neighbours), and the biggest solid piece in the middle is kept, its holes
    /// filled. Works on a small copy for speed; the answer is scaled back up.
    /// </summary>
    public static class SubjectSelect
    {
        private const int Work = 256;          // longest side of the working copy
        private const int Bins = 16;           // color steps per channel in the histograms

        /// <summary>How clear the last answer was (0..0.5) and how much of the frame it took (0..1).</summary>
        public static double LastCertainty { get; private set; }
        public static double LastShare { get; private set; }

        /// <summary>Below this certainty nothing is said to stand out.</summary>
        internal const double MinCertainty = 0.12;

        /// <summary>How surely each pixel of <paramref name="image"/> belongs to the subject,
        /// 0..255 (128 or more: it does), row by row at full size; null if nothing stands out.</summary>
        public static byte[]? Find(SKBitmap image)
        {
            var scale = Math.Min(1f, Work / (float)Math.Max(image.Width, image.Height));
            int w = Math.Max(8, (int)(image.Width * scale)), h = Math.Max(8, (int)(image.Height * scale));
            using var small = image.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            var px = small.Pixels;
            var n = w * h;
            var bin = new int[n];
            for (var i = 0; i < n; i++)
            {
                var c = px[i];
                bin[i] = c.Alpha < 16 ? -1 : (c.Red * Bins / 256) * Bins * Bins + (c.Green * Bins / 256) * Bins + c.Blue * Bins / 256;
            }

            // Start: the outer band is background; a centered ellipse is probably subject.
            var border = Math.Max(2, Math.Min(w, h) / 20);
            var isBorder = new bool[n];
            var isBottom = new bool[n];
            var p = new float[n];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    // Top, left and right edges are background; the bottom one only leans
                    // that way (people and things are often cut off by the bottom of the frame).
                    isBorder[i] = x < border || y < border || x >= w - border;
                    isBottom[i] = !isBorder[i] && y >= h - border;
                    float dx = (x - w / 2f) / (w * 0.35f), dy = (y - h / 2f) / (h * 0.4f);
                    p[i] = isBorder[i] ? 0 : isBottom[i] ? 0.2f : dx * dx + dy * dy < 1 ? 0.8f : 0.3f;
                }

            var fg = new double[Bins * Bins * Bins];
            var bg = new double[Bins * Bins * Bins];
            for (var round = 0; round < 6; round++)
            {
                Array.Clear(fg);
                Array.Clear(bg);
                for (var i = 0; i < n; i++)
                {
                    if (bin[i] < 0) continue;
                    if (isBorder[i]) bg[bin[i]] += 2;          // the edges always count as background
                    else if (isBottom[i]) bg[bin[i]] += 1;
                    else { fg[bin[i]] += p[i]; bg[bin[i]] += 1 - p[i]; }
                }
                // Nearby shades count together: a background that brightens toward the middle
                // is still recognised by its edge colors.
                fg = Smooth(fg, 1);
                bg = Smooth(bg, 1);
                double fgTotal = 1e-9, bgTotal = 1e-9;
                foreach (var v in fg) fgTotal += v;
                foreach (var v in bg) bgTotal += v;

                // How typical each pixel's color is of the subject vs. the background.
                var q = new float[n];
                for (var i = 0; i < n; i++)
                {
                    if (bin[i] < 0) { q[i] = 0; continue; }
                    var f = fg[bin[i]] / fgTotal + 1e-6;
                    var b = bg[bin[i]] / bgTotal + 1e-6;
                    q[i] = (float)(f / (f + b));
                }
                // Neighbours agree: average over a small window (twice, for a smooth result).
                p = BoxBlur(BoxBlur(q, w, h, 2), w, h, 2);
                for (var i = 0; i < n; i++) if (isBorder[i]) p[i] = Math.Min(p[i], 0.2f);
            }

            // Does anything stand out at all? Colors judged near 50/50 everywhere, or a
            // "subject" filling nearly the whole frame, mean no.
            double certainty = 0;
            var share = 0;
            for (var i = 0; i < n; i++) { certainty += Math.Abs(p[i] - 0.5f); if (p[i] > 0.5f) share++; }
            LastCertainty = certainty / n;
            LastShare = share / (double)n;
            if (certainty / n < MinCertainty || share > n * 0.85) return null;

            var inside = new bool[n];
            for (var i = 0; i < n; i++) inside[i] = p[i] > 0.5f;
            if (!KeepMainPiece(inside, w, h)) return null;
            FillHoles(inside, w, h);

            // Back to full size: the smooth probabilities scaled up, cut at one half.
            var soft = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
            var bytes = new byte[n];
            for (var i = 0; i < n; i++) bytes[i] = inside[i] ? (byte)Math.Clamp(128 + (p[i] - 0.5f) * 255, 160, 255) : (byte)Math.Clamp(p[i] * 255, 0, 100);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, soft.GetPixels(), n);
            using (soft)
            using (var big = soft.Resize(new SKImageInfo(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear)))
            {
                var full = new byte[image.Width * image.Height];
                System.Runtime.InteropServices.Marshal.Copy(big.GetPixels(), full, 0, full.Length);
                var any = false;
                for (var i = 0; i < full.Length && !any; i++) any = full[i] >= 128;
                return any ? full : null;
            }
        }

        /// <summary>A color histogram spread to neighbouring bins (a box of radius
        /// <paramref name="r"/> in each channel).</summary>
        private static double[] Smooth(double[] hist, int r)
        {
            var result = hist;
            for (var axis = 0; axis < 3; axis++)
            {
                var next = new double[result.Length];
                var stride = axis == 0 ? Bins * Bins : axis == 1 ? Bins : 1;
                for (var i = 0; i < result.Length; i++)
                {
                    var coord = i / stride % Bins;
                    double sum = 0;
                    for (var d = -r; d <= r; d++)
                    {
                        var c = coord + d;
                        if (c >= 0 && c < Bins) sum += result[i + d * stride];
                    }
                    next[i] = sum;
                }
                result = next;
            }
            return result;
        }

        private static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0; var c = 0;
                    for (var k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++) { s += src[y * w + k]; c++; }
                    tmp[y * w + x] = s / c;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0; var c = 0;
                    for (var k = Math.Max(0, y - r); k <= Math.Min(h - 1, y + r); k++) { s += tmp[k * w + x]; c++; }
                    dst[y * w + x] = s / c;
                }
            return dst;
        }

        /// <summary>Keeps only the biggest connected piece (preferring ones over the middle);
        /// false if nothing is left.</summary>
        private static bool KeepMainPiece(bool[] mask, int w, int h)
        {
            var label = new int[mask.Length];
            var best = 0;
            double bestScore = 0;
            var next = 0;
            var stack = new Stack<int>();
            for (var start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || label[start] != 0) continue;
                next++;
                double score = 0;
                stack.Push(start);
                label[start] = next;
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int x = i % w, y = i / w;
                    float dx = (x - w / 2f) / w, dy = (y - h / 2f) / h;
                    score += 1.5 - Math.Sqrt(dx * dx + dy * dy) * 2;   // middle pixels count more
                    foreach (var j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                        if (j >= 0 && mask[j] && label[j] == 0) { label[j] = next; stack.Push(j); }
                }
                if (score > bestScore) { bestScore = score; best = next; }
            }
            if (best == 0) return false;
            for (var i = 0; i < mask.Length; i++) mask[i] = label[i] == best;
            return true;
        }

        /// <summary>Background pieces not reaching the picture's edge become subject.</summary>
        private static void FillHoles(bool[] mask, int w, int h)
        {
            var outside = new bool[mask.Length];
            var stack = new Stack<int>();
            void Seed(int i) { if (!mask[i] && !outside[i]) { outside[i] = true; stack.Push(i); } }
            for (var x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
            for (var y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                int x = i % w, y = i / w;
                if (x > 0) Seed(i - 1);
                if (x < w - 1) Seed(i + 1);
                if (y > 0) Seed(i - w);
                if (y < h - 1) Seed(i + w);
            }
            for (var i = 0; i < mask.Length; i++) if (!outside[i]) mask[i] = true;
        }
    }
}
