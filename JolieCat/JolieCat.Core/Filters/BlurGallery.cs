using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>Settings of a Blur Gallery filter. Positions and sizes are fractions of
    /// the image (0..1), so the same settings fit images of any size.</summary>
    public sealed record BlurGallerySettings
    {
        /// <summary>Tilt-Shift: a sharp band; Iris: a sharp ellipse.</summary>
        public bool Iris { get; init; }

        /// <summary>Center of the sharp area (0..1 of width / height).</summary>
        public double CenterX { get; init; } = 0.5;
        public double CenterY { get; init; } = 0.5;

        /// <summary>Tilt-Shift: half the sharp band's height. Iris: the ellipse's
        /// horizontal radius. Fraction of the image's height (Iris: width).</summary>
        public double Size { get; init; } = 0.15;

        /// <summary>Iris: the ellipse's vertical radius, fraction of the height.</summary>
        public double SizeY { get; init; } = 0.2;

        /// <summary>How far (fraction of the image) the blur takes to reach full strength.</summary>
        public double Transition { get; init; } = 0.2;

        /// <summary>Tilt-Shift band angle, degrees (0 = horizontal).</summary>
        public double Angle { get; init; }

        /// <summary>Full blur radius in pixels.</summary>
        public double Blur { get; init; } = 15;

        /// <summary>Extra color saturation in the blurred parts, % - the "toy model" look.</summary>
        public double Saturation { get; init; }
    }

    /// <summary>
    /// Photoshop's Blur Gallery - Tilt-Shift and Iris Blur. A sharp area fades into
    /// blur: pixels are mixed between the original and two blur strengths by how far
    /// they are from the sharp area. Prepare once per image; rendering is then cheap.
    /// </summary>
    public sealed class BlurGallery
    {
        private readonly SKBitmap _source;
        private readonly float _blur;
        private readonly SKColor[] _sharp;
        private readonly SKColor[] _half;
        private readonly SKColor[] _full;

        private BlurGallery(SKBitmap source, float blur)
        {
            _source = source;
            _blur = blur;
            _sharp = source.Pixels;
            _half = Blurred(source, blur / 2);
            _full = Blurred(source, blur);
        }

        public static BlurGallery Prepare(SKBitmap source, double blur) =>
            new(source ?? throw new ArgumentNullException(nameof(source)), (float)Math.Max(0, blur));

        public double PreparedBlur => _blur;

        public static SKBitmap Apply(SKBitmap source, BlurGallerySettings settings) => Prepare(source, settings.Blur).Render(settings);

        /// <summary>How blurred each point should be: 0 sharp .. 1 fully blurred.</summary>
        public static float Weight(BlurGallerySettings s, float x, float y, int width, int height)
        {
            var transition = (float)Math.Max(1e-4, s.Transition);
            float distance;
            if (s.Iris)
            {
                // Normalised elliptical distance: 1 on the sharp ellipse's edge.
                var rx = (float)Math.Max(1e-4, s.Size) * width;
                var ry = (float)Math.Max(1e-4, s.SizeY) * height;
                var dx = (x - (float)s.CenterX * width) / rx;
                var dy = (y - (float)s.CenterY * height) / ry;
                var r = MathF.Sqrt(dx * dx + dy * dy);
                distance = (r - 1) * Math.Min(rx / width, ry / height); // back to image fractions
            }
            else
            {
                // Distance from the band's center line, measured across the band.
                var radians = (float)(s.Angle * Math.PI / 180);
                var nx = -MathF.Sin(radians);
                var ny = MathF.Cos(radians);
                var across = MathF.Abs((x - (float)s.CenterX * width) * nx + (y - (float)s.CenterY * height) * ny) / height;
                distance = across - (float)s.Size;
            }

            if (distance <= 0) return 0;
            var t = Math.Min(1f, distance / transition);
            return t * t * (3 - 2 * t); // smooth start and end
        }

        public SKBitmap Render(BlurGallerySettings s)
        {
            ArgumentNullException.ThrowIfNull(s);
            int width = _source.Width, height = _source.Height;
            var result = new SKColor[_sharp.Length];
            var saturation = (float)(s.Saturation / 100);

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var w = Weight(s, x + 0.5f, y + 0.5f, width, height);
                    SKColor c;
                    if (w <= 0) { result[i] = _sharp[i]; continue; }
                    c = w < 0.5f ? Mix(_sharp[i], _half[i], w * 2) : Mix(_half[i], _full[i], (w - 0.5f) * 2);

                    if (saturation != 0)
                    {
                        var boost = 1 + saturation * w;
                        var l = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
                        c = new SKColor(B(l + (c.Red - l) * boost), B(l + (c.Green - l) * boost), B(l + (c.Blue - l) * boost), c.Alpha);
                    }
                    result[i] = c;
                }
            });

            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Pixels = result;
            return bitmap;
        }

        private static SKColor[] Blurred(SKBitmap source, float radius)
        {
            if (radius < 0.5f) return source.Pixels;
            using var blurred = BlurFilter.Apply(source, BlurType.Gaussian, radius);
            return blurred.Pixels;
        }

        private static SKColor Mix(SKColor a, SKColor b, float t) => new(
            B(a.Red + (b.Red - a.Red) * t), B(a.Green + (b.Green - a.Green) * t),
            B(a.Blue + (b.Blue - a.Blue) * t), B(a.Alpha + (b.Alpha - a.Alpha) * t));

        private static byte B(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    }
}
