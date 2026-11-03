using System;
using System.Threading.Tasks;
using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>Image &gt; Adjustments that look at the whole image or the pixel's
    /// surroundings: Equalize, Shadows/Highlights and Replace Color.</summary>
    public static class ToneFilters
    {
        /// <summary>Spreads the brightness values evenly over the full range, using the
        /// histogram of all three channels together (so colors keep their balance).</summary>
        public static SKBitmap Equalize(SKBitmap source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var counts = new long[256];
            long total = 0;
            foreach (var c in source.Pixels)
            {
                if (c.Alpha == 0) continue;
                counts[c.Red]++; counts[c.Green]++; counts[c.Blue]++;
                total += 3;
            }
            if (total == 0) return source.Copy();

            long first = 0;
            for (var i = 0; i < 256; i++) if (counts[i] > 0) { first = counts[i]; break; }
            var table = new byte[256];
            long acc = 0;
            for (var i = 0; i < 256; i++)
            {
                acc += counts[i];
                table[i] = total == first ? (byte)i : (byte)Math.Clamp(Math.Round((acc - first) * 255.0 / (total - first)), 0, 255);
            }
            return EnhanceFilters.MapPixels(source, c => new SKColor(table[c.Red], table[c.Green], table[c.Blue], c.Alpha));
        }

        /// <summary>
        /// Brightens dark areas and darkens bright ones, judged by the blurred brightness
        /// around each pixel (so whole regions move together and edges keep their contrast).
        /// </summary>
        /// <param name="shadows">0..100: how much to lift the shadows.</param>
        /// <param name="highlights">0..100: how much to bring down the highlights.</param>
        /// <param name="tone">1..100: how far into the midtones each correction reaches.</param>
        /// <param name="radius">Size of the neighborhood, in pixels.</param>
        public static SKBitmap ShadowsHighlights(SKBitmap source, double shadows, double highlights, double tone, double radius)
        {
            ArgumentNullException.ThrowIfNull(source);
            int w = source.Width, h = source.Height;
            var pixels = source.Pixels;

            // Brightness map, blurred.
            using var luma = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            var lumaPixels = new SKColor[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                var l = (byte)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
                lumaPixels[i] = new SKColor(l, l, l, 255);
            }
            luma.Pixels = lumaPixels;
            using var blurred = radius > 0.5 ? BlurFilter.Apply(luma, BlurType.Gaussian, (float)(radius / 2)) : luma.Copy();
            var local = blurred.Pixels;

            var s = Math.Clamp(shadows, 0, 100) / 100 * 2.5;
            var hl = Math.Clamp(highlights, 0, 100) / 100 * 2.5;
            var reach = Math.Clamp(tone, 1, 100) / 100;

            Parallel.For(0, h, y =>
            {
                for (var i = y * w; i < (y + 1) * w; i++)
                {
                    var c = pixels[i];
                    if (c.Alpha == 0) continue;
                    var b = local[i].Red / 255.0;
                    var ws = Math.Clamp(1 - b / reach, 0, 1);
                    var wh = Math.Clamp((b - (1 - reach)) / reach, 0, 1);
                    var liftGamma = 1 + s * ws * ws;
                    var dropGamma = 1 + hl * wh * wh;

                    byte Map(byte v)
                    {
                        var x = v / 255.0;
                        x = Math.Pow(x, 1 / liftGamma);
                        x = 1 - Math.Pow(1 - x, 1 / dropGamma);
                        return (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
                    }

                    pixels[i] = new SKColor(Map(c.Red), Map(c.Green), Map(c.Blue), c.Alpha);
                }
            });

            var result = new SKBitmap(source.Info);
            result.Pixels = pixels;
            return result;
        }

        /// <summary>
        /// Shifts hue, saturation and lightness only where the image is close to
        /// <paramref name="color"/>; <paramref name="fuzziness"/> (0..200) sets how close.
        /// </summary>
        public static SKBitmap ReplaceColor(SKBitmap source, SKColor color, double fuzziness, double hue, double saturation, double lightness)
        {
            ArgumentNullException.ThrowIfNull(source);
            var shifted = new SKBitmap(source.Info);
            using (var canvas = new SKCanvas(shifted))
            using (var filter = new AdjustmentSettings(AdjustmentKind.HueSaturation, new[] { hue, saturation, lightness }).CreateColorFilter())
            using (var paint = new SKPaint { ColorFilter = filter })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, 0, 0, paint);
            }

            var original = source.Pixels;
            var changed = shifted.Pixels;
            var reach = Math.Max(1, Math.Clamp(fuzziness, 0, 200)) * 1.2;
            Parallel.For(0, source.Height, y =>
            {
                for (var i = y * source.Width; i < (y + 1) * source.Width; i++)
                {
                    var c = original[i];
                    if (c.Alpha == 0) continue;
                    var dr = c.Red - color.Red;
                    var dg = c.Green - color.Green;
                    var db = c.Blue - color.Blue;
                    var weight = Math.Clamp(1 - Math.Sqrt(dr * dr + dg * dg + db * db) / reach, 0, 1);
                    if (weight <= 0) continue;
                    var n = changed[i];
                    original[i] = new SKColor(
                        (byte)Math.Round(c.Red + (n.Red - c.Red) * weight),
                        (byte)Math.Round(c.Green + (n.Green - c.Green) * weight),
                        (byte)Math.Round(c.Blue + (n.Blue - c.Blue) * weight),
                        c.Alpha);
                }
            });
            shifted.Pixels = original;
            return shifted;
        }
    }
}
