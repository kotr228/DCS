using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>Selection helpers behind the Select and Image menus: selecting a layer's
    /// opaque pixels, Grow / Similar, and the bounds Trim keeps.</summary>
    public static class SelectionOps
    {
        /// <summary>The pixels of <paramref name="bitmap"/> with alpha at least
        /// <paramref name="threshold"/>, as a path (null if none) - Select > Load Layer
        /// Transparency, or Ctrl+click on a layer thumbnail.</summary>
        public static SKPath? FromAlpha(SKBitmap bitmap, byte threshold = 128)
        {
            var pixels = bitmap.Pixels;
            return FromMask(bitmap.Width, bitmap.Height, i => pixels[i].Alpha >= threshold);
        }

        /// <summary>Select > Grow: adds the neighbouring pixels whose color is within
        /// <paramref name="tolerance"/> of the colors already selected (spreading outward).</summary>
        public static SKPath? Grow(SKBitmap image, SKRegion selected, int tolerance) =>
            Spread(image, selected, tolerance, contiguous: true);

        /// <summary>Select > Similar: adds every pixel in the image whose color is within
        /// <paramref name="tolerance"/> of the selected colors, connected or not.</summary>
        public static SKPath? Similar(SKBitmap image, SKRegion selected, int tolerance) =>
            Spread(image, selected, tolerance, contiguous: false);

        private static SKPath? Spread(SKBitmap image, SKRegion selected, int tolerance, bool contiguous)
        {
            int w = image.Width, h = image.Height;
            var pixels = image.Pixels;
            var inside = new bool[w * h];

            // The range of colors already selected, widened by the tolerance.
            int rMin = 255, gMin = 255, bMin = 255, rMax = 0, gMax = 0, bMax = 0;
            var any = false;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!selected.Contains(x, y)) continue;
                    inside[y * w + x] = true;
                    any = true;
                    var c = pixels[y * w + x];
                    rMin = Math.Min(rMin, c.Red); rMax = Math.Max(rMax, c.Red);
                    gMin = Math.Min(gMin, c.Green); gMax = Math.Max(gMax, c.Green);
                    bMin = Math.Min(bMin, c.Blue); bMax = Math.Max(bMax, c.Blue);
                }
            if (!any) return null;

            bool Matches(SKColor c) =>
                c.Red >= rMin - tolerance && c.Red <= rMax + tolerance &&
                c.Green >= gMin - tolerance && c.Green <= gMax + tolerance &&
                c.Blue >= bMin - tolerance && c.Blue <= bMax + tolerance;

            if (!contiguous)
            {
                for (var i = 0; i < inside.Length; i++)
                    if (!inside[i] && Matches(pixels[i])) inside[i] = true;
                return FromMask(w, h, i => inside[i]);
            }

            var queue = new Queue<int>();
            for (var i = 0; i < inside.Length; i++)
                if (inside[i]) queue.Enqueue(i);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % w, y = i / w;
                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    var j = ny * w + nx;
                    if (inside[j] || !Matches(pixels[j])) return;
                    inside[j] = true;
                    queue.Enqueue(j);
                }
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            return FromMask(w, h, i => inside[i]);
        }

        /// <summary>Image > Trim: the smallest rectangle that keeps everything that isn't
        /// transparent (<paramref name="byColor"/> null) or isn't the given border color.</summary>
        public static SKRectI TrimBounds(SKBitmap image, SKColor? byColor, int tolerance = 0)
        {
            int w = image.Width, h = image.Height;
            var pixels = image.Pixels;
            bool Keep(SKColor c) => byColor is { } t
                ? Math.Abs(c.Red - t.Red) > tolerance || Math.Abs(c.Green - t.Green) > tolerance || Math.Abs(c.Blue - t.Blue) > tolerance || Math.Abs(c.Alpha - t.Alpha) > tolerance
                : c.Alpha > 0;

            int left = w, top = h, right = -1, bottom = -1;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (!Keep(pixels[y * w + x])) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
        }

        /// <summary>
        /// Select and Mask: cleans up a selection's edge. <paramref name="smooth"/> (px)
        /// rounds off jagged steps and drops specks; <paramref name="shiftEdge"/> (px)
        /// moves the whole edge out (positive) or in (negative). Returns the new outline
        /// (null if nothing is left). Softness is the selection's Feather, set separately.
        /// </summary>
        public static SKPath? Refine(SKPath path, int width, int height, float smooth, float shiftEdge)
        {
            using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(mask))
            using (var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPath(path, fill);
            }

            using var refined = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(refined))
            {
                canvas.Clear(SKColors.Transparent);
                SKImageFilter? filter = null;
                if (smooth > 0) filter = SKImageFilter.CreateBlur(smooth / 2, smooth / 2);
                if (shiftEdge > 0.5f) filter = SKImageFilter.CreateDilate(shiftEdge, shiftEdge, filter);
                else if (shiftEdge < -0.5f) filter = SKImageFilter.CreateErode(-shiftEdge, -shiftEdge, filter);
                using (filter)
                using (var paint = new SKPaint { ImageFilter = filter })
                    canvas.DrawBitmap(mask, 0, 0, paint);
            }

            // Back to a hard outline at the half-way point of the smoothed edge.
            var bytes = refined.GetPixelSpan().ToArray();
            var rowBytes = refined.RowBytes;
            return FromMask(width, height, i => bytes[(i / width) * rowBytes + i % width] >= 128);
        }

        /// <summary>A path covering the pixels where <paramref name="selected"/> is true,
        /// built from horizontal runs (null if none).</summary>
        public static SKPath? FromMask(int width, int height, Func<int, bool> selected)
        {
            using var region = new SKRegion();
            var any = false;
            for (var y = 0; y < height; y++)
            {
                var x = 0;
                while (x < width)
                {
                    if (!selected(y * width + x)) { x++; continue; }
                    var start = x;
                    while (x < width && selected(y * width + x)) x++;
                    region.Op(new SKRectI(start, y, x, y + 1), SKRegionOperation.Union);
                    any = true;
                }
            }
            return any ? region.GetBoundaryPath() : null;
        }
    }
}
