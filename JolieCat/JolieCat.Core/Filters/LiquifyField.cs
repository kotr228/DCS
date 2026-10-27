using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// The brush strokes of a Liquify session, stored as a displacement field: for each
    /// point of the image it says where the pixel shown there is taken from
    /// (<c>source = point + displacement</c>). Brushes edit the field; <see cref="Apply"/>
    /// renders any image through it, at any size, so one field drives both the small
    /// dialog preview and the full-resolution result.
    /// The field is kept at a working resolution (at most <see cref="MaxFieldSize"/> on
    /// its long side) and scaled up smoothly - liquify strokes are soft anyway.
    /// </summary>
    public sealed class LiquifyField
    {
        public const int MaxFieldSize = 1024;

        private float[] _dx;
        private float[] _dy;
        private float[] _scratchX;
        private float[] _scratchY;

        public int Width { get; }

        public int Height { get; }

        public LiquifyField(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            _dx = new float[width * height];
            _dy = new float[width * height];
            _scratchX = new float[width * height];
            _scratchY = new float[width * height];
        }

        /// <summary>A field sized for an image of <paramref name="imageWidth"/> x
        /// <paramref name="imageHeight"/>, at most <see cref="MaxFieldSize"/> on its long side.</summary>
        public static LiquifyField ForImage(int imageWidth, int imageHeight, int maxSize = MaxFieldSize)
        {
            var scale = Math.Min(1.0, (double)maxSize / Math.Max(imageWidth, imageHeight));
            return new LiquifyField(Math.Max(1, (int)Math.Round(imageWidth * scale)), Math.Max(1, (int)Math.Round(imageHeight * scale)));
        }

        /// <summary>True when nothing has been moved.</summary>
        public bool IsIdentity
        {
            get
            {
                for (var i = 0; i < _dx.Length; i++)
                    if (_dx[i] != 0 || _dy[i] != 0) return false;
                return true;
            }
        }

        /// <summary>The largest distance any point has been moved, in field pixels.</summary>
        public float MaxDisplacement
        {
            get
            {
                var max = 0f;
                for (var i = 0; i < _dx.Length; i++)
                    max = Math.Max(max, _dx[i] * _dx[i] + _dy[i] * _dy[i]);
                return MathF.Sqrt(max);
            }
        }

        public (float X, float Y) DisplacementAt(int x, int y) => (_dx[y * Width + x], _dy[y * Width + x]);

        public void Reset()
        {
            Array.Clear(_dx);
            Array.Clear(_dy);
        }

        public LiquifyField Clone()
        {
            var copy = new LiquifyField(Width, Height);
            Array.Copy(_dx, copy._dx, _dx.Length);
            Array.Copy(_dy, copy._dy, _dy.Length);
            return copy;
        }

        public void CopyFrom(LiquifyField other)
        {
            if (other.Width != Width || other.Height != Height) throw new ArgumentException("Size mismatch", nameof(other));
            Array.Copy(other._dx, _dx, _dx.Length);
            Array.Copy(other._dy, _dy, _dy.Length);
        }

        // ----- Brushes (all in field coordinates) -----

        /// <summary>Forward Warp: pushes the pixels under the brush along
        /// (<paramref name="moveX"/>, <paramref name="moveY"/>), scaled by
        /// <paramref name="pressure"/> (0..1).</summary>
        public void Push(float cx, float cy, float radius, float moveX, float moveY, float pressure)
        {
            var mx = moveX * pressure;
            var my = moveY * pressure;
            Deform(cx, cy, radius, (x, y, f) => (-mx * f, -my * f));
        }

        /// <summary>Pucker (positive <paramref name="amount"/>) pulls pixels toward the
        /// brush center; Bloat (negative) pushes them out. About 0.05 per dab is gentle.</summary>
        public void Pinch(float cx, float cy, float radius, float amount)
        {
            Deform(cx, cy, radius, (x, y, f) => ((x - cx) * amount * f, (y - cy) * amount * f));
        }

        /// <summary>Twirl: turns the pixels under the brush around its center by up to
        /// <paramref name="radians"/> (positive = clockwise on screen).</summary>
        public void Twirl(float cx, float cy, float radius, float radians)
        {
            Deform(cx, cy, radius, (x, y, f) =>
            {
                var a = -radians * f;
                var cos = MathF.Cos(a);
                var sin = MathF.Sin(a);
                var rx = x - cx;
                var ry = y - cy;
                return (rx * cos - ry * sin - rx, rx * sin + ry * cos - ry);
            });
        }

        /// <summary>Reconstruct: eases the pixels under the brush back toward where they
        /// started (<paramref name="amount"/> 0..1 per dab).</summary>
        public void Reconstruct(float cx, float cy, float radius, float amount)
        {
            ForEachInBrush(cx, cy, radius, (i, x, y, f) =>
            {
                var keep = 1 - Math.Clamp(amount * f, 0, 1);
                _dx[i] *= keep;
                _dy[i] *= keep;
            });
        }

        /// <summary>Composes a local move into the field: the point p now shows what
        /// p + e(p) showed before, i.e. d'(p) = e(p) + d(p + e(p)).</summary>
        private void Deform(float cx, float cy, float radius, Func<float, float, float, (float X, float Y)> offset)
        {
            Array.Copy(_dx, _scratchX, _dx.Length);
            Array.Copy(_dy, _scratchY, _dy.Length);
            ForEachInBrush(cx, cy, radius, (i, x, y, f) =>
            {
                var (ex, ey) = offset(x, y, f);
                var sx = x + ex;
                var sy = y + ey;
                _dx[i] = ex + SampleField(_scratchX, sx, sy);
                _dy[i] = ey + SampleField(_scratchY, sx, sy);
            });
        }

        /// <summary>Visits every field point within <paramref name="radius"/> of the
        /// center with its smooth falloff (1 at the center, 0 at the rim).</summary>
        private void ForEachInBrush(float cx, float cy, float radius, Action<int, float, float, float> visit)
        {
            if (radius <= 0) return;
            var r2 = radius * radius;
            var x0 = Math.Max(0, (int)MathF.Floor(cx - radius));
            var x1 = Math.Min(Width - 1, (int)MathF.Ceiling(cx + radius));
            var y0 = Math.Max(0, (int)MathF.Floor(cy - radius));
            var y1 = Math.Min(Height - 1, (int)MathF.Ceiling(cy + radius));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var ddx = x - cx;
                    var ddy = y - cy;
                    var d2 = ddx * ddx + ddy * ddy;
                    if (d2 >= r2) continue;
                    var t = 1 - d2 / r2;
                    visit(y * Width + x, x, y, t * t);
                }
        }

        private float SampleField(float[] field, float x, float y)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Math.Min(x0 + 1, Width - 1);
            var y1 = Math.Min(y0 + 1, Height - 1);
            var fx = x - x0;
            var fy = y - y0;
            var top = field[y0 * Width + x0] * (1 - fx) + field[y0 * Width + x1] * fx;
            var bottom = field[y1 * Width + x0] * (1 - fx) + field[y1 * Width + x1] * fx;
            return top * (1 - fy) + bottom * fy;
        }

        // ----- Rendering -----

        /// <summary>Renders <paramref name="source"/> (any size) through the field.</summary>
        public SKBitmap Apply(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var sx = (float)Width / source.Width;   // image -> field
            var sy = (float)Height / source.Height;
            return DistortFilters.Remap(source, (x, y) =>
            {
                // Field values sit at field-pixel centers.
                var fx = x * sx - 0.5f;
                var fy = y * sy - 0.5f;
                return (x + SampleField(_dx, fx, fy) / sx, y + SampleField(_dy, fx, fy) / sy);
            });
        }
    }
}
