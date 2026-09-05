using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Rendering
{
    /// <summary>
    /// Draws one frame of the center canvas from <see cref="CanvasViewModel"/>'s state:
    /// a checkerboard "transparency" backdrop clipped to the document bounds, the
    /// persistent painted bitmap on top of it, and the marching-ants marquee overlay
    /// when a selection is active - all inside the pan/zoom transform so they move and
    /// scale together. This class owns no state of its own; it only reads the view
    /// model and paints.
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

            var bitmap = viewModel.Bitmap;
            var documentRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);

            DrawCheckerboard(canvas, documentRect);
            canvas.DrawBitmap(bitmap, 0, 0);

            if (viewModel.IsMarqueeActive)
                DrawMarquee(canvas, viewModel);

            canvas.Restore();
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

        private static void DrawMarquee(SKCanvas canvas, CanvasViewModel viewModel)
        {
            // Stroke width and dash length are scaled down by zoom so the marquee reads
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

            var rect = new SKRect(
                (float)viewModel.MarqueeX,
                (float)viewModel.MarqueeY,
                (float)(viewModel.MarqueeX + viewModel.MarqueeWidth),
                (float)(viewModel.MarqueeY + viewModel.MarqueeHeight));

            canvas.DrawRect(rect, paint);
        }
    }
}
