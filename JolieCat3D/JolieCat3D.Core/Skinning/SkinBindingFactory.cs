using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// Creates a <see cref="SkinBinding"/> for a mesh node against every bone under a
    /// given armature ("Bind to Armature") - the task's own implicit prerequisite step
    /// before Weight Paint mode has anything to paint at all. Every vertex starts 100%
    /// weighted to whichever bone's own Head-Tail segment it sits CLOSEST to in world
    /// space (the standard "automatic weights" starting point most tools offer before
    /// any hand-painting), so a freshly-bound mesh already deforms sensibly instead of
    /// staying frozen (every weight at 0) until painted one vertex at a time.
    /// </summary>
    public static class SkinBindingFactory
    {
        public static SkinBinding CreateAutomatic(Node meshNode, Node armatureNode)
        {
            ArgumentNullException.ThrowIfNull(meshNode);
            ArgumentNullException.ThrowIfNull(armatureNode);

            var binding = new SkinBinding { Armature = armatureNode };
            binding.Bones.AddRange(armatureNode.Traverse().Where(candidate => candidate.Bone is not null));

            if (meshNode.Mesh is { } mesh && binding.Bones.Count > 0)
            {
                var meshWorldTransform = meshNode.GetWorldTransform();
                for (var i = 0; i < mesh.Vertices.Count; i++)
                {
                    var worldPosition = Vector3.Transform(mesh.Vertices[i].Position, meshWorldTransform);
                    var nearestBoneIndex = FindNearestBoneIndex(worldPosition, binding.Bones);
                    mesh.SetVertexBoneWeights(i, new BoneIndices(nearestBoneIndex, 0, 0, 0), new Vector4(1f, 0f, 0f, 0f));
                }
            }

            return binding;
        }

        private static int FindNearestBoneIndex(Vector3 worldPosition, IReadOnlyList<Node> bones)
        {
            var bestIndex = 0;
            var bestDistanceSquared = float.MaxValue;

            for (var i = 0; i < bones.Count; i++)
            {
                var boneData = bones[i].Bone;
                if (boneData is null) continue;

                var boneWorldTransform = bones[i].GetWorldTransform();
                var headWorld = Vector3.Transform(boneData.Head, boneWorldTransform);
                var tailWorld = Vector3.Transform(boneData.Tail, boneWorldTransform);

                var distanceSquared = DistanceSquaredToSegment(worldPosition, headWorld, tailWorld);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>The standard clamped-projection point-to-segment distance (squared,
        /// to avoid an unnecessary square root per bone per vertex) - degenerate
        /// (zero-length) segments fall back to plain point-to-point distance from
        /// <paramref name="segmentStart"/> rather than dividing by zero.</summary>
        private static float DistanceSquaredToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var lengthSquared = segment.LengthSquared();
            if (lengthSquared < 1e-12f) return Vector3.DistanceSquared(point, segmentStart);

            var t = Math.Clamp(Vector3.Dot(point - segmentStart, segment) / lengthSquared, 0f, 1f);
            var closestPoint = segmentStart + segment * t;
            return Vector3.DistanceSquared(point, closestPoint);
        }
    }
}
