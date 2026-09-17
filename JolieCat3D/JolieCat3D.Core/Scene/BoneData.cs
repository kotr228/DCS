using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Core.Scene
{
    /// <summary>
    /// Makes a <see cref="Node"/> a bone - present (non-null, on <see cref="Node.Bone"/>)
    /// only for a node authored as one, the same "optional data, not a subclass" shape
    /// <see cref="Node.Camera"/>/<see cref="Node.Light"/>/<see cref="Node.Curve"/>/
    /// <see cref="ArmatureData"/> already use. A bone's own hierarchical parenting
    /// (Forward Kinematics) is nothing more than the SAME <see cref="Node.Parent"/>/
    /// <see cref="Node.Children"/>/<see cref="Node.GetWorldTransform"/> chain every
    /// other node already has - rotating a parent bone's own <see cref="Node.LocalRotation"/>
    /// already cascades to every descendant bone (and, through <see cref="Skinning.SkinningEvaluator"/>,
    /// to whatever mesh is skinned to them) with no bone-specific transform-composition
    /// code needed at all.
    ///
    /// <see cref="Head"/>/<see cref="Tail"/> are in this bone's own LOCAL space (the
    /// same "everything is relative to the owning Node's own origin" convention
    /// <see cref="Geometry.Mesh"/>'s own vertices and <see cref="CurveData"/>'s own
    /// control points already use) - <see cref="Head"/> is conventionally
    /// <see cref="Vector3.Zero"/> (the node's own origin already IS where the bone
    /// starts), and <see cref="Tail"/> is some offset from there (typically along the
    /// bone's own local +Y, e.g. <c>(0, Length, 0)</c>) defining the bone's own rest
    /// shape/length/direction - not, say, a world-space or parent-relative pair,
    /// keeping every bone's own Head/Tail meaningful in isolation regardless of where
    /// its <see cref="Node.LocalRotation"/> currently points it.
    /// </summary>
    public sealed class BoneData
    {
        public Vector3 Head { get; set; } = Vector3.Zero;

        public Vector3 Tail { get; set; } = new(0f, 1f, 0f);

        /// <summary>This bone's own world matrix at "bind time" (the moment
        /// <see cref="CaptureRestPose"/> was last called - normally done automatically
        /// the instant a bone is created, while its own <see cref="Node.LocalRotation"/>
        /// is still <see cref="Quaternion.Identity"/>, i.e. genuinely at rest) - the
        /// task's own "Rest Pose matrix". <see cref="Skinning.SkinningEvaluator"/> needs
        /// this bone's own INVERSE bind matrix (<see cref="GetInverseBindMatrix"/>) to
        /// know how far a bone has moved AWAY from the configuration its bound mesh's
        /// own vertex positions were authored/painted against; without it, a bone that
        /// simply isn't at the world origin at rest would itself look like a spurious
        /// pose offset the instant skinning is evaluated.</summary>
        public Matrix4x4 RestPoseWorldMatrix { get; set; } = Matrix4x4.Identity;

        /// <summary>This bone's own local-space length - <see cref="Tail"/> distance
        /// from <see cref="Head"/>. A convenience read (e.g. for drawing the bone's own
        /// visual mesh, or scaling a newly-added child bone), not separately stored -
        /// there is exactly one source of truth (<see cref="Head"/>/<see cref="Tail"/>
        /// themselves), so this can never drift out of sync with them.</summary>
        public float Length => Vector3.Distance(Head, Tail);

        /// <summary>Captures <paramref name="boneNode"/>'s own CURRENT world transform
        /// as this bone's new <see cref="RestPoseWorldMatrix"/> - called automatically
        /// right after a new bone is added to the scene (while its own
        /// <see cref="Node.LocalRotation"/> is still Identity), and exposed as an
        /// explicit user action too (a "Set Rest Pose" button) for re-binding after
        /// reshaping a skeleton before painting/animating it - the same explicit
        /// "commit the current configuration as the new baseline" step every real
        /// skeletal-animation tool offers.</summary>
        public void CaptureRestPose(Node boneNode)
        {
            ArgumentNullException.ThrowIfNull(boneNode);
            RestPoseWorldMatrix = boneNode.GetWorldTransform();
        }

        /// <summary>The inverse of <see cref="RestPoseWorldMatrix"/> -
        /// <see cref="Matrix4x4.Identity"/> if that matrix happens to be non-invertible
        /// (a degenerate rest pose with a collapsed/zero scale somewhere in its own
        /// parent chain - vanishingly unlikely for a bone chain that only ever
        /// rotates/translates, but a safe, non-throwing fallback rather than
        /// propagating a NaN-producing inversion failure into every vertex it
        /// skins).</summary>
        public Matrix4x4 GetInverseBindMatrix() =>
            Matrix4x4.Invert(RestPoseWorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity;

        /// <summary>This bone's own small octahedron shape, from <see cref="Head"/> to
        /// <see cref="Tail"/> - see <see cref="Geometry.BoneMesher"/>'s own remarks on
        /// why a bone needs SOME mesh at all to be visible/selectable. Regenerated (and
        /// reassigned to <see cref="Node.Mesh"/>) after any edit to <see cref="Head"/>/
        /// <see cref="Tail"/>, the same "optional data drives Mesh, which is just a
        /// cache" convention <see cref="CurveData"/> already established.</summary>
        public Mesh GenerateMesh() => BoneMesher.CreateOctahedron(Head, Tail);

        /// <summary>A complete, independent copy - every field here is a plain value
        /// type, so there is nothing a clone could ever share a reference to (unlike
        /// <see cref="Geometry.Mesh.Material"/>'s own deliberately-shared convention).
        /// Used by <see cref="Node.Clone"/>.</summary>
        public BoneData Clone() => new()
        {
            Head = Head,
            Tail = Tail,
            RestPoseWorldMatrix = RestPoseWorldMatrix,
        };
    }
}
