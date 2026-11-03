using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class HealingBlendTests
    {
        [Fact]
        public void APatchTakesOnTheSurroundingColor_ButKeepsItsTexture()
        {
            const int w = 40, h = 40;
            // Target: flat mid-gray with a dark blemish in the middle.
            var target = new SKColor[w * h];
            Array.Fill(target, new SKColor(120, 120, 120));
            for (var y = 15; y < 25; y++)
                for (var x = 15; x < 25; x++)
                    target[y * w + x] = new SKColor(20, 20, 20);

            // Source: brighter (200) with a fine stripe texture (+-10 every other column).
            var source = new SKColor[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var v = (byte)(x % 2 == 0 ? 210 : 190);
                    source[y * w + x] = new SKColor(v, v, v);
                }

            var region = new bool[w * h];
            for (var y = 13; y < 27; y++)
                for (var x = 13; x < 27; x++)
                    region[y * w + x] = true;

            var healed = HealingBlend.Blend(target, source, w, h, region);

            // Brightness now matches the surroundings (~120), not the source (200)...
            var center = healed[20 * w + 20].Red;
            var next = healed[20 * w + 21].Red;
            Assert.InRange((center + next) / 2.0, 112, 128);
            // ...the source's texture survives (neighbours still ~20 apart)...
            Assert.InRange(Math.Abs(center - next), 15, 25);
            // ...the blemish is gone, and nothing outside the region changed.
            Assert.True(healed[20 * w + 20].Red > 90);
            Assert.Equal(target[5 * w + 5], healed[5 * w + 5]);
        }

        [Fact]
        public void AnEmptyRegion_ChangesNothing()
        {
            var target = new[] { SKColors.Red, SKColors.Blue, SKColors.Lime, SKColors.White };
            var healed = HealingBlend.Blend(target, new SKColor[4], 2, 2, new bool[4]);
            Assert.Equal(target, healed);
        }
    }
}
