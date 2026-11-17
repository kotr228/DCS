using JolieCat3D.Core.Materials;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// A whole scene's keyframe animation: one <see cref="AnimationTrack"/> per animated
    /// <see cref="Node"/> (Position/Rotation/Scale) plus one <see cref="TextureAnimationTrack"/>
    /// per animated <see cref="Material"/> (a clipbar/sprite-sheet frame sequence - see
    /// <c>Interop.ClipbarAnimationBridge"/>), a playback position (<see cref="CurrentTime"/>,
    /// in seconds - <see cref="CurrentFrame"/>/<see cref="TotalFrames"/>/<see cref="FrameRate"/>
    /// are display/scrubber conveniences derived from/onto it, never a second source of
    /// truth), and <see cref="Advance"/>/<see cref="Apply"/>, the two calls a playback
    /// transport UI ticks every frame to actually preview the animation: <see cref="Advance"/>
    /// moves <see cref="CurrentTime"/> forward while <see cref="IsPlaying"/>, and
    /// <see cref="Apply"/> pushes every track's interpolated/stepped value at that time
    /// straight onto its own <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>/
    /// <see cref="Node.LocalScale"/> or <see cref="Material.DiffuseTexturePath"/>/
    /// <see cref="Material.DiffuseTextureOffset"/>/<see cref="Material.DiffuseTextureScale"/> -
    /// the actual scene mutation a <c>JolieCat3D.Engine.Rendering.Scene3DRenderer.Refresh</c>
    /// then picks up and renders.
    /// </summary>
    public sealed class AnimationTimeline
    {
        private readonly List<AnimationAction> _actions = new() { new AnimationAction("Action") };
        private AnimationAction _active;

        /// <summary>The active action's tracks - everything below edits/plays the active action.</summary>
        private Dictionary<Node, AnimationTrack> _tracks => _active.TrackMap;

        public AnimationTimeline() => _active = _actions[0];

        /// <summary>Every animation clip; there is always at least one.</summary>
        public IReadOnlyList<AnimationAction> Actions => _actions;

        /// <summary>The clip being played and edited.</summary>
        public AnimationAction ActiveAction
        {
            get => _active;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (!_actions.Contains(value)) throw new ArgumentException("Not an action of this timeline.", nameof(value));
                _active = value;
            }
        }

        /// <summary>Adds a new action (empty, or a copy of the active one). Does not activate it.</summary>
        public AnimationAction AddAction(string name, bool copyActive = false)
        {
            var action = copyActive ? _active.Clone(UniqueActionName(name)) : new AnimationAction(UniqueActionName(name));
            _actions.Add(action);
            return action;
        }

        /// <summary>Re-adds a removed action (undo).</summary>
        public void InsertAction(int index, AnimationAction action)
        {
            if (_actions.Contains(action)) return;
            _actions.Insert(Math.Clamp(index, 0, _actions.Count), action);
        }

        /// <summary>Removes an action; the last one can't be removed.</summary>
        public bool RemoveAction(AnimationAction action)
        {
            if (_actions.Count <= 1 || !_actions.Remove(action)) return false;
            if (ReferenceEquals(_active, action)) _active = _actions[0];
            return true;
        }

        /// <summary>Back to a single empty action.</summary>
        public void ClearActions(string firstName = "Action")
        {
            _actions.Clear();
            _actions.Add(new AnimationAction(firstName));
            _nlaTracks.Clear();
            _active = _actions[0];
        }

        /// <summary>Puts back a whole action list and active action (NLA undo).</summary>
        public void RestoreActions(IEnumerable<AnimationAction> actions, AnimationAction active)
        {
            var list = actions.ToList();
            if (list.Count == 0) return;
            _actions.Clear();
            _actions.AddRange(list);
            _active = list.Contains(active) ? active : list[0];
        }

        // ================= NLA =================

        private readonly List<NlaTrack> _nlaTracks = new();

        /// <summary>The NLA stack, bottom (index 0) to top. The active action always plays
        /// above it (like Blender's action "tweak" layer).</summary>
        public List<NlaTrack> NlaTracks => _nlaTracks;

        /// <summary>True when some unmuted NLA strip can affect the scene.</summary>
        public bool HasNla => _nlaTracks.Any(t => !t.Muted && t.Strips.Any(s => !s.Muted));

        public string UniqueNlaTrackName(string baseName = "NlaTrack")
        {
            var name = baseName;
            var n = 1;
            while (_nlaTracks.Any(t => t.Name == name)) name = $"{baseName}.{n++:000}";
            return name;
        }

        /// <summary>Blender's "Push Down": the active action becomes a strip on a new top
        /// track (starting at its first key, so it keeps its timing) and a fresh empty
        /// action becomes active. Returns the new strip, or null when the action is empty.</summary>
        public NlaStrip? PushDownActiveAction()
        {
            var action = _active;
            var strip = new NlaStrip(action);
            if (action.Tracks.All(t => t.IsEmpty)) return null;
            strip.Start = strip.ActionRange.First;
            var track = new NlaTrack(UniqueNlaTrackName());
            track.Strips.Add(strip);
            _nlaTracks.Add(track);
            var fresh = new AnimationAction(UniqueActionName("Action"));
            _actions.Add(fresh);
            _active = fresh;
            return strip;
        }

        /// <summary>Deep copy of the NLA stack (strips keep pointing at the same actions).</summary>
        public List<NlaTrack> CloneNla() => _nlaTracks.Select(t => t.Clone()).ToList();

        public void RestoreNla(IEnumerable<NlaTrack> tracks)
        {
            _nlaTracks.Clear();
            _nlaTracks.AddRange(tracks.Select(t => t.Clone()));
        }

        /// <summary>Evaluates the whole NLA stack plus the active action over the timeline,
        /// one key per frame, into a new action (not added to the timeline) - what a game
        /// engine imports. The scene is restored to its pose at <see cref="CurrentTime"/>.</summary>
        public AnimationAction BakeNla(string name)
        {
            var baked = new AnimationAction(name);
            var nodes = _nlaTracks.SelectMany(t => t.Strips).Select(s => s.Action).Append(_active)
                .Distinct().SelectMany(a => a.Tracks).Where(t => !t.IsEmpty).Select(t => t.Target)
                .Distinct(ReferenceEqualityComparer.Instance).Cast<Node>().ToList();
            var properties = new Dictionary<Node, List<string>>(ReferenceEqualityComparer.Instance);
            foreach (var n in nodes)
                properties[n] = _nlaTracks.SelectMany(t => t.Strips).Select(s => s.Action).Append(_active)
                    .SelectMany(a => a.Tracks).Where(t => ReferenceEquals(t.Target, n)).SelectMany(t => t.PropertyCurves.Keys).Distinct().ToList();
            var saved = nodes.Select(n => (Node: n, n.LocalPosition, n.LocalRotation, n.LocalScale,
                Shapes: n.Mesh?.ShapeKeys.Select(k => k.Value).ToArray())).ToList();
            var time = CurrentTime;
            try
            {
                for (var frame = 0; frame <= TotalFrames; frame++)
                {
                    var t = frame / FrameRate;
                    CurrentTime = t;
                    Apply();
                    foreach (var node in nodes)
                    {
                        if (!baked.TryGetTrack(node, out var track) || track is null) { track = new AnimationTrack(node); baked.TrackMap[node] = track; }
                        track.AddKeyframeFromCurrentTransform(t, InterpolationMode.Linear);
                        foreach (var path in properties[node])
                        {
                            if (!path.StartsWith(AnimationTrack.ShapeKeyPrefix, StringComparison.Ordinal) || node.Mesh is not { } mesh) continue;
                            if (mesh.ShapeKeys.FirstOrDefault(k => k.Name == path[AnimationTrack.ShapeKeyPrefix.Length..]) is { } key)
                                track.PropertyCurve(path).Insert(t, key.Value, InterpolationMode.Linear);
                        }
                    }
                }
            }
            finally
            {
                foreach (var (node, p, r, s, shapes) in saved)
                {
                    node.LocalPosition = p; node.LocalRotation = r; node.LocalScale = s;
                    if (shapes is not null && node.Mesh is { } mesh)
                        for (var i = 0; i < shapes.Length && i < mesh.ShapeKeys.Count; i++) mesh.ShapeKeys[i].Value = shapes[i];
                }
                CurrentTime = time;
                Apply();
            }
            return baked;
        }

        public string UniqueActionName(string baseName)
        {
            var name = baseName;
            var n = 1;
            while (_actions.Any(a => a.Name == name)) name = $"{baseName}.{n++:000}";
            return name;
        }
        private readonly Dictionary<Material, TextureAnimationTrack> _textureTracks = new();

        private double _frameRate = 24.0;
        private int _totalFrames = 120;

        public IReadOnlyCollection<AnimationTrack> Tracks => _tracks.Values;

        /// <summary>Every <see cref="Material"/>'s own texture track - <see cref="Tracks"/>'s
        /// texture-animation twin, read by <see cref="Interop.ClipbarAnimationBridge"/>'s
        /// own callers and <see cref="AnimationExporter"/> alike (neither needs to reach
        /// into a private dictionary to enumerate what's actually animated).</summary>
        public IReadOnlyCollection<TextureAnimationTrack> TextureTracks => _textureTracks.Values;

        /// <summary>Frames per second - purely a display/scrubber unit conversion
        /// (<see cref="CurrentFrame"/>/<see cref="TotalFrames"/> divide or multiply by
        /// this); every <see cref="Keyframe"/>/<see cref="AnimationTrack.Evaluate"/>
        /// still works in plain seconds regardless of what this is set to, so changing
        /// it never rescales or otherwise disturbs already-recorded keyframe times.
        /// Clamped to a sane positive range - zero or negative would make
        /// <see cref="CurrentFrame"/> divide by zero or run the timeline backwards.</summary>
        public double FrameRate
        {
            get => _frameRate;
            set => _frameRate = Math.Clamp(value, 1.0, 240.0);
        }

        /// <summary>The timeline's own length, in frames - <see cref="Duration"/>'s
        /// frame-unit twin. Clamped to at least 1 frame (a zero-length timeline has
        /// nothing to scrub or play).</summary>
        public int TotalFrames
        {
            get => _totalFrames;
            set => _totalFrames = Math.Max(1, value);
        }

        /// <summary>The timeline's own length, in seconds - <see cref="TotalFrames"/> /
        /// <see cref="FrameRate"/>.</summary>
        public double Duration => TotalFrames / FrameRate;

        /// <summary>The current playback position, in seconds - always kept within
        /// [0, <see cref="Duration"/>] (see <see cref="Advance"/>/<see cref="SetTime"/>).</summary>
        public double CurrentTime { get; private set; }

        /// <summary>The current playback position, in frames (<see cref="CurrentTime"/>
        /// * <see cref="FrameRate"/>) - what a Slider-based frame scrubber binds to.
        /// Setting this is equivalent to <see cref="SetTime"/> with the matching seconds
        /// value.</summary>
        public double CurrentFrame
        {
            get => CurrentTime * FrameRate;
            set => SetTime(value / FrameRate);
        }

        public bool IsPlaying { get; private set; }

        /// <summary>Whether playback wraps back to time 0 on reaching the end (true,
        /// the default - most animation previews loop) or stops there instead.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>The <see cref="AnimationTrack"/> already recording <paramref name="node"/>'s
        /// own keyframes, or a brand new (empty) one added and returned if none exists
        /// yet - the Properties panel's own "Add Keyframe" button calls this before
        /// <see cref="AnimationTrack.AddKeyframeFromCurrentTransform"/>, so the first
        /// keyframe on a node implicitly creates its track.</summary>
        public AnimationTrack GetOrCreateTrack(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (!_tracks.TryGetValue(node, out var track)) _tracks[node] = track = new AnimationTrack(node);
            return track;
        }

        public bool TryGetTrack(Node node, out AnimationTrack? track) => _tracks.TryGetValue(node, out track);

        /// <summary>Stops tracking <paramref name="node"/>'s animation entirely (all its
        /// own keyframes discarded) - e.g. when the node itself is removed from the
        /// scene, so a stale track doesn't keep being evaluated/applied against a node
        /// no longer in it.</summary>
        public void RemoveTrack(Node node) => _tracks.Remove(node);

        /// <summary>Puts <paramref name="track"/> in place of whatever track its target had
        /// (an empty track removes it).</summary>
        public void SetTrack(AnimationTrack track)
        {
            ArgumentNullException.ThrowIfNull(track);
            if (track.IsEmpty) _tracks.Remove(track.Target);
            else _tracks[track.Target] = track;
        }

        /// <summary>The <see cref="TextureAnimationTrack"/> already recording
        /// <paramref name="material"/>'s own frame sequence, or a brand new (empty) one
        /// added and returned if none exists yet.</summary>
        public TextureAnimationTrack GetOrCreateTextureTrack(Material material)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (!_textureTracks.TryGetValue(material, out var track)) _textureTracks[material] = track = new TextureAnimationTrack(material);
            return track;
        }

        /// <summary>Installs <paramref name="track"/> as <paramref name="track"/>'s own
        /// <see cref="TextureAnimationTrack.Target"/> material's texture track,
        /// replacing any existing one for that material entirely - what
        /// <c>Interop.ClipbarAnimationBridge</c>'s own factory methods hand off to after
        /// building a whole track's worth of frames in one call, rather than a caller
        /// adding each frame through <see cref="GetOrCreateTextureTrack"/> one at a
        /// time.</summary>
        public void SetTextureTrack(TextureAnimationTrack track)
        {
            ArgumentNullException.ThrowIfNull(track);
            _textureTracks[track.Target] = track;
        }

        public void RemoveTextureTrack(Material material) => _textureTracks.Remove(material);

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;

        /// <summary>Pauses AND resets <see cref="CurrentTime"/> to 0 - the transport
        /// panel's own "Stop" button (as opposed to Pause, which leaves the scrubber
        /// wherever it was).</summary>
        public void Stop()
        {
            IsPlaying = false;
            CurrentTime = 0;
        }

        /// <summary>Moves the scrubber to exactly <paramref name="time"/> seconds,
        /// clamped to [0, <see cref="Duration"/>] - what dragging the frame scrubber
        /// slider calls directly (via <see cref="CurrentFrame"/>'s own setter), and what
        /// <see cref="Advance"/> uses internally.</summary>
        public void SetTime(double time) => CurrentTime = Math.Clamp(time, 0.0, Duration);

        /// <summary>Moves <see cref="CurrentTime"/> forward by <paramref name="deltaSeconds"/>
        /// while <see cref="IsPlaying"/> (a no-op otherwise) - call once per transport
        /// tick (e.g. a WPF <c>DispatcherTimer</c>) with the real elapsed time since the
        /// last tick. Past <see cref="Duration"/>: wraps back to 0 if <see cref="Loop"/>,
        /// otherwise clamps to the end and stops playback (mirroring how most animation
        /// previews behave at the end of a non-looping timeline).</summary>
        public void Advance(double deltaSeconds)
        {
            if (!IsPlaying || deltaSeconds <= 0) return;

            var newTime = CurrentTime + deltaSeconds;
            if (newTime <= Duration) { CurrentTime = newTime; return; }

            if (Loop && Duration > 0) CurrentTime = newTime % Duration;
            else { CurrentTime = Duration; IsPlaying = false; }
        }

        /// <summary>Evaluates every track at <see cref="CurrentTime"/> and pushes the
        /// result straight onto its own <see cref="Node.LocalPosition"/>/<see cref="Node.LocalRotation"/>/
        /// <see cref="Node.LocalScale"/> - the actual "preview the animation" step. A
        /// track with no keyframes at all (<see cref="AnimationTrack.Evaluate"/>
        /// returning null) leaves its node's transform completely untouched, rather than
        /// snapping it to some default.</summary>
        public void Apply()
        {
            if (HasNla) NlaEvaluator.Apply(this, _nlaTracks, _active, CurrentTime);
            else
            foreach (var track in _tracks.Values)
            {
                if (track.Evaluate(CurrentTime) is not { } result) continue;

                track.Target.LocalPosition = result.Position;
                track.Target.LocalRotation = result.Rotation;
                track.Target.LocalScale = result.Scale;
                track.ApplyProperties(CurrentTime);
            }

            foreach (var track in _textureTracks.Values)
            {
                if (track.Evaluate(CurrentTime) is not { } frame) continue;

                track.Target.DiffuseTexturePath = frame.TexturePath;
                track.Target.DiffuseTextureOffset = frame.Offset;
                track.Target.DiffuseTextureScale = frame.Scale;
            }
        }
    }
}
