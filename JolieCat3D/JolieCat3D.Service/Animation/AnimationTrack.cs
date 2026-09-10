using System.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// One <see cref="Node"/>'s own keyframed Position/Rotation/Scale over time - a
    /// sorted-by-time list of <see cref="Keyframe"/>s plus <see cref="Evaluate"/>, which
    /// interpolates between the two keyframes bracketing a given moment (linearly for
    /// Position/Scale, via <see cref="Quaternion.Slerp"/> - NOT a plain per-component
    /// lerp, which would take the geometrically wrong path between two rotations - for
    /// Rotation), clamping to the first/last keyframe's own value outside the track's
    /// recorded time range.
    /// </summary>
    public sealed class AnimationTrack
    {
        private readonly List<Keyframe> _keyframes = new();

        /// <summary>Keyframe times within this distance of each other are treated as
        /// "the same time" - <see cref="AddKeyframe"/> replaces rather than duplicates,
        /// and <see cref="RemoveKeyframe"/> matches, within this tolerance, since a
        /// frame-quantized UI scrubber (frame/FPS) rarely lands on the exact same double
        /// twice even for "the same frame".</summary>
        private const double TimeEpsilon = 1e-6;

        public Node Target { get; }

        /// <summary>Every keyframe, ordered by <see cref="Keyframe.Time"/> ascending.</summary>
        public IReadOnlyList<Keyframe> Keyframes => _keyframes;

        public AnimationTrack(Node target) => Target = target ?? throw new ArgumentNullException(nameof(target));

        /// <summary>Adds a keyframe at <paramref name="time"/> - replacing any existing
        /// one within <see cref="TimeEpsilon"/> of it (re-recording the same moment,
        /// rather than accumulating near-duplicates), keeping the list sorted by time.</summary>
        public void AddKeyframe(double time, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var keyframe = new Keyframe(time, position, rotation, scale);
            var existingIndex = _keyframes.FindIndex(k => Math.Abs(k.Time - time) < TimeEpsilon);

            if (existingIndex >= 0) { _keyframes[existingIndex] = keyframe; return; }

            var insertIndex = _keyframes.FindIndex(k => k.Time > time);
            if (insertIndex < 0) _keyframes.Add(keyframe);
            else _keyframes.Insert(insertIndex, keyframe);
        }

        /// <summary>Captures <see cref="Target"/>'s own CURRENT LocalPosition/LocalRotation/
        /// LocalScale as a new keyframe at <paramref name="time"/> - the "record a
        /// keyframe from where the object already is right now" convenience a Properties
        /// panel's own "Add Keyframe" button uses, rather than a caller needing to read
        /// and pass all three values itself.</summary>
        public void AddKeyframeFromCurrentTransform(double time) =>
            AddKeyframe(time, Target.LocalPosition, Target.LocalRotation, Target.LocalScale);

        /// <summary>Removes the keyframe within <see cref="TimeEpsilon"/> of
        /// <paramref name="time"/>, if any - a no-op if none is that close.</summary>
        public void RemoveKeyframe(double time) =>
            _keyframes.RemoveAll(k => Math.Abs(k.Time - time) < TimeEpsilon);

        /// <summary>The interpolated Position/Rotation/Scale at <paramref name="time"/> -
        /// null with no keyframes recorded at all (nothing for
        /// <see cref="AnimationTimeline.Apply"/> to apply). A single keyframe reads as a
        /// constant value at every time; <paramref name="time"/> before the first or
        /// after the last keyframe clamps to that keyframe's own value, rather than
        /// extrapolating.</summary>
        public (Vector3 Position, Quaternion Rotation, Vector3 Scale)? Evaluate(double time)
        {
            if (_keyframes.Count == 0) return null;
            if (_keyframes.Count == 1 || time <= _keyframes[0].Time)
            {
                var only = _keyframes[0];
                return (only.Position, only.Rotation, only.Scale);
            }

            var last = _keyframes[^1];
            if (time >= last.Time) return (last.Position, last.Rotation, last.Scale);

            // Find the last keyframe at or before `time` - the "before" bracket; the
            // very next one in the (sorted) list is the "after" bracket.
            var beforeIndex = 0;
            for (var i = 0; i < _keyframes.Count; i++)
            {
                if (_keyframes[i].Time > time) break;
                beforeIndex = i;
            }

            var before = _keyframes[beforeIndex];
            var after = _keyframes[beforeIndex + 1];

            var span = after.Time - before.Time;
            var t = span > TimeEpsilon ? (float)((time - before.Time) / span) : 0f;

            var position = Vector3.Lerp(before.Position, after.Position, t);
            var rotation = Quaternion.Slerp(before.Rotation, after.Rotation, t);
            var scale = Vector3.Lerp(before.Scale, after.Scale, t);
            return (position, rotation, scale);
        }
    }
}
