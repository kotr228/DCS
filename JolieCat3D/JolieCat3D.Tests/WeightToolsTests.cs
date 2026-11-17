using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Skinning;

namespace JolieCat3D.Tests
{
    public sealed class WeightToolsTests
    {
        [Theory]
        [InlineData("Arm.L", "Arm.R")]
        [InlineData("hand_r", "hand_l")]
        [InlineData("L_Foot", "R_Foot")]
        [InlineData("LeftUpLeg", "RightUpLeg")]
        [InlineData("mixamorig:RightHand", "mixamorig:LeftHand")]
        [InlineData("Spine", null)]
        public void BoneNamesMirror(string name, string? expected) => Assert.Equal(expected, WeightTools.MirrorName(name));

        [Fact]
        public void MirrorCopiesWeightsWithTwinBones()
        {
            var bones = new[] { new Node("Spine"), new Node("Arm.L"), new Node("Arm.R") };
            var mesh = new Mesh("M");
            var left = mesh.AddVertex(new Vertex(new Vector3(1, 0, 0), Vector3.UnitY, Vector2.Zero, default, new BoneIndices(1, 0, 0, 0), new Vector4(0.75f, 0.25f, 0, 0)));
            var right = mesh.AddVertex(new Vertex(new Vector3(-1, 0, 0), Vector3.UnitY, Vector2.Zero));
            mesh.AddVertex(new Vertex(new Vector3(0, 1, 0), Vector3.UnitY, Vector2.Zero));
            mesh.AddTriangle(0, 1, 2);

            Assert.Equal(1, WeightTools.MirrorX(mesh, bones));
            var v = mesh.Vertices[right];
            Assert.Equal(2, v.BoneIndices.X);       // Arm.L -> Arm.R
            Assert.Equal(0.75f, v.BoneWeights.X, 4);
            Assert.Equal(0, v.BoneIndices.Y);       // Spine stays Spine
            Assert.Equal(0.25f, v.BoneWeights.Y, 4);
            Assert.Equal(1, mesh.Vertices[left].BoneIndices.X); // source untouched
        }

        [Fact]
        public void SmoothBlendsAHardBoundaryAndNormalizeFixesSums()
        {
            var plane = Primitives.CreatePlane(4, 1, widthSegments: 8, depthSegments: 1);
            for (var i = 0; i < plane.Vertices.Count; i++)
                plane.SetVertexBoneWeights(i, new BoneIndices(plane.Vertices[i].Position.X < 0 ? 0 : 1, 0, 0, 0), new Vector4(1, 0, 0, 0));
            WeightTools.Smooth(plane, iterations: 3);
            // Vertices next to the old boundary now mix both bones; all sums are 1.
            var nearMiddle = plane.Vertices.Where(v => MathF.Abs(v.Position.X + 0.25f) < 0.3f).ToList(); // the two columns either side of the boundary
            Assert.All(nearMiddle, v => Assert.True(v.BoneWeights.Y > 0.05f, $"{v.Position} {v.BoneWeights}"));
            Assert.All(plane.Vertices, v => Assert.Equal(1f, v.BoneWeights.X + v.BoneWeights.Y + v.BoneWeights.Z + v.BoneWeights.W, 3));

            plane.SetVertexBoneWeights(0, new BoneIndices(0, 1, 0, 0), new Vector4(0.2f, 0.2f, 0, 0));
            Assert.Equal(1, WeightTools.Normalize(plane));
            Assert.Equal(0.5f, plane.Vertices[0].BoneWeights.X, 4);
        }
    }
}
