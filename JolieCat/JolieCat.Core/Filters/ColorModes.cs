using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkiaSharp;

namespace JolieCat.Core.Filters
{
    /// <summary>Image &gt; Mode conversions (Grayscale, Indexed Color, Bitmap) and Image &gt; Apply Image.</summary>
    public static class ColorModes
    {
        /// <summary>Gray by brightness, alpha kept.</summary>
        public static SKBitmap Grayscale(SKBitmap source) => StylizeFilters.Desaturate(source);

        /// <summary>
        /// A palette of up to <paramref name="colors"/> colors (median cut) for the
        /// visible pixels of <paramref name="picture"/>; a sample of at most 250 000
        /// pixels is enough for big pictures.
        /// </summary>
        public static List<SKColor> Palette(SKBitmap picture, int colors)
        {
            colors = Math.Clamp(colors, 2, 256);
            var all = picture.Pixels;
            var step = Math.Max(1, all.Length / 250_000);
            var sample = new SKColor[(all.Length + step - 1) / step];
            for (int i = 0, k = 0; i < all.Length && k < sample.Length; i += step, k++)
            {
                var c = all[i];
                sample[k] = c.Alpha < 128 ? SKColors.Transparent : c.WithAlpha(255);
            }
            var (palette, _, _) = Serialization.GifEncoder.Quantize(sample, colors);
            palette.RemoveAt(0); // entry 0 is the GIF's transparent slot
            if (palette.Count == 0) palette.Add(SKColors.Black);
            return palette;
        }

        /// <summary>
        /// Indexed Color: every pixel becomes the nearest <paramref name="palette"/> color;
        /// with <paramref name="dither"/> the error is spread to the neighbors
        /// (Floyd-Steinberg), so few colors still give smooth-looking shading.
        /// </summary>
        public static SKBitmap Indexed(SKBitmap source, IReadOnlyList<SKColor> palette, bool dither)
        {
            int w = source.Width, h = source.Height;
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            var cache = new int[32 * 32 * 32];
            Array.Fill(cache, -1);

            int Nearest(int r, int g, int b)
            {
                r = Math.Clamp(r, 0, 255); g = Math.Clamp(g, 0, 255); b = Math.Clamp(b, 0, 255);
                var key = (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);
                var hit = cache[key];
                if (hit >= 0) return hit;
                var best = 0;
                var bestD = int.MaxValue;
                for (var i = 0; i < palette.Count; i++)
                {
                    var p = palette[i];
                    int dr = p.Red - r, dg = p.Green - g, db = p.Blue - b;
                    var d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;   // eyes see green differences most
                    if (d < bestD) { bestD = d; best = i; }
                }
                cache[key] = best;
                return best;
            }

            if (!dither)
            {
                for (var i = 0; i < src.Length; i++)
                {
                    var c = src[i];
                    dst[i] = palette[Nearest(c.Red, c.Green, c.Blue)].WithAlpha(c.Alpha);
                }
                return EnhanceFilters.FromPixels(source, dst);
            }

            // Floyd-Steinberg: this row's and the next row's leftover error, per channel.
            var current = new float[(w + 2) * 3];
            var next = new float[(w + 2) * 3];
            for (var y = 0; y < h; y++)
            {
                Array.Clear(next);
                for (var x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    var k = (x + 1) * 3;
                    var r = c.Red + current[k];
                    var g = c.Green + current[k + 1];
                    var b = c.Blue + current[k + 2];
                    var p = palette[Nearest((int)MathF.Round(r), (int)MathF.Round(g), (int)MathF.Round(b))];
                    dst[y * w + x] = p.WithAlpha(c.Alpha);
                    if (c.Alpha == 0) continue;
                    for (var ch = 0; ch < 3; ch++)
                    {
                        var e = ch == 0 ? r - p.Red : ch == 1 ? g - p.Green : b - p.Blue;
                        current[k + 3 + ch] += e * 7 / 16;
                        next[k - 3 + ch] += e * 3 / 16;
                        next[k + ch] += e * 5 / 16;
                        next[k + 3 + ch] += e * 1 / 16;
                    }
                }
                (current, next) = (next, current);
            }
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>Bitmap mode: only black and white (by brightness), at a 50% threshold
        /// or dithered (Floyd-Steinberg) so gray tones show as dot patterns.</summary>
        public static SKBitmap Bitmap(SKBitmap source, bool dither)
        {
            using var gray = Grayscale(source);
            return Indexed(gray, new[] { SKColors.Black, SKColors.White }, dither);
        }

        public enum ApplyBlend { Normal, Multiply, Screen, Overlay, Add, Subtract, Darken, Lighten, Difference }

        /// <summary>One channel of <paramref name="source"/> as an opaque gray picture:
        /// 0 = brightness (gray), 1..3 = red, green, blue, 4 = transparency; inverted if asked.</summary>
        public static SKBitmap Channel(SKBitmap source, int channel, bool invert)
        {
            var src = source.Pixels;
            var dst = new SKColor[src.Length];
            Parallel.For(0, src.Length, i =>
            {
                var c = src[i];
                var v = channel switch
                {
                    1 => c.Red,
                    2 => c.Green,
                    3 => c.Blue,
                    4 => c.Alpha,
                    _ => (byte)Math.Round(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue),
                };
                if (invert) v = (byte)(255 - v);
                dst[i] = new SKColor(v, v, v, 255);
            });
            return EnhanceFilters.FromPixels(source, dst);
        }

        /// <summary>
        /// Image &gt; Calculations: a channel of one picture blended with a channel of another
        /// (same size), giving a gray picture - a mask to make a selection or layer mask from.
        /// </summary>
        public static SKBitmap Calculate(SKBitmap first, int firstChannel, bool firstInvert,
            SKBitmap second, int secondChannel, bool secondInvert, ApplyBlend blend, double opacity)
        {
            using var a = Channel(first, firstChannel, firstInvert);
            using var b = Channel(second, secondChannel, secondInvert);
            // ApplyImage blends b (onto a) channel by channel; both are gray, so any channel works.
            return ApplyImage(a, b, 0, false, blend, opacity);
        }

        /// <summary>
        /// Image &gt; Apply Image: blends <paramref name="source"/> (all of it, or one channel
        /// as gray: 1..3 = R, G, B) onto <paramref name="target"/> at <paramref name="opacity"/>
        /// percent - target's alpha is kept. Both are the same size.
        /// </summary>
        public static SKBitmap ApplyImage(SKBitmap target, SKBitmap source, int channel, bool invert, ApplyBlend blend, double opacity)
        {
            var t = target.Pixels;
            var s = source.Pixels;
            var dst = new SKColor[t.Length];
            var a = (float)Math.Clamp(opacity / 100, 0, 1);

            static float Mix(ApplyBlend mode, float b, float f) => mode switch
            {
                ApplyBlend.Multiply => b * f,
                ApplyBlend.Screen => 1 - (1 - b) * (1 - f),
                ApplyBlend.Overlay => b < 0.5f ? 2 * b * f : 1 - 2 * (1 - b) * (1 - f),
                ApplyBlend.Add => Math.Min(1, b + f),
                ApplyBlend.Subtract => Math.Max(0, b - f),
                ApplyBlend.Darken => Math.Min(b, f),
                ApplyBlend.Lighten => Math.Max(b, f),
                ApplyBlend.Difference => Math.Abs(b - f),
                _ => f,
            };

            Parallel.For(0, t.Length, i =>
            {
                var b = t[i];
                var f = i < s.Length ? s[i] : SKColors.Transparent;
                float fr = f.Red, fg = f.Green, fb = f.Blue;
                if (channel is >= 1 and <= 3) fr = fg = fb = channel == 1 ? f.Red : channel == 2 ? f.Green : f.Blue;
                if (invert) { fr = 255 - fr; fg = 255 - fg; fb = 255 - fb; }
                // Where the source is see-through it does less.
                var k = a * f.Alpha / 255f;
                byte C(byte bv, float fv)
                {
                    var bn = bv / 255f;
                    var m = Mix(blend, bn, fv / 255f);
                    return (byte)Math.Clamp((int)MathF.Round((bn + (m - bn) * k) * 255), 0, 255);
                }
                dst[i] = new SKColor(C(b.Red, fr), C(b.Green, fg), C(b.Blue, fb), b.Alpha);
            });
            return EnhanceFilters.FromPixels(target, dst);
        }
    }
}
