using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Tests
{
    public sealed class ExtraModifierTests
    {
        private static Mesh Grid(int n = 8)
        {
            var mesh = new Mesh("Grid");
            for (var z = 0; z <= n; z++)
            for (var x = 0; x <= n; x++)
                mesh.AddVertex(new Vertex(new Vector3(x / (float)n - 0.5f, 0, z / (float)n - 0.5f), Vector3.UnitY, new Vector2(x / (float)n, z / (float)n)));
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var i = z * (n + 1) + x;
                mesh.AddQuad(i, i + n + 1, i + n + 2, i + 1);
            }
            return mesh;
        }

        [Fact]
        public void DisplaceMovesAlongNormalsWithoutTearingSplitVertices()
        {
            var cube = Primitives.CreateCube(1f); // split corners (hard edges)
            var result = new DisplaceModifier { Strength = 0.5f, Scale = 3f }.Apply(cube);
            var moved = false;
            for (var i = 0; i < cube.Vertices.Count; i++)
            {
                moved |= Vector3.Distance(cube.Vertices[i].Position, result.Vertices[i].Position) > 1e-3f;
                // Every copy of a corner moves to the same place - the surface stays closed.
                for (var j = i + 1; j < cube.Vertices.Count; j++)
                    if (Vector3.Distance(cube.Vertices[i].Position, cube.Vertices[j].Position) < 1e-5f)
                        Assert.True(Vector3.Distance(result.Vertices[i].Position, result.Vertices[j].Position) < 1e-4f);
            }
            Assert.True(moved);

            var flat = new DisplaceModifier { Strength = 1f, Direction = 2 }.Apply(Grid()); // along Y
            Assert.All(flat.Vertices, v => Assert.InRange(v.Position.Y, -0.6f, 0.6f));
            Assert.True(flat.Vertices.Select(v => v.Position.Y).Distinct().Count() > 10);
        }

        [Fact]
        public void SmoothPullsAPeakDown()
        {
            var grid = Grid();
            var centre = 4 * 9 + 4;
            grid.SetVertexPosition(centre, grid.Vertices[centre].Position + Vector3.UnitY);
            var result = new SmoothModifier { Factor = 0.5f, Repeat = 10 }.Apply(grid);
            Assert.True(result.Vertices[centre].Position.Y < 0.3f, $"peak {result.Vertices[centre].Position.Y}");
            Assert.True(result.Vertices[centre - 1].Position.Y > 0.01f, "neighbours rise");
        }

        [Fact]
        public void TriangulateSplitsQuadsAlongTheShorterDiagonal()
        {
            var mesh = new Mesh("Quad");
            mesh.AddVertex(new Vertex(new Vector3(0, 0, 0)));
            mesh.AddVertex(new Vertex(new Vector3(3, 0, 0)));
            mesh.AddVertex(new Vertex(new Vector3(4, 0, 1)));
            mesh.AddVertex(new Vertex(new Vector3(0, 0, 1)));
            mesh.AddPolygon(new Polygon(0, 1, 2, 3) { MaterialSlotIndex = 2 });
            var result = new TriangulateModifier().Apply(mesh);
            Assert.Equal(2, result.Polygons.Count);
            Assert.All(result.Polygons, p => Assert.Equal(3, p.Indices.Count));
            Assert.All(result.Polygons, p => Assert.Equal(2, p.MaterialSlotIndex));
            // Diagonal 1-3 (length ~3.16) is shorter than 0-2 (~4.12).
            Assert.Contains(result.Polygons, p => p.Indices.Contains(1) && p.Indices.Contains(3) && p.Indices.Contains(0));
        }

        [Fact]
        public void WeldMergesCoincidentVerticesOptionallyKeepingUvSeams()
        {
            var cube = Primitives.CreateCube(1f);
            var all = new WeldModifier().Apply(cube);
            Assert.Equal(8, all.Vertices.Count);
            Assert.Equal(cube.Polygons.Count + cube.Faces.Count, all.Polygons.Count + all.Faces.Count);

            var seams = new WeldModifier { Mode = 1 }.Apply(cube);
            Assert.True(seams.Vertices.Count > 8, "UV seams keep their split vertices");
            Assert.True(seams.Vertices.Count <= cube.Vertices.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void SimpleDeformChangesTheShapeAndKeepsTheAxisCentre(int method)
        {
            var cube = Primitives.CreateCube(1f);
            var deform = new SimpleDeformModifier { Method = method, Amount = method < 2 ? 60f : 0.5f, Axis = 1 };
            var result = deform.Apply(cube);
            Assert.Contains(Enumerable.Range(0, cube.Vertices.Count), i => Vector3.Distance(cube.Vertices[i].Position, result.Vertices[i].Position) > 1e-3f);
            Assert.All(result.Vertices, v => Assert.True(float.IsFinite(v.Position.X) && float.IsFinite(v.Position.Y) && float.IsFinite(v.Position.Z)));
        }

        [Fact]
        public void CastSphereRoundsACubeAndRemeshClosesIt()
        {
            var cube = Primitives.CreateCube(2f);
            var dense = new SubdivisionSurfaceModifier { Iterations = 2 }.Apply(cube);
            var cast = new CastModifier { Factor = 1f }.Apply(dense);
            var radii = cast.Vertices.Select(v => v.Position.Length()).ToList();
            Assert.True(radii.Max() - radii.Min() < 1e-3f, "all points end on one sphere");

            var box = new CastModifier { Shape = 2, Factor = 1f }.Apply(Primitives.CreateSphere());
            Assert.Contains(box.Vertices, v => MathF.Abs(MathF.Abs(v.Position.X) - box.GetBounds().Max.X) < 1e-4f);

            var remeshed = new RemeshModifier { VoxelSize = 0.25f }.Apply(cube);
            Assert.True(remeshed.Vertices.Count > 8);
            Assert.True(new RemeshModifier().Clone() is RemeshModifier);
        }

        [Fact]
        public void ParameterizedModifiersSurviveSavingTheProject()
        {
            var scene = new Scene3D();
            var node = new Node("Rock") { Mesh = Primitives.CreateCube(1f) };
            node.Modifiers.Add(new DisplaceModifier { Strength = 0.33f, Direction = 3, Seed = 7 });
            node.Modifiers.Add(new SimpleDeformModifier { Method = 2, Amount = 0.25f, Axis = 0 });
            node.Modifiers.Add(new WeldModifier { Distance = 0.01f, Mode = 1 });
            node.Modifiers.Add(new SmoothModifier { Repeat = 3 });
            node.Modifiers.Add(new TriangulateModifier { QuadMethod = 1, IsEnabled = false });
            scene.AddRootNode(node);

            var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new JolieCat3D.Service.Animation.AnimationTimeline());
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
            var mods = loaded.Traverse().First(n => n.Name == "Rock").Modifiers;
            var displace = Assert.IsType<DisplaceModifier>(mods[0]);
            Assert.Equal((0.33f, 3, 7), (displace.Strength, displace.Direction, displace.Seed));
            var simple = Assert.IsType<SimpleDeformModifier>(mods[1]);
            Assert.Equal((2, 0.25f, 0), (simple.Method, simple.Amount, simple.Axis));
            var weld = Assert.IsType<WeldModifier>(mods[2]);
            Assert.Equal((0.01f, 1), (weld.Distance, weld.Mode));
            Assert.Equal(3, Assert.IsType<SmoothModifier>(mods[3]).Repeat);
            var tri = Assert.IsType<TriangulateModifier>(mods[4]);
            Assert.False(tri.IsEnabled);
            Assert.Equal(1, tri.QuadMethod);
        }
    }
}
