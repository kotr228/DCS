using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class LscmUnwrapTests
    {
        private static Mesh BentStrip()
        {
            // A 6x2 strip of quads folded 90° in the middle: an exact unwrap is a flat
            // 6x2 rectangle (developable surface), so LSCM must recover it undistorted.
            var mesh = new Mesh("Strip");
            var index = new int[7, 3];
            for (var i = 0; i <= 6; i++)
            for (var j = 0; j <= 2; j++)
            {
                var p = i <= 3 ? new Vector3(i, 0, j) : new Vector3(3, i - 3, j);
                index[i, j] = mesh.AddVertex(new Vertex(p, Vector3.UnitY, Vector2.Zero));
            }
            for (var i = 0; i < 6; i++)
            for (var j = 0; j < 2; j++)
                mesh.AddPolygon(new Polygon(new[] { index[i, j], index[i, j + 1], index[i + 1, j + 1], index[i + 1, j] }));
            return mesh;
        }

        [Fact]
        public void DevelopableSurfaceUnwrapsWithoutDistortion()
        {
            var mesh = BentStrip();
            Assert.Equal(1, mesh.Unwrap());

            // Every pair of vertices: UV distance / 3D surface distance is one constant.
            // (Along the strip the surface distance is the unfolded one.)
            static Vector2 Unfolded(Vector3 p) => p.Y > 1e-4f ? new Vector2(3 + p.Y, p.Z) : new Vector2(p.X, p.Z);
            var ratios = new List<float>();
            for (var a = 0; a < mesh.Vertices.Count; a++)
            for (var b = a + 1; b < mesh.Vertices.Count; b++)
            {
                var d3 = Vector2.Distance(Unfolded(mesh.Vertices[a].Position), Unfolded(mesh.Vertices[b].Position));
                if (d3 < 1e-4f) continue;
                ratios.Add(Vector2.Distance(mesh.Vertices[a].UV, mesh.Vertices[b].UV) / d3);
            }
            Assert.InRange(ratios.Max() / ratios.Min(), 1f, 1.01f);
            Assert.All(mesh.Vertices, v => { Assert.InRange(v.UV.X, 0f, 1f); Assert.InRange(v.UV.Y, 0f, 1f); });
        }

        [Fact]
        public void UvTrianglesKeepFrontFacing()
        {
            var mesh = BentStrip();
            mesh.Unwrap();
            // D3D convention (V down): a front face is clockwise in (u, v) => negative cross.
            foreach (var p in mesh.Polygons)
            {
                var a = mesh.Vertices[p.Indices[0]].UV;
                var b = mesh.Vertices[p.Indices[1]].UV;
                var c = mesh.Vertices[p.Indices[2]].UV;
                var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                var first = Math.Sign(cross);
                Assert.NotEqual(0, first);
            }
            var signs = mesh.Polygons.Select(p =>
            {
                var a = mesh.Vertices[p.Indices[0]].UV;
                var b = mesh.Vertices[p.Indices[1]].UV;
                var c = mesh.Vertices[p.Indices[2]].UV;
                return Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
            }).Distinct().ToList();
            Assert.Single(signs); // no flipped faces inside the island
        }

        private static int UvWinding(Mesh mesh) => mesh.Polygons.Concat(mesh.Faces.Select(f => new Polygon(new[] { f.A, f.B, f.C }))).Select(p =>
        {
            var a = mesh.Vertices[p.Indices[0]].UV;
            var b = mesh.Vertices[p.Indices[1]].UV;
            var c = mesh.Vertices[p.Indices[2]].UV;
            return Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
        }).First(s => s != 0);

        [Theory]
        [InlineData("plane")]
        [InlineData("cube")]
        public void UnwrapKeepsTheSameUvOrientationAsThePrimitives(string kind)
        {
            var mesh = kind == "plane" ? Primitives.CreatePlane(2, 1, widthSegments: 3, depthSegments: 2) : Primitives.CreateCube();
            var authored = UvWinding(mesh);
            if (kind == "cube")
                foreach (var p in mesh.Polygons)
                    for (var i = 0; i < p.Indices.Count; i++) mesh.MarkSeam(p.Indices[i], p.Indices[(i + 1) % p.Indices.Count]);
            mesh.Unwrap();
            Assert.Equal(authored, UvWinding(mesh));
        }

        [Fact]
        public void CubeCutAlongEveryEdgeGivesSixSquareIslandsWithoutOverlap()
        {
            var cube = Primitives.CreateCube();
            foreach (var p in cube.Polygons)
                for (var i = 0; i < p.Indices.Count; i++)
                    cube.MarkSeam(p.Indices[i], p.Indices[(i + 1) % p.Indices.Count]);
            foreach (var f in cube.Faces)
            {
                cube.MarkSeam(f.A, f.B); cube.MarkSeam(f.B, f.C); cube.MarkSeam(f.C, f.A);
            }

            Assert.Equal(6, cube.Unwrap());
            Assert.False(UvOperators.IslandsOverlap(cube));
            foreach (var group in Enumerable.Range(0, cube.Vertices.Count).GroupBy(i => UvOperators.Islands(cube)[i]).Where(g => g.Key >= 0))
            {
                var uvs = group.Select(i => cube.Vertices[i].UV).ToList();
                var size = uvs.Aggregate(Vector2.Max) - uvs.Aggregate(Vector2.Min);
                Assert.InRange(size.X / size.Y, 0.98f, 1.02f);
            }
        }

        [Fact]
        public void SeamlessSphereIsSplitAutomaticallyAndDoesNotOverlap()
        {
            var sphere = Primitives.CreateSphere(0.5f, 8, 12);
            var islands = sphere.Unwrap();
            Assert.InRange(islands, 2, 40);
            Assert.False(UvOperators.IslandsOverlap(sphere));
            Assert.All(sphere.Vertices, v => { Assert.InRange(v.UV.X, 0f, 1f); Assert.InRange(v.UV.Y, 0f, 1f); });
        }

        [Fact]
        public void SmartProjectIgnoresSeamsAndSplitsByAngle()
        {
            var cube = Primitives.CreateCube();
            cube.ShadeSmooth();
            Assert.Equal(6, LscmUnwrapper.SmartProject(cube));
            Assert.False(UvOperators.IslandsOverlap(cube));
        }

        [Fact]
        public void SquareIslandsAreAxisAlignedSoPackingWastesNoSpace()
        {
            var cube = Primitives.CreateCube();
            foreach (var p in cube.Polygons)
                for (var i = 0; i < p.Indices.Count; i++) cube.MarkSeam(p.Indices[i], p.Indices[(i + 1) % p.Indices.Count]);
            cube.Unwrap();
            // Axis-aligned squares: every UV edge is horizontal or vertical.
            foreach (var p in cube.Polygons)
                for (var i = 0; i < p.Indices.Count; i++)
                {
                    var d = cube.Vertices[p.Indices[(i + 1) % p.Indices.Count]].UV - cube.Vertices[p.Indices[i]].UV;
                    Assert.True(MathF.Abs(d.X) < 1e-3f || MathF.Abs(d.Y) < 1e-3f, $"edge {d}");
                }
        }

        [Fact]
        public void ShapeKeysFollowDuplicatedVertices()
        {
            var mesh = BentStrip();
            ShapeKeys.AddBasis(mesh);
            mesh.SetVertexPosition(0, mesh.Vertices[0].Position + Vector3.UnitY);
            ShapeKeys.SaveCurrentAsShapeKey(mesh, "Lift");
            // A seam across the middle splits shared vertices.
            for (var j = 0; j < 2; j++)
            {
                var a = Enumerable.Range(0, mesh.Vertices.Count).First(i => mesh.Vertices[i].Position == new Vector3(3, 0, j));
                var b = Enumerable.Range(0, mesh.Vertices.Count).First(i => mesh.Vertices[i].Position == new Vector3(3, 0, j + 1));
                mesh.MarkSeam(a, b);
            }

            Assert.Equal(2, mesh.Unwrap());
            Assert.Equal(mesh.Vertices.Count, mesh.ShapeBasis!.Length);
            Assert.True(ShapeKeys.IsValid(mesh, mesh.ShapeKeys[0]));
        }
    }
}
