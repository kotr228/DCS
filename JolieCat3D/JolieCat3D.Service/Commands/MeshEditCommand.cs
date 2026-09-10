using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Commands
{
    /// <summary>
    /// Undoes/redoes one structural mesh edit (Extrude, Subdivide, or any future
    /// <see cref="Mesh"/>-mutating operation) by swapping <see cref="Node.Mesh"/> between
    /// two complete, independent <see cref="Mesh.Clone"/> snapshots - "before" and
    /// "after" - rather than trying to invert the edit itself. Extrude/Subdivide have no
    /// simple, cheap inverse operation of their own (Subdivide in particular: there's no
    /// way to "un-subdivide" a mesh back to fewer faces without having kept the original
    /// around), so a full snapshot is the only generally-correct way to guarantee
    /// restoring the EXACT prior geometry - see <see cref="MeshEditCommandFactory"/> for
    /// how those two snapshots are safely captured without either one being a reference
    /// some LATER edit could go on mutating out from under this already-recorded
    /// command.
    /// </summary>
    public sealed class MeshEditCommand : IUndoableCommand
    {
        private readonly Node _target;
        private readonly Mesh _before;
        private readonly Mesh _after;
        private readonly Action? _onChanged;

        public string Description { get; }

        public MeshEditCommand(Node target, Mesh before, Mesh after, string description, Action? onChanged = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _onChanged = onChanged;
        }

        public void Execute()
        {
            _target.Mesh = _after;
            _onChanged?.Invoke();
        }

        public void Undo()
        {
            _target.Mesh = _before;
            _onChanged?.Invoke();
        }
    }

    /// <summary>
    /// Wraps an already-in-place mesh mutation (a call to
    /// <c>Engine.Editing.MeshEditSession.ExtrudeSelectedFace</c>/<c>SubdivideMesh</c>,
    /// today - or any future one) into a <see cref="MeshEditCommand"/>, capturing a
    /// TRUE, permanently-independent "before" and "after" snapshot of
    /// <paramref name="node"/>'s own mesh either side of it.
    ///
    /// This independence matters more than it might look: <c>Mesh.ExtrudeFace</c>/
    /// <c>Subdivide</c> mutate a mesh IN PLACE, so on every edit after the very first
    /// one, <paramref name="node"/>'s own live <see cref="Node.Mesh"/> IS the previous
    /// command's own stored "after" snapshot - letting <paramref name="applyEdit"/>
    /// mutate it directly would silently corrupt that earlier command's history (undoing
    /// it, then redoing it, would replay against geometry that had already moved on past
    /// what was actually recorded). <see cref="Capture"/> instead clones
    /// <paramref name="node"/>'s mesh TWICE before ever calling
    /// <paramref name="applyEdit"/> - once for its own frozen "before", once as a brand
    /// new working copy for the edit to mutate - so whatever object <c>node.Mesh</c>
    /// pointed to at the start is left completely untouched, no matter how many further
    /// edits/undos/redos happen afterward. See <see cref="Capture"/>'s own remarks for
    /// how a first version of this method got that wrong, and the scratch-script
    /// regression that caught it.
    /// </summary>
    public static class MeshEditCommandFactory
    {
        /// <summary>Captures <paramref name="node"/>'s mesh before calling
        /// <paramref name="applyEdit"/> (which is expected to mutate
        /// <paramref name="node"/>'s current <see cref="Node.Mesh"/> in place, exactly
        /// the way <c>MeshEditSession.ExtrudeSelectedFace</c>/<c>SubdivideMesh</c>
        /// already do, and to return whether it actually changed anything). Returns null
        /// (nothing to record) if <paramref name="node"/> has no mesh at all, or if
        /// <paramref name="applyEdit"/> itself reports no change (a "nothing selected to
        /// extrude" no-op, say) - callers should treat null exactly like "this edit
        /// didn't happen", the same meaning <c>MeshEditSession.ExtrudeSelectedFace</c>'s
        /// own <c>false</c> return already has.
        ///
        /// Critically, <paramref name="applyEdit"/> is NEVER allowed to mutate whatever
        /// <see cref="Node.Mesh"/> already was the moment this method was called - only
        /// on the very FIRST edit ever made to a node is that the original, user-authored
        /// mesh; on every edit after that, it's actually the PREVIOUS command's own
        /// stored "after" snapshot (mesh edits mutate in place, so nothing else ever
        /// reassigns <see cref="Node.Mesh"/> to a fresh object in between two edits made
        /// through this factory) - mutating it in place would silently corrupt an
        /// already-recorded command's own history out from under it (an earlier,
        /// seemingly-safe version of this method did exactly that, caught by a
        /// scratch-script regression covering chained Extrude-then-Subdivide-then-Undo-
        /// Undo-Redo-Redo before it ever shipped). Cloning <paramref name="node"/>'s mesh
        /// TWICE up front - one clone becomes <see cref="MeshEditCommand"/>'s own frozen
        /// "before", a completely separate clone becomes the live, about-to-be-mutated
        /// <see cref="Node.Mesh"/> - is what enforces that: whatever object <c>node.Mesh</c>
        /// pointed to when this method started is left byte-for-byte untouched forever
        /// after, no matter how many further edits/undos/redos happen later.
        ///
        /// One consequence for callers: because <c>node.Mesh</c> is already a DIFFERENT
        /// object by the time <paramref name="applyEdit"/> actually runs,
        /// <paramref name="applyEdit"/> must look up whatever it operates on FRESH from
        /// <c>node.Mesh</c> at call time (by vertex INDEX/selection state, the way
        /// <c>MeshEditSession.ExtrudeSelectedFace</c>/<c>SubdivideMesh</c> already do)
        /// rather than close over a <see cref="Core.Geometry.Polygon"/>/<see cref="Vertex"/>
        /// reference captured before calling this method - such a reference would belong
        /// to the mesh object this method already cloned away from by the time it's
        /// used.</summary>
        public static MeshEditCommand? Capture(Node node, string description, Func<bool> applyEdit, Action? onChanged = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(description);
            ArgumentNullException.ThrowIfNull(applyEdit);

            if (node.Mesh is not { } mesh) return null;

            var before = mesh.Clone();
            node.Mesh = mesh.Clone(); // a fresh, never-before-referenced-by-any-command working copy for applyEdit to mutate

            var changed = applyEdit();
            if (!changed)
            {
                node.Mesh = mesh; // nothing happened - put the original reference straight back rather than leaving an untouched duplicate clone in its place
                return null;
            }

            if (node.Mesh is not { } after) return null; // applyEdit itself cleared node.Mesh out from under us - nothing sane to record

            return new MeshEditCommand(node, before, after, description, onChanged);
        }
    }
}
