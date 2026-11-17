using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Mesh > Symmetrize (Blender): cuts the mesh at the axis plane through the origin,
    /// keeps the source half, and replaces the other half with its mirror image. Faces
    /// crossing the plane are clipped (new vertices interpolate UV/colour/weights) so the
    /// two halves meet in an exact, welded seam.
    /// </summary>
    public static class MeshSymmetry
    {
        /// <param name="axis">0 = X, 1 = Y, 2 = Z.</param>
        /// <param name="positiveToNegative">True keeps the + side and mirrors it onto the - side.</param>
        public static void Symmetrize(Mesh mesh, int axis = 0, bool positiveToNegative = true)
        {
            var sign = positiveToNegative ? 1f : -1f;
            float Side(Vector3 p) => Component(p, axis) * sign;
            const float eps = 1e-5f;

            var vertices = mesh.Vertices.ToList();
            var cutCache = new Dictionary<(int, int), int>();
            var newVertices = new List<Vertex>(vertices);

            int Cut(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cutCache.TryGetValue(key, out var existing)) return existing;
                var (lo, hi) = key; // fixed order so both faces sharing the edge get the same point
                var da = Side(vertices[lo].Position);
                var db = Side(vertices[hi].Position);
                var t = da / (da - db);
                var va = vertices[lo];
                var vb = vertices[hi];
                var p = Vector3.Lerp(va.Position, vb.Position, t);
                p = WithComponent(p, axis, 0f);
                var v = new Vertex(p, Vector3.Normalize(Vector3.Lerp(va.Normal, vb.Normal, t) + new Vector3(1e-9f)), Vector2.Lerp(va.UV, vb.UV, t),
                    new Numerics.Color4(Lerp(va.Color.R, vb.Color.R, t), Lerp(va.Color.G, vb.Color.G, t), Lerp(va.Color.B, vb.Color.B, t), Lerp(va.Color.A, vb.Color.A, t)),
                    t < 0.5f ? va.BoneIndices : vb.BoneIndices, t < 0.5f ? va.BoneWeights : vb.BoneWeights);
                newVertices.Add(v);
                cutCache[key] = newVertices.Count - 1;
                return newVertices.Count - 1;
            }

            // Clip every face to the kept side (Sutherland-Hodgman against one plane).
            var kept = new List<(List<int> Indices, int Slot)>();
            IEnumerable<(IReadOnlyList<int>, int)> Faces()
            {
                foreach (var f in mesh.Faces) yield return (new[] { f.A, f.B, f.C }, -1);
                foreach (var p in mesh.Polygons) yield return (p.Indices, p.MaterialSlotIndex);
            }
            foreach (var (indices, slot) in Faces())
            {
                var output = new List<int>();
                for (var i = 0; i < indices.Count; i++)
                {
                    var a = indices[i];
                    var b = indices[(i + 1) % indices.Count];
                    var da = Side(vertices[a].Position);
                    var db = Side(vertices[b].Position);
                    var aIn = da >= -eps;
                    var bIn = db >= -eps;
                    if (aIn) output.Add(a);
                    if (aIn != bIn && MathF.Abs(da) > eps && MathF.Abs(db) > eps) output.Add(Cut(a, b));
                }
                if (output.Count >= 3) kept.Add((output, slot));
            }

            // Snap near-plane vertices exactly onto the plane so the mirror welds.
            for (var i = 0; i < newVertices.Count; i++)
                if (MathF.Abs(Component(newVertices[i].Position, axis)) <= eps)
                    newVertices[i] = newVertices[i].WithPosition(WithComponent(newVertices[i].Position, axis, 0f));

            // Mirror: plane vertices are shared, others duplicated with flipped coordinate/normal.
            var mirrorOf = new Dictionary<int, int>();
            int Mirror(int i)
            {
                if (mirrorOf.TryGetValue(i, out var m)) return m;
                var v = newVertices[i];
                if (Component(v.Position, axis) == 0f) { mirrorOf[i] = i; return i; }
                var mv = v.WithPosition(WithComponent(v.Position, axis, -Component(v.Position, axis)))
                          .WithNormal(WithComponent(v.Normal, axis, -Component(v.Normal, axis)));
                newVertices.Add(mv);
                mirrorOf[i] = newVertices.Count - 1;
                return newVertices.Count - 1;
            }

            var polygons = new List<Polygon>();
            foreach (var (indices, slot) in kept)
            {
                polygons.Add(new Polygon(indices) { MaterialSlotIndex = slot });
                var mirrored = indices.Select(Mirror).Reverse().ToList(); // reversed: mirroring flips winding
                polygons.Add(new Polygon(mirrored) { MaterialSlotIndex = slot });
            }

            // Rebuild the mesh in place.
            var originalCount = mesh.Vertices.Count;
            for (var i = originalCount; i < newVertices.Count; i++) mesh.AddVertex(newVertices[i]);
            for (var i = 0; i < originalCount; i++) mesh.SetVertexPosition(i, newVertices[i].Position);
            mesh.SetTopology(Array.Empty<Face>(), polygons);
            mesh.RemoveUnusedVertices();
            mesh.ShapeKeys.Clear();
            mesh.ShapeBasis = null;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        private static Vector3 WithComponent(Vector3 v, int axis, float value) =>
            axis == 0 ? new Vector3(value, v.Y, v.Z) : axis == 1 ? new Vector3(v.X, value, v.Z) : new Vector3(v.X, v.Y, value);
    }
}
