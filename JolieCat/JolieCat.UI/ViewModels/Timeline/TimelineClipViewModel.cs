using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Documents;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// One draggable/resizable animation clip block on a <see cref="TimelineTrackViewModel"/>.
    /// Positioning math (frame -&gt; pixel) lives in the view via <c>FramesToPixelsConverter</c>;
    /// this class only owns frame-space state and the pixel-space drag/resize gestures that
    /// mutate it.
    /// </summary>
    public partial class TimelineClipViewModel : ObservableObject
    {
        private const double MinLengthFrames = 2;

        private readonly TimelineViewModel _owner;

        public string Name { get; set; }

        /// <summary>The layer this clip shows exclusively for as long as the playhead
        /// sits within [<see cref="StartFrame"/>, <see cref="StartFrame"/>+<see cref="LengthFrames"/>) -
        /// null for an ordinary clip with no such association (every clip except ones
        /// from a Sprite Sheet -&gt; Clipbar Animation derivation, or a match
        /// <see cref="TimelineViewModel.RewireFrameLayers"/> later sets). Set directly
        /// on the Core model, not a <c>Layers.LayerViewModel</c> - Timeline has no
        /// reason to depend on the Layers view-model namespace, and a Core
        /// <see cref="Layer"/> reference is what <see cref="TimelineViewModel"/>'s own
        /// flipbook logic needs (see its own remarks for the trade-off that implies).</summary>
        public Layer? TargetLayer { get; set; }

        [ObservableProperty]
        private double startFrame;

        [ObservableProperty]
        private double lengthFrames;

        /// <summary>Delegates to the owning timeline's scale so the clip can size/position itself on screen.</summary>
        public double PixelsPerFrame => _owner.PixelsPerFrame;

        public TimelineClipViewModel(TimelineViewModel owner, string name, double startFrame, double lengthFrames)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Name = name;
            this.startFrame = Math.Max(0, startFrame);
            this.lengthFrames = Math.Max(MinLengthFrames, lengthFrames);
        }

        /// <summary>Moves the whole clip, from a body-drag gesture measured in pixels.</summary>
        public void DragBy(double pixelDelta)
        {
            var frameDelta = pixelDelta / PixelsPerFrame;
            var maxStart = Math.Max(0, _owner.TotalFrames - LengthFrames);
            StartFrame = Math.Clamp(StartFrame + frameDelta, 0, maxStart);
        }

        /// <summary>Drags the clip's left edge: trims/extends the start without moving the end.</summary>
        public void ResizeStartBy(double pixelDelta)
        {
            var frameDelta = pixelDelta / PixelsPerFrame;
            var end = StartFrame + LengthFrames;
            var newStart = Math.Clamp(StartFrame + frameDelta, 0, end - MinLengthFrames);
            StartFrame = newStart;
            LengthFrames = end - newStart;
        }

        /// <summary>Drags the clip's right edge: trims/extends the length only.</summary>
        public void ResizeEndBy(double pixelDelta)
        {
            var frameDelta = pixelDelta / PixelsPerFrame;
            var maxLength = Math.Max(MinLengthFrames, _owner.TotalFrames - StartFrame);
            LengthFrames = Math.Clamp(LengthFrames + frameDelta, MinLengthFrames, maxLength);
        }
    }
}
