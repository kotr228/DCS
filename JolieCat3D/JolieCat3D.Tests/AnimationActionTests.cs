using System.Numerics;
using System.Text.Json;
using JolieCat3D.Core.Geometry;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Export;
using JolieCat3D.Service.Project;

namespace JolieCat3D.Tests
{
    public sealed class AnimationActionTests
    {
        private static (Scene3D Scene, Node Node, AnimationTimeline Timeline) TwoClips()
        {
            var scene = new Scene3D();
            var node = new Node("Hero") { Mesh = Primitives.CreateCube() };
            scene.AddRootNode(node);
            var timeline = new AnimationTimeline();
            timeline.ActiveAction.Name = "Idle";
            var idle = timeline.GetOrCreateTrack(node);
            idle.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One, InterpolationMode.Linear);
            idle.AddKeyframe(1, new Vector3(0, 0.1f, 0), Quaternion.Identity, Vector3.One, InterpolationMode.Linear);

            timeline.ActiveAction = timeline.AddAction("Run");
            var run = timeline.GetOrCreateTrack(node);
            run.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One, InterpolationMode.Linear);
            run.AddKeyframe(1, new Vector3(5, 0, 0), Quaternion.Identity, Vector3.One, InterpolationMode.Linear);
            return (scene, node, timeline);
        }

        [Fact]
        public void ActionsKeepSeparateTracks()
        {
            var (_, node, timeline) = TwoClips();
            Assert.Equal(2, timeline.Actions.Count);
            timeline.SetTime(1);
            timeline.Apply();
            Assert.Equal(5f, node.LocalPosition.X, 3);

            timeline.ActiveAction = timeline.Actions[0];
            timeline.Apply();
            Assert.Equal(0f, node.LocalPosition.X, 3);
            Assert.Equal(0.1f, node.LocalPosition.Y, 3);
        }

        [Fact]
        public void UniqueNamesAndLastActionProtected()
        {
            var timeline = new AnimationTimeline();
            var copy = timeline.AddAction("Action");
            Assert.Equal("Action.001", copy.Name);
            Assert.True(timeline.RemoveAction(copy));
            Assert.False(timeline.RemoveAction(timeline.ActiveAction));
            Assert.Single(timeline.Actions);
        }

        [Fact]
        public void ActionsSurviveProjectSaveAndLoad()
        {
            var (scene, _, timeline) = TwoClips();
            var data = Jolie3DProjectSerializer.BuildData(scene, timeline);
            var json = JsonSerializer.Serialize(data);
            var (loadedScene, loaded) = Jolie3DProjectSerializer.FromData(JsonSerializer.Deserialize<ProjectFileData>(json)!);

            Assert.Equal(new[] { "Idle", "Run" }, loaded.Actions.Select(a => a.Name));
            Assert.Equal("Run", loaded.ActiveAction.Name);
            var hero = loadedScene.RootNodes[0];
            Assert.True(loaded.Actions[0].TryGetTrack(hero, out var idle));
            Assert.Equal(0.1f, idle!.Curve(AnimationChannel.LocationY).Evaluate(1)!.Value, 3);
            Assert.True(loaded.Actions[1].TryGetTrack(hero, out var run));
            Assert.Equal(5f, run!.Curve(AnimationChannel.LocationX).Evaluate(1)!.Value, 3);
        }

        [Fact]
        public void GltfExportWritesOneAnimationPerAction()
        {
            var (scene, _, timeline) = TwoClips();
            var glb = Path.Combine(Path.GetTempPath(), "jc3d-actions-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                GltfExporter.Export(scene, timeline, glb);
                var model = SharpGLTF.Schema2.ModelRoot.Load(glb);
                Assert.Equal(new[] { "Idle", "Run" }, model.LogicalAnimations.Select(a => a.Name).OrderBy(n => n));
            }
            finally
            {
                File.Delete(glb);
            }
        }

        [Fact]
        public void GltfImportRestoresEveryActionAsItsOwnClip()
        {
            var (scene, _, timeline) = TwoClips();
            var glb = Path.Combine(Path.GetTempPath(), "jc3d-actions-in-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                GltfExporter.Export(scene, timeline, glb);
                var (loadedScene, loaded) = JolieCat3D.Service.MeshFileService.ImportSceneWithAnimation(glb);
                Assert.NotNull(loaded);
                Assert.Equal(new[] { "Idle", "Run" }, loaded!.Actions.Select(a => a.Name).OrderBy(n => n));
                var hero = loadedScene.Traverse().First(n => n.Name == "Hero");
                var run = loaded.Actions.First(a => a.Name == "Run");
                Assert.True(run.TryGetTrack(hero, out var track));
                Assert.Equal(5f, track!.Evaluate(1)!.Value.Position.X, 3);
                Assert.Equal(2.5f, track.Evaluate(0.5)!.Value.Position.X, 3);
            }
            finally
            {
                File.Delete(glb);
            }
        }

        [Fact]
        public void MergeActionsCommandUndoRemovesImportedTracks()
        {
            var (_, node, source) = TwoClips();
            var target = new AnimationTimeline();
            var command = new MergeActionsCommand(() => target, source);
            command.Execute();
            Assert.Equal(new[] { "Idle", "Run" }, target.Actions.Select(a => a.Name));
            Assert.True(target.Actions[1].TryGetTrack(node, out _));
            command.Undo();
            Assert.Single(target.Actions);
            Assert.Equal("Action", target.Actions[0].Name);
            Assert.Empty(target.Actions[0].Tracks);
        }

        [Fact]
        public void DeletingNodeRemovesAndRestoresTracksInEveryAction()
        {
            var (scene, node, timeline) = TwoClips();
            var command = DeleteNodeCommandFactory.Create(scene, node, timeline);
            command.Execute();
            Assert.All(timeline.Actions, a => Assert.False(a.TryGetTrack(node, out _)));
            command.Undo();
            Assert.All(timeline.Actions, a => Assert.True(a.TryGetTrack(node, out _)));
        }

        [Fact]
        public void KeyframeUndoTargetsTheEditedActionEvenAfterSwitching()
        {
            var (_, node, timeline) = TwoClips();
            var before = KeyframeTrackCommand.Snapshot(timeline, node);
            timeline.GetOrCreateTrack(node).AddKeyframe(2, new Vector3(9, 0, 0), Quaternion.Identity, Vector3.One, InterpolationMode.Linear);
            var after = KeyframeTrackCommand.Snapshot(timeline, node);
            var command = new KeyframeTrackCommand(() => timeline, node, before, after, "Key");

            timeline.ActiveAction = timeline.Actions[0]; // switch to Idle before undoing
            command.Undo();
            Assert.True(timeline.Actions[1].TryGetTrack(node, out var run));
            Assert.Equal(2, run!.Curve(AnimationChannel.LocationX).Keys.Count);
            Assert.True(timeline.Actions[0].TryGetTrack(node, out var idle));
            Assert.Equal(2, idle!.Curve(AnimationChannel.LocationX).Keys.Count);
        }

        [Fact]
        public void ActionListCommandUndoRedo()
        {
            var timeline = new AnimationTimeline();
            var first = timeline.ActiveAction;
            var action = new AnimationAction("Jump");
            var command = new ActionListCommand(() => timeline, action, isAdd: true);
            command.Execute();
            Assert.Same(action, timeline.ActiveAction);
            command.Undo();
            Assert.Single(timeline.Actions);
            Assert.Same(first, timeline.ActiveAction);
        }

        [Fact]
        public void MotionPathFollowsTheKeysAndRestoresTheScene()
        {
            var (scene, node, timeline) = TwoClips(); // active "Run": x 0 -> 5 over 1 s
            timeline.FrameRate = 10;
            node.LocalPosition = new Vector3(0, 7, 0); // un-keyed edit must survive
            var path = MotionPath.Compute(scene, timeline, node, 0, 10);
            Assert.Equal(11, path.Positions.Count);
            Assert.Equal(0f, path.Positions[0].X, 3);
            Assert.Equal(2.5f, path.Positions[5].X, 3);
            Assert.Equal(5f, path.Positions[10].X, 3);
            Assert.Equal(new[] { 0, 10 }, path.KeyFrames.OrderBy(f => f));
            Assert.Equal(new Vector3(0, 7, 0), node.LocalPosition);
        }
    }
}
