using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class UvOperatorTests
    {
        [Fact]
        public void SplitCubeHasOneIslandPerFace()
        {
            var cube = Primitives.CreateCube(); // 24 vertices: each face has its own corners
            var islands = UvOperators.Islands(cube);
            Assert.Equal(6, islands.Where(i => i >= 0).Distinct().Count());
        }

        [Fact]
        public void SelectIslandsGrowsToWholeIsland()
        {
            var cube = Primitives.CreateCube();
            var island = UvOperators.SelectIslands(cube, new[] { 0 });
            Assert.Equal(4, island.Count);
        }

        [Fact]
        public void PackingFitsTheUnitSquareWithoutOverlap()
        {
            var cube = Primitives.CreateCube();
            // Stack every face on top of each other first (worst case).
            for (var i = 0; i < cube.Vertices.Count; i++) cube.SetVertexUV(i, cube.Vertices[i].UV * 0.5f);

            Assert.Equal(6, UvOperators.PackIslands(cube, 0.02f));

            Assert.All(cube.Vertices, v =>
            {
                Assert.InRange(v.UV.X, 0f, 1f);
                Assert.InRange(v.UV.Y, 0f, 1f);
            });
            Assert.False(UvOperators.IslandsOverlap(cube));
        }

        [Fact]
        public void TransformMovesOnlyTheGivenVertices()
        {
            var cube = Primitives.CreateCube();
            var before = cube.Vertices.Select(v => v.UV).ToArray();
            UvOperators.Transform(cube, new[] { 0, 1 }, uv => uv + new Vector2(0.1f, 0));
            Assert.Equal(before[0] + new Vector2(0.1f, 0), cube.Vertices[0].UV);
            Assert.Equal(before[2], cube.Vertices[2].UV);
        }
    }
}
