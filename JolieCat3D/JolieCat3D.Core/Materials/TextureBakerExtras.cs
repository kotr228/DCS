using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// Texturing helper maps for game asset painting (Substance-style masks): Curvature
    /// (convex edges light, cavities dark, flat = 50% grey) and ID (a flat colour per
    /// material slot, or per separate mesh part when there is only one material).
    /// </summary>
    public static class TextureBakerExtras
    {
        /// <summary>Calls <paramref name="texel"/> for every texel centre covered by the
        /// given UV triangles (each tagged with a caller value).</summary>
        public static void RasterizeTriangles(Mesh mesh, IEnumerable<(Face Face, int Tag)> triangles, int size, Action<int, int, Face, int, Vector3> texel)
        {
            foreach (var (face, tag) in triangles)
            {
                var uv0 = mesh.Vertices[face.A].UV * size;
                var uv1 = mesh.Vertices[face.B].UV * size;
                var uv2 = mesh.Vertices[face.C].UV * size;
                var area = Edge(uv0, uv1, uv2);
                if (MathF.Abs(area) < 1e-9f) continue;
                var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(uv0.X, MathF.Min(uv1.X, uv2.X))));
                var maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(uv0.X, MathF.Max(uv1.X, uv2.X))));
                var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(uv0.Y, MathF.Min(uv1.Y, uv2.Y))));
                var maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(uv0.Y, MathF.Max(uv1.Y, uv2.Y))));
                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var w0 = Edge(uv1, uv2, p) / area;
                    var w1 = Edge(uv2, uv0, p) / area;
                    var w2 = 1 - w0 - w1;
                    const float eps = -1e-4f;
                    if (w0 < eps || w1 < eps || w2 < eps) continue;
                    texel(x, y, face, tag, new Vector3(w0, w1, w2));
                }
            }

            static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        }

        private static IEnumerable<(Face, int)> Triangles(Mesh mesh, Func<int, int> polygonTag)
        {
            foreach (var f in mesh.Faces) yield return (f, polygonTag(-1));
            for (var i = 0; i < mesh.Polygons.Count; i++)
                foreach (var t in mesh.Polygons[i].Triangulate()) yield return (t, polygonTag(i));
        }

        /// <summary>
        /// Curvature / edge mask: every texel looks at the mesh edges within
        /// <paramref name="width"/> (world units) and is lightened near convex edges and
        /// darkened near concave ones, weighted by how sharp each edge is (dihedral angle)
        /// and how close it is. Flat areas stay 50% grey. Works for hard-surface low-poly
        /// (crisp edge wear) and smooth meshes alike.
        /// </summary>
        public static TextureBuffer BakeCurvature(Mesh mesh, int size, float width = 0.05f, IProgress<float>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            width = MathF.Max(width, 1e-5f);

            // Faces with their normals and centroids; edges keyed by welded endpoint positions.
            var tris = mesh.GetRenderFaces().ToList();
            var faceNormal = new Vector3[tris.Count];
            var faceCentre = new Vector3[tris.Count];
            var edgeFaces = new Dictionary<((int, int, int), (int, int, int)), List<int>>();
            var edgeEnds = new Dictionary<((int, int, int), (int, int, int)), (Vector3, Vector3)>();
            for (var i = 0; i < tris.Count; i++)
            {
                var f = tris[i];
                var pa = mesh.Vertices[f.A].Position; var pb = mesh.Vertices[f.B].Position; var pc = mesh.Vertices[f.C].Position;
                var n = Vector3.Cross(pb - pa, pc - pa);
                faceNormal[i] = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.Zero;
                faceCentre[i] = (pa + pb + pc) / 3f;
                foreach (var (u, v) in new[] { (pa, pb), (pb, pc), (pc, pa) })
                {
                    var ku = MeshOperators.Key(u); var kv = MeshOperators.Key(v);
                    var key = ku.CompareTo(kv) < 0 ? (ku, kv) : (kv, ku);
                    (edgeFaces.TryGetValue(key, out var l) ? l : edgeFaces[key] = new List<int>()).Add(i);
                    edgeEnds[key] = (u, v);
                }
            }

            // Signed sharpness per edge: + convex, - concave, scaled by the angle (90 deg = 1).
            var segments = new List<(Vector3 A, Vector3 B, float Weight)>();
            foreach (var (key, faces) in edgeFaces)
            {
                if (faces.Count != 2) continue;
                var (f1, f2) = (faces[0], faces[1]);
                var cos = Math.Clamp(Vector3.Dot(faceNormal[f1], faceNormal[f2]), -1f, 1f);
                var angle = MathF.Acos(cos);
                if (angle < 1e-3f) continue;
                var convex = Vector3.Dot(faceNormal[f1], faceCentre[f2] - faceCentre[f1]) < 0;
                var (u, v) = edgeEnds[key];
                segments.Add((u, v, (convex ? 1 : -1) * MathF.Min(angle / (MathF.PI / 2), 1.5f)));
            }

            // Uniform grid over the segments for fast "edges near this point" queries.
            var cell = width;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 q) => ((int)MathF.Floor(q.X / cell), (int)MathF.Floor(q.Y / cell), (int)MathF.Floor(q.Z / cell));
            for (var si = 0; si < segments.Count; si++)
            {
                var (sa, sb, _) = segments[si];
                var lo = Cell(Vector3.Min(sa, sb) - new Vector3(width));
                var hi = Cell(Vector3.Max(sa, sb) + new Vector3(width));
                var cells = (long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1);
                if (cells > 200000) continue; // absurdly long edge vs width: skip rather than stall
                for (var x = lo.Item1; x <= hi.Item1; x++)
                for (var y = lo.Item2; y <= hi.Item2; y++)
                for (var z = lo.Item3; z <= hi.Item3; z++)
                    (grid.TryGetValue((x, y, z), out var l) ? l : grid[(x, y, z)] = new List<int>()).Add(si);
            }

            var values = new float[size * size];
            var covered = new bool[size * size];
            var jobs = new List<(int X, int Y, Vector3 P)>();
            RasterizeTriangles(mesh, Triangles(mesh, _ => 0), size, (x, y, f, _, w) =>
                jobs.Add((x, y, mesh.Vertices[f.A].Position * w.X + mesh.Vertices[f.B].Position * w.Y + mesh.Vertices[f.C].Position * w.Z)));
            Parallel.For(0, jobs.Count, i =>
            {
                var (x, y, q) = jobs[i];
                var sum = 0f;
                if (grid.TryGetValue(Cell(q), out var near))
                    foreach (var si in near)
                    {
                        var (sa, sb, weight) = segments[si];
                        var d = DistanceToSegment(q, sa, sb);
                        if (d >= width) continue;
                        var t = 1 - d / width;
                        sum += weight * t * t;
                    }
                values[y * size + x] = Math.Clamp(0.5f + 0.5f * sum, 0, 1);
                covered[y * size + x] = true;
            });
            TextureBaker.Dilate(values, covered, size, 4);
            var buffer = new TextureBuffer(size, size, new Color4(0.5f, 0.5f, 0.5f, 1));
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var v = covered[y * size + x] ? values[y * size + x] : 0.5f;
                buffer.SetPixel(x, y, new Color4(v, v, v, 1));
            }
            progress?.Report(1f);
            return buffer;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared();
            var t = len2 > 1e-20f ? Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0, 1) : 0;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>ID map: one distinct flat colour per material slot; with a single material,
        /// one per connected mesh part instead. Empty texels stay black.</summary>
        public static TextureBuffer BakeId(Mesh mesh, int size, IProgress<float>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            var slots = mesh.Polygons.Select(p => p.MaterialSlotIndex).Distinct().Count();
            Func<int, int> tag;
            if (slots > 1)
            {
                tag = i => i < 0 ? -1 : mesh.Polygons[i].MaterialSlotIndex;
            }
            else
            {
                // Connected parts via shared positions.
                var parent = Enumerable.Range(0, mesh.Polygons.Count).ToArray();
                int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
                var owner = new Dictionary<(int, int, int), int>();
                for (var i = 0; i < mesh.Polygons.Count; i++)
                    foreach (var v in mesh.Polygons[i].Indices)
                    {
                        var key = MeshOperators.Key(mesh.Vertices[v].Position);
                        if (owner.TryGetValue(key, out var o)) parent[Find(i)] = Find(o);
                        else owner[key] = i;
                    }
                var ids = new Dictionary<int, int>();
                tag = i => i < 0 ? -1 : ids.TryGetValue(Find(i), out var id) ? id : ids[Find(i)] = ids.Count;
            }

            var buffer = new TextureBuffer(size, size, new Color4(0, 0, 0, 1));
            RasterizeTriangles(mesh, Triangles(mesh, tag).ToList(), size, (x, y, _, t, _) => buffer.SetPixel(x, y, IdColor(t)));
            progress?.Report(1f);
            return buffer;
        }

        /// <summary>Well separated, saturated colours (golden-ratio hues).</summary>
        public static Color4 IdColor(int index)
        {
            var h = ((index + 1) * 0.618034f) % 1f;
            var s = index % 2 == 0 ? 0.85f : 0.6f;
            var v = index % 3 == 0 ? 0.95f : 0.8f;
            var i = (int)(h * 6);
            var f = h * 6 - i;
            float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            return (i % 6) switch
            {
                0 => new Color4(v, t, p),
                1 => new Color4(q, v, p),
                2 => new Color4(p, v, t),
                3 => new Color4(p, q, v),
                4 => new Color4(t, p, v),
                _ => new Color4(v, p, q),
            };
        }
    }
}
