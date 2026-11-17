using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Markers (named spots on the timeline - a beat, a step, a word - to line the
    /// animation up with) and the work area (In / Out: play and export only that part).
    /// </summary>
    public partial class TimelineViewModel
    {
        public const string MarkersTrackName = "Markers";

        private TimelineTrackViewModel? MarkersTrack => Tracks.FirstOrDefault(t => t.IsMarkers);

        /// <summary>Adds a marker named <paramref name="label"/> at the playhead. One undo step.</summary>
        public void AddMarker(string label)
        {
            var before = Capture();
            var track = MarkersTrack;
            if (track is null)
            {
                track = new TimelineTrackViewModel(this, MarkersTrackName) { IsMarkers = true };
                Tracks.Insert(0, track);
            }
            var frame = Math.Floor(CurrentFrame);
            foreach (var old in track.Clips.Where(c => c.StartFrame == frame).ToList()) track.Clips.Remove(old);
            track.AddClip(string.IsNullOrWhiteSpace(label) ? $"Marker {track.Clips.Count + 1}" : label.Trim(), frame, 1);
            Record(before, "Add Marker");
        }

        [RelayCommand]
        private void NextMarker() => JumpToMarker(+1);

        [RelayCommand]
        private void PreviousMarker() => JumpToMarker(-1);

        private void JumpToMarker(int direction)
        {
            if (MarkersTrack is not { } track) return;
            var frame = Math.Floor(CurrentFrame);
            var target = direction > 0
                ? track.Clips.Where(c => c.StartFrame > frame).OrderBy(c => c.StartFrame).FirstOrDefault()
                : track.Clips.Where(c => c.StartFrame < frame).OrderByDescending(c => c.StartFrame).FirstOrDefault();
            if (target is null) return;
            Pause();
            CurrentFrame = target.StartFrame;
        }

        // ----- Work area -----

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasWorkArea), nameof(WorkAreaLeft), nameof(WorkAreaWidth))]
        private double? inPoint;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasWorkArea), nameof(WorkAreaLeft), nameof(WorkAreaWidth))]
        private double? outPoint;

        public bool HasWorkArea => InPoint is not null || OutPoint is not null;

        /// <summary>The first frame that plays and is exported.</summary>
        public double PlayStart => Math.Clamp(InPoint ?? 0, 0, Math.Max(0, PlaybackEnd - 1));

        /// <summary>Just past the last frame that plays and is exported.</summary>
        public double PlayEnd => Math.Max(PlayStart + 1, Math.Min(PlaybackEnd, (OutPoint ?? double.MaxValue) + 1));

        public double WorkAreaLeft => PlayStart * PixelsPerFrame;

        public double WorkAreaWidth => (PlayEnd - PlayStart) * PixelsPerFrame;

        /// <summary>Animation &gt; Set In: playback and export start at the playhead.</summary>
        [RelayCommand]
        private void SetInPoint()
        {
            InPoint = Math.Floor(CurrentFrame);
            if (OutPoint is { } o && o < InPoint) OutPoint = null;
        }

        /// <summary>Animation &gt; Set Out: playback and export end at the playhead.</summary>
        [RelayCommand]
        private void SetOutPoint()
        {
            OutPoint = Math.Floor(CurrentFrame);
            if (InPoint is { } i && i > OutPoint) InPoint = null;
        }

        [RelayCommand]
        private void ClearWorkArea()
        {
            InPoint = null;
            OutPoint = null;
        }

        partial void OnPixelsPerFrameChanged(double oldValue, double newValue)
        {
            OnPropertyChanged(nameof(WorkAreaLeft));
            OnPropertyChanged(nameof(WorkAreaWidth));
        }
    }
}
