using System.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Blender-style edit-mode operators working on a vertex-index selection.
    ///
    /// Meshes here may store one corner as several vertices (split normals/UVs), so every
    /// operator reasons about <em>positions</em> (see <see cref="Key"/>): two vertices at the
    /// same spot are the same corner, exactly like Blender's welded topology. Each operator
    /// returns the new selection (vertex indices) or null when it could not apply.
    /// </summary>
    public static class MeshOperators
    {
        private const float KeyScale = 100000f;

        public static (int, int, int) Key(Vector3 p) =>
            ((int)MathF.Round(p.X * KeyScale), (int)MathF.Round(p.Y * KeyScale), (int)MathF.Round(p.Z * KeyScale));

        /// <summary>All polygons whose every corner is selected (triangles become 3-gons).</summary>
        private static List<Polygon> SelectedPolygons(Mesh mesh, IReadOnlySet<int> selection, out List<Polygon> others, out List<Face> otherFaces)
        {
            var selected = new List<Polygon>();
            others = new List<Polygon>();
            otherFaces = new List<Face>();
            foreach (var p in mesh.Polygons)
            {
                if (p.Indices.All(selection.Contains)) selected.Add(p);
                else others.Add(p);
            }
            foreach (var f in mesh.Faces)
            {
                if (selection.Contains(f.A) && selection.Contains(f.B) && selection.Contains(f.C)) selected.Add(new Polygon(f.A, f.B, f.C));
                else otherFaces.Add(f);
            }
            return selected;
        }

        private static Vector3 Centroid(Mesh mesh, IEnumerable<int> indices)
        {
            var sum = Vector3.Zero;
            var n = 0;
            foreach (var i in indices) { sum += mesh.Vertices[i].Position; n++; }
            return n == 0 ? Vector3.Zero : sum / n;
        }

        private static Vector3 PolygonNormal(Mesh mesh, IReadOnlyList<int> idx)
        {
            // Newell's method - robust for any planar-ish n-gon.
            var n = Vector3.Zero;
            for (var i = 0; i < idx.Count; i++)
            {
                var a = mesh.Vertices[idx[i]].Position;
                var b = mesh.Vertices[idx[(i + 1) % idx.Count]].Position;
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
        }

        // =====================================================================
        // Merge
        // =====================================================================

        /// <summary>M → At Center: collapses the selection into one point and removes the
        /// faces that became degenerate.</summary>
        public static IReadOnlySet<int>? MergeAtCenter(Mesh mesh, IReadOnlySet<int> selection)
        {
            var valid = selection.Where(i => (uint)i < (uint)mesh.Vertices.Count).ToList();
            if (valid.Select(i => Key(mesh.Vertices[i].Position)).Distinct().Count() < 2) return null;

            var center = Centroid(mesh, valid.DistinctBy(i => Key(mesh.Vertices[i].Position)));
            foreach (var i in valid) mesh.SetVertexPosition(i, center);
            RemoveDegenerate(mesh);
            mesh.RemoveUnusedVertices();

            var key = Key(center);
            return Enumerable.Range(0, mesh.Vertices.Count).Where(i => Key(mesh.Vertices[i].Position) == key).ToHashSet();
        }

        /// <summary>Drops repeated corners inside polygons and polygons/triangles with fewer than three distinct corners.</summary>
        public static void RemoveDegenerate(Mesh mesh)
        {
            var polygons = new List<Polygon>();
            foreach (var p in mesh.Polygons)
            {
                var cleaned = new List<int>();
                foreach (var i in p.Indices)
                {
                    var k = Key(mesh.Vertices[i].Position);
                    if (cleaned.Count > 0 && Key(mesh.Vertices[cleaned[^1]].Position) == k) continue;
                    cleaned.Add(i);
                }
                while (cleaned.Count > 1 && Key(mesh.Vertices[cleaned[0]].Position) == Key(mesh.Vertices[cleaned[^1]].Position)) cleaned.RemoveAt(cleaned.Count - 1);
                if (cleaned.Select(i => Key(mesh.Vertices[i].Position)).Distinct().Count() >= 3)
                    polygons.Add(new Polygon(cleaned) { MaterialSlotIndex = p.MaterialSlotIndex });
            }

            var faces = mesh.Faces.Where(f =>
            {
                var a = Key(mesh.Vertices[f.A].Position);
                var b = Key(mesh.Vertices[f.B].Position);
                var c = Key(mesh.Vertices[f.C].Position);
                return a != b && b != c && a != c;
            }).ToList();

            mesh.SetTopology(faces, polygons);
        }

        // =====================================================================
        // Inset
        // =====================================================================

        /// <summary>I: insets each selected face individually by <paramref name="fraction"/>
        /// of the way towards its centre. The new inner faces become the selection.</summary>
        public static IReadOnlySet<int>? InsetFaces(Mesh mesh, IReadOnlySet<int> selection, float fraction = 0.2f)
        {
            fraction = Math.Clamp(fraction, 0.01f, 0.99f);
            var selected = SelectedPolygons(mesh, selection, out var others, out var otherFaces);
            if (selected.Count == 0) return null;

            var newSelection = new HashSet<int>();
            var polygons = new List<Polygon>(others);
            foreach (var poly in selected)
            {
                var idx = poly.Indices;
                var center = Centroid(mesh, idx);
                var centerUv = idx.Aggregate(Vector2.Zero, (s, i) => s + mesh.Vertices[i].UV) / idx.Count;
                var normal = PolygonNormal(mesh, idx);
                var inner = new int[idx.Count];
                for (var i = 0; i < idx.Count; i++)
                {
                    var v = mesh.Vertices[idx[i]];
                    var nv = v.WithPosition(Vector3.Lerp(v.Position, center, fraction)).WithUV(Vector2.Lerp(v.UV, centerUv, fraction)).WithNormal(normal);
                    inner[i] = mesh.AddVertex(nv);
                    newSelection.Add(inner[i]);
                }

                polygons.Add(new Polygon(inner) { MaterialSlotIndex = poly.MaterialSlotIndex });
                for (var i = 0; i < idx.Count; i++)
                {
                    var j = (i + 1) % idx.Count;
                    polygons.Add(new Polygon(idx[i], idx[j], inner[j], inner[i]) { MaterialSlotIndex = poly.MaterialSlotIndex });
                }
            }

            mesh.SetTopology(otherFaces, polygons);
            return newSelection;
        }

        // =====================================================================
        // Extrude region
        // =====================================================================

        /// <summary>E: extrudes the selected faces as one region (walls only on the region's
        /// outer boundary). Distance 0 - the caller moves the new cap afterwards (Blender's
        /// "extrude and move"). The cap's vertices become the selection.</summary>
        public static IReadOnlySet<int>? ExtrudeRegion(Mesh mesh, IReadOnlySet<int> selection, float distance = 0f)
        {
            var selected = SelectedPolygons(mesh, selection, out var others, out var otherFaces);
            if (selected.Count == 0) return null;

            // Edge use count by position, to find the region boundary.
            var edgeUse = new Dictionary<((int, int, int), (int, int, int)), int>();
            (( int, int, int), (int, int, int)) EdgeKey(int a, int b)
            {
                var ka = Key(mesh.Vertices[a].Position);
                var kb = Key(mesh.Vertices[b].Position);
                return Comparer<(int, int, int)>.Default.Compare(ka, kb) <= 0 ? (ka, kb) : (kb, ka);
            }
            foreach (var poly in selected)
                for (var i = 0; i < poly.Indices.Count; i++)
                {
                    var k = EdgeKey(poly.Indices[i], poly.Indices[(i + 1) % poly.Indices.Count]);
                    edgeUse[k] = edgeUse.GetValueOrDefault(k) + 1;
                }

            var regionNormal = Vector3.Zero;
            foreach (var poly in selected) regionNormal += PolygonNormal(mesh, poly.Indices);
            regionNormal = regionNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(regionNormal) : Vector3.UnitY;
            var offset = regionNormal * distance;

            var duplicate = new Dictionary<int, int>();
            int Dup(int i)
            {
                if (duplicate.TryGetValue(i, out var d)) return d;
                var v = mesh.Vertices[i];
                d = mesh.AddVertex(v.WithPosition(v.Position + offset));
                duplicate[i] = d;
                return d;
            }

            var polygons = new List<Polygon>(others);
            var newSelection = new HashSet<int>();
            foreach (var poly in selected)
            {
                var idx = poly.Indices;
                var cap = idx.Select(Dup).ToArray();
                foreach (var c in cap) newSelection.Add(c);
                polygons.Add(new Polygon(cap) { MaterialSlotIndex = poly.MaterialSlotIndex });

                for (var i = 0; i < idx.Count; i++)
                {
                    var a = idx[i];
                    var b = idx[(i + 1) % idx.Count];
                    if (edgeUse[EdgeKey(a, b)] != 1) continue; // interior edge of the region
                    // Walls get their own vertices so they shade flat.
                    var va = mesh.Vertices[a];
                    var vb = mesh.Vertices[b];
                    var w0 = mesh.AddVertex(va);
                    var w1 = mesh.AddVertex(vb);
                    var w2 = mesh.AddVertex(vb.WithPosition(vb.Position + offset));
                    var w3 = mesh.AddVertex(va.WithPosition(va.Position + offset));
                    polygons.Add(new Polygon(w0, w1, w2, w3) { MaterialSlotIndex = poly.MaterialSlotIndex });
                    // The wall's top edge must follow the cap when it is moved.
                    newSelection.Add(w2);
                    newSelection.Add(w3);
                }
            }

            mesh.SetTopology(otherFaces, polygons);
            var remap = mesh.RemoveUnusedVertices();
            return remap is null ? newSelection : newSelection.Where(i => remap[i] >= 0).Select(i => remap[i]).ToHashSet();
        }

        // =====================================================================
        // Fill
        // =====================================================================

        /// <summary>F: makes one face from the selected corners (at least three distinct positions).</summary>
        public static IReadOnlySet<int>? Fill(Mesh mesh, IReadOnlySet<int> selection)
        {
            var corners = selection.Where(i => (uint)i < (uint)mesh.Vertices.Count)
                .GroupBy(i => Key(mesh.Vertices[i].Position)).Select(g => g.Min()).ToList();
            if (corners.Count < 3) return null;

            var points = corners.Select(i => mesh.Vertices[i].Position).ToList();
            var center = points.Aggregate(Vector3.Zero, (s, p) => s + p) / points.Count;

            // Plane normal from the two most spread directions.
            var far = points.OrderByDescending(p => (p - center).LengthSquared()).First();
            var u = far - center;
            if (u.LengthSquared() < 1e-12f) return null;
            u = Vector3.Normalize(u);
            var normal = Vector3.Zero;
            foreach (var p in points)
            {
                var c = Vector3.Cross(u, p - center);
                if (Vector3.Dot(c, normal) < 0) c = -c;
                normal += c;
            }
            if (normal.LengthSquared() < 1e-12f) return null;
            normal = Vector3.Normalize(normal);
            var v = Vector3.Cross(normal, u);

            var ordered = corners.OrderBy(i =>
            {
                var d = mesh.Vertices[i].Position - center;
                return MathF.Atan2(Vector3.Dot(d, v), Vector3.Dot(d, u));
            }).ToList();

            // Face the same way as the neighbouring geometry (average vertex normal).
            var avgNormal = ordered.Aggregate(Vector3.Zero, (s, i) => s + mesh.Vertices[i].Normal);
            if (avgNormal.LengthSquared() > 1e-12f && Vector3.Dot(PolygonNormal(mesh, ordered), avgNormal) < 0) ordered.Reverse();

            mesh.AddPolygon(new Polygon(ordered));
            return selection.ToHashSet();
        }

        // =====================================================================
        // Selection helpers
        // =====================================================================

        /// <summary>L / Ctrl+L: every vertex connected to the selection through faces.</summary>
        public static IReadOnlySet<int> SelectLinked(Mesh mesh, IReadOnlySet<int> seed)
        {
            var keys = new Dictionary<(int, int, int), int>();
            int KeyId(int vertex)
            {
                var k = Key(mesh.Vertices[vertex].Position);
                if (!keys.TryGetValue(k, out var id)) { id = keys.Count; keys[k] = id; }
                return id;
            }

            var vertexKey = new int[mesh.Vertices.Count];
            for (var i = 0; i < vertexKey.Length; i++) vertexKey[i] = KeyId(i);

            var parent = Enumerable.Range(0, keys.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            foreach (var p in mesh.Polygons)
                for (var i = 1; i < p.Indices.Count; i++) Union(vertexKey[p.Indices[0]], vertexKey[p.Indices[i]]);
            foreach (var f in mesh.Faces) { Union(vertexKey[f.A], vertexKey[f.B]); Union(vertexKey[f.A], vertexKey[f.C]); }

            var roots = seed.Where(i => (uint)i < (uint)mesh.Vertices.Count).Select(i => Find(vertexKey[i])).ToHashSet();
            return Enumerable.Range(0, mesh.Vertices.Count).Where(i => roots.Contains(Find(vertexKey[i]))).ToHashSet();
        }

        // =====================================================================
        // Object operators
        // =====================================================================

        /// <summary>Ctrl+A → All Transforms: bakes the node's rotation and scale (and
        /// optionally location) into its mesh, resetting them, while every child keeps its
        /// world transform.</summary>
        public static void ApplyTransform(Node node, bool location = true, bool rotation = true, bool scale = true)
        {
            ArgumentNullException.ThrowIfNull(node);
            var s = scale ? node.LocalScale : Vector3.One;
            var r = rotation ? node.LocalRotation : Quaternion.Identity;
            var t = location ? node.LocalPosition : Vector3.Zero;
            var applied = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
            if (applied.IsIdentity) return;

            var childWorlds = node.Children.Select(c => (c, c.GetWorldTransform())).ToList();

            if (node.Mesh is { } mesh)
            {
                Matrix4x4.Invert(applied, out var inv);
                var normalMatrix = Matrix4x4.Transpose(inv);
                for (var i = 0; i < mesh.Vertices.Count; i++)
                {
                    var v = mesh.Vertices[i];
                    var n = Vector3.TransformNormal(v.Normal, normalMatrix);
                    mesh.SetVertexPosition(i, Vector3.Transform(v.Position, applied));
                    if (n.LengthSquared() > 1e-12f) mesh.SetVertexNormal(i, Vector3.Normalize(n));
                }
                // Mirrored scale flips triangle winding; keep faces pointing outwards.
                if (s.X * s.Y * s.Z < 0) mesh.FlipWinding();
            }

            if (scale) node.LocalScale = Vector3.One;
            if (rotation) node.LocalRotation = Quaternion.Identity;
            if (location) node.LocalPosition = Vector3.Zero;

            if (!Matrix4x4.Invert(node.GetWorldTransform(), out var newParentInverse)) return;
            foreach (var (child, world) in childWorlds)
            {
                var local = world * newParentInverse;
                if (!Matrix4x4.Decompose(local, out var cs, out var cr, out var ct)) continue;
                child.LocalScale = cs;
                child.LocalRotation = Quaternion.Normalize(cr);
                child.LocalPosition = ct;
            }
        }

        /// <summary>Ctrl+J: appends <paramref name="source"/> to <paramref name="target"/>,
        /// moving its vertices into the target's local space so nothing moves in the world.
        /// A different material on the source becomes a material slot on the target.</summary>
        public static void JoinInto(Mesh target, Matrix4x4 targetWorld, Mesh source, Matrix4x4 sourceWorld)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);
            if (!Matrix4x4.Invert(targetWorld, out var targetInverse)) return;
            var toTarget = sourceWorld * targetInverse;
            Matrix4x4.Invert(toTarget, out var inv);
            var normalMatrix = Matrix4x4.Transpose(inv);

            int SlotFor(Materials.Material? material)
            {
                if (material is null || ReferenceEquals(material, target.Material)) return -1;
                for (var i = 0; i < target.MaterialSlots.Count; i++)
                    if (ReferenceEquals(target.MaterialSlots[i], material)) return i;
                if (target.Material is null && target.MaterialSlots.Count == 0 && target.Vertices.Count == 0)
                {
                    target.Material = material;
                    return -1;
                }
                return target.AddMaterialSlot(material);
            }

            var offset = target.Vertices.Count;
            foreach (var v in source.Vertices)
            {
                var n = Vector3.TransformNormal(v.Normal, normalMatrix);
                target.AddVertex(v.WithPosition(Vector3.Transform(v.Position, toTarget))
                    .WithNormal(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : v.Normal));
            }

            var mainSlot = SlotFor(source.Material);
            foreach (var f in source.Faces)
            {
                if (mainSlot < 0) target.AddTriangle(f.A + offset, f.B + offset, f.C + offset);
                else target.AddPolygon(new Polygon(f.A + offset, f.B + offset, f.C + offset) { MaterialSlotIndex = mainSlot });
            }
            foreach (var p in source.Polygons)
            {
                var slot = p.MaterialSlotIndex >= 0 && p.MaterialSlotIndex < source.MaterialSlots.Count
                    ? SlotFor(source.MaterialSlots[p.MaterialSlotIndex])
                    : mainSlot;
                target.AddPolygon(new Polygon(p.Indices.Select(i => i + offset)) { MaterialSlotIndex = slot });
            }
            foreach (var (a, b) in source.SeamEdges) target.MarkSeam(a + offset, b + offset);
            if (toTarget.GetDeterminant() < 0)
            {
                // A mirrored source flips winding; reverse just the appended part.
                var polys = target.Polygons.ToList();
                var faces = target.Faces.ToList();
                for (var i = 0; i < polys.Count; i++)
                    if (polys[i].Indices.All(x => x >= offset)) polys[i] = new Polygon(polys[i].Indices.Reverse()) { MaterialSlotIndex = polys[i].MaterialSlotIndex };
                for (var i = 0; i < faces.Count; i++)
                    if (faces[i].A >= offset) faces[i] = new Face(faces[i].A, faces[i].C, faces[i].B);
                target.SetTopology(faces, polys);
            }
        }

        /// <summary>Set Origin → Origin to Geometry: moves the node's origin to the centre of
        /// its mesh bounds without moving the mesh in the world.</summary>
        public static bool OriginToGeometry(Node node)
        {
            if (node.Mesh is not { Vertices.Count: > 0 } mesh) return false;
            var (min, max) = mesh.GetBounds();
            var center = (min + max) * 0.5f;
            if (center.LengthSquared() < 1e-12f) return false;
            var previous = node.GetLocalTransform();
            node.LocalPosition = Vector3.Transform(center, previous);
            node.CompensateMeshForOriginChange(previous);
            return true;
        }
    }
}
