using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class MeshSweepTests
    {
        private static int FaceCount(Mesh mesh) => mesh.Polygons.Count + mesh.Faces.Count;

        private static void AssertClosedManifold(Mesh mesh)
        {
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            IEnumerable<IReadOnlyList<int>> faces = mesh.Polygons.Select(p => p.Indices).Concat(mesh.Faces.Select(f => (IReadOnlyList<int>)new[] { f.A, f.B, f.C }));
            foreach (var f in faces)
                for (var i = 0; i < f.Count; i++)
                {
                    var a = MeshOperators.Key(mesh.Vertices[f[i]].Position);
                    var b = MeshOperators.Key(mesh.Vertices[f[(i + 1) % f.Count]].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            Assert.All(use, kv => Assert.Equal(2, kv.Value));
        }

        private static Mesh OffsetQuad()
        {
            // A vertical quad in the XY plane, 2..3 units from the Y axis.
            var m = new Mesh();
            var a = m.AddVertex(new Vertex(new Vector3(2, 0, 0)));
            var b = m.AddVertex(new Vertex(new Vector3(3, 0, 0)));
            var c = m.AddVertex(new Vertex(new Vector3(3, 1, 0)));
            var d = m.AddVertex(new Vertex(new Vector3(2, 1, 0)));
            m.AddQuad(a, b, c, d);
            return m;
        }

        [Fact]
        public void FullSpinMakesAClosedRing()
        {
            var mesh = OffsetQuad();
            var result = MeshSweepOperators.Spin(mesh, Enumerable.Range(0, 4).ToHashSet(), Vector3.Zero, Vector3.UnitY, 360f, 8);
            Assert.NotNull(result);
            Assert.Equal(8 * 4, FaceCount(mesh));
            AssertClosedManifold(mesh);
        }

        [Fact]
        public void PartialSpinHasStartAndEndCaps()
        {
            var mesh = OffsetQuad();
            var result = MeshSweepOperators.Spin(mesh, Enumerable.Range(0, 4).ToHashSet(), Vector3.Zero, Vector3.UnitY, 90f, 4);
            Assert.NotNull(result);
            Assert.Equal(4 * 4 + 2, FaceCount(mesh));
            AssertClosedManifold(mesh);
            // End cap rotated 90 degrees: it now lies in the YZ plane (x = 0).
            Assert.All(result!, i => Assert.Equal(0f, mesh.Vertices[i].Position.X, 3));
        }

        [Fact]
        public void ScrewAdvancesAlongTheAxis()
        {
            var mesh = OffsetQuad();
            var result = MeshSweepOperators.Spin(mesh, Enumerable.Range(0, 4).ToHashSet(), Vector3.Zero, Vector3.UnitY, 360f, 8, advancePerStep: 0.25f);
            Assert.NotNull(result);
            Assert.Equal(2f, result!.Min(i => mesh.Vertices[i].Position.Y), 3); // raised 8 * 0.25
            AssertClosedManifold(mesh);
        }

        [Fact]
        public void ExtrudeAlongNormalsMovesTheCapOut()
        {
            var cube = Primitives.CreateCube();
            var maxY = cube.Vertices.Max(v => v.Position.Y);
            var top = Enumerable.Range(0, cube.Vertices.Count).Where(i => MathF.Abs(cube.Vertices[i].Position.Y - maxY) < 1e-4f).ToHashSet();
            var faces = FaceCount(cube);

            var cap = MeshSweepOperators.ExtrudeAlongNormals(cube, top, 0.5f);

            Assert.NotNull(cap);
            Assert.Equal(faces + 4, FaceCount(cube));
            Assert.All(cap!, i => Assert.Equal(1f, cube.Vertices[i].Position.Y, 3));
            AssertClosedManifold(cube);
        }
    }
}
