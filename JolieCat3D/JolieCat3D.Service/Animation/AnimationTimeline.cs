using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// A whole scene's keyframe animation: one <see cref="AnimationTrack"/> per animated
    /// <see cref="Node"/>, a playback position (<see cref="CurrentTime"/>, in seconds -
    /// <see cref="CurrentFrame"/>/<see cref="TotalFrames"/>/<see cref="FrameRate"/> are
    /// display/scrubber conveniences derived from/onto it, never a second source of
    /// truth), and <see cref="Advance"/>/<see cref="Apply"/>, the two calls a playback
    /// transport UI ticks every frame to actually preview the animation: <see cref="Advance"/>
    /// moves <see cref="CurrentTime"/> forward while <see cref="IsPlaying"/>, and
    /// <see cref="Apply"/> pushes every track's interpolated value at that time straight
    /// onto its own <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>/
    /// <see cref="Node.LocalScale"/> - the actual scene mutation a
    /// <c>JolieCat3D.Engine.Rendering.Scene3DRenderer.Refresh</c> then picks up and
    /// renders.
    /// </summary>
    public sealed class AnimationTimeline
    {
        private readonly Dictionary<Node, AnimationTrack> _tracks = new();

        private double _frameRate = 24.0;
        private int _totalFrames = 120;

        public IReadOnlyCollection<AnimationTrack> Tracks => _tracks.Values;

        /// <summary>Frames per second - purely a display/scrubber unit conversion
        /// (<see cref="CurrentFrame"/>/<see cref="TotalFrames"/> divide or multiply by
        /// this); every <see cref="Keyframe"/>/<see cref="AnimationTrack.Evaluate"/>
        /// still works in plain seconds regardless of what this is set to, so changing
        /// it never rescales or otherwise disturbs already-recorded keyframe times.
        /// Clamped to a sane positive range - zero or negative would make
        /// <see cref="CurrentFrame"/> divide by zero or run the timeline backwards.</summary>
        public double FrameRate
        {
            get => _frameRate;
            set => _frameRate = Math.Clamp(value, 1.0, 240.0);
        }

        /// <summary>The timeline's own length, in frames - <see cref="Duration"/>'s
        /// frame-unit twin. Clamped to at least 1 frame (a zero-length timeline has
        /// nothing to scrub or play).</summary>
        public int TotalFrames
        {
            get => _totalFrames;
            set => _totalFrames = Math.Max(1, value);
        }

        /// <summary>The timeline's own length, in seconds - <see cref="TotalFrames"/> /
        /// <see cref="FrameRate"/>.</summary>
        public double Duration => TotalFrames / FrameRate;

        /// <summary>The current playback position, in seconds - always kept within
        /// [0, <see cref="Duration"/>] (see <see cref="Advance"/>/<see cref="SetTime"/>).</summary>
        public double CurrentTime { get; private set; }

        /// <summary>The current playback position, in frames (<see cref="CurrentTime"/>
        /// * <see cref="FrameRate"/>) - what a Slider-based frame scrubber binds to.
        /// Setting this is equivalent to <see cref="SetTime"/> with the matching seconds
        /// value.</summary>
        public double CurrentFrame
        {
            get => CurrentTime * FrameRate;
            set => SetTime(value / FrameRate);
        }

        public bool IsPlaying { get; private set; }

        /// <summary>Whether playback wraps back to time 0 on reaching the end (true,
        /// the default - most animation previews loop) or stops there instead.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>The <see cref="AnimationTrack"/> already recording <paramref name="node"/>'s
        /// own keyframes, or a brand new (empty) one added and returned if none exists
        /// yet - the Properties panel's own "Add Keyframe" button calls this before
        /// <see cref="AnimationTrack.AddKeyframeFromCurrentTransform"/>, so the first
        /// keyframe on a node implicitly creates its track.</summary>
        public AnimationTrack GetOrCreateTrack(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!_tracks.TryGetValue(node, out var track)) _tracks[node] = track = new AnimationTrack(node);
            return track;
        }

        public bool TryGetTrack(Node node, out AnimationTrack? track) => _tracks.TryGetValue(node, out track);

        /// <summary>Stops tracking <paramref name="node"/>'s animation entirely (all its
        /// own keyframes discarded) - e.g. when the node itself is removed from the
        /// scene, so a stale track doesn't keep being evaluated/applied against a node
        /// no longer in it.</summary>
        public void RemoveTrack(Node node) => _tracks.Remove(node);

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;

        /// <summary>Pauses AND resets <see cref="CurrentTime"/> to 0 - the transport
        /// panel's own "Stop" button (as opposed to Pause, which leaves the scrubber
        /// wherever it was).</summary>
        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0;
        }

        /// <summary>Moves the scrubber to exactly <paramref name="time"/> seconds,
        /// clamped to [0, <see cref="Duration"/>] - what dragging the frame scrubber
        /// slider calls directly (via <see cref="CurrentFrame"/>'s own setter), and what
        /// <see cref="Advance"/> uses internally.</summary>
        public void SetTime(double time) => CurrentTime = Math.Clamp(time, 0.0, Duration);

        /// <summary>Moves <see cref="CurrentTime"/> forward by <paramref name="deltaSeconds"/>
        /// while <see cref="IsPlaying"/> (a no-op otherwise) - call once per transport
        /// tick (e.g. a WPF <c>DispatcherTimer</c>) with the real elapsed time since the
        /// last tick. Past <see cref="Duration"/>: wraps back to 0 if <see cref="Loop"/>,
        /// otherwise clamps to the end and stops playback (mirroring how most animation
        /// previews behave at the end of a non-looping timeline).</summary>
        public void Advance(double deltaSeconds)
        {
            if (!IsPlaying || deltaSeconds <= 0) return;

            var newTime = CurrentTime + deltaSeconds;
            if (newTime <= Duration) { CurrentTime = newTime; return; }

            if (Loop && Duration > 0) CurrentTime = newTime % Duration;
            else { CurrentTime = Duration; IsPlaying = false; }
        }

        /// <summary>Evaluates every track at <see cref="CurrentTime"/> and pushes the
        /// result straight onto its own <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>/
        /// <see cref="Node.LocalScale"/> - the actual "preview the animation" step. A
        /// track with no keyframes at all (<see cref="AnimationTrack.Evaluate"/>
        /// returning null) leaves its node's transform completely untouched, rather than
        /// snapping it to some default.</summary>
        public void Apply()
        {
            foreach (var track in _tracks.Values)
            {
                if (track.Evaluate(CurrentTime) is not { } result) continue;

                track.Target.LocalPosition = result.Position;
                track.Target.LocalRotation = result.Rotation;
                track.Target.LocalScale = result.Scale;
            }
        }
    }
}
