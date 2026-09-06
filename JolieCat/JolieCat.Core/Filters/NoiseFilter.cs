using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>Adds monochrome grain: the same random offset applied to a pixel's R,
    /// G, and B alike (not independent per-channel noise, which reads as color
    /// speckle rather than film-grain-style noise) - direct per-pixel manipulation,
    /// since Skia has no built-in "add noise" image filter to delegate to.</summary>
    public static class NoiseFilter
    {
        /// <param name="intensity">0-100; the maximum random offset (as a percentage
        /// of the full 0-255 range) added to or subtracted from each pixel.</param>
        /// <param name="seed">Fixes the random sequence - used by the live preview so
        /// repainting the same intensity while nothing else changed doesn't visibly
        /// "swim" with a fresh random pattern every frame; omit for a genuinely new
        /// pattern each call (the final commit's own last-previewed look).</param>
        public static SKBitmap Apply(SKBitmap source, float intensity, int? seed = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            intensity = Math.Clamp(intensity, 0f, 100f);

            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            var maxOffset = intensity / 100.0 * 255.0;

            var srcPixels = source.Pixels;
            var dstPixels = new SKColor[srcPixels.Length];

            for (var i = 0; i < srcPixels.Length; i++)
            {
                var c = srcPixels[i];
                var offset = (random.NextDouble() * 2.0 - 1.0) * maxOffset;

                byte Adjust(byte channel) => (byte)Math.Clamp(channel + offset, 0, 255);
                dstPixels[i] = new SKColor(Adjust(c.Red), Adjust(c.Green), Adjust(c.Blue), c.Alpha);
            }

            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Pixels = dstPixels;
            return result;
        }
    }
}
