using System;
using JolieCat.Core.Adjustments;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>Re-editing an adjustment layer's settings. The layer is found by Id
    /// when undoing/redoing, so the entry survives structural undo/redo in between.</summary>
    public sealed class AdjustmentChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly AdjustmentSettings _before;
        private readonly AdjustmentSettings _after;

        public AdjustmentChangeCommand(Scene scene, Layer layer, AdjustmentSettings before, AdjustmentSettings after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(AdjustmentSettings settings)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.Adjustment = settings;
                return;
            }
        }
    }
}
