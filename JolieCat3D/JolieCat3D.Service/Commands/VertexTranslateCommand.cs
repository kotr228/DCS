using System.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undoes/redoes one Edit Mode vertex-drag gesture against a single <see cref="Node"/>'s
    /// own mesh - <c>TransformNodeCommand</c>'s sibling for "moving a subset of a mesh's own
    /// vertices" rather than "moving a whole node". Stores each affected vertex's explicit
    /// BEFORE/AFTER position (not a delta, and not a full <see cref="Core.Geometry.Mesh.Clone"/>
    /// the way <c>MeshEditCommand</c> uses for Extrude/Subdivide) - a vertex translate never
    /// changes topology (no vertex is added, removed, or reindexed), so recording only the
    /// handful of positions that actually moved is both cheaper and, like
    /// <c>TransformNodeCommand</c>'s own before/after pair, bit-exact on Undo/Redo (re-applying
    /// a delta twice could accumulate floating-point drift; re-applying an explicit value never
    /// can).
    ///
    /// Built AFTER the drag that produced <see cref="_changes"/>'s own "after" positions has
    /// already happened live (<c>JolieCat3D.Engine.Editing.ComponentGizmo</c> applies every tick
    /// of a drag immediately, then raises <c>ComponentGizmo.TranslationCommitted</c> once the
    /// WHOLE drag gesture ends) - so this is normally handed to
    /// <see cref="CommandHistory.Record"/>, never <see cref="CommandHistory.Execute"/> (which
    /// would needlessly re-apply the exact same "after" positions a second time).
    /// </summary>
    public sealed class VertexTranslateCommand : IUndoableCommand
    {
        private readonly Node _target;
        private readonly IReadOnlyList<(int Index, Vector3 Before, Vector3 After)> _changes;
        private readonly Action? _onChanged;

        public string Description { get; }

        public VertexTranslateCommand(
            Node target,
            IReadOnlyList<(int Index, Vector3 Before, Vector3 After)> changes,
            string description,
            Action? onChanged = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute() => Apply(useAfter: true);
        public void Undo() => Apply(useAfter: false);

        private void Apply(bool useAfter)
        {
            if (_target.Mesh is not { } mesh) return;

            foreach (var (index, before, after) in _changes)
                mesh.SetVertexPosition(index, useAfter ? after : before);

            mesh.RecalculateNormals();
            _onChanged?.Invoke();
        }
    }
}
