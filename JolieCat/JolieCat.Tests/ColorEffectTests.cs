using JolieCat.Core.Animation;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class ColorEffectTests
    {
        private static SKColor Through(float[]? m, SKColor c)
        {
            using var b = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            b.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(b))
            using (var paint = new SKPaint { Color = c, ColorFilter = m is null ? null : SKColorFilter.CreateColorMatrix(m) })
                canvas.DrawRect(0, 0, 1, 1, paint);
            return b.GetPixel(0, 0);
        }

        [Fact]
        public void Brightness_Saturation_Hue()
        {
            Assert.Null(LayerKey.ColorEffect(0, 100, 0));
            var red = new SKColor(200, 30, 30);
            Assert.Equal(SKColors.Black, Through(LayerKey.ColorEffect(-100, 100, 0), red));
            Assert.Equal(SKColors.White, Through(LayerKey.ColorEffect(100, 100, 0), red));
            var gray = Through(LayerKey.ColorEffect(0, 0, 0), red);
            Assert.InRange(gray.Red - gray.Green, -1, 1);
            Assert.InRange(gray.Green - gray.Blue, -1, 1);
            var turned = Through(LayerKey.ColorEffect(0, 100, 120), red);    // red turned a third: green-ish
            Assert.True(turned.Green > turned.Red && turned.Green > turned.Blue, turned.ToString());
            var half = Through(LayerKey.ColorEffect(-50, 100, 0), red);
            Assert.InRange((int)half.Red, 98, 102);
        }

        [Fact]
        public void Keys_BlendTheEffects()
        {
            var a = new LayerKey(0) { Brightness = 0, Saturation = 100 };
            var b = new LayerKey(10) { Brightness = -100, Saturation = 0, Hue = 90 };
            var mid = LayerKey.At(new[] { a, b }, 5);
            Assert.InRange(mid.Brightness, -60, -40);
            Assert.InRange(mid.Saturation, 40, 60);
            Assert.False(mid.IsIdentity);
        }
    }
}
