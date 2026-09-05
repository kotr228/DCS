using System;
using System.Collections.Generic;
using JolieCat.Core.Documents;

namespace JolieCat.Core.History
{
    /// <summary>
    /// Undoes/redoes a layer-list operation (add, delete, reorder, merge down) via a
    /// whole-scene before/after snapshot - see <see cref="HistoryManager.PushStructural"/>
    /// for why a full-scene snapshot (rather than a targeted inverse of each operation)
    /// is this system's uniform answer for every structural change.
    /// </summary>
    public sealed class SceneStructuralCommand : IEditCommand
    {
        private readonly Scene _scene;
        private readonly IReadOnlyList<LayerSnapshot> _before;
        private readonly int _beforeActiveIndex;
        private readonly IReadOnlyList<LayerSnapshot> _after;
        private readonly int _afterActiveIndex;

        public SceneStructuralCommand(
            Scene scene,
            IReadOnlyList<LayerSnapshot> before, int beforeActiveIndex,
            IReadOnlyList<LayerSnapshot> after, int afterActiveIndex)
        {
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _beforeActiveIndex = beforeActiveIndex;
            _after = after ?? throw new ArgumentNullException(nameof(after));
            _afterActiveIndex = afterActiveIndex;
        }

        public void Undo() => _scene.RestoreLayers(_before, _beforeActiveIndex);

        public void Redo() => _scene.RestoreLayers(_after, _afterActiveIndex);
    }
}
