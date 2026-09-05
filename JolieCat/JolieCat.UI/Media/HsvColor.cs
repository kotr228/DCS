using System;

namespace JolieCat.UI.Media
{
    /// <summary>
    /// HSV&lt;-&gt;RGB conversion for the Photoshop-style color picker (<see cref="Controls.HsvColorPicker"/>
    /// and <see cref="ViewModels.CanvasViewModel"/>'s Hue/Saturation/Brightness properties).
    /// Kept as plain math with no WPF or SkiaSharp dependency - both call in with their
    /// own color type's channel bytes.
    /// </summary>
    public static class HsvColor
    {
        /// <summary>Converts 0-255 RGB channels to Hue (0-360), Saturation (0-1), and
        /// Value/Brightness (0-1).</summary>
        public static (double Hue, double Saturation, double Value) FromRgb(byte r, byte g, byte b)
        {
            var rf = r / 255.0;
            var gf = g / 255.0;
            var bf = b / 255.0;

            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var delta = max - min;

            double hue;
            if (delta < 1e-9) hue = 0;
            else if (max == rf) hue = 60.0 * (((gf - bf) / delta) % 6.0);
            else if (max == gf) hue = 60.0 * (((bf - rf) / delta) + 2.0);
            else hue = 60.0 * (((rf - gf) / delta) + 4.0);
            if (hue < 0) hue += 360.0;

            var saturation = max <= 0 ? 0 : delta / max;

            return (hue, saturation, max);
        }

        /// <summary>Converts Hue (0-360)/Saturation (0-1)/Value-Brightness (0-1) back to
        /// 0-255 RGB channels.</summary>
        public static (byte R, byte G, byte B) ToRgb(double hue, double saturation, double value)
        {
            hue = ((hue % 360.0) + 360.0) % 360.0;
            saturation = Math.Clamp(saturation, 0.0, 1.0);
            value = Math.Clamp(value, 0.0, 1.0);

            var c = value * saturation;
            var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
            var m = value - c;

            var (r1, g1, b1) = hue switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };

            return (ToByte(r1 + m), ToByte(g1 + m), ToByte(b1 + m));
        }

        private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255.0), 0, 255);
    }
}
