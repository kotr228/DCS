using System;
using System.Collections.Generic;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>
    /// A <see cref="Scene"/>'s current selection: an arbitrary pixel region built from a
    /// rectangle, ellipse, freehand/polygon path, or flood-selected pixels, that
    /// constrains where painting, erasing, and filling can affect the active layer.
    /// Backed by both an <see cref="SKRegion"/> and an equivalent <see cref="SKPath"/> -
    /// built together from the same shape whenever the selection changes - so callers
    /// can pick whichever fits: <see cref="Path"/> drives <c>SKCanvas.ClipPath</c> (with
    /// anti-aliased edges) for canvas-based drawing tools, while <see cref="Region"/>
    /// answers fast <see cref="Contains"/> point tests for pixel-array operations like
    /// flood fill, where a per-pixel path hit-test would be far more expensive.
    /// </summary>
    /// <summary>How a new selection shape combines with the existing selection.</summary>
    public enum SelectionCombine
    {
        New,
        Add,
        Subtract,
        Intersect,
    }

    public sealed class Selection
    {
        private SKRegion? _region;
        private SKPath? _path;
        private float _feather;

        /// <summary>Soft edge width in pixels (0 = hard edge). Tools that honour
        /// it fade their effect out across this distance around the selection's edge.</summary>
        public float Feather
        {
            get => _feather;
            set
            {
                var clamped = Math.Clamp(value, 0f, 500f);
                if (clamped == _feather) return;
                _feather = clamped;
                Version++;
            }
        }

        /// <summary>Changes whenever the selected area or its feather changes - lets
        /// callers cache things derived from the selection (e.g. the feathered mask).</summary>
        public int Version { get; private set; }

        /// <summary>Combines <paramref name="shape"/> with the current selection:
        /// replace, add (Shift), subtract (Alt) or intersect (Shift+Alt).</summary>
        public void Combine(SKPath shape, SelectionCombine mode, int documentWidth, int documentHeight)
        {
            ArgumentNullException.ThrowIfNull(shape);

            if (mode == SelectionCombine.New || (mode == SelectionCombine.Add && (!HasSelection || _path is null)))
            {
                _feather = 0;
                ApplyResult(new SKPath(shape), documentWidth, documentHeight);
                return;
            }

            if (!HasSelection || _path is null) return; // nothing to subtract from / intersect with

            var op = mode switch
            {
                SelectionCombine.Add => SKPathOp.Union,
                SelectionCombine.Subtract => SKPathOp.Difference,
                _ => SKPathOp.Intersect,
            };
            ApplyResult(_path.Op(shape, op), documentWidth, documentHeight);
        }

        /// <summary>False when nothing is selected - painting/filling/erasing then affects the whole layer.</summary>
        public bool HasSelection => _region is { IsEmpty: false };

        /// <summary>The selected region, or null when there's no active selection. Used
        /// for fast per-pixel <see cref="Contains"/> tests.</summary>
        public SKRegion? Region => _region;

        /// <summary>The selected area as a path, or null when there's no active
        /// selection. Used to clip canvas drawing (<c>SKCanvas.ClipPath</c>) so painting,
        /// erasing, and fills stop exactly at the selection's edge - with anti-aliasing,
        /// unlike a region-based clip.</summary>
        public SKPath? Path => _path;

        /// <summary>Selects an axis-aligned rectangle (Rectangular Marquee).</summary>
        public void SetRect(SKRectI rect)
        {
            var region = new SKRegion();
            region.SetRect(rect);
            _region = region;

            var path = new SKPath();
            path.AddRect(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height));
            _path = path;
            Version++;
        }

        /// <summary>Rasterizes an arbitrary closed path - an elliptical marquee, a
        /// freehand lasso, or a clicked-vertex polygon - into the selection, bounded to
        /// the document. The path itself becomes <see cref="Path"/> directly, so the
        /// clip mask matches the drawn shape exactly rather than the region's
        /// integer-aligned approximation of it.</summary>
        public void SetPath(SKPath path, int documentWidth, int documentHeight)
        {
            using var bounds = new SKRegion();
            bounds.SetRect(new SKRectI(0, 0, documentWidth, documentHeight));

            var region = new SKRegion();
            region.SetPath(path, bounds);
            _region = region;
            _path = path;
            Version++;
        }

        /// <summary>Adopts an already-built region directly (Magic Wand/Quick
        /// Selection), deriving its equivalent clip path from the region's own boundary.</summary>
        public void SetRegion(SKRegion region)
        {
            _region = region;
            _path = region.GetBoundaryPath();
            Version++;
        }

        public void Clear()
        {
            _region = null;
            _path = null;
            _feather = 0;
            Version++;
        }

        /// <summary>Selects the whole document (Ctrl+A).</summary>
        public void SelectAll(int documentWidth, int documentHeight) =>
            SetRect(new SKRectI(0, 0, documentWidth, documentHeight));

        /// <summary>Swaps selected and unselected areas (Ctrl+Shift+I). With nothing
        /// selected this selects nothing - the inverse of "everything".</summary>
        public void Invert(int documentWidth, int documentHeight)
        {
            if (!HasSelection || _path is null)
            {
                Clear();
                return;
            }

            using var full = new SKPath();
            full.AddRect(SKRect.Create(0, 0, documentWidth, documentHeight));
            var inverted = full.Op(_path, SKPathOp.Difference);
            if (inverted is null || inverted.IsEmpty)
            {
                Clear();
                return;
            }

            SetPath(inverted, documentWidth, documentHeight);
            if (!HasSelection) Clear();
        }

        /// <summary>Select &gt; Modify &gt; Expand: grows the selection outward by
        /// <paramref name="pixels"/>, rounding its corners like Photoshop.</summary>
        public void Expand(float pixels, int documentWidth, int documentHeight) =>
            ModifyWithOutline(pixels * 2, SKPathOp.Union, documentWidth, documentHeight);

        /// <summary>Select &gt; Modify &gt; Contract: shrinks the selection inward by
        /// <paramref name="pixels"/>. A selection thinner than that disappears.</summary>
        public void Contract(float pixels, int documentWidth, int documentHeight) =>
            ModifyWithOutline(pixels * 2, SKPathOp.Difference, documentWidth, documentHeight);

        /// <summary>Select &gt; Modify &gt; Border: replaces the selection with a band
        /// <paramref name="pixels"/> wide centered on its edge.</summary>
        public void Border(float pixels, int documentWidth, int documentHeight)
        {
            if (!HasSelection || _path is null || pixels <= 0) return;

            using var outline = Outline(_path, pixels);
            ApplyResult(outline.Op(new SKPath(), SKPathOp.Union) ?? new SKPath(), documentWidth, documentHeight);
        }

        /// <summary>Select &gt; Modify &gt; Smooth: rounds off jagged corners and removes
        /// specks smaller than <paramref name="pixels"/> (contract, then expand back).</summary>
        public void Smooth(float pixels, int documentWidth, int documentHeight)
        {
            if (!HasSelection || pixels <= 0) return;

            Contract(pixels, documentWidth, documentHeight);
            if (HasSelection) Expand(pixels, documentWidth, documentHeight);
        }

        private void ModifyWithOutline(float strokeWidth, SKPathOp op, int documentWidth, int documentHeight)
        {
            if (!HasSelection || _path is null || strokeWidth <= 0) return;

            using var outline = Outline(_path, strokeWidth);
            ApplyResult(_path.Op(outline, op), documentWidth, documentHeight);
        }

        private void ApplyResult(SKPath? result, int documentWidth, int documentHeight)
        {
            if (result is null || result.IsEmpty)
            {
                Clear();
                return;
            }

            using var bounds = new SKPath();
            bounds.AddRect(SKRect.Create(0, 0, documentWidth, documentHeight));
            var clipped = result.Op(bounds, SKPathOp.Intersect);
            if (clipped is null || clipped.IsEmpty)
            {
                Clear();
                return;
            }

            SetPath(clipped, documentWidth, documentHeight);
            if (!HasSelection) Clear();
        }

        /// <summary>The filled area of <paramref name="path"/>'s edge stroked
        /// <paramref name="width"/> wide with round joins.</summary>
        private static SKPath Outline(SKPath path, float width)
        {
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
            };
            var outline = new SKPath();
            paint.GetFillPath(path, outline);
            return outline;
        }

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
        public static SKRegion CreateRegionFromColorFlood(SKBitmap bitmap, int x0, int y0, float tolerance) =>
            CreateRegionFromColorFlood(bitmap.Pixels, bitmap.Width, bitmap.Height, x0, y0, tolerance, new SKRectI(0, 0, bitmap.Width, bitmap.Height));

        /// <summary>Select &gt; Color Range: every pixel (anywhere, not just connected
        /// ones) whose color is within <paramref name="tolerance"/> of <paramref name="color"/>.</summary>
        public static SKRegion CreateRegionFromColor(SKColor[] pixels, int width, int height, SKColor color, float tolerance)
        {
            var region = new SKRegion();
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                var x = 0;
                while (x < width)
                {
                    if (!ColorTolerance.IsWithin(pixels[row + x], color, tolerance)) { x++; continue; }
                    var runStart = x;
                    while (x < width && ColorTolerance.IsWithin(pixels[row + x], color, tolerance)) x++;
                    region.Op(new SKRectI(runStart, y, x, y + 1), SKRegionOperation.Union);
                }
            }
            return region;
        }

        /// <summary>Flood fill over an already-read pixel array, never leaving
        /// <paramref name="limit"/> - Quick Selection grows only near the brush, and reads
        /// the layer's pixels once per stroke instead of once per mouse move.</summary>
        public static SKRegion CreateRegionFromColorFlood(SKColor[] pixels, int width, int height, int x0, int y0, float tolerance, SKRectI limit)
        {
            limit = SKRectI.Intersect(limit, new SKRectI(0, 0, width, height));

            var region = new SKRegion();
            if (!limit.Contains(x0, y0))
                return region;

            var targetColor = pixels[y0 * width + x0];

            // The visited mask only covers the limit rectangle.
            var lw = limit.Width;
            var mask = new bool[lw * limit.Height];
            int M(int x, int y) => (y - limit.Top) * lw + (x - limit.Left);

            var stack = new Stack<(int X, int Y)>();

            mask[M(x0, y0)] = true;
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
                if (x < limit.Left || x >= limit.Right || y < limit.Top || y >= limit.Bottom) return;

                var m = M(x, y);
                if (mask[m] || !ColorTolerance.IsWithin(pixels[y * width + x], targetColor, tolerance)) return;

                mask[m] = true;
                stack.Push((x, y));
            }

            // One rectangle per contiguous row-run, not one per pixel.
            for (var y = limit.Top; y < limit.Bottom; y++)
            {
                var x = limit.Left;
                while (x < limit.Right)
                {
                    if (!mask[M(x, y)]) { x++; continue; }

                    var runStart = x;
                    while (x < limit.Right && mask[M(x, y)]) x++;

                    region.Op(new SKRectI(runStart, y, x, y + 1), SKRegionOperation.Union);
                }
            }

            return region;
        }
    }
}
