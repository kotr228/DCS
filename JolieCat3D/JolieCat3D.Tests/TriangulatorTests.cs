using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class TriangulatorTests
    {
        private static List<Vector2> Square(float half, bool ccw = true, Vector2 center = default)
        {
            var list = new List<Vector2> { new(-half, -half), new(half, -half), new(half, half), new(-half, half) };
            if (!ccw) list.Reverse();
            return list.Select(p => p + center).ToList();
        }

        private static float Area(List<(Vector2 A, Vector2 B, Vector2 C)> tris) =>
            tris.Sum(t => 0.5f * ((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)));

        [Fact]
        public void RingAreaExcludesTheHole()
        {
            // Same winding for both contours on purpose: nesting decides what is a hole.
            var tris = PolygonTriangulator.Triangulate(new[] { Square(2), Square(1) });
            Assert.Equal(16f - 4f, Area(tris), 3);
            Assert.All(tris, t => Assert.False(MathF.Abs(t.A.X) < 1 && MathF.Abs(t.A.Y) < 1 && MathF.Abs(t.B.X) < 1 && MathF.Abs(t.B.Y) < 1 && MathF.Abs(t.C.X) < 1 && MathF.Abs(t.C.Y) < 1));
        }

        [Fact]
        public void ConcaveAndSeparateShapes()
        {
            var l = new List<Vector2> { new(0, 0), new(3, 0), new(3, 1), new(1, 1), new(1, 3), new(0, 3) };
            var island = Square(0.5f, ccw: false, center: new Vector2(10, 10));
            var tris = PolygonTriangulator.Triangulate(new IReadOnlyList<Vector2>[] { l, island });
            Assert.Equal(5f + 1f, Area(tris), 3);
        }

        [Fact]
        public void ExtrudedRingIsClosedWithOutwardWalls()
        {
            var mesh = ShapeExtruder.Extrude(new[] { Square(2), Square(1, ccw: false) }, 0.5f);

            // Closed (by position).
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

            // Every face normal points away from the solid: outer walls outward, hole walls inward.
            foreach (var f in mesh.GetRenderFaces())
            {
                var a = mesh.Vertices[f.A].Position;
                var b = mesh.Vertices[f.B].Position;
                var c = mesh.Vertices[f.C].Position;
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                var center = (a + b + c) / 3f;
                if (MathF.Abs(n.Z) > 0.9f) { Assert.Equal(MathF.Sign(center.Z), MathF.Sign(n.Z)); continue; }
                var radial = new Vector2(center.X, center.Y);
                var isOuterWall = MathF.Max(MathF.Abs(radial.X), MathF.Abs(radial.Y)) > 1.5f;
                var dot = Vector2.Dot(new Vector2(n.X, n.Y), radial);
                Assert.True(isOuterWall ? dot > 0 : dot < 0, $"wall at {center} normal {n}");
            }
        }

        [Fact]
        public void TextFromASystemFontBecomesAClosedSolid()
        {
            var mesh = JolieCat3D.Engine.Text.TextOutline.BuildMesh("AOB", "Arial", 1f, 0.2f);
            Assert.True(mesh.Faces.Count > 20);
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            foreach (var f in mesh.GetRenderFaces())
                foreach (var (x, y) in new[] { (f.A, f.B), (f.B, f.C), (f.C, f.A) })
                {
                    var a = MeshOperators.Key(mesh.Vertices[x].Position);
                    var b = MeshOperators.Key(mesh.Vertices[y].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            var open = use.Count(kv => kv.Value != 2);
            Assert.True(open == 0, $"{open} open edges");
            var min = mesh.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var max = mesh.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            Assert.Equal(0.2f, max.Z - min.Z, 3);
            Assert.True(max.X - min.X > 1.5f); // three letters wide
        }
    }
}
