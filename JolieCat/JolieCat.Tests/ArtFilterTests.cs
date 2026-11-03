using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class ArtFilterTests
    {
        /// <summary>A white image with a black vertical line through the middle.</summary>
        private static SKBitmap LineImage(int size = 100)
        {
            var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var black = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(size / 2 - 2, 0, 4, size, black);
            canvas.DrawRect(0, size / 2 - 2, size, 4, black);
            return bitmap;
        }

        [Fact]
        public void Spherize_ZeroChangesNothing_AndPositiveMagnifiesTheMiddle()
        {
            using var source = LineImage();
            using var none = ArtFilters.Spherize(source, 0);
            Assert.Equal(source.GetPixel(30, 30), none.GetPixel(30, 30));

            // Bulging: the 4 px cross in the middle gets wider.
            using var bulge = ArtFilters.Spherize(source, 100);
            Assert.True(bulge.GetPixel(53, 30).Red < 128, $"{bulge.GetPixel(53, 30)}");
            Assert.True(source.GetPixel(53, 30).Red > 128);
        }

        [Fact]
        public void Ripple_MovesTheStraightLine()
        {
            using var source = LineImage(200);
            using var rippled = ArtFilters.Ripple(source, 300, 20);
            var moved = false;
            for (var y = 0; y < 200 && !moved; y++)
                if (rippled.GetPixel(99, y).Red > 128) moved = true;
            Assert.True(moved);
        }

        [Fact]
        public void OilPaint_FlattensNoiseIntoPatches()
        {
            using var source = new SKBitmap(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
            var random = new Random(1);
            for (var y = 0; y < 60; y++)
                for (var x = 0; x < 60; x++)
                {
                    var v = (byte)(x < 30 ? 60 + random.Next(30) : 190 + random.Next(30));
                    source.SetPixel(x, y, new SKColor(v, v, v));
                }

            using var painted = ArtFilters.OilPaint(source, 4, 8);
            static double Spread(SKBitmap b)
            {
                var values = Enumerable.Range(5, 20).SelectMany(y => Enumerable.Range(5, 20).Select(x => (double)b.GetPixel(x, y).Red)).ToList();
                var mean = values.Average();
                return values.Sum(v => (v - mean) * (v - mean)) / values.Count;
            }
            Assert.True(Spread(painted) < Spread(source) / 2);
            Assert.True(painted.GetPixel(10, 30).Red < 128 && painted.GetPixel(50, 30).Red > 128); // halves kept
        }

        [Fact]
        public void SmartSharpen_SharpensEdges_ButNotFlatAreas()
        {
            using var source = new SKBitmap(40, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(source))
            {
                canvas.Clear(new SKColor(100, 100, 100));
                using var light = new SKPaint { Color = new SKColor(160, 160, 160) };
                canvas.DrawRect(20, 0, 20, 10, light);
            }
            using var sharp = ArtFilters.SmartSharpen(source, 200, 2);
            Assert.True(sharp.GetPixel(20, 5).Red > 165);       // bright side of the edge brighter
            Assert.True(sharp.GetPixel(19, 5).Red < 95);        // dark side darker
            Assert.Equal(100, sharp.GetPixel(3, 5).Red);        // flat area untouched
        }

        [Fact]
        public void Clouds_MixTheTwoColors_AndRepeatForTheSameSeed()
        {
            using var source = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var a = ArtFilters.Clouds(source, SKColors.Black, SKColors.White, 5);
            using var b = ArtFilters.Clouds(source, SKColors.Black, SKColors.White, 5);
            Assert.Equal(a.GetPixel(10, 10), b.GetPixel(10, 10));
            var values = a.Pixels.Select(p => (int)p.Red).ToList();
            Assert.True(values.Max() - values.Min() > 60);
            Assert.All(a.Pixels, p => Assert.Equal(255, p.Alpha));
        }

        [Fact]
        public void LensFlare_BrightensAroundTheSource()
        {
            using var source = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(40, 40, 60));
            using var flared = ArtFilters.LensFlare(source, 0.3f, 0.3f, 100, 0);
            Assert.True(flared.GetPixel(30, 30).Red > 200);
            Assert.True(flared.GetPixel(95, 5).Red < 80);
        }

        [Fact]
        public void LensCorrection_VignetteDarkensCorners_AndZeroChangesNothing()
        {
            using var source = new SKBitmap(80, 80, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(150, 150, 150));
            using var same = ArtFilters.LensCorrection(source, 0, 0, 0, 0, 50);
            Assert.Equal(source.GetPixel(2, 2), same.GetPixel(2, 2));

            using var dark = ArtFilters.LensCorrection(source, 0, 0, 0, -100, 50);
            Assert.True(dark.GetPixel(1, 1).Red < 60);
            Assert.InRange(dark.GetPixel(40, 40).Red, 148, 152);
        }

        [Fact]
        public void LensCorrection_RemovesBarrelDistortion()
        {
            // Positive Remove Distortion samples further out near the edges, so a line
            // near the edge moves toward the center.
            using var source = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(source))
            {
                canvas.Clear(SKColors.White);
                using var black = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(88, 0, 3, 100, black);
            }
            using var fixedImage = ArtFilters.LensCorrection(source, -100, 0, 0, 0, 50);
            var column = Enumerable.Range(0, 100).First(x => fixedImage.GetPixel(x, 50).Red < 128);
            Assert.NotEqual(88, column);
        }
    }
}
