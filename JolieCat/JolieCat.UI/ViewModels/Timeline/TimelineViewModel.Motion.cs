using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Animation;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// More motion tools: Wiggle (a layer jitters by itself), moving a layer along a Pen
    /// path, reversing and stretching keys in time, and hearing the sound while the
    /// playhead is dragged (scrubbing).
    /// </summary>
    public partial class TimelineViewModel
    {
        // ----- Wiggle -----

        /// <summary>The wiggle at <paramref name="frame"/>: a smooth, never-repeating-looking
        /// shake made of a few sine waves at odd ratios, different for every layer.</summary>
        internal static (double Dx, double Dy, double Degrees) WiggleAt(TimelineTrackViewModel track, double frame, double fps)
        {
            if (!track.HasWiggle) return (0, 0, 0);
            var seed = (track.TargetLayerId ?? Guid.Empty).GetHashCode();
            var rnd = new Random(seed);
            double Phase() => rnd.NextDouble() * Math.PI * 2;
            var t = frame / Math.Max(1, fps) * track.WiggleSpeed * Math.PI * 2;
            double Wave(double p1, double p2, double p3) => (Math.Sin(t + p1) * 0.55 + Math.Sin(t * 2.17 + p2) * 0.3 + Math.Sin(t * 3.71 + p3) * 0.15);
            double px1 = Phase(), px2 = Phase(), px3 = Phase(), py1 = Phase(), py2 = Phase(), py3 = Phase(), pr1 = Phase(), pr2 = Phase(), pr3 = Phase();
            return (track.WiggleAmount * Wave(px1, px2, px3), track.WiggleAmount * Wave(py1, py2, py3), track.WiggleRotation * Wave(pr1, pr2, pr3));
        }

        /// <summary>The selected layer's wiggle: how far it shifts (px), turns (°) and how fast (shakes a second).</summary>
        public double WiggleAmount
        {
            get => SelectedTrack?.WiggleAmount ?? 0;
            set => SetWiggle(t => t.WiggleAmount = Math.Clamp(value, 0, 500));
        }

        public double WiggleRotation
        {
            get => SelectedTrack?.WiggleRotation ?? 0;
            set => SetWiggle(t => t.WiggleRotation = Math.Clamp(value, 0, 180));
        }

        public double WiggleSpeed
        {
            get => SelectedTrack?.WiggleSpeed ?? 2;
            set => SetWiggle(t => t.WiggleSpeed = Math.Clamp(value, 0.1, 30));
        }

        private void SetWiggle(Action<TimelineTrackViewModel> change)
        {
            if (SelectedTrack is not { TargetLayer: not null } track) return;
            var before = Capture();
            change(track);
            ApplyAnimation();
            Record(before, "Wiggle", mergeKey: ("wiggle", track.TargetLayerId));
            OnPropertyChanged(nameof(WiggleAmount));
            OnPropertyChanged(nameof(WiggleRotation));
            OnPropertyChanged(nameof(WiggleSpeed));
        }

        /// <summary>The wiggle as a placement change, applied after the layer's own pose.</summary>
        private SKMatrix WiggleMatrix(TimelineTrackViewModel track, double frame, SKMatrix local)
        {
            if (!track.HasWiggle || track.TargetLayer is not { } layer) return local;
            var (dx, dy, degrees) = WiggleAt(track, frame, FrameRate);
            track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
            var center = local.MapPoint(track.Pivot.Value);
            return local.PostConcat(SKMatrix.CreateRotationDegrees((float)degrees, center.X, center.Y))
                        .PostConcat(SKMatrix.CreateTranslation((float)dx, (float)dy));
        }

        // ----- Move Along Path -----

        /// <summary>
        /// Animation &gt; Move Along Path: keys for the selected layer so its middle travels
        /// along <paramref name="path"/> from the playhead over <paramref name="frames"/> frames
        /// (one key a frame, even speed); with <paramref name="orient"/> it also turns to face
        /// the way it moves. One undo step. False without a layer track or a usable path.
        /// </summary>
        public bool MoveAlongPath(SKPath path, int frames, bool orient)
        {
            if (SelectedTrack is not { TargetLayer: { } layer, IsCamera: false } track || frames < 1) return false;
            using var measure = new SKPathMeasure(path, false);
            var length = measure.Length;
            if (length <= 0) return false;
            track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
            var pivot = track.Pivot.Value;
            var before = Capture();
            var start = Math.Floor(CurrentFrame);
            measure.GetPositionAndTangent(0, out _, out var firstTangent);
            var firstAngle = Math.Atan2(firstTangent.Y, firstTangent.X) * 180 / Math.PI;

            for (var i = 0; i <= frames; i++)
            {
                var frame = start + i;
                if (!measure.GetPositionAndTangent(length * i / frames, out var at, out var tangent)) continue;
                var pose = PoseAt(track, frame) with
                {
                    Frame = frame,
                    X = at.X - pivot.X,
                    Y = at.Y - pivot.Y,
                    Easing = KeyEasing.Linear,
                };
                if (orient) pose = pose with { Rotation = Math.Atan2(tangent.Y, tangent.X) * 180 / Math.PI - firstAngle };
                if (KeyAt(track, frame) is { } existing) existing.Pose = pose;
                else track.Keyframes.Add(new KeyframeViewModel(this, frame, pose));
            }
            TotalFrames = Math.Max(TotalFrames, start + frames + 1);
            OnKeysChanged();
            ApplyAnimation();
            Record(before, "Move Along Path");
            return true;
        }

        // ----- Reverse / stretch keys -----

        /// <summary>The keys these tools act on: the selected keys, or else every key of the selected track.</summary>
        private List<KeyframeViewModel> KeysToRetime()
        {
            var selected = SelectedKeys.ToList();
            if (selected.Count > 0) return selected;
            return SelectedTrack?.Keyframes.ToList() ?? new List<KeyframeViewModel>();
        }

        /// <summary>Animation &gt; Reverse Keyframes: the keys play backwards over the same frames.</summary>
        [RelayCommand]
        private void ReverseKeys()
        {
            var keys = KeysToRetime();
            if (keys.Count < 2) return;
            var before = Capture();
            double first = keys.Min(k => k.Frame), last = keys.Max(k => k.Frame);
            // Each key moves to the mirrored frame, but keeps the easing that now leads into it
            // from the other side (the easing of the key that was before it).
            var byTrack = keys.GroupBy(k => Tracks.First(t => t.Keyframes.Contains(k)));
            foreach (var group in byTrack)
            {
                var ordered = group.OrderBy(k => k.Frame).ToList();
                var poses = ordered.Select(k => k.Pose).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var mirror = poses[ordered.Count - 1 - i];
                    var easing = ordered.Count - 2 - i >= 0 ? poses[ordered.Count - 2 - i] : mirror;
                    ordered[i].Pose = mirror with
                    {
                        Frame = ordered[i].Frame,
                        // Played backwards, a gentle start becomes a gentle stop.
                        Easing = easing.Easing switch { KeyEasing.EaseIn => KeyEasing.EaseOut, KeyEasing.EaseOut => KeyEasing.EaseIn, var e => e },
                        CurveX1 = 1 - easing.CurveX2, CurveY1 = 1 - easing.CurveY2,
                        CurveX2 = 1 - easing.CurveX1, CurveY2 = 1 - easing.CurveY1,
                    };
                }
            }
            OnKeysChanged();
            ApplyAnimation();
            Record(before, "Reverse Keyframes");
        }

        /// <summary>Animation &gt; Stretch Keyframes: the keys spread out (or squeeze together)
        /// in time by <paramref name="percent"/>, from the first of them.</summary>
        public void StretchKeys(double percent)
        {
            var keys = KeysToRetime();
            if (keys.Count < 2 || percent <= 0) return;
            var before = Capture();
            var first = keys.Min(k => k.Frame);
            foreach (var key in keys.OrderByDescending(k => k.Frame))
                key.Frame = first + Math.Round((key.Frame - first) * percent / 100);
            // Keys that landed on the same frame: keep the later one.
            foreach (var track in Tracks.Where(t => t.Keyframes.Count > 1))
                foreach (var dup in track.Keyframes.GroupBy(k => k.Frame).Where(g => g.Count() > 1).SelectMany(g => g.Take(g.Count() - 1)).ToList())
                    track.Keyframes.Remove(dup);
            TotalFrames = Math.Max(TotalFrames, keys.Max(k => k.Frame) + 1);
            OnKeysChanged();
            ApplyAnimation();
            Record(before, "Stretch Keyframes");
        }

        // ----- Audio scrubbing -----

        /// <summary>Plays a moment of sound when the playhead is moved by hand (not while playing).</summary>
        public bool ScrubAudio { get; set; } = true;

        private DispatcherTimer? _scrubTimer;

        private void ScrubSound()
        {
            if (!ScrubAudio || IsPlaying || !Tracks.Any(t => t.IsAudio && !t.IsMuted)) return;
            var fps = Math.Max(1, FrameRate);
            foreach (var track in Tracks.Where(t => t.IsAudio && !t.IsMuted && t.Clips.Count > 0))
            {
                var clip = track.Clips[0];
                if (CurrentFrame < clip.StartFrame || CurrentFrame >= clip.StartFrame + clip.LengthFrames) continue;
                var player = PlayerFor(track);
                player.Volume = Math.Clamp(track.Volume / 100, 0, 1);
                player.Position = TimeSpan.FromSeconds((CurrentFrame - clip.StartFrame) / fps);
                player.Play();
            }
            // A short moment (a little longer than a frame), then quiet again.
            _scrubTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _scrubTimer.Stop();
            _scrubTimer.Tick -= StopScrub;
            _scrubTimer.Tick += StopScrub;
            _scrubTimer.Start();
        }

        private void StopScrub(object? sender, EventArgs e)
        {
            _scrubTimer?.Stop();
            if (!IsPlaying) foreach (var player in _players.Values) player.Pause();
        }
    }
}
