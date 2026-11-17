using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>How playback (and an exported GIF) repeats.</summary>
    public enum PlaybackLoop
    {
        /// <summary>From the end back to the start, forever.</summary>
        Loop,
        /// <summary>Plays once and stops at the end.</summary>
        Once,
        /// <summary>Forward, then backward, then forward...</summary>
        PingPong,
    }

    /// <summary>Editing frames from the frame strip (its right-click menu): how long a
    /// frame shows, moving it earlier or later; and how playback repeats.</summary>
    public partial class TimelineViewModel
    {
        public static int[] HoldChoices { get; } = { 1, 2, 3, 4, 6, 8, 12 };

        public static PlaybackLoop[] LoopChoices { get; } = Enum.GetValues<PlaybackLoop>();

        [ObservableProperty]
        private PlaybackLoop loopMode = PlaybackLoop.Loop;

        private int _direction = 1;

        /// <summary>One playback step, honoring <see cref="LoopMode"/>.</summary>
        private void StepPlayback()
        {
            var start = PlayStart;
            var end = PlayEnd;
            var frame = Math.Floor(CurrentFrame);
            if (frame < start || frame >= end)
            {
                CurrentFrame = start;
                SyncAudio(restart: true);
                return;
            }
            if (LoopMode == PlaybackLoop.PingPong)
            {
                var next = frame + _direction;
                if (next >= end || next < start)
                {
                    _direction = -_direction;
                    next = Math.Clamp(frame + _direction, start, Math.Max(start, end - 1));
                }
                CurrentFrame = next;
                SyncAudio(restart: false);
                return;
            }

            if (frame + 1 >= end)
            {
                if (LoopMode == PlaybackLoop.Once)
                {
                    Pause();
                    return;
                }
                CurrentFrame = start;
                SyncAudio(restart: true);
                return;
            }
            CurrentFrame = frame + 1;
            SyncAudio(restart: false);
        }

        /// <summary>Makes a frame show for <paramref name="frames"/> timeline frames; later frames move along.</summary>
        public void SetFrameHold(FrameThumbViewModel? thumb, int frames)
        {
            if (thumb is null || frames < 1) return;
            var clip = thumb.Clip;
            var delta = frames - clip.LengthFrames;
            if (delta == 0) return;
            var before = Capture();
            var end = clip.StartFrame + clip.LengthFrames;
            foreach (var later in Tracks.SelectMany(t => t.Clips).Where(c => !ReferenceEquals(c, clip) && c.TargetLayer is not null && c.StartFrame >= end).ToList())
                later.StartFrame += delta;
            clip.LengthFrames = frames;
            TotalFrames = Math.Max(TotalFrames, PlaybackEnd);
            UpdateFrameLayerVisibility();
            Record(before, $"Hold Frame {thumb.Number} for {frames}");
            FrameChanged?.Invoke(this, EventArgs.Empty); // the frame shown now is the one to draw on
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void MoveFrameEarlier(FrameThumbViewModel? thumb) => MoveFrame(thumb, -1);

        [RelayCommand]
        private void MoveFrameLater(FrameThumbViewModel? thumb) => MoveFrame(thumb, +1);

        /// <summary>Swaps a frame with its neighbor (earlier or later). One undo step.</summary>
        public void MoveFrame(FrameThumbViewModel? thumb, int direction)
        {
            if (thumb is null) return;
            var frames = FrameClips.ToList();
            var i = frames.IndexOf(thumb.Clip);
            var j = i + Math.Sign(direction);
            if (i < 0 || j < 0 || j >= frames.Count) return;
            var before = Capture();
            var (first, second) = i < j ? (frames[i], frames[j]) : (frames[j], frames[i]);
            var start = first.StartFrame;
            second.StartFrame = start;
            first.StartFrame = start + second.LengthFrames;
            CurrentFrame = thumb.Clip.StartFrame;
            UpdateFrameLayerVisibility();
            Record(before, "Move Frame");
            FrameChanged?.Invoke(this, EventArgs.Empty);
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void DuplicateFrameAt(FrameThumbViewModel? thumb)
        {
            if (thumb is null) return;
            CurrentFrame = thumb.Clip.StartFrame;
            AddFrame(duplicate: true);
        }

        [RelayCommand]
        private void DeleteFrameAt(FrameThumbViewModel? thumb)
        {
            if (thumb is null) return;
            CurrentFrame = thumb.Clip.StartFrame;
            DeleteFrame();
        }
    }
}
