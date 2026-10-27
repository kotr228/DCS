using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class FilterTests
    {
        [Fact]
        public void Invert_TwiceIsIdentity()
        {
            using var source = TestHelpers.Solid(4, 4, new SKColor(10, 120, 240));
            using var once = StylizeFilters.Invert(source);
            using var twice = StylizeFilters.Invert(once);

            Assert.Equal(new SKColor(245, 135, 15), once.GetPixel(0, 0));
            Assert.Equal(source.GetPixel(0, 0), twice.GetPixel(0, 0));
        }

        [Fact]
        public void Desaturate_ProducesGray()
        {
            using var source = TestHelpers.Solid(2, 2, new SKColor(255, 0, 0));
            using var result = StylizeFilters.Desaturate(source);
            var p = result.GetPixel(0, 0);

            Assert.Equal(p.Red, p.Green);
            Assert.Equal(p.Green, p.Blue);
        }

        [Fact]
        public void Threshold_SplitsAtLevel()
        {
            using var dark = TestHelpers.Solid(2, 2, new SKColor(40, 40, 40));
            using var light = TestHelpers.Solid(2, 2, new SKColor(200, 200, 200));
            using var darkResult = StylizeFilters.Threshold(dark, 128);
            using var lightResult = StylizeFilters.Threshold(light, 128);

            Assert.Equal(SKColors.Black, darkResult.GetPixel(0, 0));
            Assert.Equal(SKColors.White, lightResult.GetPixel(0, 0));
        }

        [Fact]
        public void Posterize_TwoLevels_GivesOnlyExtremes()
        {
            using var source = TestHelpers.Solid(2, 2, new SKColor(100, 160, 30));
            using var result = StylizeFilters.Posterize(source, 2);
            var p = result.GetPixel(0, 0);

            Assert.All(new[] { p.Red, p.Green, p.Blue }, v => Assert.True(v is 0 or 255));
        }

        [Fact]
        public void Pixelate_MakesUniformBlocks()
        {
            using var source = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(source))
            {
                canvas.Clear(SKColors.Black);
                using var white = new SKPaint { Color = SKColors.White };
                canvas.DrawRect(0, 0, 2, 4, white); // half of the first 4x4 block
            }

            using var result = StylizeFilters.Pixelate(source, 4);

            var first = result.GetPixel(0, 0);
            Assert.Equal(first, result.GetPixel(3, 3));
            Assert.InRange(first.Red, 120, 135); // the block's average
            Assert.Equal(SKColors.Black, result.GetPixel(6, 6));
        }

        [Fact]
        public void Filters_KeepTransparentPixelsTransparent()
        {
            using var source = TestHelpers.Solid(4, 4, SKColors.Transparent);
            using var inverted = StylizeFilters.Invert(source);
            Assert.Equal(0, inverted.GetPixel(1, 1).Alpha);
        }

        [Fact]
        public void BoxBlur_OfUniformImageIsUnchanged()
        {
            using var source = TestHelpers.Solid(20, 20, new SKColor(90, 90, 90));
            using var blurred = BlurFilter.Apply(source, BlurType.Box, 3);
            var p = blurred.GetPixel(10, 10);

            Assert.InRange(p.Red, 88, 92);
        }
    }
}
