using System;
using SkiaSharp;

namespace JolieCat.UI.Rendering
{
    /// <summary>
    /// Draws the canvas surface content. For now that's a test pattern - a checkerboard
    /// "transparency" backdrop plus a handful of sample vector shapes - proving the Skia
    /// render pipeline works end-to-end and scales correctly with the control's size.
    /// This is a placeholder: the real per-document render pipeline (layers, brush
    /// strokes, vector paths, driven by <c>JolieCat.Core</c>'s scene graph) replaces it.
    /// </summary>
    public static class CanvasRenderer
    {
        private const int CheckerSize = 16;

        private static readonly SKColor CheckerLight = new(0x3A, 0x3A, 0x3D);
        private static readonly SKColor CheckerDark = new(0x2A, 0x2A, 0x2C);
        private static readonly SKColor AccentStroke = new(0x3D, 0x8B, 0xFD);
        private static readonly SKColor AccentFill = new(0x3D, 0x8B, 0xFD, 0x40);

        public static void Render(SKCanvas canvas, SKImageInfo info)
        {
            canvas.Clear(SKColors.Black);
            DrawCheckerboard(canvas, info);
            DrawSampleVectors(canvas, info);
        }

        private static void DrawCheckerboard(SKCanvas canvas, SKImageInfo info)
        {
            using var lightPaint = new SKPaint { Color = CheckerLight, Style = SKPaintStyle.Fill };
            using var darkPaint = new SKPaint { Color = CheckerDark, Style = SKPaintStyle.Fill };

            for (var y = 0; y < info.Height; y += CheckerSize)
            {
                for (var x = 0; x < info.Width; x += CheckerSize)
                {
                    var isLight = (x / CheckerSize + y / CheckerSize) % 2 == 0;
                    canvas.DrawRect(x, y, CheckerSize, CheckerSize, isLight ? lightPaint : darkPaint);
                }
            }
        }

        private static void DrawSampleVectors(SKCanvas canvas, SKImageInfo info)
        {
            var centerX = info.Width / 2f;
            var centerY = info.Height / 2f;
            var extent = Math.Min(info.Width, info.Height) * 0.3f;

            using var strokePaint = new SKPaint
            {
                Color = AccentStroke,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                IsAntialias = true,
            };

            using var fillPaint = new SKPaint
            {
                Color = AccentFill,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };

            // Sample rectangle (top-left quadrant of the sample cluster).
            var rect = new SKRect(centerX - extent, centerY - extent, centerX, centerY);
            canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, strokePaint);

            // Sample ellipse (top-right quadrant).
            var oval = new SKRect(centerX, centerY - extent, centerX + extent, centerY);
            canvas.DrawOval(oval, fillPaint);
            canvas.DrawOval(oval, strokePaint);

            // Sample vector path - a triangle - proves path rendering alongside primitives.
            using var path = new SKPath();
            path.MoveTo(centerX - extent / 2, centerY + extent);
            path.LineTo(centerX + extent / 2, centerY + extent);
            path.LineTo(centerX, centerY + extent / 2);
            path.Close();
            canvas.DrawPath(path, fillPaint);
            canvas.DrawPath(path, strokePaint);
        }
    }
}
