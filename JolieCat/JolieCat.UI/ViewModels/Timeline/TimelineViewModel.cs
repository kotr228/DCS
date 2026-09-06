using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Serialization;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Foundation for the animation clipbar: tracks, clips, keyframes, a frame ruler, a
    /// scrubbable playhead, and frame-by-frame playback transport (see <see cref="Play"/>/
    /// <see cref="Pause"/>/<see cref="StepForward"/>/<see cref="StepBackward"/>/
    /// <see cref="GoToStart"/>/<see cref="GoToEnd"/>). This proves the interaction model
    /// a real timeline needs (drag a clip to move it, drag an edge to trim it, scrub the
    /// playhead, add tracks/clips/keyframes, play the playhead forward) so frame-by-
    /// frame and skeletal/transform-based animation have somewhere to attach later -
    /// playback here only advances <see cref="CurrentFrame"/> and scrubs the playhead
    /// within this timeline itself, since no per-frame layer property (transform,
    /// opacity, ...) is wired to interpolate from a clip/keyframe yet; the canvas does
    /// not (yet) render a different frame as the playhead moves.
    /// </summary>
    public partial class TimelineViewModel : ObservableObject, IDisposable
    {
        private const double RulerStepFrames = 10;

        private readonly DispatcherTimer _playbackTimer = new();
        private bool _disposed;

        public ObservableCollection<TimelineTrackViewModel> Tracks { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RulerTicks))]
        [NotifyPropertyChangedFor(nameof(TimelineWidth))]
        private double totalFrames = 240;

        [ObservableProperty]
        private double frameRate = 24;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RulerTicks))]
        [NotifyPropertyChangedFor(nameof(TimelineWidth))]
        private double pixelsPerFrame = 8;

        [ObservableProperty]
        private double currentFrame;

        /// <summary>True while the playhead is auto-advancing (see <see cref="Play"/>).</summary>
        [ObservableProperty]
        private bool isPlaying;

        /// <summary>Tick marks for the ruler, in both frame and pre-computed pixel space.</summary>
        public IReadOnlyList<TimelineRulerTick> RulerTicks => BuildRulerTicks();

        /// <summary>Total pixel width of the scrollable lane area, so the ruler and every track line up.</summary>
        public double TimelineWidth => TotalFrames * PixelsPerFrame;

        public TimelineViewModel()
        {
            // Seed a couple of tracks so the panel demonstrates real content immediately
            // instead of opening empty.
            var transform = new TimelineTrackViewModel(this, "Transform");
            transform.AddClip("Move In", 0, 48);
            transform.Keyframes.Add(new KeyframeViewModel(this, 0));
            transform.Keyframes.Add(new KeyframeViewModel(this, 48));
            Tracks.Add(transform);

            var opacity = new TimelineTrackViewModel(this, "Opacity");
            opacity.AddClip("Fade In", 12, 24);
            Tracks.Add(opacity);

            _playbackTimer.Tick += (_, _) => AdvancePlayback();
            UpdatePlaybackInterval();
        }

        partial void OnFrameRateChanged(double value) => UpdatePlaybackInterval();

        private void UpdatePlaybackInterval() =>
            _playbackTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1.0, FrameRate));

        /// <summary>Starts auto-advancing <see cref="CurrentFrame"/> at <see cref="FrameRate"/>
        /// frames per second, looping back to 0 once it passes <see cref="TotalFrames"/> -
        /// a no-op if already playing.</summary>
        [RelayCommand]
        private void Play()
        {
            if (IsPlaying) return;

            UpdatePlaybackInterval();
            IsPlaying = true;
            _playbackTimer.Start();
        }

        /// <summary>Stops auto-advancing the playhead where it currently sits - a no-op if not playing.</summary>
        [RelayCommand]
        private void Pause()
        {
            if (!IsPlaying) return;

            _playbackTimer.Stop();
            IsPlaying = false;
        }

        /// <summary>The playback transport's single play/pause button - <see cref="Play"/>
        /// if paused, <see cref="Pause"/> if playing.</summary>
        [RelayCommand]
        private void TogglePlayback()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        /// <summary>Pauses (a scrub gesture mid-playback should stop it, not fight it)
        /// and steps the playhead back one frame, clamped to 0.</summary>
        [RelayCommand]
        private void StepBackward()
        {
            Pause();
            CurrentFrame = Math.Max(0, CurrentFrame - 1);
        }

        /// <summary>Pauses and steps the playhead forward one frame, clamped to <see cref="TotalFrames"/>.</summary>
        [RelayCommand]
        private void StepForward()
        {
            Pause();
            CurrentFrame = Math.Min(TotalFrames, CurrentFrame + 1);
        }

        [RelayCommand]
        private void GoToStart()
        {
            Pause();
            CurrentFrame = 0;
        }

        [RelayCommand]
        private void GoToEnd()
        {
            Pause();
            CurrentFrame = TotalFrames;
        }

        /// <summary>One playback tick: advances <see cref="CurrentFrame"/> by a frame,
        /// looping back to 0 rather than stopping once it passes <see cref="TotalFrames"/> -
        /// a clipbar animation's playhead is conventionally meant to preview on a loop,
        /// not play once and stall at the end.</summary>
        private void AdvancePlayback()
        {
            var next = CurrentFrame + 1;
            CurrentFrame = next > TotalFrames ? 0 : next;
        }

        [RelayCommand]
        private void AddTrack() => Tracks.Add(new TimelineTrackViewModel(this, $"Track {Tracks.Count + 1}"));

        /// <summary>Moves the playhead, from a scrub-drag gesture measured in pixels.</summary>
        public void ScrubPlayheadBy(double pixelDelta) =>
            CurrentFrame = Math.Clamp(CurrentFrame + pixelDelta / PixelsPerFrame, 0, TotalFrames);

        /// <summary>Replaces every track with ones reconstructed from a loaded <c>.jolie</c>
        /// project's plain-data timeline (see <c>ProjectSerializer.Load</c>) - the reverse
        /// of the mapping <c>MainViewModel</c>'s save path builds from these same view
        /// models.</summary>
        public void LoadTracks(IReadOnlyList<TimelineTrackData> tracks, double totalFrames, double frameRate)
        {
            Pause();
            Tracks.Clear();

            foreach (var trackData in tracks)
            {
                var track = new TimelineTrackViewModel(this, trackData.Name);

                foreach (var clip in trackData.Clips)
                    track.AddClip(clip.Name, clip.StartFrame, clip.LengthFrames);

                foreach (var frame in trackData.KeyframeFrames)
                    track.Keyframes.Add(new KeyframeViewModel(this, frame));

                Tracks.Add(track);
            }

            TotalFrames = totalFrames;
            FrameRate = frameRate;
            CurrentFrame = 0;
        }

        private List<TimelineRulerTick> BuildRulerTicks()
        {
            var ticks = new List<TimelineRulerTick>();
            for (var frame = 0.0; frame <= TotalFrames; frame += RulerStepFrames)
                ticks.Add(new TimelineRulerTick(frame, frame * PixelsPerFrame));

            return ticks;
        }

        /// <summary>Stops <see cref="_playbackTimer"/> - needed now that playback exists
        /// at all: a <see cref="DispatcherTimer"/> left running would keep ticking
        /// (posting to the WPF dispatcher queue and advancing a now-orphaned
        /// <see cref="CurrentFrame"/> forever) even after the document tab that owns
        /// this timeline is closed, unless something explicitly stops it here.</summary>
        public void Dispose()
        {
            if (_disposed) return;

            _playbackTimer.Stop();
            _disposed = true;
        }
    }
}
