using System.Numerics;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;
using JolieCat3D.Service.Animation;

namespace JolieCat3D.Tests
{
    public sealed class AnimationChannelTests
    {
        private static Keyframe Key(double t) =>
            new(t, new Vector3(1, 2, 3), EulerAngles.FromDegrees(10, 20, 30), new Vector3(4, 5, 6));

        [Theory]
        [InlineData(AnimationChannel.LocationX, 1f)]
        [InlineData(AnimationChannel.LocationZ, 3f)]
        [InlineData(AnimationChannel.ScaleY, 5f)]
        [InlineData(AnimationChannel.RotationY, 20f)]
        public void ReadsEachChannel(AnimationChannel channel, float expected) =>
            Assert.Equal(expected, AnimationChannels.GetValue(Key(0), channel), 2);

        [Fact]
        public void EditingOneChannelLeavesTheOthersAlone()
        {
            var edited = AnimationChannels.WithValue(Key(0), AnimationChannel.RotationZ, 75f);
            Assert.Equal(75f, AnimationChannels.GetValue(edited, AnimationChannel.RotationZ), 2);
            Assert.Equal(10f, AnimationChannels.GetValue(edited, AnimationChannel.RotationX), 2);
            Assert.Equal(20f, AnimationChannels.GetValue(edited, AnimationChannel.RotationY), 2);
            Assert.Equal(new Vector3(1, 2, 3), edited.Position);

            var moved = AnimationChannels.WithValue(Key(0), AnimationChannel.LocationY, -7f);
            Assert.Equal(new Vector3(1, -7, 3), moved.Position);
            Assert.Equal(Key(0).Rotation, moved.Rotation);
        }

        [Fact]
        public void EvaluatesBetweenKeys()
        {
            var timeline = new AnimationTimeline();
            var node = new Node("N");
            AnimationChannels.ApplyKeyframes(timeline, node, new[]
            {
                new Keyframe(0, Vector3.Zero, Quaternion.Identity, Vector3.One),
                new Keyframe(2, new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One),
            });
            Assert.True(timeline.TryGetTrack(node, out var track));
            Assert.Equal(5f, AnimationChannels.Evaluate(track!, AnimationChannel.LocationX, 1.0)!.Value, 3);
        }

        [Fact]
        public void ApplyKeyframesSortsAndEmptyRemovesTheTrack()
        {
            var timeline = new AnimationTimeline();
            var node = new Node("N");
            AnimationChannels.ApplyKeyframes(timeline, node, new[] { Key(2), Key(0), Key(1) });
            Assert.True(timeline.TryGetTrack(node, out var track));
            Assert.Equal(new[] { 0.0, 1.0, 2.0 }, track!.Keyframes.Select(k => k.Time));

            AnimationChannels.ApplyKeyframes(timeline, node, Array.Empty<Keyframe>());
            Assert.False(timeline.TryGetTrack(node, out _));
        }
    }
}
