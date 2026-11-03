using JolieCat.Core.Documents;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class SelectionOpsTests
    {
        [Fact]
        public void FromAlpha_SelectsTheOpaquePixels()
        {
            using var bitmap = new SKBitmap(30, 20, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(bitmap))
            using (var red = new SKPaint { Color = SKColors.Red })
                canvas.DrawRect(5, 4, 10, 6, red);

            using var path = SelectionOps.FromAlpha(bitmap)!;
            Assert.Equal(new SKRect(5, 4, 15, 10), path.Bounds);
            Assert.Null(SelectionOps.FromAlpha(new SKBitmap(4, 4)));
        }

        /// <summary>Two separate light-gray squares and a dark one on black.</summary>
        private static SKBitmap Squares()
        {
            var bitmap = new SKBitmap(60, 20, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            using var light = new SKPaint { Color = new SKColor(200, 200, 200) };
            using var lighter = new SKPaint { Color = new SKColor(210, 210, 210) };
            canvas.DrawRect(0, 0, 10, 10, light);
            canvas.DrawRect(10, 0, 10, 10, lighter);   // touching the first
            canvas.DrawRect(40, 0, 10, 10, light);     // separate
            return bitmap;
        }

        [Fact]
        public void Grow_SpreadsToTouchingSimilarColors_Only()
        {
            using var image = Squares();
            using var selected = new SKRegion(new SKRectI(2, 2, 4, 4));
            using var path = SelectionOps.Grow(image, selected, 16)!;
            Assert.Equal(new SKRect(0, 0, 20, 10), path.Bounds);   // both touching squares, not the far one
        }

        [Fact]
        public void Similar_TakesMatchingColorsAnywhere()
        {
            using var image = Squares();
            using var selected = new SKRegion(new SKRectI(2, 2, 4, 4));
            using var path = SelectionOps.Similar(image, selected, 16)!;
            Assert.Equal(new SKRect(0, 0, 50, 10), path.Bounds);
            Assert.False(path.Contains(30, 5));                     // the black between stays out
        }

        [Fact]
        public void Refine_ShiftsTheEdge_AndSmoothsAwaySpecks()
        {
            using var square = new SKPath();
            square.AddRect(new SKRect(20, 20, 60, 60));
            square.AddRect(new SKRect(80, 20, 82, 22));    // a 2-px speck

            using var grown = SelectionOps.Refine(square, 100, 100, 0, 5)!;
            Assert.InRange(grown.Bounds.Left, 14, 16);
            using var shrunk = SelectionOps.Refine(square, 100, 100, 0, -5)!;
            Assert.InRange(shrunk.Bounds.Left, 24, 26);

            using var smoothed = SelectionOps.Refine(square, 100, 100, 8, 0)!;
            Assert.False(smoothed.Contains(81, 21));        // the speck is gone
            Assert.True(smoothed.Contains(40, 40));
            Assert.False(smoothed.Contains(21, 21));        // the corner got rounded
        }

        [Fact]
        public void TrimBounds_ByTransparency_OrByBorderColor()
        {
            using var image = Squares();
            Assert.Equal(new SKRectI(0, 0, 60, 20), SelectionOps.TrimBounds(image, null));
            Assert.Equal(new SKRectI(0, 0, 50, 10), SelectionOps.TrimBounds(image, SKColors.Black));
        }
    }
}
