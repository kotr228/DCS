using System.Numerics;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>The nine F-curve channels the Graph Editor shows for one transform track.</summary>
    public enum AnimationChannel
    {
        LocationX, LocationY, LocationZ,
        RotationX, RotationY, RotationZ,   // Euler degrees
        ScaleX, ScaleY, ScaleZ,
    }

    /// <summary>
    /// Splits whole-transform <see cref="Keyframe"/>s into per-axis channel values (and back),
    /// so a Graph Editor can show and edit one curve at a time. Rotation channels are Euler
    /// degrees converted to/from the stored quaternion.
    /// </summary>
    public static class AnimationChannels
    {
        public static readonly AnimationChannel[] All = Enum.GetValues<AnimationChannel>();

        public static string DisplayName(AnimationChannel channel) => channel switch
        {
            AnimationChannel.LocationX => "X Location",
            AnimationChannel.LocationY => "Y Location",
            AnimationChannel.LocationZ => "Z Location",
            AnimationChannel.RotationX => "X Rotation",
            AnimationChannel.RotationY => "Y Rotation",
            AnimationChannel.RotationZ => "Z Rotation",
            AnimationChannel.ScaleX => "X Scale",
            AnimationChannel.ScaleY => "Y Scale",
            _ => "Z Scale",
        };

        /// <summary>0 = X, 1 = Y, 2 = Z (for colouring like Blender).</summary>
        public static int Axis(AnimationChannel channel) => (int)channel % 3;

        public static float GetValue(Keyframe key, AnimationChannel channel) =>
            GetValue(key.Position, key.Rotation, key.Scale, channel);

        public static float GetValue(Vector3 position, Quaternion rotation, Vector3 scale, AnimationChannel channel)
        {
            switch (channel)
            {
                case AnimationChannel.LocationX: return position.X;
                case AnimationChannel.LocationY: return position.Y;
                case AnimationChannel.LocationZ: return position.Z;
                case AnimationChannel.ScaleX: return scale.X;
                case AnimationChannel.ScaleY: return scale.Y;
                case AnimationChannel.ScaleZ: return scale.Z;
                default:
                    var e = EulerAngles.ToDegrees(rotation);
                    return channel == AnimationChannel.RotationX ? e.X : channel == AnimationChannel.RotationY ? e.Y : e.Z;
            }
        }

        public static Keyframe WithValue(Keyframe key, AnimationChannel channel, float value)
        {
            var p = key.Position;
            var s = key.Scale;
            var r = key.Rotation;
            switch (channel)
            {
                case AnimationChannel.LocationX: p.X = value; break;
                case AnimationChannel.LocationY: p.Y = value; break;
                case AnimationChannel.LocationZ: p.Z = value; break;
                case AnimationChannel.ScaleX: s.X = value; break;
                case AnimationChannel.ScaleY: s.Y = value; break;
                case AnimationChannel.ScaleZ: s.Z = value; break;
                default:
                    var e = EulerAngles.ToDegrees(r);
                    if (channel == AnimationChannel.RotationX) e.X = value;
                    else if (channel == AnimationChannel.RotationY) e.Y = value;
                    else e.Z = value;
                    r = EulerAngles.FromDegrees(e);
                    break;
            }
            return key with { Position = p, Rotation = r, Scale = s };
        }

        /// <summary>The channel's animated value at <paramref name="time"/> (seconds).</summary>
        public static float? Evaluate(AnimationTrack track, AnimationChannel channel, double time) =>
            track.Curve(channel).Evaluate(time);

        /// <summary>Replaces a node's whole keyframe list (removes the track when empty).</summary>
        public static void ApplyKeyframes(AnimationTimeline timeline, Node node, IReadOnlyList<Keyframe> keyframes)
        {
            timeline.RemoveTrack(node);
            if (keyframes.Count == 0) return;
            var track = timeline.GetOrCreateTrack(node);
            foreach (var k in keyframes.OrderBy(k => k.Time))
                track.AddKeyframe(k.Time, k.Position, k.Rotation, k.Scale, k.Interpolation);
        }
    }
}
