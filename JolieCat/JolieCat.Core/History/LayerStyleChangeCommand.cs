using System;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;

namespace JolieCat.Core.History
{
    /// <summary>Changing a layer's style (adding, editing or clearing effects). The
    /// layer is found by Id, so the entry survives structural undo/redo in between.</summary>
    public sealed class LayerStyleChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly LayerStyle? _before;
        private readonly LayerStyle? _after;

        public LayerStyleChangeCommand(Scene scene, Layer layer, LayerStyle? before, LayerStyle? after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before;
            _after = after;
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(LayerStyle? style)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.Style = style;
                return;
            }
        }
    }
}
