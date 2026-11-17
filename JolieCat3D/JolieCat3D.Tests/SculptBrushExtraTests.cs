using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Sculpting;

namespace JolieCat3D.Tests
{
    public sealed class SculptBrushExtraTests
    {
        private const int N = 16;

        /// <summary>A flat 1x1 grid in XZ facing +Y, with an optional bump/dent at the centre.</summary>
        private static Mesh Grid(float centreHeight = 0)
        {
            var mesh = new Mesh("Grid");
            for (var z = 0; z <= N; z++)
            for (var x = 0; x <= N; x++)
            {
                var p = new Vector3(x / (float)N - 0.5f, 0, z / (float)N - 0.5f);
                var r = new Vector2(p.X, p.Z).Length();
                p.Y = centreHeight * MathF.Max(0, 1 - r / 0.2f);
                mesh.AddVertex(new Vertex(p, Vector3.UnitY, new Vector2(x / (float)N, z / (float)N)));
            }
            for (var z = 0; z < N; z++)
            for (var x = 0; x < N; x++)
            {
                var i = z * (N + 1) + x;
                mesh.AddQuad(i, i + N + 1, i + N + 2, i + 1);
            }
            mesh.RecalculateNormals();
            return mesh;
        }

        private static int Centre => (N / 2) * (N + 1) + N / 2;

        [Fact]
        public void InflateMovesAlongNormals()
        {
            var g = Grid();
            for (var i = 0; i < 10; i++) SculptBrushExtras.ApplyInflate(g, Matrix4x4.Identity, Vector3.Zero, 0.3f, 1f, invert: false);
            Assert.True(g.Vertices[Centre].Position.Y > 0.05f, $"{g.Vertices[Centre].Position.Y}");
            Assert.Equal(0f, g.Vertices[0].Position.Y); // outside the brush
        }

        [Fact]
        public void FlattenRemovesABump()
        {
            var g = Grid(0.3f);
            var before = g.Vertices[Centre].Position.Y;
            for (var i = 0; i < 20; i++) SculptBrushExtras.ApplyFlatten(g, Matrix4x4.Identity, Vector3.Zero, 0.4f, 1f, invert: false);
            Assert.True(g.Vertices[Centre].Position.Y < before * 0.5f, $"{before} -> {g.Vertices[Centre].Position.Y}");
        }

        [Fact]
        public void PinchPullsSidewaysTowardsTheCentre()
        {
            var g = Grid();
            var side = Centre + 2;
            var before = g.Vertices[side].Position.X;
            SculptBrushExtras.ApplyPinch(g, Matrix4x4.Identity, Vector3.Zero, 0.4f, 1f, invert: false);
            Assert.True(g.Vertices[side].Position.X < before && g.Vertices[side].Position.X > 0);
            Assert.Equal(0f, g.Vertices[side].Position.Y, 4); // tangential only on a flat patch
        }

        [Fact]
        public void CreaseCutsAGrooveAndRidgeWhenInverted()
        {
            var g = Grid();
            for (var i = 0; i < 5; i++) SculptBrushExtras.ApplyCrease(g, Matrix4x4.Identity, Vector3.Zero, 0.3f, 1f, invert: false);
            Assert.True(g.Vertices[Centre].Position.Y < -0.03f, $"{g.Vertices[Centre].Position.Y}");
            var r = Grid();
            for (var i = 0; i < 5; i++) SculptBrushExtras.ApplyCrease(r, Matrix4x4.Identity, Vector3.Zero, 0.3f, 1f, invert: true);
            Assert.True(r.Vertices[Centre].Position.Y > 0.03f);
        }

        [Fact]
        public void ClayFillsADentButLeavesHighPointsAlone()
        {
            var g = Grid(-0.2f); // dent
            var before = g.Vertices[Centre].Position.Y;
            for (var i = 0; i < 20; i++) SculptBrushExtras.ApplyClay(g, Matrix4x4.Identity, Vector3.Zero, 0.4f, 1f, invert: false);
            Assert.True(g.Vertices[Centre].Position.Y > before + 0.05f);
        }

        [Fact]
        public void SplitVerticesNeverTearApart()
        {
            var cube = Primitives.CreateCube(1f);
            var corner = new Vector3(0.5f, 0.5f, 0.5f);
            foreach (var brush in new Action<Mesh>[]
            {
                m => SculptBrushExtras.ApplyInflate(m, Matrix4x4.Identity, corner, 0.8f, 0.1f, false),
                m => SculptBrushExtras.ApplyFlatten(m, Matrix4x4.Identity, corner, 0.8f, 0.1f, false),
                m => SculptBrushExtras.ApplyPinch(m, Matrix4x4.Identity, corner, 0.8f, 0.1f, false),
                m => SculptBrushExtras.ApplyCrease(m, Matrix4x4.Identity, corner, 0.8f, 0.1f, false),
                m => SculptBrushExtras.ApplyClay(m, Matrix4x4.Identity, corner, 0.8f, 0.1f, false),
            })
            {
                var m = Primitives.CreateCube(1f);
                brush(m);
                for (var i = 0; i < m.Vertices.Count; i++)
                for (var j = i + 1; j < m.Vertices.Count; j++)
                    if (Vector3.Distance(cube.Vertices[i].Position, cube.Vertices[j].Position) < 1e-5f)
                        Assert.True(Vector3.Distance(m.Vertices[i].Position, m.Vertices[j].Position) < 1e-5f);
            }
        }
    }
}
