using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class MeshTopologyOperatorTests
    {
        private static int Corners(Mesh mesh, IEnumerable<int> indices) =>
            indices.Select(i => MeshOperators.Key(mesh.Vertices[i].Position)).Distinct().Count();

        [Fact]
        public void EdgeLoopOnGridSelectsAWholeRow()
        {
            var plane = Primitives.CreatePlane(4, 4, "Grid", 4, 4); // 5x5 corners
            // Two adjacent corners on the middle row (z = 0).
            var row = Enumerable.Range(0, plane.Vertices.Count)
                .Where(i => MathF.Abs(plane.Vertices[i].Position.Z) < 1e-4f)
                .OrderBy(i => plane.Vertices[i].Position.X).ToList();
            var a = row[1];
            var b = row[2];

            var loop = MeshTopologyOperators.SelectEdgeLoop(plane, a, b);

            Assert.Equal(5, Corners(plane, loop));
            Assert.All(loop, i => Assert.True(MathF.Abs(plane.Vertices[i].Position.Z) < 1e-4f));
        }

        [Fact]
        public void BridgeJoinsTwoQuadLoops()
        {
            var mesh = new Mesh();
            void Quad(float y)
            {
                var i0 = mesh.AddVertex(new Vertex(new Vector3(0, y, 0)));
                var i1 = mesh.AddVertex(new Vertex(new Vector3(1, y, 0)));
                var i2 = mesh.AddVertex(new Vertex(new Vector3(1, y, 1)));
                var i3 = mesh.AddVertex(new Vertex(new Vector3(0, y, 1)));
                mesh.AddQuad(i0, i1, i2, i3);
            }
            Quad(0);
            Quad(1);
            var all = Enumerable.Range(0, mesh.Vertices.Count).ToHashSet();

            var result = MeshTopologyOperators.BridgeEdgeLoops(mesh, all);

            Assert.NotNull(result);
            Assert.Equal(6, mesh.Polygons.Count); // 2 caps + 4 walls = a closed box
            // Every wall spans both levels.
            foreach (var wall in mesh.Polygons.Skip(2))
            {
                var ys = wall.Indices.Select(i => mesh.Vertices[i].Position.Y).Distinct().Count();
                Assert.Equal(2, ys);
            }
        }

        [Fact]
        public void BridgeRefusesWhenSelectionIsNotTwoLoops()
        {
            var cube = Primitives.CreateCube(); // closed - no boundary loops
            var all = Enumerable.Range(0, cube.Vertices.Count).ToHashSet();
            Assert.Null(MeshTopologyOperators.BridgeEdgeLoops(cube, all));
        }

        [Theory]
        [InlineData(0.5f)]
        [InlineData(0.25f)]
        public void LoopCutRingsAroundASplitVertexCube(float factor)
        {
            var cube = Primitives.CreateCube();
            var before = cube.Polygons.Count + cube.Faces.Count;
            // An edge along X on the top face: from (-0.5, 0.5, 0.5) to (0.5, 0.5, 0.5).
            int Find(Vector3 p) => Enumerable.Range(0, cube.Vertices.Count).First(i => Vector3.Distance(cube.Vertices[i].Position, p) < 1e-4f);
            var a = Find(new Vector3(-0.5f, 0.5f, 0.5f));
            var b = Find(new Vector3(0.5f, 0.5f, 0.5f));

            var loop = MeshTopologyOperators.LoopCut(cube, a, b, factor);

            Assert.NotNull(loop);
            Assert.Equal(4, Corners(cube, loop!)); // ring through top, front/back and bottom
            Assert.Equal(before + 4, cube.Polygons.Count + cube.Faces.Count);
            // The loop is a plane x = const, slid by the factor from a's side (x = -0.5).
            var expectedX = -0.5f + factor;
            Assert.All(loop!, i => Assert.Equal(expectedX, cube.Vertices[i].Position.X, 4));
        }

        [Fact]
        public void LoopCutKeepsMaterialSlots()
        {
            var cube = Primitives.CreateCube();
            foreach (var p in cube.Polygons) p.MaterialSlotIndex = 0;
            cube.AddMaterialSlot(new JolieCat3D.Core.Materials.Material("Slot"));
            int Find(Vector3 p) => Enumerable.Range(0, cube.Vertices.Count).First(i => Vector3.Distance(cube.Vertices[i].Position, p) < 1e-4f);
            Assert.NotNull(MeshTopologyOperators.LoopCut(cube, Find(new Vector3(-0.5f, 0.5f, 0.5f)), Find(new Vector3(0.5f, 0.5f, 0.5f))));
            Assert.All(cube.Polygons, p => Assert.Equal(0, p.MaterialSlotIndex));
        }

        [Fact]
        public void KnifeThroughCubeSplitsTheFourSides()
        {
            var cube = Primitives.CreateCube();
            var before = cube.Polygons.Count + cube.Faces.Count;

            var cut = MeshTopologyOperators.KnifeCut(cube, Matrix4x4.Identity, Vector3.Zero, Vector3.UnitY, _ => true);

            Assert.NotNull(cut);
            Assert.Equal(4, Corners(cube, cut!)); // four new corners around the middle
            Assert.Equal(before + 4, cube.Polygons.Count + cube.Faces.Count);
            Assert.All(cut!, i => Assert.Equal(0f, cube.Vertices[i].Position.Y, 4));
        }

        [Fact]
        public void KnifeRespectsTheCutSegment()
        {
            var cube = Primitives.CreateCube();
            // Only allow hits on the +X half: the -X side face stays whole.
            var cut = MeshTopologyOperators.KnifeCut(cube, Matrix4x4.Identity, Vector3.Zero, Vector3.UnitY, p => p.X > 0.25f);
            Assert.NotNull(cut);
            Assert.DoesNotContain(cut!, i => cube.Vertices[i].Position.X < 0);
        }
    }
}
