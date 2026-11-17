using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>One picture in the frame strip: a frame's thumbnail, number and length.</summary>
    public sealed partial class FrameThumbViewModel : ObservableObject
    {
        private readonly TimelineViewModel _owner;

        public FrameThumbViewModel(TimelineViewModel owner, TimelineClipViewModel clip, int number)
        {
            _owner = owner;
            Clip = clip;
            Number = number;
        }

        // The frame's right-click menu.
        [RelayCommand] private void Hold(int frames) => _owner.SetFrameHold(this, frames);
        [RelayCommand] private void MoveEarlier() => _owner.MoveFrame(this, -1);
        [RelayCommand] private void MoveLater() => _owner.MoveFrame(this, +1);
        [RelayCommand] private void Duplicate() => _owner.DuplicateFrameAtCommand.Execute(this);
        [RelayCommand] private void Delete() => _owner.DeleteFrameAtCommand.Execute(this);

        /// <summary>The "Show For" submenu: each entry knows its own command.</summary>
        public System.Collections.Generic.IReadOnlyList<HoldOption> HoldOptions =>
            TimelineViewModel.HoldChoices.Select(n => new HoldOption(n == 1 ? "1 frame" : $"{n} frames", HoldCommand, n)).ToList();

        public TimelineClipViewModel Clip { get; }

        public int Number { get; }

        public string Label => Clip.LengthFrames > 1 ? $"{Number}  ×{Clip.LengthFrames:0}" : Number.ToString();

        internal void RefreshLabel() => OnPropertyChanged(nameof(Label));

        [ObservableProperty] private BitmapSource? thumbnail;

        [ObservableProperty] private bool isCurrent;
    }

    /// <summary>One "Show For" menu entry.</summary>
    public sealed record HoldOption(string Header, System.Windows.Input.ICommand Command, int Frames);

    /// <summary>The frame strip above the tracks: every frame as a thumbnail; click one to go there.</summary>
    public partial class TimelineViewModel
    {
        public ObservableCollection<FrameThumbViewModel> FrameStrip { get; } = new();

        public bool HasFrames => FrameStrip.Count > 0;

        /// <summary>"Frame 3 / 12": the frame number counted from 1, and how long the animation is.</summary>
        public string FrameCounterText => $"Frame {Math.Floor(CurrentFrame) + 1:0} / {PlaybackEnd:0}";

        /// <summary>Rebuilds the strip (frames added, removed, moved or redrawn). With
        /// <paramref name="onlyChangedLayer"/> (a brush stroke on one layer) only that
        /// frame's picture is redone, so drawing stays quick with many frames.</summary>
        public void RefreshFrameStrip(Guid? onlyChangedLayer = null)
        {
            var frames = FrameClips;
            var same = frames.Count == FrameStrip.Count && frames.Select((c, i) => ReferenceEquals(FrameStrip[i].Clip, c)).All(x => x);
            if (!same)
            {
                FrameStrip.Clear();
                for (var i = 0; i < frames.Count; i++) FrameStrip.Add(new FrameThumbViewModel(this, frames[i], i + 1));
                OnPropertyChanged(nameof(HasFrames));
            }
            foreach (var thumb in FrameStrip)
            {
                thumb.RefreshLabel();
                if (same && onlyChangedLayer is { } changed && thumb.Thumbnail is not null && thumb.Clip.TargetLayer is { } target
                    && target.Id != changed && !LayersOfFrame(target).Any(l => l.Id == changed)) continue;
                if (thumb.Clip.TargetLayer is { } layer)
                {
                    try
                    {
                        if (!layer.IsGroup)
                        {
                            thumb.Thumbnail = FastThumbnail(layer.Bitmap, 64);
                        }
                        else
                        {
                            // A frame group: its layers together.
                            using var picture = new SkiaSharp.SKBitmap(layer.Bitmap.Width, layer.Bitmap.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
                            using (var canvas = new SkiaSharp.SKCanvas(picture))
                            {
                                canvas.Clear(SkiaSharp.SKColors.Transparent);
                                foreach (var l in LayersOfFrame(layer)) canvas.DrawBitmap(l.Bitmap, 0, 0);
                            }
                            thumb.Thumbnail = FastThumbnail(picture, 64);
                        }
                    }
                    catch (ObjectDisposedException) { thumb.Thumbnail = null; }
                }
            }
            MarkCurrentThumb();
        }

        /// <summary>A small picture of <paramref name="bitmap"/> straight into WPF's pixel
        /// format (no encoding), fast enough to redo after every brush stroke.</summary>
        internal static BitmapSource FastThumbnail(SkiaSharp.SKBitmap bitmap, int size)
        {
            var scale = Math.Min(1.0, Math.Min((double)size / bitmap.Width, (double)size / bitmap.Height));
            var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var small = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
            using (var canvas = new SkiaSharp.SKCanvas(small))
            using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.Transparent);
                canvas.DrawImage(image, new SkiaSharp.SKRect(0, 0, w, h), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
            }
            var result = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, small.Bytes, small.RowBytes);
            result.Freeze();
            return result;
        }

        private void MarkCurrentThumb()
        {
            OnPropertyChanged(nameof(FrameCounterText));
            var current = CurrentFrameClip;
            foreach (var thumb in FrameStrip) thumb.IsCurrent = ReferenceEquals(thumb.Clip, current);
        }

        [RelayCommand]
        private void GoToFrame(FrameThumbViewModel? thumb)
        {
            if (thumb is null) return;
            Pause();
            CurrentFrame = thumb.Clip.StartFrame;
        }
    }
}
