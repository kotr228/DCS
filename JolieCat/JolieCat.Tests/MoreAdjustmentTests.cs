using JolieCat.Core.Adjustments;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class MoreAdjustmentTests
    {
        private static SKColor Apply(AdjustmentSettings settings, SKColor input)
        {
            using var source = TestHelpers.Solid(1, 1, input);
            using var result = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(result))
            using (var filter = settings.CreateColorFilter())
            using (var paint = new SKPaint { ColorFilter = filter })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, 0, 0, paint);
            }
            return result.GetPixel(0, 0);
        }

        [Fact]
        public void Exposure_PlusOneStop_DoublesBrightness()
        {
            var c = Apply(new AdjustmentSettings(AdjustmentKind.Exposure, new[] { 1.0, 0, 1 }), new SKColor(60, 60, 60));
            Assert.InRange(c.Red, 118, 122);
        }

        [Fact]
        public void Exposure_Default_ChangesNothing()
        {
            var c = Apply(AdjustmentSettings.Default(AdjustmentKind.Exposure), new SKColor(37, 140, 222));
            Assert.Equal(new SKColor(37, 140, 222), c);
        }

        [Fact]
        public void BlackWhite_MakesGray_WeightedByChannel()
        {
            var settings = new AdjustmentSettings(AdjustmentKind.BlackWhite, new[] { 100.0, 0, 0 }); // red channel only
            var c = Apply(settings, new SKColor(200, 10, 10));
            Assert.InRange(c.Red, 198, 202);
            Assert.Equal(c.Red, c.Green);
            Assert.Equal(c.Red, c.Blue);
        }

        [Fact]
        public void ColorBalance_Midtones_TouchesMidtonesMost()
        {
            var values = new double[9];
            values[3] = 100; // midtones toward red
            var settings = new AdjustmentSettings(AdjustmentKind.ColorBalance, values);

            var mid = Apply(settings, new SKColor(128, 128, 128));
            var black = Apply(settings, SKColors.Black);

            Assert.True(mid.Red > 200, $"mid {mid}");
            Assert.Equal(128, mid.Green);
            Assert.True(black.Red < 5);
        }

        [Fact]
        public void PhotoFilter_WarmsTheImage()
        {
            var c = Apply(AdjustmentSettings.Default(AdjustmentKind.PhotoFilter), new SKColor(128, 128, 128));
            Assert.True(c.Red > c.Blue + 20, $"{c}");
        }

        [Fact]
        public void GradientMap_MapsBrightnessToColors()
        {
            var settings = new AdjustmentSettings(AdjustmentKind.GradientMap,
                new[] { AdjustmentSettings.PackColor(SKColors.Red), AdjustmentSettings.PackColor(SKColors.Blue) });

            Assert.Equal(SKColors.Red, Apply(settings, SKColors.Black));
            Assert.Equal(SKColors.Blue, Apply(settings, SKColors.White));
        }

        [Fact]
        public void NewKinds_RoundTripThroughSerialization()
        {
            foreach (var kind in new[] { AdjustmentKind.Exposure, AdjustmentKind.BlackWhite, AdjustmentKind.ColorBalance, AdjustmentKind.PhotoFilter, AdjustmentKind.GradientMap })
            {
                var settings = AdjustmentSettings.Default(kind);
                var back = AdjustmentSettings.Deserialize(settings.Serialize())!;
                Assert.Equal(kind, back.Kind);
                Assert.Equal(settings.Values, back.Values);
            }
        }
    }
}
