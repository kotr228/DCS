using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// "Auto Smooth" / the standard "Edge Split" algorithm: smooth-shades a mesh EXCEPT
    /// across any edge whose two owning faces meet at more than <c>angleThresholdDegrees</c>
    /// - the standard hard-surface-modeling middle ground between fully
    /// <see cref="Mesh.ShadeSmooth"/> (every shared vertex averages ALL its own
    /// surrounding faces, including ones that should read as a genuinely sharp corner)
    /// and fully <see cref="Mesh.ShadeFlat"/> (nothing ever averages at all, even a
    /// gently-curved, should-look-smooth surface). Wrapped by
    /// <see cref="Modifiers.EdgeSplitModifier"/> for non-destructive use in the
    /// Modifier stack - kept here as a small, independently testable static helper (the
    /// same "plain Mesh in, plain Mesh out, no Node/Modifier coupling" shape
    /// <see cref="CurveMesher"/>/<see cref="UVProjector"/> already have) since verifying
    /// this algorithm's own edge-classification logic against a concrete hinge-angle
    /// test case is exactly the kind of thing this project's own established rigor
    /// distrusts hand-derivation for and insists on a real, computational check.
    ///
    /// Algorithm: first <see cref="Mesh.WeldVertices"/> (there is no meaningful "shared
    /// edge" to measure an angle across at all unless shared vertices already exist -
    /// two faces authored with separate, duplicated vertex entries at the same
    /// position, like <c>Primitives.CreateCube</c>'s own hard edges, have no edge in
    /// common to begin with, by this mesh format's own index-based definition of an
    /// edge). Then, per welded vertex, partition its own incident faces into groups:
    /// two incident faces are in the SAME group when they share an edge (both
    /// containing this vertex) whose dihedral angle is AT MOST the threshold; a
    /// boundary edge (touched by only one face) or a non-manifold one (touched by 3+)
    /// is always treated as a hard split, the same "nothing to be smooth WITH on the
    /// other side" reasoning <see cref="SolidifyModifier"/>'s own boundary-edge
    /// detection already relies on. Each group becomes its own output vertex, with a
    /// normal averaged over just that group's own faces - so a vertex touched by, say,
    /// 4 faces at a sharp cube-like corner can end up as 3 or 4 SEPARATE output
    /// vertices (one per smoothly-connected cluster), each shared only among the faces
    /// that belong on its own side of every hard edge.
    /// </summary>
    public static class EdgeSplitter
    {
        public static Mesh Apply(Mesh input, float angleThresholdDegrees)
        {
            ArgumentNullException.ThrowIfNull(input);

            var welded = input.Clone();
            welded.WeldVertices();

            var faces = welded.GetRenderFaces().Select(f => (f.A, f.B, f.C)).ToList();
            if (faces.Count == 0) return new Mesh(input.Name) { Material = input.Material };

            var faceNormals = new Vector3[faces.Count];
            for (var i = 0; i < faces.Count; i++)
            {
                var (a, b, c) = faces[i];
                var pa = welded.Vertices[a].Position;
                var pb = welded.Vertices[b].Position;
                var pc = welded.Vertices[c].Position;
                var normal = Vector3.Cross(pb - pa, pc - pa);
                faceNormals[i] = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero;
            }

            // Undirected edge -> every face index touching it.
            var edgeFaces = new Dictionary<(int, int), List<int>>();
            void TrackEdge(int u, int v, int faceIndex)
            {
                var key = u < v ? (u, v) : (v, u);
                if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>();
                list.Add(faceIndex);
            }
            for (var i = 0; i < faces.Count; i++)
            {
                var (a, b, c) = faces[i];
                TrackEdge(a, b, i);
                TrackEdge(b, c, i);
                TrackEdge(c, a, i);
            }

            var cosThreshold = MathF.Cos(angleThresholdDegrees * MathF.PI / 180f);
            bool IsSmoothEdge(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                if (!edgeFaces.TryGetValue(key, out var owners) || owners.Count != 2) return false;
                return Vector3.Dot(faceNormals[owners[0]], faceNormals[owners[1]]) >= cosThreshold;
            }

            var vertexFaces = new List<int>[welded.Vertices.Count];
            for (var i = 0; i < vertexFaces.Length; i++) vertexFaces[i] = new List<int>();
            for (var i = 0; i < faces.Count; i++)
            {
                var (a, b, c) = faces[i];
                vertexFaces[a].Add(i);
                vertexFaces[b].Add(i);
                vertexFaces[c].Add(i);
            }

            var output = new Mesh(input.Name) { Material = input.Material };
            var cornerToNewIndex = new Dictionary<(int Vertex, int Face), int>();

            for (var v = 0; v < welded.Vertices.Count; v++)
            {
                var incident = vertexFaces[v];
                if (incident.Count == 0) continue;

                var parent = new Dictionary<int, int>();
                foreach (var f in incident) parent[f] = f;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                void Union(int x, int y) { var rx = Find(x); var ry = Find(y); if (rx != ry) parent[rx] = ry; }

                // Faces sharing the SAME edge (v, w) - grouped per "other" vertex w of
                // this vertex's own incident faces - unioned when that specific edge is
                // smooth.
                var byOtherVertex = new Dictionary<int, List<int>>();
                foreach (var f in incident)
                {
                    var (a, b, c) = faces[f];
                    foreach (var other in OtherTwo(a, b, c, v))
                    {
                        if (!byOtherVertex.TryGetValue(other, out var list)) byOtherVertex[other] = list = new List<int>();
                        list.Add(f);
                    }
                }

                foreach (var (other, sharing) in byOtherVertex)
                    if (sharing.Count == 2 && IsSmoothEdge(v, other))
                        Union(sharing[0], sharing[1]);

                var groupNormal = new Dictionary<int, Vector3>();
                var groupNewIndex = new Dictionary<int, int>();
                foreach (var f in incident)
                {
                    var root = Find(f);
                    groupNormal[root] = groupNormal.GetValueOrDefault(root) + faceNormals[f];
                }

                foreach (var f in incident)
                {
                    var root = Find(f);
                    if (!groupNewIndex.TryGetValue(root, out var newIndex))
                    {
                        var accumulated = groupNormal[root];
                        var normal = accumulated.LengthSquared() > 1e-12f ? Vector3.Normalize(accumulated) : Vector3.UnitY;
                        newIndex = output.AddVertex(welded.Vertices[v].WithNormal(normal));
                        groupNewIndex[root] = newIndex;
                    }
                    cornerToNewIndex[(v, f)] = newIndex;
                }
            }

            for (var f = 0; f < faces.Count; f++)
            {
                var (a, b, c) = faces[f];
                output.AddTriangle(cornerToNewIndex[(a, f)], cornerToNewIndex[(b, f)], cornerToNewIndex[(c, f)]);
            }

            return output;
        }

        /// <summary>The two OTHER corners of triangle (a,b,c) besides <paramref name="v"/>
        /// (which must be one of a/b/c) - the two vertices <paramref name="v"/>'s own
        /// two edges of this face reach toward.</summary>
        private static IEnumerable<int> OtherTwo(int a, int b, int c, int v)
        {
            if (v == a) { yield return b; yield return c; }
            else if (v == b) { yield return a; yield return c; }
            else { yield return a; yield return b; }
        }
    }
}
