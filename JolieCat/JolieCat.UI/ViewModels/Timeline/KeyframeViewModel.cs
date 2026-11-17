using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat.Core.Animation;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>A keyframe on a <see cref="TimelineTrackViewModel"/>: a frame and, on a
    /// track that animates a layer, the layer's pose there.</summary>
    public partial class KeyframeViewModel : ObservableObject
    {
        private readonly TimelineViewModel _owner;

        [ObservableProperty]
        private double frame;

        /// <summary>Selected (drawn white): drags, Delete Keys and Copy Keys act on all selected keys.</summary>
        [ObservableProperty]
        private bool isSelected;

        /// <summary>The pose at this key (offset, scale, rotation, opacity, easing).</summary>
        [ObservableProperty]
        private LayerKey pose;

        /// <summary>Delegates to the owning timeline's scale so the marker can position itself on screen.</summary>
        public double PixelsPerFrame => _owner.PixelsPerFrame;

        internal void NotifyScaleChanged() => OnPropertyChanged(nameof(PixelsPerFrame));

        public KeyframeViewModel(TimelineViewModel owner, double frame, LayerKey? pose = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.frame = frame;
            this.pose = (pose ?? new LayerKey(frame)) with { Frame = frame };
        }

        partial void OnFrameChanged(double value) => Pose = Pose with { Frame = value };
    }
}
