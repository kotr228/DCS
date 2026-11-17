using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Tests
{
    public sealed class MeshOperatorTests
    {
        private static HashSet<int> TopFace(Mesh mesh)
        {
            var maxY = mesh.Vertices.Max(v => v.Position.Y);
            return Enumerable.Range(0, mesh.Vertices.Count).Where(i => MathF.Abs(mesh.Vertices[i].Position.Y - maxY) < 1e-4f).ToHashSet();
        }

        private static int DistinctCorners(Mesh mesh) => mesh.Vertices.Select(v => MeshOperators.Key(v.Position)).Distinct().Count();

        [Fact]
        public void ExtrudeTopFaceAddsFourCornersAndWalls()
        {
            var cube = Primitives.CreateCube();
            var polygonsBefore = cube.Polygons.Count + cube.Faces.Count;
            var selection = MeshOperators.ExtrudeRegion(cube, TopFace(cube), distance: 1f);

            Assert.NotNull(selection);
            Assert.Equal(12, DistinctCorners(cube)); // 8 + 4 new corners
            Assert.Equal(polygonsBefore + 4, cube.Polygons.Count + cube.Faces.Count); // cap moved, 4 walls added
            Assert.All(selection!, i => Assert.Equal(1.5f, cube.Vertices[i].Position.Y, 3));
        }

        [Fact]
        public void ExtrudeSinglePlaneKeepsSelectionValidAfterCleanup()
        {
            var plane = Primitives.CreatePlane();
            var all = Enumerable.Range(0, plane.Vertices.Count).ToHashSet();
            var selection = MeshOperators.ExtrudeRegion(plane, all, distance: 0.5f);
            Assert.NotNull(selection);
            Assert.All(selection!, i => Assert.InRange(i, 0, plane.Vertices.Count - 1));
            Assert.All(selection!, i => Assert.Equal(0.5f, MathF.Abs(plane.Vertices[i].Position.Y), 3));
        }

        [Fact]
        public void InsetCreatesInnerFaceAndRing()
        {
            var cube = Primitives.CreateCube();
            var faces = cube.Polygons.Count + cube.Faces.Count;
            var inner = MeshOperators.InsetFaces(cube, TopFace(cube), 0.25f);
            Assert.NotNull(inner);
            Assert.Equal(4, inner!.Count);
            Assert.Equal(faces + 4, cube.Polygons.Count + cube.Faces.Count);
            Assert.All(inner, i => Assert.True(MathF.Abs(cube.Vertices[i].Position.X) < 0.5f - 1e-4f));
        }

        [Fact]
        public void MergeCollapsesTopFaceIntoAPyramid()
        {
            var cube = Primitives.CreateCube();
            var merged = MeshOperators.MergeAtCenter(cube, TopFace(cube));
            Assert.NotNull(merged);
            Assert.Equal(5, DistinctCorners(cube)); // 4 bottom corners + apex
            Assert.All(merged!, i => Assert.Equal(new Vector3(0, 0.5f, 0), cube.Vertices[i].Position));
            // No degenerate polygons left.
            foreach (var p in cube.Polygons)
                Assert.True(p.Indices.Select(i => MeshOperators.Key(cube.Vertices[i].Position)).Distinct().Count() >= 3);
        }

        [Fact]
        public void FillMakesAFaceFromLooseCorners()
        {
            var mesh = new Mesh();
            mesh.AddVertex(new Vertex(new Vector3(0, 0, 0)));
            mesh.AddVertex(new Vertex(new Vector3(1, 0, 1)));
            mesh.AddVertex(new Vertex(new Vector3(1, 0, 0)));
            mesh.AddVertex(new Vertex(new Vector3(0, 0, 1)));
            Assert.NotNull(MeshOperators.Fill(mesh, new HashSet<int> { 0, 1, 2, 3 }));
            var poly = Assert.Single(mesh.Polygons);
            // Corners must be ordered around the face (no bow-tie): consecutive corners share an axis.
            for (var i = 0; i < 4; i++)
            {
                var a = mesh.Vertices[poly.Indices[i]].Position;
                var b = mesh.Vertices[poly.Indices[(i + 1) % 4]].Position;
                Assert.Equal(1f, Vector3.Distance(a, b), 3);
            }
        }

        [Fact]
        public void SelectLinkedFindsOnlyTheConnectedPart()
        {
            var mesh = Primitives.CreateCube();
            var offset = mesh.Vertices.Count;
            var second = Primitives.CreateCube();
            foreach (var v in second.Vertices) mesh.AddVertex(v.WithPosition(v.Position + new Vector3(5, 0, 0)));
            foreach (var p in second.Polygons) mesh.AddPolygon(new Polygon(p.Indices.Select(i => i + offset)));
            foreach (var f in second.Faces) mesh.AddTriangle(f.A + offset, f.B + offset, f.C + offset);

            var linked = MeshOperators.SelectLinked(mesh, new HashSet<int> { 0 });
            Assert.Equal(offset, linked.Count);
            Assert.All(linked, i => Assert.True(i < offset));
        }

        [Fact]
        public void ApplyTransformBakesIntoMeshAndKeepsChildrenInPlace()
        {
            var parent = new Node("P")
            {
                Mesh = Primitives.CreateCube(),
                LocalPosition = new Vector3(1, 2, 3),
                LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f),
                LocalScale = new Vector3(2, 1, 0.5f),
            };
            var child = new Node("C") { LocalPosition = new Vector3(0, 1, 0) };
            parent.AddChild(child);
            var worldVertex = Vector3.Transform(parent.Mesh.Vertices[0].Position, parent.GetWorldTransform());
            var childWorld = child.GetWorldPosition();

            MeshOperators.ApplyTransform(parent);

            Assert.Equal(Vector3.One, parent.LocalScale);
            Assert.Equal(Quaternion.Identity, parent.LocalRotation);
            Assert.True(Vector3.Distance(worldVertex, Vector3.Transform(parent.Mesh!.Vertices[0].Position, parent.GetWorldTransform())) < 1e-4f);
            Assert.True(Vector3.Distance(childWorld, child.GetWorldPosition()) < 1e-4f);
        }

        [Fact]
        public void JoinKeepsSourceGeometryInPlaceAndAddsItsMaterialAsSlot()
        {
            var target = new Node("T") { Mesh = Primitives.CreateCube(), LocalPosition = new Vector3(2, 0, 0) };
            target.Mesh.Material = new JolieCat3D.Core.Materials.Material("A");
            var source = new Node("S") { Mesh = Primitives.CreateCube(), LocalPosition = new Vector3(-3, 1, 0), LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f) };
            source.Mesh.Material = new JolieCat3D.Core.Materials.Material("B");
            var sourceWorldVertex = Vector3.Transform(source.Mesh.Vertices[3].Position, source.GetWorldTransform());
            var targetVerts = target.Mesh.Vertices.Count;
            var targetPolys = target.Mesh.Polygons.Count + target.Mesh.Faces.Count;

            MeshOperators.JoinInto(target.Mesh, target.GetWorldTransform(), source.Mesh, source.GetWorldTransform());

            Assert.Equal(targetVerts + source.Mesh.Vertices.Count, target.Mesh.Vertices.Count);
            Assert.Equal(targetPolys + source.Mesh.Polygons.Count + source.Mesh.Faces.Count, target.Mesh.Polygons.Count + target.Mesh.Faces.Count);
            var joinedWorld = Vector3.Transform(target.Mesh.Vertices[targetVerts + 3].Position, target.GetWorldTransform());
            Assert.True(Vector3.Distance(sourceWorldVertex, joinedWorld) < 1e-4f);
            Assert.Equal("B", Assert.Single(target.Mesh.MaterialSlots).Name);
            Assert.All(target.Mesh.Polygons.Skip(target.Mesh.Polygons.Count - source.Mesh.Polygons.Count), p => Assert.Equal(0, p.MaterialSlotIndex));
        }

        [Fact]
        public void OriginToGeometryKeepsMeshInPlace()
        {
            var mesh = Primitives.CreateCube();
            for (var i = 0; i < mesh.Vertices.Count; i++) mesh.SetVertexPosition(i, mesh.Vertices[i].Position + new Vector3(3, 0, 0));
            var node = new Node("N") { Mesh = mesh };
            var before = Vector3.Transform(mesh.Vertices[0].Position, node.GetWorldTransform());

            Assert.True(MeshOperators.OriginToGeometry(node));
            Assert.Equal(3f, node.LocalPosition.X, 4);
            Assert.True(Vector3.Distance(before, Vector3.Transform(node.Mesh!.Vertices[0].Position, node.GetWorldTransform())) < 1e-4f);
        }

        [Fact]
        public void SurfaceSamplingIsUniformAndRespectsSpacing()
        {
            var plane = Primitives.CreatePlane(10, 10, widthSegments: 4, depthSegments: 4);
            var points = SurfaceSampler.Sample(plane, System.Numerics.Matrix4x4.Identity, 400, seed: 3);
            Assert.Equal(400, points.Count);
            Assert.All(points, p => { Assert.InRange(p.Position.X, -5f, 5f); Assert.Equal(0f, p.Position.Y, 4); Assert.True(MathF.Abs(p.Normal.Y) > 0.99f); });
            // Roughly uniform: each half holds 35-65% of the points.
            var left = points.Count(p => p.Position.X < 0);
            Assert.InRange(left, 140, 260);

            var spaced = SurfaceSampler.Sample(plane, System.Numerics.Matrix4x4.Identity, 200, seed: 3, minDistance: 0.6f);
            for (var a = 0; a < spaced.Count; a++)
                for (var b = a + 1; b < spaced.Count; b++)
                    Assert.True(System.Numerics.Vector3.Distance(spaced[a].Position, spaced[b].Position) >= 0.6f);
        }

        [Fact]
        public void SymmetrizeMirrorsTheKeptHalfWithAWeldedSeam()
        {
            // An off-centre cube: x from -0.2 to 0.8. After +X -> -X it spans -0.8..0.8.
            var cube = Primitives.CreateCube();
            for (var i = 0; i < cube.Vertices.Count; i++) cube.SetVertexPosition(i, cube.Vertices[i].Position + new System.Numerics.Vector3(0.3f, 0, 0));
            MeshSymmetry.Symmetrize(cube);

            var xs = cube.Vertices.Select(v => v.Position.X).ToList();
            Assert.Equal(-0.8f, xs.Min(), 4);
            Assert.Equal(0.8f, xs.Max(), 4);
            // Closed: every edge (by position) shared by exactly two faces, and mirror-symmetric.
            var use = new Dictionary<((int, int, int), (int, int, int)), int>();
            foreach (var p in cube.Polygons)
                for (var k = 0; k < p.Indices.Count; k++)
                {
                    var a = MeshOperators.Key(cube.Vertices[p.Indices[k]].Position);
                    var b = MeshOperators.Key(cube.Vertices[p.Indices[(k + 1) % p.Indices.Count]].Position);
                    var key = Comparer<(int, int, int)>.Default.Compare(a, b) <= 0 ? (a, b) : (b, a);
                    use[key] = use.GetValueOrDefault(key) + 1;
                }
            Assert.All(use, kv => Assert.Equal(2, kv.Value));
            var keys = cube.Vertices.Select(v => MeshOperators.Key(v.Position)).ToHashSet();
            Assert.All(cube.Vertices, v => Assert.Contains(MeshOperators.Key(new System.Numerics.Vector3(-v.Position.X, v.Position.Y, v.Position.Z)), keys));
        }

        [Fact]
        public void WeightedNormalsFavourLargeFlatFaces()
        {
            // Big flat face and a small 45-degree bevel strip, with split (flat-shaded) vertices on the shared edge.
            var mesh = new Mesh("Bevelled");
            var up = System.Numerics.Vector3.UnitY;
            var tilt = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1, 1, 0));
            int V(float x, float y, float z, System.Numerics.Vector3 n) => mesh.AddVertex(new Vertex(new System.Numerics.Vector3(x, y, z), n, System.Numerics.Vector2.Zero));
            var big = new[] { V(-5, 0, -1, up), V(-5, 0, 1, up), V(0, 0, 1, up), V(0, 0, -1, up) };
            var strip = new[] { V(0, 0, -1, tilt), V(0, 0, 1, tilt), V(0.1f, -0.1f, 1, tilt), V(0.1f, -0.1f, -1, tilt) };
            mesh.AddPolygon(new Polygon(big));
            mesh.AddPolygon(new Polygon(strip));

            var result = new JolieCat3D.Core.Modifiers.WeightedNormalModifier { AngleDegrees = 60 }.Apply(mesh);
            // The strip edge shared with the big face now shades almost like the big face.
            Assert.True(System.Numerics.Vector3.Dot(result.Vertices[strip[0]].Normal, up) > 0.98f);
            Assert.True(System.Numerics.Vector3.Dot(result.Vertices[big[2]].Normal, up) > 0.98f);
            // With a small angle limit, the strip keeps its own normal.
            var sharp = new JolieCat3D.Core.Modifiers.WeightedNormalModifier { AngleDegrees = 10 }.Apply(mesh);
            Assert.True(System.Numerics.Vector3.Dot(sharp.Vertices[strip[0]].Normal, tilt) > 0.99f);
        }
    }
}
