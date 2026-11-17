using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;

namespace JolieCat3D.Tests
{
    public sealed class BakingTests
    {
        [Fact]
        public void BvhMatchesBruteForce()
        {
            var random = new Random(7);
            Vector3 R() => new((float)random.NextDouble() * 10 - 5, (float)random.NextDouble() * 10 - 5, (float)random.NextDouble() * 10 - 5);
            var tris = Enumerable.Range(0, 400).Select(_ => { var c = R(); return (c, c + R() * 0.2f, c + R() * 0.2f); }).ToList();
            var bvh = new TriangleBvh(tris);

            for (var i = 0; i < 200; i++)
            {
                var origin = R() * 2;
                var dir = Vector3.Normalize(R());
                float? brute = null;
                foreach (var (a, b, c) in tris)
                    if (RayIntersection.IntersectTriangle(new Ray(origin, dir), a, b, c) is { } t && t > 1e-5f && (brute is null || t < brute)) brute = t;
                var hit = bvh.Intersect(origin, dir);
                Assert.Equal(brute.HasValue, hit.HasValue);
                if (brute is { } bt) Assert.Equal(bt, hit!.Value.T, 3);
            }
        }

        [Fact]
        public void OpenPlaneBakesWhiteAndCoveredPartDarker()
        {
            var plane = Primitives.CreatePlane(2, 2);
            var size = 32;

            var open = TextureBaker.BakeAmbientOcclusion(plane, Matrix4x4.Identity, new TriangleBvh(Array.Empty<(Vector3, Vector3, Vector3)>()), size, 16, 1f);
            Assert.All(open.Pixels, p => Assert.Equal(1f, p.R, 3));

            // A roof 0.1 above the +X half of the plane.
            var roof = new List<(Vector3, Vector3, Vector3)>
            {
                (new Vector3(0, 0.1f, -1), new Vector3(1, 0.1f, -1), new Vector3(1, 0.1f, 1)),
                (new Vector3(0, 0.1f, -1), new Vector3(1, 0.1f, 1), new Vector3(0, 0.1f, 1)),
            };
            var ao = TextureBaker.BakeAmbientOcclusion(plane, Matrix4x4.Identity, new TriangleBvh(roof), size, 32, 1f);

            float Sample(float worldX)
            {
                // Find the texel whose UV maps closest to this world X on the plane.
                var best = 0f; var bestDist = float.MaxValue;
                TextureBaker.RasterizeUv(plane, size, (x, y, f, w) =>
                {
                    var p = plane.Vertices[f.A].Position * w.X + plane.Vertices[f.B].Position * w.Y + plane.Vertices[f.C].Position * w.Z;
                    var d = MathF.Abs(p.X - worldX) + MathF.Abs(p.Z);
                    if (d < bestDist) { bestDist = d; best = ao.GetPixel(x, y).R; }
                });
                return best;
            }

            Assert.True(Sample(0.5f) < 0.3f, $"under the roof: {Sample(0.5f)}");
            Assert.True(Sample(-0.7f) > 0.8f, $"open side: {Sample(-0.7f)}");
        }

        [Fact]
        public void FlatHighMeshBakesTheNeutralNormalColour()
        {
            var low = Primitives.CreatePlane(2, 2);
            var map = TextureBaker.BakeNormalMap(low, low.Clone(), 16, 0.1f);
            Assert.All(map.Pixels, p =>
            {
                Assert.Equal(0.5f, p.R, 2);
                Assert.Equal(0.5f, p.G, 2);
                Assert.Equal(1f, p.B, 2);
            });
        }

        [Fact]
        public void TiltedHighMeshBakesTiltedNormals()
        {
            var low = Primitives.CreatePlane(2, 2);
            var high = low.Clone();
            // Tilt the high surface: height rises along +X, so its normal leans towards -X.
            for (var i = 0; i < high.Vertices.Count; i++)
            {
                var p = high.Vertices[i].Position;
                high.SetVertexPosition(i, new Vector3(p.X, p.X * 0.2f, p.Z));
            }
            high.RecalculateNormals();

            var map = TextureBaker.BakeNormalMap(low, high, 16, 0.2f);
            var center = map.GetPixel(8, 8);
            Assert.True(MathF.Abs(center.R - 0.5f) + MathF.Abs(center.G - 0.5f) > 0.015f, $"{center.R}, {center.G}");
            Assert.True(center.B < 0.995f, $"B = {center.B}");
        }
    }
}
