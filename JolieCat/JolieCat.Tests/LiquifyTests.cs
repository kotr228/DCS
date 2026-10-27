using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class LiquifyTests
    {
        /// <summary>A white 40x40 image with a red vertical bar at x 8..12.</summary>
        private static SKBitmap Bar(int size = 40)
        {
            var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            var scale = size / 40f;
            canvas.DrawRect(8 * scale, 0, 4 * scale, size, red);
            return bitmap;
        }

        private static bool IsRed(SKColor c) => c.Red > 200 && c.Green < 80;

        [Fact]
        public void UntouchedField_LeavesTheImageAlone()
        {
            using var source = Bar();
            var field = new LiquifyField(40, 40);
            Assert.True(field.IsIdentity);
            using var result = field.Apply(source);
            Assert.Equal(source.Pixels, result.Pixels);
        }

        [Fact]
        public void Push_DragsPixelsAlongTheStroke()
        {
            using var source = Bar();
            var field = new LiquifyField(40, 40);
            field.Push(10, 20, 15, 10, 0, 1);
            using var result = field.Apply(source);

            Assert.True(IsRed(result.GetPixel(16, 20)), $"{result.GetPixel(16, 20)}");    // pushed right
            Assert.True(IsRed(result.GetPixel(10, 2)), $"{result.GetPixel(10, 2)}");      // outside the brush: unchanged
            Assert.Equal(SKColors.White, result.GetPixel(30, 20));
        }

        [Fact]
        public void Bloat_GrowsWhatsUnderTheBrush_AndPuckerShrinksIt()
        {
            using var source = Bar();
            var bloat = new LiquifyField(40, 40);
            for (var i = 0; i < 10; i++) bloat.Pinch(10, 20, 12, -0.15f);
            using var bloated = bloat.Apply(source);
            Assert.True(IsRed(bloated.GetPixel(13, 20)), $"bloat {bloated.GetPixel(13, 20)}"); // bar got wider
            Assert.True(IsRed(bloated.GetPixel(10, 20)));                                      // center stays

            var pucker = new LiquifyField(40, 40);
            for (var i = 0; i < 5; i++) pucker.Pinch(10, 20, 12, 0.1f);
            using var puckered = pucker.Apply(source);
            Assert.False(IsRed(puckered.GetPixel(8, 20)), $"pucker {puckered.GetPixel(8, 20)}"); // bar got thinner
        }

        [Fact]
        public void Reconstruct_UndoesTheDistortion()
        {
            using var source = Bar();
            var field = new LiquifyField(40, 40);
            field.Push(10, 20, 15, 10, 0, 1);
            field.Twirl(25, 20, 10, 1);
            var before = field.MaxDisplacement;
            Assert.True(before > 3);

            for (var i = 0; i < 6; i++) field.Reconstruct(18, 20, 40, 1);
            Assert.True(field.MaxDisplacement < 0.05f, $"{field.MaxDisplacement}");
        }

        [Fact]
        public void Twirl_KeepsTheCenterAndTheOutside()
        {
            using var source = Bar();
            var field = new LiquifyField(40, 40);
            field.Twirl(20, 20, 10, 2);
            var (cx, cy) = field.DisplacementAt(20, 20);
            Assert.Equal(0, cx, 3);
            Assert.Equal(0, cy, 3);
            Assert.Equal((0f, 0f), field.DisplacementAt(2, 2));
        }

        [Fact]
        public void SmallField_DrivesALargerImage()
        {
            // The same stroke on a half-resolution field gives the same picture, scaled.
            using var small = Bar(40);
            using var large = Bar(80);
            var field = LiquifyField.ForImage(80, 80, maxSize: 40);
            Assert.Equal(40, field.Width);
            field.Push(10, 20, 15, 10, 0, 1);

            using var a = field.Apply(small);
            using var b = field.Apply(large);
            Assert.True(IsRed(a.GetPixel(16, 20)));
            Assert.True(IsRed(b.GetPixel(32, 40)), $"{b.GetPixel(32, 40)}");
            Assert.Equal(SKColors.White, b.GetPixel(60, 40));
        }
    }
}
