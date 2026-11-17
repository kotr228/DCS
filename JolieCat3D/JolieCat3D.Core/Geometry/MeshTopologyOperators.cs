using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Topology-aware edit operators: edge loops, bridging and knife cuts. Like
    /// <see cref="MeshOperators"/> they treat vertices that share a position as one corner.
    /// </summary>
    public static class MeshTopologyOperators
    {
        /// <summary>Corner ids per vertex plus every face as a list of corner ids.</summary>
        private sealed class CornerGraph
        {
            public int[] CornerOf = Array.Empty<int>();
            public List<int[]> Faces = new();
            public Dictionary<(int, int), List<int>> EdgeFaces = new();
            public Dictionary<int, HashSet<int>> Neighbours = new();

            public static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

            public static CornerGraph Build(Mesh mesh)
            {
                var g = new CornerGraph { CornerOf = new int[mesh.Vertices.Count] };
                var ids = new Dictionary<(int, int, int), int>();
                for (var i = 0; i < mesh.Vertices.Count; i++)
                {
                    var k = MeshOperators.Key(mesh.Vertices[i].Position);
                    if (!ids.TryGetValue(k, out var id)) { id = ids.Count; ids[k] = id; }
                    g.CornerOf[i] = id;
                }

                IEnumerable<IReadOnlyList<int>> rawFaces = mesh.Polygons.Select(p => p.Indices)
                    .Concat(mesh.Faces.Select(f => (IReadOnlyList<int>)new[] { f.A, f.B, f.C }));
                foreach (var raw in rawFaces)
                {
                    var face = raw.Select(i => g.CornerOf[i]).ToArray();
                    var fi = g.Faces.Count;
                    g.Faces.Add(face);
                    for (var i = 0; i < face.Length; i++)
                    {
                        var a = face[i];
                        var b = face[(i + 1) % face.Length];
                        if (a == b) continue;
                        var key = EdgeKey(a, b);
                        if (!g.EdgeFaces.TryGetValue(key, out var list)) g.EdgeFaces[key] = list = new List<int>();
                        list.Add(fi);
                        (g.Neighbours.TryGetValue(a, out var na) ? na : g.Neighbours[a] = new HashSet<int>()).Add(b);
                        (g.Neighbours.TryGetValue(b, out var nb) ? nb : g.Neighbours[b] = new HashSet<int>()).Add(a);
                    }
                }
                return g;
            }

            public IEnumerable<int> VerticesOfCorners(ISet<int> corners)
            {
                for (var i = 0; i < CornerOf.Length; i++)
                    if (corners.Contains(CornerOf[i])) yield return i;
            }
        }

        // =====================================================================
        // Edge loop
        // =====================================================================

        /// <summary>Alt+click: the edge loop through edge (<paramref name="a"/>, <paramref name="b"/>),
        /// walking straight across valence-4 corners in both directions.</summary>
        public static IReadOnlySet<int> SelectEdgeLoop(Mesh mesh, int a, int b)
        {
            var g = CornerGraph.Build(mesh);
            var ca = g.CornerOf[a];
            var cb = g.CornerOf[b];
            var loop = new HashSet<int> { ca, cb };
            Walk(g, ca, cb, loop);
            Walk(g, cb, ca, loop);
            return g.VerticesOfCorners(loop).ToHashSet();
        }

        private static void Walk(CornerGraph g, int from, int to, HashSet<int> loop)
        {
            var guard = 0;
            while (guard++ < 100000)
            {
                if (!g.Neighbours.TryGetValue(to, out var neighbours) || neighbours.Count != 4) return;
                if (!g.EdgeFaces.TryGetValue(CornerGraph.EdgeKey(from, to), out var incoming)) return;

                // The continuation is the neighbour whose edge shares no face with the incoming edge.
                int? next = null;
                foreach (var n in neighbours)
                {
                    if (n == from) continue;
                    if (!g.EdgeFaces.TryGetValue(CornerGraph.EdgeKey(to, n), out var faces)) continue;
                    if (faces.Any(incoming.Contains)) continue;
                    next = n;
                }
                if (next is not { } nxt || !loop.Add(nxt))
                {
                    return; // pole / boundary reached, or the loop closed
                }
                from = to;
                to = nxt;
            }
        }

        // =====================================================================
        // Bridge
        // =====================================================================

        /// <summary>Bridge Edge Loops: joins two selected open-boundary loops (or chains)
        /// with a strip of quads. Returns the selection including the new faces, or null
        /// when the selection is not exactly two boundary loops.</summary>
        public static IReadOnlySet<int>? BridgeEdgeLoops(Mesh mesh, IReadOnlySet<int> selection)
        {
            var g = CornerGraph.Build(mesh);
            var selectedCorners = selection.Where(i => (uint)i < (uint)mesh.Vertices.Count).Select(i => g.CornerOf[i]).ToHashSet();

            // Boundary edges (used by one face) with both corners selected.
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var ((x, y), faces) in g.EdgeFaces)
            {
                if (faces.Count != 1 || !selectedCorners.Contains(x) || !selectedCorners.Contains(y)) continue;
                (adjacency.TryGetValue(x, out var lx) ? lx : adjacency[x] = new List<int>()).Add(y);
                (adjacency.TryGetValue(y, out var ly) ? ly : adjacency[y] = new List<int>()).Add(x);
            }

            var chains = new List<(List<int> Corners, bool Closed)>();
            var visited = new HashSet<int>();
            foreach (var start in adjacency.Keys.OrderBy(k => adjacency[k].Count)) // chain ends first
            {
                if (visited.Contains(start)) continue;
                var chain = new List<int> { start };
                visited.Add(start);
                var current = start;
                while (true)
                {
                    var next = adjacency[current].FirstOrDefault(n => !visited.Contains(n), -1);
                    if (next < 0) break;
                    chain.Add(next);
                    visited.Add(next);
                    current = next;
                }
                var closed = chain.Count > 2 && adjacency[chain[^1]].Contains(chain[0]);
                chains.Add((chain, closed));
            }

            if (chains.Count != 2 || chains[0].Corners.Count != chains[1].Corners.Count || chains[0].Corners.Count < 2) return null;
            if (chains[0].Closed != chains[1].Closed) return null;

            // Representative vertex (position) per corner.
            var rep = new Dictionary<int, int>();
            for (var i = 0; i < g.CornerOf.Length; i++) rep.TryAdd(g.CornerOf[i], i);
            Vector3 P(int corner) => mesh.Vertices[rep[corner]].Position;

            var a = chains[0].Corners;
            var b = chains[1].Corners;
            var closedLoops = chains[0].Closed;
            var n = a.Count;

            // Align B to A: best start offset and direction by total distance.
            var best = (Cost: float.MaxValue, Offset: 0, Reverse: false);
            for (var reverse = 0; reverse < 2; reverse++)
            {
                var candidate = reverse == 1 ? Enumerable.Reverse(b).ToList() : b;
                var offsets = closedLoops ? n : 1;
                for (var o = 0; o < offsets; o++)
                {
                    var cost = 0f;
                    for (var i = 0; i < n; i++) cost += Vector3.DistanceSquared(P(a[i]), P(candidate[(i + o) % n]));
                    if (cost < best.Cost) best = (cost, o, reverse == 1);
                }
            }
            var bAligned = (best.Reverse ? Enumerable.Reverse(b).ToList() : b);
            bAligned = Enumerable.Range(0, n).Select(i => bAligned[(i + best.Offset) % n]).ToList();

            var center = a.Concat(bAligned).Aggregate(Vector3.Zero, (s, c) => s + P(c)) / (2 * n);
            var newSelection = selection.ToHashSet();
            var segments = closedLoops ? n : n - 1;
            for (var i = 0; i < segments; i++)
            {
                var j = (i + 1) % n;
                var quad = new[] { P(a[i]), P(a[j]), P(bAligned[j]), P(bAligned[i]) };
                var normal = Vector3.Cross(quad[1] - quad[0], quad[3] - quad[0]);
                var quadCenter = (quad[0] + quad[1] + quad[2] + quad[3]) / 4f;
                // Point the new faces away from the middle of the bridged shape.
                var radial = quadCenter - center;
                var flip = radial.LengthSquared() > 1e-10f && Vector3.Dot(normal, radial) < 0;
                if (flip) Array.Reverse(quad);
                var faceNormal = Vector3.Cross(quad[1] - quad[0], quad[3] - quad[0]);
                faceNormal = faceNormal.LengthSquared() > 1e-20f ? Vector3.Normalize(faceNormal) : Vector3.UnitY;

                var indices = quad.Select(p => mesh.AddVertex(new Vertex(p, faceNormal, Vector2.Zero, Numerics.Color4.White))).ToArray();
                foreach (var idx in indices) newSelection.Add(idx);
                mesh.AddPolygon(new Polygon(indices));
            }

            return newSelection;
        }


        private static ((int, int, int), (int, int, int), bool Swapped) OrderedEdgeKey(Mesh mesh, int p, int q)
        {
            var kp = MeshOperators.Key(mesh.Vertices[p].Position);
            var kq = MeshOperators.Key(mesh.Vertices[q].Position);
            return Comparer<(int, int, int)>.Default.Compare(kp, kq) <= 0 ? (kp, kq, false) : (kq, kp, true);
        }

        /// <summary>Inserts a vertex on every cut edge (parameter measured from the edge's
        /// lower position key) in every face using that edge, and splits faces cut twice.</summary>
        private static IReadOnlySet<int> ApplyEdgeCuts(Mesh mesh, Dictionary<((int, int, int), (int, int, int)), float> cuts)
        {
            var allFaces = mesh.Polygons.Select(p => (Indices: p.Indices.ToArray(), Slot: p.MaterialSlotIndex))
                .Concat(mesh.Faces.Select(f => (Indices: new[] { f.A, f.B, f.C }, Slot: -1))).ToList();
            ((int, int, int), (int, int, int), bool Swapped) EdgeKey(int p, int q) => OrderedEdgeKey(mesh, p, q);

            var newVertices = new HashSet<int>();
            var polygons = new List<Polygon>();
            var faces = new List<Face>();
            foreach (var (idx, slot) in allFaces)
            {
                var ring = new List<(int Index, bool IsCut)>();
                for (var i = 0; i < idx.Length; i++)
                {
                    var p = idx[i];
                    var q = idx[(i + 1) % idx.Length];
                    ring.Add((p, false));
                    var (k0, k1, swapped) = EdgeKey(p, q);
                    if (!cuts.TryGetValue((k0, k1), out var t)) continue;
                    if (swapped) t = 1 - t;
                    var vp = mesh.Vertices[p];
                    var vq = mesh.Vertices[q];
                    var nv = new Vertex(
                        Vector3.Lerp(vp.Position, vq.Position, t),
                        Vector3.Normalize(Vector3.Lerp(vp.Normal, vq.Normal, t) + new Vector3(1e-9f)),
                        Vector2.Lerp(vp.UV, vq.UV, t),
                        new Numerics.Color4(Vector4.Lerp(vp.Color.Value, vq.Color.Value, t)),
                        vp.BoneIndices,
                        Vector4.Lerp(vp.BoneWeights, vq.BoneWeights, t));
                    var ni = mesh.AddVertex(nv);
                    newVertices.Add(ni);
                    ring.Add((ni, true));
                }

                var cutPositions = ring.Select((r, i) => (r, i)).Where(x => x.r.IsCut).Select(x => x.i).ToList();
                if (cutPositions.Count == 2)
                {
                    var (i0, i1) = (cutPositions[0], cutPositions[1]);
                    var first = ring.Skip(i0).Take(i1 - i0 + 1).Select(r => r.Index).ToList();
                    var second = ring.Skip(i1).Concat(ring.Take(i0 + 1)).Select(r => r.Index).ToList();
                    polygons.Add(new Polygon(first) { MaterialSlotIndex = slot });
                    polygons.Add(new Polygon(second) { MaterialSlotIndex = slot });
                }
                else if (ring.Count == 3 && slot == -1 && idx.Length == 3 && cutPositions.Count == 0)
                {
                    faces.Add(new Face(idx[0], idx[1], idx[2]));
                }
                else
                {
                    polygons.Add(new Polygon(ring.Select(r => r.Index)) { MaterialSlotIndex = slot });
                }
            }

            mesh.SetTopology(faces, polygons);
            return newVertices;
        }

        // =====================================================================
        // Loop cut
        // =====================================================================

        /// <summary>Ctrl+R: cuts the ring of quads crossing edge (<paramref name="a"/>,
        /// <paramref name="b"/>). <paramref name="factor"/> slides the new loop along the ring
        /// edges (0.5 = middle; measured from the side of <paramref name="a"/>). Works across
        /// split vertices, keeps UVs and material slots. Returns the new loop's vertices.</summary>
        public static IReadOnlySet<int>? LoopCut(Mesh mesh, int a, int b, float factor = 0.5f)
        {
            if ((uint)a >= (uint)mesh.Vertices.Count || (uint)b >= (uint)mesh.Vertices.Count) return null;
            factor = Math.Clamp(factor, 0.01f, 0.99f);
            var g = CornerGraph.Build(mesh);
            var quadFaces = new HashSet<int>(Enumerable.Range(0, g.Faces.Count).Where(i => g.Faces[i].Length == 4 && g.Faces[i].Distinct().Count() == 4));

            var rep = new Dictionary<int, int>();
            for (var i = 0; i < g.CornerOf.Length; i++) rep.TryAdd(g.CornerOf[i], i);

            // Oriented ring edges: Item1 is always on the same side of the loop.
            var ring = new List<(int A, int B)>();
            var visitedFaces = new HashSet<int>();
            var start = (g.CornerOf[a], g.CornerOf[b]);
            if (start.Item1 == start.Item2) return null;
            ring.Add(start);

            void Walk((int A, int B) edge, bool forward)
            {
                var guard = 0;
                while (guard++ < 100000)
                {
                    if (!g.EdgeFaces.TryGetValue(CornerGraph.EdgeKey(edge.A, edge.B), out var faces)) return;
                    var next = faces.FirstOrDefault(f => quadFaces.Contains(f) && !visitedFaces.Contains(f), -1);
                    if (next < 0) return;
                    visitedFaces.Add(next);
                    var c = g.Faces[next];
                    (int, int) opposite = (-1, -1);
                    for (var i = 0; i < 4; i++)
                    {
                        if (c[i] == edge.A && c[(i + 1) % 4] == edge.B) { opposite = (c[(i + 3) % 4], c[(i + 2) % 4]); break; }
                        if (c[i] == edge.B && c[(i + 1) % 4] == edge.A) { opposite = (c[(i + 2) % 4], c[(i + 3) % 4]); break; }
                    }
                    if (opposite.Item1 < 0) return;
                    if (CornerGraph.EdgeKey(opposite.Item1, opposite.Item2) == CornerGraph.EdgeKey(start.Item1, start.Item2)) return; // closed ring
                    if (forward) ring.Add(opposite); else ring.Insert(0, opposite);
                    edge = opposite;
                }
            }

            Walk(start, forward: true);
            Walk(start, forward: false);
            if (visitedFaces.Count == 0) return null;

            var cuts = new Dictionary<((int, int, int), (int, int, int)), float>();
            foreach (var (ca, cb) in ring)
            {
                var (k0, k1, swapped) = OrderedEdgeKey(mesh, rep[ca], rep[cb]);
                cuts[(k0, k1)] = swapped ? 1 - factor : factor;
            }
            return ApplyEdgeCuts(mesh, cuts);
        }

        // =====================================================================
        // Knife
        // =====================================================================

        /// <summary>
        /// Straight knife cut. The cut surface is the plane through <paramref name="planePoint"/>
        /// with <paramref name="planeNormal"/> (world space); <paramref name="withinCut"/> limits it
        /// to the drawn segment. Every crossed edge gets a new vertex (in every face using that
        /// edge, so no T-junctions), and faces crossed twice are split in two.
        /// Returns the new cut vertices, or null when nothing was cut.
        /// </summary>
        public static IReadOnlySet<int>? KnifeCut(Mesh mesh, Matrix4x4 world, Vector3 planePoint, Vector3 planeNormal, Func<Vector3, bool> withinCut)
        {
            if (planeNormal.LengthSquared() < 1e-20f) return null;
            planeNormal = Vector3.Normalize(planeNormal);

            var worldPos = mesh.Vertices.Select(v => Vector3.Transform(v.Position, world)).ToArray();
            float Side(int i) => Vector3.Dot(worldPos[i] - planePoint, planeNormal);

            // Edge (by position) -> parameter t from the lower-key end, when crossed.
            var cuts = new Dictionary<((int, int, int), (int, int, int)), float>();
            ((int, int, int), (int, int, int), bool Swapped) EdgeKey(int p, int q)
            {
                var kp = MeshOperators.Key(mesh.Vertices[p].Position);
                var kq = MeshOperators.Key(mesh.Vertices[q].Position);
                return Comparer<(int, int, int)>.Default.Compare(kp, kq) <= 0 ? (kp, kq, false) : (kq, kp, true);
            }

            var allFaces = mesh.Polygons.Select(p => (Indices: p.Indices.ToArray(), Slot: p.MaterialSlotIndex))
                .Concat(mesh.Faces.Select(f => (Indices: new[] { f.A, f.B, f.C }, Slot: -1))).ToList();

            foreach (var (idx, _) in allFaces)
            {
                for (var i = 0; i < idx.Length; i++)
                {
                    var p = idx[i];
                    var q = idx[(i + 1) % idx.Length];
                    var sp = Side(p);
                    var sq = Side(q);
                    if (sp * sq >= 0 || MathF.Abs(sp - sq) < 1e-12f) continue;
                    var t = sp / (sp - sq);
                    var hit = Vector3.Lerp(worldPos[p], worldPos[q], t);
                    if (!withinCut(hit)) continue;
                    var (k0, k1, swapped) = EdgeKey(p, q);
                    cuts[(k0, k1)] = swapped ? 1 - t : t;
                }
            }
            if (cuts.Count == 0) return null;
            return ApplyEdgeCuts(mesh, cuts);
        }
    }
}
