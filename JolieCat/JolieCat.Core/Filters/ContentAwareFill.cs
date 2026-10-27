using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Content-Aware Fill: fills a hole with texture taken from the rest of the image,
    /// so removed objects are replaced by plausible background instead of a smear.
    /// </summary>
    /// <remarks>
    /// The same approach Photoshop uses (Wexler et al. / PatchMatch, Barnes et al.):
    /// <list type="number">
    /// <item>Work on a region around the hole and build an image pyramid, so the
    /// hole is only a few pixels wide at the coarsest level.</item>
    /// <item>At each level, find for every hole pixel the most similar 7x7 patch
    /// outside the hole (PatchMatch: random guesses, spread to neighbours, refined
    /// by a shrinking random search).</item>
    /// <item>Rebuild each hole pixel as the average of what the overlapping matched
    /// patches say it should be, repeat, then move to the next finer level.</item>
    /// </list>
    /// </remarks>
    public static class ContentAwareFill
    {
        private const int R = 3;                 // patch radius: 7x7 patches
        private const int PatchSide = 2 * R + 1;

        /// <summary>Returns a copy of <paramref name="pixels"/> with every pixel where
        /// <paramref name="hole"/> is true filled from the surroundings. Pixels outside
        /// the hole are never changed.</summary>
        public static SKColor[] Fill(SKColor[] pixels, int width, int height, bool[] hole, int seed = 12345)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentNullException.ThrowIfNull(hole);
            if (pixels.Length != width * height || hole.Length != width * height)
                throw new ArgumentException("Pixel and hole sizes must match the image.");

            var result = (SKColor[])pixels.Clone();

            // Bounding box of the hole.
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (!hole[y * width + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return result;

            // Texture is taken from a band around the hole about as wide as the hole
            // itself: close enough to match the surroundings, small enough to be fast.
            var margin = Math.Clamp(Math.Max(maxX - minX, maxY - minY) + 1, 24, 400);
            var cx0 = Math.Max(0, minX - margin);
            var cy0 = Math.Max(0, minY - margin);
            var cx1 = Math.Min(width, maxX + 1 + margin);
            var cy1 = Math.Min(height, maxY + 1 + margin);

            var finest = Level.Crop(pixels, hole, width, cx0, cy0, cx1 - cx0, cy1 - cy0);
            var pyramid = BuildPyramid(finest);
            if (pyramid.Count == 0) return result; // nothing outside the hole to copy from

            var random = new Random(seed);
            var coarsest = pyramid[^1];
            coarsest.InitializeByDiffusion();
            coarsest.RandomizeNnf(random);

            for (var levelIndex = pyramid.Count - 1; levelIndex >= 0; levelIndex--)
            {
                var level = pyramid[levelIndex];
                if (levelIndex < pyramid.Count - 1)
                    level.UpsampleFrom(pyramid[levelIndex + 1], random);

                var iterations = levelIndex == pyramid.Count - 1 ? 8 : levelIndex == 0 ? 3 : 5;
                for (var i = 0; i < iterations; i++)
                {
                    level.PatchMatch(random, reverse: (i & 1) == 1);
                    level.Vote();
                }
            }

            finest.WriteHole(result, width, cx0, cy0);
            return result;
        }

        /// <summary>Halves the image until the hole is only a few patches wide.
        /// Levels without any valid source patch are dropped (coarsest first).</summary>
        private static List<Level> BuildPyramid(Level finest)
        {
            var levels = new List<Level> { finest };
            while (true)
            {
                var last = levels[^1];
                if (last.HoleExtent <= PatchSide * 2 || last.Width / 2 < PatchSide * 3 || last.Height / 2 < PatchSide * 3) break;
                levels.Add(last.Downsample());
            }

            for (var i = levels.Count - 1; i >= 0 && levels[i].ValidSources.Length == 0; i--)
                levels.RemoveAt(i);
            return levels;
        }

        private sealed class Level
        {
            public int Width;
            public int Height;
            public float[] Pixels = Array.Empty<float>();   // RGBA, straight alpha, 0..255
            public bool[] Hole = Array.Empty<bool>();
            public int[] HoleIndices = Array.Empty<int>();
            public bool[] Valid = Array.Empty<bool>();      // patch centred here lies fully outside the hole
            public int[] ValidSources = Array.Empty<int>();
            public int[] Nnf = Array.Empty<int>();          // best source index per pixel (hole pixels only)
            public float[] Cost = Array.Empty<float>();

            public int HoleExtent
            {
                get
                {
                    int minX = Width, minY = Height, maxX = -1, maxY = -1;
                    foreach (var i in HoleIndices)
                    {
                        var x = i % Width;
                        var y = i / Width;
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                    return maxX < 0 ? 0 : Math.Max(maxX - minX, maxY - minY) + 1;
                }
            }

            public static Level Crop(SKColor[] pixels, bool[] hole, int stride, int x0, int y0, int w, int h)
            {
                var level = new Level { Width = w, Height = h, Pixels = new float[w * h * 4], Hole = new bool[w * h] };
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var src = (y + y0) * stride + x + x0;
                        var dst = y * w + x;
                        var c = pixels[src];
                        level.Pixels[dst * 4] = c.Red;
                        level.Pixels[dst * 4 + 1] = c.Green;
                        level.Pixels[dst * 4 + 2] = c.Blue;
                        level.Pixels[dst * 4 + 3] = c.Alpha;
                        level.Hole[dst] = hole[src];
                    }
                level.Prepare();
                return level;
            }

            public Level Downsample()
            {
                var w = Width / 2;
                var h = Height / 2;
                var level = new Level { Width = w, Height = h, Pixels = new float[w * h * 4], Hole = new bool[w * h] };
                Span<float> sum = stackalloc float[4];
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var dst = y * w + x;
                        var anyHole = false;
                        var known = 0;
                        sum.Clear();
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++)
                            {
                                var src = (2 * y + dy) * Width + 2 * x + dx;
                                if (Hole[src]) { anyHole = true; continue; }
                                known++;
                                for (var c = 0; c < 4; c++) sum[c] += Pixels[src * 4 + c];
                            }
                        level.Hole[dst] = anyHole;
                        if (known > 0)
                            for (var c = 0; c < 4; c++) level.Pixels[dst * 4 + c] = sum[c] / known;
                    }
                level.Prepare();
                return level;
            }

            /// <summary>Hole list, source validity (via a summed-area table of the hole).</summary>
            private void Prepare()
            {
                var holes = new List<int>();
                for (var i = 0; i < Hole.Length; i++)
                    if (Hole[i]) holes.Add(i);
                HoleIndices = holes.ToArray();

                var sat = new int[(Width + 1) * (Height + 1)];
                for (var y = 0; y < Height; y++)
                {
                    var row = 0;
                    for (var x = 0; x < Width; x++)
                    {
                        row += Hole[y * Width + x] ? 1 : 0;
                        sat[(y + 1) * (Width + 1) + x + 1] = sat[y * (Width + 1) + x + 1] + row;
                    }
                }

                Valid = new bool[Width * Height];
                var valid = new List<int>();
                for (var y = R; y < Height - R; y++)
                    for (var x = R; x < Width - R; x++)
                    {
                        int x0 = x - R, y0 = y - R, x1 = x + R + 1, y1 = y + R + 1;
                        var count = sat[y1 * (Width + 1) + x1] - sat[y0 * (Width + 1) + x1] - sat[y1 * (Width + 1) + x0] + sat[y0 * (Width + 1) + x0];
                        if (count != 0) continue;
                        Valid[y * Width + x] = true;
                        valid.Add(y * Width + x);
                    }
                ValidSources = valid.ToArray();

                Nnf = new int[Width * Height];
                Cost = new float[Width * Height];
            }

            /// <summary>Coarsest-level starting guess: grow the known colours inward,
            /// ring by ring, averaging the known neighbours.</summary>
            public void InitializeByDiffusion()
            {
                var known = new bool[Hole.Length];
                for (var i = 0; i < known.Length; i++) known[i] = !Hole[i];
                var remaining = new List<int>(HoleIndices);

                Span<float> sum = stackalloc float[4];
                while (remaining.Count > 0)
                {
                    var filled = new List<int>();
                    var next = new List<int>();
                    foreach (var i in remaining)
                    {
                        var x = i % Width;
                        var y = i / Width;
                        sum.Clear();
                        var n = 0;
                        for (var dy = -1; dy <= 1; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                                var j = ny * Width + nx;
                                if (!known[j]) continue;
                                n++;
                                for (var c = 0; c < 4; c++) sum[c] += Pixels[j * 4 + c];
                            }

                        if (n == 0) { next.Add(i); continue; }
                        for (var c = 0; c < 4; c++) Pixels[i * 4 + c] = sum[c] / n;
                        filled.Add(i);
                    }

                    if (filled.Count == 0) break; // isolated (no known pixels at all)
                    foreach (var i in filled) known[i] = true;
                    remaining = next;
                }
            }

            public void RandomizeNnf(Random random)
            {
                foreach (var t in HoleIndices)
                {
                    Nnf[t] = ValidSources[random.Next(ValidSources.Length)];
                    Cost[t] = Distance(t, Nnf[t], float.MaxValue);
                }
            }

            /// <summary>Starts this level from the coarser one: hole colours and
            /// matches are scaled up, then the colours are rebuilt by voting.</summary>
            public void UpsampleFrom(Level coarse, Random random)
            {
                foreach (var t in HoleIndices)
                {
                    var x = t % Width;
                    var y = t / Width;
                    var cx = Math.Min(x / 2, coarse.Width - 1);
                    var cy = Math.Min(y / 2, coarse.Height - 1);
                    var ci = cy * coarse.Width + cx;

                    for (var c = 0; c < 4; c++) Pixels[t * 4 + c] = coarse.Pixels[ci * 4 + c];

                    var source = -1;
                    if (coarse.Hole[ci])
                    {
                        var s = coarse.Nnf[ci];
                        var sx = Math.Clamp(2 * (s % coarse.Width) + (x & 1), 0, Width - 1);
                        var sy = Math.Clamp(2 * (s / coarse.Width) + (y & 1), 0, Height - 1);
                        var candidate = sy * Width + sx;
                        if (Valid[candidate]) source = candidate;
                    }
                    Nnf[t] = source >= 0 ? source : ValidSources[random.Next(ValidSources.Length)];
                }

                Vote();
                foreach (var t in HoleIndices) Cost[t] = Distance(t, Nnf[t], float.MaxValue);
            }

            /// <summary>Sum of squared differences between the patches centred at
            /// <paramref name="target"/> and <paramref name="source"/>; stops early
            /// once it exceeds <paramref name="limit"/>.</summary>
            private float Distance(int target, int source, float limit)
            {
                var tx = target % Width;
                var ty = target / Width;
                var sx = source % Width;
                var sy = source / Width;
                var sum = 0f;

                for (var dy = -R; dy <= R; dy++)
                {
                    var py = ty + dy;
                    if (py < 0 || py >= Height) continue;
                    var tRow = py * Width;
                    var sRow = (sy + dy) * Width;
                    for (var dx = -R; dx <= R; dx++)
                    {
                        var px = tx + dx;
                        if (px < 0 || px >= Width) continue;
                        var ti = (tRow + px) * 4;
                        var si = (sRow + sx + dx) * 4;
                        float d0 = Pixels[ti] - Pixels[si], d1 = Pixels[ti + 1] - Pixels[si + 1];
                        float d2 = Pixels[ti + 2] - Pixels[si + 2], d3 = Pixels[ti + 3] - Pixels[si + 3];
                        sum += d0 * d0 + d1 * d1 + d2 * d2 + d3 * d3;
                    }
                    if (sum > limit) return sum;
                }
                return sum;
            }

            private void TryCandidate(int target, int sx, int sy)
            {
                if (sx < 0 || sy < 0 || sx >= Width || sy >= Height) return;
                var source = sy * Width + sx;
                if (!Valid[source] || source == Nnf[target]) return;
                var d = Distance(target, source, Cost[target]);
                if (d < Cost[target])
                {
                    Cost[target] = d;
                    Nnf[target] = source;
                }
            }

            /// <summary>One PatchMatch sweep: propagate good matches from the
            /// previous neighbours, then a random search around the current best.</summary>
            public void PatchMatch(Random random, bool reverse)
            {
                // Scores change after each vote, so refresh them first.
                foreach (var t in HoleIndices) Cost[t] = Distance(t, Nnf[t], float.MaxValue);

                var step = reverse ? -1 : 1;
                var count = HoleIndices.Length;
                for (var k = 0; k < count; k++)
                {
                    var t = HoleIndices[reverse ? count - 1 - k : k];
                    var x = t % Width;
                    var y = t / Width;

                    // Propagation: the neighbour's match, shifted by one pixel.
                    var nx = x - step;
                    if (nx >= 0 && nx < Width && Hole[y * Width + nx])
                    {
                        var s = Nnf[y * Width + nx];
                        TryCandidate(t, s % Width + step, s / Width);
                    }
                    var ny = y - step;
                    if (ny >= 0 && ny < Height && Hole[ny * Width + x])
                    {
                        var s = Nnf[ny * Width + x];
                        TryCandidate(t, s % Width, s / Width + step);
                    }

                    // Random search in shrinking windows around the best match.
                    for (var radius = Math.Max(Width, Height); radius >= 1; radius /= 2)
                    {
                        var best = Nnf[t];
                        TryCandidate(t, best % Width + random.Next(-radius, radius + 1), best / Width + random.Next(-radius, radius + 1));
                    }
                }
            }

            /// <summary>Each hole pixel becomes the weighted average of what every
            /// overlapping patch's match says it should be. Better matches weigh more.</summary>
            private int[]? _slot;

            public void Vote()
            {
                var sum = new float[HoleIndices.Length * 4];
                var weight = new float[HoleIndices.Length];
                _slot ??= new int[Width * Height];
                var slot = _slot;
                for (var k = 0; k < HoleIndices.Length; k++) slot[HoleIndices[k]] = k;

                // Typical patch cost, for turning costs into weights.
                var mean = 0.0;
                foreach (var t in HoleIndices) mean += Cost[t];
                var sigma = (float)Math.Max(1.0, mean / Math.Max(1, HoleIndices.Length));

                foreach (var t in HoleIndices)
                {
                    var tx = t % Width;
                    var ty = t / Width;
                    var s = Nnf[t];
                    var sx = s % Width;
                    var sy = s / Width;
                    var w = MathF.Exp(-Cost[t] / (2 * sigma));
                    if (w < 1e-6f) w = 1e-6f;

                    for (var dy = -R; dy <= R; dy++)
                    {
                        var py = ty + dy;
                        if (py < 0 || py >= Height) continue;
                        for (var dx = -R; dx <= R; dx++)
                        {
                            var px = tx + dx;
                            if (px < 0 || px >= Width) continue;
                            var p = py * Width + px;
                            if (!Hole[p]) continue;
                            var k = slot[p];
                            var si = ((sy + dy) * Width + sx + dx) * 4;
                            sum[k * 4] += w * Pixels[si];
                            sum[k * 4 + 1] += w * Pixels[si + 1];
                            sum[k * 4 + 2] += w * Pixels[si + 2];
                            sum[k * 4 + 3] += w * Pixels[si + 3];
                            weight[k] += w;
                        }
                    }
                }

                for (var k = 0; k < HoleIndices.Length; k++)
                {
                    if (weight[k] <= 0) continue;
                    var p = HoleIndices[k] * 4;
                    for (var c = 0; c < 4; c++) Pixels[p + c] = sum[k * 4 + c] / weight[k];
                }
            }

            public void WriteHole(SKColor[] target, int stride, int x0, int y0)
            {
                foreach (var i in HoleIndices)
                {
                    var x = i % Width;
                    var y = i / Width;
                    var p = i * 4;
                    target[(y + y0) * stride + x + x0] = new SKColor(
                        ToByte(Pixels[p]), ToByte(Pixels[p + 1]), ToByte(Pixels[p + 2]), ToByte(Pixels[p + 3]));
                }
            }

            private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
        }
    }
}
