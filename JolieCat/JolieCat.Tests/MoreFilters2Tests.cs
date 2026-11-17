using System.Linq;
using JolieCat.Core.Filters;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class MoreFilters2Tests
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
        public void AllKeepTheSize()
        {
            using var src = Halves();
            foreach (var r in new[]
            {
                MoreFilters2.ZigZag(src, 50, 5), MoreFilters2.Shear(src, 40), MoreFilters2.SpinBlur(src, 20),
                MoreFilters2.ReduceNoise(src, 5, 50), MoreFilters2.SharpenEdges(src),
                MoreFilters2.Fibers(src, SKColors.Black, SKColors.White, 16, 4, 1), MoreFilters2.GlowingEdges(src, 6), MoreFilters2.Cutout(src, 4),
            })
                using (r)
                {
                    Assert.Equal(60, r.Width);
                    Assert.Equal(40, r.Height);
                }
        }

        [Fact]
        public void GlowingEdges_LightsTheEdgeOnly()
        {
            using var src = Halves();
            using var r = MoreFilters2.GlowingEdges(src, 6);
            Assert.True(r.GetPixel(30, 20).Red > 200);     // the edge glows
            Assert.Equal(0, r.GetPixel(10, 20).Red);       // flat parts are black
            Assert.Equal(0, r.GetPixel(50, 20).Red);
        }

        [Fact]
        public void Shear_SlidesTheMiddleMost()
        {
            using var src = Halves();
            using var r = MoreFilters2.Shear(src, 20);    // middle rows slide 6 px right
            Assert.Equal(255, r.GetPixel(33, 20).Red);
            Assert.Equal(0, r.GetPixel(33, 0).Red);       // the top row stays
        }

        [Fact]
        public void Fibers_UseOnlyTheTwoColorsRange()
        {
            using var src = Halves();
            using var r = MoreFilters2.Fibers(src, new SKColor(200, 0, 0), new SKColor(0, 0, 200), 32, 8, 7);
            Assert.All(r.Pixels, c => { Assert.Equal(0, c.Green); Assert.InRange(c.Red + c.Blue, 195, 205); });
            Assert.True(r.Pixels.Select(c => c.Red).Distinct().Count() > 5);
        }

        [Fact]
        public void Custom_Kernel_AndParsing()
        {
            using var src = Halves();
            var identity = MoreFilters2.ParseKernel("0 0 0; 0 1 0; 0 0 0")!;
            using (var same = MoreFilters2.Custom(src, identity, 1, 0))
                Assert.Equal(src.GetPixel(29, 5), same.GetPixel(29, 5));
            var edges = MoreFilters2.ParseKernel("-1 -1 -1\n-1 8 -1\n-1 -1 -1")!;
            using (var e = MoreFilters2.Custom(src, edges, 1, 0))
            {
                Assert.Equal(0, e.GetPixel(10, 10).Red);       // flat: nothing
                Assert.Equal(255, e.GetPixel(29, 10).Red);     // the edge lights up
            }
            Assert.Null(MoreFilters2.ParseKernel("1 2; 3 4"));      // even size
            Assert.Null(MoreFilters2.ParseKernel("1 2 3; 4 5"));    // ragged
            Assert.Null(MoreFilters2.ParseKernel("a b c; 1 2 3; 1 2 3"));
        }

        [Fact]
        public void Displace_GrayStays_WhiteShifts()
        {
            using var src = Halves();                                // white left half (0..29), black right
            using var gray = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            gray.Erase(new SKColor(128, 128, 128));
            using (var same = MoreFilters2.Displace(src, gray, 20, 20))
                Assert.Equal(255, same.GetPixel(25, 20).Red);
            using var white = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            white.Erase(SKColors.White);
            using (var moved = MoreFilters2.Displace(src, white, 10, 0))  // everything moves ~10 px right
            {
                Assert.Equal(255, moved.GetPixel(37, 20).Red);
                Assert.Equal(0, moved.GetPixel(42, 20).Red);
            }
        }
    }
}
