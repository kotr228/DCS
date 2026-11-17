using System.Numerics;
using JolieCat3D.Core.Numerics;
using JolieCat3D.Core.Scene;

namespace JolieCat3D.Service.Animation
{
    /// <summary>
    /// A node's transform animation as nine F-curves (X/Y/Z location, Euler rotation in
    /// degrees, scale) - like Blender, every channel has its own keys, interpolation and
    /// Bézier handles. The whole-transform API (<see cref="AddKeyframe"/>,
    /// <see cref="Keyframes"/>, <see cref="Evaluate"/>) still works on top of the curves.
    /// </summary>
    public sealed class AnimationTrack
    {
        private const double TimeEpsilon = 1e-6;
        private readonly Dictionary<AnimationChannel, FCurve> _curves = new();
        private readonly Dictionary<string, FCurve> _propertyCurves = new();

        /// <summary>Prefix of a property curve that drives a shape key's value.</summary>
        public const string ShapeKeyPrefix = "shape:";

        /// <summary>Extra animated values besides the transform, keyed by a path such as
        /// "shape:Smile" (a shape key's value).</summary>
        public IReadOnlyDictionary<string, FCurve> PropertyCurves => _propertyCurves;

        public FCurve PropertyCurve(string path)
        {
            if (!_propertyCurves.TryGetValue(path, out var curve)) _propertyCurves[path] = curve = new FCurve();
            return curve;
        }

        /// <summary>Applies the property curves (shape key values) for <paramref name="time"/>.</summary>
        public void ApplyProperties(double time)
        {
            foreach (var (path, curve) in _propertyCurves)
            {
                if (curve.Evaluate(time) is not { } value) continue;
                if (path.StartsWith(ShapeKeyPrefix, StringComparison.Ordinal) && Target.Mesh is { } mesh)
                {
                    var name = path[ShapeKeyPrefix.Length..];
                    if (mesh.ShapeKeys.FirstOrDefault(k => k.Name == name) is { } key) key.Value = value;
                }
            }
        }

        public Node Target { get; }

        public AnimationTrack(Node target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            foreach (var channel in AnimationChannels.All) _curves[channel] = new FCurve();
        }

        public FCurve Curve(AnimationChannel channel) => _curves[channel];

        public IReadOnlyDictionary<AnimationChannel, FCurve> Curves => _curves;

        public bool IsEmpty => _curves.Values.All(c => c.Keys.Count == 0) && _propertyCurves.Values.All(c => c.Keys.Count == 0);

        /// <summary>Whole-transform keys: one per distinct key time on any channel, holding the
        /// full evaluated transform there. Used by the dopesheet, exporters and older code.</summary>
        public IReadOnlyList<Keyframe> Keyframes
        {
            get
            {
                var times = _curves.Values.Concat(_propertyCurves.Values).SelectMany(c => c.Keys.Select(k => k.Time)).OrderBy(t => t).ToList();
                var result = new List<Keyframe>();
                foreach (var t in times)
                {
                    if (result.Count > 0 && Math.Abs(result[^1].Time - t) < TimeEpsilon) continue;
                    var (p, r, s) = Evaluate(t)!.Value;
                    var interpolation = _curves.Values.Select(c => c.KeyAt(t)).FirstOrDefault(k => k is not null)?.Interpolation ?? InterpolationMode.Linear;
                    result.Add(new Keyframe(t, p, r, s, interpolation));
                }
                return result;
            }
        }

        /// <summary>Keys every channel at <paramref name="time"/>.</summary>
        public void AddKeyframe(double time, Vector3 position, Quaternion rotation, Vector3 scale, InterpolationMode interpolation = InterpolationMode.Linear)
        {
            var euler = EulerAngles.ToDegrees(rotation);
            Insert(AnimationChannel.LocationX, time, position.X, interpolation);
            Insert(AnimationChannel.LocationY, time, position.Y, interpolation);
            Insert(AnimationChannel.LocationZ, time, position.Z, interpolation);
            Insert(AnimationChannel.RotationX, time, Unwrap(AnimationChannel.RotationX, time, euler.X), interpolation);
            Insert(AnimationChannel.RotationY, time, Unwrap(AnimationChannel.RotationY, time, euler.Y), interpolation);
            Insert(AnimationChannel.RotationZ, time, Unwrap(AnimationChannel.RotationZ, time, euler.Z), interpolation);
            Insert(AnimationChannel.ScaleX, time, scale.X, interpolation);
            Insert(AnimationChannel.ScaleY, time, scale.Y, interpolation);
            Insert(AnimationChannel.ScaleZ, time, scale.Z, interpolation);
        }

        private void Insert(AnimationChannel channel, double time, float value, InterpolationMode interpolation) =>
            _curves[channel].Insert(time, value, interpolation);

        /// <summary>Picks the Euler angle equivalent (±360°) closest to the neighbouring key so
        /// rotation curves don't jump by a full turn.</summary>
        private float Unwrap(AnimationChannel channel, double time, float degrees)
        {
            var keys = _curves[channel].Keys;
            var neighbour = keys.Where(k => k.Time < time).LastOrDefault() ?? keys.FirstOrDefault(k => k.Time > time);
            if (neighbour is null) return degrees;
            while (degrees - neighbour.Value > 180f) degrees -= 360f;
            while (neighbour.Value - degrees > 180f) degrees += 360f;
            return degrees;
        }

        public void AddKeyframeFromCurrentTransform(double time, InterpolationMode interpolation = InterpolationMode.Linear) =>
            AddKeyframe(time, Target.LocalPosition, Target.LocalRotation, Target.LocalScale, interpolation);

        /// <summary>Removes every channel's key at <paramref name="time"/>.</summary>
        public void RemoveKeyframe(double time)
        {
            foreach (var curve in _curves.Values) curve.Remove(time);
            foreach (var curve in _propertyCurves.Values) curve.Remove(time);
        }

        /// <summary>Moves every channel's key at <paramref name="oldTime"/> to <paramref name="newTime"/>.</summary>
        public bool RetimeKeyframe(double oldTime, double newTime)
        {
            var moved = false;
            foreach (var curve in _curves.Values.Concat(_propertyCurves.Values))
                if (curve.KeyAt(oldTime) is { } key) moved |= curve.Retime(key, newTime);
            return moved;
        }

        /// <summary>The transform at <paramref name="time"/>; channels without keys keep the
        /// node's current value (like an un-animated channel in Blender).</summary>
        public (Vector3 Position, Quaternion Rotation, Vector3 Scale)? Evaluate(double time)
        {
            if (IsEmpty) return null;
            float V(AnimationChannel c, float fallback) => _curves[c].Evaluate(time) ?? fallback;

            var p = Target.LocalPosition;
            var s = Target.LocalScale;
            var currentEuler = EulerAngles.ToDegrees(Target.LocalRotation);
            var position = new Vector3(V(AnimationChannel.LocationX, p.X), V(AnimationChannel.LocationY, p.Y), V(AnimationChannel.LocationZ, p.Z));
            var rotationCurves = _curves[AnimationChannel.RotationX].Keys.Count + _curves[AnimationChannel.RotationY].Keys.Count + _curves[AnimationChannel.RotationZ].Keys.Count;
            var rotation = rotationCurves == 0
                ? Target.LocalRotation
                : EulerAngles.FromDegrees(V(AnimationChannel.RotationX, currentEuler.X), V(AnimationChannel.RotationY, currentEuler.Y), V(AnimationChannel.RotationZ, currentEuler.Z));
            var scale = new Vector3(V(AnimationChannel.ScaleX, s.X), V(AnimationChannel.ScaleY, s.Y), V(AnimationChannel.ScaleZ, s.Z));
            return (position, rotation, scale);
        }

        /// <summary>Deep copy (for undo, duplicating objects, ...), optionally for another node.</summary>
        public AnimationTrack Clone(Node? target = null)
        {
            var copy = new AnimationTrack(target ?? Target);
            foreach (var (channel, curve) in _curves) copy._curves[channel] = curve.Clone();
            foreach (var (path, curve) in _propertyCurves) copy._propertyCurves[path] = curve.Clone();
            return copy;
        }

        public bool ContentEquals(AnimationTrack? other) =>
            other is not null && _curves.All(kv => kv.Value.ContentEquals(other._curves[kv.Key])) &&
            _propertyCurves.Count == other._propertyCurves.Count &&
            _propertyCurves.All(kv => other._propertyCurves.TryGetValue(kv.Key, out var o) && kv.Value.ContentEquals(o));
    }
}
