using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class SceneDetectTests
    {
        /// <summary>Blue gradient sky over a dark textured hill.</summary>
        private static SKBitmap Landscape()
        {
            var bitmap = new SKBitmap(200, 150, SKColorType.Rgba8888, SKAlphaType.Premul);
            var random = new Random(2);
            for (var y = 0; y < 150; y++)
                for (var x = 0; x < 200; x++)
                {
                    var hill = 80 + 15 * Math.Sin(x / 25.0);
                    bitmap.SetPixel(x, y, y < hill
                        ? new SKColor((byte)(60 + y / 2), (byte)(120 + y / 2), 230)
                        : new SKColor((byte)(30 + random.Next(40)), (byte)(60 + random.Next(50)), (byte)(20 + random.Next(30))));
                }
            return bitmap;
        }

        [Fact]
        public void SkyMask_FindsTheSkyAndNotTheHill()
        {
            using var photo = Landscape();
            var mask = SceneDetect.SkyMask(photo);
            Assert.Equal(255, mask[20 * 200 + 100]);    // high in the sky
            Assert.Equal(0, mask[130 * 200 + 100]);     // in the hill
        }

        [Fact]
        public void ReplaceSky_ChangesTheSkyOnly()
        {
            using var photo = Landscape();
            using var sunset = SceneDetect.RenderSky("Sunset", 100, 80);
            using var result = SceneDetect.ReplaceSky(photo, sunset, 0, 2, 0);
            var sky = result.GetPixel(100, 10);
            Assert.True(sky.Blue < 200, $"new sky {sky}");                // no longer the old blue
            Assert.Equal(photo.GetPixel(100, 140), result.GetPixel(100, 140)); // hill untouched (no lighting)
        }

        [Fact]
        public void FocusMask_PicksTheSharpSubject()
        {
            using var photo = new SKBitmap(200, 120, SKColorType.Rgba8888, SKAlphaType.Premul);
            var random = new Random(4);
            for (var y = 0; y < 120; y++)
                for (var x = 0; x < 200; x++)
                {
                    var inSubject = x is > 70 and < 130 && y is > 30 and < 90;
                    var v = inSubject ? random.Next(256) : 120 + (int)(10 * Math.Sin(x / 30.0)); // sharp detail vs smooth blur
                    photo.SetPixel(x, y, new SKColor((byte)v, (byte)v, (byte)v));
                }
            var mask = SceneDetect.FocusMask(photo, 50);
            Assert.Equal(255, mask[60 * 200 + 100]);
            Assert.Equal(0, mask[10 * 200 + 10]);
        }
    }
}
