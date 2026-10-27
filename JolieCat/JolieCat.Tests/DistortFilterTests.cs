using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class DistortFilterTests
    {
        /// <summary>Left half red, right half blue, 40x40.</summary>
        private static SKBitmap Halves()
        {
            var bitmap = new SKBitmap(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 20, 40, red);
            return bitmap;
        }

        private static void AssertSame(SKBitmap a, SKBitmap b, int tolerance = 2)
        {
            var pa = a.Pixels;
            var pb = b.Pixels;
            for (var i = 0; i < pa.Length; i++)
            {
                Assert.InRange(pa[i].Red - pb[i].Red, -tolerance, tolerance);
                Assert.InRange(pa[i].Blue - pb[i].Blue, -tolerance, tolerance);
            }
        }

        [Fact]
        public void ZeroAmounts_LeaveTheImageAlone()
        {
            using var source = Halves();
            using var pinch = DistortFilters.Pinch(source, 0);
            using var wave = DistortFilters.Wave(source, 0, 50);
            using var twirl = DistortFilters.Twirl(source, 0);

            AssertSame(source, pinch);
            AssertSame(source, wave);
            AssertSame(source, twirl);
        }

        [Fact]
        public void Twirl_RotatesAroundTheCenter_ButNotTheCorners()
        {
            using var source = Halves();
            using var twirled = DistortFilters.Twirl(source, 180);

            // Near the center the halves swap sides; the corners (outside the circle) stay.
            Assert.True(twirled.GetPixel(17, 20).Blue > 200, $"{twirled.GetPixel(17, 20)}");
            Assert.Equal(SKColors.Red, twirled.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, twirled.GetPixel(39, 39));
        }

        [Fact]
        public void HighPass_OfAFlatImage_IsMidGray()
        {
            using var source = TestHelpers.Solid(20, 20, new SKColor(30, 200, 90));
            using var result = DistortFilters.HighPass(source, 5);
            var c = result.GetPixel(10, 10);
            Assert.InRange(c.Red, 126, 130);
            Assert.InRange(c.Green, 126, 130);
        }

        [Fact]
        public void SurfaceBlur_KeepsHardEdges()
        {
            using var source = Halves();
            using var result = DistortFilters.SurfaceBlur(source, 5, 20);
            Assert.Equal(SKColors.Red, result.GetPixel(19, 20));
            Assert.Equal(SKColors.Blue, result.GetPixel(20, 20));
        }

        [Fact]
        public void SurfaceBlur_SmoothsSmallDifferences()
        {
            using var source = TestHelpers.Solid(20, 20, new SKColor(100, 100, 100));
            source.SetPixel(10, 10, new SKColor(110, 110, 110));
            using var result = DistortFilters.SurfaceBlur(source, 3, 20);
            Assert.InRange(result.GetPixel(10, 10).Red, 100, 103);
        }

        [Fact]
        public void RadialBlur_LeavesTheCenter_AndSmearsTheEdges()
        {
            using var source = Halves();
            using var result = DistortFilters.RadialBlur(source, 60);
            var edge = result.GetPixel(2, 2);
            Assert.Equal(255, edge.Alpha);
            Assert.True(edge.Red > 200); // streaks run toward the center, still on the red side
        }
    }
}
