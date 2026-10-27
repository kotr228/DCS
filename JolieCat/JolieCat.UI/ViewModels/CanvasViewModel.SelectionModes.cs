using System;
using System.Windows.Input;
using JolieCat.Core.Documents;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Selection combine modes =================

        /// <summary>How the selection gesture in progress combines with the existing
        /// selection - decided when the gesture starts, like Photoshop.</summary>
        private SelectionCombine _selectionCombine = SelectionCombine.New;

        private SelectionToolOptionsViewModel? SelectionOptions => _toolbox.CurrentToolOptions as SelectionToolOptionsViewModel;

        /// <summary>Shift+Alt = intersect, Shift = add, Alt = subtract, otherwise the
        /// mode chosen in the tool options.</summary>
        private void CaptureSelectionCombine()
        {
            var modifiers = Keyboard.Modifiers;
            var shift = (modifiers & ModifierKeys.Shift) != 0;
            var alt = (modifiers & ModifierKeys.Alt) != 0;

            _selectionCombine = shift && alt ? SelectionCombine.Intersect
                : shift ? SelectionCombine.Add
                : alt ? SelectionCombine.Subtract
                : SelectionOptions?.Mode ?? SelectionCombine.New;
        }

        /// <summary>Applies a finished selection shape with the captured combine mode
        /// and the tool's feather.</summary>
        private void ApplySelectionShape(SKPath? shape)
        {
            var selection = Layers.Scene.Selection;
            var bounds = shape?.Bounds ?? SKRect.Empty;
            var tooSmall = shape is null || bounds.Width < MinSelectionExtent || bounds.Height < MinSelectionExtent;

            if (tooSmall)
            {
                // A plain click deselects in New mode; in the other modes it does nothing.
                if (_selectionCombine == SelectionCombine.New) selection.Clear();
            }
            else
            {
                selection.Combine(shape!, _selectionCombine, Layers.DocumentWidth, Layers.DocumentHeight);
                if (selection.HasSelection && SelectionOptions is { } options && (_selectionCombine == SelectionCombine.New || options.FeatherRadius > 0))
                    selection.Feather = (float)options.FeatherRadius;
            }

            RaiseInvalidate();
        }

        /// <summary>Select &gt; Modify &gt; Feather.</summary>
        public void SetSelectionFeather(float pixels)
        {
            if (!Layers.Scene.Selection.HasSelection) return;
            Layers.Scene.Selection.Feather = pixels;
            RaiseInvalidate();
        }

        // ================= Feathered clipping =================

        private SKBitmap? _featherMask;
        private int _featherMaskVersion = -1;
        private SKSizeI _featherMaskSize;

        /// <summary>The selection as a soft alpha mask (document-sized), rebuilt only
        /// when the selection changes.</summary>
        private SKBitmap? FeatherMask()
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path || selection.Feather <= 0) return null;

            var size = new SKSizeI(Layers.DocumentWidth, Layers.DocumentHeight);
            if (_featherMask is not null && _featherMaskVersion == selection.Version && _featherMaskSize == size)
                return _featherMask;

            _featherMask?.Dispose();
            _featherMask = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(_featherMask))
            using (var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, selection.Feather / 2f),
            })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPath(path, paint);
            }

            _featherMaskVersion = selection.Version;
            _featherMaskSize = size;
            return _featherMask;
        }

        /// <summary>
        /// Runs <paramref name="draw"/> limited to the selection. A hard selection
        /// clips by its path (anti-aliased); a feathered one draws into a temporary
        /// layer and fades it out through the soft mask. <paramref name="area"/> (the
        /// part being drawn, in document pixels) keeps that temporary layer small for
        /// brush strokes.
        /// </summary>
        private void WithSelectionClip(SKCanvas canvas, Action<SKCanvas> draw, SKRect? area = null)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } clipPath)
            {
                draw(canvas);
                return;
            }

            if (FeatherMask() is not { } mask)
            {
                canvas.Save();
                canvas.ClipPath(clipPath, SKClipOperation.Intersect, antialias: true);
                draw(canvas);
                canvas.Restore();
                return;
            }

            var bounds = area ?? new SKRect(0, 0, Layers.DocumentWidth, Layers.DocumentHeight);
            canvas.SaveLayer(bounds, null);
            draw(canvas);
            using (var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn })
                canvas.DrawBitmap(mask, 0, 0, maskPaint);
            canvas.Restore();
        }

        /// <summary>Filters/adjustments: keeps <paramref name="filtered"/> only inside
        /// the selection (softly, if feathered), the original everywhere else.</summary>
        private void BlendThroughSelection(SKCanvas canvas, SKBitmap filtered)
        {
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            if (FeatherMask() is { } mask)
            {
                // Original stays underneath; the filtered copy goes on top, faded by the mask.
                canvas.SaveLayer();
                canvas.DrawBitmap(filtered, 0, 0, paint);
                using (var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn })
                    canvas.DrawBitmap(mask, 0, 0, maskPaint);
                canvas.Restore();
                return;
            }

            canvas.ClipPath(Layers.Scene.Selection.Path!, SKClipOperation.Intersect, antialias: true);
            canvas.DrawBitmap(filtered, 0, 0, paint);
        }

        // ================= Quick Selection =================
        //
        // A brush that grows the selection: at every point along the stroke it flood-
        // selects similar colors (Tolerance) around the brush - limited to a window a
        // few brush-widths across, so it stays local - plus the brush circle itself.
        // The first stroke starts a new selection, later ones add to it; Alt subtracts.

        private bool _isQuickSelecting;
        private SKRegion? _quickRegion;
        private SKColor[]? _quickPixels;
        private SKSizeI _quickSize;
        private SKPoint _quickLast;

        private void BeginQuickSelection(Core.Documents.Layer layer, SKPoint doc)
        {
            CaptureSelectionCombine();
            if (_selectionCombine == SelectionCombine.New && Layers.Scene.Selection.HasSelection)
                _selectionCombine = SelectionCombine.Add;

            _isQuickSelecting = true;
            _quickPixels = layer.Bitmap.Pixels;
            _quickSize = new SKSizeI(layer.Bitmap.Width, layer.Bitmap.Height);
            _quickRegion = new SKRegion();
            _quickLast = doc;
            QuickSelectAt(doc);
        }

        private void UpdateQuickSelection(SKPoint doc)
        {
            if (!_isQuickSelecting) return;

            // Sample every half brush-radius along the drag, so fast strokes don't skip.
            var radius = QuickBrushRadius;
            var step = Math.Max(1f, radius / 2f);
            var dx = doc.X - _quickLast.X;
            var dy = doc.Y - _quickLast.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < step) return;

            for (var t = step; t <= distance; t += step)
                QuickSelectAt(new SKPoint(_quickLast.X + dx * t / distance, _quickLast.Y + dy * t / distance));

            _quickLast = doc;
        }

        private float QuickBrushRadius => (float)Math.Max(1, (SelectionOptions?.BrushSize ?? 40) / 2);

        private void QuickSelectAt(SKPoint point)
        {
            if (_quickPixels is null || _quickRegion is null) return;

            var radius = QuickBrushRadius;
            var x = (int)point.X;
            var y = (int)point.Y;
            var reach = (int)MathF.Ceiling(radius * 3);
            var window = new SKRectI(x - reach, y - reach, x + reach + 1, y + reach + 1);
            var tolerance = (float)(SelectionOptions?.Tolerance ?? 32);

            using (var grown = Selection.CreateRegionFromColorFlood(_quickPixels, _quickSize.Width, _quickSize.Height, x, y, tolerance, window))
                _quickRegion.Op(grown, SKRegionOperation.Union);

            using (var brush = new SKPath())
            using (var bounds = new SKRegion(new SKRectI(0, 0, _quickSize.Width, _quickSize.Height)))
            using (var disc = new SKRegion())
            {
                brush.AddCircle(point.X, point.Y, radius);
                disc.SetPath(brush, bounds);
                _quickRegion.Op(disc, SKRegionOperation.Union);
            }

            LiveSelectionPath = _quickRegion.IsEmpty ? null : _quickRegion.GetBoundaryPath();
            RaiseInvalidate();
        }

        private void CommitQuickSelection()
        {
            if (!_isQuickSelecting) return;
            _isQuickSelecting = false;

            var shape = _quickRegion is { IsEmpty: false } region ? region.GetBoundaryPath() : null;
            LiveSelectionPath = null;
            _quickRegion?.Dispose();
            _quickRegion = null;
            _quickPixels = null;

            if (shape is not null)
            {
                Layers.Scene.Selection.Combine(shape, _selectionCombine, Layers.DocumentWidth, Layers.DocumentHeight);
                if (Layers.Scene.Selection.HasSelection && SelectionOptions is { FeatherRadius: > 0 } options)
                    Layers.Scene.Selection.Feather = (float)options.FeatherRadius;
            }

            RaiseInvalidate();
        }

        private void CancelQuickSelection()
        {
            _isQuickSelecting = false;
            _quickRegion?.Dispose();
            _quickRegion = null;
            _quickPixels = null;
            LiveSelectionPath = null;
        }
    }
}
