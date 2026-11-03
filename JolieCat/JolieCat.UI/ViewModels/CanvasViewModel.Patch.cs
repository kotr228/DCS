using System;
using System.Windows.Input;
using JolieCat.Core.Filters;
using JolieCat.Shared.Enums;
using JolieCat.UI.ViewModels.ToolOptions;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>What a Patch/Content-Aware Move drag does when it's let go.</summary>
    public enum PatchAction
    {
        /// <summary>Rebuild the selected area from where it was dragged to (Patch, Source).</summary>
        HealFromDrop,

        /// <summary>Copy the selected content to where it was dragged to (Patch, Destination;
        /// Content-Aware Move, Extend).</summary>
        CopyToDrop,

        /// <summary>Copy the content to the drop place and fill the place it left
        /// (Content-Aware Move, Move).</summary>
        MoveToDrop,
    }

    /// <summary>
    /// The Patch and Content-Aware Move tools: draw around an area (or use any
    /// selection), then drag the selection. Depending on the tool and its mode the
    /// area is rebuilt from the drop place, copied to it, or moved to it with the
    /// old place filled in. Every result is healed into its surroundings so no seam
    /// shows. Pressing outside a selection draws a new one (freehand, like the Lasso).
    /// </summary>
    public partial class CanvasViewModel
    {
        private bool _isPatching;
        private SKPoint _patchStart;
        private SKPath? _patchPath;

        private PatchAction CurrentPatchAction => _toolbox.CurrentToolOptions switch
        {
            PatchToolOptionsViewModel { Mode: PatchMode.Destination } => PatchAction.CopyToDrop,
            PatchToolOptionsViewModel => PatchAction.HealFromDrop,
            ContentAwareMoveToolOptionsViewModel { Mode: ContentAwareMoveMode.Extend } => PatchAction.CopyToDrop,
            _ => _toolbox.ActiveTool.Type == ToolType.ContentAwareMove ? PatchAction.MoveToDrop : PatchAction.HealFromDrop,
        };

        /// <returns>True if the press was handled.</returns>
        private bool BeginPatch(SKPoint doc, Core.Documents.Layer? layer)
        {
            var selection = Layers.Scene.Selection;
            if (selection.HasSelection && selection.Path is { } path && path.Contains(doc.X, doc.Y))
            {
                if (layer is null || layer.IsLocked || layer.IsGroup || layer.IsAdjustment || BlockedBySmartFilters(layer)) return true;
                _isPatching = true;
                _patchStart = doc;
                _patchPath = new SKPath(path);
                ToolHintText = CurrentPatchAction == PatchAction.HealFromDrop
                    ? "Drag onto clean texture, then let go"
                    : "Drag to where it should go, then let go";
                return true;
            }

            // Otherwise: draw the area, freehand.
            CaptureSelectionCombine();
            _isDrawingLasso = true;
            _lassoPath = new SKPath();
            _lassoPath.MoveTo(doc);
            LiveSelectionPath = _lassoPath;
            RaiseInvalidate();
            return true;
        }

        private bool UpdatePatch(SKPoint doc)
        {
            if (!_isPatching || _patchPath is null) return false;
            var moved = new SKPath(_patchPath);
            moved.Transform(SKMatrix.CreateTranslation(MathF.Round(doc.X - _patchStart.X), MathF.Round(doc.Y - _patchStart.Y)));
            LiveSelectionPath = moved; // where the area will come from / go to
            RaiseInvalidate();
            return true;
        }

        private void EndPatch(SKPoint doc)
        {
            if (!_isPatching) return;
            _isPatching = false;
            LiveSelectionPath = null;
            ToolHintText = string.Empty;
            var path = _patchPath;
            _patchPath = null;
            if (path is null) return;

            using (path)
            {
                var dx = (int)MathF.Round(doc.X - _patchStart.X);
                var dy = (int)MathF.Round(doc.Y - _patchStart.Y);
                if ((dx == 0 && dy == 0) || Layers.ActiveLayer?.Model is not { } layer)
                {
                    RaiseInvalidate();
                    return;
                }

                var action = CurrentPatchAction;
                ApplyPatch(layer, path, dx, dy, action);

                // Copies and moves leave the selection on the new place, like Photoshop.
                if (action != PatchAction.HealFromDrop && Layers.Scene.Selection.Path is { } selected)
                {
                    var moved = new SKPath(selected);
                    moved.Transform(SKMatrix.CreateTranslation(dx, dy));
                    Layers.Scene.Selection.SetPath(moved, Layers.DocumentWidth, Layers.DocumentHeight);
                }
            }
        }

        /// <summary>Applies a patch drag of (<paramref name="dx"/>, <paramref name="dy"/>)
        /// for the area inside <paramref name="path"/> on <paramref name="layer"/> (or its
        /// mask). One undo step.</summary>
        internal void ApplyPatch(Core.Documents.Layer layer, SKPath path, int dx, int dy, PatchAction action = PatchAction.HealFromDrop)
        {
            RasterizeIfText(layer);
            var target = layer.PaintBitmap;
            int width = target.Width, height = target.Height;

            // Where the pixels end up, with its soft (antialiased/feathered) edge: the
            // selected area itself, or the place it was dropped.
            var destination = new SKPath(path);
            if (action != PatchAction.HealFromDrop) destination.Transform(SKMatrix.CreateTranslation(dx, dy));
            var (destMask, destRegion) = Rasterize(destination, width, height);
            destination.Dispose();
            if (destRegion is null) return;

            var before = target.Pixels;
            var start = before;

            var previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                if (action == PatchAction.MoveToDrop)
                {
                    // First fill the place the content leaves, from its surroundings.
                    var (_, sourceRegion) = Rasterize(path, width, height);
                    if (sourceRegion is not null)
                        start = Core.Filters.ContentAwareFill.Fill(before, width, height, sourceRegion);
                }

                // The replacement pixels for each destination pixel. Healing from the
                // drop place reads (dx, dy) away; copying reads (dx, dy) back. Copies
                // always read the original image, so moved content isn't its own fill.
                var offsetX = action == PatchAction.HealFromDrop ? dx : -dx;
                var offsetY = action == PatchAction.HealFromDrop ? dy : -dy;
                var source = new SKColor[before.Length];
                for (var y = 0; y < height; y++)
                {
                    var sy = Math.Clamp(y + offsetY, 0, height - 1);
                    for (var x = 0; x < width; x++)
                        source[y * width + x] = before[sy * width + Math.Clamp(x + offsetX, 0, width - 1)];
                }

                // Patching texture: heal it in fully (Poisson), so it takes on the colors
                // around it. Copying/moving an object: keep the object as it is and only
                // fade its edge into the new place - healing would tint it.
                SKColor[] pasted;
                byte[] weight;
                if (action == PatchAction.HealFromDrop)
                {
                    pasted = HealingBlend.Blend(start, source, width, height, destRegion);
                    weight = destMask;
                }
                else
                {
                    pasted = source;
                    weight = SoftenInside(destMask, width, height, EdgeFade);
                }

                var result = new SKColor[before.Length];
                for (var i = 0; i < result.Length; i++)
                {
                    var a = weight[i];
                    result[i] = a switch
                    {
                        0 => start[i],
                        255 => pasted[i],
                        _ => Lerp(start[i], pasted[i], a / 255f),
                    };
                }

                target.Pixels = result;
            }
            finally
            {
                Mouse.OverrideCursor = previousCursor;
            }

            var name = _toolbox.ActiveTool.Type == ToolType.ContentAwareMove ? "Content-Aware Move" : "Patch";
            Layers.History.Push(PixelEdit(layer, before), name);
            RaiseInvalidate();
        }

        /// <summary>How far (pixels) a copied/moved object's edge fades into its new place.</summary>
        private const int EdgeFade = 4;

        /// <summary>A coverage mask whose edge fades out over <paramref name="fade"/> pixels
        /// inside the area (never spreading outside it).</summary>
        private static byte[] SoftenInside(byte[] coverage, int width, int height, int fade)
        {
            using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(coverage, 0, mask.GetPixels(), coverage.Length);
            using var blurred = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(blurred))
            using (var filter = SKImageFilter.CreateBlur(fade / 2f, fade / 2f))
            using (var paint = new SKPaint { ImageFilter = filter })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(mask, 0, 0, paint);
            }

            // The blur is half-strength right at the edge; stretching 0.5..1 to 0..1
            // keeps the fade inside the area.
            var bytes = blurred.GetPixelSpan();
            var rowBytes = blurred.RowBytes;
            var result = new byte[coverage.Length];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (coverage[i] == 0) continue;
                    var v = Math.Clamp((bytes[y * rowBytes + x] / 255f - 0.5f) * 2f, 0f, 1f);
                    result[i] = (byte)Math.Round(Math.Min(v * 255, coverage[i]));
                }
            return result;
        }

        /// <summary>The path's coverage per pixel (0-255) and which pixels it touches
        /// (null if none).</summary>
        private static (byte[] Coverage, bool[]? Region) Rasterize(SKPath path, int width, int height)
        {
            using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(mask))
            using (var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawPath(path, fill);
            }

            var bytes = mask.GetPixelSpan();
            var rowBytes = mask.RowBytes;
            var coverage = new byte[width * height];
            var region = new bool[width * height];
            var any = false;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var v = bytes[y * rowBytes + x];
                    coverage[y * width + x] = v;
                    if (v > 0) { region[y * width + x] = true; any = true; }
                }
            return (coverage, any ? region : null);
        }

        private static SKColor Lerp(SKColor a, SKColor b, float t)
        {
            byte L(byte p, byte q) => (byte)Math.Round(p + (q - p) * t);
            return new SKColor(L(a.Red, b.Red), L(a.Green, b.Green), L(a.Blue, b.Blue), L(a.Alpha, b.Alpha));
        }
    }
}
