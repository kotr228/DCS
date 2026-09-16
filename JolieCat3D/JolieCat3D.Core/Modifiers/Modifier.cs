using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Modifiers
{
    /// <summary>
    /// A single, non-destructive mesh-generating step in a <see cref="Scene.Node.Modifiers"/>
    /// stack: reads a <see cref="Mesh"/> and returns a brand NEW one, never mutating its
    /// input - the same "read the whole mesh, produce/replace, never edit in place"
    /// shape <see cref="Geometry.Mesh.Subdivide"/> uses internally, just pushed out to a
    /// pluggable, stackable, render-time-only operation instead of a permanent Edit Mode
    /// action. This is what makes a modifier "non-destructive" in the modeling-tool
    /// sense: <see cref="Scene.Node.Mesh"/> itself is exactly what Edit Mode's own
    /// vertex/face tools (Extrude, Subdivide, a component drag) see and edit, regardless
    /// of what any modifier does to how it's actually rendered - toggling
    /// <see cref="IsEnabled"/> off, removing a modifier, or reordering the stack never
    /// loses or corrupts any authored geometry.
    /// </summary>
    public abstract class Modifier
    {
        /// <summary>A short, user-facing label ("Mirror", "Subdivision Surface") - what
        /// a Modifiers panel's stack list shows for each entry.</summary>
        public abstract string Name { get; }

        /// <summary>Off by default (see <see cref="Modifier"/> - it's a mutable
        /// property, unlike <see cref="Name"/>) means <see cref="Apply"/> is never
        /// called for this modifier at all (see <see cref="ModifierStack.Evaluate"/>) -
        /// the toolbar/panel "eye" toggle every modifier stack UI has, so a user can
        /// compare with/without a modifier's effect without losing its settings or
        /// having to remove and re-add it.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Produces this modifier's own output mesh from <paramref name="input"/> -
        /// implementations must never mutate <paramref name="input"/> itself (see this
        /// class's own remarks on non-destructiveness). <paramref name="owner"/> is the
        /// <see cref="Node"/> this modifier's own stack lives on - optional (and unused by
        /// <see cref="MirrorModifier"/>/<see cref="SubdivisionSurfaceModifier"/>, neither
        /// of which needs anything beyond the mesh itself), but required by
        /// <see cref="BooleanModifier"/> to resolve its own target's mesh into the SAME
        /// local space <paramref name="input"/> is already in (see its own remarks) -
        /// <see cref="ModifierStack.Evaluate"/> always has it on hand from whichever
        /// <see cref="Node"/> it was called for, so passing it costs every OTHER modifier
        /// nothing.</summary>
        public abstract Mesh Apply(Mesh input, Node? owner = null);

        /// <summary>A complete, independent copy of this modifier - its own concrete type,
        /// <see cref="IsEnabled"/>, and every type-specific setting (a <see cref="MirrorModifier"/>'s
        /// own <c>Axis</c>/<c>WeldThreshold</c>, a <see cref="SubdivisionSurfaceModifier"/>'s
        /// own <c>Iterations</c>) - never a reference to this same instance, so toggling or
        /// tuning the CLONE's settings (e.g. after <c>Scene.Node.Clone</c> duplicates a
        /// whole node's own stack) can never reach back into the original modifier this
        /// was cloned from, or vice versa.</summary>
        public abstract Modifier Clone();
    }
}
