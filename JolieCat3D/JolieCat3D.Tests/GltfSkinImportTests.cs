using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Core.Skinning;
using JolieCat3D.Service;
using JolieCat3D.Service.Export;

namespace JolieCat3D.Tests
{
    public sealed class GltfSkinImportTests
    {
        private static Scene3D BuildRig(Vector3 bodyOffset = default)
        {
            var scene = new Scene3D();
            var armature = new Node("Rig") { Armature = new ArmatureData() };
            scene.AddRootNode(armature);
            var root = new Node("Root") { Bone = new BoneData { Tail = new Vector3(0, 1, 0) } };
            armature.AddChild(root);
            var tip = new Node("Tip") { Bone = new BoneData { Tail = new Vector3(0, 1, 0) }, LocalPosition = new Vector3(0, 1, 0) };
            root.AddChild(tip);
            foreach (var bone in new[] { root, tip })
            {
                bone.Mesh = bone.Bone!.GenerateMesh();
                bone.Bone.CaptureRestPose(bone);
            }

            var mesh = Primitives.CreateCube();
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                mesh.SetVertexPosition(i, new Vector3(v.Position.X * 0.2f, v.Position.Y + 1f, v.Position.Z * 0.2f));
                var upper = mesh.Vertices[i].Position.Y > 1f;
                mesh.SetVertexBoneWeights(i, new BoneIndices(upper ? 1 : 0, 0, 0, 0), new Vector4(1, 0, 0, 0));
            }
            for (var i = 0; i < mesh.Vertices.Count; i++) mesh.SetVertexPosition(i, mesh.Vertices[i].Position - bodyOffset);
            var body = new Node("Body") { Mesh = mesh, LocalPosition = bodyOffset };
            body.SkinBinding = new SkinBinding { Armature = armature };
            body.SkinBinding.Bones.Add(root);
            body.SkinBinding.Bones.Add(tip);
            scene.AddRootNode(body);
            return scene;
        }

        private static List<Vector3> DeformedWorld(Scene3D scene)
        {
            var body = scene.Traverse().First(n => n.SkinBinding is not null && n.Mesh is not null);
            var world = body.GetWorldTransform();
            var deformed = SkinningEvaluator.Deform(body.Mesh!, world, body.SkinBinding);
            return deformed.Vertices.Select(v => Vector3.Transform(v.Position, world)).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToList();
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(2f)]
        public void SkinnedRigRoundTripsAndDeformsTheSame(float offset)
        {
            var scene = BuildRig(new Vector3(offset, 0.5f * offset, 0));
            var glb = Path.Combine(Path.GetTempPath(), "jc3d-skin-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                GltfExporter.Export(scene, null, glb);
                var (loaded, _) = MeshFileService.ImportSceneWithAnimation(glb);

                var body = loaded.Traverse().First(n => n.SkinBinding is not null && n.Mesh is not null);
                Assert.Equal(2, body.SkinBinding!.Bones.Count);
                Assert.All(body.SkinBinding.Bones, b => Assert.NotNull(b.Bone));

                // Rest pose: mesh stays where it was.
                AssertClose(DeformedWorld(scene), DeformedWorld(loaded));

                // Bend the tip bone in both scenes: the upper half follows identically.
                var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.8f);
                scene.Traverse().First(n => n.Name == "Tip").LocalRotation = bend;
                loaded.Traverse().First(n => n.Name == "Tip").LocalRotation = bend;
                AssertClose(DeformedWorld(scene), DeformedWorld(loaded));
            }
            finally
            {
                File.Delete(glb);
            }
        }

        private static void AssertClose(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
                Assert.True(Vector3.Distance(expected[i], actual[i]) < 1e-3f, $"vertex {i}: {expected[i]} vs {actual[i]}");
        }
    }
}
