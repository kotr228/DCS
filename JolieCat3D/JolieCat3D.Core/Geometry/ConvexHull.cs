using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// 3D convex hull (incremental, horizon-based), mainly for game physics colliders.
    /// <see cref="Build"/> can first reduce the input to its extreme points along evenly
    /// spread directions, which bounds the hull's vertex count (physics engines usually
    /// want at most ~255; 32-64 is typical) while staying a true convex polytope that
    /// encloses most of the shape.
    /// </summary>
    public static class ConvexHull
    {
        /// <summary>
        /// Builds the hull of <paramref name="points"/> as a flat-shaded triangle mesh,
        /// or null when the points are coplanar/degenerate. When
        /// <paramref name="maxVertices"/> is &gt; 0 the input is first reduced to its
        /// support points along that many directions.
        /// </summary>
        public static Mesh? Build(IEnumerable<Vector3> points, int maxVertices = 0, string name = "Collider")
        {
            var input = Dedupe(points);
            if (maxVertices > 0 && input.Count > maxVertices) input = SupportPoints(input, maxVertices);
            var faces = Compute(input);
            if (faces is null) return null;

            var mesh = new Mesh(name);
            foreach (var (a, b, c) in faces)
            {
                var n = Vector3.Normalize(Vector3.Cross(input[b] - input[a], input[c] - input[a]));
                var ia = mesh.AddVertex(new Vertex(input[a], n, Vector2.Zero));
                var ib = mesh.AddVertex(new Vertex(input[b], n, Vector2.Zero));
                var ic = mesh.AddVertex(new Vertex(input[c], n, Vector2.Zero));
                mesh.AddTriangle(ia, ib, ic);
            }
            return mesh;
        }

        private static List<Vector3> Dedupe(IEnumerable<Vector3> points)
        {
            var seen = new HashSet<(int, int, int)>();
            var list = new List<Vector3>();
            foreach (var p in points)
                if (seen.Add(MeshOperators.Key(p))) list.Add(p);
            return list;
        }

        /// <summary>The farthest point along each of <paramref name="count"/> Fibonacci-sphere directions.</summary>
        private static List<Vector3> SupportPoints(List<Vector3> points, int count)
        {
            var chosen = new HashSet<int>();
            var golden = MathF.PI * (3f - MathF.Sqrt(5f));
            for (var i = 0; i < count; i++)
            {
                var y = 1f - 2f * (i + 0.5f) / count;
                var r = MathF.Sqrt(1f - y * y);
                var dir = new Vector3(MathF.Cos(golden * i) * r, y, MathF.Sin(golden * i) * r);
                var best = 0;
                var bestDot = float.MinValue;
                for (var p = 0; p < points.Count; p++)
                {
                    var d = Vector3.Dot(points[p], dir);
                    if (d > bestDot) { bestDot = d; best = p; }
                }
                chosen.Add(best);
            }
            return chosen.Select(i => points[i]).ToList();
        }

        /// <summary>Outward-oriented triangles over indices of <paramref name="p"/>.</summary>
        private static List<(int A, int B, int C)>? Compute(List<Vector3> p)
        {
            if (p.Count < 4) return null;
            var extent = p.Aggregate(Vector3.Max) - p.Aggregate(Vector3.Min);
            var eps = 1e-6f * MathF.Max(1e-3f, MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)));

            // Initial tetrahedron.
            int i0 = 0, i1 = 0;
            for (var i = 1; i < p.Count; i++)
            {
                if (p[i].X < p[i0].X) i0 = i;
                if (p[i].X > p[i1].X) i1 = i;
            }
            if (i0 == i1)
            {
                // All share X: use the farthest pair from point 0 instead.
                i1 = Enumerable.Range(0, p.Count).OrderByDescending(i => Vector3.DistanceSquared(p[i], p[i0])).First();
                if (i1 == i0) return null;
            }
            var lineDir = Vector3.Normalize(p[i1] - p[i0]);
            var i2 = -1;
            var best = eps;
            for (var i = 0; i < p.Count; i++)
            {
                var d = Vector3.Cross(p[i] - p[i0], lineDir).Length();
                if (d > best) { best = d; i2 = i; }
            }
            if (i2 < 0) return null;
            var planeN = Vector3.Normalize(Vector3.Cross(p[i1] - p[i0], p[i2] - p[i0]));
            var i3 = -1;
            best = eps;
            for (var i = 0; i < p.Count; i++)
            {
                var d = MathF.Abs(Vector3.Dot(p[i] - p[i0], planeN));
                if (d > best) { best = d; i3 = i; }
            }
            if (i3 < 0) return null;

            var faces = new List<(int A, int B, int C)>();
            var inside = (p[i0] + p[i1] + p[i2] + p[i3]) * 0.25f;
            void AddOriented(int a, int b, int c)
            {
                var n = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
                if (Vector3.Dot(n, p[a] - inside) < 0) (b, c) = (c, b);
                faces.Add((a, b, c));
            }
            AddOriented(i0, i1, i2);
            AddOriented(i0, i1, i3);
            AddOriented(i0, i2, i3);
            AddOriented(i1, i2, i3);

            var used = new HashSet<int> { i0, i1, i2, i3 };
            // Farthest-first order keeps intermediate hulls large, so most points are skipped early.
            var order = Enumerable.Range(0, p.Count).Where(i => !used.Contains(i))
                .OrderByDescending(i => Vector3.DistanceSquared(p[i], inside)).ToList();

            foreach (var pi in order)
            {
                var point = p[pi];
                var visible = new List<int>();
                for (var f = 0; f < faces.Count; f++)
                {
                    var (a, b, c) = faces[f];
                    var n = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
                    var len = n.Length();
                    if (len < 1e-20f) continue;
                    if (Vector3.Dot(n / len, point - p[a]) > eps) visible.Add(f);
                }
                if (visible.Count == 0) continue;

                var directed = new HashSet<(int, int)>();
                foreach (var f in visible)
                {
                    var (a, b, c) = faces[f];
                    directed.Add((a, b)); directed.Add((b, c)); directed.Add((c, a));
                }
                var horizon = directed.Where(e => !directed.Contains((e.Item2, e.Item1))).ToList();

                var visibleSet = visible.ToHashSet();
                var kept = new List<(int, int, int)>(faces.Count);
                for (var f = 0; f < faces.Count; f++) if (!visibleSet.Contains(f)) kept.Add(faces[f]);
                faces = kept;
                foreach (var (u, v) in horizon) faces.Add((u, v, pi));
            }
            return faces;
        }
    }
}
