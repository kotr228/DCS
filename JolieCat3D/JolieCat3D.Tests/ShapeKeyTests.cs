using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Project;

namespace JolieCat3D.Tests
{
    public sealed class ShapeKeyTests
    {
        private static (Mesh Mesh, ShapeKey Key) CubeWithRaisedTop()
        {
            var mesh = Primitives.CreateCube();
            ShapeKeys.AddBasis(mesh);
            for (var i = 0; i < mesh.Vertices.Count; i++)
                if (mesh.Vertices[i].Position.Y > 0) mesh.SetVertexPosition(i, mesh.Vertices[i].Position + new Vector3(0, 1, 0));
            var key = ShapeKeys.SaveCurrentAsShapeKey(mesh, "Tall")!;
            return (mesh, key);
        }

        [Fact]
        public void SavingAKeyRestoresTheBasis()
        {
            var (mesh, key) = CubeWithRaisedTop();
            Assert.NotNull(key);
            Assert.Equal(0.5f, mesh.Vertices.Max(v => v.Position.Y), 4);
            Assert.Equal(1.5f, key.Positions.Max(p => p.Y), 4);
        }

        [Fact]
        public void ValueBlendsBetweenBasisAndKey()
        {
            var (mesh, key) = CubeWithRaisedTop();
            Assert.Same(mesh, ShapeKeys.Evaluate(mesh)); // value 0: nothing to blend
            key.Value = 0.5f;
            var blended = ShapeKeys.Evaluate(mesh);
            Assert.Equal(1.0f, blended.Vertices.Max(v => v.Position.Y), 4);
            Assert.Equal(0.5f, mesh.Vertices.Max(v => v.Position.Y), 4); // source untouched
        }

        [Fact]
        public void TopologyChangeInvalidatesKeys()
        {
            var (mesh, key) = CubeWithRaisedTop();
            key.Value = 1f;
            mesh.AddVertex(new Vertex(new Vector3(9, 9, 9)));
            Assert.False(ShapeKeys.IsValid(mesh, key));
            Assert.Same(mesh, ShapeKeys.Evaluate(mesh));
        }

        [Fact]
        public void TimelineAnimatesTheValue()
        {
            var (mesh, key) = CubeWithRaisedTop();
            var node = new Node("Box") { Mesh = mesh };
            var timeline = new AnimationTimeline();
            var curve = timeline.GetOrCreateTrack(node).PropertyCurve(AnimationTrack.ShapeKeyPrefix + "Tall");
            curve.Insert(0, 0f, InterpolationMode.Linear);
            curve.Insert(1, 1f, InterpolationMode.Linear);

            timeline.SetTime(0.5);
            timeline.Apply();

            Assert.Equal(0.5f, key.Value, 3);
        }

        [Fact]
        public void ShapeKeysAndTheirAnimationSurviveSaveAndLoad()
        {
            var (mesh, key) = CubeWithRaisedTop();
            key.Value = 0.25f;
            var scene = new Scene3D();
            var node = new Node("Box") { Mesh = mesh };
            scene.AddRootNode(node);
            var timeline = new AnimationTimeline();
            timeline.GetOrCreateTrack(node).PropertyCurve(AnimationTrack.ShapeKeyPrefix + "Tall").Insert(0, 0.75f, InterpolationMode.Bezier);

            var data = Jolie3DProjectSerializer.BuildData(scene, timeline);
            var (loaded, loadedTimeline) = Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<ProjectFileData>(System.Text.Json.JsonSerializer.Serialize(data))!);

            var loadedMesh = loaded.RootNodes[0].Mesh!;
            var loadedKey = Assert.Single(loadedMesh.ShapeKeys);
            Assert.Equal("Tall", loadedKey.Name);
            Assert.Equal(0.25f, loadedKey.Value);
            Assert.Equal(key.Positions, loadedKey.Positions);
            Assert.NotNull(loadedMesh.ShapeBasis);
            Assert.True(loadedTimeline.TryGetTrack(loaded.RootNodes[0], out var track));
            Assert.Equal(0.75f, track!.PropertyCurve(AnimationTrack.ShapeKeyPrefix + "Tall").Evaluate(0)!.Value);
        }

        [Fact]
        public void SliderChangeIsUndoable()
        {
            var (mesh, key) = CubeWithRaisedTop();
            var node = new Node("Box") { Mesh = mesh };
            var before = NodeState.Capture(node);
            node.Mesh!.ShapeKeys[0].Value = 0.8f;
            var after = NodeState.Capture(node, before);
            Assert.False(before.ContentEquals(after));

            new NodeStateCommand(node, before, after, "Shape Key").Undo();
            Assert.Equal(0f, node.Mesh!.ShapeKeys[0].Value);
        }
    }
}
