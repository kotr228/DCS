using System;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>Editing (or moving) a shape layer's shape. Undo/redo swap the shape and
    /// redraw the layer. The layer is found by Id, so the entry survives structural
    /// undo/redo in between.</summary>
    public sealed class ShapeChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly ShapeContent _before;
        private readonly ShapeContent _after;

        public ShapeChangeCommand(Scene scene, Layer layer, ShapeContent before, ShapeContent after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(ShapeContent shape)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.Shape = shape;
                layer.RenderShape();
                return;
            }
        }
    }
}
