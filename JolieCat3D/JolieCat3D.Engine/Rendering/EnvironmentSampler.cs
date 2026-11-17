using System.Numerics;
using JolieCat3D.Core.Materials;

namespace JolieCat3D.Engine.Rendering
{
    /// <summary>
    /// Importance sampling of an equirectangular HDR environment (Cycles' "multiple
    /// importance" world sampling): directions are picked in proportion to the
    /// panorama's brightness, so a small bright sun casts sharp, noise-free shadows
    /// instead of being found by chance. Uses the same mapping and rotation as
    /// <see cref="HdrImage.SampleEquirect"/>.
    /// </summary>
    internal sealed class EnvironmentSampler
    {
        private readonly HdrImage _image;
        private readonly float _strength;
        private readonly Quaternion _toWorld;
        private readonly Quaternion _toLocal;
        private readonly float[] _weights;   // per pixel: luminance * sin(theta)
        private readonly float[] _rowCdf;    // H + 1
        private readonly float[] _colCdf;    // H * (W + 1)
        private readonly float _total;

        public float RotationDegrees { get; }

        private EnvironmentSampler(HdrImage image, float rotationDegrees, float strength, float[] weights, float[] rowCdf, float[] colCdf, float total)
        {
            _image = image;
            _strength = strength;
            RotationDegrees = rotationDegrees;
            var angle = rotationDegrees * MathF.PI / 180f;
            _toWorld = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
            _toLocal = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angle);
            _weights = weights;
            _rowCdf = rowCdf;
            _colCdf = colCdf;
            _total = total;
        }

        /// <summary>Null when the panorama is completely black (nothing to sample).</summary>
        public static EnvironmentSampler? Build(HdrImage image, float rotationDegrees, float strength)
        {
            int w = image.Width, h = image.Height;
            var weights = new float[w * h];
            var rowCdf = new float[h + 1];
            var colCdf = new float[h * (w + 1)];
            for (var y = 0; y < h; y++)
            {
                var sinTheta = MathF.Sin((y + 0.5f) / h * MathF.PI);
                var rowSum = 0f;
                colCdf[y * (w + 1)] = 0f;
                for (var x = 0; x < w; x++)
                {
                    var c = image[x, y];
                    var lum = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
                    var weight = float.IsFinite(lum) && lum > 0 ? lum * sinTheta : 0f;
                    weights[y * w + x] = weight;
                    rowSum += weight;
                    colCdf[y * (w + 1) + x + 1] = rowSum;
                }
                rowCdf[y + 1] = rowCdf[y] + rowSum;
            }
            var total = rowCdf[h];
            return total > 0 && strength > 0 ? new EnvironmentSampler(image, rotationDegrees, strength, weights, rowCdf, colCdf, total) : null;
        }

        /// <summary>Picks a direction; returns its radiance and solid-angle pdf.</summary>
        public bool Sample(Random rng, out Vector3 direction, out Vector3 radiance, out float pdf)
        {
            int w = _image.Width, h = _image.Height;
            var y = Search(_rowCdf, 0, h, rng.NextSingle() * _total);
            var rowStart = y * (w + 1);
            var x = Search(_colCdf, rowStart, w, rng.NextSingle() * _colCdf[rowStart + w]);

            var u = (x + rng.NextSingle()) / w;
            var v = (y + rng.NextSingle()) / h;
            var theta = v * MathF.PI;
            var phi = (u - 0.5f) * 2f * MathF.PI;
            var sinTheta = MathF.Sin(theta);
            direction = Vector3.Zero;
            radiance = Vector3.Zero;
            pdf = 0;
            if (sinTheta < 1e-6f) return false;

            var local = new Vector3(sinTheta * MathF.Sin(phi), MathF.Cos(theta), -sinTheta * MathF.Cos(phi));
            direction = Vector3.Normalize(Vector3.Transform(local, _toWorld));
            pdf = _weights[y * w + x] / _total * w * h / (2f * MathF.PI * MathF.PI * sinTheta);
            if (!(pdf > 0)) return false;
            radiance = _image.SampleEquirect(direction, RotationDegrees) * _strength;
            return true;
        }

        /// <summary>The pdf <see cref="Sample"/> would give <paramref name="direction"/>.</summary>
        public float Pdf(Vector3 direction)
        {
            int w = _image.Width, h = _image.Height;
            var local = Vector3.Transform(direction, _toLocal);
            var length = local.Length();
            if (!(length > 1e-12f)) return 0;
            local /= length;
            var sinTheta = MathF.Sqrt(MathF.Max(0, 1f - local.Y * local.Y));
            if (sinTheta < 1e-6f) return 0;
            var u = 0.5f + MathF.Atan2(local.X, -local.Z) / (2f * MathF.PI);
            var v = MathF.Acos(Math.Clamp(local.Y, -1f, 1f)) / MathF.PI;
            var x = Math.Clamp((int)(u * w), 0, w - 1);
            var y = Math.Clamp((int)(v * h), 0, h - 1);
            return _weights[y * w + x] / _total * w * h / (2f * MathF.PI * MathF.PI * sinTheta);
        }

        /// <summary>Index i in [0, count) with cdf[start+i] &lt;= value &lt; cdf[start+i+1], skipping empty bins.</summary>
        private static int Search(float[] cdf, int start, int count, float value)
        {
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (cdf[start + mid + 1] <= value) lo = mid + 1;
                else hi = mid;
            }
            // Never land on a zero-weight bin (value exactly on a boundary).
            while (lo < count - 1 && cdf[start + lo + 1] - cdf[start + lo] <= 0) lo++;
            while (lo > 0 && cdf[start + lo + 1] - cdf[start + lo] <= 0) lo--;
            return lo;
        }

        /// <summary>Power-heuristic weight of a strategy with pdf <paramref name="a"/> against one with <paramref name="b"/>.</summary>
        public static float PowerHeuristic(float a, float b)
        {
            var a2 = a * a;
            var sum = a2 + b * b;
            return sum > 0 ? a2 / sum : 0f;
        }
    }
}
