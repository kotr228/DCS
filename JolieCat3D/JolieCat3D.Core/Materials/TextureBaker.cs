using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Numerics;

namespace JolieCat3D.Core.Materials
{
    /// <summary>
    /// CPU texture baking into a mesh's UV layout (needs non-overlapping UVs - use Unwrap):
    /// ambient occlusion and high-to-low tangent-space normal maps, plus edge padding so
    /// mip-mapping and bilinear filtering don't bleed the background into UV seams.
    /// Pixel (x, y) covers UV ((x + 0.5) / size, (y + 0.5) / size), matching the renderer.
    /// </summary>
    public static class TextureBaker
    {
        /// <summary>Calls <paramref name="texel"/> for every texel centre covered by a
        /// triangle in UV space, with that triangle and the texel's barycentric weights.</summary>
        public static void RasterizeUv(Mesh mesh, int size, Action<int, int, Face, Vector3> texel)
        {
            foreach (var face in mesh.GetRenderFaces())
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
                    texel(x, y, face, new Vector3(w0, w1, w2));
                }
            }

            static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        }

        /// <summary>
        /// Ambient occlusion: for each texel, the fraction of cosine-weighted hemisphere rays
        /// that escape within <paramref name="maxDistance"/>. <paramref name="world"/> places the
        /// mesh among <paramref name="occluders"/> (world-space triangles, usually including the
        /// mesh itself so it shadows its own creases). 1 = fully open (white).
        /// </summary>
        public static TextureBuffer BakeAmbientOcclusion(Mesh mesh, Matrix4x4 world, TriangleBvh occluders, int size,
            int samples = 32, float maxDistance = 1f, IProgress<float>? progress = null, CancellationToken cancel = default)
        {
            var result = new float[size * size];
            var covered = new bool[size * size];
            var normalMatrix = Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : Matrix4x4.Identity;

            var jobs = new List<(int X, int Y, Vector3 P, Vector3 N)>();
            RasterizeUv(mesh, size, (x, y, f, w) =>
            {
                var va = mesh.Vertices[f.A]; var vb = mesh.Vertices[f.B]; var vc = mesh.Vertices[f.C];
                var p = va.Position * w.X + vb.Position * w.Y + vc.Position * w.Z;
                var n = va.Normal * w.X + vb.Normal * w.Y + vc.Normal * w.Z;
                if (n.LengthSquared() < 1e-12f) n = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
                jobs.Add((x, y, Vector3.Transform(p, world), Vector3.Normalize(Vector3.TransformNormal(n, normalMatrix))));
            });

            var done = 0;
            Parallel.For(0, jobs.Count, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                var (x, y, p, n) = jobs[i];
                var (t, b) = Basis(n);
                var bias = MathF.Max(1e-4f, maxDistance * 1e-3f);
                var origin = p + n * bias;
                var open = 0;
                for (var s = 0; s < samples; s++)
                {
                    // Stratified cosine-weighted hemisphere sample (deterministic per texel).
                    var r1 = (s + Hash(x, y, s)) / samples;
                    var r2 = Hash(y, x, s + 7);
                    var r = MathF.Sqrt(r1);
                    var phi = 2 * MathF.PI * r2;
                    var dir = t * (r * MathF.Cos(phi)) + b * (r * MathF.Sin(phi)) + n * MathF.Sqrt(MathF.Max(0, 1 - r1));
                    if (!occluders.Occluded(origin, dir, 0, maxDistance)) open++;
                }
                result[y * size + x] = open / (float)samples;
                covered[y * size + x] = true;
                if ((Interlocked.Increment(ref done) & 1023) == 0) progress?.Report(done / (float)jobs.Count);
            });

            // Light denoise: average with covered neighbours only (never across into empty space).
            Smooth(result, covered, size, 2);
            Dilate(result, covered, size, 4);
            var buffer = new TextureBuffer(size, size, new Color4(1, 1, 1, 1));
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var v = covered[y * size + x] ? result[y * size + x] : 1f;
                buffer.SetPixel(x, y, new Color4(v, v, v, 1));
            }
            progress?.Report(1f);
            return buffer;
        }

        /// <summary>
        /// Tangent-space normal map: for each texel of <paramref name="low"/>, finds the
        /// <paramref name="high"/> surface along the low normal (within ±<paramref name="cageDistance"/>)
        /// and stores the high normal in the low triangle's tangent frame (T = +U, B = +V,
        /// matching the viewport shader), encoded as RGB = n * 0.5 + 0.5. Both meshes share
        /// one space (e.g. a base mesh and the same mesh after Subdivision Surface).
        /// </summary>
        public static TextureBuffer BakeNormalMap(Mesh low, Mesh high, int size, float cageDistance = 0.1f,
            IProgress<float>? progress = null, CancellationToken cancel = default)
        {
            var highFaces = high.GetRenderFaces().ToArray();
            var bvh = new TriangleBvh(highFaces.Select(f => (high.Vertices[f.A].Position, high.Vertices[f.B].Position, high.Vertices[f.C].Position)).ToList());
            var result = new Vector3[size * size];
            var covered = new bool[size * size];

            var jobs = new List<(int X, int Y, Vector3 P, Vector3 N, Vector3 T, Vector3 B)>();
            RasterizeUv(low, size, (x, y, f, w) =>
            {
                var va = low.Vertices[f.A]; var vb = low.Vertices[f.B]; var vc = low.Vertices[f.C];
                var p = va.Position * w.X + vb.Position * w.Y + vc.Position * w.Z;
                var n = va.Normal * w.X + vb.Normal * w.Y + vc.Normal * w.Z;
                var faceNormal = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
                if (n.LengthSquared() < 1e-12f) n = faceNormal;
                n = Vector3.Normalize(n);

                // Tangent frame from the triangle's UV gradients.
                var e1 = vb.Position - va.Position; var e2 = vc.Position - va.Position;
                var d1 = vb.UV - va.UV; var d2 = vc.UV - va.UV;
                var r = d1.X * d2.Y - d2.X * d1.Y;
                Vector3 t, b;
                if (MathF.Abs(r) < 1e-12f) (t, b) = Basis(n);
                else
                {
                    t = (e1 * d2.Y - e2 * d1.Y) / r;
                    b = (e2 * d1.X - e1 * d2.X) / r;
                    t = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                    var bOrtho = b - n * Vector3.Dot(n, b) - t * Vector3.Dot(t, b);
                    b = bOrtho.LengthSquared() > 1e-12f ? Vector3.Normalize(bOrtho) : Vector3.Cross(n, t);
                }
                jobs.Add((x, y, p, n, t, b));
            });

            var done = 0;
            Parallel.For(0, jobs.Count, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                var (x, y, p, n, t, b) = jobs[i];
                var highNormal = n;
                var start = p + n * cageDistance;
                if (bvh.Intersect(start, -n, 0, cageDistance * 2) is { } hit)
                {
                    var f = highFaces[hit.Triangle];
                    var w0 = 1 - hit.U - hit.V;
                    var hn = high.Vertices[f.A].Normal * w0 + high.Vertices[f.B].Normal * hit.U + high.Vertices[f.C].Normal * hit.V;
                    if (hn.LengthSquared() < 1e-12f)
                        hn = Vector3.Cross(high.Vertices[f.B].Position - high.Vertices[f.A].Position, high.Vertices[f.C].Position - high.Vertices[f.A].Position);
                    highNormal = Vector3.Normalize(hn);
                    if (Vector3.Dot(highNormal, n) < 0) highNormal = -highNormal;
                }
                result[y * size + x] = new Vector3(Vector3.Dot(highNormal, t), Vector3.Dot(highNormal, b), Vector3.Dot(highNormal, n));
                covered[y * size + x] = true;
                if ((Interlocked.Increment(ref done) & 1023) == 0) progress?.Report(done / (float)jobs.Count);
            });

            var channels = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                channels[c] = result.Select(v => c == 0 ? v.X : c == 1 ? v.Y : v.Z).ToArray();
                Dilate(channels[c], (bool[])covered.Clone(), size, 4);
            }

            var buffer = new TextureBuffer(size, size, new Color4(0.5f, 0.5f, 1f, 1f));
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = y * size + x;
                var v = new Vector3(channels[0][i], channels[1][i], channels[2][i]);
                if (v.LengthSquared() < 1e-12f) v = Vector3.UnitZ;
                v = Vector3.Normalize(v);
                buffer.SetPixel(x, y, new Color4(v.X * 0.5f + 0.5f, v.Y * 0.5f + 0.5f, v.Z * 0.5f + 0.5f, 1));
            }
            progress?.Report(1f);
            return buffer;
        }

        /// <summary>3x3 box blur over covered texels only.</summary>
        public static void Smooth(float[] values, bool[] covered, int size, int iterations)
        {
            for (var it = 0; it < iterations; it++)
            {
                var next = (float[])values.Clone();
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    if (!covered[i]) continue;
                    float sum = 0; var count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx; var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size || !covered[ny * size + nx]) continue;
                        sum += values[ny * size + nx]; count++;
                    }
                    next[i] = sum / count;
                }
                Array.Copy(next, values, values.Length);
            }
        }

        /// <summary>Grows covered texels into empty neighbours <paramref name="iterations"/> times.</summary>
        public static void Dilate(float[] values, bool[] covered, int size, int iterations)
        {
            for (var it = 0; it < iterations; it++)
            {
                var next = (bool[])covered.Clone();
                var nextValues = (float[])values.Clone();
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    if (covered[i]) continue;
                    float sum = 0; var count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx; var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                        var j = ny * size + nx;
                        if (!covered[j]) continue;
                        sum += values[j]; count++;
                    }
                    if (count == 0) continue;
                    nextValues[i] = sum / count;
                    next[i] = true;
                }
                Array.Copy(next, covered, covered.Length);
                Array.Copy(nextValues, values, values.Length);
            }
        }

        private static (Vector3 T, Vector3 B) Basis(Vector3 n)
        {
            var helper = MathF.Abs(n.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
            var t = Vector3.Normalize(Vector3.Cross(helper, n));
            return (t, Vector3.Cross(n, t));
        }

        private static float Hash(int a, int b, int c)
        {
            unchecked
            {
                var h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
