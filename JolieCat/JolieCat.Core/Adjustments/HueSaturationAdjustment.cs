using System;
using SkiaSharp;

namespace JolieCat.Core.Adjustments
{
    /// <summary>
    /// Hue/Saturation/Lightness: unlike Levels/Curves/Brightness-Contrast, a hue
    /// rotation mixes all three color channels together rather than remapping each
    /// independently, so this can't be expressed as an <see cref="AdjustmentLut"/> -
    /// it converts every pixel to HSL, adjusts, and converts back directly.
    /// </summary>
    public static class HueSaturationAdjustment
    {
        /// <summary>Applies a hue shift (degrees, wraps around 360), a saturation
        /// scale (-100..100%, -100 fully desaturates), and a lightness offset
        /// (-100..100%, added directly to each pixel's own lightness) to every pixel
        /// of <paramref name="source"/>, returning a new bitmap - alpha is passed
        /// through unchanged.</summary>
        public static SKBitmap Apply(SKBitmap source, double hueDegrees, double saturationPercent, double lightnessPercent)
        {
            ArgumentNullException.ThrowIfNull(source);

            var hueShift = (((hueDegrees % 360) + 360) % 360) / 360.0;
            var saturationFactor = 1.0 + Math.Clamp(saturationPercent, -100, 100) / 100.0;
            var lightnessDelta = Math.Clamp(lightnessPercent, -100, 100) / 100.0;

            var srcPixels = source.Pixels;
            var dstPixels = new SKColor[srcPixels.Length];

            for (var i = 0; i < srcPixels.Length; i++)
            {
                var c = srcPixels[i];
                var (h, s, l) = RgbToHsl(c.Red, c.Green, c.Blue);

                h = (h + hueShift) % 1.0;
                if (h < 0) h += 1.0;
                s = Math.Clamp(s * saturationFactor, 0.0, 1.0);
                l = Math.Clamp(l + lightnessDelta, 0.0, 1.0);

                var (r, g, b) = HslToRgb(h, s, l);
                dstPixels[i] = new SKColor(r, g, b, c.Alpha);
            }

            var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            result.Pixels = dstPixels;
            return result;
        }

        private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
        {
            var rf = r / 255.0;
            var gf = g / 255.0;
            var bf = b / 255.0;
            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var l = (max + min) / 2.0;

            if (max == min) return (0.0, 0.0, l); // achromatic - hue is undefined, 0 is as good as any

            var d = max - min;
            var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            double h;
            if (max == rf) h = (gf - bf) / d + (gf < bf ? 6.0 : 0.0);
            else if (max == gf) h = (bf - rf) / d + 2.0;
            else h = (rf - gf) / d + 4.0;
            h /= 6.0;

            return (h, s, l);
        }

        private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
        {
            if (s <= 0.0)
            {
                var v = ToByte(l);
                return (v, v, v);
            }

            var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            var p = 2.0 * l - q;
            return (ToByte(HueToRgb(p, q, h + 1.0 / 3.0)), ToByte(HueToRgb(p, q, h)), ToByte(HueToRgb(p, q, h - 1.0 / 3.0)));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
    }
}
