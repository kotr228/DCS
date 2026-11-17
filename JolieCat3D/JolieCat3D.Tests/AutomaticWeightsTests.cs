using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Skinning;

namespace JolieCat3D.Tests
{
    public sealed class AutomaticWeightsTests
    {
        private static (Node Mesh, Node Armature) Arm()
        {
            // A 2-unit tall cylinder with a two-bone chain up its middle (joint at y = 1).
            var armature = new Node("Rig") { Armature = new ArmatureData() };
            var upper = new Node("Upper") { Bone = new BoneData { Tail = new Vector3(0, 1, 0) } };
            var lower = new Node("Lower") { Bone = new BoneData { Tail = new Vector3(0, 1, 0) }, LocalPosition = new Vector3(0, 1, 0) };
            armature.AddChild(upper);
            upper.AddChild(lower);
            var mesh = Primitives.CreateCylinder(0.2f, 2f, 12, capped: true);
            for (var i = 0; i < mesh.Vertices.Count; i++) mesh.SetVertexPosition(i, mesh.Vertices[i].Position + new Vector3(0, 1, 0));
            mesh.Subdivide();
            mesh.Subdivide();
            return (new Node("Arm") { Mesh = mesh }, armature);
        }

        [Fact]
        public void WeightsBlendSmoothlyAcrossTheJoint()
        {
            var (arm, rig) = Arm();
            var binding = SkinBindingFactory.CreateAutomatic(arm, rig);
            var mesh = arm.Mesh!;
            float LowerWeight(Vertex v)
            {
                var w = 0f;
                var idx = new[] { v.BoneIndices.X, v.BoneIndices.Y, v.BoneIndices.Z, v.BoneIndices.W };
                var ws = new[] { v.BoneWeights.X, v.BoneWeights.Y, v.BoneWeights.Z, v.BoneWeights.W };
                for (var k = 0; k < 4; k++) if (ReferenceEquals(binding.Bones[idx[k]], rig.Children[0].Children[0])) w += ws[k];
                return w;
            }

            foreach (var v in mesh.Vertices)
            {
                var sum = v.BoneWeights.X + v.BoneWeights.Y + v.BoneWeights.Z + v.BoneWeights.W;
                Assert.InRange(sum, 0.999f, 1.001f);
                if (v.Position.Y < 0.3f) Assert.True(LowerWeight(v) < 0.1f, $"bottom at {v.Position} follows the upper bone");
                if (v.Position.Y > 1.7f) Assert.True(LowerWeight(v) > 0.9f, $"top at {v.Position} follows the lower bone");
            }
            // Around the joint both bones share the vertex.
            var joint = mesh.Vertices.Where(v => MathF.Abs(v.Position.Y - 1f) < 0.05f).ToList();
            Assert.NotEmpty(joint);
            Assert.All(joint, v => Assert.InRange(LowerWeight(v), 0.2f, 0.8f));
        }

        [Fact]
        public void CoincidentVerticesGetIdenticalWeights()
        {
            var (arm, rig) = Arm();
            SkinBindingFactory.CreateAutomatic(arm, rig);
            foreach (var group in arm.Mesh!.Vertices.GroupBy(v => MeshOperators.Key(v.Position)))
            {
                var first = group.First();
                Assert.All(group, v => { Assert.Equal(first.BoneIndices, v.BoneIndices); Assert.Equal(first.BoneWeights, v.BoneWeights); });
            }
        }
    }
}
