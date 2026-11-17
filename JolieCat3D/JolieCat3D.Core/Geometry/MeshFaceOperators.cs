using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Face-level Edit Mode operators (Blender's Face menu): Poke, Triangulate Faces
    /// (Ctrl+T), Tris to Quads (Alt+J) and Dissolve Faces. Each takes the selected vertex
    /// indices, works on the polygons whose corners are all selected, and returns the new
    /// selection (null = nothing to do).
    /// </summary>
    public static class MeshFaceOperators
    {
        private static List<int> SelectedPolygons(Mesh mesh, IReadOnlySet<int> selection)
        {
            var result = new List<int>();
            for (var i = 0; i < mesh.Polygons.Count; i++)
                if (mesh.Polygons[i].Indices.Count >= 3 && mesh.Polygons[i].Indices.All(selection.Contains)) result.Add(i);
            return result;
        }

        private static Vertex Average(Mesh mesh, IReadOnlyList<int> indices)
        {
            var p = Vector3.Zero; var n = Vector3.Zero; var uv = Vector2.Zero; var c = Vector4.Zero;
            foreach (var i in indices)
            {
                var v = mesh.Vertices[i];
                p += v.Position; n += v.Normal; uv += v.UV; c += v.Color.Value;
            }
            var k = 1f / indices.Count;
            var first = mesh.Vertices[indices[0]];
            return new Vertex(p * k, n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : first.Normal, uv * k, new Numerics.Color4(c * k), first.BoneIndices, first.BoneWeights);
        }

        private static void Replace(Mesh mesh, ICollection<int> removed, IEnumerable<Polygon> added)
        {
            var removedSet = new HashSet<int>(removed);
            var polygons = mesh.Polygons.Where((_, i) => !removedSet.Contains(i)).Concat(added).ToList();
            mesh.SetTopology(mesh.Faces, polygons);
        }

        /// <summary>Poke: a new vertex in the middle of each selected face, fanned to its corners.</summary>
        public static IReadOnlySet<int>? Poke(Mesh mesh, IReadOnlySet<int> selection)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var faces = SelectedPolygons(mesh, selection);
            if (faces.Count == 0) return null;
            var added = new List<Polygon>();
            var newSelection = new HashSet<int>(selection);
            foreach (var fi in faces)
            {
                var poly = mesh.Polygons[fi];
                var centre = mesh.AddVertex(Average(mesh, poly.Indices));
                newSelection.Add(centre);
                for (var i = 0; i < poly.Indices.Count; i++)
                    added.Add(new Polygon(poly.Indices[i], poly.Indices[(i + 1) % poly.Indices.Count], centre) { MaterialSlotIndex = poly.MaterialSlotIndex });
            }
            Replace(mesh, faces, added);
            mesh.RecalculateNormals();
            return newSelection;
        }

        /// <summary>Triangulate Faces (Ctrl+T): quads split along the shorter diagonal, n-gons fanned.</summary>
        public static IReadOnlySet<int>? Triangulate(Mesh mesh, IReadOnlySet<int> selection)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var faces = SelectedPolygons(mesh, selection).Where(i => mesh.Polygons[i].Indices.Count > 3).ToList();
            if (faces.Count == 0) return null;
            var added = new List<Polygon>();
            foreach (var fi in faces)
            {
                var poly = mesh.Polygons[fi];
                var idx = poly.Indices;
                if (idx.Count == 4)
                {
                    var d02 = Vector3.DistanceSquared(mesh.Vertices[idx[0]].Position, mesh.Vertices[idx[2]].Position);
                    var d13 = Vector3.DistanceSquared(mesh.Vertices[idx[1]].Position, mesh.Vertices[idx[3]].Position);
                    if (d13 < d02)
                    {
                        added.Add(new Polygon(idx[0], idx[1], idx[3]) { MaterialSlotIndex = poly.MaterialSlotIndex });
                        added.Add(new Polygon(idx[1], idx[2], idx[3]) { MaterialSlotIndex = poly.MaterialSlotIndex });
                        continue;
                    }
                }
                for (var i = 1; i + 1 < idx.Count; i++)
                    added.Add(new Polygon(idx[0], idx[i], idx[i + 1]) { MaterialSlotIndex = poly.MaterialSlotIndex });
            }
            Replace(mesh, faces, added);
            return new HashSet<int>(selection);
        }

        /// <summary>Tris to Quads (Alt+J): joins neighbouring selected triangles into quads,
        /// best (most square, flattest) pairs first. Triangles with different materials or
        /// that would make a bent or concave quad stay as they are.</summary>
        public static IReadOnlySet<int>? TrisToQuads(Mesh mesh, IReadOnlySet<int> selection, float maxAngleDegrees = 40f)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var tris = SelectedPolygons(mesh, selection).Where(i => mesh.Polygons[i].Indices.Count == 3).ToList();
            if (tris.Count < 2) return null;

            var byEdge = new Dictionary<(int, int), List<int>>();
            foreach (var t in tris)
            {
                var idx = mesh.Polygons[t].Indices;
                for (var k = 0; k < 3; k++)
                {
                    var key = (Math.Min(idx[k], idx[(k + 1) % 3]), Math.Max(idx[k], idx[(k + 1) % 3]));
                    (byEdge.TryGetValue(key, out var l) ? l : byEdge[key] = new List<int>()).Add(t);
                }
            }

            Vector3 P(int i) => mesh.Vertices[i].Position;
            Vector3 Normal(IReadOnlyList<int> idx) => Vector3.Cross(P(idx[1]) - P(idx[0]), P(idx[2]) - P(idx[0]));
            var cosLimit = MathF.Cos(maxAngleDegrees * MathF.PI / 180f);
            var candidates = new List<(float Score, int A, int B, int[] Quad)>();
            foreach (var (edge, owners) in byEdge)
            {
                if (owners.Count != 2) continue;
                var (ta, tb) = (owners[0], owners[1]);
                var pa = mesh.Polygons[ta]; var pb = mesh.Polygons[tb];
                if (pa.MaterialSlotIndex != pb.MaterialSlotIndex) continue;
                var na = Normal(pa.Indices); var nb = Normal(pb.Indices);
                if (na.LengthSquared() < 1e-20f || nb.LengthSquared() < 1e-20f) continue;
                var cos = Vector3.Dot(Vector3.Normalize(na), Vector3.Normalize(nb));
                if (cos < cosLimit) continue;

                // Quad = triangle A with B's far corner inserted on the shared edge (keeps A's winding).
                var a = pa.Indices;
                var far = pb.Indices.First(i => i != edge.Item1 && i != edge.Item2);
                int k0 = -1;
                for (var k = 0; k < 3; k++)
                {
                    var u = a[k]; var v = a[(k + 1) % 3];
                    if ((u == edge.Item1 && v == edge.Item2) || (u == edge.Item2 && v == edge.Item1)) { k0 = k; break; }
                }
                if (k0 < 0) continue;
                var quad = new[] { a[k0], far, a[(k0 + 1) % 3], a[(k0 + 2) % 3] };

                // Convex and close to rectangular.
                var n = Vector3.Normalize(na + nb);
                var convex = true;
                var squareness = 0f;
                for (var k = 0; k < 4; k++)
                {
                    var e1 = P(quad[(k + 1) % 4]) - P(quad[k]);
                    var e2 = P(quad[(k + 2) % 4]) - P(quad[(k + 1) % 4]);
                    if (Vector3.Dot(Vector3.Cross(e1, e2), n) <= 1e-12f) { convex = false; break; }
                    var c = Vector3.Dot(Vector3.Normalize(e1), Vector3.Normalize(e2));
                    squareness += c * c;
                }
                if (!convex) continue;
                candidates.Add((squareness + (1 - cos), ta, tb, quad));
            }
            if (candidates.Count == 0) return null;

            var used = new HashSet<int>();
            var added = new List<Polygon>();
            var removed = new List<int>();
            foreach (var c in candidates.OrderBy(c => c.Score))
            {
                if (used.Contains(c.A) || used.Contains(c.B)) continue;
                used.Add(c.A); used.Add(c.B);
                removed.Add(c.A); removed.Add(c.B);
                added.Add(new Polygon(c.Quad) { MaterialSlotIndex = mesh.Polygons[c.A].MaterialSlotIndex });
            }
            Replace(mesh, removed, added);
            return new HashSet<int>(selection);
        }

        /// <summary>Separate (P > Selection): moves the selected faces into a new mesh (same
        /// material and material slots) and removes them from <paramref name="mesh"/>.
        /// Null when no face is selected.</summary>
        public static Mesh? Separate(Mesh mesh, IReadOnlySet<int> selection)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var faces = SelectedPolygons(mesh, selection);
            if (faces.Count == 0) return null;

            var part = new Mesh(mesh.Name + ".001") { Material = mesh.Material };
            foreach (var slot in mesh.MaterialSlots) part.AddMaterialSlot(slot);
            var map = new Dictionary<int, int>();
            foreach (var fi in faces)
            {
                var poly = mesh.Polygons[fi];
                var indices = poly.Indices.Select(i => map.TryGetValue(i, out var n) ? n : map[i] = part.AddVertex(mesh.Vertices[i])).ToList();
                part.AddPolygon(new Polygon(indices) { MaterialSlotIndex = poly.MaterialSlotIndex });
            }
            foreach (var (a, b) in mesh.SeamEdges)
                if (map.TryGetValue(a, out var na) && map.TryGetValue(b, out var nb)) part.MarkSeam(na, nb);
            foreach (var (a, b) in mesh.CreaseEdges)
                if (map.TryGetValue(a, out var na) && map.TryGetValue(b, out var nb)) part.MarkCrease(na, nb);

            Replace(mesh, faces, Array.Empty<Polygon>());
            mesh.RemoveUnusedVertices();
            return part;
        }

        /// <summary>Dissolve Faces: each connected patch of selected faces becomes one n-gon
        /// (patches with holes are left alone); interior vertices are removed.</summary>
        public static IReadOnlySet<int>? DissolveFaces(Mesh mesh, IReadOnlySet<int> selection)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var faces = SelectedPolygons(mesh, selection);
            if (faces.Count < 2) return null;

            // Patches: faces sharing an edge.
            var parent = faces.ToDictionary(f => f, f => f);
            int Find(int f) { while (parent[f] != f) f = parent[f] = parent[parent[f]]; return f; }
            var edgeOwner = new Dictionary<(int, int), int>();
            foreach (var f in faces)
            {
                var idx = mesh.Polygons[f].Indices;
                for (var k = 0; k < idx.Count; k++)
                {
                    var key = (Math.Min(idx[k], idx[(k + 1) % idx.Count]), Math.Max(idx[k], idx[(k + 1) % idx.Count]));
                    if (edgeOwner.TryGetValue(key, out var other)) parent[Find(f)] = Find(other);
                    else edgeOwner[key] = f;
                }
            }

            var removed = new List<int>();
            var added = new List<Polygon>();
            foreach (var patch in faces.GroupBy(Find))
            {
                var list = patch.ToList();
                if (list.Count < 2) continue;
                // Directed boundary edges: those whose reverse isn't in the patch.
                var directed = new HashSet<(int, int)>();
                foreach (var f in list)
                {
                    var idx = mesh.Polygons[f].Indices;
                    for (var k = 0; k < idx.Count; k++) directed.Add((idx[k], idx[(k + 1) % idx.Count]));
                }
                var next = new Dictionary<int, int>();
                var ok = true;
                foreach (var (a, b) in directed)
                {
                    if (directed.Contains((b, a))) continue;
                    if (next.ContainsKey(a)) { ok = false; break; } // pinched boundary
                    next[a] = b;
                }
                if (!ok || next.Count < 3) continue;
                var loop = new List<int>();
                var start = next.Keys.First();
                var cur = start;
                do
                {
                    loop.Add(cur);
                    if (!next.TryGetValue(cur, out cur) || loop.Count > next.Count) { ok = false; break; }
                } while (cur != start);
                if (!ok || loop.Count != next.Count) continue; // several loops = a hole
                removed.AddRange(list);
                added.Add(new Polygon(loop) { MaterialSlotIndex = mesh.Polygons[list[0]].MaterialSlotIndex });
            }
            if (added.Count == 0) return null;

            Replace(mesh, removed, added);
            var map = mesh.RemoveUnusedVertices();
            var result = new HashSet<int>();
            foreach (var p in added)
                foreach (var i in p.Indices)
                    result.Add(map is null ? i : map[i]);
            mesh.RecalculateNormals();
            return result;
        }
    }
}
