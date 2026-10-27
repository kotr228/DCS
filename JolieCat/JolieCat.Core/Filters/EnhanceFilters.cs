using System;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>
    /// Photo-style filters and one-click corrections: Motion Blur, Unsharp Mask,
    /// Median, Auto Contrast, Auto Color and Vibrance. Like every other filter, each
    /// returns a new bitmap and leaves the source untouched. Rows are processed in
    /// parallel so live previews stay responsive on large layers.
    /// </summary>
    public static class EnhanceFilters
    {
        /// <summary>Smears pixels along a line at <paramref name="angleDegrees"/>
        /// (0 = horizontal), <paramref name="distance"/> pixels long, centered on each pixel.
        /// Averaging is alpha-weighted, so transparent areas don't darken the streaks.</summary>
        public static SKBitmap MotionBlur(SKBitmap source, float angleDegrees, float distance)
        {
            ArgumentNullException.ThrowIfNull(source);
            distance = Math.Clamp(distance, 0f, 500f);
            if (distance < 1f) return source.Copy();

            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];

            var radians = angleDegrees * MathF.PI / 180f;
            var dx = MathF.Cos(radians);
            var dy = -MathF.Sin(radians); // screen y points down; positive angles go up-right like Photoshop
            var samples = Math.Max(2, (int)MathF.Ceiling(distance) + 1);
            var offsets = new (int X, int Y)[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = -distance / 2f + distance * i / (samples - 1);
                offsets[i] = ((int)MathF.Round(dx * t), (int)MathF.Round(dy * t));
            }

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    long r = 0, g = 0, b = 0, a = 0;
                    foreach (var (ox, oy) in offsets)
                    {
                        var sx = Math.Clamp(x + ox, 0, width - 1);
                        var sy = Math.Clamp(y + oy, 0, height - 1);
                        var c = src[sy * width + sx];
                        r += c.Red * c.Alpha;
                        g += c.Green * c.Alpha;
                        b += c.Blue * c.Alpha;
                        a += c.Alpha;
                    }

                    dst[y * width + x] = a == 0
                        ? SKColors.Transparent
                        : new SKColor((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / samples));
                }
            });

            return FromPixels(source, dst);
        }

        /// <summary>Classic darkroom sharpening: adds back <paramref name="amountPercent"/>%
        /// of the difference between the image and a Gaussian blur of
        /// <paramref name="radius"/> px. Small radius = fine detail, large = local contrast.</summary>
        public static SKBitmap UnsharpMask(SKBitmap source, float radius, float amountPercent)
        {
            ArgumentNullException.ThrowIfNull(source);
            radius = Math.Clamp(radius, 0.1f, 250f);
            var amount = Math.Clamp(amountPercent, 0f, 500f) / 100f;

            using var blurred = BlurFilter.Apply(source, BlurType.Gaussian, radius);
            var src = source.Pixels;
            var blur = blurred.Pixels;
            var dst = new SKColor[src.Length];
            var width = source.Width;

            Parallel.For(0, source.Height, y =>
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    var i = row + x;
                    var c = src[i];
                    if (c.Alpha == 0) continue;

                    var bl = blur[i];
                    dst[i] = new SKColor(
                        Clamp(c.Red + amount * (c.Red - bl.Red)),
                        Clamp(c.Green + amount * (c.Green - bl.Green)),
                        Clamp(c.Blue + amount * (c.Blue - bl.Blue)),
                        c.Alpha);
                }
            });

            return FromPixels(source, dst);
        }

        /// <summary>Replaces each channel with the median of a square neighbourhood of
        /// <paramref name="radius"/> px - removes dust, scratches and speckle noise while
        /// keeping edges sharper than a blur would. Uses Huang's sliding histogram, so
        /// the cost grows with the radius, not its square.</summary>
        public static SKBitmap Median(SKBitmap source, int radius)
        {
            ArgumentNullException.ThrowIfNull(source);
            radius = Math.Clamp(radius, 1, 50);

            var width = source.Width;
            var height = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var windowSize = (2 * radius + 1) * (2 * radius + 1);
            var half = windowSize / 2;

            Parallel.For(0, height, y =>
            {
                var hist = new int[3, 256];
                var median = new int[3];
                var below = new int[3]; // how many window values are < median, per channel

                void Add(SKColor c, int sign)
                {
                    Update(0, c.Red, sign);
                    Update(1, c.Green, sign);
                    Update(2, c.Blue, sign);
                }

                void Update(int ch, int v, int sign)
                {
                    hist[ch, v] += sign;
                    if (v < median[ch]) below[ch] += sign;
                }

                SKColor Pixel(int px, int py) =>
                    src[Math.Clamp(py, 0, height - 1) * width + Math.Clamp(px, 0, width - 1)];

                for (var ky = -radius; ky <= radius; ky++)
                    for (var kx = -radius; kx <= radius; kx++)
                        Add(Pixel(kx, y + ky), +1);

                for (var x = 0; x < width; x++)
                {
                    if (x > 0)
                    {
                        for (var ky = -radius; ky <= radius; ky++)
                        {
                            Add(Pixel(x - radius - 1, y + ky), -1);
                            Add(Pixel(x + radius, y + ky), +1);
                        }
                    }

                    // Slide each channel's median until exactly `half` values sit below it.
                    for (var ch = 0; ch < 3; ch++)
                    {
                        while (below[ch] > half)
                        {
                            median[ch]--;
                            below[ch] -= hist[ch, median[ch]];
                        }

                        while (below[ch] + hist[ch, median[ch]] <= half)
                        {
                            below[ch] += hist[ch, median[ch]];
                            median[ch]++;
                        }
                    }

                    var i = y * width + x;
                    dst[i] = new SKColor((byte)median[0], (byte)median[1], (byte)median[2], src[i].Alpha);
                }
            });

            return FromPixels(source, dst);
        }

        /// <summary>Stretches the overall brightness range so the darkest 0.1% becomes
        /// black and the brightest 0.1% white - the same stretch on every channel, so
        /// colors don't shift.</summary>
        public static SKBitmap AutoContrast(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var pixels = source.Pixels;
            var hist = new long[256];
            foreach (var c in pixels)
                if (c.Alpha != 0)
                {
                    hist[c.Red]++;
                    hist[c.Green]++;
                    hist[c.Blue]++;
                }

            var (low, high) = ClipRange(hist);
            var lut = StretchLut(low, high);
            return MapPixels(source, c => new SKColor(lut[c.Red], lut[c.Green], lut[c.Blue], c.Alpha));
        }

        /// <summary>Stretches each channel on its own (0.1% clipping) - removes a color
        /// cast as well as boosting contrast.</summary>
        public static SKBitmap AutoColor(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var pixels = source.Pixels;
            var hr = new long[256];
            var hg = new long[256];
            var hb = new long[256];
            foreach (var c in pixels)
                if (c.Alpha != 0)
                {
                    hr[c.Red]++;
                    hg[c.Green]++;
                    hb[c.Blue]++;
                }

            var lr = StretchLut(ClipRange(hr));
            var lg = StretchLut(ClipRange(hg));
            var lb = StretchLut(ClipRange(hb));
            return MapPixels(source, c => new SKColor(lr[c.Red], lg[c.Green], lb[c.Blue], c.Alpha));
        }

        /// <summary>Boosts (positive) or mutes (negative, down to -100) saturation,
        /// affecting already-saturated colors less than dull ones - so skin tones and
        /// skies don't go neon the way a plain Saturation slider would.</summary>
        public static SKBitmap Vibrance(SKBitmap source, float amount)
        {
            var strength = Math.Clamp(amount, -100f, 100f) / 100f;
            return MapPixels(source, c =>
            {
                var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                var saturation = max == 0 ? 0f : (max - min) / (float)max;
                var gray = 0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue;

                // Dull colors get the full push, saturated ones almost none.
                var factor = 1f + strength * (strength > 0 ? 1f - saturation : 1f);
                return new SKColor(
                    Clamp(gray + (c.Red - gray) * factor),
                    Clamp(gray + (c.Green - gray) * factor),
                    Clamp(gray + (c.Blue - gray) * factor),
                    c.Alpha);
            });
        }

        private static (int Low, int High) ClipRange(long[] hist)
        {
            long total = 0;
            foreach (var n in hist) total += n;
            if (total == 0) return (0, 255);

            var clip = (long)(total * 0.001);
            int low = 0, high = 255;
            for (long acc = 0; low < 255; low++)
            {
                acc += hist[low];
                if (acc > clip) break;
            }

            for (long acc = 0; high > 0; high--)
            {
                acc += hist[high];
                if (acc > clip) break;
            }

            return high <= low ? (0, 255) : (low, high);
        }

        private static byte[] StretchLut((int Low, int High) range) => StretchLut(range.Low, range.High);

        private static byte[] StretchLut(int low, int high)
        {
            var lut = new byte[256];
            for (var i = 0; i < 256; i++)
                lut[i] = Clamp((i - low) * 255f / (high - low));
            return lut;
        }

        private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

        internal static SKBitmap MapPixels(SKBitmap source, Func<SKColor, SKColor> map)
        {
            ArgumentNullException.ThrowIfNull(source);
            var pixels = source.Pixels;
            var width = source.Width;

            Parallel.For(0, source.Height, y =>
            {
                var end = (y + 1) * width;
                for (var i = y * width; i < end; i++)
                {
                    var c = pixels[i];
                    if (c.Alpha != 0) pixels[i] = map(c);
                }
            });

            return FromPixels(source, pixels);
        }

        internal static SKBitmap FromPixels(SKBitmap source, SKColor[] pixels)
        {
            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Pixels = pixels;
            return result;
        }
    }
}
