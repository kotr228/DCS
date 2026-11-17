using System.Numerics;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>How a strip combines with the layers below it.</summary>
    public enum NlaBlendMode
    {
        /// <summary>Mix towards the strip's values by its influence.</summary>
        Replace,

        /// <summary>Layer on top (Blender's "Combine"): location and rotation add, scale
        /// multiplies - the usual additive game-animation layer (aim, breathing, recoil).</summary>
        Add,
    }

    /// <summary>What a strip does outside its own time range.</summary>
    public enum NlaExtrapolation
    {
        /// <summary>Only affects frames inside the strip.</summary>
        Nothing,

        /// <summary>Keeps its last pose after it ends (until another strip on the track starts).</summary>
        HoldForward,

        /// <summary>Keeps its first pose before it starts and its last pose after it ends.</summary>
        Hold,
    }

    /// <summary>
    /// One use of an action on an NLA track (Blender's action strip): where it sits on the
    /// timeline, how fast and how many times it plays, and how strongly it blends.
    /// Times are in seconds, like everything else in the animation system.
    /// </summary>
    public sealed class NlaStrip
    {
        private double _speed = 1, _repeat = 1;
        private float _influence = 1;

        public NlaStrip(AnimationAction action) => Action = action ?? throw new ArgumentNullException(nameof(action));

        public AnimationAction Action { get; set; }

        /// <summary>Scene time at which the strip begins.</summary>
        public double Start { get; set; }

        /// <summary>Playback speed (2 = twice as fast, half as long).</summary>
        public double Speed { get => _speed; set => _speed = Math.Clamp(double.IsFinite(value) ? value : 1, 0.01, 100); }

        /// <summary>How many times the action plays back to back.</summary>
        public double Repeat { get => _repeat; set => _repeat = Math.Clamp(double.IsFinite(value) ? value : 1, 0.01, 1000); }

        public float Influence { get => _influence; set => _influence = Math.Clamp(float.IsFinite(value) ? value : 1, 0f, 1f); }

        /// <summary>Seconds over which the influence ramps up from 0 at the start / down to 0 at the end.</summary>
        public double BlendIn { get; set; }
        public double BlendOut { get; set; }

        public NlaBlendMode BlendMode { get; set; } = NlaBlendMode.Replace;
        public NlaExtrapolation Extrapolation { get; set; } = NlaExtrapolation.HoldForward;
        public bool Muted { get; set; }

        /// <summary>First and last key time of the action (its frame range).</summary>
        public (double First, double Last) ActionRange
        {
            get
            {
                double first = double.MaxValue, last = double.MinValue;
                foreach (var track in Action.Tracks)
                foreach (var curve in track.Curves.Values.Concat(track.PropertyCurves.Values))
                {
                    if (curve.Keys.Count == 0) continue;
                    first = Math.Min(first, curve.Keys[0].Time);
                    last = Math.Max(last, curve.Keys[^1].Time);
                }
                return first > last ? (0, 0) : (first, last);
            }
        }

        public double Length
        {
            get
            {
                var (first, last) = ActionRange;
                return (last - first) * Repeat / Speed;
            }
        }

        public double End => Start + Length;

        public bool Contains(double time) => time >= Start - 1e-9 && time <= End + 1e-9;

        /// <summary>The action time to sample at scene time <paramref name="time"/> and the
        /// blend weight there; false when the strip has no effect at that time.</summary>
        public bool TryEvaluate(double time, out double actionTime, out float weight)
        {
            var (first, last) = ActionRange;
            actionTime = first;
            weight = 0;
            if (Muted) return false;

            if (time < Start - 1e-9)
            {
                if (Extrapolation != NlaExtrapolation.Hold) return false;
                actionTime = first;
                weight = Influence;
                return weight > 0;
            }
            if (time > End + 1e-9)
            {
                if (Extrapolation == NlaExtrapolation.Nothing) return false;
                actionTime = EndActionTime(first, last);
                weight = Influence;
                return weight > 0;
            }

            var span = last - first;
            var local = (time - Start) * Speed;
            if (span <= 1e-9) actionTime = first;
            else if (local >= span * Repeat - 1e-9) actionTime = EndActionTime(first, last);
            else actionTime = first + local % span;

            weight = Influence;
            if (BlendIn > 1e-9 && time - Start < BlendIn) weight *= (float)Math.Clamp((time - Start) / BlendIn, 0, 1);
            if (BlendOut > 1e-9 && End - time < BlendOut) weight *= (float)Math.Clamp((End - time) / BlendOut, 0, 1);
            return weight > 0;
        }

        /// <summary>Where the last repeat stops (a fractional repeat ends mid-action).</summary>
        private double EndActionTime(double first, double last)
        {
            var span = last - first;
            if (span <= 1e-9) return first;
            var fraction = Repeat - Math.Floor(Repeat);
            return fraction < 1e-9 ? last : first + span * fraction;
        }

        public NlaStrip Clone() => (NlaStrip)MemberwiseClone();
    }

    /// <summary>One NLA layer: strips that play one after another. Tracks stack bottom
    /// (index 0) to top; higher tracks blend over lower ones.</summary>
    public sealed class NlaTrack
    {
        public NlaTrack(string name) => Name = name;

        public string Name { get; set; }
        public bool Muted { get; set; }
        public List<NlaStrip> Strips { get; } = new();

        /// <summary>The strip that drives this track at <paramref name="time"/>: one that
        /// contains it, else the nearest earlier strip that holds its pose, else the first
        /// strip if it holds backwards.</summary>
        public NlaStrip? StripAt(double time)
        {
            NlaStrip? held = null;
            NlaStrip? first = null;
            foreach (var strip in Strips)
            {
                if (strip.Muted) continue;
                if (strip.Contains(time)) return strip;
                if (strip.End < time && strip.Extrapolation != NlaExtrapolation.Nothing && (held is null || strip.End > held.End)) held = strip;
                if (first is null || strip.Start < first.Start) first = strip;
            }
            if (held is not null) return held;
            return first is { Extrapolation: NlaExtrapolation.Hold } && time < first.Start ? first : null;
        }

        public NlaTrack Clone()
        {
            var copy = new NlaTrack(Name) { Muted = Muted };
            foreach (var strip in Strips) copy.Strips.Add(strip.Clone());
            return copy;
        }
    }

    /// <summary>Layered evaluation of the NLA stack plus the active action on top.</summary>
    internal static class NlaEvaluator
    {
        /// <summary>Channel values built up layer by layer. Like Blender, a channel starts
        /// from its default (location/rotation 0, scale 1, shape key 0) the first time a
        /// layer touches it, so evaluation never feeds on the previous frame's result;
        /// channels no layer animates keep the node's own value.</summary>
        private sealed class Pose
        {
            public readonly float[] Values = { 0, 0, 0, 0, 0, 0, 1, 1, 1 };
            public readonly bool[] Touched = new bool[9];
            public readonly Dictionary<string, float> Properties = new();
        }

        public static void Apply(AnimationTimeline timeline, IReadOnlyList<NlaTrack> tracks, AnimationAction active, double time)
        {
            var poses = new Dictionary<Node, Pose>(ReferenceEqualityComparer.Instance);
            var actions = new HashSet<AnimationAction>(timeline.Actions);

            foreach (var track in tracks)
            {
                if (track.Muted || track.StripAt(time) is not { } strip || !actions.Contains(strip.Action)) continue;
                if (!strip.TryEvaluate(time, out var actionTime, out var weight)) continue;
                Blend(poses, strip.Action, actionTime, weight, strip.BlendMode);
            }
            Blend(poses, active, time, 1f, NlaBlendMode.Replace);

            foreach (var (node, pose) in poses)
            {
                var euler = EulerAngles.ToDegrees(node.LocalRotation);
                var p = node.LocalPosition;
                var sc = node.LocalScale;
                float[] current = { p.X, p.Y, p.Z, euler.X, euler.Y, euler.Z, sc.X, sc.Y, sc.Z };
                var v = new float[9];
                for (var i = 0; i < 9; i++) v[i] = pose.Touched[i] ? pose.Values[i] : current[i];
                if (pose.Touched[0] || pose.Touched[1] || pose.Touched[2]) node.LocalPosition = new Vector3(v[0], v[1], v[2]);
                if (pose.Touched[3] || pose.Touched[4] || pose.Touched[5]) node.LocalRotation = EulerAngles.FromDegrees(v[3], v[4], v[5]);
                if (pose.Touched[6] || pose.Touched[7] || pose.Touched[8]) node.LocalScale = new Vector3(v[6], v[7], v[8]);
                if (node.Mesh is { } mesh)
                    foreach (var (path, value) in pose.Properties)
                        if (path.StartsWith(AnimationTrack.ShapeKeyPrefix, StringComparison.Ordinal)
                            && mesh.ShapeKeys.FirstOrDefault(k => k.Name == path[AnimationTrack.ShapeKeyPrefix.Length..]) is { } key)
                            key.Value = value;
            }
        }

        private static void Blend(Dictionary<Node, Pose> poses, AnimationAction action, double time, float weight, NlaBlendMode mode)
        {
            foreach (var track in action.Tracks)
            {
                if (track.IsEmpty) continue;
                var node = track.Target;
                if (!poses.TryGetValue(node, out var pose)) poses[node] = pose = new Pose();

                for (var i = 0; i < AnimationChannels.All.Length; i++)
                {
                    var channel = AnimationChannels.All[i];
                    if (track.Curve(channel).Evaluate(time) is not { } value) continue;
                    pose.Values[i] = Mix(pose.Values[i], value, weight, mode, isScale: i >= 6);
                    pose.Touched[i] = true;
                }
                foreach (var (path, curve) in track.PropertyCurves)
                {
                    if (curve.Evaluate(time) is not { } value) continue;
                    var below = pose.Properties.TryGetValue(path, out var c) ? c : 0f;
                    pose.Properties[path] = Mix(below, value, weight, mode, isScale: false);
                }
            }
        }

        private static float Mix(float below, float value, float weight, NlaBlendMode mode, bool isScale) => mode switch
        {
            NlaBlendMode.Add when isScale => below * (1f + (value - 1f) * weight),
            NlaBlendMode.Add => below + value * weight,
            _ => below + (value - below) * weight,
        };
    }
}
