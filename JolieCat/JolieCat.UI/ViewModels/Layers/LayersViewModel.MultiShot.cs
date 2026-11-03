using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Edit &gt; Auto-Align Layers and File &gt; Automate &gt; Merge to HDR Pro:
    /// for several shots of one scene stacked as layers (File &gt; Import).</summary>
    public partial class LayersViewModel
    {
        /// <summary>Visible plain pixel layers, bottom first.</summary>
        private List<Layer> ShotLayers() => Scene.Layers
            .Where(l => !l.IsGroup && !l.IsAdjustment && !l.IsText && !l.IsShape && !l.IsFill && l.SmartObject is null
                        && !l.HasSmartFilters && Scene.IsEffectivelyVisible(l))
            .ToList();

        /// <summary>The message the last Auto-Align / Merge to HDR left (for the status bar or a dialog).</summary>
        public string LastMultiShotMessage { get; private set; } = string.Empty;

        /// <summary>Shifts every visible pixel layer to line up with the bottom one
        /// (camera moved a little between shots). One undo step. Shifts only: no
        /// rotation or lens correction.</summary>
        [RelayCommand]
        private void AutoAlignLayers()
        {
            var shots = ShotLayers();
            if (shots.Count < 2)
            {
                LastMultiShotMessage = "Auto-Align needs at least two visible pixel layers.";
                return;
            }

            var reference = shots[0];
            var offsets = shots.Skip(1).Select(l => (Layer: l, Offset: MultiImage.FindOffset(reference.Bitmap, l.Bitmap))).ToList();
            if (offsets.All(o => o.Offset == SKPointI.Empty))
            {
                LastMultiShotMessage = "The layers already line up.";
                return;
            }

            ExecuteStructuralChange(() =>
            {
                foreach (var (layer, offset) in offsets)
                {
                    if (offset == SKPointI.Empty) continue;
                    Shift(layer.Bitmap, offset);
                    if (layer.Mask is { } mask) Shift(mask.Bitmap, offset, SKColors.White);
                }
            });
            LastMultiShotMessage = "Aligned: " + string.Join(", ", offsets.Select(o => $"{o.Layer.Name} ({o.Offset.X}, {o.Offset.Y})"));
            NotifyContentChanged();
        }

        private static void Shift(SKBitmap bitmap, SKPointI by, SKColor? fill = null)
        {
            using var copy = bitmap.Copy();
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(fill ?? SKColors.Transparent);
            canvas.DrawBitmap(copy, by.X, by.Y);
        }

        /// <summary>Blends the visible pixel layers (bracketed exposures of one scene)
        /// into a new layer on top with detail in both shadows and highlights. One
        /// undo step; the source layers are hidden.</summary>
        [RelayCommand]
        private void MergeToHdrPro()
        {
            var shots = ShotLayers();
            if (shots.Count < 2)
            {
                LastMultiShotMessage = "Merge to HDR needs at least two visible pixel layers (the bracketed shots).";
                return;
            }

            using var fused = MultiImage.FuseExposures(shots.Select(l => l.Bitmap).ToList());
            ExecuteStructuralChange(() =>
            {
                var layer = Scene.AddLayer("Merged HDR", DocumentWidth, DocumentHeight);
                layer.Bitmap.Pixels = fused.Pixels;
                foreach (var shot in shots) shot.IsVisible = false;
                Scene.ActiveLayer = layer;
            });
            LastMultiShotMessage = $"Merged {shots.Count} exposures into \"Merged HDR\".";
        }
    }
}
