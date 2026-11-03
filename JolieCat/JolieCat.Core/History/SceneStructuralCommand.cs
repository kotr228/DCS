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
    public sealed class SceneStructuralCommand : IEditCommand, ISizedEditCommand
    {
        public long ByteSize
        {
            get
            {
                long total = 0;
                foreach (var s in _before) total += s.ByteSize;
                foreach (var s in _after) total += s.ByteSize;
                return total;
            }
        }

        private readonly Scene _scene;
        private readonly IReadOnlyList<LayerSnapshot> _before;
        private readonly int _beforeActiveIndex;
        private readonly IReadOnlyList<LayerSnapshot> _after;
        private readonly int _afterActiveIndex;

        public SceneStructuralCommand(
            Scene scene,
            IReadOnlyList<LayerSnapshot> before, int beforeActiveIndex,
            IReadOnlyList<LayerSnapshot> after, int afterActiveIndex,
            IReadOnlyList<Guide>? guidesBefore = null, IReadOnlyList<Guide>? guidesAfter = null)
        {
            _guidesBefore = guidesBefore;
            _guidesAfter = guidesAfter;
            _scene = scene ?? throw new ArgumentNullException(nameof(scene));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _beforeActiveIndex = beforeActiveIndex;
            _after = after ?? throw new ArgumentNullException(nameof(after));
            _afterActiveIndex = afterActiveIndex;
        }

        // Guides move with canvas rotate/flip/resize/crop, so those steps restore them too.
        private readonly IReadOnlyList<Guide>? _guidesBefore;
        private readonly IReadOnlyList<Guide>? _guidesAfter;

        public void Undo()
        {
            _scene.RestoreLayers(_before, _beforeActiveIndex);
            if (_guidesBefore is not null) _scene.RestoreGuides(_guidesBefore);
        }

        public void Redo()
        {
            _scene.RestoreLayers(_after, _afterActiveIndex);
            if (_guidesAfter is not null) _scene.RestoreGuides(_guidesAfter);
        }
    }
}
