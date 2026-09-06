using System;

namespace JolieCat.Core.Adjustments
{
    /// <summary>
    /// Classic Levels math: clamp the input range to (<c>InputBlack</c>,
    /// <c>InputWhite</c>), apply gamma, then remap into the (<c>OutputBlack</c>,
    /// <c>OutputWhite</c>) output range - the same three-step pipeline every raster
    /// editor's own Levels dialog uses.
    /// </summary>
    public static class LevelsAdjustment
    {
        public static AdjustmentLut BuildLut(int inputBlack, int inputWhite, double gamma, int outputBlack, int outputWhite)
        {
            inputBlack = Math.Clamp(inputBlack, 0, 255);
            inputWhite = Math.Clamp(inputWhite, 0, 255);
            outputBlack = Math.Clamp(outputBlack, 0, 255);
            outputWhite = Math.Clamp(outputWhite, 0, 255);

            // A collapsed or inverted input range has no meaningful slope to divide
            // by - widen it to the smallest valid range rather than dividing by zero
            // or silently flipping the image.
            if (inputWhite <= inputBlack) inputWhite = inputBlack + 1;
            if (gamma <= 0) gamma = 1;

            var table = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                var t = (i - inputBlack) / (double)(inputWhite - inputBlack);
                t = Math.Clamp(t, 0.0, 1.0);
                t = Math.Pow(t, 1.0 / gamma);
                var value = outputBlack + t * (outputWhite - outputBlack);
                table[i] = (byte)Math.Clamp(Math.Round(value), 0, 255);
            }

            return AdjustmentLut.Uniform(table);
        }
    }
}
