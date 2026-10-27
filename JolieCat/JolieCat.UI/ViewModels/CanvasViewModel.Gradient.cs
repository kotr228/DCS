using System;
using System.Windows.Input;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Gradient tool =================
        //
        // Drag to define the gradient: the press point gets the first color, the
        // release point the second. Shift snaps the direction to 45° steps. While
        // dragging, the gradient is previewed over the canvas with a guide line.

        private bool _isDraggingGradient;
        private SKPoint _gradientStart;
        private SKPoint _gradientEnd;

        /// <summary>The live gradient guide (start, end) while dragging, for the renderer.</summary>
        public (SKPoint Start, SKPoint End)? GradientGuide =>
            _isDraggingGradient ? (_gradientStart, _gradientEnd) : null;

        private void BeginGradient(SKPoint doc)
        {
            _isDraggingGradient = true;
            _gradientStart = doc;
            _gradientEnd = doc;
        }

        private void UpdateGradient(SKPoint doc)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                var dx = doc.X - _gradientStart.X;
                var dy = doc.Y - _gradientStart.Y;
                var angle = MathF.Round(MathF.Atan2(dy, dx) / (MathF.PI / 4)) * (MathF.PI / 4);
                var length = MathF.Sqrt(dx * dx + dy * dy);
                doc = new SKPoint(_gradientStart.X + MathF.Cos(angle) * length, _gradientStart.Y + MathF.Sin(angle) * length);
            }

            _gradientEnd = doc;
            RaiseInvalidate();
        }

        private void CommitGradient()
        {
            _isDraggingGradient = false;

            var layer = Layers.ActiveLayer?.Model;
            var dx = _gradientEnd.X - _gradientStart.X;
            var dy = _gradientEnd.Y - _gradientStart.Y;
            if (layer is null || layer.IsLocked || dx * dx + dy * dy < 1)
            {
                RaiseInvalidate();
                return;
            }

            var before = layer.PaintBitmap.Pixels;
            WithSelectionClip(layer.PaintCanvas, canvas => DrawGradient(canvas, _gradientStart, _gradientEnd));
            Layers.History.Push(PixelEdit(layer, before));
            RaiseInvalidate();
        }

        /// <summary>Live preview, drawn over the composite by the renderer.</summary>
        private void DrawLiveGradient(SKCanvas canvas)
        {
            if (!_isDraggingGradient) return;

            var selection = Layers.Scene.Selection;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, Layers.DocumentWidth, Layers.DocumentHeight));
            if (selection.HasSelection && selection.Path is { } clip)
                canvas.ClipPath(clip, antialias: true);
            DrawGradient(canvas, _gradientStart, _gradientEnd);
            canvas.Restore();
        }

        private void DrawGradient(SKCanvas canvas, SKPoint start, SKPoint end)
        {
            var options = _toolbox.CurrentToolOptions as GradientToolOptionsViewModel ?? new GradientToolOptionsViewModel();

            var first = BrushColor;
            var second = options.Colors == GradientColors.ForegroundToTransparent ? first.WithAlpha(0) : SecondaryBrushColor;
            if (options.Reverse) (first, second) = (second, first);

            using var shader = CreateGradientShader(options.Kind, start, end, first, second);
            using var paint = new SKPaint
            {
                Shader = shader,
                IsDither = true,
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(options.Opacity / 100 * 255, 0, 255)),
            };
            canvas.DrawRect(new SKRect(0, 0, Layers.DocumentWidth, Layers.DocumentHeight), paint);
        }

        internal static SKShader CreateGradientShader(GradientKind kind, SKPoint start, SKPoint end, SKColor first, SKColor second)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));

            switch (kind)
            {
                case GradientKind.Radial:
                    return SKShader.CreateRadialGradient(start, length, new[] { first, second }, null, SKShaderTileMode.Clamp);

                case GradientKind.Angle:
                {
                    // Sweep starts along the drag direction and turns clockwise.
                    var degrees = MathF.Atan2(dy, dx) * 180f / MathF.PI;
                    var rotation = SKMatrix.CreateRotationDegrees(degrees, start.X, start.Y);
                    return SKShader.CreateSweepGradient(start, new[] { first, second }, null, rotation);
                }

                case GradientKind.Reflected:
                {
                    var mirrored = new SKPoint(start.X - dx, start.Y - dy);
                    return SKShader.CreateLinearGradient(mirrored, end, new[] { second, first, second }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
                }

                default:
                    return SKShader.CreateLinearGradient(start, end, new[] { first, second }, null, SKShaderTileMode.Clamp);
            }
        }
    }
}
