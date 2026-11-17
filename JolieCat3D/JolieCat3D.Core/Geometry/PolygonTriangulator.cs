using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Triangulates flat shapes given as closed contours (any orientation): contours are
    /// classified by nesting (even depth = solid, odd = hole of its parent), each hole is
    /// bridged into its outer contour (Eberly's method) and the result is ear-clipped.
    /// Used for 3D text and other extruded outlines.
    /// </summary>
    public static class PolygonTriangulator
    {
        /// <summary>Triangles (counter-clockwise, as point triples) covering the filled area.</summary>
        public static List<(Vector2 A, Vector2 B, Vector2 C)> Triangulate(IReadOnlyList<IReadOnlyList<Vector2>> contours)
        {
            var clean = contours.Select(Clean).Where(c => c.Count >= 3 && MathF.Abs(SignedArea(c)) > 1e-9f).ToList();
            var result = new List<(Vector2, Vector2, Vector2)>();

            // Nesting depth and direct parent of each contour.
            var depth = new int[clean.Count];
            var parent = Enumerable.Repeat(-1, clean.Count).ToArray();
            for (var i = 0; i < clean.Count; i++)
            {
                var probe = clean[i][0];
                var bestArea = float.MaxValue;
                for (var j = 0; j < clean.Count; j++)
                {
                    if (i == j || !Contains(clean[j], probe)) continue;
                    depth[i]++;
                    var area = MathF.Abs(SignedArea(clean[j]));
                    if (area < bestArea) { bestArea = area; parent[i] = j; }
                }
            }

            for (var i = 0; i < clean.Count; i++)
            {
                if (depth[i] % 2 != 0) continue;
                var outer = Oriented(clean[i], ccw: true);
                var holes = Enumerable.Range(0, clean.Count)
                    .Where(h => parent[h] == i && depth[h] % 2 == 1)
                    .Select(h => Oriented(clean[h], ccw: false))
                    .OrderByDescending(h => h.Max(p => p.X))
                    .ToList();
                foreach (var hole in holes) outer = Bridge(outer, hole);
                EarClip(outer, result);
            }
            return result;
        }

        public static float SignedArea(IReadOnlyList<Vector2> c)
        {
            var area = 0f;
            for (var i = 0; i < c.Count; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % c.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area * 0.5f;
        }

        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static List<Vector2> Clean(IReadOnlyList<Vector2> contour)
        {
            var list = new List<Vector2>();
            foreach (var p in contour)
                if (list.Count == 0 || Vector2.DistanceSquared(list[^1], p) > 1e-12f) list.Add(p);
            if (list.Count > 1 && Vector2.DistanceSquared(list[0], list[^1]) <= 1e-12f) list.RemoveAt(list.Count - 1);
            // Drop collinear points.
            for (var i = 0; i < list.Count && list.Count > 3;)
            {
                var a = list[(i + list.Count - 1) % list.Count];
                var b = list[i];
                var c = list[(i + 1) % list.Count];
                if (MathF.Abs(Cross(b - a, c - b)) < 1e-10f) list.RemoveAt(i);
                else i++;
            }
            return list;
        }

        private static List<Vector2> Oriented(List<Vector2> c, bool ccw)
        {
            var copy = c.ToList();
            if (SignedArea(copy) > 0 != ccw) copy.Reverse();
            return copy;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Joins a clockwise hole into a counter-clockwise outer contour with a two-way bridge edge.</summary>
        private static List<Vector2> Bridge(List<Vector2> outer, List<Vector2> hole)
        {
            var mi = 0;
            for (var i = 1; i < hole.Count; i++) if (hole[i].X > hole[mi].X) mi = i;
            var m = hole[mi];

            // Closest edge hit by the ray from M towards +X.
            var bestX = float.MaxValue;
            var pi = -1;
            for (var i = 0; i < outer.Count; i++)
            {
                var a = outer[i];
                var b = outer[(i + 1) % outer.Count];
                if ((a.Y > m.Y) == (b.Y > m.Y)) continue;
                var x = a.X + (m.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (x < m.X - 1e-7f || x >= bestX) continue;
                bestX = x;
                pi = a.X > b.X ? i : (i + 1) % outer.Count;
            }
            if (pi < 0)
            {
                // Fallback: nearest outer vertex.
                pi = Enumerable.Range(0, outer.Count).OrderBy(i => Vector2.DistanceSquared(outer[i], m)).First();
            }
            else
            {
                // A reflex vertex inside triangle (M, I, P) blocks the view: take the one closest in angle.
                var intersection = new Vector2(bestX, m.Y);
                var p = outer[pi];
                var bestAngle = float.MaxValue;
                for (var i = 0; i < outer.Count; i++)
                {
                    var v = outer[i];
                    if (i == pi || v == p) continue;
                    var prev = outer[(i + outer.Count - 1) % outer.Count];
                    var next = outer[(i + 1) % outer.Count];
                    var reflex = Cross(v - prev, next - v) <= 0;
                    if (!reflex || !InTriangle(v, m, intersection, p)) continue;
                    var d = v - m;
                    var angle = MathF.Abs(MathF.Atan2(d.Y, d.X));
                    if (angle < bestAngle - 1e-6f || (MathF.Abs(angle - bestAngle) <= 1e-6f && d.LengthSquared() < (outer[pi] - m).LengthSquared()))
                    {
                        bestAngle = angle;
                        pi = i;
                    }
                }
            }

            var merged = new List<Vector2>(outer.Count + hole.Count + 2);
            for (var i = 0; i <= pi; i++) merged.Add(outer[i]);
            for (var k = 0; k <= hole.Count; k++) merged.Add(hole[(mi + k) % hole.Count]);
            merged.Add(outer[pi]);
            for (var i = pi + 1; i < outer.Count; i++) merged.Add(outer[i]);
            return merged;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);
            var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static void EarClip(List<Vector2> polygon, List<(Vector2, Vector2, Vector2)> output)
        {
            var indices = Enumerable.Range(0, polygon.Count).ToList();
            var guard = 0;
            while (indices.Count > 3 && guard++ < polygon.Count * polygon.Count + 10)
            {
                var clipped = false;
                for (var k = 0; k < indices.Count; k++)
                {
                    var a = polygon[indices[(k + indices.Count - 1) % indices.Count]];
                    var b = polygon[indices[k]];
                    var c = polygon[indices[(k + 1) % indices.Count]];
                    if (Cross(b - a, c - b) <= 1e-12f) continue; // reflex or flat
                    var blocked = false;
                    foreach (var other in indices)
                    {
                        var p = polygon[other];
                        if (p == a || p == b || p == c) continue; // bridge duplicates
                        if (InTriangle(p, a, b, c)) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    output.Add((a, b, c));
                    indices.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Degenerate leftovers (numerical issues): remove the flattest vertex.
                    var worst = Enumerable.Range(0, indices.Count).OrderBy(k =>
                    {
                        var a = polygon[indices[(k + indices.Count - 1) % indices.Count]];
                        var b = polygon[indices[k]];
                        var c = polygon[indices[(k + 1) % indices.Count]];
                        return MathF.Abs(Cross(b - a, c - b));
                    }).First();
                    indices.RemoveAt(worst);
                }
            }
            if (indices.Count == 3)
            {
                var a = polygon[indices[0]];
                var b = polygon[indices[1]];
                var c = polygon[indices[2]];
                if (Cross(b - a, c - b) > 1e-12f) output.Add((a, b, c));
            }
        }
    }
}
