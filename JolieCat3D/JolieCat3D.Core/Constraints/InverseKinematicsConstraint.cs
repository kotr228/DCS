using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Constraints
{
    /// <summary>
    /// Blender's "Inverse Kinematics" constraint. Put it on the last bone (or node) of a
    /// chain: the chain's joints rotate (lengths never change) so the chain's tip reaches
    /// <see cref="Target"/>. Solved with CCD (cyclic coordinate descent); an optional
    /// <see cref="PoleTarget"/> decides which way the middle joint (elbow/knee) bends.
    ///
    /// The tip is the owner's bone tail when it has <see cref="BoneData"/>, otherwise the
    /// owner's own origin (so plain node hierarchies can be posed too).
    /// </summary>
    public sealed class InverseKinematicsConstraint : Constraint
    {
        public override string Name => "Inverse Kinematics";

        public Node? Target { get; set; }

        /// <summary>Optional: the chain bends towards this node.</summary>
        public Node? PoleTarget { get; set; }

        /// <summary>How many joints up from the owner take part (Blender's "Chain Length").</summary>
        public int ChainLength
        {
            get => _chainLength;
            set => _chainLength = Math.Clamp(value, 1, 64);
        }
        private int _chainLength = 2;

        public int Iterations
        {
            get => _iterations;
            set => _iterations = Math.Clamp(value, 1, 500);
        }
        private int _iterations = 100;

        /// <summary>Distance at which the solve counts as done.</summary>
        public float Tolerance { get; set; } = 1e-4f;

        public override void Apply(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (Target is null || ReferenceEquals(Target, node)) return;

            var joints = BuildChain(node);
            if (joints.Count == 0) return;

            var effectorLocal = node.Bone?.Tail ?? Vector3.Zero;
            Vector3 Effector() => Vector3.Transform(effectorLocal, node.GetWorldTransform());
            var target = Target.GetWorldPosition();

            if (joints.Count == 2)
            {
                SolveTwoBone(joints[1], joints[0], Effector, target);
                return;
            }

            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                if (Vector3.Distance(Effector(), target) < Tolerance) break;
                foreach (var joint in joints) // tip first
                {
                    var pivot = joint.GetWorldPosition();
                    var toEffector = Effector() - pivot;
                    var toTarget = target - pivot;
                    if (toEffector.LengthSquared() < 1e-12f || toTarget.LengthSquared() < 1e-12f) continue;
                    var delta = RotationMath.ShortestArcRotation(Vector3.Normalize(toEffector), Vector3.Normalize(toTarget));
                    SetWorldRotation(joint, Quaternion.Normalize(delta * joint.GetWorldRotation()));
                }
            }

            if (PoleTarget is not null && joints.Count >= 2) ApplyPole(joints, Effector());
        }

        /// <summary>Exact two-bone solve (arms, legs): law of cosines for the bend angle, the
        /// bend plane from the pole target (or the current pose when there is none).</summary>
        private void SolveTwoBone(Node rootJoint, Node midJoint, Func<Vector3> effector, Vector3 target)
        {
            var root = rootJoint.GetWorldPosition();
            var mid = midJoint.GetWorldPosition();
            var a = Vector3.Distance(root, mid);
            var b = Vector3.Distance(mid, effector());
            if (a < 1e-6f || b < 1e-6f) return;

            var toTarget = target - root;
            if (toTarget.LengthSquared() < 1e-12f) return;
            var u = Vector3.Normalize(toTarget);
            var d = Math.Clamp(toTarget.Length(), MathF.Abs(a - b) + 1e-5f, a + b - 1e-5f);

            // Which side the joint bends to.
            var hint = (PoleTarget?.GetWorldPosition() ?? mid) - root;
            var v = hint - u * Vector3.Dot(hint, u);
            if (v.LengthSquared() < 1e-10f)
            {
                v = Vector3.Cross(u, MathF.Abs(u.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
            }
            v = Vector3.Normalize(v);

            var cosAlpha = Math.Clamp((a * a + d * d - b * b) / (2 * a * d), -1f, 1f);
            var sinAlpha = MathF.Sqrt(1 - cosAlpha * cosAlpha);
            var wantedMid = root + u * (a * cosAlpha) + v * (a * sinAlpha);

            var swing = RotationMath.ShortestArcRotation(Vector3.Normalize(mid - root), Vector3.Normalize(wantedMid - root));
            SetWorldRotation(rootJoint, Quaternion.Normalize(swing * rootJoint.GetWorldRotation()));

            var newMid = midJoint.GetWorldPosition();
            var reach = root + u * d;
            var toEffector = effector() - newMid;
            var toReach = reach - newMid;
            if (toEffector.LengthSquared() < 1e-12f || toReach.LengthSquared() < 1e-12f) return;
            var bend = RotationMath.ShortestArcRotation(Vector3.Normalize(toEffector), Vector3.Normalize(toReach));
            SetWorldRotation(midJoint, Quaternion.Normalize(bend * midJoint.GetWorldRotation()));
        }

        /// <summary>Joints from the tip towards the root. A bone owner rotates itself; a plain
        /// node's origin is the tip, so its chain starts at the parent.</summary>
        private List<Node> BuildChain(Node owner)
        {
            var joints = new List<Node>();
            var current = owner.Bone is not null ? owner : owner.Parent;
            while (current is not null && joints.Count < ChainLength)
            {
                // Inside an armature the chain stops at the armature root.
                if (owner.Bone is not null && current.Bone is null) break;
                joints.Add(current);
                current = current.Parent;
            }
            return joints;
        }

        /// <summary>Swings the whole chain around the root→tip axis so the joint after the
        /// root (elbow/knee) points at the pole target.</summary>
        private void ApplyPole(List<Node> joints, Vector3 effector)
        {
            var root = joints[^1];
            var bend = joints[^2]; // the joint right after the root
            var rootPos = root.GetWorldPosition();
            var axis = effector - rootPos;
            if (axis.LengthSquared() < 1e-12f) return;
            axis = Vector3.Normalize(axis);

            Vector3 Flatten(Vector3 p)
            {
                var v = p - rootPos;
                return v - axis * Vector3.Dot(v, axis);
            }

            var current = Flatten(bend.GetWorldPosition());
            var wanted = Flatten(PoleTarget!.GetWorldPosition());
            if (current.LengthSquared() < 1e-10f || wanted.LengthSquared() < 1e-10f) return;
            current = Vector3.Normalize(current);
            wanted = Vector3.Normalize(wanted);
            var angle = MathF.Atan2(Vector3.Dot(Vector3.Cross(current, wanted), axis), Vector3.Dot(current, wanted));
            var swing = Quaternion.CreateFromAxisAngle(axis, angle);
            SetWorldRotation(root, Quaternion.Normalize(swing * root.GetWorldRotation()));
        }

        private static void SetWorldRotation(Node node, Quaternion world)
        {
            var parent = node.Parent?.GetWorldRotation() ?? Quaternion.Identity;
            node.LocalRotation = Quaternion.Normalize(Quaternion.Inverse(parent) * world);
        }

        public override Constraint Clone() => new InverseKinematicsConstraint
        {
            IsEnabled = IsEnabled,
            Target = Target,
            PoleTarget = PoleTarget,
            ChainLength = ChainLength,
            Iterations = Iterations,
            Tolerance = Tolerance,
        };
    }
}
