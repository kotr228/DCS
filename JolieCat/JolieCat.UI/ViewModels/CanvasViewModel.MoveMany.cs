using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    public partial class CanvasViewModel
    {
        // ================= Move tool: groups and several layers =================
        //
        // With a group active, or several layers selected in the Layers panel, the Move
        // tool shifts all of their pixels (and masks) together, in whole-pixel steps.
        // One undo step for the lot.

        private sealed class MovingLayer
        {
            public required Layer Layer;
            public required SKBitmap Content;
            public SKBitmap? Mask;
        }

        private List<MovingLayer>? _movingMany;
        private SKPoint _moveManyStart;
        private SKPointI _moveManyDelta;

        /// <summary>The pixel layers the Move tool should shift together, or null when
        /// it's just the active layer (the normal single-layer move).</summary>
        private List<Layer>? LayersToMoveTogether(Layer active)
        {
            var scene = Layers.Scene;
            var selected = Layers.SelectedLayers.Where(l => scene.IndexOf(l) >= 0).ToList();
            var units = selected.Count > 1 && selected.Contains(active) ? selected : new List<Layer> { active };
            if (units.Count == 1 && !active.IsGroup) return null;

            var result = new List<Layer>();
            foreach (var unit in scene.TopUnits(units))
            {
                var (start, end) = scene.BlockOf(unit);
                for (var i = start; i <= end; i++)
                {
                    var layer = scene.Layers[i];
                    if (!layer.IsGroup && !layer.IsAdjustment && !layer.IsLocked) result.Add(layer);
                }
            }
            return result;
        }

        private void BeginMoveMany(List<Layer> layers, SKPoint doc)
        {
            _movingMany = layers.Select(l => new MovingLayer
            {
                Layer = l,
                Content = l.Bitmap.Copy(),
                Mask = l.Mask?.Bitmap.Copy(),
            }).ToList();
            _moveManyStart = doc;
            _moveManyDelta = default;
        }

        private void UpdateMoveMany(SKPoint doc)
        {
            if (_movingMany is null) return;

            var delta = new SKPointI((int)MathF.Round(doc.X - _moveManyStart.X), (int)MathF.Round(doc.Y - _moveManyStart.Y));
            if (delta == _moveManyDelta) return;
            _moveManyDelta = delta;

            foreach (var moving in _movingMany)
            {
                moving.Layer.Canvas.Clear(SKColors.Transparent);
                moving.Layer.Canvas.DrawBitmap(moving.Content, delta.X, delta.Y);
                if (moving.Mask is not null && moving.Layer.Mask is { } mask)
                {
                    mask.Canvas.Clear(SKColors.White); // revealed areas stay visible
                    mask.Canvas.DrawBitmap(moving.Mask, delta.X, delta.Y);
                }
            }

            // Layers other than the active one changed: drop the canvas's layer cache.
            Layers.NotifyContentChanged();
        }

        private void CommitMoveMany()
        {
            if (_movingMany is null) return;
            var moving = _movingMany;
            _movingMany = null;

            if (_moveManyDelta != default)
            {
                var commands = new List<IEditCommand>();
                foreach (var m in moving)
                {
                    commands.Add(new LayerPixelsCommand(Layers.Scene, m.Layer, false, m.Content.Pixels, m.Layer.Bitmap.Pixels));
                    if (m.Layer.Text is { } text)
                    {
                        // Keep the text editable: its position moves with the pixels.
                        var moved = text.MovedBy(_moveManyDelta.X, _moveManyDelta.Y);
                        m.Layer.Text = moved;
                        commands.Add(new TextChangeCommand(Layers.Scene, m.Layer, text, moved));
                    }
                    if (m.Mask is not null && m.Layer.Mask is { } mask)
                        commands.Add(new LayerPixelsCommand(Layers.Scene, m.Layer, true, m.Mask.Pixels, mask.Bitmap.Pixels));
                }

                var composite = new CompositeEditCommand(commands);
                if (!composite.IsEmpty) Layers.History.Push(composite, "Move");
            }

            foreach (var m in moving)
            {
                m.Content.Dispose();
                m.Mask?.Dispose();
            }
            RaiseInvalidate();
        }
    }
}
