using System;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>Changing a fill layer's fill. Undo/redo swap the fill and
    /// redraw the layer. The layer is found by Id, so the entry survives structural
    /// undo/redo in between.</summary>
    public sealed class FillChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly FillContent _before;
        private readonly FillContent _after;

        public FillChangeCommand(Scene scene, Layer layer, FillContent before, FillContent after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(FillContent shape)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.Fill = shape;
                layer.RenderFill();
                return;
            }
        }
    }
}
