using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Small, shared rotation-from-vectors math with no <see cref="Scene.Node"/>/WPF
    /// coupling at all - kept independent (the same "plain vector math lives in
    /// Core.Geometry, testable with no engine/UI runtime involved" shape
    /// <see cref="RayIntersection"/>/<see cref="VertexEdgeSnapping"/> already establish)
    /// since more than one feature now needs the exact same "shortest arc" derivation:
    /// <see cref="Constraints.TrackToConstraint"/>'s own aim-at-target rotation, and
    /// <c>Engine.Gizmos.TransformGizmo</c>'s own Align to Surface (rotating a dragged
    /// object's local axis to match a snapped-onto face's normal).
    /// </summary>
    public static class RotationMath
    {
        /// <summary>The standard "shortest arc" rotation taking unit vector
        /// <paramref name="from"/> onto unit vector <paramref name="to"/> - identity if
        /// they already point the same way; a 180-degree turn around any axis
        /// perpendicular to <paramref name="from"/> if they point exactly opposite (a
        /// cross product alone is zero right at that singularity, so an arbitrary valid
        /// perpendicular is picked instead - the same "pick a fallback rather than
        /// produce a zero/NaN result" latitude <see cref="Ray"/>'s own direction
        /// normalization already gives elsewhere in this project).</summary>
        public static Quaternion ShortestArcRotation(Vector3 from, Vector3 to)
        {
            var dot = Vector3.Dot(from, to);

            if (dot >= 1f - 1e-6f) return Quaternion.Identity;

            if (dot <= -1f + 1e-6f)
            {
                var axis = Vector3.Cross(Vector3.UnitX, from);
                if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, from);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }

            var cross = Vector3.Cross(from, to);
            var s = MathF.Sqrt((1f + dot) * 2f);
            var invs = 1f / s;
            return Quaternion.Normalize(new Quaternion(cross.X * invs, cross.Y * invs, cross.Z * invs, s * 0.5f));
        }
    }
}
