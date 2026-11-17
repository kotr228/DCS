using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class MeshBevelTests
    {
        private static int Corners(Mesh mesh) => mesh.Vertices.Select(v => MeshOperators.Key(v.Position)).Distinct().Count();

        private static int FaceCount(Mesh mesh) => mesh.Polygons.Count + mesh.Faces.Count;

        /// <summary>Every edge (by position) used by exactly two faces - no holes, no overlaps.</summary>
        private static void AssertClosedManifold(Mesh mesh)
        {
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            IEnumerable<IReadOnlyList<int>> faces = mesh.Polygons.Select(p => p.Indices).Concat(mesh.Faces.Select(f => (IReadOnlyList<int>)new[] { f.A, f.B, f.C }));
            foreach (var f in faces)
            {
                for (var i = 0; i < f.Count; i++)
                {
                    var a = MeshOperators.Key(mesh.Vertices[f[i]].Position);
                    var b = MeshOperators.Key(mesh.Vertices[f[(i + 1) % f.Count]].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            }
            Assert.All(use, kv => Assert.Equal(2, kv.Value));
        }

        private static HashSet<int> At(Mesh mesh, params Vector3[] points) =>
            Enumerable.Range(0, mesh.Vertices.Count).Where(i => points.Any(p => Vector3.Distance(p, mesh.Vertices[i].Position) < 1e-4f)).ToHashSet();

        [Fact]
        public void SingleEdgeBecomesAChamfer()
        {
            var cube = Primitives.CreateCube();
            var faces = FaceCount(cube);
            var edge = At(cube, new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f));

            var result = MeshBevel.BevelEdges(cube, edge, 0.1f);

            Assert.NotNull(result);
            Assert.Equal(faces + 1, FaceCount(cube));   // one strip face
            Assert.Equal(10, Corners(cube));             // the two ends split in two
            AssertClosedManifold(cube);
            Assert.DoesNotContain(cube.Vertices, v => Vector3.Distance(v.Position, new Vector3(0.5f, 0.5f, 0.5f)) < 1e-4f);
        }

        [Fact]
        public void BevellingEveryCubeEdgeAddsCornerPatches()
        {
            var cube = Primitives.CreateCube();
            var all = Enumerable.Range(0, cube.Vertices.Count).ToHashSet();

            Assert.NotNull(MeshBevel.BevelEdges(cube, all, 0.1f));

            Assert.Equal(6 + 12 + 8, FaceCount(cube)); // faces + strips + corner triangles
            Assert.Equal(24, Corners(cube));
            AssertClosedManifold(cube);
        }

        [Fact]
        public void TwoEdgesMeetingAtACornerStayClosed()
        {
            var cube = Primitives.CreateCube();
            // Top face front edge and top face right edge share the corner (0.5, 0.5, 0.5).
            var selection = At(cube, new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f));
            Assert.NotNull(MeshBevel.BevelEdges(cube, selection, 0.1f));
            AssertClosedManifold(cube);
        }

        [Fact]
        public void WidthIsClampedSoFacesNeverInvert()
        {
            var cube = Primitives.CreateCube();
            var edge = At(cube, new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f));
            Assert.NotNull(MeshBevel.BevelEdges(cube, edge, 5f));
            Assert.All(cube.Vertices, v => Assert.InRange(MathF.Abs(v.Position.Y), 0f, 0.5001f));
            AssertClosedManifold(cube);
        }

        [Fact]
        public void SegmentsRoundTheEdgeAlongAQuarterCircle()
        {
            var cube = Primitives.CreateCube();
            var faces = FaceCount(cube);
            var edge = At(cube, new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f));

            var result = MeshBevel.BevelEdges(cube, edge, 0.1f, segments: 4);

            Assert.NotNull(result);
            Assert.Equal(faces + 4 + 2, FaceCount(cube)); // 4 strip quads + 2 end-cap fillers
            AssertClosedManifold(cube);
            // Every strip vertex lies on the arc: radius 0.1 around the line y = z = 0.4.
            foreach (var i in result!)
            {
                var p = cube.Vertices[i].Position;
                var r = new Vector2(p.Y - 0.4f, p.Z - 0.4f).Length();
                Assert.InRange(r, 0.0999f, 0.1001f);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void SegmentedBevelOfEveryCubeEdgeStaysClosed(int segments)
        {
            var cube = Primitives.CreateCube();
            var all = Enumerable.Range(0, cube.Vertices.Count).ToHashSet();
            Assert.NotNull(MeshBevel.BevelEdges(cube, all, 0.1f, segments));
            Assert.Equal(6 + 12 * segments + 8 * 3 * segments, FaceCount(cube)); // + spherical corner fans
            AssertClosedManifold(cube);
        }

        [Fact]
        public void NothingToBevelReturnsNull()
        {
            var cube = Primitives.CreateCube();
            Assert.Null(MeshBevel.BevelEdges(cube, At(cube, new Vector3(0.5f, 0.5f, 0.5f)), 0.1f));
        }

        [Fact]
        public void BevelModifierBevelsOnlySharpEdges()
        {
            // A cube: all 12 edges are 90 degrees -> all bevelled at a 30 degree limit, none at 100.
            var cube = Primitives.CreateCube();
            var bevelled = new JolieCat3D.Core.Modifiers.BevelModifier { Width = 0.1f, Segments = 1, AngleDegrees = 30 }.Apply(cube);
            Assert.Equal(6 + 12 + 8, FaceCount(bevelled));
            AssertClosedManifold(bevelled);
            var untouched = new JolieCat3D.Core.Modifiers.BevelModifier { Width = 0.1f, AngleDegrees = 100 }.Apply(cube);
            Assert.Same(cube, untouched);
            Assert.Equal(6, FaceCount(cube)); // the input mesh is never modified
        }
    }
}
