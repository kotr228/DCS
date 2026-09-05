using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// A <see cref="Scene"/>'s current selection: an arbitrary pixel region built from a
    /// rectangle, ellipse, freehand/polygon path, or flood-selected pixels, that
    /// constrains where painting, erasing, and filling can affect the active layer.
    /// Backed by an <see cref="SKRegion"/> (Skia's own clip/region type) rather than a
    /// hand-rolled boolean mask, so the same value both drives
    /// <c>SKCanvas.ClipRegion</c> for canvas-based drawing and answers direct
    /// <see cref="Contains"/> point tests for pixel-array operations like flood fill.
    /// </summary>
    public sealed class Selection
    {
        private SKRegion? _region;

        /// <summary>False when nothing is selected - painting/filling/erasing then affects the whole layer.</summary>
        public bool HasSelection => _region is { IsEmpty: false };

        /// <summary>The selected region, or null when there's no active selection.</summary>
        public SKRegion? Region => _region;

        /// <summary>Selects an axis-aligned rectangle (Rectangular Marquee).</summary>
        public void SetRect(SKRectI rect)
        {
            var region = new SKRegion();
            region.SetRect(rect);
            _region = region;
        }

        /// <summary>Rasterizes an arbitrary closed path - an elliptical marquee, a
        /// freehand lasso, or a clicked-vertex polygon - into the selection, bounded to
        /// the document.</summary>
        public void SetPath(SKPath path, int documentWidth, int documentHeight)
        {
            using var bounds = new SKRegion();
            bounds.SetRect(new SKRectI(0, 0, documentWidth, documentHeight));

            var region = new SKRegion();
            region.SetPath(path, bounds);
            _region = region;
        }

        /// <summary>Adopts an already-built region directly (Magic Wand/Quick Selection).</summary>
        public void SetRegion(SKRegion region) => _region = region;

        public void Clear() => _region = null;

        /// <summary>
        /// True if (x, y) is selected - or if there's no active selection at all, since
        /// "nothing selected" means "everywhere is fair game," matching how every tool in
        /// this app behaves before a first selection is made.
        /// </summary>
        public bool Contains(int x, int y) => _region is null || _region.Contains(x, y);

        /// <summary>
        /// Flood-selects every pixel reachable from (x0, y0) by 4-connected steps whose
        /// color is within <paramref name="tolerance"/> of the clicked pixel - the Magic
        /// Wand/Quick Selection algorithm. Marks each pixel visited the moment it's
        /// pushed (not when it's popped), bounding the stack to one entry per pixel
        /// instead of letting duplicates pile up on a large uniform region (a fresh,
        /// blank layer is exactly that).
        /// </summary>
        public static SKRegion CreateRegionFromColorFlood(SKBitmap bitmap, int x0, int y0, float tolerance)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;

            var region = new SKRegion();
            if (x0 < 0 || y0 < 0 || x0 >= width || y0 >= height)
                return region;

            var pixels = bitmap.Pixels;
            var targetColor = pixels[y0 * width + x0];

            var mask = new bool[width * height];
            var stack = new Stack<(int X, int Y)>();

            mask[y0 * width + x0] = true;
            stack.Push((x0, y0));

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();

                TryVisit(x + 1, y);
                TryVisit(x - 1, y);
                TryVisit(x, y + 1);
                TryVisit(x, y - 1);
            }

            void TryVisit(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;

                var index = y * width + x;
                if (mask[index] || !ColorTolerance.IsWithin(pixels[index], targetColor, tolerance)) return;

                mask[index] = true;
                stack.Push((x, y));
            }

            // One rectangle per contiguous row-run, not one per pixel.
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * width;
                var x = 0;
                while (x < width)
                {
                    if (!mask[rowStart + x]) { x++; continue; }

                    var runStart = x;
                    while (x < width && mask[rowStart + x]) x++;

                    region.Op(new SKRectI(runStart, y, x, y + 1), SKRegionOperation.Union);
                }
            }

            return region;
        }
    }
}
