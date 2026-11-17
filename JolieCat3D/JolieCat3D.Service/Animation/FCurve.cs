using System.Numerics;

namespace JolieCat3D.Service.Animation
{
    /// <summary>One key on an <see cref="FCurve"/>. Handles are offsets (seconds, value)
    /// from the key; the left handle points back in time, the right one forward.</summary>
    public sealed class FCurveKey
    {
        public double Time { get; set; }
        public float Value { get; set; }

        /// <summary>How the curve goes from this key to the next one.</summary>
        public InterpolationMode Interpolation { get; set; } = InterpolationMode.Bezier;

        public Vector2 HandleLeft { get; set; }
        public Vector2 HandleRight { get; set; }

        /// <summary>Handles are recomputed automatically (Blender "Auto Clamped") until the user moves one.</summary>
        public bool AutoHandles { get; set; } = true;

        public FCurveKey Clone() => new()
        {
            Time = Time, Value = Value, Interpolation = Interpolation,
            HandleLeft = HandleLeft, HandleRight = HandleRight, AutoHandles = AutoHandles,
        };

        public bool ContentEquals(FCurveKey o) =>
            Math.Abs(Time - o.Time) < 1e-9 && Value == o.Value && Interpolation == o.Interpolation &&
            HandleLeft == o.HandleLeft && HandleRight == o.HandleRight && AutoHandles == o.AutoHandles;
    }

    /// <summary>
    /// One animated value over time (Blender's F-Curve): keys sorted by time, evaluated as
    /// constant, linear or cubic Bézier segments.
    /// </summary>
    public sealed class FCurve
    {
        private const double TimeEpsilon = 1e-6;
        private readonly List<FCurveKey> _keys = new();

        public IReadOnlyList<FCurveKey> Keys => _keys;

        public FCurveKey? KeyAt(double time) => _keys.FirstOrDefault(k => Math.Abs(k.Time - time) < TimeEpsilon);

        /// <summary>Inserts a key, or replaces the value of the one already at that time.</summary>
        public FCurveKey Insert(double time, float value, InterpolationMode interpolation)
        {
            if (KeyAt(time) is { } existing)
            {
                existing.Value = value;
                existing.Interpolation = interpolation;
                UpdateAutoHandles();
                return existing;
            }
            var key = new FCurveKey { Time = time, Value = value, Interpolation = interpolation };
            var index = _keys.FindIndex(k => k.Time > time);
            if (index < 0) _keys.Add(key); else _keys.Insert(index, key);
            UpdateAutoHandles();
            return key;
        }

        public void Add(FCurveKey key)
        {
            var index = _keys.FindIndex(k => k.Time > key.Time);
            if (index < 0) _keys.Add(key); else _keys.Insert(index, key);
        }

        public bool Remove(double time) => _keys.RemoveAll(k => Math.Abs(k.Time - time) < TimeEpsilon) > 0 && UpdateAutoHandlesReturnTrue();

        private bool UpdateAutoHandlesReturnTrue() { UpdateAutoHandles(); return true; }

        public void Remove(FCurveKey key)
        {
            _keys.Remove(key);
            UpdateAutoHandles();
        }

        /// <summary>Moves a key in time (refused if another key already sits there).</summary>
        public bool Retime(FCurveKey key, double newTime)
        {
            if (_keys.Any(k => !ReferenceEquals(k, key) && Math.Abs(k.Time - newTime) < TimeEpsilon)) return false;
            key.Time = newTime;
            _keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            UpdateAutoHandles();
            return true;
        }

        /// <summary>Re-sorts and refreshes automatic handles after keys were edited directly.</summary>
        public void Changed()
        {
            _keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            UpdateAutoHandles();
        }

        /// <summary>Blender's "Auto Clamped" handles: slope from the neighbours, flat at
        /// local extremes (so the curve never overshoots a key), one third of each span long.</summary>
        public void UpdateAutoHandles()
        {
            for (var i = 0; i < _keys.Count; i++)
            {
                var k = _keys[i];
                if (!k.AutoHandles) continue;
                var prev = i > 0 ? _keys[i - 1] : null;
                var next = i + 1 < _keys.Count ? _keys[i + 1] : null;
                var leftSpan = prev is null ? 0 : (float)(k.Time - prev.Time);
                var rightSpan = next is null ? 0 : (float)(next.Time - k.Time);

                float slope = 0;
                if (prev is not null && next is not null)
                {
                    var isExtreme = (k.Value >= prev.Value && k.Value >= next.Value) || (k.Value <= prev.Value && k.Value <= next.Value);
                    if (!isExtreme) slope = (next.Value - prev.Value) / (float)Math.Max(1e-9, next.Time - prev.Time);
                }

                k.HandleLeft = new Vector2(-leftSpan / 3f, -slope * leftSpan / 3f);
                k.HandleRight = new Vector2(rightSpan / 3f, slope * rightSpan / 3f);
            }
        }

        public float? Evaluate(double time)
        {
            if (_keys.Count == 0) return null;
            if (time <= _keys[0].Time) return _keys[0].Value;
            var last = _keys[^1];
            if (time >= last.Time) return last.Value;

            var i = 0;
            while (i + 1 < _keys.Count && _keys[i + 1].Time <= time) i++;
            var a = _keys[i];
            var b = _keys[i + 1];
            var span = b.Time - a.Time;
            if (span < TimeEpsilon) return b.Value;

            switch (a.Interpolation)
            {
                case InterpolationMode.Constant:
                    return a.Value;
                case InterpolationMode.Linear:
                    return a.Value + (b.Value - a.Value) * (float)((time - a.Time) / span);
                default:
                {
                    // Cubic Bézier in (time, value); handles clamped inside the segment so
                    // time stays monotonic, then solve x(s) = time for s.
                    var p0 = new Vector2(0, a.Value);
                    var p3 = new Vector2((float)span, b.Value);
                    var h1 = a.HandleRight;
                    var h2 = b.HandleLeft;
                    var p1 = p0 + new Vector2(Math.Clamp(h1.X, 0, (float)span), h1.Y);
                    var p2 = p3 + new Vector2(Math.Clamp(h2.X, -(float)span, 0), h2.Y);
                    var x = (float)(time - a.Time);
                    float lo = 0, hi = 1, s = x / (float)span;
                    for (var iter = 0; iter < 40; iter++)
                    {
                        var bx = Bezier(p0.X, p1.X, p2.X, p3.X, s);
                        if (MathF.Abs(bx - x) < 1e-7f) break;
                        if (bx < x) lo = s; else hi = s;
                        s = (lo + hi) * 0.5f;
                    }
                    return Bezier(p0.Y, p1.Y, p2.Y, p3.Y, s);
                }
            }
        }

        private static float Bezier(float a, float b, float c, float d, float s)
        {
            var u = 1 - s;
            return u * u * u * a + 3 * u * u * s * b + 3 * u * s * s * c + s * s * s * d;
        }

        public FCurve Clone()
        {
            var c = new FCurve();
            foreach (var k in _keys) c._keys.Add(k.Clone());
            return c;
        }

        public bool ContentEquals(FCurve other) =>
            _keys.Count == other._keys.Count && _keys.Zip(other._keys).All(p => p.First.ContentEquals(p.Second));
    }
}
