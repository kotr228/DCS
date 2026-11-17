using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Animation;
using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.UI.ViewModels.Timeline
{
    /// <summary>
    /// Keyframe animation: a track that animates a layer moves, scales, turns and fades
    /// it between its keyframes (the layer's pixels stay as they are). The Keyframe
    /// panel edits the selected track's pose at the playhead - changing a value there
    /// adds a keyframe at the playhead, or updates the one already there.
    /// </summary>
    public partial class TimelineViewModel : IAnimatedMove
    {
        /// <summary>True while the document shows the Timeline (the Move tool then animates).</summary>
        public bool IsWorkspaceActive
        {
            get => _isWorkspaceActive;
            set
            {
                _isWorkspaceActive = value;
                ApplyAnimation();
            }
        }

        private bool _isWorkspaceActive;

        private (double X, double Y) _moveStartPose;

        private bool _movingCamera;

        public bool BeginAnimatedMove(Layer layer)
        {
            if (!IsWorkspaceActive || FrameLayers is null) return false;

            // The camera track is selected: dragging pans the camera.
            if (SelectedTrack is { IsCamera: true } cameraTrack)
            {
                Pause();
                var cameraPose = PoseAt(cameraTrack, Math.Floor(CurrentFrame));
                _moveStartPose = (cameraPose.X, cameraPose.Y);
                _movingCamera = true;
                return true;
            }
            _movingCamera = false;
            // The layer's own track, or the track of a group it is in.
            var track = Tracks.FirstOrDefault(t => t.TargetLayer is not null && ReferenceEquals(t.TargetLayer, layer));
            var parent = layer.ParentId;
            while (track is null && parent is { } id)
            {
                var group = FrameLayers.Scene.Layers.FirstOrDefault(l => l.Id == id);
                track = Tracks.FirstOrDefault(t => t.TargetLayer is not null && ReferenceEquals(t.TargetLayer, group));
                parent = group?.ParentId;
            }
            if (track is null) return false;

            Pause();
            SelectedTrack = track;
            var pose = PoseAt(track, Math.Floor(CurrentFrame));
            _moveStartPose = (pose.X, pose.Y);
            return true;
        }

        public void UpdateAnimatedMove(float dx, float dy)
        {
            // Dragging the picture right makes the camera look further left.
            var sign = _movingCamera ? -1 : 1;
            PoseX = Math.Round(_moveStartPose.X + sign * dx, 1);
            PoseY = Math.Round(_moveStartPose.Y + sign * dy, 1);
        }

        /// <summary>The track the Keyframe panel edits.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEditPose))]
        [NotifyPropertyChangedFor(nameof(SelectedTrackTitle))]
        private TimelineTrackViewModel? selectedTrack;

        public bool CanEditPose => SelectedTrack?.IsAnimated == true;

        public string SelectedTrackTitle => SelectedTrack switch
        {
            { IsAudio: true } audio => $"AUDIO - {audio.Name.Replace("Audio: ", "")}",
            { IsCamera: true } => "CAMERA - X/Y: where it looks, Scale: zoom, Opacity: fade",
            { TargetLayer: not null } layer => $"KEYFRAME - {layer.Name}",
            _ => "KEYFRAME - pick a layer track",
        };

        /// <summary>True while exporting: the camera always applies then.</summary>
        internal bool _exporting;

        partial void OnSelectedTrackChanged(TimelineTrackViewModel? value)
        {
            ApplyAnimation();
            OnPropertyChanged(nameof(IsAudioSelected));
            OnPropertyChanged(nameof(IsTextTrack));
            OnPropertyChanged(nameof(ParentChoices));
            OnPropertyChanged(nameof(SelectedParent));
            OnPropertyChanged(nameof(PivotX));
            OnPropertyChanged(nameof(PivotY));
            OnPropertyChanged(nameof(BendX));
            OnPropertyChanged(nameof(BendY));
            OnPropertyChanged(nameof(IsLayerTrack));
            OnPropertyChanged(nameof(SelectedVolume));
            OnPropertyChanged(nameof(SelectedMuted));
            OnPropertyChanged(nameof(SelectedMotionBlur));
            OnPropertyChanged(nameof(WiggleAmount));
            OnPropertyChanged(nameof(WiggleRotation));
            OnPropertyChanged(nameof(WiggleSpeed));
            foreach (var track in Tracks) track.IsSelected = ReferenceEquals(track, value);
            RefreshPoseEditor();
        }

        [RelayCommand]
        private void SelectTrack(TimelineTrackViewModel? track) => SelectedTrack = track;

        /// <summary>Adds (or selects) a track animating the active layer, with a first
        /// keyframe at the playhead holding the layer as it is.</summary>
        [RelayCommand]
        private void AnimateActiveLayer()
        {
            // A layer or a whole group (e.g. a character drawn frame by frame, moved across the scene).
            if (FrameLayers?.ActiveLayer?.Model is not { IsAdjustment: false } layer) return;
            var existing = Tracks.FirstOrDefault(t => ReferenceEquals(t.TargetLayer, layer));
            if (existing is null)
            {
                var before = Capture();
                existing = new TimelineTrackViewModel(this, layer.Name) { TargetLayer = layer };
                existing.Keyframes.Add(new KeyframeViewModel(this, Math.Floor(CurrentFrame)));
                Tracks.Add(existing);
                Record(before, "Animate Layer");
            }
            SelectedTrack = existing;
            ApplyAnimation();
        }

        private LayerKey? _copiedPose;

        /// <summary>The selected layer track's motion path, for drawing on the canvas: where
        /// the middle of the layer is at each frame, which frames are keyframes, and
        /// which one is current. Empty when there's nothing to show.</summary>
        public System.Collections.Generic.IReadOnlyList<(SKPoint Point, bool IsKey, bool IsCurrent)> MotionPath()
        {
            var result = new System.Collections.Generic.List<(SKPoint, bool, bool)>();
            if (!IsWorkspaceActive || IsPlaying || SelectedTrack is not { TargetLayer: { } layer } track || track.Keyframes.Count < 2) return result;
            var pivot = track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
            var keys = track.Keyframes.Select(k => k.Frame).ToHashSet();
            var end = (int)Math.Ceiling(PlaybackEnd);
            var current = Math.Floor(CurrentFrame);
            for (var f = 0; f < end; f++)
            {
                // Where the joint is drawn at each frame (parents included).
                result.Add((WorldMatrixAt(track, f).MapPoint(pivot), keys.Contains(f), f == current));
            }
            return result;
        }

        /// <summary>Remembers the selected track's pose at the playhead.</summary>
        [RelayCommand]
        private void CopyPose()
        {
            if (SelectedTrack is { IsAnimated: true } track) _copiedPose = PoseAt(track, Math.Floor(CurrentFrame));
        }

        /// <summary>Sets the remembered pose at the playhead (on the selected track).</summary>
        [RelayCommand]
        private void PastePose()
        {
            if (_copiedPose is not { } pose) return;
            SetPose(_ => pose);
            RefreshPoseEditor();
        }

        // ----- Dragging keyframes along the timeline -----

        private KeyframeViewModel? _draggedKey;
        private double _dragOffset;
        private System.Collections.Generic.List<(KeyframeViewModel Key, double Start)> _dragged = new();

        private System.Collections.Generic.IEnumerable<KeyframeViewModel> AllKeys => Tracks.SelectMany(t => t.Keyframes);

        /// <summary>The selected keyframes, across all tracks.</summary>
        public System.Collections.Generic.IReadOnlyList<KeyframeViewModel> SelectedKeys => AllKeys.Where(k => k.IsSelected).ToList();

        /// <summary>A keyframe diamond is pressed: select it (Shift adds to / removes from
        /// the selection), select its track and jump the playhead to it.</summary>
        public void BeginKeyDrag(KeyframeViewModel key, bool addToSelection = false)
        {
            var track = Tracks.FirstOrDefault(t => t.Keyframes.Contains(key));
            if (track is null) return;
            Pause();
            if (addToSelection) key.IsSelected = !key.IsSelected;
            else if (!key.IsSelected)
            {
                foreach (var k in AllKeys) k.IsSelected = false;
                key.IsSelected = true;
            }
            _gestureBefore = Capture();
            _draggedKey = key.IsSelected ? key : null;
            _dragOffset = 0;
            _dragged = SelectedKeys.Select(k => (k, k.Frame)).ToList();
            SelectedTrack = track;
            CurrentFrame = key.Frame;
            OnPropertyChanged(nameof(SelectedKeys));
        }

        /// <summary>Moves every selected key by the same number of frames.</summary>
        public void DragKeyBy(double pixels)
        {
            if (_draggedKey is null || _dragged.Count == 0) return;
            _dragOffset += pixels / PixelsPerFrame;
            var earliest = _dragged.Min(d => d.Start);
            var latest = _dragged.Max(d => d.Start);
            var delta = Math.Clamp(Math.Round(_dragOffset), -earliest, Math.Max(0, TotalFrames - 1 - latest));
            foreach (var (k, start) in _dragged) k.Frame = start + delta;
            CurrentFrame = _draggedKey.Frame;
            ApplyAnimation();
        }

        /// <summary>The drag ended: keys they landed on are replaced; one undo step.</summary>
        public void EndKeyDrag()
        {
            if (_draggedKey is null) return;
            var moved = _dragged.Any(d => d.Key.Frame != d.Start);
            foreach (var track in Tracks)
            {
                var landed = track.Keyframes.Where(k => _dragged.Any(d => ReferenceEquals(d.Key, k))).ToList();
                foreach (var other in track.Keyframes.Where(k => !landed.Contains(k) && landed.Any(l => Math.Abs(l.Frame - k.Frame) < 0.5)).ToList())
                    track.Keyframes.Remove(other);
            }
            _draggedKey = null;
            _dragged.Clear();
            if (_gestureBefore is { } before && moved) Record(before, "Move Keyframes");
            _gestureBefore = null;
            OnKeysChanged();
            RefreshPoseEditor();
        }

        /// <summary>Deletes every selected keyframe. One undo step.</summary>
        [RelayCommand]
        private void DeleteSelectedKeys()
        {
            var selected = SelectedKeys;
            if (selected.Count == 0) return;
            var before = Capture();
            foreach (var track in Tracks)
                foreach (var key in track.Keyframes.Where(k => k.IsSelected).ToList()) track.Keyframes.Remove(key);
            OnKeysChanged();
            RefreshPoseEditor();
            Record(before, $"Delete {selected.Count} Keyframe(s)");
            OnPropertyChanged(nameof(SelectedKeys));
        }

        private System.Collections.Generic.List<LayerKey>? _copiedKeys;

        /// <summary>Remembers the selected keys of the selected track (their timing kept).</summary>
        [RelayCommand]
        private void CopyKeys()
        {
            if (SelectedTrack is not { } track) return;
            var keys = track.Keyframes.Where(k => k.IsSelected).Select(k => k.Pose).OrderBy(p => p.Frame).ToList();
            _copiedKeys = keys.Count > 0 ? keys : null;
        }

        /// <summary>Puts the copied keys on the selected track, the first one at the
        /// playhead (keys already there are replaced). One undo step.</summary>
        [RelayCommand]
        private void PasteKeys()
        {
            if (_copiedKeys is not { Count: > 0 } keys || SelectedTrack is not { IsAnimated: true } track) return;
            var before = Capture();
            var offset = Math.Floor(CurrentFrame) - keys[0].Frame;
            foreach (var k in track.Keyframes) k.IsSelected = false;
            foreach (var pose in keys)
            {
                var frame = pose.Frame + offset;
                foreach (var old in track.Keyframes.Where(k => Math.Abs(k.Frame - frame) < 0.5).ToList()) track.Keyframes.Remove(old);
                track.Keyframes.Add(new KeyframeViewModel(this, frame, pose with { Frame = frame }) { IsSelected = true });
            }
            TotalFrames = Math.Max(TotalFrames, PlaybackEnd);
            OnKeysChanged();
            RefreshPoseEditor();
            Record(before, $"Paste {keys.Count} Keyframe(s)");
        }

        /// <summary>Animation &gt; Add Camera: a camera track that zooms, pans and turns the
        /// whole picture (a slow zoom into a still, a pan across a wide scene...).</summary>
        [RelayCommand]
        private void AddCamera()
        {
            var existing = Tracks.FirstOrDefault(t => t.IsCamera);
            if (existing is null)
            {
                var before = Capture();
                existing = new TimelineTrackViewModel(this, "Camera") { IsCamera = true };
                existing.Keyframes.Add(new KeyframeViewModel(this, Math.Floor(CurrentFrame)));
                Tracks.Insert(0, existing);
                Record(before, "Add Camera");
            }
            SelectedTrack = existing;
            ApplyAnimation();
        }

        /// <summary>Removes the selected track's keyframe at the playhead.</summary>
        [RelayCommand]
        private void DeleteKeyAtPlayhead()
        {
            if (SelectedTrack is not { } track || KeyAt(track, CurrentFrame) is not { } key) return;
            var before = Capture();
            track.Keyframes.Remove(key);
            OnKeysChanged();
            Record(before, "Delete Keyframe");
        }

        private static KeyframeViewModel? KeyAt(TimelineTrackViewModel track, double frame)
        {
            var f = Math.Floor(frame);
            return track.Keyframes.FirstOrDefault(k => Math.Abs(k.Frame - f) < 0.5);
        }

        /// <summary>The track's pose at <paramref name="frame"/>, from its keyframes.</summary>
        internal static LayerKey PoseAt(TimelineTrackViewModel track, double frame) =>
            LayerKey.At(track.Keyframes.Select(k => k.Pose).OrderBy(p => p.Frame).ToList(), frame);

        // ----- The Keyframe panel: the selected track's pose at the playhead -----

        private bool _syncingPose;

        [ObservableProperty] private double poseX;
        [ObservableProperty] private double poseY;
        [ObservableProperty] private double poseScale = 100;
        [ObservableProperty] private double poseRotation;
        [ObservableProperty] private double poseOpacity = 100;
        [ObservableProperty] private double poseReveal = 100;
        [ObservableProperty] private double poseBlur;
        [ObservableProperty] private double poseBend;
        [ObservableProperty] private double poseBrightness;
        [ObservableProperty] private double poseSaturation = 100;
        [ObservableProperty] private double poseHue;

        partial void OnPoseBrightnessChanged(double value) => SetPose(p => p with { Brightness = value });
        partial void OnPoseSaturationChanged(double value) => SetPose(p => p with { Saturation = value });
        partial void OnPoseHueChanged(double value) => SetPose(p => p with { Hue = value });

        /// <summary>The selected track animates a text layer (so Text Reveal applies).</summary>
        public bool IsTextTrack => SelectedTrack?.TargetLayer?.IsText == true;

        /// <summary>The selected track animates a layer (so it has a joint and can have a parent).</summary>
        public bool IsLayerTrack => SelectedTrack?.TargetLayer is not null;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomCurve))]
        private KeyEasing poseEasing = KeyEasing.EaseInOut;

        // The Custom speed curve's handles (time, progress) and its picture.
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurvePoints))] private double curveX1 = 0.42;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurvePoints))] private double curveY1;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurvePoints))] private double curveX2 = 0.58;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurvePoints))] private double curveY2 = 1;

        public bool IsCustomCurve => PoseEasing == KeyEasing.Custom;

        partial void OnCurveX1Changed(double value) => SetPose(p => p with { CurveX1 = value });
        partial void OnCurveY1Changed(double value) => SetPose(p => p with { CurveY1 = value });
        partial void OnCurveX2Changed(double value) => SetPose(p => p with { CurveX2 = value });
        partial void OnCurveY2Changed(double value) => SetPose(p => p with { CurveY2 = value });

        /// <summary>The speed curve drawn in a 100 x 100 box (progress up, time right).</summary>
        public System.Windows.Media.PointCollection CurvePoints
        {
            get
            {
                var points = new System.Windows.Media.PointCollection();
                for (var i = 0; i <= 40; i++)
                {
                    var t = i / 40.0;
                    var y = LayerKey.Bezier(t, CurveX1, CurveY1, CurveX2, CurveY2);
                    points.Add(new System.Windows.Point(t * 100, 100 - Math.Clamp(y, -0.5, 1.5) * 100));
                }
                return points;
            }
        }

        public static string[] CurvePresets { get; } = { "Ease", "Ease In", "Ease Out", "Ease In Out", "Back (overshoot)", "Anticipate" };

        /// <summary>Puts a ready-made curve in the handles.</summary>
        [RelayCommand]
        private void ApplyCurvePreset(string? name)
        {
            var (x1, y1, x2, y2) = name switch
            {
                "Ease In" => (0.42, 0.0, 1.0, 1.0),
                "Ease Out" => (0.0, 0.0, 0.58, 1.0),
                "Ease In Out" => (0.42, 0.0, 0.58, 1.0),
                "Back (overshoot)" => (0.34, 1.56, 0.64, 1.0),
                "Anticipate" => (0.36, -0.4, 0.66, 1.0),
                _ => (0.25, 0.1, 0.25, 1.0),
            };
            if (SelectedTrack is not { IsAnimated: true }) return;
            SetPose(p => p with { Easing = KeyEasing.Custom, CurveX1 = x1, CurveY1 = y1, CurveX2 = x2, CurveY2 = y2 });
            RefreshPoseEditor();
        }

        public static KeyEasing[] Easings { get; } = Enum.GetValues<KeyEasing>();

        /// <summary>Whether there's a keyframe exactly at the playhead on the selected track.</summary>
        [ObservableProperty] private bool hasKeyAtPlayhead;

        partial void OnPoseXChanged(double value) => SetPose(p => p with { X = value });
        partial void OnPoseYChanged(double value) => SetPose(p => p with { Y = value });
        partial void OnPoseScaleChanged(double value) => SetPose(p => p with { Scale = value });
        partial void OnPoseRotationChanged(double value) => SetPose(p => p with { Rotation = value });
        partial void OnPoseOpacityChanged(double value) => SetPose(p => p with { Opacity = value });
        partial void OnPoseRevealChanged(double value) => SetPose(p => p with { Reveal = value });
        partial void OnPoseBlurChanged(double value) => SetPose(p => p with { Blur = value });
        partial void OnPoseBendChanged(double value)
        {
            // Bending needs a bend point: start it at the middle of the layer's content.
            if (value != 0 && SelectedTrack is { TargetLayer: { } layer, BendPoint: null } track)
                track.BendPoint = ContentCenter(layer, FrameLayers?.Scene);
            SetPose(p => p with { Bend = value });
            OnPropertyChanged(nameof(BendX));
            OnPropertyChanged(nameof(BendY));
        }
        partial void OnPoseEasingChanged(KeyEasing value) => SetPose(p => p with { Easing = value });

        private void SetPose(Func<LayerKey, LayerKey> change)
        {
            if (_syncingPose || SelectedTrack is not { IsAnimated: true } track) return;
            var before = Capture();
            var frame = Math.Floor(CurrentFrame);
            var key = KeyAt(track, frame);
            if (key is null)
            {
                key = new KeyframeViewModel(this, frame, PoseAt(track, frame));
                track.Keyframes.Add(key);
            }
            key.Pose = change(key.Pose) with { Frame = key.Frame };
            OnKeysChanged();
            // Dragging a slider: one undo step per keyframe, not one per tiny move.
            Record(before, "Change Keyframe", mergeKey: (track.TargetLayerId, frame));
        }

        private void RefreshPoseEditor()
        {
            _syncingPose = true;
            try
            {
                var track = SelectedTrack;
                var pose = track is { IsAnimated: true } ? PoseAt(track, Math.Floor(CurrentFrame)) : new LayerKey(0);
                PoseX = Math.Round(pose.X, 2);
                PoseY = Math.Round(pose.Y, 2);
                PoseScale = Math.Round(pose.Scale, 2);
                PoseRotation = Math.Round(pose.Rotation, 2);
                PoseOpacity = Math.Round(pose.Opacity, 2);
                PoseReveal = Math.Round(pose.Reveal, 2);
                PoseBlur = Math.Round(pose.Blur, 2);
                PoseBend = Math.Round(pose.Bend, 2);
                PoseBrightness = Math.Round(pose.Brightness, 2);
                PoseSaturation = Math.Round(pose.Saturation, 2);
                PoseHue = Math.Round(pose.Hue, 2);
                PoseEasing = pose.Easing;
                CurveX1 = pose.CurveX1;
                CurveY1 = pose.CurveY1;
                CurveX2 = pose.CurveX2;
                CurveY2 = pose.CurveY2;
                HasKeyAtPlayhead = track is not null && KeyAt(track, CurrentFrame) is not null;
            }
            finally
            {
                _syncingPose = false;
            }
        }

        /// <summary>Keyframes were added, removed or changed: redraw the animation.</summary>
        internal void OnKeysChanged()
        {
            if (SelectedTrack is not null) HasKeyAtPlayhead = KeyAt(SelectedTrack, CurrentFrame) is not null;
            ApplyAnimation();
        }

        /// <summary>Sets every animated layer's drawing transform for the current frame.</summary>
        internal void ApplyAnimation()
        {
            var changed = false;

            // The camera: the whole picture, seen through its zoom/pan/turn - while playing,
            // exporting, or editing the camera; not while drawing (strokes go where the
            // picture really is).
            if (FrameLayers is { } doc)
            {
                var showCamera = _exporting || (IsWorkspaceActive && (IsPlaying || SelectedTrack?.IsCamera == true));
                var camera = showCamera ? Tracks.FirstOrDefault(t => t.IsCamera && !t.IsMuted && t.Keyframes.Count > 0) : null;
                var pose = camera is null ? null : PoseAt(camera, CurrentFrame);
                var matrix = pose is null || pose.IsIdentity ? (SKMatrix?)null
                    : pose.ToCameraMatrix(new SKPoint(doc.DocumentWidth / 2f, doc.DocumentHeight / 2f));
                var opacity = pose is null ? 1 : Math.Clamp(pose.Opacity / 100, 0, 1);
                if (!Equals(doc.Scene.CameraMatrix, matrix) || doc.Scene.CameraOpacity != opacity)
                {
                    doc.Scene.CameraMatrix = matrix;
                    doc.Scene.CameraOpacity = opacity;
                    changed = true;
                }
            }

            foreach (var track in Tracks)
            {
                if (track.TargetLayer is not { } layer) continue;
                if (track.Keyframes.Count == 0 && ParentOf(track) is null && !track.HasWiggle)
                {
                    changed |= layer.AnimationMatrix is not null || layer.AnimationOpacity != 1 || layer.AnimationBitmap is not null || layer.AnimationBlur != 0 || layer.AnimationBend is not null;
                    layer.AnimationMatrix = null;
                    layer.AnimationOpacity = 1;
                    layer.AnimationBlur = 0;
                    layer.AnimationMotion = null;
                    layer.AnimationColor = null;
                    ApplyPicture(track, layer, new LayerKey(0), FrameLayers?.Scene);
                    continue;
                }
                var pose = PoseAt(track, CurrentFrame);
                var world = WorldMatrix(track, 0);
                layer.AnimationMatrix = world.IsIdentity ? null : world;
                layer.AnimationOpacity = Math.Clamp(pose.Opacity / 100, 0, 1);
                layer.AnimationBlur = Math.Max(0, pose.Blur);
                layer.AnimationColor = pose.ColorMatrix();
                layer.AnimationMotion = track.MotionBlur ? MotionMoments(track, world) : null;
                ApplyPicture(track, layer, pose, FrameLayers?.Scene);
                changed = true;
            }
            if (changed) FrameVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Where a layer is drawn at <paramref name="frame"/>: its own move/scale/turn
        /// (around its joint), then each parent's in turn (a chain that never loops).</summary>
        private SKMatrix WorldMatrixAt(TimelineTrackViewModel track, double frame)
        {
            var local = SKMatrix.Identity;
            if (track.TargetLayer is { } layer && track.Keyframes.Count > 0)
            {
                var pose = PoseAt(track, frame);
                track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
                if (!pose.IsStill) local = pose.ToMatrix(track.Pivot.Value);
            }
            var chain = WiggleMatrix(track, frame, local);
            var parent = ParentOf(track);
            for (var i = 0; parent is not null && i < 16; parent = ParentOf(parent), i++)
            {
                var parentLocal = SKMatrix.Identity;
                if (parent.TargetLayer is { } pl && parent.Keyframes.Count > 0)
                {
                    var pp = PoseAt(parent, frame);
                    parent.Pivot ??= ContentCenter(pl, FrameLayers?.Scene);
                    if (!pp.IsStill) parentLocal = pp.ToMatrix(parent.Pivot.Value);
                }
                chain = chain.PostConcat(WiggleMatrix(parent, frame, parentLocal));
            }
            return chain;
        }

        // ----- Bones: layers attached to other layers -----

        /// <summary>The track of the layer <paramref name="track"/> is attached to, if any.</summary>
        internal TimelineTrackViewModel? ParentOf(TimelineTrackViewModel track) =>
            track.ParentLayerId is { } id ? Tracks.FirstOrDefault(t => !ReferenceEquals(t, track) && t.TargetLayer?.Id == id) : null;

        /// <summary>Where a layer is drawn at the current frame: its own keyframes'
        /// move/scale/turn (around its joint), then everything its parents do.</summary>
        internal SKMatrix WorldMatrix(TimelineTrackViewModel track, int depth) => WorldMatrixAt(track, CurrentFrame);

        /// <summary>How many moments a motion-blurred frame is made of, and over how much of
        /// a frame they spread (half a frame, like a film camera's 180° shutter).</summary>
        internal const int MotionSamples = 8;
        internal const double MotionShutter = 0.5;

        /// <summary>The layer's placement at moments spread over the shutter around the
        /// playhead; null when it hardly moves (no blur needed).</summary>
        private SKMatrix[]? MotionMoments(TimelineTrackViewModel track, SKMatrix now)
        {
            var moments = new SKMatrix[MotionSamples];
            for (var i = 0; i < MotionSamples; i++)
                moments[i] = WorldMatrixAt(track, CurrentFrame + (i / (double)(MotionSamples - 1) - 0.5) * MotionShutter);
            // Does any corner of the picture move more than a pixel over the shutter?
            var w = track.TargetLayer?.Bitmap.Width ?? 1;
            var h = track.TargetLayer?.Bitmap.Height ?? 1;
            var corners = new[] { new SKPoint(0, 0), new SKPoint(w, 0), new SKPoint(0, h), new SKPoint(w, h) };
            var spread = corners.Max(c => (moments[0].MapPoint(c) - moments[^1].MapPoint(c)).Length);
            return spread < 1 ? null : moments;
        }

        /// <summary>Keyframe panel: the selected layer's motion blur on or off.</summary>
        public bool SelectedMotionBlur
        {
            get => SelectedTrack?.MotionBlur ?? false;
            set
            {
                if (SelectedTrack is not { TargetLayer: not null } track || track.MotionBlur == value) return;
                var before = Capture();
                track.MotionBlur = value;
                ApplyAnimation();
                Record(before, value ? "Motion Blur On" : "Motion Blur Off");
                OnPropertyChanged();
            }
        }

        /// <summary>The layer tracks the selected one can be attached to (not itself or its own children).</summary>
        public System.Collections.Generic.IReadOnlyList<string> ParentChoices =>
            new[] { "(none)" }.Concat(Tracks.Where(t => t.TargetLayer is not null && SelectedTrack is not null
                && !ReferenceEquals(t, SelectedTrack) && !IsAncestorOf(SelectedTrack, t)).Select(t => t.Name)).ToList();

        private bool IsAncestorOf(TimelineTrackViewModel ancestor, TimelineTrackViewModel track)
        {
            for (var (p, i) = (ParentOf(track), 0); p is not null && i < 16; p = ParentOf(p), i++)
                if (ReferenceEquals(p, ancestor)) return true;
            return false;
        }

        /// <summary>The selected track's parent (its name), or "(none)".</summary>
        public string SelectedParent
        {
            get => SelectedTrack is { } t && ParentOf(t) is { } p ? p.Name : "(none)";
            set
            {
                if (SelectedTrack is not { TargetLayer: not null } track) return;
                var parent = Tracks.FirstOrDefault(t => t.Name == value && t.TargetLayer is not null && !ReferenceEquals(t, track));
                if (parent is not null && IsAncestorOf(track, parent)) return;      // would loop
                if (track.ParentLayerId == parent?.TargetLayer?.Id) return;
                var before = Capture();
                track.ParentLayerId = parent?.TargetLayer?.Id;
                ApplyAnimation();
                Record(before, parent is null ? "Detach Layer" : $"Attach to {parent.Name}");
                OnPropertyChanged();
            }
        }

        /// <summary>The selected layer's joint (what it turns and scales around), in picture pixels.</summary>
        public double PivotX
        {
            get => SelectedTrack?.Pivot?.X ?? 0;
            set => SetPivot(value, null);
        }

        public double PivotY
        {
            get => SelectedTrack?.Pivot?.Y ?? 0;
            set => SetPivot(null, value);
        }

        private void SetPivot(double? x, double? y)
        {
            if (SelectedTrack is not { TargetLayer: { } layer } track) return;
            var old = track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
            var pivot = new SKPoint((float)(x ?? old.X), (float)(y ?? old.Y));
            if (pivot == old) return;
            var before = Capture();
            track.Pivot = pivot;
            ApplyAnimation();
            Record(before, "Move Joint", mergeKey: ("pivot", track.TargetLayerId));
            OnPropertyChanged(nameof(PivotX));
            OnPropertyChanged(nameof(PivotY));
        }

        /// <summary>Puts the joint back in the middle of the layer's content.</summary>
        [RelayCommand]
        private void ResetPivot()
        {
            if (SelectedTrack is not { TargetLayer: { } layer }) return;
            var center = ContentCenter(layer, FrameLayers?.Scene);
            SetPivot(center.X, center.Y);
        }

        /// <summary>The selected layer's joint where it is drawn now (for the canvas), or null.</summary>
        public SKPoint? JointMarker()
        {
            if (!IsWorkspaceActive || SelectedTrack is not { TargetLayer: { } layer } track) return null;
            var pivot = track.Pivot ??= ContentCenter(layer, FrameLayers?.Scene);
            return WorldMatrix(track, 0).MapPoint(pivot);
        }

        /// <summary>The picture a layer shows at this pose instead of its own pixels: typed
        /// out (Text Reveal) and/or bent at its bend point. None: its own pixels.</summary>
        private static void ApplyPicture(TimelineTrackViewModel track, Layer layer, LayerKey pose, Scene? scene)
        {
            if (layer.IsGroup)
            {
                // A group is bent while it's drawn (its layers together).
                if (Math.Abs(pose.Bend) < 0.05 || track.BendPoint is not { } joint)
                {
                    layer.AnimationBend = null;
                    return;
                }
                var groupRoot = track.Pivot ?? ContentCenter(layer, scene);
                if ((groupRoot - joint).Length < 4) groupRoot = new SKPoint(joint.X - Math.Max(20, layer.Bitmap.Width / 8f), joint.Y);
                layer.AnimationBend = (groupRoot, joint, pose.Bend);
                return;
            }
            var source = RevealPicture(track, layer, pose.Reveal);
            if (Math.Abs(pose.Bend) < 0.05 || track.BendPoint is not { } bend)
            {
                layer.AnimationBitmap = source;
                track.BentPicture?.Dispose();
                track.BentPicture = null;
                return;
            }
            var root = track.Pivot ?? ContentCenter(layer, null);
            // The joint on the bend point gives no direction: bend the right part, as for a
            // drawing lying left to right.
            if ((root - bend).Length < 4) root = new SKPoint(bend.X - Math.Max(20, layer.Bitmap.Width / 8f), bend.Y);
            var bent = Core.Animation.MeshBend.Bend(source ?? layer.Bitmap, root, bend, pose.Bend);
            layer.AnimationBitmap = bent;
            track.BentPicture?.Dispose();
            track.BentPicture = bent;
        }

        /// <summary>A text layer typed out to <paramref name="reveal"/> % of its letters
        /// (redrawn only when the number of letters changes); null when it isn't typed out.</summary>
        private static SKBitmap? RevealPicture(TimelineTrackViewModel track, Layer layer, double reveal)
        {
            if (layer.Text is not { } text || reveal >= 100)
            {
                track.RevealedPicture?.Dispose();
                track.RevealedPicture = null;
                track.RevealedCharacters = -1;
                return null;
            }
            var count = (int)Math.Round(text.Text.Length * Math.Clamp(reveal, 0, 100) / 100);
            if (count == track.RevealedCharacters && track.RevealedPicture is { } same) return same;
            track.RevealedCharacters = count;
            var picture = new SKBitmap(layer.Bitmap.Width, layer.Bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(picture))
            {
                canvas.Clear(SKColors.Transparent);
                if (count > 0) (text with { Text = text.Text[..count] }).Render(canvas);
            }
            track.RevealedPicture?.Dispose();
            track.RevealedPicture = picture;
            return picture;
        }

        /// <summary>The selected layer's bend point (the elbow), in picture pixels.</summary>
        public double BendX
        {
            get => SelectedTrack?.BendPoint?.X ?? 0;
            set => SetBendPoint(value, null);
        }

        public double BendY
        {
            get => SelectedTrack?.BendPoint?.Y ?? 0;
            set => SetBendPoint(null, value);
        }

        private void SetBendPoint(double? x, double? y)
        {
            if (SelectedTrack is not { TargetLayer: { } layer } track) return;
            var old = track.BendPoint ?? ContentCenter(layer, FrameLayers?.Scene);
            var point = new SKPoint((float)(x ?? old.X), (float)(y ?? old.Y));
            if (point == track.BendPoint) return;
            var before = Capture();
            track.BendPoint = point;
            ApplyAnimation();
            Record(before, "Move Bend Point", mergeKey: ("bend", track.TargetLayerId));
            OnPropertyChanged(nameof(BendX));
            OnPropertyChanged(nameof(BendY));
        }

        /// <summary>The selected layer's bend point where it is drawn now (for the canvas), or null.</summary>
        public SKPoint? BendMarker()
        {
            if (!IsWorkspaceActive || SelectedTrack is not { TargetLayer: not null, BendPoint: { } bend } track) return null;
            return WorldMatrix(track, 0).MapPoint(bend);
        }

        /// <summary>The middle of what's painted on the layer (scale and rotation turn around it).</summary>
        private static SKPoint ContentCenter(Layer layer, Scene? scene)
        {
            // A group: everything painted on the layers inside it.
            var layers = new System.Collections.Generic.List<Layer> { layer };
            if (layer.IsGroup && scene is not null)
            {
                var (start, end) = scene.BlockOf(layer);
                layers = scene.Layers.Skip(start).Take(end - start + 1).Where(l => !l.IsGroup).ToList();
            }
            var any = false;
            var union = SKRectI.Empty;
            foreach (var l in layers)
            {
                var b = SelectionOps.TrimBounds(l.Bitmap, null);
                if (b.IsEmpty) continue;
                union = any ? SKRectI.Union(union, b) : b;
                any = true;
            }
            return any
                ? new SKPoint(union.MidX, union.MidY)
                : new SKPoint(layer.Bitmap.Width / 2f, layer.Bitmap.Height / 2f);
        }
    }
}
