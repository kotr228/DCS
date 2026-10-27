using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Content-Aware Fill / Spot Healing Brush =================
        //
        // Both rebuild an area from the texture around it (Core.Filters.ContentAwareFill).
        // Content-Aware Fill uses the selection; the Spot Healing Brush uses the area
        // painted over in one stroke, shown as a dark overlay until the mouse is released.

        /// <summary>Edit &gt; Content-Aware Fill (Shift+F5): replaces the selected area of
        /// the active layer with texture from around it. Needs a selection. Undoable.</summary>
        [RelayCommand]
        public void ContentAwareFill()
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path) return;

            // A pixel of overlap past the outline, so no seam of the old object stays.
            using var stroke = new SKPaint { Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 2, IsAntialias = true };
            HealArea(canvas => canvas.DrawPath(path, stroke), "Content-Aware Fill", throughSelection: true);
        }

        private SKPath? _spotHealPath;
        private float _spotHealSize;

        /// <summary>The Spot Healing Brush's stroke so far (document space), drawn as a
        /// translucent overlay; null when no stroke is in progress.</summary>
        public SKPath? SpotHealingStroke => _spotHealPath;

        public float SpotHealingStrokeWidth => _spotHealSize;

        private void BeginSpotHealing(SKPoint doc)
        {
            _spotHealSize = _toolbox.CurrentToolOptions is PaintToolOptionsViewModel options ? (float)Math.Max(1, options.Size) : 20f;
            _spotHealPath = new SKPath();
            _spotHealPath.MoveTo(doc);
            _spotHealPath.LineTo(doc.X + 0.01f, doc.Y); // a click alone still marks a dot
            RaiseInvalidate();
        }

        private void UpdateSpotHealing(SKPoint doc)
        {
            _spotHealPath?.LineTo(doc);
            RaiseInvalidate();
        }

        private void CommitSpotHealing()
        {
            if (_spotHealPath is not { } path) return;
            _spotHealPath = null;

            using (path)
            using (var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _spotHealSize,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            })
            {
                // Spot healing respects the selection, like any other brush.
                HealArea(canvas => canvas.DrawPath(path, paint), "Spot Healing Brush", throughSelection: true);
            }
        }

        private void CancelSpotHealing()
        {
            _spotHealPath?.Dispose();
            _spotHealPath = null;
        }

        /// <summary>Rebuilds the area drawn by <paramref name="drawArea"/> on the active
        /// layer (or its mask) and records one undo step.</summary>
        private void HealArea(Action<SKCanvas> drawArea, string historyName, bool throughSelection)
        {
            var layer = Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsLocked || layer.IsAdjustment || layer.IsGroup)
            {
                RaiseInvalidate();
                return;
            }

            RasterizeIfText(layer);
            var target = layer.PaintBitmap;
            var width = target.Width;
            var height = target.Height;

            using var areaMask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var maskCanvas = new SKCanvas(areaMask))
            {
                maskCanvas.Clear(SKColors.Transparent);
                drawArea(maskCanvas);
            }

            var hole = new bool[width * height];
            var any = false;
            var maskBytes = areaMask.GetPixelSpan();
            var rowBytes = areaMask.RowBytes;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (maskBytes[y * rowBytes + x] == 0) continue;
                    hole[y * width + x] = true;
                    any = true;
                }
            if (!any)
            {
                RaiseInvalidate();
                return;
            }

            var before = target.Pixels;
            SKColor[] filled;
            var previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                filled = Core.Filters.ContentAwareFill.Fill(before, width, height, hole);
            }
            finally
            {
                Mouse.OverrideCursor = previousCursor;
            }

            using var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = filled };
            using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
            if (throughSelection)
                WithSelectionClip(layer.PaintCanvas, canvas => canvas.DrawBitmap(result, 0, 0, copy));
            else
                layer.PaintCanvas.DrawBitmap(result, 0, 0, copy);

            Layers.History.Push(PixelEdit(layer, before), historyName);
            RaiseInvalidate();
        }
    }
}
