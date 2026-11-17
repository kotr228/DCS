using System.Numerics;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;
using JolieCat3D.Service.Commands;
using JolieCat3D.Service.Project;

namespace JolieCat3D.Tests
{
    public sealed class FCurveTests
    {
        private static FCurve Curve(InterpolationMode mode, params (double T, float V)[] keys)
        {
            var c = new FCurve();
            foreach (var (t, v) in keys) c.Insert(t, v, mode);
            return c;
        }

        [Fact]
        public void LinearAndConstantInterpolation()
        {
            Assert.Equal(5f, Curve(InterpolationMode.Linear, (0, 0), (2, 10)).Evaluate(1)!.Value, 4);
            Assert.Equal(0f, Curve(InterpolationMode.Constant, (0, 0), (2, 10)).Evaluate(1.9)!.Value, 4);
            Assert.Equal(10f, Curve(InterpolationMode.Constant, (0, 0), (2, 10)).Evaluate(2)!.Value, 4);
        }

        [Fact]
        public void AutoClampedBezierNeverOvershootsKeys()
        {
            var c = Curve(InterpolationMode.Bezier, (0, 0), (1, 10), (2, 0), (3, 10));
            for (var t = 0.0; t <= 3.0; t += 0.01)
                Assert.InRange(c.Evaluate(t)!.Value, -1e-3f, 10.001f);
            // Smooth: the middle of a rising segment is the midpoint by symmetry.
            Assert.Equal(5f, c.Evaluate(0.5)!.Value, 2);
        }

        [Fact]
        public void EditedHandleShapesTheCurve()
        {
            var c = Curve(InterpolationMode.Bezier, (0, 0), (1, 0));
            Assert.Equal(0f, c.Evaluate(0.5)!.Value, 4);
            var first = c.Keys[0];
            first.AutoHandles = false;
            first.HandleRight = new Vector2(0.3f, 3f);
            Assert.True(c.Evaluate(0.3)!.Value > 0.5f);
            c.UpdateAutoHandles(); // manual handles survive auto updates
            Assert.Equal(new Vector2(0.3f, 3f), c.Keys[0].HandleRight);
        }

        [Fact]
        public void ChannelsHaveIndependentKeys()
        {
            var node = new Node("N");
            var track = new AnimationTrack(node);
            track.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One);
            track.AddKeyframe(1, new Vector3(4, 0, 0), Quaternion.Identity, Vector3.One);

            var xCurve = track.Curve(AnimationChannel.LocationX);
            xCurve.Retime(xCurve.Keys[1], 2.0);

            Assert.Equal(2.0, track.Curve(AnimationChannel.LocationX).Keys[1].Time);
            Assert.Equal(1.0, track.Curve(AnimationChannel.LocationY).Keys[1].Time);
            Assert.Equal(new[] { 0.0, 1.0, 2.0 }, track.Keyframes.Select(k => k.Time));
        }

        [Fact]
        public void RotationKeysAreUnwrappedToAvoidFullTurnJumps()
        {
            var node = new Node("N");
            var track = new AnimationTrack(node);
            track.AddKeyframe(0, Vector3.Zero, EulerAngles.FromDegrees(0, 170, 0), Vector3.One);
            track.AddKeyframe(1, Vector3.Zero, EulerAngles.FromDegrees(0, -170, 0), Vector3.One); // = 190°
            var keys = track.Curve(AnimationChannel.RotationY).Keys;
            Assert.True(MathF.Abs(keys[1].Value - keys[0].Value) < 30f, $"{keys[0].Value} -> {keys[1].Value}");
        }

        [Fact]
        public void HandlesSurviveProjectSaveAndLoad()
        {
            var scene = new Scene3D();
            var node = new Node("Box");
            scene.AddRootNode(node);
            var timeline = new AnimationTimeline();
            var track = timeline.GetOrCreateTrack(node);
            track.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One, InterpolationMode.Bezier);
            track.AddKeyframe(1, Vector3.One, Quaternion.Identity, Vector3.One, InterpolationMode.Bezier);
            var key = track.Curve(AnimationChannel.LocationY).Keys[0];
            key.AutoHandles = false;
            key.HandleRight = new Vector2(0.25f, 2f);
            track.Curve(AnimationChannel.LocationZ).Keys[1].Interpolation = InterpolationMode.Constant;

            var data = Jolie3DProjectSerializer.BuildData(scene, timeline);
            var (loadedScene, loadedTimeline) = Jolie3DProjectSerializer.FromData(System.Text.Json.JsonSerializer.Deserialize<ProjectFileData>(System.Text.Json.JsonSerializer.Serialize(data))!);

            Assert.True(loadedTimeline.TryGetTrack(loadedScene.RootNodes[0], out var loaded));
            var loadedKey = loaded!.Curve(AnimationChannel.LocationY).Keys[0];
            Assert.False(loadedKey.AutoHandles);
            Assert.Equal(new Vector2(0.25f, 2f), loadedKey.HandleRight);
            Assert.Equal(InterpolationMode.Constant, loaded.Curve(AnimationChannel.LocationZ).Keys[1].Interpolation);
        }

        [Fact]
        public void UndoRestoresHandles()
        {
            var timeline = new AnimationTimeline();
            var node = new Node("N");
            var track = timeline.GetOrCreateTrack(node);
            track.AddKeyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One, InterpolationMode.Bezier);
            track.AddKeyframe(1, Vector3.One, Quaternion.Identity, Vector3.One, InterpolationMode.Bezier);
            var before = KeyframeTrackCommand.Snapshot(timeline, node);

            var key = track.Curve(AnimationChannel.LocationX).Keys[0];
            key.AutoHandles = false;
            key.HandleRight = new Vector2(0.4f, 5f);
            var after = KeyframeTrackCommand.Snapshot(timeline, node);
            var command = new KeyframeTrackCommand(() => timeline, node, before, after, "Edit Handle");

            command.Undo();
            Assert.True(timeline.TryGetTrack(node, out var restored));
            Assert.True(restored!.Curve(AnimationChannel.LocationX).Keys[0].AutoHandles);
            command.Execute();
            timeline.TryGetTrack(node, out restored);
            Assert.Equal(new Vector2(0.4f, 5f), restored!.Curve(AnimationChannel.LocationX).Keys[0].HandleRight);
        }
    }
}
