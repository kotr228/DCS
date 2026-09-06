using System;

namespace JolieCat.Core.Adjustments
{
    /// <summary>Simple brightness/contrast: contrast pivots around mid-gray (128)
    /// using the standard "259*(C+255) / (255*(259-C))" contrast-factor formula,
    /// then brightness adds a flat offset - both sliders run -100..100.</summary>
    public static class BrightnessContrastAdjustment
    {
        public static AdjustmentLut BuildLut(double brightness, double contrast)
        {
            brightness = Math.Clamp(brightness, -100, 100);
            contrast = Math.Clamp(contrast, -100, 100);

            var contrastFactor = 259.0 * (contrast + 255.0) / (255.0 * (259.0 - contrast));

            var table = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var value = contrastFactor * (i - 128.0) + 128.0 + brightness * 2.55;
                table[i] = (byte)Math.Clamp(Math.Round(value), 0, 255);
            }

            return AdjustmentLut.Uniform(table);
        }
    }
}
