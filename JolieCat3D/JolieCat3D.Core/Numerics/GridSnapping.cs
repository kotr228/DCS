using System.Numerics;

namespace JolieCat3D.Core.Numerics
{
    /// <summary>
    /// Snaps a position onto a discrete world-space grid - the actual math behind
    /// Precision Modeling's "hold Ctrl while dragging" behavior
    /// (<c>Engine.Gizmos.TransformGizmo</c>/<c>Engine.Editing.ComponentGizmo</c> both call
    /// this per drag tick, snapping the drag's own TOTAL accumulated offset since the
    /// gesture began - never the raw per-tick delta directly, which would let rounding
    /// error silently accumulate/drift across many small ticks - see either gizmo's own
    /// remarks on how it tracks that running total). Pure <see cref="Vector3"/> math, no
    /// WPF dependency, so this is testable without a WPF runtime at all.
    /// </summary>
    public static class GridSnapping
    {
        /// <summary>Rounds each component of <paramref name="value"/> to the nearest
        /// multiple of <paramref name="gridSize"/> - <paramref name="value"/> itself,
        /// unchanged, for a non-positive <paramref name="gridSize"/> (a "grid" of zero or
        /// negative size is meaningless, so this degrades to a no-op rather than
        /// dividing by zero/flipping signs unpredictably).</summary>
        public static Vector3 Snap(Vector3 value, float gridSize)
        {
            if (gridSize <= 0f) return value;
            return new Vector3(SnapComponent(value.X, gridSize), SnapComponent(value.Y, gridSize), SnapComponent(value.Z, gridSize));
        }

        private static float SnapComponent(float value, float gridSize) => MathF.Round(value / gridSize) * gridSize;

        /// <summary>The ABSOLUTE position a Translate-gizmo drag should actually apply
        /// this tick (<c>Engine.Gizmos.TransformGizmo.ApplyTranslate</c> writes this
        /// straight onto <c>Core.Scene.Node.LocalPosition</c>) - <paramref name="startPosition"/>
        /// plus <paramref name="accumulatedRawDelta"/> (this WHOLE drag gesture's own
        /// running total offset since it began, never just the latest tick's own small
        /// delta) snapped as one single value when <paramref name="snapRequested"/>. Doing
        /// the snap against the FIXED starting position's own total offset, fresh every
        /// tick, is what keeps a long drag from ever drifting off the true grid the way
        /// repeatedly re-snapping an already-snapped running position tick after tick
        /// would (each small unsnapped sub-grid remainder getting rounded away again and
        /// again).</summary>
        public static Vector3 ComputeSnappedPosition(Vector3 startPosition, Vector3 accumulatedRawDelta, bool snapRequested, float gridSize) =>
            startPosition + (snapRequested ? Snap(accumulatedRawDelta, gridSize) : accumulatedRawDelta);

        /// <summary>The INCREMENTAL delta a component (vertex) drag should actually apply
        /// this tick (<c>Engine.Editing.ComponentGizmo.ApplyTranslate</c> hands this
        /// straight to <c>Engine.Editing.MeshEditSession.ApplyTranslation</c>, which has
        /// no absolute "position" of its own to overwrite the way a whole node's
        /// <c>LocalPosition</c> does - it only ever knows how to move every selected
        /// vertex by a given amount) - the difference between this WHOLE drag gesture's
        /// own running total offset (snapped, when <paramref name="snapRequested"/>) and
        /// whatever total was already applied on a previous tick
        /// (<paramref name="previouslyAppliedDelta"/>): applying the full snapped total
        /// again every tick would move the selection by that amount ON TOP OF what a
        /// previous tick already moved it, compounding far past the intended offset. The
        /// second element of the returned tuple is the new running "already applied"
        /// total a caller should keep for the NEXT tick's own
        /// <paramref name="previouslyAppliedDelta"/>.</summary>
        public static (Vector3 IncrementToApply, Vector3 NewAppliedTotal) ComputeSnappedIncrement(
            Vector3 accumulatedRawDelta, Vector3 previouslyAppliedDelta, bool snapRequested, float gridSize)
        {
            var targetTotal = snapRequested ? Snap(accumulatedRawDelta, gridSize) : accumulatedRawDelta;
            return (targetTotal - previouslyAppliedDelta, targetTotal);
        }
    }
}
