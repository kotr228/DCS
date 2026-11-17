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

        /// <summary>The layer this track animates with its keyframes, or null.</summary>
        public Core.Documents.Layer? TargetLayer
        {
            get => _targetLayer;
            set
            {
                _targetLayer = value;
                if (value is not null) TargetLayerId = value.Id;
            }
        }

        private Core.Documents.Layer? _targetLayer;

        /// <summary>The animated layer's Id (see <see cref="TimelineClipViewModel.TargetLayerId"/>).</summary>
        public System.Guid? TargetLayerId { get; set; }

        /// <summary>The animated layer's name from a loaded file (wired up by name).</summary>
        public string? LayerName { get; set; }

        /// <summary>Bones: the layer this one is attached to (its track's layer Id) - it
        /// moves, scales and turns along with that one, plus its own keyframes.</summary>
        public System.Guid? ParentLayerId { get; set; }

        /// <summary>The parent's layer name from a loaded file (wired up by name).</summary>
        public string? ParentLayerName { get; set; }

        /// <summary>How many letters the typed-out picture last showed (to redo it only when it changes).</summary>
        internal int RevealedCharacters { get; set; } = -1;

        /// <summary>The typed-out picture (owned here, reused while the letter count stays).</summary>
        internal SkiaSharp.SKBitmap? RevealedPicture { get; set; }

        /// <summary>The bent picture shown now (owned here).</summary>
        internal SkiaSharp.SKBitmap? BentPicture { get; set; }

        /// <summary>Where the layer bends (the elbow), in picture pixels; null: it doesn't.</summary>
        public SkiaSharp.SKPoint? BendPoint { get; set; }

        /// <summary>Fast motion of this layer smears (motion blur).</summary>
        public bool MotionBlur { get; set; }

        /// <summary>Wiggle: the layer jitters by itself - up to this far (px) and this much (degrees),
        /// this many times a second.</summary>
        public double WiggleAmount { get; set; }
        public double WiggleRotation { get; set; }
        public double WiggleSpeed { get; set; } = 2;

        public bool HasWiggle => WiggleAmount > 0 || WiggleRotation > 0;

        /// <summary>The point the layer scales and turns around (the middle of its content).</summary>
        public SkiaSharp.SKPoint? Pivot { get; set; }

        /// <summary>An audio track's sound file (its clip says when it plays), or null.</summary>
        public string? AudioPath { get; set; }

        public bool IsAudio => AudioPath is not null;

        /// <summary>The camera track: its keyframes zoom, pan and turn the whole picture.</summary>
        public bool IsCamera { get; init; }

        /// <summary>The markers track: its clips are named spots on the timeline.</summary>
        public bool IsMarkers { get; init; }

        /// <summary>A track whose keyframes animate something (a layer, or the camera).</summary>
        public bool IsAnimated => TargetLayer is not null || IsCamera;

        /// <summary>Audio track volume, 0..100.</summary>
        [ObservableProperty]
        private double volume = 100;

        /// <summary>Highlighted as the track the Keyframe panel edits.</summary>
        [ObservableProperty]
        private bool isSelected;

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
            var clip = new TimelineClipViewModel(_owner, name, startFrame, lengthFrames) { IsAudio = IsAudio, IsMarker = IsMarkers };
            Clips.Add(clip);
            return clip;
        }

        [RelayCommand]
        private void AddClipAtPlayhead()
        {
            var before = _owner.Capture();
            AddClip("Clip", System.Math.Floor(_owner.CurrentFrame), 24);
            _owner.Record(before, "Add Clip");
        }

        /// <summary>A keyframe at the playhead holding the track's current pose (so
        /// nothing jumps); a no-op if there is one there already.</summary>
        [RelayCommand]
        private void AddKeyframeAtPlayhead()
        {
            var frame = System.Math.Floor(_owner.CurrentFrame);
            if (System.Linq.Enumerable.Any(Keyframes, k => System.Math.Abs(k.Frame - frame) < 0.5)) return;
            var before = _owner.Capture();
            Keyframes.Add(new KeyframeViewModel(_owner, frame, TimelineViewModel.PoseAt(this, frame)));
            _owner.Record(before, "Add Keyframe");
            _owner.SelectedTrack = this;
            _owner.OnKeysChanged();
        }
    }
}
