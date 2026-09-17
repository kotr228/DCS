using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Constraints
{
    /// <summary>
    /// Evaluates every <see cref="Node.Constraints"/> stack in a <see cref="Scene3D"/>,
    /// in scene-traversal order - called from <c>JolieCat3D.Engine.Rendering.Scene3DRenderer.Render</c>,
    /// the one place EVERY render already funnels through (an animation playback tick, a
    /// Properties Inspector edit, a gizmo drag, a frame-scrubber move), so a constraint
    /// re-solves fresh whenever anything might have changed - not only during animation
    /// playback, and not needing its own separate call site the way
    /// <c>Service.Animation.AnimationTimeline.Apply</c> needs one at every playback/scrub
    /// call site. Traversal order matters when a constrained node's own
    /// <see cref="TrackToConstraint.Target"/> is ITSELF constrained (a rare but valid
    /// chain) - see <see cref="Apply"/>'s own remarks on why this is only a partial
    /// guarantee, not a full dependency solve.
    /// </summary>
    public static class ConstraintSolver
    {
        /// <summary>Solves every node's own constraints, in <see cref="Scene3D.Traverse"/>
        /// order. That order already resolves the common "target is an ancestor/earlier
        /// sibling" case correctly (the target's own constrained rotation, if any, is
        /// already up to date by the time a LATER node reads its world position), but a
        /// target positioned LATER in traversal order (or a mutual/cyclic reference)
        /// still sees that target's own PREVIOUS solve - a known, disclosed
        /// simplification (a full solve would need dependency-ordering or iteration to
        /// converge) rather than the unbounded complexity a general constraint graph
        /// solver would add for a "basic" pipeline.</summary>
        public static void Apply(Scene3D scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            foreach (var node in scene.Traverse())
            {
                foreach (var constraint in node.Constraints)
                {
                    if (!constraint.IsEnabled) continue;
                    constraint.Apply(node);
                }
            }
        }
    }
}
