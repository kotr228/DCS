using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>A single keyframe marker placed on a <see cref="TimelineTrackViewModel"/> at a given frame.</summary>
    public partial class KeyframeViewModel : ObservableObject
    {
        private readonly TimelineViewModel _owner;

        [ObservableProperty]
        private double frame;

        /// <summary>Delegates to the owning timeline's scale so the marker can position itself on screen.</summary>
        public double PixelsPerFrame => _owner.PixelsPerFrame;

        public KeyframeViewModel(TimelineViewModel owner, double frame)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.frame = frame;
        }
    }
}
