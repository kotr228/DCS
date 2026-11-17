using System.Numerics;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.Tests
{
    public sealed class ConvexHullTests
    {
        private static void AssertConvexAndEnclosing(Mesh hull, IEnumerable<Vector3> points)
        {
            foreach (var f in hull.GetRenderFaces())
            {
                var a = hull.Vertices[f.A].Position;
                var n = Vector3.Normalize(Vector3.Cross(hull.Vertices[f.B].Position - a, hull.Vertices[f.C].Position - a));
                foreach (var v in hull.Vertices) Assert.True(Vector3.Dot(v.Position - a, n) < 1e-4f, "hull must be convex");
                foreach (var p in points) Assert.True(Vector3.Dot(p - a, n) < 1e-4f, $"point {p} outside face");
            }
        }

        [Fact]
        public void CubeHullIsTheCube()
        {
            var cube = Primitives.CreateCube();
            var hull = ConvexHull.Build(cube.Vertices.Select(v => v.Position))!;
            Assert.Equal(12, hull.Faces.Count);
            Assert.Equal(8, hull.Vertices.Select(v => MeshOperators.Key(v.Position)).Distinct().Count());
            AssertConvexAndEnclosing(hull, cube.Vertices.Select(v => v.Position));
        }

        [Fact]
        public void ConcaveShapeGetsAConvexEnvelope()
        {
            // An L-shape: two boxes. Hull must enclose every point and be convex.
            var points = new List<Vector3>();
            foreach (var v in Primitives.CreateCube().Vertices) points.Add(v.Position);
            foreach (var v in Primitives.CreateCube().Vertices) points.Add(v.Position + new Vector3(1, 0, 0));
            foreach (var v in Primitives.CreateCube().Vertices) points.Add(v.Position + new Vector3(0, 1, 0));
            var hull = ConvexHull.Build(points)!;
            AssertConvexAndEnclosing(hull, points);
        }

        [Fact]
        public void VertexLimitIsRespected()
        {
            var sphere = Primitives.CreateSphere(1f, 32, 48);
            var hull = ConvexHull.Build(sphere.Vertices.Select(v => v.Position), maxVertices: 32)!;
            var corners = hull.Vertices.Select(v => MeshOperators.Key(v.Position)).Distinct().Count();
            Assert.InRange(corners, 8, 32);
            AssertConvexAndEnclosing(hull, hull.Vertices.Select(v => v.Position));
            // Still a good fit: every hull vertex lies on the sphere.
            Assert.All(hull.Vertices, v => Assert.InRange(v.Position.Length(), 0.99f, 1.01f));
        }

        [Fact]
        public void FlatInputHasNoHull()
        {
            var plane = Primitives.CreatePlane();
            Assert.Null(ConvexHull.Build(plane.Vertices.Select(v => v.Position)));
        }
    }
}
