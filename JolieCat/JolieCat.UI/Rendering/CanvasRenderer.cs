using System;
using JolieCat.Core.Documents;
using JolieCat.UI.ViewModels;
using JolieCat.UI.ViewModels.Layers;
using SkiaSharp;

namespace JolieCat.UI.Rendering
{
    /// <summary>
    /// Draws one frame of the center canvas from <see cref="CanvasViewModel"/>'s state: a
    /// checkerboard "transparency" backdrop clipped to the document bounds, every visible
    /// layer composited back-to-front with its own opacity and blend mode, and the
    /// marching-ants marquee overlay when a selection is active - all inside the pan/zoom
    /// transform so they move and scale together. This class owns no state of its own; it
    /// only reads the view model and paints.
    /// </summary>
    public static class CanvasRenderer
    {
        private const int CheckerSize = 16;

        private static readonly SKColor CheckerLight = new(0x3A, 0x3A, 0x3D);
        private static readonly SKColor CheckerDark = new(0x2A, 0x2A, 0x2C);
        private static readonly SKColor OutsideDocumentColor = new(0x12, 0x12, 0x13);

        public static void Render(SKCanvas canvas, SKImageInfo info, CanvasViewModel viewModel)
        {
            // Solid fill for the area outside the document bounds - distinguishes the
            // "desk" from the canvas itself, like the gray surround in Photoshop.
            canvas.Clear(OutsideDocumentColor);

            canvas.Save();
            canvas.Translate((float)viewModel.PanX, (float)viewModel.PanY);
            canvas.Scale((float)viewModel.Zoom);

            var documentRect = new SKRect(0, 0, LayersViewModel.DocumentWidth, LayersViewModel.DocumentHeight);

            DrawCheckerboard(canvas, documentRect);
            DrawLayers(canvas, viewModel.Layers.Scene);
            DrawSelectionOverlay(canvas, viewModel);

            canvas.Restore();
        }

        private static void DrawLayers(SKCanvas canvas, Scene scene)
        {
            // Scene.Layers is already back-to-front, so drawing in list order composites
            // correctly with no extra sorting.
            foreach (var layer in scene.Layers)
            {
                if (!layer.IsVisible) continue;

                using var paint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha((byte)Math.Clamp(layer.Opacity * 255.0, 0, 255)),
                    BlendMode = Layer.ToSkiaBlendMode(layer.BlendMode),
                };

                canvas.DrawBitmap(layer.Bitmap, 0, 0, paint);
            }
        }

        private static void DrawCheckerboard(SKCanvas canvas, SKRect documentRect)
        {
            using var lightPaint = new SKPaint { Color = CheckerLight, Style = SKPaintStyle.Fill };
            using var darkPaint = new SKPaint { Color = CheckerDark, Style = SKPaintStyle.Fill };

            canvas.Save();
            canvas.ClipRect(documentRect);

            for (var y = 0; y < documentRect.Height; y += CheckerSize)
            {
                for (var x = 0; x < documentRect.Width; x += CheckerSize)
                {
                    var isLight = (x / CheckerSize + y / CheckerSize) % 2 == 0;
                    canvas.DrawRect(x, y, CheckerSize, CheckerSize, isLight ? lightPaint : darkPaint);
                }
            }

            canvas.Restore();
        }

        /// <summary>
        /// One dashed "marching ants" outline, uniform across every selection tool: while
        /// a marquee/lasso/polygon is still being drawn, that's <see cref="CanvasViewModel.LiveSelectionPath"/>;
        /// once committed (including Magic Wand/Quick Selection's region-based result,
        /// which was never a simple path to begin with), it's the committed
        /// <c>Scene.Selection</c>'s own boundary, via <c>SKRegion.GetBoundaryPath</c>.
        /// </summary>
        private static void DrawSelectionOverlay(SKCanvas canvas, CanvasViewModel viewModel)
        {
            var path = viewModel.LiveSelectionPath;

            if (path is null)
            {
                var region = viewModel.Layers.Scene.Selection.Region;
                if (region is null || region.IsEmpty) return;

                path = region.GetBoundaryPath();
            }

            // Stroke width and dash length are scaled down by zoom so the outline reads
            // as a crisp ~1px dashed line on screen at any zoom level, matching how a
            // real selection outline behaves rather than zooming with the pixels.
            var onScreenScale = 1f / (float)viewModel.Zoom;

            using var paint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = onScreenScale,
                IsAntialias = false,
                PathEffect = SKPathEffect.CreateDash(new[] { 4f * onScreenScale, 4f * onScreenScale }, 0),
            };

            canvas.DrawPath(path, paint);
        }
    }
}
