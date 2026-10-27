using System.Diagnostics;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class ContentAwareFillTests
    {
        private static bool[] RectHole(int width, int height, SKRectI rect)
        {
            var hole = new bool[width * height];
            for (var y = rect.Top; y < rect.Bottom; y++)
                for (var x = rect.Left; x < rect.Right; x++)
                    hole[y * width + x] = true;
            return hole;
        }

        [Fact]
        public void Stripes_AreContinuedThroughTheHole()
        {
            // Vertical stripes, 4 px red / 4 px blue.
            const int w = 120, h = 120;
            SKColor Expected(int x) => (x / 4) % 2 == 0 ? SKColors.Red : SKColors.Blue;
            var pixels = new SKColor[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    pixels[y * w + x] = Expected(x);

            var holeRect = new SKRectI(45, 45, 75, 75);
            var hole = RectHole(w, h, holeRect);
            for (var i = 0; i < pixels.Length; i++)
                if (hole[i]) pixels[i] = SKColors.Lime; // the "object" being removed

            var filled = ContentAwareFill.Fill(pixels, w, h, hole);

            // Stripes may come back shifted by whole periods, but each filled column
            // must be one solid stripe colour, the colours must alternate every 4 px,
            // and no trace of the removed object may remain.
            var good = 0;
            var total = 0;
            for (var y = holeRect.Top; y < holeRect.Bottom; y++)
                for (var x = holeRect.Left; x < holeRect.Right; x++)
                {
                    var c = filled[y * w + x];
                    Assert.True(c.Green < 40, $"object still visible at ({x},{y}): {c}");
                    total++;
                    if ((c.Red > 200 && c.Blue < 60) == (Expected(x).Red == 255)) good++;
                }

            Assert.True(good > total * 0.85, $"only {good}/{total} pixels continue the stripes");
        }

        [Fact]
        public void PixelsOutsideTheHole_AreUnchanged()
        {
            const int w = 60, h = 50;
            var random = new Random(3);
            var pixels = new SKColor[w * h];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var hole = RectHole(w, h, new SKRectI(20, 20, 30, 28));

            var filled = ContentAwareFill.Fill(pixels, w, h, hole);

            for (var i = 0; i < pixels.Length; i++)
                if (!hole[i]) Assert.Equal(pixels[i], filled[i]);
        }

        [Fact]
        public void Gradient_IsFilledSmoothly()
        {
            const int w = 160, h = 100;
            var pixels = new SKColor[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    pixels[y * w + x] = new SKColor((byte)(x * 255 / (w - 1)), 80, 80);
            var holeRect = new SKRectI(60, 30, 100, 70);
            var hole = RectHole(w, h, holeRect);

            var filled = ContentAwareFill.Fill(pixels, w, h, hole);

            var totalError = 0.0;
            var n = 0;
            for (var y = holeRect.Top; y < holeRect.Bottom; y++)
                for (var x = holeRect.Left; x < holeRect.Right; x++)
                {
                    totalError += Math.Abs(filled[y * w + x].Red - x * 255 / (w - 1));
                    n++;
                }

            Assert.True(totalError / n < 25, $"mean error {totalError / n:F1}");
        }

        [Fact]
        public void LargeImage_FinishesQuickly()
        {
            const int w = 800, h = 600;
            var pixels = new SKColor[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    pixels[y * w + x] = new SKColor((byte)((x * 7 + y * 3) % 256), (byte)(y % 256), 120);
            var hole = RectHole(w, h, new SKRectI(300, 200, 450, 350));

            var watch = Stopwatch.StartNew();
            ContentAwareFill.Fill(pixels, w, h, hole);
            Assert.True(watch.Elapsed.TotalSeconds < 15, $"took {watch.Elapsed.TotalSeconds:F1}s");
        }

        [Fact]
        public void EmptyHole_ReturnsACopy()
        {
            var pixels = new[] { SKColors.Red, SKColors.Blue };
            var filled = ContentAwareFill.Fill(pixels, 2, 1, new bool[2]);
            Assert.Equal(pixels, filled);
            Assert.NotSame(pixels, filled);
        }
    }
}
