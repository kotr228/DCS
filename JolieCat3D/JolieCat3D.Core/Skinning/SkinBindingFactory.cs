using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// Creates a <see cref="SkinBinding"/> for a mesh node against every bone under a
    /// given armature ("Bind with Automatic Weights"). Each vertex gets up to 4 bone
    /// influences, falling off with the distance to each bone's Head-Tail segment
    /// (inverse 4th power, so the nearest bone dominates), then smoothed over the mesh
    /// surface so joints bend softly instead of tearing. Vertices sharing a position get
    /// identical weights (no cracks along UV seams / hard edges).
    /// </summary>
    public static class SkinBindingFactory
    {
        private const int MaxInfluences = 4;
        private const int SmoothingIterations = 4;

        public static SkinBinding CreateAutomatic(Node meshNode, Node armatureNode)
        {
            ArgumentNullException.ThrowIfNull(meshNode);
            ArgumentNullException.ThrowIfNull(armatureNode);

            var binding = new SkinBinding { Armature = armatureNode };
            binding.Bones.AddRange(armatureNode.Traverse().Where(candidate => candidate.Bone is not null));

            if (meshNode.Mesh is { } mesh && binding.Bones.Count > 0 && mesh.Vertices.Count > 0)
                AssignAutomaticWeights(mesh, meshNode.GetWorldTransform(), binding.Bones);

            return binding;
        }

        /// <summary>Distance-falloff weights, smoothed over the surface; see the class remarks.</summary>
        public static void AssignAutomaticWeights(Mesh mesh, Matrix4x4 meshWorld, IReadOnlyList<Node> bones)
        {
            var boneCount = bones.Count;
            var segments = bones.Select(b =>
            {
                var world = b.GetWorldTransform();
                var data = b.Bone ?? new BoneData();
                return (Head: Vector3.Transform(data.Head, world), Tail: Vector3.Transform(data.Tail, world));
            }).ToArray();

            // Corners: vertices welded by position.
            var cornerOf = new int[mesh.Vertices.Count];
            var cornerIds = new Dictionary<(int, int, int), int>();
            var cornerPos = new List<Vector3>();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var k = MeshOperators.Key(mesh.Vertices[i].Position);
                if (!cornerIds.TryGetValue(k, out var id)) { id = cornerPos.Count; cornerIds[k] = id; cornerPos.Add(Vector3.Transform(mesh.Vertices[i].Position, meshWorld)); }
                cornerOf[i] = id;
            }
            var corners = cornerPos.Count;

            var min = cornerPos.Aggregate(Vector3.Min);
            var max = cornerPos.Aggregate(Vector3.Max);
            var softness = MathF.Max(1e-4f, Vector3.Distance(min, max) * 0.01f);

            // Raw falloff weights.
            var weights = new float[corners * boneCount];
            for (var c = 0; c < corners; c++)
            {
                var distances = new float[boneCount];
                var nearest = float.MaxValue;
                for (var b = 0; b < boneCount; b++)
                {
                    distances[b] = MathF.Sqrt(DistanceSquaredToSegment(cornerPos[c], segments[b].Head, segments[b].Tail));
                    nearest = MathF.Min(nearest, distances[b]);
                }
                var sum = 0f;
                for (var b = 0; b < boneCount; b++)
                {
                    // Bones much farther than the nearest one don't pull this vertex at all.
                    if (distances[b] > nearest * 2.5f + softness * 2) continue;
                    var w = 1f / MathF.Pow(distances[b] + softness, 4);
                    weights[c * boneCount + b] = w;
                    sum += w;
                }
                for (var b = 0; b < boneCount; b++) weights[c * boneCount + b] /= sum;
            }

            // Surface smoothing (Laplacian) over corner adjacency.
            var neighbours = new HashSet<int>[corners];
            for (var c = 0; c < corners; c++) neighbours[c] = new HashSet<int>();
            foreach (var f in mesh.GetRenderFaces())
            {
                var a = cornerOf[f.A]; var b = cornerOf[f.B]; var cc = cornerOf[f.C];
                neighbours[a].Add(b); neighbours[a].Add(cc);
                neighbours[b].Add(a); neighbours[b].Add(cc);
                neighbours[cc].Add(a); neighbours[cc].Add(b);
            }
            var next = new float[weights.Length];
            for (var iteration = 0; iteration < SmoothingIterations; iteration++)
            {
                for (var c = 0; c < corners; c++)
                {
                    var count = neighbours[c].Count;
                    for (var b = 0; b < boneCount; b++)
                    {
                        var own = weights[c * boneCount + b];
                        if (count == 0) { next[c * boneCount + b] = own; continue; }
                        var avg = 0f;
                        foreach (var n in neighbours[c]) avg += weights[n * boneCount + b];
                        next[c * boneCount + b] = own * 0.5f + avg / count * 0.5f;
                    }
                }
                (weights, next) = (next, weights);
            }

            // Top 4 per corner, normalised, written to every vertex of that corner.
            var top = new (int Bone, float Weight)[corners][];
            for (var c = 0; c < corners; c++)
            {
                var best = Enumerable.Range(0, boneCount)
                    .Select(b => (Bone: b, Weight: weights[c * boneCount + b]))
                    .Where(t => t.Weight > 1e-4f)
                    .OrderByDescending(t => t.Weight)
                    .Take(MaxInfluences).ToArray();
                if (best.Length == 0) best = new[] { (Bone: 0, Weight: 1f) };
                var total = best.Sum(t => t.Weight);
                top[c] = best.Select(t => (t.Bone, t.Weight / total)).ToArray();
            }
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var t = top[cornerOf[i]];
                int Index(int k) => k < t.Length ? t[k].Bone : 0;
                float Weight(int k) => k < t.Length ? t[k].Weight : 0f;
                mesh.SetVertexBoneWeights(i, new BoneIndices(Index(0), Index(1), Index(2), Index(3)), new Vector4(Weight(0), Weight(1), Weight(2), Weight(3)));
            }
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
