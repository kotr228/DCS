using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class MeshFaceOperatorTests
    {
        /// <summary>A flat n x n grid of quads (shared vertices) in XZ.</summary>
        private static Mesh Grid(int n)
        {
            var mesh = new Mesh("Grid");
            for (var z = 0; z <= n; z++)
            for (var x = 0; x <= n; x++)
                mesh.AddVertex(new Vertex(new Vector3(x, 0, z), Vector3.UnitY, new Vector2(x / (float)n, z / (float)n)));
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var i = z * (n + 1) + x;
                mesh.AddQuad(i, i + n + 1, i + n + 2, i + 1);
            }
            return mesh;
        }

        private static HashSet<int> All(Mesh m) => Enumerable.Range(0, m.Vertices.Count).ToHashSet();

        [Fact]
        public void PokeFansEachFaceAroundANewCentreVertex()
        {
            var m = Grid(2);
            var result = MeshFaceOperators.Poke(m, All(m));
            Assert.NotNull(result);
            Assert.Equal(16, m.Polygons.Count);
            Assert.Equal(9 + 4, m.Vertices.Count);
            Assert.Contains(m.Vertices, v => v.Position == new Vector3(0.5f, 0, 0.5f));
        }

        [Fact]
        public void TriangulateThenTrisToQuadsRestoresTheQuads()
        {
            var m = Grid(3);
            Assert.NotNull(MeshFaceOperators.Triangulate(m, All(m)));
            Assert.Equal(18, m.Polygons.Count);
            Assert.All(m.Polygons, p => Assert.Equal(3, p.Indices.Count));

            Assert.NotNull(MeshFaceOperators.TrisToQuads(m, All(m)));
            Assert.Equal(9, m.Polygons.Count);
            Assert.All(m.Polygons, p => Assert.Equal(4, p.Indices.Count));
            // Still a flat grid facing the same way (winding kept).
            foreach (var p in m.Polygons)
            {
                var a = m.Vertices[p.Indices[0]].Position; var b = m.Vertices[p.Indices[1]].Position; var c = m.Vertices[p.Indices[2]].Position;
                Assert.True(Vector3.Cross(b - a, c - a).Y > 0);
            }
        }

        [Fact]
        public void DissolveMergesAPatchIntoOneNgonAndDropsInteriorVertices()
        {
            var m = Grid(2);
            var result = MeshFaceOperators.DissolveFaces(m, All(m));
            Assert.NotNull(result);
            var poly = Assert.Single(m.Polygons);
            Assert.Equal(8, poly.Indices.Count);     // the 8 border vertices
            Assert.Equal(8, m.Vertices.Count);       // centre vertex removed
            Assert.DoesNotContain(m.Vertices, v => v.Position == new Vector3(1, 0, 1));
        }

        [Fact]
        public void DissolveLeavesARingWithAHoleAlone()
        {
            var m = Grid(3);
            m.SetTopology(m.Faces, m.Polygons.Where((_, i) => i != 4).ToList()); // punch out the middle face
            Assert.Null(MeshFaceOperators.DissolveFaces(m, All(m)));
            Assert.Equal(8, m.Polygons.Count);
        }

        [Fact]
        public void SeparateMovesSelectedFacesIntoANewMesh()
        {
            var m = Grid(2);
            m.MarkSeam(0, 1);
            // The left column of faces: vertices with x == 0 or 1.
            var selection = Enumerable.Range(0, m.Vertices.Count).Where(i => m.Vertices[i].Position.X <= 1).ToHashSet();
            var part = MeshFaceOperators.Separate(m, selection);
            Assert.NotNull(part);
            Assert.Equal(2, part!.Polygons.Count);
            Assert.Equal(6, part.Vertices.Count);
            Assert.Equal(2, m.Polygons.Count);
            Assert.Equal(6, m.Vertices.Count);          // unused vertices dropped
            Assert.Single(part.SeamEdges);              // the seam came along
            Assert.All(m.Vertices, v => Assert.True(v.Position.X >= 1));
        }
    }
}
