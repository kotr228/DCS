using System.Numerics;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Modifiers;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;

namespace JolieCat3D.Tests
{
    public sealed class UndoCommandTests
    {
        [Fact]
        public void NodeStateCommandUndoesAndRedoesMeshMaterialAndModifierEdits()
        {
            var node = new Node("N") { Mesh = Primitives.CreateCube() };
            var material = new Material("M") { Roughness = 0.5f };
            node.Mesh.Material = material;
            var originalPos = node.Mesh.Vertices[0].Position;

            var before = NodeState.Capture(node);

            node.Mesh.SetVertexPosition(0, new Vector3(9, 9, 9));
            material.Roughness = 0.9f;
            node.Modifiers.Add(new MirrorModifier());
            node.LocalPosition = new Vector3(1, 2, 3);
            node.Name = "Renamed";

            var after = NodeState.Capture(node, before);
            var history = new CommandHistory();
            history.Record(new NodeStateCommand(node, before, after, "Edit"));

            history.Undo();
            Assert.Equal(originalPos, node.Mesh!.Vertices[0].Position);
            Assert.Equal(0.5f, material.Roughness);
            Assert.Same(material, node.Mesh.Material); // identity kept for animation tracks
            Assert.Empty(node.Modifiers);
            Assert.Equal(Vector3.Zero, node.LocalPosition);
            Assert.Equal("N", node.Name);

            history.Redo();
            Assert.Equal(new Vector3(9, 9, 9), node.Mesh!.Vertices[0].Position);
            Assert.Equal(0.9f, material.Roughness);
            Assert.Single(node.Modifiers);
            Assert.Equal("Renamed", node.Name);
        }

        [Fact]
        public void IdenticalSnapshotsCompareEqualAndChangesDoNot()
        {
            var node = new Node("N") { Mesh = Primitives.CreateCube() };
            node.Modifiers.Add(new MirrorModifier());
            var a = NodeState.Capture(node);
            var b = NodeState.Capture(node, a);
            Assert.True(a.ContentEquals(b));

            ((MirrorModifier)node.Modifiers[0]).Axis = MirrorAxis.Z;
            Assert.False(a.ContentEquals(NodeState.Capture(node, a)));
        }

        [Fact]
        public void SnapshotIsNotCorruptedByLaterInPlaceEdits()
        {
            var node = new Node("N") { Mesh = Primitives.CreateCube() };
            var s0 = NodeState.Capture(node);
            node.Mesh.SetVertexPosition(0, new Vector3(1, 1, 1));
            var s1 = NodeState.Capture(node, s0);
            var history = new CommandHistory();
            history.Record(new NodeStateCommand(node, s0, s1, "Stroke 1"));

            node.Mesh!.SetVertexPosition(0, new Vector3(2, 2, 2));
            var s2 = NodeState.Capture(node, s1);
            history.Record(new NodeStateCommand(node, s1, s2, "Stroke 2"));

            history.Undo();
            history.Undo();
            history.Redo();
            Assert.Equal(new Vector3(1, 1, 1), node.Mesh!.Vertices[0].Position);

            // Editing in place after a redo must not leak into the stored snapshot.
            node.Mesh.SetVertexPosition(0, new Vector3(7, 7, 7));
            history.Undo();
            history.Redo();
            Assert.Equal(new Vector3(1, 1, 1), node.Mesh!.Vertices[0].Position);
        }

        [Fact]
        public void TexturePaintIsUndoneInPlace()
        {
            var buffer = new TextureBuffer(2, 2, new Color4(1, 1, 1));
            var material = new Material { PaintedTextureBuffer = buffer };
            var node = new Node("N") { Mesh = Primitives.CreatePlane() };
            node.Mesh.Material = material;

            var before = NodeState.Capture(node);
            buffer.SetPixel(0, 0, new Color4(1, 0, 0));
            var after = NodeState.Capture(node, before);

            new NodeStateCommand(node, before, after, "Paint").Undo();

            Assert.Same(buffer, material.PaintedTextureBuffer); // paint session keeps working
            Assert.Equal(new Color4(1, 1, 1), buffer.GetPixel(0, 0));
        }

        [Fact]
        public void UnchangedMeshIsSharedBetweenSnapshots()
        {
            var node = new Node("N") { Mesh = Primitives.CreateSphere() };
            var a = NodeState.Capture(node);
            node.LocalPosition = Vector3.One; // mesh untouched
            var b = NodeState.Capture(node, a);

            var meshBefore = node.Mesh;
            new NodeStateCommand(node, a, b, "Move").Undo();
            Assert.Same(meshBefore, node.Mesh); // hash matched -> mesh not replaced
        }

        [Fact]
        public void MeshFingerprintDetectsChanges()
        {
            var mesh = Primitives.CreateCube();
            var h0 = MeshFingerprint.Compute(mesh);
            Assert.Equal(h0, MeshFingerprint.Compute(mesh.Clone()));
            mesh.SetVertexPosition(3, new Vector3(0.1f, 0.2f, 0.3f));
            Assert.NotEqual(h0, MeshFingerprint.Compute(mesh));
        }

        [Fact]
        public void AddNodeCommandAddsAndRemoves()
        {
            var scene = new Scene3D();
            var cam = new Node("Cam") { Camera = new CameraData() };
            var history = new CommandHistory();
            history.Execute(new AddNodeCommand(scene, null, cam, "Add"));
            scene.ActiveCamera = cam;
            Assert.Single(scene.RootNodes);

            history.Undo();
            Assert.Empty(scene.RootNodes);
            Assert.Null(scene.ActiveCamera);

            history.Redo();
            Assert.Same(cam, Assert.Single(scene.RootNodes));
        }

        [Fact]
        public void ReparentCommandRestoresExactLocalTransform()
        {
            var scene = new Scene3D();
            var parent = new Node("P") { LocalPosition = new Vector3(10, 0, 0), LocalScale = new Vector3(2, 2, 2) };
            var child = new Node("C") { LocalPosition = new Vector3(1, 2, 3) };
            scene.AddRootNode(parent);
            scene.AddRootNode(child);

            var command = new ReparentNodeCommand(scene, child, parent, "Parent");
            Assert.True(command.TryExecute());
            Assert.Same(parent, child.Parent);

            command.Undo();
            Assert.Null(child.Parent);
            Assert.Equal(new Vector3(1, 2, 3), child.LocalPosition);
            Assert.Contains(child, scene.RootNodes);
        }

        [Fact]
        public void KeyframeTrackCommandRestoresKeyframes()
        {
            var timeline = new AnimationTimeline();
            var node = new Node("N");
            var before = KeyframeTrackCommand.Snapshot(timeline, node);
            timeline.GetOrCreateTrack(node).AddKeyframe(0.5, Vector3.One, Quaternion.Identity, Vector3.One);
            var after = KeyframeTrackCommand.Snapshot(timeline, node);

            var command = new KeyframeTrackCommand(() => timeline, node, before, after, "Insert Keyframe");
            command.Undo();
            Assert.False(timeline.TryGetTrack(node, out _));
            command.Execute();
            Assert.True(timeline.TryGetTrack(node, out var track));
            Assert.Equal(0.5, Assert.Single(track!.Keyframes).Time);
        }

        [Fact]
        public void HideIsUndoableAndSaved()
        {
            var scene = new Scene3D();
            var a = new Node("A");
            var b = new Node("B") { IsHidden = true };
            scene.AddRootNode(a);
            scene.AddRootNode(b);
            var hide = new SetHiddenCommand(new[] { a, b }, hidden: true);
            hide.Execute();
            Assert.True(a.IsHidden && b.IsHidden);
            hide.Undo();
            Assert.False(a.IsHidden);
            Assert.True(b.IsHidden); // restored to its own previous state

            var data = JolieCat3D.Service.Project.Jolie3DProjectSerializer.BuildData(scene, new AnimationTimeline());
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            var (loaded, _) = JolieCat3D.Service.Project.Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<JolieCat3D.Service.Project.ProjectFileData>(json)!);
            Assert.False(loaded.RootNodes[0].IsHidden);
            Assert.True(loaded.RootNodes[1].IsHidden);
        }

        [Fact]
        public void MeshEditUndoKeepsLinkedDuplicatesLinked()
        {
            var shared = Primitives.CreateCube();
            var a = new Node("A") { Mesh = shared };
            var b = new Node("B") { Mesh = shared };
            var original = shared.Vertices[0].Position;

            var edit = MeshEditCommandFactory.Capture(a, "Move", () =>
            {
                a.Mesh!.SetVertexPosition(0, new Vector3(5, 5, 5));
                return true;
            })!;
            Assert.Same(shared, a.Mesh);
            Assert.Equal(new Vector3(5, 5, 5), b.Mesh!.Vertices[0].Position);

            edit.Undo();
            Assert.Same(a.Mesh, b.Mesh);
            Assert.Equal(original, b.Mesh!.Vertices[0].Position);
            edit.Execute();
            Assert.Same(a.Mesh, b.Mesh);
            Assert.Equal(new Vector3(5, 5, 5), b.Mesh!.Vertices[0].Position);

            // An edit that swaps in a new mesh object still lands in the shared one.
            var cubeVertexCount = shared.Vertices.Count;
            var swap = MeshEditCommandFactory.Capture(b, "Swap", () =>
            {
                b.Mesh = Primitives.CreateSphere(1f, 6, 8);
                return true;
            })!;
            Assert.Same(shared, a.Mesh);
            Assert.Same(shared, b.Mesh);
            swap.Undo();
            Assert.NotEqual(cubeVertexCount, Primitives.CreateSphere(1f, 6, 8).Vertices.Count);
            Assert.Equal(cubeVertexCount, b.Mesh!.Vertices.Count);
            Assert.Same(shared, b.Mesh);

            // Node snapshots restore into the same shared object too.
            var before = NodeState.Capture(a);
            a.Mesh!.SetVertexPosition(1, new Vector3(-7, 0, 0));
            before.RestoreTo(a);
            Assert.Same(shared, a.Mesh);
            Assert.NotEqual(new Vector3(-7, 0, 0), b.Mesh!.Vertices[1].Position);
        }
    }
}
