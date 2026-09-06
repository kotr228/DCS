using System;
using SkiaSharp;

namespace JolieCat.Core.Adjustments
{
    /// <summary>Per-channel and luminance pixel-value counts (0-255 each) for one
    /// bitmap - the Levels tool's own live histogram preview. Fully-transparent
    /// pixels are excluded: their color components don't represent anything visible,
    /// and including them would skew the histogram toward whatever color a blank
    /// layer happens to have been cleared to.</summary>
    public sealed class Histogram
    {
        public int[] Red { get; } = new int[256];

        public int[] Green { get; } = new int[256];

        public int[] Blue { get; } = new int[256];

        public int[] Luminance { get; } = new int[256];

        public static Histogram Compute(SKBitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            var histogram = new Histogram();
            foreach (var pixel in bitmap.Pixels)
            {
                if (pixel.Alpha == 0) continue;

                histogram.Red[pixel.Red]++;
                histogram.Green[pixel.Green]++;
                histogram.Blue[pixel.Blue]++;

                var luma = (int)Math.Round(pixel.Red * 0.299 + pixel.Green * 0.587 + pixel.Blue * 0.114);
                histogram.Luminance[Math.Clamp(luma, 0, 255)]++;
            }

            return histogram;
        }
    }
}
