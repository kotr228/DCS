using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class CameraRawTests
    {
        /// <summary>Left half dark (40), right half bright (220), 60x30.</summary>
        private static SKBitmap DarkAndBright()
        {
            var bitmap = new SKBitmap(60, 30, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(220, 220, 220));
            using var dark = new SKPaint { Color = new SKColor(40, 40, 40) };
            canvas.DrawRect(0, 0, 30, 30, dark);
            return bitmap;
        }

        [Fact]
        public void NeutralSettings_ChangeNothing()
        {
            using var source = DarkAndBright();
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings());
            Assert.Equal(source.Pixels, result.Pixels);
        }

        [Fact]
        public void Exposure_IsInStops()
        {
            using var source = TestHelpers.Solid(10, 10, new SKColor(100, 100, 100));
            using var plus = CameraRawFilter.Apply(source, new CameraRawSettings { Exposure = 1 });
            using var minus = CameraRawFilter.Apply(source, new CameraRawSettings { Exposure = -1 });

            // sRGB 100 is ~0.127 linear; one stop up is ~0.254 linear = ~138 sRGB.
            Assert.InRange(plus.GetPixel(5, 5).Red, 134, 142);
            Assert.InRange(minus.GetPixel(5, 5).Red, 68, 76);
        }

        [Fact]
        public void Temperature_WarmsAndCools()
        {
            using var source = TestHelpers.Solid(10, 10, new SKColor(128, 128, 128));
            using var warm = CameraRawFilter.Apply(source, new CameraRawSettings { Temperature = 60 });
            using var cool = CameraRawFilter.Apply(source, new CameraRawSettings { Temperature = -60 });

            Assert.True(warm.GetPixel(5, 5).Red > warm.GetPixel(5, 5).Blue + 20);
            Assert.True(cool.GetPixel(5, 5).Blue > cool.GetPixel(5, 5).Red + 20);
        }

        [Fact]
        public void Shadows_LiftTheDarkSide_AndLeaveTheBrightSide()
        {
            using var source = DarkAndBright();
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings { Shadows = 100 });

            Assert.True(result.GetPixel(3, 15).Red > 70, $"{result.GetPixel(3, 15)}");
            Assert.InRange(result.GetPixel(56, 15).Red, 216, 224);
        }

        [Fact]
        public void NegativeHighlights_RecoverTheBrightSide_AndLeaveTheDarkSide()
        {
            using var source = DarkAndBright();
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings { Highlights = -100 });

            Assert.True(result.GetPixel(56, 15).Red < 190, $"{result.GetPixel(56, 15)}");
            Assert.InRange(result.GetPixel(3, 15).Red, 36, 44);
        }

        [Fact]
        public void Contrast_SpreadsTones_AroundTheMiddle()
        {
            using var source = DarkAndBright();
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings { Contrast = 60 });

            Assert.True(result.GetPixel(3, 15).Red < 40);
            Assert.True(result.GetPixel(56, 15).Red > 220);
        }

        [Fact]
        public void SaturationMinus100_IsGray_AndVibranceFavoursMutedColors()
        {
            using var vivid = TestHelpers.Solid(10, 10, new SKColor(230, 30, 30));
            using var gray = CameraRawFilter.Apply(vivid, new CameraRawSettings { Saturation = -100 });
            var g = gray.GetPixel(5, 5);
            Assert.True(Math.Abs(g.Red - g.Green) <= 1 && Math.Abs(g.Green - g.Blue) <= 1, $"{g}");

            using var muted = TestHelpers.Solid(10, 10, new SKColor(150, 120, 120));
            using var mutedBoost = CameraRawFilter.Apply(muted, new CameraRawSettings { Vibrance = 100 });
            using var vividBoost = CameraRawFilter.Apply(vivid, new CameraRawSettings { Vibrance = 100 });
            var mutedGain = (mutedBoost.GetPixel(5, 5).Red - mutedBoost.GetPixel(5, 5).Green) / 30.0;
            var vividGain = (vividBoost.GetPixel(5, 5).Red - vividBoost.GetPixel(5, 5).Green) / 200.0;
            Assert.True(mutedGain > vividGain, $"muted x{mutedGain:F2}, vivid x{vividGain:F2}");
        }

        [Fact]
        public void EdgeAwareSmooth_KeepsHardEdges_AndFlattensNoise()
        {
            // Left half 0.1, right half 0.9, with small noise everywhere.
            const int w = 60, h = 20;
            var input = new float[w * h];
            var random = new Random(3);
            for (var i = 0; i < input.Length; i++)
                input[i] = (i % w < 30 ? 0.1f : 0.9f) + (float)(random.NextDouble() - 0.5) * 0.04f;

            var output = CameraRawFilter.EdgeAwareSmooth(input, w, h, 6, 0.01f);

            // Right next to the edge each side keeps its level (no halo)...
            Assert.InRange(output[10 * w + 29], 0.05f, 0.16f);
            Assert.InRange(output[10 * w + 30], 0.84f, 0.95f);
            // ...and the noise is smoothed away.
            Assert.InRange(output[10 * w + 10], 0.09f, 0.11f);
        }

        [Fact]
        public void Shadows_DontLeaveAHaloAlongAnEdge()
        {
            using var source = DarkAndBright();
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings { Shadows = 100 });

            // The dark pixel at the edge is lifted about as much as one far from it.
            var atEdge = result.GetPixel(29, 15).Red;
            var farAway = result.GetPixel(3, 15).Red;
            Assert.InRange(atEdge - farAway, -12, 12);
        }

        [Fact]
        public void Transparency_IsKept()
        {
            using var source = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(SKColors.Transparent);
            source.SetPixel(5, 5, new SKColor(100, 50, 50, 128));
            using var result = CameraRawFilter.Apply(source, new CameraRawSettings { Exposure = 1, Saturation = 20 });

            Assert.Equal(0, result.GetPixel(0, 0).Alpha);
            Assert.InRange(result.GetPixel(5, 5).Alpha, 127, 129);
        }
    }
}
