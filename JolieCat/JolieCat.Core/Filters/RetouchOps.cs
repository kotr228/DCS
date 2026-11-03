using System;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>Pixel work behind the Red Eye and Background Eraser tools.</summary>
    public static class RetouchOps
    {
        /// <summary>
        /// Red Eye: inside the circle, strongly red pixels lose their red (it is pulled to
        /// the green/blue level) and get darker, by <paramref name="darken"/> (0..1).
        /// Returns how many pixels changed.
        /// </summary>
        public static int RemoveRedEye(SKColor[] pixels, int width, int height, SKPoint center, float radius, float darken)
        {
            var changed = 0;
            var left = Math.Max(0, (int)(center.X - radius));
            var right = Math.Min(width - 1, (int)(center.X + radius));
            var top = Math.Max(0, (int)(center.Y - radius));
            var bottom = Math.Min(height - 1, (int)(center.Y + radius));
            darken = Math.Clamp(darken, 0, 1);

            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var dx = x + 0.5f - center.X;
                    var dy = y + 0.5f - center.Y;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > radius) continue;

                    var i = y * width + x;
                    var c = pixels[i];
                    if (c.Alpha == 0) continue;
                    var other = Math.Max(c.Green, c.Blue);
                    if (c.Red < 60 || c.Red < other * 1.3f) continue;

                    // How red (0..1), softened at the circle's rim.
                    var redness = Math.Clamp((c.Red - other * 1.3f) / 80f + 0.4f, 0, 1);
                    var edge = Math.Clamp((radius - distance) / Math.Max(1, radius * 0.25f), 0, 1);
                    var s = redness * edge;

                    var target = (c.Green + c.Blue) / 2f;
                    var dark = 1 - darken * 0.6f * s;
                    pixels[i] = new SKColor(
                        (byte)Math.Clamp((c.Red + (target - c.Red) * s) * dark, 0, 255),
                        (byte)Math.Clamp(c.Green * dark, 0, 255),
                        (byte)Math.Clamp(c.Blue * dark, 0, 255),
                        c.Alpha);
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>
        /// Background Eraser: how much of each pixel of <paramref name="area"/> to erase
        /// (as the alpha of a black bitmap), 1 for colors at <paramref name="sample"/>,
        /// falling to 0 at <paramref name="tolerance"/> (0..255) away.
        /// </summary>
        public static SKBitmap EraseMask(SKBitmap area, SKColor sample, float tolerance)
        {
            var pixels = area.Pixels;
            var reach = Math.Max(1f, tolerance) * 1.5f;
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.Alpha == 0) { pixels[i] = SKColors.Transparent; continue; }
                float dr = c.Red - sample.Red, dg = c.Green - sample.Green, db = c.Blue - sample.Blue;
                var distance = MathF.Sqrt(dr * dr + dg * dg + db * db);
                // Full strength inside two thirds of the reach, then a soft fade.
                var strength = Math.Clamp((reach - distance) / (reach / 3f), 0, 1);
                pixels[i] = new SKColor(0, 0, 0, (byte)Math.Round(strength * 255));
            }
            var mask = new SKBitmap(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            mask.Pixels = pixels;
            return mask;
        }
    }
}
