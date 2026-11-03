using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class SeamCarvingTests
    {
        /// <summary>Flat blue sky (200x60) with a 20-px red square at x 30..50 and another at 140..160.</summary>
        private static SKBitmap Scene()
        {
            var bitmap = new SKBitmap(200, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(120, 170, 230));
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(30, 20, 20, 20, red);
            canvas.DrawRect(140, 20, 20, 20, red);
            return bitmap;
        }

        private static int RedWidth(SKBitmap b, int y)
        {
            var n = 0;
            for (var x = 0; x < b.Width; x++) if (b.GetPixel(x, y) == SKColors.Red) n++;
            return n;
        }

        [Fact]
        public void Narrowing_TakesTheSky_AndKeepsTheSquaresWhole()
        {
            using var image = Scene();
            var plan = SeamCarvingPlan.Create(image, 120, 60);
            using var result = plan.Apply(image);

            Assert.Equal(120, result.Width);
            Assert.Equal(40, RedWidth(result, 30));     // both 20-px squares survive in full
        }

        [Fact]
        public void Widening_RepeatsTheSky()
        {
            using var image = Scene();
            using var result = SeamCarvingPlan.Create(image, 260, 60).Apply(image);
            Assert.Equal(260, result.Width);
            Assert.Equal(40, RedWidth(result, 30));
        }

        [Fact]
        public void Height_CanChangeToo()
        {
            using var image = Scene();
            using var result = SeamCarvingPlan.Create(image, 200, 40).Apply(image);
            Assert.Equal(40, result.Height);
            var redRows = 0;
            for (var y = 0; y < 40; y++) if (result.GetPixel(40, y) == SKColors.Red) redRows++;
            Assert.Equal(20, redRows);
        }

        [Fact]
        public void ProtectedPixels_AreKept_EvenWhenFlat()
        {
            // A plain image: without protection any column could go. Protect x 0..100.
            using var flat = TestHelpers.Solid(200, 20, SKColors.Gray);
            for (var y = 0; y < 20; y++) flat.SetPixel(10, y, SKColors.Red);   // a marker in the protected half
            var protect = new bool[200 * 20];
            for (var y = 0; y < 20; y++) for (var x = 0; x < 100; x++) protect[y * 200 + x] = true;

            using var result = SeamCarvingPlan.Create(flat, 120, 20, protect).Apply(flat);
            Assert.Equal(SKColors.Red, result.GetPixel(10, 5));             // nothing removed left of it
        }
    }
}
