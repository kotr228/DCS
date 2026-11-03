using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public enum StrokeLocation
    {
        Inside,
        Center,
        Outside,
    }

    /// <summary>
    /// Edit > Stroke and Edit > Fade, and the Select menu's Grow / Similar / Load Layer
    /// Transparency / Save and Load Selection.
    /// </summary>
    public partial class CanvasViewModel
    {
        // ================= Edit > Stroke =================

        /// <summary>Draws a line of <paramref name="width"/> pixels along the selection's
        /// edge on the active layer (or its mask). One undo step.</summary>
        public void StrokeSelection(float width, SKColor color, StrokeLocation location, double opacityPercent = 100)
        {
            var layer = Layers.ActiveLayer?.Model;
            var selection = Layers.Scene.Selection;
            if (layer is null || layer.IsLocked || layer.IsGroup && !layer.PaintsMask || !selection.HasSelection || selection.Path is not { } path) return;
            if (BlockedBySmartFilters(layer)) return;
            RasterizeIfText(layer);

            var before = layer.PaintBitmap.Pixels;
            var canvas = layer.PaintCanvas;
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                // Inside/Outside: twice as wide, then half is clipped away.
                StrokeWidth = location == StrokeLocation.Center ? width : width * 2,
                Color = color.WithAlpha((byte)Math.Clamp(Math.Round(opacityPercent * 2.55), 0, 255)),
                IsAntialias = true,
                StrokeJoin = SKStrokeJoin.Miter,
            };

            canvas.Save();
            if (location == StrokeLocation.Inside) canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
            if (location == StrokeLocation.Outside) canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);
            canvas.DrawPath(path, paint);
            canvas.Restore();

            Layers.History.Push(PixelEdit(layer, before), "Stroke");
            RaiseInvalidate();
        }

        // ================= Edit > Fade =================

        private sealed record FadeState(Guid LayerId, SKColor[] Before, SKColor[] After, Core.History.IEditCommand Command);

        private FadeState? _fade;

        /// <summary>Remembered by every pixel edit: Fade can mix its before and after.</summary>
        private void RememberForFade(Layer layer, bool isMask, SKColor[] before, SKColor[] after, Core.History.IEditCommand command) =>
            _fade = isMask ? null : new FadeState(layer.Id, before, after, command);

        /// <summary>True right after a filter, adjustment, fill or paint stroke on a
        /// layer's own pixels, until something else is done.</summary>
        public bool CanFade =>
            _fade is { } f && ReferenceEquals(Layers.History.LastCommand, f.Command) && Layers.ActiveLayer?.Model.Id == f.LayerId;

        /// <summary>Starts Edit > Fade: a live preview of the last edit mixed back with how
        /// the layer was before it.</summary>
        public bool BeginFade(string name)
        {
            if (!CanFade) return false;
            BeginAdjustment($"Fade {name}");
            return true;
        }

        /// <summary>Previews the fade: the last edit at <paramref name="opacityPercent"/>,
        /// combined with the earlier pixels through <paramref name="mode"/>.</summary>
        public void UpdateFade(double opacityPercent, Shared.Enums.BlendMode mode)
        {
            if (_fade is not { } f) return;
            UpdateAdjustmentPreview(current => Faded(current.Width, current.Height, f.Before, f.After, opacityPercent, mode));
        }

        internal static SKBitmap Faded(int width, int height, SKColor[] before, SKColor[] after, double opacityPercent, Shared.Enums.BlendMode mode)
        {
            var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = before };
            using var edited = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = after };
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(opacityPercent * 2.55), 0, 255)),
                BlendMode = Layer.ToSkiaBlendMode(mode),
            };
            canvas.DrawBitmap(edited, 0, 0, paint);
            return result;
        }

        // ================= Select menu =================

        /// <summary>Select > Load Layer Transparency (also Ctrl+click on a layer's
        /// thumbnail): selects the layer's visible pixels.</summary>
        public void SelectLayerTransparency(Layer? layer = null, SelectionCombine mode = SelectionCombine.New)
        {
            layer ??= Layers.ActiveLayer?.Model;
            if (layer is null || layer.IsGroup) return;
            var path = SelectionOps.FromAlpha(layer.Bitmap);
            ApplySelectionPath(path, mode);
        }

        /// <summary>Select > Grow (<paramref name="contiguous"/>) or Similar: widens the
        /// selection to pixels of similar color on the active layer.</summary>
        public void GrowSelection(int tolerance, bool contiguous)
        {
            var layer = Layers.ActiveLayer?.Model;
            var selection = Layers.Scene.Selection;
            if (layer is null || layer.IsGroup || !selection.HasSelection || selection.Region is not { } region) return;
            var path = contiguous ? SelectionOps.Grow(layer.Bitmap, region, tolerance) : SelectionOps.Similar(layer.Bitmap, region, tolerance);
            ApplySelectionPath(path, SelectionCombine.New);
        }

        public IReadOnlyList<string> SavedSelectionNames => Layers.Scene.SavedSelections.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Select > Save Selection: keeps the current selection under
        /// <paramref name="name"/> (saved with the project). False if nothing is selected.</summary>
        public bool SaveSelection(string name)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path || string.IsNullOrWhiteSpace(name)) return false;
            Layers.Scene.SavedSelections[name.Trim()] = path.ToSvgPathData();
            Layers.History.MarkModified();
            return true;
        }

        /// <summary>Select > Load Selection: brings back a saved selection - as the new
        /// selection or combined with the current one.</summary>
        public bool LoadSelection(string name, SelectionCombine mode = SelectionCombine.New)
        {
            if (!Layers.Scene.SavedSelections.TryGetValue(name, out var svg)) return false;
            ApplySelectionPath(SKPath.ParseSvgPathData(svg), mode);
            return true;
        }

        // ================= Select and Mask =================

        /// <summary>Shows what Select and Mask would make of the selection (as the live outline).</summary>
        public void PreviewRefineSelection(float smooth, float shiftEdge)
        {
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path) return;
            LiveSelectionPath = SelectionOps.Refine(path, Layers.DocumentWidth, Layers.DocumentHeight, smooth, shiftEdge) ?? new SKPath();
            RaiseInvalidate();
        }

        /// <summary>Applies Select and Mask: the cleaned-up outline, with <paramref name="feather"/> px of softness.</summary>
        public void RefineSelection(float smooth, float shiftEdge, float feather)
        {
            LiveSelectionPath = null;
            var selection = Layers.Scene.Selection;
            if (!selection.HasSelection || selection.Path is not { } path) return;
            var refined = SelectionOps.Refine(path, Layers.DocumentWidth, Layers.DocumentHeight, smooth, shiftEdge);
            if (refined is null) selection.Clear();
            else
            {
                selection.SetPath(refined, Layers.DocumentWidth, Layers.DocumentHeight);
                selection.Feather = feather;
            }
            RaiseInvalidate();
        }

        public void CancelRefineSelection()
        {
            LiveSelectionPath = null;
            RaiseInvalidate();
        }

        private void ApplySelectionPath(SKPath? path, SelectionCombine mode)
        {
            var selection = Layers.Scene.Selection;
            if (path is null)
            {
                if (mode == SelectionCombine.New) selection.Clear();
            }
            else
            {
                selection.Combine(path, mode, Layers.DocumentWidth, Layers.DocumentHeight);
            }
            RaiseInvalidate();
        }
    }
}
