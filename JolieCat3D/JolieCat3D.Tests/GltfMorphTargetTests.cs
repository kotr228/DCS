using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Export;

namespace JolieCat3D.Tests
{
    public sealed class GltfMorphTargetTests
    {
        [Fact]
        public void ShapeKeysAndTheirAnimationRoundTripAsMorphTargets()
        {
            var scene = new Scene3D();
            var mesh = Primitives.CreateCube();
            ShapeKeys.AddBasis(mesh);
            for (var i = 0; i < mesh.Vertices.Count; i++)
                if (mesh.Vertices[i].Position.Y > 0) mesh.SetVertexPosition(i, mesh.Vertices[i].Position + new Vector3(0, 0.5f, 0));
            var key = ShapeKeys.SaveCurrentAsShapeKey(mesh, "Stretch")!;
            key.Value = 0.25f;
            var node = new Node("Face") { Mesh = mesh };
            scene.AddRootNode(node);

            var timeline = new AnimationTimeline();
            timeline.ActiveAction.Name = "Talk";
            var curve = timeline.GetOrCreateTrack(node).PropertyCurve(AnimationTrack.ShapeKeyPrefix + "Stretch");
            curve.Insert(0, 0f, InterpolationMode.Linear);
            curve.Insert(1, 1f, InterpolationMode.Linear);

            var glb = Path.Combine(Path.GetTempPath(), "jc3d-morph-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                GltfExporter.Export(scene, timeline, glb);
                var (loaded, loadedTimeline) = MeshFileService.ImportSceneWithAnimation(glb);

                var face = loaded.Traverse().First(n => n.Mesh is { ShapeKeys.Count: > 0 });
                var loadedKey = Assert.Single(face.Mesh!.ShapeKeys);
                Assert.Equal("Stretch", loadedKey.Name);
                Assert.Equal(0.25f, loadedKey.Value, 3);
                var basis = face.Mesh.ShapeBasis!;
                for (var i = 0; i < basis.Length; i++)
                {
                    var expected = basis[i].Y > 0 ? 0.5f : 0f;
                    Assert.Equal(expected, loadedKey.Positions[i].Y - basis[i].Y, 3);
                }

                Assert.NotNull(loadedTimeline);
                var talk = loadedTimeline!.Actions.First(a => a.Name == "Talk");
                Assert.True(talk.TryGetTrack(face, out var track));
                var loadedCurve = track!.PropertyCurves[AnimationTrack.ShapeKeyPrefix + "Stretch"];
                Assert.Equal(0.5f, loadedCurve.Evaluate(0.5)!.Value, 2);
            }
            finally
            {
                File.Delete(glb);
            }
        }
    }
}
