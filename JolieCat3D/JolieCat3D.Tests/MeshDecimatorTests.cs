using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;

namespace JolieCat3D.Tests
{
    public sealed class MeshDecimatorTests
    {
        private static int Triangles(Mesh mesh) => mesh.GetRenderFaces().Count();

        private static void AssertClosedManifold(Mesh mesh)
        {
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            foreach (var f in mesh.GetRenderFaces())
                foreach (var (x, y) in new[] { (f.A, f.B), (f.B, f.C), (f.C, f.A) })
                {
                    var a = MeshOperators.Key(mesh.Vertices[x].Position);
                    var b = MeshOperators.Key(mesh.Vertices[y].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            Assert.All(use, kv => Assert.Equal(2, kv.Value));
        }

        [Fact]
        public void SphereKeepsItsShapeAtAQuarterOfTheTriangles()
        {
            var sphere = Primitives.CreateSphere(1f, 24, 32);
            sphere.ShadeSmooth();
            var before = Triangles(sphere);

            var lod = MeshDecimator.Decimate(sphere, 0.25f);

            Assert.InRange(Triangles(lod), before * 0.2, before * 0.3);
            AssertClosedManifold(lod);
            Assert.All(lod.Vertices, v => Assert.InRange(v.Position.Length(), 0.9f, 1.05f));
        }

        [Fact]
        public void FlatGridKeepsItsOutline()
        {
            var plane = Primitives.CreatePlane(4, 2, widthSegments: 12, depthSegments: 6);
            var lod = MeshDecimator.Decimate(plane, 0.1f);

            Assert.True(Triangles(lod) < Triangles(plane) / 2);
            Assert.All(lod.Vertices, v => Assert.InRange(v.Position.Y, -1e-4f, 1e-4f));
            var min = lod.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var max = lod.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            Assert.Equal(-2f, min.X, 3); Assert.Equal(2f, max.X, 3);
            Assert.Equal(-1f, min.Z, 3); Assert.Equal(1f, max.Z, 3);
            // UVs keep following the surface: corners still map to the UV corners.
            foreach (var v in lod.Vertices)
            {
                var expected = new Vector2((v.Position.X + 2) / 4, (v.Position.Z + 1) / 2);
                var direct = Vector2.Distance(v.UV, expected) < 0.02f;
                var flipped = Vector2.Distance(v.UV, new Vector2(expected.X, 1 - expected.Y)) < 0.02f;
                Assert.True(direct || flipped, $"uv {v.UV} at {v.Position}");
            }
        }

        [Fact]
        public void SmoothNormalsMatchAcrossUvSeams()
        {
            var sphere = Primitives.CreateSphere(1f, 16, 24);
            var lod = MeshDecimator.Decimate(sphere, 0.3f);
            foreach (var group in Enumerable.Range(0, lod.Vertices.Count).GroupBy(i => MeshOperators.Key(lod.Vertices[i].Position)))
            {
                var normals = group.Select(i => lod.Vertices[i].Normal).ToList();
                foreach (var n in normals) Assert.True(Vector3.Dot(n, normals[0]) > 0.999f);
            }
        }

        [Fact]
        public void HardEdgesStayHard()
        {
            var cube = Primitives.CreateCube(1f, segmentsPerFace: 4);
            var lod = MeshDecimator.Decimate(cube, 0.3f);
            // Every vertex normal stays one of the six face directions.
            Assert.All(lod.Vertices, v => Assert.True(MathF.Max(MathF.Abs(v.Normal.X), MathF.Max(MathF.Abs(v.Normal.Y), MathF.Abs(v.Normal.Z))) > 0.999f, $"{v.Normal}"));
        }

        [Fact]
        public void RatioOneLeavesTheMeshAlone()
        {
            var cube = Primitives.CreateCube();
            Assert.Same(cube, MeshDecimator.Decimate(cube, 1f));
        }

        [Fact]
        public void ModifierReducesTheEvaluatedMesh()
        {
            var sphere = Primitives.CreateSphere(1f, 16, 24);
            var modifier = new DecimateModifier { Ratio = 0.5f };
            var result = modifier.Apply(sphere);
            Assert.InRange(Triangles(result), Triangles(sphere) * 0.4, Triangles(sphere) * 0.6);
        }

        [Fact]
        public void VoxelRemeshRebuildsAClosedEvenSurface()
        {
            var sphere = Primitives.CreateSphere(1f, 12, 16);
            var remeshed = VoxelRemesher.Remesh(sphere, 0.1f)!;
            Assert.NotNull(remeshed);
            Assert.True(remeshed.Polygons.Count > 500);
            Assert.All(remeshed.Vertices, v => Assert.InRange(v.Position.Length(), 0.9f, 1.05f));
            AssertClosedManifold(remeshed);
            // Outward faces: normals point away from the centre.
            var outward = remeshed.Polygons.Count(p =>
            {
                var a = remeshed.Vertices[p.Indices[0]].Position;
                var n = Vector3.Cross(remeshed.Vertices[p.Indices[1]].Position - a, remeshed.Vertices[p.Indices[2]].Position - a);
                return Vector3.Dot(n, a) > 0;
            });
            Assert.True(outward > remeshed.Polygons.Count * 0.95);
        }

        [Fact]
        public void VoxelRemeshMergesOverlappingParts()
        {
            // Two intersecting cubes become one surface (no interior faces).
            var mesh = Primitives.CreateCube();
            var other = Primitives.CreateCube();
            MeshOperators.JoinInto(mesh, Matrix4x4.Identity, other, Matrix4x4.CreateTranslation(0.5f, 0.3f, 0.2f));
            var remeshed = VoxelRemesher.Remesh(mesh, 0.08f)!;
            AssertClosedManifold(remeshed);
            Assert.DoesNotContain(remeshed.Vertices, v => MathF.Abs(v.Position.X) < 0.3f && MathF.Abs(v.Position.Y - 0.1f) < 0.2f && MathF.Abs(v.Position.Z) < 0.2f);
        }
    }
}
