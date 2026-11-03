using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class PhotoFilterTests
    {
        private static SKBitmap Gradient(SKColor from, SKColor to, int size = 40)
        {
            var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            for (var x = 0; x < size; x++)
            {
                var t = x / (float)(size - 1);
                var c = new SKColor((byte)(from.Red + (to.Red - from.Red) * t), (byte)(from.Green + (to.Green - from.Green) * t),
                    (byte)(from.Blue + (to.Blue - from.Blue) * t));
                for (var y = 0; y < size; y++) bitmap.SetPixel(x, y, c);
            }
            return bitmap;
        }

        [Fact]
        public void MatchColor_TakesOnTheSourcesColors()
        {
            using var target = Gradient(new SKColor(20, 40, 120), new SKColor(80, 110, 220));   // cool, bluish
            using var source = Gradient(new SKColor(140, 80, 20), new SKColor(250, 190, 90));   // warm, orange
            var wanted = ColorStats.Of(source);

            using var matched = PhotoFilters.MatchColor(target, wanted, 100, 100, 0, neutralize: false);
            var got = ColorStats.Of(matched);
            Assert.InRange(got.MeanY, wanted.MeanY - 4, wanted.MeanY + 4);
            Assert.InRange(got.MeanCr, wanted.MeanCr - 6, wanted.MeanCr + 6);
            var mid = matched.GetPixel(20, 20);
            Assert.True(mid.Red > mid.Blue, $"warm now: {mid}");
        }

        [Fact]
        public void MatchColor_NeutralizeRemovesACast_AndFadeKeepsTheOriginal()
        {
            using var target = Gradient(new SKColor(60, 90, 160), new SKColor(120, 150, 230));
            using var neutral = PhotoFilters.MatchColor(target, null, 100, 100, 0, neutralize: true);
            var stats = ColorStats.Of(neutral);
            Assert.InRange(stats.MeanCb, -3, 3);
            Assert.InRange(stats.MeanCr, -3, 3);

            using var faded = PhotoFilters.MatchColor(target, null, 100, 100, 100, neutralize: true);
            Assert.Equal(target.GetPixel(5, 5), faded.GetPixel(5, 5));
        }

        [Fact]
        public void LensBlur_SpreadsAPointIntoADisc_AndBlooms()
        {
            using var source = new SKBitmap(61, 61, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(20, 20, 20));
            using (var canvas = new SKCanvas(source))
            using (var white = new SKPaint { Color = SKColors.White })
                canvas.DrawRect(29, 29, 3, 3, white);

            using var plain = PhotoFilters.LensBlur(source, 10, 0, 230);
            // Flat inside the disc, nothing past its edge.
            Assert.InRange(plain.GetPixel(30 + 5, 30).Red - plain.GetPixel(30, 30).Red, -3, 3);
            Assert.InRange(plain.GetPixel(30 + 14, 30).Red, 19, 21);

            using var bokeh = PhotoFilters.LensBlur(source, 10, 100, 200);
            Assert.True(bokeh.GetPixel(30, 30).Red > plain.GetPixel(30, 30).Red + 10);
        }

        [Fact]
        public void LensBlur_ZeroRadius_ChangesNothing()
        {
            using var source = Gradient(SKColors.Black, SKColors.White);
            using var same = PhotoFilters.LensBlur(source, 0, 50, 200);
            Assert.Equal(source.GetPixel(13, 7), same.GetPixel(13, 7));
        }
    }
}
