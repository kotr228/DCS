using System.Numerics;

namespace JolieCat3D.Service.Animation
{
    /// <summary>One recorded Position/Rotation/Scale at a single point in time (seconds,
    /// not frames - <see cref="AnimationTimeline"/>'s own FPS is only a display/scrubber
    /// convenience, never baked into a keyframe's own storage) on an
    /// <see cref="AnimationTrack"/> - the fundamental unit a 3D animation timeline
    /// interpolates between, the same way a 2D animation tool's own timeline keyframes a
    /// layer's properties.</summary>
    public readonly record struct Keyframe(double Time, Vector3 Position, Quaternion Rotation, Vector3 Scale);
}
