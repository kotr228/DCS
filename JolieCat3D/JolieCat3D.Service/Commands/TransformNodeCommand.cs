using System.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undoes/redoes one <see cref="Node"/>'s own Translate, Rotate, OR Scale (all three
    /// captured together, as one before/after Position+Rotation+Scale triple - a single
    /// gizmo drag gesture only ever changes one of the three in practice, but bundling
    /// all three is simpler than three near-identical command types and, since setting a
    /// value to what it already was is a harmless no-op, costs nothing when only one
    /// actually changed) - the "object transformations" half of the Undo/Redo system,
    /// <c>MeshEditCommand</c>'s sibling for "mesh modifications".
    ///
    /// Built AFTER the drag/edit that produced <see cref="_after"/> has already happened
    /// live (<c>JolieCat3D.Engine.Gizmos.TransformGizmo</c> applies every tick of a drag
    /// immediately, for responsive visual feedback, then raises
    /// <c>TransformGizmo.TransformCommitted</c> once the WHOLE drag gesture ends) - so
    /// this is normally handed to <see cref="CommandHistory.Record"/>, never
    /// <see cref="CommandHistory.Execute"/> (which would needlessly re-apply the exact
    /// same "after" state a second time).
    /// </summary>
    public sealed class TransformNodeCommand : IUndoableCommand
    {
        private readonly Node _target;
        private readonly (Vector3 Position, Quaternion Rotation, Vector3 Scale) _before;
        private readonly (Vector3 Position, Quaternion Rotation, Vector3 Scale) _after;
        private readonly Action? _onChanged;

        public string Description { get; }

        public TransformNodeCommand(
            Node target,
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) before,
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) after,
            string description,
            Action? onChanged = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _before = before;
            _after = after;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute() => Apply(_after);
        public void Undo() => Apply(_before);

        private void Apply((Vector3 Position, Quaternion Rotation, Vector3 Scale) state)
        {
            _target.LocalPosition = state.Position;
            _target.LocalRotation = state.Rotation;
            _target.LocalScale = state.Scale;
            _onChanged?.Invoke();
        }

        /// <summary>Builds a command from <paramref name="target"/>'s CURRENT transform
        /// as the "after" state and <paramref name="before"/> as the "before" one - null
        /// (nothing to record) if they're identical in every component, since a drag
        /// that ends up exactly where it started changed nothing worth undoing.</summary>
        public static TransformNodeCommand? CaptureIfChanged(
            Node target,
            (Vector3 Position, Quaternion Rotation, Vector3 Scale) before,
            string description,
            Action? onChanged = null)
        {
            ArgumentNullException.ThrowIfNull(target);

            var after = (target.LocalPosition, target.LocalRotation, target.LocalScale);
            if (before.Position == after.LocalPosition && before.Rotation == after.LocalRotation && before.Scale == after.LocalScale)
                return null;

            return new TransformNodeCommand(target, before, after, description, onChanged);
        }
    }
}
