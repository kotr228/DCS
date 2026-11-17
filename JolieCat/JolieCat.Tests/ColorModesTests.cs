using System.Linq;
using JolieCat.Core.Filters;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class ColorModesTests
    {
        private static SKBitmap Gradient()
        {
            var b = new SKBitmap(64, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 64; x++)
                    b.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(255 - x * 4), 90));
            return b;
        }

        [Fact]
        public void Indexed_UsesOnlyThePalette()
        {
            using var src = Gradient();
            var palette = ColorModes.Palette(src, 4);
            Assert.InRange(palette.Count, 2, 4);
            foreach (var dither in new[] { false, true })
            {
                using var r = ColorModes.Indexed(src, palette, dither);
                Assert.All(r.Pixels, c => Assert.Contains(c, palette));
            }
        }

        [Fact]
        public void Bitmap_IsBlackAndWhite_AndDitheringKeepsTheGrayLevel()
        {
            using var gray = new SKBitmap(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            gray.Erase(new SKColor(128, 128, 128));
            using var threshold = ColorModes.Bitmap(gray, dither: false);
            using var dithered = ColorModes.Bitmap(gray, dither: true);
            Assert.All(dithered.Pixels, c => Assert.True(c.Red == 0 || c.Red == 255));
            Assert.Single(threshold.Pixels.Distinct());                 // all one color
            var white = dithered.Pixels.Count(c => c.Red == 255) / (double)dithered.Pixels.Length;
            Assert.InRange(white, 0.4, 0.6);                            // half the dots white
        }

        [Fact]
        public void ApplyImage_BlendsAChannel()
        {
            using var target = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            target.Erase(new SKColor(200, 200, 200));
            using var source = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(255, 0, 0));
            using var multiplyGreen = ColorModes.ApplyImage(target, source, 2, false, ColorModes.ApplyBlend.Multiply, 100);
            Assert.Equal(new SKColor(0, 0, 0), multiplyGreen.GetPixel(1, 1));     // green of red is 0
            using var half = ColorModes.ApplyImage(target, source, 0, false, ColorModes.ApplyBlend.Normal, 50);
            Assert.InRange((int)half.GetPixel(1, 1).Red, 226, 229);
            Assert.InRange((int)half.GetPixel(1, 1).Green, 99, 101);
            using var inverted = ColorModes.ApplyImage(target, source, 1, true, ColorModes.ApplyBlend.Normal, 100);
            Assert.Equal(0, inverted.GetPixel(1, 1).Red);                          // red 255 inverted
        }

        [Fact]
        public void Calculations_BlendTwoChannels_IntoGray()
        {
            using var a = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            a.Erase(new SKColor(200, 100, 0));
            using var b = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            b.Erase(new SKColor(0, 0, 255));
            using var multiply = ColorModes.Calculate(a, 1, false, b, 3, false, ColorModes.ApplyBlend.Multiply, 100);
            Assert.Equal(new SKColor(200, 200, 200), multiply.GetPixel(1, 1));        // 200 x 255/255
            using var add = ColorModes.Calculate(a, 2, true, b, 1, false, ColorModes.ApplyBlend.Add, 100);
            Assert.Equal(155, add.GetPixel(1, 1).Red);                                  // (255-100) + 0
            using var alpha = ColorModes.Channel(a, 4, false);
            Assert.Equal(255, alpha.GetPixel(0, 0).Red);
        }
    }
}
