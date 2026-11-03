using System;
using SkiaSharp;

namespace JolieCat.Core.Documents
{
    /// <summary>Perspective Crop: straightens a four-cornered area into a rectangle.</summary>
    public sealed partial class Scene
    {
        /// <summary>
        /// The perspective transform that maps the unit square's corners (0,0), (1,0),
        /// (1,1), (0,1) onto <paramref name="quad"/>'s corners 0..3 (Heckbert's
        /// square-to-quad mapping).
        /// </summary>
        public static SKMatrix SquareToQuad(SKPoint[] quad)
        {
            if (quad is null || quad.Length != 4) throw new ArgumentException("Four corners are needed.", nameof(quad));
            float x0 = quad[0].X, y0 = quad[0].Y, x1 = quad[1].X, y1 = quad[1].Y;
            float x2 = quad[2].X, y2 = quad[2].Y, x3 = quad[3].X, y3 = quad[3].Y;

            var dx3 = x0 - x1 + x2 - x3;
            var dy3 = y0 - y1 + y2 - y3;
            float a, b, c, d, e, f, g, h;
            if (Math.Abs(dx3) < 1e-6f && Math.Abs(dy3) < 1e-6f)
            {
                // A parallelogram: no perspective.
                a = x1 - x0; b = x3 - x0; c = x0;
                d = y1 - y0; e = y3 - y0; f = y0;
                g = 0; h = 0;
            }
            else
            {
                float dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
                var det = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(det) < 1e-9f) throw new ArgumentException("The corners don't make a usable shape.", nameof(quad));
                g = (dx3 * dy2 - dx2 * dy3) / det;
                h = (dx1 * dy3 - dx3 * dy1) / det;
                a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0;
                d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
            }

            return new SKMatrix(a, b, c, d, e, f, g, h, 1);
        }

        /// <summary>The transform that takes <paramref name="quad"/> (top-left, top-right,
        /// bottom-right, bottom-left) onto the rectangle 0,0 - width,height.</summary>
        public static SKMatrix QuadToRect(SKPoint[] quad, int width, int height)
        {
            if (!SquareToQuad(quad).TryInvert(out var toSquare))
                throw new ArgumentException("The corners don't make a usable shape.", nameof(quad));
            return SKMatrix.CreateScale(width, height).PreConcat(toSquare);
        }

        /// <summary>A sensible output size for <paramref name="quad"/>: the average of its
        /// opposite edges' lengths.</summary>
        public static SKSizeI PerspectiveCropSize(SKPoint[] quad)
        {
            static float Length(SKPoint a, SKPoint b) => SKPoint.Distance(a, b);
            var width = (Length(quad[0], quad[1]) + Length(quad[3], quad[2])) / 2;
            var height = (Length(quad[0], quad[3]) + Length(quad[1], quad[2])) / 2;
            return new SKSizeI(Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
        }

        /// <summary>Content-Aware Scale: every layer and mask carved by the same plan.</summary>
        public void ContentAwareScaleLayers(Filters.SeamCarvingPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ScaleGuides((float)plan.NewWidth / plan.Width, (float)plan.NewHeight / plan.Height, plan.NewWidth, plan.NewHeight);
            RebuildLayers(plan.NewWidth, plan.NewHeight, (canvas, source) =>
            {
                using var carved = plan.Apply(source);
                canvas.DrawBitmap(carved, 0, 0);
            });
        }

        /// <summary>Every layer (and mask) is warped so <paramref name="quad"/> fills a
        /// <paramref name="width"/> x <paramref name="height"/> canvas. Guides can't follow
        /// a perspective change, so they're removed.</summary>
        public void PerspectiveCropLayers(SKPoint[] quad, int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            var matrix = QuadToRect(quad, width, height);
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            Guides.Clear();

            RebuildLayers(width, height, (canvas, source) =>
            {
                using var image = SKImage.FromBitmap(source);
                canvas.Save();
                canvas.SetMatrix(matrix);
                canvas.DrawImage(image, 0, 0, sampling);
                canvas.Restore();
            });
        }
    }
}
