using System;
using System.Collections.Generic;
using System.Linq;
using JolieCat.Core.Animation;
using JolieCat.Core.Documents;
using JolieCat.Core.History;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Undo for the Timeline: each edit (frames, keyframes, tracks, moved clips) is
    /// recorded in the document's history as the timeline's state before and after.
    /// Layers are referred to by Id, since undoing a layer change rebuilds the layers.
    /// </summary>
    public partial class TimelineViewModel
    {
        internal sealed record ClipState(string Name, double Start, double Length, Guid? LayerId);

        internal sealed record TrackState(string Name, Guid? LayerId, SKPoint? Pivot, IReadOnlyList<LayerKey> Keys, IReadOnlyList<ClipState> Clips,
            string? AudioPath = null, double Volume = 100, bool Muted = false, bool IsCamera = false, bool IsMarkers = false,
            Guid? ParentLayerId = null, SKPoint? BendPoint = null, bool MotionBlur = false,
            double WiggleAmount = 0, double WiggleRotation = 0, double WiggleSpeed = 2);

        internal sealed record State(IReadOnlyList<TrackState> Tracks, double TotalFrames, int Selected);

        /// <summary>One timeline edit (optionally with the layer change that went with it).</summary>
        internal sealed class EditCommand : IEditCommand
        {
            private readonly TimelineViewModel _timeline;
            private readonly IEditCommand? _layers;

            public EditCommand(TimelineViewModel timeline, State before, State after, IEditCommand? layers, object? mergeKey)
            {
                _timeline = timeline;
                Before = before;
                After = after;
                _layers = layers;
                MergeKey = mergeKey;
            }

            public State Before { get; }

            public State After { get; set; }

            /// <summary>Edits with the same key in a row (dragging one slider) merge into one step.</summary>
            public object? MergeKey { get; }

            public void Undo()
            {
                _layers?.Undo();
                _timeline.Restore(Before);
            }

            public void Redo()
            {
                _layers?.Redo();
                _timeline.Restore(After);
            }
        }

        /// <summary>Records an edit in the document's history (set by the document).</summary>
        public Action<IEditCommand, string>? RecordEdit { get; set; }

        /// <summary>The newest step in the document's history (for merging slider drags).</summary>
        public Func<IEditCommand?>? LastEdit { get; set; }

        private bool _restoring;

        internal State Capture() => new(
            Tracks.Select(t => new TrackState(t.Name, t.TargetLayerId, t.Pivot,
                t.Keyframes.Select(k => k.Pose).OrderBy(p => p.Frame).ToList(),
                t.Clips.Select(c => new ClipState(c.Name, c.StartFrame, c.LengthFrames, c.TargetLayerId)).ToList(),
                t.AudioPath, t.Volume, t.IsMuted, t.IsCamera, t.IsMarkers, t.ParentLayerId, t.BendPoint, t.MotionBlur, t.WiggleAmount, t.WiggleRotation, t.WiggleSpeed)).ToList(),
            TotalFrames,
            SelectedTrack is null ? -1 : Tracks.IndexOf(SelectedTrack));

        /// <summary>Records the change from <paramref name="before"/> to now as one undo step.</summary>
        internal void Record(State before, string name, IEditCommand? layers = null, object? mergeKey = null)
        {
            if (_restoring || RecordEdit is null) return;
            var after = Capture();
            if (mergeKey is not null && layers is null && LastEdit?.Invoke() is EditCommand last && Equals(last.MergeKey, mergeKey))
            {
                last.After = after;
                return;
            }
            RecordEdit(new EditCommand(this, before, after, layers, mergeKey), name);
        }

        private void Restore(State state)
        {
            _restoring = true;
            try
            {
                Pause();
                CloseAudio();
                Tracks.Clear();
                foreach (var t in state.Tracks)
                {
                    var track = new TimelineTrackViewModel(this, t.Name) { TargetLayerId = t.LayerId, Pivot = t.Pivot, AudioPath = t.AudioPath, Volume = t.Volume, IsMuted = t.Muted, IsCamera = t.IsCamera, IsMarkers = t.IsMarkers, ParentLayerId = t.ParentLayerId, BendPoint = t.BendPoint, MotionBlur = t.MotionBlur, WiggleAmount = t.WiggleAmount, WiggleRotation = t.WiggleRotation, WiggleSpeed = t.WiggleSpeed };
                    foreach (var key in t.Keys) track.Keyframes.Add(new KeyframeViewModel(this, key.Frame, key));
                    foreach (var c in t.Clips) track.AddClip(c.Name, c.Start, c.Length).TargetLayerId = c.LayerId;
                    Tracks.Add(track);
                }
                TotalFrames = state.TotalFrames;
                if (FrameLayers is not null) RewireById(FrameLayers.Scene);
                RefreshWaveforms();
                SelectedTrack = state.Selected >= 0 && state.Selected < Tracks.Count ? Tracks[state.Selected] : null;
            }
            finally
            {
                _restoring = false;
            }
        }

        /// <summary>Re-finds every clip's and track's layer by Id (after undo/redo rebuilt
        /// the layers), then redraws the frame.</summary>
        public void RewireById(Scene scene, Guid? onlyChangedLayer = null)
        {
            Layer? Find(Guid? id) => id is { } g ? scene.Layers.FirstOrDefault(l => l.Id == g) : null;
            foreach (var track in Tracks)
            {
                if (track.TargetLayerId is not null) track.TargetLayer = Find(track.TargetLayerId);
                foreach (var clip in track.Clips)
                    if (clip.TargetLayerId is not null) clip.TargetLayer = Find(clip.TargetLayerId);
            }
            UpdateFrameLayerVisibility();
            ApplyAnimation();
            RefreshPoseEditor();
            RefreshFrameStrip(onlyChangedLayer);
            FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
