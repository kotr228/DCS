using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>The sliders of the Camera Raw filter. All are -100..100 except
    /// <see cref="Exposure"/> (stops, -5..5). Zero everywhere = no change.</summary>
    public sealed record CameraRawSettings
    {
        public double Temperature { get; init; }
        public double Tint { get; init; }
        public double Exposure { get; init; }
        public double Contrast { get; init; }
        public double Highlights { get; init; }
        public double Shadows { get; init; }
        public double Whites { get; init; }
        public double Blacks { get; init; }
        public double Clarity { get; init; }
        public double Vibrance { get; init; }
        public double Saturation { get; init; }

        public bool IsNeutral => this == new CameraRawSettings();
    }

    /// <summary>
    /// Photoshop's Camera Raw basics: white balance, exposure, tone (contrast,
    /// highlights, shadows, whites, blacks) and presence (clarity, vibrance,
    /// saturation). Highlights/Shadows look at the surrounding brightness, not just the
    /// pixel, so they recover a bright sky or open up a dark face without flattening
    /// all detail; Clarity boosts local contrast in the midtones.
    /// Create one per image with <see cref="Prepare"/> - the blurred brightness maps
    /// are built once, so moving a slider only redoes the per-pixel math.
    /// </summary>
    public sealed class CameraRawFilter
    {
        private readonly int _width;
        private readonly int _height;
        private readonly SKColor[] _pixels;
        private readonly float[] _wideLuma;   // surroundings, for highlights/shadows
        private readonly float[] _localLuma;  // finer, for clarity

        private CameraRawFilter(SKBitmap source)
        {
            _width = source.Width;
            _height = source.Height;
            _pixels = source.Pixels;
            var longSide = Math.Max(_width, _height);
            var luma = new float[_pixels.Length];
            for (var i = 0; i < luma.Length; i++)
                luma[i] = Luma(_pixels[i].Red / 255f, _pixels[i].Green / 255f, _pixels[i].Blue / 255f);
            _wideLuma = EdgeAwareSmooth(luma, _width, _height, Math.Max(2, (int)(longSide * 0.03f)), 0.01f);
            _localLuma = EdgeAwareSmooth(luma, _width, _height, Math.Max(2, (int)(longSide * 0.01f)), 0.004f);
        }

        public static CameraRawFilter Prepare(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new CameraRawFilter(source);
        }

        /// <summary>One-shot helper.</summary>
        public static SKBitmap Apply(SKBitmap source, CameraRawSettings settings) => Prepare(source).Render(settings);

        public int Width => _width;

        public int Height => _height;

        public SKBitmap Render(CameraRawSettings s)
        {
            ArgumentNullException.ThrowIfNull(s);
            var result = new SKColor[_pixels.Length];
            if (s.IsNeutral)
            {
                Array.Copy(_pixels, result, result.Length);
                return ToBitmap(result);
            }

            // White balance as channel gains: warm = more red, less blue; tint = less green (magenta).
            var t = (float)(s.Temperature / 100);
            var tint = (float)(s.Tint / 100);
            var gainR = 1 + 0.30f * t;
            var gainG = 1 - 0.20f * tint;
            var gainB = 1 - 0.30f * t;
            var exposure = MathF.Pow(2, (float)s.Exposure);

            var contrast = (float)(s.Contrast / 100);
            var highlights = (float)(s.Highlights / 100);
            var shadows = (float)(s.Shadows / 100);
            // Whites/Blacks move the end points of the tone range.
            var whitePoint = 1 - 0.25f * (float)(s.Whites / 100);
            var blackPoint = -0.15f * (float)(s.Blacks / 100);
            var clarity = (float)(s.Clarity / 100);
            var vibrance = (float)(s.Vibrance / 100);
            var saturation = (float)(s.Saturation / 100);

            Parallel.For(0, _height, y =>
            {
                for (var x = 0; x < _width; x++)
                {
                    var i = y * _width + x;
                    var c = _pixels[i];
                    if (c.Alpha == 0) { result[i] = c; continue; }

                    // White balance and exposure in linear light.
                    var r = ToLinear(c.Red) * gainR * exposure;
                    var g = ToLinear(c.Green) * gainG * exposure;
                    var b = ToLinear(c.Blue) * gainB * exposure;
                    r = ToGamma(r); g = ToGamma(g); b = ToGamma(b);

                    var luma = Luma(r, g, b);
                    var target = luma;

                    // Highlights/Shadows: driven by the surroundings' brightness.
                    var around = _wideLuma[i] * exposure;
                    if (highlights != 0)
                    {
                        var w = SmoothStep(0.45f, 1f, around);
                        target += highlights * w * (highlights < 0 ? target * 0.3f : (1 - target) * 0.4f);
                    }
                    if (shadows != 0)
                    {
                        var w = SmoothStep(0.55f, 0f, around);
                        target += shadows * w * (shadows > 0 ? (1 - target) * 0.45f : target * 0.5f);
                    }

                    // Clarity: midtone local contrast.
                    if (clarity != 0)
                    {
                        var detail = target - _localLuma[i] * exposure;
                        var mid = 1 - MathF.Abs(target - 0.5f) * 2;
                        target += clarity * detail * Math.Max(0, mid) * 1.5f;
                    }

                    // Whites/Blacks, then an S-shaped contrast curve.
                    target = (target - blackPoint) / (whitePoint - blackPoint);
                    if (contrast != 0)
                    {
                        var clamped = Math.Clamp(target, 0, 1);
                        var curved = clamped < 0.5f
                            ? 0.5f * MathF.Pow(2 * clamped, 1 + contrast)
                            : 1 - 0.5f * MathF.Pow(2 * (1 - clamped), 1 + contrast);
                        target = contrast > -1 ? curved + (target - clamped) : 0.5f;
                    }

                    // Apply the tone change to all channels, keeping hue.
                    if (luma > 1e-4f)
                    {
                        var k = target / luma;
                        r *= k; g *= k; b *= k;
                    }
                    else
                    {
                        r = g = b = target;
                    }

                    // Vibrance (favours muted colors) and saturation.
                    var l = Luma(r, g, b);
                    var max = MathF.Max(r, MathF.Max(g, b));
                    var min = MathF.Min(r, MathF.Min(g, b));
                    var sat = max > 1e-4f ? (max - min) / max : 0;
                    var boost = 1 + saturation + vibrance * (1 - Math.Clamp(sat, 0, 1));
                    if (boost != 1)
                    {
                        r = l + (r - l) * boost;
                        g = l + (g - l) * boost;
                        b = l + (b - l) * boost;
                    }

                    result[i] = new SKColor(ToByte(r), ToByte(g), ToByte(b), c.Alpha);
                }
            });

            return ToBitmap(result);
        }

        private SKBitmap ToBitmap(SKColor[] pixels)
        {
            var bitmap = new SKBitmap(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Pixels = pixels;
            return bitmap;
        }

        /// <summary>
        /// Smooths <paramref name="input"/> but keeps strong edges (a self-guided
        /// "guided filter"): flat areas are averaged over <paramref name="radius"/>,
        /// while across a sharp boundary - sky against a hill - each side keeps its own
        /// level. That is what stops Highlights/Shadows/Clarity from drawing halos.
        /// <paramref name="epsilon"/> sets how strong an edge must be to be kept.
        /// </summary>
        internal static float[] EdgeAwareSmooth(float[] input, int width, int height, int radius, float epsilon)
        {
            var mean = BoxMean(input, width, height, radius);
            var squares = new float[input.Length];
            for (var i = 0; i < input.Length; i++) squares[i] = input[i] * input[i];
            var meanSquares = BoxMean(squares, width, height, radius);

            var a = new float[input.Length];
            var b = new float[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                var variance = Math.Max(0, meanSquares[i] - mean[i] * mean[i]);
                a[i] = variance / (variance + epsilon);
                b[i] = (1 - a[i]) * mean[i];
            }

            var meanA = BoxMean(a, width, height, radius);
            var meanB = BoxMean(b, width, height, radius);
            var output = new float[input.Length];
            for (var i = 0; i < input.Length; i++) output[i] = meanA[i] * input[i] + meanB[i];
            return output;
        }

        /// <summary>Mean over a (2r+1)^2 window (clipped at the borders), via a
        /// summed-area table - the cost doesn't depend on the radius.</summary>
        private static float[] BoxMean(float[] input, int width, int height, int radius)
        {
            var stride = width + 1;
            var sum = new double[stride * (height + 1)];
            for (var y = 0; y < height; y++)
            {
                double row = 0;
                for (var x = 0; x < width; x++)
                {
                    row += input[y * width + x];
                    sum[(y + 1) * stride + x + 1] = sum[y * stride + x + 1] + row;
                }
            }

            var output = new float[input.Length];
            Parallel.For(0, height, y =>
            {
                var y0 = Math.Max(0, y - radius);
                var y1 = Math.Min(height, y + radius + 1);
                for (var x = 0; x < width; x++)
                {
                    var x0 = Math.Max(0, x - radius);
                    var x1 = Math.Min(width, x + radius + 1);
                    var total = sum[y1 * stride + x1] - sum[y0 * stride + x1] - sum[y1 * stride + x0] + sum[y0 * stride + x0];
                    output[y * width + x] = (float)(total / ((x1 - x0) * (y1 - y0)));
                }
            });
            return output;
        }

        private static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        private static readonly float[] LinearTable = BuildLinearTable();

        private static float[] BuildLinearTable()
        {
            var table = new float[256];
            for (var i = 0; i < 256; i++)
            {
                var v = i / 255f;
                table[i] = v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
            }
            return table;
        }

        private static float ToLinear(byte v) => LinearTable[v];

        private static float ToGamma(float v)
        {
            if (v <= 0) return 0;
            return v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f;
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
    }
}
