using JolieCat.Core.Filters;
using SkiaSharp;
using Xunit;

namespace JolieCat.Tests
{
    public class VanishingPointTests
    {
        private static readonly SKPoint[] Floor =
        {
            new(80, 40), new(120, 40),      // far edge: short
            new(190, 140), new(10, 140),    // near edge: long
        };

        [Fact]
        public void SquareToQuad_HitsTheCorners()
        {
            var m = VanishingPoint.SquareToQuad(Floor);
            var unit = new[] { new SKPoint(0, 0), new SKPoint(1, 0), new SKPoint(1, 1), new SKPoint(0, 1) };
            for (var i = 0; i < 4; i++)
            {
                var p = m.MapPoint(unit[i]);
                Assert.Equal(Floor[i].X, p.X, 2);
                Assert.Equal(Floor[i].Y, p.Y, 2);
            }
            // The middle of the floor is nearer the far edge on screen (perspective).
            Assert.True(m.MapPoint(new SKPoint(0.5f, 0.5f)).Y < 90);
        }

        [Fact]
        public void Tile_FillsTheSurface_AndNothingOutside()
        {
            using var red = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
            red.Erase(SKColors.Red);
            using var layer = VanishingPoint.Render(200, 150, Floor, red, 0.5f, 0.5f, 0.1f, tile: true);
            Assert.Equal(SKColors.Red, layer.GetPixel(100, 120));
            Assert.Equal(SKColors.Red, layer.GetPixel(100, 50));
            Assert.Equal(0, layer.GetPixel(10, 50).Alpha);     // outside the floor
            Assert.Equal(0, layer.GetPixel(100, 145).Alpha);
        }

        [Fact]
        public void Placed_OnlyCoversItsSpot()
        {
            using var red = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
            red.Erase(SKColors.Red);
            var square = new[] { new SKPoint(0, 0), new SKPoint(100, 0), new SKPoint(100, 100), new SKPoint(0, 100) };
            using var layer = VanishingPoint.Render(100, 100, square, red, 0.25f, 0.25f, 0.2f, tile: false);
            Assert.Equal(SKColors.Red, layer.GetPixel(25, 25));
            Assert.Equal(0, layer.GetPixel(75, 75).Alpha);
            Assert.Equal(0, layer.GetPixel(25, 40).Alpha);     // 20 px tall around y 25
        }
    }
}
