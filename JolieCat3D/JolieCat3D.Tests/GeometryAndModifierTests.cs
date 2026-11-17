using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service;

namespace JolieCat3D.Tests
{
    public sealed class GeometryAndModifierTests
    {
        [Theory]
        [InlineData("cube")]
        [InlineData("sphere")]
        [InlineData("plane")]
        [InlineData("cylinder")]
        public void PrimitivesHaveValidIndices(string kind)
        {
            var mesh = kind switch
            {
                "cube" => Primitives.CreateCube(),
                "sphere" => Primitives.CreateSphere(),
                "plane" => Primitives.CreatePlane(),
                _ => Primitives.CreateCylinder(),
            };

            Assert.NotEmpty(mesh.Vertices);
            Assert.True(mesh.Faces.Count + mesh.Polygons.Count > 0);
            foreach (var f in mesh.Faces)
                Assert.All(new[] { f.A, f.B, f.C }, i => Assert.InRange(i, 0, mesh.Vertices.Count - 1));
            foreach (var p in mesh.Polygons)
                Assert.All(p.Indices, i => Assert.InRange(i, 0, mesh.Vertices.Count - 1));
        }

        [Fact]
        public void ArrayModifierMultipliesGeometry()
        {
            var cube = Primitives.CreateCube();
            var result = new ArrayModifier { Count = 4 }.Apply(cube);
            Assert.Equal(cube.Vertices.Count * 4, result.Vertices.Count);
        }

        [Fact]
        public void MirrorModifierMirrorsAcrossAxis()
        {
            var cube = Primitives.CreateCube();
            for (var i = 0; i < cube.Vertices.Count; i++)
                cube.SetVertexPosition(i, cube.Vertices[i].Position + new Vector3(2, 0, 0));

            var result = new MirrorModifier { Axis = MirrorAxis.X }.Apply(cube);

            Assert.Contains(result.Vertices, v => v.Position.X < -1f);
            Assert.Contains(result.Vertices, v => v.Position.X > 1f);
        }

        [Fact]
        public void SubdivisionIncreasesDensity()
        {
            var cube = Primitives.CreateCube();
            var result = new SubdivisionSurfaceModifier { Iterations = 1 }.Apply(cube);
            Assert.True(result.Vertices.Count > cube.Vertices.Count);
        }

        [Fact]
        public void DisabledModifiersAreSkippedByTheStack()
        {
            var cube = Primitives.CreateCube();
            var result = ModifierStack.Evaluate(cube, new Modifier[] { new ArrayModifier { Count = 3, IsEnabled = false } });
            Assert.Equal(cube.Vertices.Count, result.Vertices.Count);
        }

        [Fact]
        public void ModifiersDoNotMutateTheirInput()
        {
            var cube = Primitives.CreateCube();
            var hash = MeshFingerprint.Compute(cube);
            _ = new SolidifyModifier { Thickness = 0.2f }.Apply(cube);
            _ = new EdgeSplitModifier().Apply(cube);
            _ = new SubdivisionSurfaceModifier().Apply(cube);
            Assert.Equal(hash, MeshFingerprint.Compute(cube));
        }

        [Fact]
        public void BundledGlbTestAssetImports()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "TestAssets"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);

            var scene = MeshFileService.ImportScene(Path.Combine(dir!, "TestAssets", "bottle.glb"));
            Assert.Contains(scene.Traverse(), n => n.Mesh is { Vertices.Count: > 0 });
        }

        [Theory]
        [InlineData(".obj")]
        [InlineData(".stl")]
        [InlineData(".glb")]
        public void MeshFormatsRoundTripVertexPositions(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), "jc3d-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                var scene = new Scene3D();
                scene.AddRootNode(new Node("Box") { Mesh = Primitives.CreateCube(2f) });
                MeshFileService.ExportScene(scene, path);

                var loaded = MeshFileService.ImportScene(path);
                var meshes = loaded.Traverse().Where(n => n.Mesh is not null).Select(n => n.Mesh!).ToList();
                Assert.NotEmpty(meshes);
                var maxX = meshes.SelectMany(m => m.Vertices).Max(v => v.Position.X);
                Assert.Equal(1f, maxX, 3);
            }
            finally
            {
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "*"))
                    File.Delete(f);
            }
        }

        [Theory]
        [InlineData(ShrinkwrapMode.NearestSurface)]
        [InlineData(ShrinkwrapMode.Project)]
        public void ShrinkwrapSnapsOntoTheTargetSurface(ShrinkwrapMode mode)
        {
            var target = new Node("Ball") { Mesh = Primitives.CreateSphere(1f, 32, 48), LocalPosition = new System.Numerics.Vector3(5, 0, 0) };
            var cage = new Node("Cage") { Mesh = Primitives.CreateSphere(2f, 8, 12), LocalPosition = new System.Numerics.Vector3(5, 0, 0) };
            var modifier = new ShrinkwrapModifier { Target = target, Mode = mode, Offset = 0.1f };
            var result = modifier.Apply(cage.Mesh, cage);
            Assert.All(result.Vertices, v => Assert.InRange(v.Position.Length(), 1.05f, 1.12f)); // cage-local = sphere centre
        }

        [Fact]
        public void BvhClosestPointMatchesBruteForce()
        {
            var mesh = Primitives.CreateSphere(1f, 12, 16);
            var tris = mesh.GetRenderFaces().Select(f => (mesh.Vertices[f.A].Position, mesh.Vertices[f.B].Position, mesh.Vertices[f.C].Position)).ToList();
            var bvh = new TriangleBvh(tris);
            var rng = new Random(4);
            for (var k = 0; k < 50; k++)
            {
                var p = new System.Numerics.Vector3(rng.NextSingle() * 4 - 2, rng.NextSingle() * 4 - 2, rng.NextSingle() * 4 - 2);
                var fast = bvh.ClosestPoint(p)!.Value.Point;
                var brute = tris.Select(t => ClosestBrute(p, t.Item1, t.Item2, t.Item3)).Min(d => d);
                Assert.Equal(brute, System.Numerics.Vector3.Distance(p, fast), 3);
            }

            static float ClosestBrute(System.Numerics.Vector3 p, System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c)
            {
                var best = float.MaxValue;
                for (var i = 0; i <= 60; i++)
                    for (var j = 0; j <= 60 - i; j++)
                    {
                        var q = a + (b - a) * (i / 60f) + (c - a) * (j / 60f);
                        best = MathF.Min(best, System.Numerics.Vector3.Distance(p, q));
                    }
                return best;
            }
        }

        [Fact]
        public void CurveModifierBendsAlongAnLShapedPath()
        {
            // Straight segments (handles on the line): along +X for 5, then along +Z for 5.
            var curve = new CurveData();
            var p0 = System.Numerics.Vector3.Zero;
            var p1 = new System.Numerics.Vector3(5, 0, 0);
            var p2 = new System.Numerics.Vector3(5, 0, 5);
            curve.Points.Add(new CurvePoint(p0, p0, System.Numerics.Vector3.Lerp(p0, p1, 1f / 3)));
            curve.Points.Add(new CurvePoint(p1, System.Numerics.Vector3.Lerp(p0, p1, 2f / 3), System.Numerics.Vector3.Lerp(p1, p2, 1f / 3)));
            curve.Points.Add(new CurvePoint(p2, System.Numerics.Vector3.Lerp(p1, p2, 2f / 3), p2));
            var path = new Node("Path") { Curve = curve };

            var mesh = new Mesh("Points");
            mesh.AddVertex(new Vertex(new System.Numerics.Vector3(2, 0, 0), System.Numerics.Vector3.UnitY, System.Numerics.Vector2.Zero));
            mesh.AddVertex(new Vertex(new System.Numerics.Vector3(8, 0, 0), System.Numerics.Vector3.UnitY, System.Numerics.Vector2.Zero));
            mesh.AddVertex(new Vertex(new System.Numerics.Vector3(8, 1, 0), System.Numerics.Vector3.UnitY, System.Numerics.Vector2.Zero));
            var owner = new Node("Rail") { Mesh = mesh };

            var bent = new CurveDeformModifier { Target = path }.Apply(mesh, owner);
            Assert.True(System.Numerics.Vector3.Distance(bent.Vertices[0].Position, new System.Numerics.Vector3(2, 0, 0)) < 0.05f);
            Assert.True(System.Numerics.Vector3.Distance(bent.Vertices[1].Position, new System.Numerics.Vector3(5, 0, 3)) < 0.05f);
            // Y offsets stay "up" through the bend (no twist).
            Assert.True(System.Numerics.Vector3.Distance(bent.Vertices[2].Position, new System.Numerics.Vector3(5, 1, 3)) < 0.05f);
        }

        [Fact]
        public void CreasedEdgesStaySharpUnderSubdivision()
        {
            // A welded cube (shared vertices) so subdivision rounds it.
            var cube = new Mesh("Cube");
            var v = new int[8];
            for (var i = 0; i < 8; i++)
                v[i] = cube.AddVertex(new Vertex(new System.Numerics.Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f), System.Numerics.Vector3.UnitY, System.Numerics.Vector2.Zero));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var q in quads) cube.AddPolygon(new Polygon(q.Select(i => v[i])));

            static float MaxCornerReach(Mesh m) => m.Vertices.Max(x => MathF.Abs(x.Position.X) + MathF.Abs(x.Position.Y) + MathF.Abs(x.Position.Z));
            var smooth = CatmullClarkSubdivider.Subdivide(CatmullClarkSubdivider.Subdivide(cube));
            Assert.True(MaxCornerReach(smooth) < 1.3f); // corners rounded off (sharp corner would be 1.5)

            foreach (var (a, b) in cube.GetEdges()) cube.MarkCrease(a, b);
            var sharp = CatmullClarkSubdivider.Subdivide(CatmullClarkSubdivider.Subdivide(cube));
            Assert.Equal(1.5f, MaxCornerReach(sharp), 3); // every edge creased: the cube keeps its shape
            Assert.True(sharp.CreaseEdges.Count > 12);     // creases carried to the finer level
            Assert.All(sharp.Vertices, x => Assert.InRange(MathF.Max(MathF.Abs(x.Position.X), MathF.Max(MathF.Abs(x.Position.Y), MathF.Abs(x.Position.Z))), 0.499f, 0.501f));
        }

        [Fact]
        public void SplitVertexCubeSubdividesAsOneClosedSurface()
        {
            var cube = Primitives.CreateCube(); // vertices split per face (hard normals, UVs)
            var sub = CatmullClarkSubdivider.Subdivide(CatmullClarkSubdivider.Subdivide(cube));
            // Rounded like Blender: the corners pull in, but every face stays attached.
            Assert.True(sub.Vertices.Max(x => MathF.Abs(x.Position.X) + MathF.Abs(x.Position.Y) + MathF.Abs(x.Position.Z)) < 1.3f);
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            foreach (var p in sub.Polygons)
                for (var k = 0; k < p.Indices.Count; k++)
                {
                    var a = MeshOperators.Key(sub.Vertices[p.Indices[k]].Position);
                    var b = MeshOperators.Key(sub.Vertices[p.Indices[(k + 1) % p.Indices.Count]].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            Assert.All(use, kv => Assert.Equal(2, kv.Value));
            Assert.Contains(sub.Vertices, x => x.UV != System.Numerics.Vector2.Zero); // UVs carried
            Assert.All(sub.Polygons, p => Assert.Equal(-1, p.MaterialSlotIndex));

            // Creasing every edge of the split cube keeps it a cube.
            var creased = Primitives.CreateCube();
            foreach (var (a, b) in creased.GetEdges()) creased.MarkCrease(a, b);
            var sharp = CatmullClarkSubdivider.Subdivide(creased);
            Assert.All(sharp.Vertices, x => Assert.InRange(MathF.Max(MathF.Abs(x.Position.X), MathF.Max(MathF.Abs(x.Position.Y), MathF.Abs(x.Position.Z))), 0.499f, 0.501f));
        }
    }
}
