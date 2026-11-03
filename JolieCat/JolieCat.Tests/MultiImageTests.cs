using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class MultiImageTests
    {
        /// <summary>A scene with some shapes, shifted by (dx, dy) and brightened by gain.</summary>
        private static SKBitmap Scene(int dx, int dy, float gain = 1)
        {
            var bitmap = new SKBitmap(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            SKColor C(byte v) => new((byte)Math.Min(255, v * gain), (byte)Math.Min(255, v * gain), (byte)Math.Min(255, v * gain * 0.9f));
            canvas.Clear(C(60));
            using var p1 = new SKPaint { Color = C(200), IsAntialias = true };
            using var p2 = new SKPaint { Color = C(120), IsAntialias = true };
            canvas.DrawCircle(90 + dx, 80 + dy, 35, p1);
            canvas.DrawRect(170 + dx, 60 + dy, 70, 90, p2);
            canvas.DrawRect(40 + dx, 140 + dy, 120, 20, p1);
            return bitmap;
        }

        [Fact]
        public void FindOffset_FindsTheShift_EvenWithADifferentExposure()
        {
            using var reference = Scene(0, 0);
            using var moved = Scene(13, -7, gain: 1.25f);
            var offset = MultiImage.FindOffset(reference, moved);
            Assert.Equal(-13, offset.X);
            Assert.Equal(7, offset.Y);
        }

        [Fact]
        public void FuseExposures_KeepsShadowAndHighlightDetail()
        {
            // Dark shot: the bright side is fine, the dark side is crushed. Bright shot: the opposite.
            using var dark = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bright = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var stripe = (x / 4) % 2 == 0 ? 1 : 0;
                    if (x < 32)
                    {
                        dark.SetPixel(x, y, new SKColor(3, 3, 3));                              // crushed
                        bright.SetPixel(x, y, stripe == 1 ? new SKColor(150, 150, 150) : new SKColor(90, 90, 90));
                    }
                    else
                    {
                        dark.SetPixel(x, y, stripe == 1 ? new SKColor(170, 170, 170) : new SKColor(110, 110, 110));
                        bright.SetPixel(x, y, new SKColor(254, 254, 254));                     // blown out
                    }
                }

            using var fused = MultiImage.FuseExposures(new[] { dark, bright });
            int Contrast(int x0) => Math.Abs(fused.GetPixel(x0, 30).Red - fused.GetPixel(x0 + 4, 30).Red);
            Assert.True(Contrast(10) > 25, $"shadow side detail {Contrast(10)}");
            Assert.True(Contrast(42) > 25, $"highlight side detail {Contrast(42)}");
        }

        [Fact]
        public void FuseExposures_OfOneImage_IsThatImage()
        {
            using var only = Scene(0, 0);
            using var fused = MultiImage.FuseExposures(new[] { only });
            Assert.Equal(only.GetPixel(90, 80), fused.GetPixel(90, 80));
        }
    }
}
