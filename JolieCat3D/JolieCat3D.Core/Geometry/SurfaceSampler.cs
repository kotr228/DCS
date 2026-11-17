using System.Numerics;

namespace JolieCat3D.Core.Geometry
{
    /// <summary>
    /// Random points on a mesh surface, uniform by area (Blender's "Distribute Points on
    /// Faces"), optionally kept at least <c>minDistance</c> apart (dart throwing). Used by
    /// Scatter. Deterministic for a given seed.
    /// </summary>
    public static class SurfaceSampler
    {
        public static List<(Vector3 Position, Vector3 Normal)> Sample(Mesh mesh, Matrix4x4 world, int count, int seed = 0, float minDistance = 0f)
        {
            var result = new List<(Vector3, Vector3)>();
            if (count <= 0) return result;

            var tris = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 N)>();
            var cumulative = new List<double>();
            var total = 0.0;
            foreach (var f in mesh.GetRenderFaces())
            {
                var a = Vector3.Transform(mesh.Vertices[f.A].Position, world);
                var b = Vector3.Transform(mesh.Vertices[f.B].Position, world);
                var c = Vector3.Transform(mesh.Vertices[f.C].Position, world);
                var cross = Vector3.Cross(b - a, c - a);
                var area = cross.Length() * 0.5;
                if (area < 1e-12) continue;
                total += area;
                tris.Add((a, b, c, Vector3.Normalize(cross)));
                cumulative.Add(total);
            }
            if (tris.Count == 0) return result;

            var rng = new Random(seed);
            var minSq = minDistance * minDistance;
            var grid = new Dictionary<(int, int, int), List<Vector3>>();
            (int, int, int) Cell(Vector3 p) => minDistance > 0
                ? ((int)MathF.Floor(p.X / minDistance), (int)MathF.Floor(p.Y / minDistance), (int)MathF.Floor(p.Z / minDistance))
                : (0, 0, 0);

            var attempts = 0;
            var maxAttempts = count * (minDistance > 0 ? 30 : 1);
            while (result.Count < count && attempts++ < maxAttempts)
            {
                var pick = rng.NextDouble() * total;
                var index = cumulative.BinarySearch(pick);
                if (index < 0) index = ~index;
                index = Math.Min(index, tris.Count - 1);
                var (a, b, c, n) = tris[index];
                var r1 = MathF.Sqrt(rng.NextSingle());
                var r2 = rng.NextSingle();
                var p = a * (1 - r1) + b * (r1 * (1 - r2)) + c * (r1 * r2);

                if (minDistance > 0)
                {
                    var (cx, cy, cz) = Cell(p);
                    var tooClose = false;
                    for (var dx = -1; dx <= 1 && !tooClose; dx++)
                    for (var dy = -1; dy <= 1 && !tooClose; dy++)
                    for (var dz = -1; dz <= 1 && !tooClose; dz++)
                        if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var near))
                            tooClose = near.Any(q => Vector3.DistanceSquared(p, q) < minSq);
                    if (tooClose) continue;
                    var key = (cx, cy, cz);
                    (grid.TryGetValue(key, out var list) ? list : grid[key] = new List<Vector3>()).Add(p);
                }
                result.Add((p, n));
            }
            return result;
        }
    }
}
