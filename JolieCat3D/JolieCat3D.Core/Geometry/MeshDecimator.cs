using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Polygon reduction for LODs (Blender's Decimate "Collapse"): repeatedly collapses the
    /// edge whose removal changes the shape least, measured with quadric error metrics
    /// (Garland &amp; Heckbert 1997), until the triangle count reaches the requested ratio.
    /// Collapses are half-edge (one end survives unchanged), so kept vertices are exact.
    ///
    /// Topology is welded by position; every triangle corner keeps the attributes (UV,
    /// normal, colour, skin weights) of the vertex it started from, so UV seams and hard
    /// edges survive. Open boundaries and attribute seams are weighted heavily so their
    /// outline is preserved. Collapses that would flip a triangle or make the surface
    /// non-manifold are rejected. The result is triangles (material slots kept); shape
    /// keys are not carried over.
    /// </summary>
    public static class MeshDecimator
    {
        private const double BoundaryWeight = 1000.0;

        /// <summary>Quadric as the 10 unique entries of a symmetric 4x4 matrix.</summary>
        private struct Quadric
        {
            public double A, B, C, D, E, F, G, H, I, J; // aa ab ac ad bb bc bd cc cd dd

            public static Quadric FromPlane(double a, double b, double c, double d, double w) => new()
            {
                A = w * a * a, B = w * a * b, C = w * a * c, D = w * a * d,
                E = w * b * b, F = w * b * c, G = w * b * d,
                H = w * c * c, I = w * c * d, J = w * d * d,
            };

            public void Add(in Quadric q)
            {
                A += q.A; B += q.B; C += q.C; D += q.D; E += q.E;
                F += q.F; G += q.G; H += q.H; I += q.I; J += q.J;
            }

            public readonly double Error(Vector3 p)
            {
                double x = p.X, y = p.Y, z = p.Z;
                return A * x * x + 2 * B * x * y + 2 * C * x * z + 2 * D * x
                     + E * y * y + 2 * F * y * z + 2 * G * y
                     + H * z * z + 2 * I * z + J;
            }
        }

        /// <summary>
        /// Returns a new mesh with about <paramref name="ratio"/> of the input's triangles
        /// (0..1; 1 returns the input unchanged).
        /// </summary>
        public static Mesh Decimate(Mesh input, float ratio)
        {
            ArgumentNullException.ThrowIfNull(input);
            ratio = Math.Clamp(ratio, 0f, 1f);
            if (ratio >= 0.9999f) return input;

            // ---- triangles over welded corners ----
            var cornerId = new Dictionary<(int, int, int), int>();
            var positions = new List<Vector3>();
            var cornerOfVertex = new int[input.Vertices.Count];
            for (var i = 0; i < input.Vertices.Count; i++)
            {
                var k = MeshOperators.Key(input.Vertices[i].Position);
                if (!cornerId.TryGetValue(k, out var id)) { id = positions.Count; cornerId[k] = id; positions.Add(input.Vertices[i].Position); }
                cornerOfVertex[i] = id;
            }

            var triCorners = new List<int[]>();
            var triVertices = new List<int[]>();
            var triSlot = new List<int>();
            void AddTri(int a, int b, int c, int slot)
            {
                var ca = cornerOfVertex[a];
                var cb = cornerOfVertex[b];
                var cc = cornerOfVertex[c];
                if (ca == cb || cb == cc || ca == cc) return;
                triCorners.Add(new[] { ca, cb, cc });
                triVertices.Add(new[] { a, b, c });
                triSlot.Add(slot);
            }
            foreach (var f in input.Faces) AddTri(f.A, f.B, f.C, -1);
            foreach (var p in input.Polygons)
                for (var i = 1; i + 1 < p.Indices.Count; i++) AddTri(p.Indices[0], p.Indices[i], p.Indices[i + 1], p.MaterialSlotIndex);

            var triCount = triCorners.Count;
            var target = Math.Max(1, (int)Math.Round(triCount * ratio));
            if (triCount <= target) return input;

            var removed = new bool[triCount];
            var cornerCount = positions.Count;
            var adjacency = new List<int>[cornerCount];
            for (var i = 0; i < cornerCount; i++) adjacency[i] = new List<int>();
            for (var t = 0; t < triCount; t++) foreach (var c in triCorners[t]) adjacency[c].Add(t);

            // ---- quadrics: face planes + heavy planes along boundaries / attribute seams ----
            var quadrics = new Quadric[cornerCount];
            var edgeUse = new Dictionary<(int, int), List<(int Tri, int Va, int Vb)>>();
            for (var t = 0; t < triCount; t++)
            {
                var c = triCorners[t];
                var n = Vector3.Cross(positions[c[1]] - positions[c[0]], positions[c[2]] - positions[c[0]]);
                var area = n.Length();
                if (area < 1e-20f) continue;
                n /= area;
                var plane = Quadric.FromPlane(n.X, n.Y, n.Z, -Vector3.Dot(n, positions[c[0]]), area);
                foreach (var corner in c) quadrics[corner].Add(plane);
                for (var i = 0; i < 3; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    var va = triVertices[t][i];
                    var vb = triVertices[t][(i + 1) % 3];
                    (edgeUse.TryGetValue(key, out var l) ? l : edgeUse[key] = new List<(int, int, int)>()).Add(a < b ? (t, va, vb) : (t, vb, va));
                }
            }
            foreach (var (edge, uses) in edgeUse)
            {
                var isBoundary = uses.Count == 1;
                var isSeam = uses.Count == 2 && (uses[0].Va != uses[1].Va || uses[0].Vb != uses[1].Vb);
                if (!isBoundary && !isSeam) continue;
                foreach (var (t, _, _) in uses)
                {
                    var c = triCorners[t];
                    var faceNormal = Vector3.Cross(positions[c[1]] - positions[c[0]], positions[c[2]] - positions[c[0]]);
                    var dir = positions[edge.Item2] - positions[edge.Item1];
                    var side = Vector3.Cross(dir, faceNormal);
                    if (side.LengthSquared() < 1e-20f) continue;
                    side = Vector3.Normalize(side);
                    var weight = BoundaryWeight * dir.LengthSquared();
                    var plane = Quadric.FromPlane(side.X, side.Y, side.Z, -Vector3.Dot(side, positions[edge.Item1]), weight);
                    quadrics[edge.Item1].Add(plane);
                    quadrics[edge.Item2].Add(plane);
                }
            }

            // ---- greedy collapses ----
            var version = new int[cornerCount];
            var dead = new bool[cornerCount];
            var queue = new PriorityQueue<(int A, int B, int VA, int VB), double>();

            // Half-edge collapse: the merged corner stays at one of the two ends, so every
            // surviving vertex keeps its exact UV / weights (what game LODs want).
            (double Cost, bool KeepB) Evaluate(int a, int b)
            {
                var q = quadrics[a];
                q.Add(quadrics[b]);
                var costB = q.Error(positions[b]);
                var costA = q.Error(positions[a]);
                return costB <= costA ? (Math.Max(0, costB), true) : (Math.Max(0, costA), false);
            }

            void PushEdgesOf(int corner)
            {
                var seen = new HashSet<int>();
                foreach (var t in adjacency[corner])
                {
                    if (removed[t]) continue;
                    foreach (var other in triCorners[t])
                    {
                        if (other == corner || !seen.Add(other)) continue;
                        var (cost, _) = Evaluate(corner, other);
                        queue.Enqueue((corner, other, version[corner], version[other]), cost);
                    }
                }
            }

            for (var c = 0; c < cornerCount; c++) PushEdgesOf(c);

            var alive = triCount;
            while (alive > target && queue.TryDequeue(out var item, out _))
            {
                var (ea, eb, va, vb) = item;
                if (dead[ea] || dead[eb] || version[ea] != va || version[eb] != vb) continue;
                var (_, keepB) = Evaluate(ea, eb);
                var (a, b) = keepB ? (ea, eb) : (eb, ea); // a is removed, b survives
                var position = positions[b];
                if (!CanCollapse(a, b, position)) continue;

                // Attribute vertices of a map to b's on the same side of the edge.
                var attributeMap = new Dictionary<int, int>();
                foreach (var t in adjacency[a])
                {
                    if (removed[t]) continue;
                    var c = triCorners[t];
                    var ib = Array.IndexOf(c, b);
                    if (ib < 0) continue;
                    attributeMap[triVertices[t][Array.IndexOf(c, a)]] = triVertices[t][ib];
                }

                // Collapse a into b.
                foreach (var t in adjacency[a])
                {
                    if (removed[t]) continue;
                    var c = triCorners[t];
                    if (c[0] == b || c[1] == b || c[2] == b) { removed[t] = true; alive--; continue; }
                    for (var i = 0; i < 3; i++)
                        if (c[i] == a)
                        {
                            c[i] = b;
                            if (attributeMap.TryGetValue(triVertices[t][i], out var mapped)) triVertices[t][i] = mapped;
                        }
                    adjacency[b].Add(t);
                }
                adjacency[a].Clear();
                dead[a] = true;
                quadrics[b].Add(quadrics[a]);
                version[b]++;
                adjacency[b].RemoveAll(t => removed[t]);
                foreach (var t in adjacency[b]) foreach (var other in triCorners[t]) if (other != b) version[other]++;
                PushEdgesOf(b);
                foreach (var t in adjacency[b]) foreach (var other in triCorners[t]) if (other != b) PushEdgesOf(other);
            }

            bool CanCollapse(int a, int b, Vector3 position)
            {
                // Link condition: the shared neighbours must be exactly the ones of the
                // triangles on the edge (keeps the surface manifold).
                var na = new HashSet<int>();
                var nb = new HashSet<int>();
                var shared = 0;
                foreach (var t in adjacency[a]) if (!removed[t]) { foreach (var x in triCorners[t]) if (x != a) na.Add(x); if (Array.IndexOf(triCorners[t], b) >= 0) shared++; }
                foreach (var t in adjacency[b]) if (!removed[t]) foreach (var x in triCorners[t]) if (x != b) nb.Add(x);
                na.Remove(b);
                nb.Remove(a);
                na.IntersectWith(nb);
                if (na.Count != shared) return false;
                if (shared == 0) return false;

                // No flipped or degenerate triangles around either end.
                foreach (var (corner, other) in new[] { (a, b), (b, a) })
                    foreach (var t in adjacency[corner])
                    {
                        if (removed[t]) continue;
                        var c = triCorners[t];
                        if (Array.IndexOf(c, other) >= 0) continue;
                        var p = new Vector3[3];
                        for (var i = 0; i < 3; i++) p[i] = c[i] == corner ? position : positions[c[i]];
                        var before = Vector3.Cross(positions[c[1]] - positions[c[0]], positions[c[2]] - positions[c[0]]);
                        var after = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
                        if (after.LengthSquared() < 1e-24f) return false;
                        if (Vector3.Dot(Vector3.Normalize(before), Vector3.Normalize(after)) < 0.2f) return false;
                    }
                return true;
            }

            // ---- output: one vertex per (corner, original vertex) ----
            var output = new Mesh(input.Name) { Material = input.Material };
            foreach (var slot in input.MaterialSlots) output.AddMaterialSlot(slot);
            var newIndex = new Dictionary<(int Corner, int Vertex), int>();
            for (var t = 0; t < triCount; t++)
            {
                if (removed[t]) continue;
                var idx = new int[3];
                for (var i = 0; i < 3; i++)
                {
                    var corner = triCorners[t][i];
                    var vertex = triVertices[t][i];
                    if (!newIndex.TryGetValue((corner, vertex), out var ni))
                    {
                        ni = output.AddVertex(input.Vertices[vertex].WithPosition(positions[corner]));
                        newIndex[(corner, vertex)] = ni;
                    }
                    idx[i] = ni;
                }
                if (triSlot[t] >= 0) output.AddPolygon(new Polygon(idx) { MaterialSlotIndex = triSlot[t] });
                else output.AddTriangle(idx[0], idx[1], idx[2]);
            }
            // Normals: average the faces around each corner whose original vertex normal
            // matched this vertex's (UV seams stay smooth, hard edges stay hard).
            var faceNormals = new Dictionary<int, Vector3>();
            var cornerTris = new Dictionary<int, List<int>>();
            for (var t = 0; t < triCount; t++)
            {
                if (removed[t]) continue;
                var c = triCorners[t];
                faceNormals[t] = Vector3.Cross(positions[c[1]] - positions[c[0]], positions[c[2]] - positions[c[0]]);
                foreach (var corner in c) (cornerTris.TryGetValue(corner, out var l) ? l : cornerTris[corner] = new List<int>()).Add(t);
            }
            foreach (var ((corner, vertex), index) in newIndex)
            {
                var own = input.Vertices[vertex].Normal;
                var sum = Vector3.Zero;
                foreach (var t in cornerTris[corner])
                {
                    var other = input.Vertices[triVertices[t][Array.IndexOf(triCorners[t], corner)]].Normal;
                    if (Vector3.Dot(own, other) > 0.9f) sum += faceNormals[t];
                }
                output.SetVertexNormal(index, sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : own);
            }
            return output;
        }
    }
}
