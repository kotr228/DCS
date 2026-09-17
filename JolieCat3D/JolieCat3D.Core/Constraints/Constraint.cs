namespace JolieCat3D.Core.Constraints
{
    /// <summary>
    /// One entry of a <see cref="Scene.Node.Constraints"/> stack - the non-destructive
    /// "override this node's own transform, computed fresh every evaluation" pipeline
    /// the task calls for (crucial for animation: a camera/light that stays aimed at a
    /// moving target with no keyframes of its own needed for the AIMING itself, only
    /// for whatever moves the TARGET). Deliberately mirrors <see cref="Modifiers.Modifier"/>'s
    /// own shape (<see cref="Name"/>, <see cref="IsEnabled"/>, an <c>Apply</c> method,
    /// <see cref="Clone"/>) - the same "small, evaluated-fresh-every-time, never
    /// mutates its own inputs" concept, just for a node's TRANSFORM instead of its MESH.
    ///
    /// Unlike a <see cref="Modifiers.Modifier"/> (evaluated into a brand new <see cref="Geometry.Mesh"/>
    /// each time, leaving <see cref="Scene.Node.Mesh"/> itself untouched), a constraint's
    /// own <c>Apply</c> DOES mutate its owning node's <see cref="Scene.Node.LocalRotation"/>
    /// directly - the same "keyframe animation already mutates the transform in place"
    /// precedent <c>Service.Animation.AnimationTimeline.Apply</c> already set, evaluated
    /// (via <see cref="ConstraintSolver"/>) immediately AFTER it each frame, so a
    /// constraint always has the final say over whatever a keyframe track (or a manual
    /// Properties Inspector edit) most recently set.
    /// </summary>
    public abstract class Constraint
    {
        public abstract string Name { get; }

        public bool IsEnabled { get; set; } = true;

        /// <summary>Recomputes and overrides whatever this constraint governs on
        /// <paramref name="node"/> - for <see cref="TrackToConstraint"/>, its
        /// <see cref="Scene.Node.LocalRotation"/>. Called once per node per
        /// <see cref="ConstraintSolver"/> evaluation (every render, not only during
        /// animation playback - see <see cref="ConstraintSolver"/>'s own remarks), so
        /// implementations should be cheap and side-effect-free beyond the one field
        /// they own.</summary>
        public abstract void Apply(Scene.Node node);

        /// <summary>A copy of this constraint's own settings - <see cref="TrackToConstraint.Target"/>
        /// (a reference to some OTHER node in the scene) is copied BY REFERENCE, not
        /// re-pointed at any duplicated node, the exact same precedent
        /// <see cref="Modifiers.BooleanModifier.Clone"/> already set for its own
        /// <see cref="Modifiers.BooleanModifier.Target"/>.</summary>
        public abstract Constraint Clone();
    }
}
