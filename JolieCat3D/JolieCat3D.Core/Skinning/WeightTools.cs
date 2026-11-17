using System.Numerics;
using System.Text.RegularExpressions;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Skinning
{
    /// <summary>
    /// Weight Paint tools (Blender's Weights menu): Normalize All, Smooth and Mirror.
    /// Weights are handled per position-welded corner so vertices split for UV seams or
    /// hard edges never end up with different weights.
    /// </summary>
    public static class WeightTools
    {
        private const int MaxInfluences = 4;

        /// <summary>Makes every weighted vertex's influences sum to 1. Returns how many changed.</summary>
        public static int Normalize(Mesh mesh)
        {
            var changed = 0;
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var w = mesh.Vertices[i].BoneWeights;
                var sum = w.X + w.Y + w.Z + w.W;
                if (sum < 1e-6f || MathF.Abs(sum - 1f) < 1e-5f) continue;
                mesh.SetVertexBoneWeights(i, mesh.Vertices[i].BoneIndices, w / sum);
                changed++;
            }
            return changed;
        }

        /// <summary>Blends every vertex's weights with its neighbours' (Laplacian), keeping
        /// the strongest 4 influences, normalized. <paramref name="factor"/> 0..1 per pass.</summary>
        public static void Smooth(Mesh mesh, int iterations = 2, float factor = 0.5f)
        {
            var (cornerOf, corners) = Corners(mesh);
            var weights = new Dictionary<int, float>[corners];
            for (var c = 0; c < corners; c++) weights[c] = new Dictionary<int, float>();
            var seen = new bool[corners];
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var c = cornerOf[i];
                if (seen[c]) continue;
                seen[c] = true;
                foreach (var (bone, weight) in Influences(mesh.Vertices[i])) weights[c][bone] = weight;
            }

            var neighbours = new HashSet<int>[corners];
            for (var c = 0; c < corners; c++) neighbours[c] = new HashSet<int>();
            foreach (var f in mesh.GetRenderFaces())
            {
                int a = cornerOf[f.A], b = cornerOf[f.B], cc = cornerOf[f.C];
                neighbours[a].Add(b); neighbours[a].Add(cc);
                neighbours[b].Add(a); neighbours[b].Add(cc);
                neighbours[cc].Add(a); neighbours[cc].Add(b);
            }

            factor = Math.Clamp(factor, 0f, 1f);
            for (var it = 0; it < Math.Max(1, iterations); it++)
            {
                var next = new Dictionary<int, float>[corners];
                for (var c = 0; c < corners; c++)
                {
                    var result = new Dictionary<int, float>();
                    foreach (var (bone, w) in weights[c]) result[bone] = w * (1f - factor);
                    if (neighbours[c].Count == 0)
                    {
                        foreach (var (bone, w) in weights[c]) result[bone] = w;
                        next[c] = result;
                        continue;
                    }
                    var share = factor / neighbours[c].Count;
                    foreach (var n in neighbours[c])
                        foreach (var (bone, w) in weights[n])
                            result[bone] = result.GetValueOrDefault(bone) + w * share;
                    next[c] = result;
                }
                weights = next;
            }

            for (var i = 0; i < mesh.Vertices.Count; i++) Write(mesh, i, weights[cornerOf[i]]);
        }

        /// <summary>
        /// Mirror Weights across X: every vertex on the destination side takes the weights
        /// of its mirror-image vertex, with each bone swapped for its opposite-side twin by
        /// name (Arm.L ↔ Arm.R, Hand_L ↔ Hand_R, LeftLeg ↔ RightLeg; centre bones map to
        /// themselves). <paramref name="positiveToNegative"/> copies +X onto -X. Returns the
        /// number of vertices that received weights.
        /// </summary>
        public static int MirrorX(Mesh mesh, IReadOnlyList<Node> bones, bool positiveToNegative = true)
        {
            var boneMap = new int[bones.Count];
            for (var b = 0; b < bones.Count; b++)
            {
                var twin = MirrorName(bones[b].Name);
                var index = twin is null ? -1 : FindIndex(bones, twin);
                boneMap[b] = index >= 0 ? index : b;
            }

            var byPosition = new Dictionary<(int, int, int), int>();
            for (var i = 0; i < mesh.Vertices.Count; i++) byPosition.TryAdd(MeshOperators.Key(mesh.Vertices[i].Position), i);

            var sourceSign = positiveToNegative ? 1f : -1f;
            var updates = new List<(int Target, Dictionary<int, float> Weights)>();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var p = mesh.Vertices[i].Position;
                if (p.X * sourceSign >= -1e-5f) continue; // only destination-side vertices
                if (!byPosition.TryGetValue(MeshOperators.Key(new Vector3(-p.X, p.Y, p.Z)), out var source)) continue;
                var mirrored = new Dictionary<int, float>();
                foreach (var (bone, w) in Influences(mesh.Vertices[source]))
                {
                    var target = bone < boneMap.Length ? boneMap[bone] : bone;
                    mirrored[target] = mirrored.GetValueOrDefault(target) + w;
                }
                updates.Add((i, mirrored));
            }
            foreach (var (target, weights) in updates) Write(mesh, target, weights);
            return updates.Count;
        }

        /// <summary>The opposite-side name for a bone, or null when it has no side.</summary>
        public static string? MirrorName(string name)
        {
            (string Pattern, string Left, string Right)[] rules =
            {
                (@"([._ -])L$", "L", "R"), (@"([._ -])R$", "R", "L"),
                (@"([._ -])l$", "l", "r"), (@"([._ -])r$", "r", "l"),
                (@"^L([._ -])", "L", "R"), (@"^R([._ -])", "R", "L"),
            };
            foreach (var (pattern, from, to) in rules)
            {
                var m = Regex.Match(name, pattern);
                if (!m.Success) continue;
                return m.Index == 0 && pattern.StartsWith('^')
                    ? to + name[from.Length..]
                    : name[..(name.Length - from.Length)] + to;
            }
            if (name.Contains("Left", StringComparison.Ordinal)) return name.Replace("Left", "Right", StringComparison.Ordinal);
            if (name.Contains("Right", StringComparison.Ordinal)) return name.Replace("Right", "Left", StringComparison.Ordinal);
            if (name.Contains("left", StringComparison.Ordinal)) return name.Replace("left", "right", StringComparison.Ordinal);
            if (name.Contains("right", StringComparison.Ordinal)) return name.Replace("right", "left", StringComparison.Ordinal);
            return null;
        }

        private static int FindIndex(IReadOnlyList<Node> bones, string name)
        {
            for (var i = 0; i < bones.Count; i++) if (bones[i].Name == name) return i;
            return -1;
        }

        private static (int[] CornerOf, int Count) Corners(Mesh mesh)
        {
            var cornerOf = new int[mesh.Vertices.Count];
            var ids = new Dictionary<(int, int, int), int>();
            for (var i = 0; i < cornerOf.Length; i++)
            {
                var key = MeshOperators.Key(mesh.Vertices[i].Position);
                if (!ids.TryGetValue(key, out var id)) ids[key] = id = ids.Count;
                cornerOf[i] = id;
            }
            return (cornerOf, ids.Count);
        }

        private static IEnumerable<(int Bone, float Weight)> Influences(Vertex v)
        {
            if (v.BoneWeights.X > 0) yield return (v.BoneIndices.X, v.BoneWeights.X);
            if (v.BoneWeights.Y > 0) yield return (v.BoneIndices.Y, v.BoneWeights.Y);
            if (v.BoneWeights.Z > 0) yield return (v.BoneIndices.Z, v.BoneWeights.Z);
            if (v.BoneWeights.W > 0) yield return (v.BoneIndices.W, v.BoneWeights.W);
        }

        private static void Write(Mesh mesh, int index, Dictionary<int, float> weights)
        {
            var top = weights.Where(kv => kv.Value > 1e-4f).OrderByDescending(kv => kv.Value).Take(MaxInfluences).ToArray();
            var sum = top.Sum(kv => kv.Value);
            if (sum < 1e-6f) { mesh.SetVertexBoneWeights(index, default, Vector4.Zero); return; }
            int I(int k) => k < top.Length ? top[k].Key : 0;
            float W(int k) => k < top.Length ? top[k].Value / sum : 0f;
            mesh.SetVertexBoneWeights(index, new BoneIndices(I(0), I(1), I(2), I(3)), new Vector4(W(0), W(1), W(2), W(3)));
        }
    }
}
