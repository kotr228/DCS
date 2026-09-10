using JolieCat3D.Core.Materials;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// One <see cref="Material"/>'s own animated texture sequence - a sorted-by-time
    /// list of <see cref="TextureKeyframe"/>s, each naming which whole image (or, for a
    /// sprite-sheet-driven sequence, which UV sub-rect of a SHARED image) is showing
    /// starting at that moment. Unlike <see cref="AnimationTrack"/>'s own Position/
    /// Rotation/Scale (continuously blended between two keyframes), texture frames are
    /// always STEPPED: <see cref="Evaluate"/> returns whichever frame's own time is the
    /// latest one at or before the query time, with NO blending between two different
    /// images - the same "one discrete frame is showing at a time" semantics a sprite-
    /// sheet/flipbook/clipbar animation always has (there is no such thing as "40% of
    /// the way between frame 3 and frame 4" for a texture swap the way there is for a
    /// smoothly blended position).
    /// </summary>
    public sealed class TextureAnimationTrack
    {
        private readonly List<TextureKeyframe> _keyframes = new();

        private const double TimeEpsilon = 1e-6;

        public Material Target { get; }

        public IReadOnlyList<TextureKeyframe> Keyframes => _keyframes;

        public TextureAnimationTrack(Material target) => Target = target ?? throw new ArgumentNullException(nameof(target));

        /// <summary>Adds a frame at <paramref name="time"/> - replacing any existing one
        /// within <see cref="TimeEpsilon"/> of it, keeping the list sorted by time (the
        /// same convention <see cref="AnimationTrack.AddKeyframe"/> uses).</summary>
        public void AddFrame(double time, TextureFrame frame)
        {
            var keyframe = new TextureKeyframe(time, frame);
            var existingIndex = _keyframes.FindIndex(k => Math.Abs(k.Time - time) < TimeEpsilon);

            if (existingIndex >= 0) { _keyframes[existingIndex] = keyframe; return; }

            var insertIndex = _keyframes.FindIndex(k => k.Time > time);
            if (insertIndex < 0) _keyframes.Add(keyframe);
            else _keyframes.Insert(insertIndex, keyframe);
        }

        /// <summary>The frame showing at <paramref name="time"/> - null with no frames
        /// recorded at all. Before the first frame's own time, clamps to that first
        /// frame (rather than showing nothing); at or after the last frame's own time,
        /// clamps to the last.</summary>
        public TextureFrame? Evaluate(double time)
        {
            if (_keyframes.Count == 0) return null;
            if (time <= _keyframes[0].Time) return _keyframes[0].Frame;

            var current = _keyframes[0].Frame;
            foreach (var keyframe in _keyframes)
            {
                if (keyframe.Time > time) break;
                current = keyframe.Frame;
            }

            return current;
        }
    }
}
