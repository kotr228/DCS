using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Bounding-volume hierarchy over triangles for fast ray queries (baking, picking).
    /// Built once (median split on the longest axis), then read-only and thread-safe.
    /// </summary>
    public sealed class TriangleBvh
    {
        private readonly Vector3[] _a, _b, _c;
        private readonly int[] _order;
        private readonly List<Node> _nodes = new();

        private struct Node
        {
            public Vector3 Min, Max;
            public int Left, Right;   // child node indices (-1 for leaves)
            public int Start, Count;  // triangle range in _order for leaves
        }

        public int TriangleCount => _a.Length;

        public TriangleBvh(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> triangles)
        {
            var n = triangles.Count;
            _a = new Vector3[n]; _b = new Vector3[n]; _c = new Vector3[n];
            var centers = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                (_a[i], _b[i], _c[i]) = triangles[i];
                centers[i] = (_a[i] + _b[i] + _c[i]) / 3f;
            }
            _order = Enumerable.Range(0, n).ToArray();
            if (n > 0) Build(0, n, centers);
        }

        private int Build(int start, int count, Vector3[] centers)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = start; i < start + count; i++)
            {
                var t = _order[i];
                min = Vector3.Min(min, Vector3.Min(_a[t], Vector3.Min(_b[t], _c[t])));
                max = Vector3.Max(max, Vector3.Max(_a[t], Vector3.Max(_b[t], _c[t])));
            }

            var index = _nodes.Count;
            _nodes.Add(new Node { Min = min, Max = max, Left = -1, Right = -1, Start = start, Count = count });
            if (count <= 4) return index;

            var size = max - min;
            var axis = size.X > size.Y && size.X > size.Z ? 0 : size.Y > size.Z ? 1 : 2;
            Array.Sort(_order, start, count, Comparer<int>.Create((x, y) => Axis(centers[x], axis).CompareTo(Axis(centers[y], axis))));
            var half = count / 2;
            var left = Build(start, half, centers);
            var right = Build(start + half, count - half, centers);
            var node = _nodes[index];
            node.Left = left;
            node.Right = right;
            node.Count = 0;
            _nodes[index] = node;
            return index;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        /// <summary>Nearest hit within (minT, maxT): distance, triangle index, barycentric (u, v).</summary>
        public (float T, int Triangle, float U, float V)? Intersect(Vector3 origin, Vector3 direction, float minT = 1e-5f, float maxT = float.MaxValue)
        {
            if (_nodes.Count == 0) return null;
            var inv = SafeInverse(direction);
            (float, int, float, float)? best = null;
            var bestT = maxT;
            Span<int> stack = stackalloc int[64];
            var sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                var node = _nodes[stack[--sp]];
                if (!HitBox(origin, inv, node.Min, node.Max, bestT)) continue;
                if (node.Left < 0)
                {
                    for (var i = node.Start; i < node.Start + node.Count; i++)
                    {
                        var t = _order[i];
                        if (HitTriangle(origin, direction, _a[t], _b[t], _c[t], out var dist, out var u, out var v) && dist > minT && dist < bestT)
                        {
                            bestT = dist;
                            best = (dist, t, u, v);
                        }
                    }
                }
                else if (sp < 62)
                {
                    stack[sp++] = node.Left;
                    stack[sp++] = node.Right;
                }
            }
            return best;
        }

        /// <summary>True if anything is hit within (minT, maxT) - cheaper than the nearest hit.</summary>
        public bool Occluded(Vector3 origin, Vector3 direction, float minT, float maxT)
        {
            if (_nodes.Count == 0) return false;
            var inv = SafeInverse(direction);
            Span<int> stack = stackalloc int[64];
            var sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                var node = _nodes[stack[--sp]];
                if (!HitBox(origin, inv, node.Min, node.Max, maxT)) continue;
                if (node.Left < 0)
                {
                    for (var i = node.Start; i < node.Start + node.Count; i++)
                    {
                        var t = _order[i];
                        if (HitTriangle(origin, direction, _a[t], _b[t], _c[t], out var dist, out _, out _) && dist > minT && dist < maxT) return true;
                    }
                }
                else if (sp < 62)
                {
                    stack[sp++] = node.Left;
                    stack[sp++] = node.Right;
                }
            }
            return false;
        }

        /// <summary>The closest point on any triangle to <paramref name="p"/> (and that triangle), or null if empty.</summary>
        public (Vector3 Point, int Triangle)? ClosestPoint(Vector3 p)
        {
            if (_nodes.Count == 0) return null;
            var bestSq = float.MaxValue;
            var bestPoint = Vector3.Zero;
            var bestTri = -1;
            Span<int> stack = stackalloc int[64];
            var sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                var node = _nodes[stack[--sp]];
                var clamped = Vector3.Clamp(p, node.Min, node.Max);
                if (Vector3.DistanceSquared(clamped, p) >= bestSq) continue;
                if (node.Left < 0)
                {
                    for (var i = node.Start; i < node.Start + node.Count; i++)
                    {
                        var t = _order[i];
                        var q = ClosestOnTriangle(p, _a[t], _b[t], _c[t]);
                        var d = Vector3.DistanceSquared(p, q);
                        if (d < bestSq) { bestSq = d; bestPoint = q; bestTri = t; }
                    }
                }
                else if (sp < 62)
                {
                    // Visit the nearer child first (pushed last).
                    var l = _nodes[node.Left];
                    var r = _nodes[node.Right];
                    var dl = Vector3.DistanceSquared(Vector3.Clamp(p, l.Min, l.Max), p);
                    var dr = Vector3.DistanceSquared(Vector3.Clamp(p, r.Min, r.Max), p);
                    if (dl < dr) { stack[sp++] = node.Right; stack[sp++] = node.Left; }
                    else { stack[sp++] = node.Left; stack[sp++] = node.Right; }
                }
            }
            return bestTri < 0 ? null : (bestPoint, bestTri);
        }

        /// <summary>Normal (unnormalised winding order) of triangle <paramref name="index"/>.</summary>
        public Vector3 TriangleNormal(int index) => Vector3.Normalize(Vector3.Cross(_b[index] - _a[index], _c[index] - _a[index]));

        /// <summary>Closest point on triangle (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            var d1 = Vector3.Dot(ab, ap); var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b;
            var d3 = Vector3.Dot(ab, bp); var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            var d5 = Vector3.Dot(ab, cp); var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            var va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            var denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>1/d per axis, with zero components replaced by a huge finite value
        /// (avoids 0 * infinity = NaN in the slab test for axis-parallel rays).</summary>
        private static Vector3 SafeInverse(Vector3 d)
        {
            static float Inv(float x) => MathF.Abs(x) < 1e-20f ? (x < 0 ? -1e20f : 1e20f) : 1f / x;
            return new Vector3(Inv(d.X), Inv(d.Y), Inv(d.Z));
        }

        private static bool HitBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max, float maxT)
        {
            var t1 = (min - o) * inv;
            var t2 = (max - o) * inv;
            var tmin = Vector3.Min(t1, t2);
            var tmax = Vector3.Max(t1, t2);
            var enter = MathF.Max(MathF.Max(tmin.X, tmin.Y), tmin.Z);
            var exit = MathF.Min(MathF.Min(tmax.X, tmax.Y), tmax.Z);
            return exit >= MathF.Max(enter, 0) && enter <= maxT;
        }

        /// <summary>Möller–Trumbore, two-sided.</summary>
        private static bool HitTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
        {
            t = u = v = 0;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-12f) return false;
            var invDet = 1f / det;
            var s = o - a;
            u = Vector3.Dot(s, p) * invDet;
            const float edgeTolerance = 1e-6f; // rays exactly through shared edges/vertices must not leak
            if (u < -edgeTolerance || u > 1 + edgeTolerance) return false;
            var q = Vector3.Cross(s, e1);
            v = Vector3.Dot(d, q) * invDet;
            if (v < -edgeTolerance || u + v > 1 + edgeTolerance) return false;
            t = Vector3.Dot(e2, q) * invDet;
            return true;
        }
    }
}
