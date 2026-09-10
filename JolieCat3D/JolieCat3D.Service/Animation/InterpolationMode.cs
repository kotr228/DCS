namespace JolieCat3D.Service.Animation
{
    /// <summary>How <see cref="AnimationTrack.Evaluate"/> blends from one
    /// <see cref="Keyframe"/> toward the next one after it - a property of the FIRST
    /// keyframe of the pair (the segment it starts), the same "each keyframe owns the
    /// curve leaving it" convention most keyframe-based animation tools use, so a
    /// track's last keyframe (which starts no segment of its own) has an
    /// <see cref="Keyframe.Interpolation"/> that's simply never consulted.</summary>
    public enum InterpolationMode
    {
        /// <summary>Constant-rate blending the whole way from one keyframe to the
        /// next - <see cref="System.Numerics.Vector3.Lerp"/>/<see cref="System.Numerics.Quaternion.Slerp"/>
        /// applied directly to the raw time fraction.</summary>
        Linear,

        /// <summary>Eased blending via a cubic Bezier easing curve (see
        /// <see cref="CubicBezierEasing"/>) applied to the time fraction before
        /// lerping/slerping - the standard "ease in and out" animation curve (the same
        /// shape CSS's own default <c>ease</c> timing function produces), starting and
        /// ending each segment gently rather than at a constant speed.</summary>
        Bezier,
    }
}
