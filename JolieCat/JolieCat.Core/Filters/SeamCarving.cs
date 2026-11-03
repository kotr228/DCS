using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Content-Aware Scale (seam carving): changes an image's width/height by removing
    /// (or repeating) the least noticeable connected paths of pixels - "seams" through
    /// flat sky, plain walls - so people and objects keep their shape. A plan is worked
    /// out once from the visible image and then applied the same way to every layer
    /// and mask, so they stay lined up. Pixels marked as protected are avoided.
    /// </summary>
    public sealed class SeamCarvingPlan
    {
        private readonly int[][] _columnMap;   // per source row: source x for each new x
        private readonly int[][] _rowMap;      // per new x: row (in the stage-1 image) for each new y

        private SeamCarvingPlan(int width, int height, int newWidth, int newHeight, int[][] columnMap, int[][] rowMap)
        {
            Width = width;
            Height = height;
            NewWidth = newWidth;
            NewHeight = newHeight;
            _columnMap = columnMap;
            _rowMap = rowMap;
        }

        public int Width { get; }
        public int Height { get; }
        public int NewWidth { get; }
        public int NewHeight { get; }

        /// <summary>Plans resizing <paramref name="image"/> to the new size.
        /// <paramref name="protect"/> (optional, one flag per pixel) marks pixels to keep.</summary>
        public static SeamCarvingPlan Create(SKBitmap image, int newWidth, int newHeight, bool[]? protect = null)
        {
            ArgumentNullException.ThrowIfNull(image);
            int w = image.Width, h = image.Height;
            if (newWidth < 1 || newHeight < 1) throw new ArgumentOutOfRangeException(nameof(newWidth));
            if (newWidth > w * 2 || newHeight > h * 2) throw new ArgumentOutOfRangeException(nameof(newWidth), "At most twice the size.");

            var pixels = image.Pixels;
            var luma = new float[w * h];
            for (var i = 0; i < luma.Length; i++)
                luma[i] = 0.299f * pixels[i].Red + 0.587f * pixels[i].Green + 0.114f * pixels[i].Blue + pixels[i].Alpha * 0.25f;

            // Stage 1: columns (along each row).
            var columnMap = PlanAxis(luma, protect, w, h, newWidth);

            // Stage 2: rows, worked out on the stage-1 image, transposed.
            var stage1 = new float[newWidth * h];
            bool[]? stage1Protect = protect is null ? null : new bool[newWidth * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < newWidth; x++)
                {
                    var src = y * w + columnMap[y][x];
                    stage1[x * h + y] = luma[src];                        // transposed: row = x
                    if (stage1Protect is not null) stage1Protect[x * h + y] = protect![src];
                }
            var rowMap = PlanAxis(stage1, stage1Protect, h, newWidth, newHeight);

            return new SeamCarvingPlan(w, h, newWidth, newHeight, columnMap, rowMap);
        }

        /// <summary>For an image <paramref name="w"/> wide and <paramref name="h"/> tall,
        /// which source x each of <paramref name="newW"/> output columns takes, per row.</summary>
        private static int[][] PlanAxis(float[] luma, bool[]? protect, int w, int h, int newW)
        {
            var map = new int[h][];
            if (newW == w)
            {
                for (var y = 0; y < h; y++)
                {
                    map[y] = new int[w];
                    for (var x = 0; x < w; x++) map[y][x] = x;
                }
                return map;
            }

            var remove = Math.Abs(newW - w);
            var seams = FindSeams(luma, protect, w, h, Math.Min(remove, w - 1));

            for (var y = 0; y < h; y++)
            {
                var marked = new bool[w];
                foreach (var seam in seams) marked[seam[y]] = true;
                var row = new List<int>(newW);
                if (newW < w)
                {
                    for (var x = 0; x < w; x++) if (!marked[x]) row.Add(x);
                }
                else
                {
                    // Enlarging: the found seams are repeated (twice if needed).
                    var extra = newW - w;
                    var repeats = (extra + seams.Count - 1) / Math.Max(1, seams.Count);
                    var added = 0;
                    for (var x = 0; x < w; x++)
                    {
                        row.Add(x);
                        if (!marked[x]) continue;
                        for (var r = 0; r < repeats && added < extra; r++, added++) row.Add(x);
                    }
                    while (row.Count < newW) row.Add(w - 1);
                }
                map[y] = row.ToArray();
            }
            return map;
        }

        /// <summary>The <paramref name="count"/> cheapest vertical seams, one after another,
        /// each as a source x per row. Energy is computed once, then only refreshed next to
        /// each removed seam, so each further seam costs one pass of the path search.</summary>
        private static List<int[]> FindSeams(float[] luma, bool[]? protect, int w, int h, int count)
        {
            // cur[y] holds the source x of each remaining column; energy[y] lines up with it.
            var cur = new int[h][];
            var energy = new float[h][];
            for (var y = 0; y < h; y++)
            {
                cur[y] = new int[w];
                for (var x = 0; x < w; x++) cur[y][x] = x;
                energy[y] = new float[w];
            }

            var width = w;

            float Energy(int y, int x)
            {
                var row = cur[y];
                var up = cur[y > 0 ? y - 1 : y];
                var down = cur[y < h - 1 ? y + 1 : y];
                var xi = x < width ? x : width - 1;
                var l = luma[y * w + row[x > 0 ? x - 1 : 0]];
                var r = luma[y * w + row[x < width - 1 ? x + 1 : width - 1]];
                var u = luma[(y > 0 ? y - 1 : 0) * w + up[xi]];
                var d = luma[(y < h - 1 ? y + 1 : h - 1) * w + down[xi]];
                var e = Math.Abs(r - l) + Math.Abs(d - u);
                return protect is not null && protect[y * w + row[x]] ? e + 1e6f : e;
            }

            for (var y = 0; y < h; y++)
                for (var x = 0; x < width; x++)
                    energy[y][x] = Energy(y, x);

            var seams = new List<int[]>(count);
            var cost = new float[w];
            var prevCost = new float[w];
            var from = new int[h * w];   // the x in the row above each cheapest path comes from

            for (var n = 0; n < count && width > 1; n++)
            {
                // Dynamic programming: the cheapest connected path from top to bottom.
                Array.Copy(energy[0], prevCost, width);
                for (var y = 1; y < h; y++)
                {
                    var e = energy[y];
                    var link = y * w;
                    for (var x = 0; x < width; x++)
                    {
                        var bx = x;
                        var best = prevCost[x];
                        if (x > 0 && prevCost[x - 1] < best) { best = prevCost[x - 1]; bx = x - 1; }
                        if (x < width - 1 && prevCost[x + 1] < best) { best = prevCost[x + 1]; bx = x + 1; }
                        cost[x] = e[x] + best;
                        from[link + x] = bx;
                    }
                    (prevCost, cost) = (cost, prevCost);
                }

                var at = 0;
                for (var x = 1; x < width; x++) if (prevCost[x] < prevCost[at]) at = x;

                var seam = new int[h];
                var positions = new int[h];
                for (var y = h - 1; y >= 0; y--)
                {
                    positions[y] = at;
                    seam[y] = cur[y][at];
                    if (y > 0) at = from[y * w + at];
                }

                // Remove it, then refresh the energy just around where it was.
                for (var y = 0; y < h; y++)
                {
                    var bx = positions[y];
                    Array.Copy(cur[y], bx + 1, cur[y], bx, width - bx - 1);
                    Array.Copy(energy[y], bx + 1, energy[y], bx, width - bx - 1);
                }
                width--;
                for (var y = 0; y < h; y++)
                {
                    var bx = positions[y];
                    for (var x = Math.Max(0, bx - 2); x <= Math.Min(width - 1, bx + 1); x++)
                        energy[y][x] = Energy(y, x);
                }
                seams.Add(seam);
            }
            return seams;
        }

        /// <summary>Applies the plan to an image (or mask) the same size as the one planned on.</summary>
        public SKBitmap Apply(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.Width != Width || source.Height != Height) throw new ArgumentException("Size mismatch.", nameof(source));
            var src = source.Pixels;
            var dst = new SKColor[NewWidth * NewHeight];
            for (var x = 0; x < NewWidth; x++)
                for (var y = 0; y < NewHeight; y++)
                {
                    var stageRow = _rowMap[x][y];               // row in the stage-1 image
                    var sx = _columnMap[stageRow][x];
                    dst[y * NewWidth + x] = src[stageRow * Width + sx];
                }
            var result = new SKBitmap(NewWidth, NewHeight, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = dst };
            return result;
        }
    }
}
