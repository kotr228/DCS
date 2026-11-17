using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Turns flat outlines (e.g. text glyphs) into a solid mesh: filled front and back caps
    /// (see <see cref="PolygonTriangulator"/>) at z = +depth/2 and -depth/2, joined by side
    /// walls with hard edges. Depth 0 gives a single flat, double-faced-free front cap.
    /// Caps get planar UVs over the shape bounds; walls run U along the outline, V across.
    /// </summary>
    public static class ShapeExtruder
    {
        public static Mesh Extrude(IReadOnlyList<IReadOnlyList<Vector2>> contours, float depth, string name = "Shape")
        {
            var mesh = new Mesh(name);
            var triangles = PolygonTriangulator.Triangulate(contours);
            if (triangles.Count == 0) return mesh;

            var all = contours.SelectMany(c => c).ToList();
            var min = all.Aggregate(Vector2.Min);
            var size = Vector2.Max(all.Aggregate(Vector2.Max) - min, new Vector2(1e-6f));
            var scale = MathF.Max(size.X, size.Y);
            Vector2 CapUv(Vector2 p) => new((p.X - min.X) / scale, 1f - (p.Y - min.Y) / scale); // V down
            var half = MathF.Max(0f, depth) * 0.5f;

            void Cap(float z, bool front)
            {
                var normal = front ? Vector3.UnitZ : -Vector3.UnitZ;
                var index = new Dictionary<Vector2, int>();
                int V(Vector2 p)
                {
                    if (!index.TryGetValue(p, out var i)) index[p] = i = mesh.AddVertex(new Vertex(new Vector3(p, z), normal, CapUv(p)));
                    return i;
                }
                foreach (var (a, b, c) in triangles)
                {
                    if (front) mesh.AddTriangle(V(a), V(b), V(c));
                    else mesh.AddTriangle(V(a), V(c), V(b));
                }
            }

            Cap(half, front: true);
            if (half <= 0f) return mesh;
            Cap(-half, front: false);

            bool Filled(Vector2 p) => contours.Count(c => c.Count >= 3 && PolygonTriangulator.Contains(c, p)) % 2 == 1;

            // Side walls: outward normal regardless of each contour's winding.
            foreach (var raw in contours)
            {
                var contour = raw.ToList();
                if (contour.Count < 3) continue;
                var perimeter = 0f;
                for (var i = 0; i < contour.Count; i++)
                {
                    var a = contour[i];
                    var b = contour[(i + 1) % contour.Count];
                    var edge = b - a;
                    var length = edge.Length();
                    if (length < 1e-9f) continue;
                    // Outward = the side of the edge that is NOT filled (even-odd over all contours).
                    var outward2 = new Vector2(edge.Y, -edge.X) / length;
                    var probe = (a + b) * 0.5f + outward2 * MathF.Max(1e-5f, length * 1e-3f);
                    if (Filled(probe)) outward2 = -outward2;
                    var n = Vector3.Normalize(new Vector3(outward2, 0));
                    var u0 = perimeter / scale;
                    var u1 = (perimeter + length) / scale;
                    perimeter += length;
                    var v0 = mesh.AddVertex(new Vertex(new Vector3(a, half), n, new Vector2(u0, 0)));
                    var v1 = mesh.AddVertex(new Vertex(new Vector3(b, half), n, new Vector2(u1, 0)));
                    var v2 = mesh.AddVertex(new Vertex(new Vector3(b, -half), n, new Vector2(u1, depth / scale)));
                    var v3 = mesh.AddVertex(new Vertex(new Vector3(a, -half), n, new Vector2(u0, depth / scale)));
                    // Wind so the face normal matches n.
                    var faceN = Vector3.Cross(mesh.Vertices[v1].Position - mesh.Vertices[v0].Position, mesh.Vertices[v2].Position - mesh.Vertices[v0].Position);
                    if (Vector3.Dot(faceN, n) >= 0) mesh.AddPolygon(new Polygon(new[] { v0, v1, v2, v3 }));
                    else mesh.AddPolygon(new Polygon(new[] { v0, v3, v2, v1 }));
                }
            }
            return mesh;
        }
    }
}
