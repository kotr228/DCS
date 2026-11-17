using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Layers
{
    /// <summary>Layer &gt; Arrange, Align and Distribute.</summary>
    public partial class LayersViewModel
    {
        /// <summary>Layer &gt; Arrange &gt; Bring to Front: the active layer to the top of its level.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void BringToFront()
        {
            if (ActiveLayer?.Model is not { } layer) return;
            ExecuteStructuralChange(() => { for (var i = 0; i < Scene.Layers.Count && Scene.MoveLayerUp(layer); i++) { } });
        }

        /// <summary>Layer &gt; Arrange &gt; Send to Back: the active layer to the bottom of its level.</summary>
        [RelayCommand(CanExecute = nameof(HasActiveLayer))]
        private void SendToBack()
        {
            if (ActiveLayer?.Model is not { } layer) return;
            ExecuteStructuralChange(() => { for (var i = 0; i < Scene.Layers.Count && Scene.MoveLayerDown(layer); i++) { } });
        }

        /// <summary>The layers Align/Distribute move, with what's painted on each (empty ones skipped).</summary>
        private List<(Layer Layer, SKRectI Bounds)> ArrangeTargets()
        {
            var result = new List<(Layer, SKRectI)>();
            foreach (var layer in SelectionForCommand())
            {
                if (layer.IsGroup || layer.Adjustment is not null || layer.IsLocked) continue;
                if (SpriteSheetTools.Bounds(layer.Bitmap, new SKRectI(0, 0, layer.Bitmap.Width, layer.Bitmap.Height)) is { } bounds)
                    result.Add((layer, bounds));
            }
            return result;
        }

        /// <summary>
        /// Layer &gt; Align: "Left", "HCenter", "Right", "Top", "VCenter" or "Bottom".
        /// Several selected layers line up with each other (their outer edges); one layer
        /// lines up with the canvas. Moves what's painted, as the Move tool would. One undo step.
        /// </summary>
        [RelayCommand]
        public void AlignLayers(string edge)
        {
            var targets = ArrangeTargets();
            if (targets.Count == 0) return;
            var area = targets.Count > 1
                ? targets.Skip(1).Aggregate(targets[0].Bounds, (a, t) => SKRectI.Union(a, t.Bounds))
                : new SKRectI(0, 0, DocumentWidth, DocumentHeight);

            var moves = targets.Select(t =>
            {
                var b = t.Bounds;
                var by = edge switch
                {
                    "Left" => new SKPointI(area.Left - b.Left, 0),
                    "Right" => new SKPointI(area.Right - b.Right, 0),
                    "HCenter" => new SKPointI((area.Left + area.Right) / 2 - (b.Left + b.Right) / 2, 0),
                    "Top" => new SKPointI(0, area.Top - b.Top),
                    "Bottom" => new SKPointI(0, area.Bottom - b.Bottom),
                    "VCenter" => new SKPointI(0, (area.Top + area.Bottom) / 2 - (b.Top + b.Bottom) / 2),
                    _ => SKPointI.Empty,
                };
                return (t.Layer, By: by);
            }).ToList();
            ShiftLayers(moves);
        }

        /// <summary>
        /// Layer &gt; Distribute: "Horizontal" or "Vertical" - three or more selected layers
        /// spaced so their middles are evenly apart between the outermost two. One undo step.
        /// </summary>
        [RelayCommand]
        public void DistributeLayers(string axis)
        {
            var targets = ArrangeTargets();
            if (targets.Count < 3) return;
            var horizontal = axis == "Horizontal";
            float Middle(SKRectI b) => horizontal ? (b.Left + b.Right) / 2f : (b.Top + b.Bottom) / 2f;
            var ordered = targets.OrderBy(t => Middle(t.Bounds)).ToList();
            var first = Middle(ordered[0].Bounds);
            var step = (Middle(ordered[^1].Bounds) - first) / (ordered.Count - 1);
            var moves = ordered.Select((t, i) =>
            {
                var d = (int)MathF.Round(first + step * i - Middle(t.Bounds));
                return (t.Layer, By: horizontal ? new SKPointI(d, 0) : new SKPointI(0, d));
            }).ToList();
            ShiftLayers(moves);
        }

        private void ShiftLayers(List<(Layer Layer, SKPointI By)> moves)
        {
            moves.RemoveAll(m => m.By == SKPointI.Empty);
            if (moves.Count == 0) return;
            ExecuteStructuralChange(() =>
            {
                foreach (var (layer, by) in moves)
                {
                    Shift(layer.Bitmap, by);
                    if (layer.Mask is { } mask) Shift(mask.Bitmap, by, SKColors.White);
                    if (layer.Text is { } text) layer.Text = text with { Origin = new SKPoint(text.Origin.X + by.X, text.Origin.Y + by.Y) };
                }
            });
            NotifyContentChanged();
        }
    }
}
