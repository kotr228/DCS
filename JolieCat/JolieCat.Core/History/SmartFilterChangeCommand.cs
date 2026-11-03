using System;
using System.Collections.Generic;
using JolieCat.Core.Documents;
using JolieCat.Core.Effects;

namespace JolieCat.Core.History
{
    /// <summary>Adding, editing, hiding, reordering or deleting a layer's Smart Filters.
    /// The layer keeps its original pixels; undo/redo swap the filter list and redraw.
    /// The layer is found by Id, so the entry survives structural undo/redo in between.</summary>
    public sealed class SmartFilterChangeCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly Guid _layerId;
        private readonly IReadOnlyList<SmartFilter> _before;
        private readonly IReadOnlyList<SmartFilter> _after;

        public SmartFilterChangeCommand(Scene scene, Layer layer, IReadOnlyList<SmartFilter> before, IReadOnlyList<SmartFilter> after)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _layerId = layer?.Id ?? throw new ArgumentNullException(nameof(layer));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(IReadOnlyList<SmartFilter> filters)
        {
            foreach (var layer in _scene.Layers)
            {
                if (layer.Id != _layerId) continue;
                layer.ReplaceSmartFilters(filters);
                return;
            }
        }
    }
}
