using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>One row of the timeline: a name, its clips, and its keyframe markers.</summary>
    public partial class TimelineTrackViewModel : ObservableObject
    {
        private readonly TimelineViewModel _owner;

        public string Name { get; set; }

        public ObservableCollection<TimelineClipViewModel> Clips { get; } = new();

        public ObservableCollection<KeyframeViewModel> Keyframes { get; } = new();

        [ObservableProperty]
        private bool isMuted;

        [ObservableProperty]
        private bool isLocked;

        public TimelineTrackViewModel(TimelineViewModel owner, string name)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Name = name;
        }

        public TimelineClipViewModel AddClip(string name, double startFrame, double lengthFrames)
        {
            var clip = new TimelineClipViewModel(_owner, name, startFrame, lengthFrames);
            Clips.Add(clip);
            return clip;
        }

        [RelayCommand]
        private void AddClipAtPlayhead() => AddClip("Clip", _owner.CurrentFrame, 24);

        [RelayCommand]
        private void AddKeyframeAtPlayhead() => Keyframes.Add(new KeyframeViewModel(_owner, _owner.CurrentFrame));
    }
}
