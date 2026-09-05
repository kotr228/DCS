using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Serialization;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Foundation for the animation clipbar: tracks, clips, keyframes, a frame ruler and
    /// a scrubbable playhead. This proves the interaction model a real timeline needs
    /// (drag a clip to move it, drag an edge to trim it, scrub the playhead, add
    /// tracks/clips/keyframes) so frame-by-frame and skeletal/transform-based animation
    /// have somewhere to attach later.
    /// </summary>
    public partial class TimelineViewModel : ObservableObject
    {
        private const double RulerStepFrames = 10;

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
    }
}
