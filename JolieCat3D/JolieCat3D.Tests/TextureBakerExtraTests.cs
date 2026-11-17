using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;

namespace JolieCat3D.Tests
{
    public sealed class TextureBakerExtraTests
    {
        [Fact]
        public void CurvatureLightsConvexEdgesAndLeavesFacesGrey()
        {
            var cube = Primitives.CreateCube(1f);
            cube.Unwrap();
            var map = TextureBakerExtras.BakeCurvature(cube, 64, width: 0.1f);
            var values = map.Pixels.Select(p => p.R).ToList();
            Assert.Contains(values, v => v > 0.8f);                          // edges
            Assert.True(values.Count(v => MathF.Abs(v - 0.5f) < 0.01f) > values.Count / 4, "face centres stay grey");
            Assert.DoesNotContain(values, v => v < 0.45f);                   // no concave edges on a cube
        }

        [Fact]
        public void IdMapGivesSeparatePartsDifferentColours()
        {
            var mesh = new Mesh("Two");
            // Two separate quads side by side in UV space.
            void Quad(float x0, float u0)
            {
                var i = mesh.AddVertex(new Vertex(new Vector3(x0, 0, 0), Vector3.UnitY, new Vector2(u0, 0)));
                mesh.AddVertex(new Vertex(new Vector3(x0 + 1, 0, 0), Vector3.UnitY, new Vector2(u0 + 0.4f, 0)));
                mesh.AddVertex(new Vertex(new Vector3(x0 + 1, 0, 1), Vector3.UnitY, new Vector2(u0 + 0.4f, 1)));
                mesh.AddVertex(new Vertex(new Vector3(x0, 0, 1), Vector3.UnitY, new Vector2(u0, 1)));
                mesh.AddQuad(i, i + 1, i + 2, i + 3);
            }
            Quad(0, 0.05f);
            Quad(5, 0.55f);
            var map = TextureBakerExtras.BakeId(mesh, 32);
            var left = map.GetPixel(6, 16);
            var right = map.GetPixel(25, 16);
            Assert.NotEqual(left, right);
            Assert.True(left.R + left.G + left.B > 0.5f && right.R + right.G + right.B > 0.5f);
        }
    }
}
