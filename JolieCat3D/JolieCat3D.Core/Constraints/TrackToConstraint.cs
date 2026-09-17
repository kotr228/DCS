using System.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Constraints
{
    /// <summary>
    /// Overrides its owning node's <see cref="Node.LocalRotation"/> so that its own
    /// <see cref="ForwardAxis"/> (in local space) always points at <see cref="Target"/>'s
    /// current world-space origin - the task's own "Track To" constraint, most commonly
    /// put on a <see cref="CameraData"/>/directional <see cref="LightData"/> node to
    /// keep it aimed at a moving subject with no keyframes of its own needed for the
    /// aiming itself.
    ///
    /// Deliberately constrains ONLY the aim direction, not "roll"/up orientation (the
    /// two axes perpendicular to <see cref="ForwardAxis"/> are whatever the shortest-arc
    /// rotation from the axis to the target direction naturally produces) - the task
    /// asks only that the forward axis "point... at the Target's origin", not for a
    /// second, independent "Up" reference the way a full Blender-style Track To (which
    /// takes a separate Up axis specifically to pin down roll) does; a known, disclosed
    /// simplification, not a bug - <see cref="Apply"/>'s own remarks cover the one
    /// visible consequence (roll can vary path-dependently as the target moves).
    /// </summary>
    public sealed class TrackToConstraint : Constraint
    {
        public override string Name => "Track To";

        /// <summary>The node to aim at - null (the default, until picked from the
        /// Inspector's own target combo box) makes <see cref="Apply"/> a no-op, the same
        /// "unconfigured constraint/modifier does nothing rather than throwing" latitude
        /// <see cref="Modifiers.BooleanModifier.Target"/> already gives.</summary>
        public Node? Target { get; set; }

        public ConstraintAxis ForwardAxis { get; set; } = ConstraintAxis.PlusZ;

        /// <summary>Recomputes <see cref="Node.LocalRotation"/> from scratch every call -
        /// cheap (one subtraction, one cross product, one square root) and entirely
        /// determined by <see cref="Target"/>'s CURRENT world position, so calling this
        /// every frame (via <see cref="ConstraintSolver"/>) already gives correct
        /// frame-by-frame tracking with no extra "previous frame" state to keep in sync.
        ///
        /// A no-op if <see cref="Target"/> is unset, or if it happens to sit (within
        /// float tolerance) exactly at <paramref name="node"/>'s own current world
        /// position - there is no meaningful "direction to aim" a zero-length vector
        /// could ever represent, and leaving the node's last-known rotation in place
        /// (rather than snapping to some arbitrary fallback) is the least surprising
        /// behavior for that genuinely-degenerate instant.</summary>
        public override void Apply(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (Target is null) return;

            var direction = Target.GetWorldPosition() - node.GetWorldPosition();
            if (direction.LengthSquared() < 1e-12f) return;
            direction = Vector3.Normalize(direction);

            var localAxis = ForwardAxis switch
            {
                ConstraintAxis.PlusX => Vector3.UnitX,
                ConstraintAxis.MinusX => -Vector3.UnitX,
                ConstraintAxis.PlusY => Vector3.UnitY,
                ConstraintAxis.MinusY => -Vector3.UnitY,
                ConstraintAxis.MinusZ => -Vector3.UnitZ,
                _ => Vector3.UnitZ,
            };

            var worldRotation = ShortestArcRotation(localAxis, direction);

            // The same world -> owner-local quaternion conversion
            // Scene3DRenderer.OnPilotedCameraChanged already established elsewhere in
            // this project (World = ParentWorldRotation * LocalRotation, per
            // Node.GetWorldRotation's own remarks, so LocalRotation =
            // Inverse(ParentWorldRotation) * WorldRotation).
            node.LocalRotation = node.Parent is null
                ? worldRotation
                : Quaternion.Normalize(Quaternion.Inverse(node.Parent.GetWorldRotation()) * worldRotation);
        }

        /// <summary>The standard "shortest arc" rotation taking unit vector
        /// <paramref name="from"/> onto unit vector <paramref name="to"/> - identity if
        /// they already point the same way; a 180-degree turn around any axis
        /// perpendicular to <paramref name="from"/> if they point exactly opposite (a
        /// cross product alone is zero right at that singularity, so an arbitrary valid
        /// perpendicular is picked instead - the same "pick a fallback rather than
        /// produce a zero/NaN result" latitude <see cref="Geometry.Ray"/>'s own direction
        /// normalization already gives elsewhere in this project).</summary>
        private static Quaternion ShortestArcRotation(Vector3 from, Vector3 to)
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

        public override Constraint Clone() => new TrackToConstraint
        {
            IsEnabled = IsEnabled,
            Target = Target,
            ForwardAxis = ForwardAxis,
        };
    }
}
