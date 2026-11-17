using JolieCat.Core.Filters;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class MoreFiltersTests
    {
        private static SKBitmap Halves()
        {
            var b = new SKBitmap(60, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var c = new SKCanvas(b);
            c.Clear(SKColors.Black);
            using var white = new SKPaint { Color = SKColors.White };
            c.DrawRect(0, 0, 30, 40, white);
            return b;
        }

        [Fact]
        public void Solarize_TurnsBrightTonesOver()
        {
            using var src = Halves();
            using var r = MoreFilters.Solarize(src);
            Assert.Equal(0, r.GetPixel(5, 5).Red);       // white -> black
            Assert.Equal(0, r.GetPixel(50, 5).Red);      // black stays
        }

        [Fact]
        public void Wind_StreaksBrightPartsToTheRight()
        {
            using var src = Halves();
            using var r = MoreFilters.Wind(src, 20);
            Assert.True(r.GetPixel(35, 20).Red > 100);   // just past the white edge
            Assert.Equal(0, r.GetPixel(55, 20).Red);     // far away: untouched
        }

        [Fact]
        public void MaximumAndMinimum_GrowLightOrDark()
        {
            using var src = Halves();
            using var max = MoreFilters.MaximumMinimum(src, 3, grow: true);
            using var min = MoreFilters.MaximumMinimum(src, 3, grow: false);
            Assert.Equal(255, max.GetPixel(32, 20).Red);
            Assert.Equal(0, min.GetPixel(28, 20).Red);
        }

        [Fact]
        public void Maximum_MatchesTheSlowWay()
        {
            var rnd = new System.Random(3);
            using var src = new SKBitmap(37, 23, SKColorType.Rgba8888, SKAlphaType.Premul);
            for (var y = 0; y < 23; y++)
                for (var x = 0; x < 37; x++)
                    src.SetPixel(x, y, new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 255));
            foreach (var r in new[] { 1, 2, 5, 9 })
            {
                using var fast = MoreFilters.MaximumMinimum(src, r, grow: true);
                for (var y = 0; y < 23; y++)
                    for (var x = 0; x < 37; x++)
                    {
                        var best = 0;
                        for (var yy = System.Math.Max(0, y - r); yy <= System.Math.Min(22, y + r); yy++)
                            for (var xx = System.Math.Max(0, x - r); xx <= System.Math.Min(36, x + r); xx++)
                                best = System.Math.Max(best, src.GetPixel(xx, yy).Red);
                        Assert.Equal(best, fast.GetPixel(x, y).Red);
                    }
            }
        }

        [Fact]
        public void Offset_WrapsAround()
        {
            using var src = Halves();
            using var r = MoreFilters.Offset(src, 30, 0);
            Assert.Equal(0, r.GetPixel(5, 5).Red);       // the black half came round to the left
            Assert.Equal(255, r.GetPixel(45, 5).Red);
        }

        [Fact]
        public void Average_FillsWithTheMeanColor()
        {
            using var src = Halves();
            using var r = MoreFilters.Average(src);
            Assert.InRange((int)r.GetPixel(10, 10).Red, 125, 130);
            Assert.Equal(r.GetPixel(10, 10), r.GetPixel(50, 30));
        }

        [Fact]
        public void TheOthers_KeepTheSizeAndRun()
        {
            using var src = Halves();
            foreach (var r in new[]
            {
                MoreFilters.PolarCoordinates(src, true), MoreFilters.PolarCoordinates(src, false),
                MoreFilters.Diffuse(src, 4), MoreFilters.Crystallize(src, 8), MoreFilters.ColorHalftone(src, 4),
                MoreFilters.Mezzotint(src), MoreFilters.Tiles(src, 4), MoreFilters.Despeckle(src),
            })
                using (r)
                {
                    Assert.Equal(60, r.Width);
                    Assert.Equal(40, r.Height);
                }
            using var crystal = MoreFilters.Crystallize(src, 8);
            Assert.Equal(255, crystal.GetPixel(2, 20).Red);  // the white side stays white
            using var halftone = MoreFilters.ColorHalftone(src, 4);
            Assert.Equal(new SKColor(255, 255, 255), halftone.GetPixel(10, 10).WithAlpha(255)); // no ink on white
        }
    }
}
