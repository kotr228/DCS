using JolieCat.Core.Adjustments;
using JolieCat.Core.Filters;
using SkiaSharp;

namespace JolieCat.Tests
{
    public class ToneAdjustmentTests
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
        public void ChannelMixer_Default_ChangesNothing()
        {
            var c = Apply(AdjustmentSettings.Default(AdjustmentKind.ChannelMixer), new SKColor(37, 140, 222));
            Assert.Equal(new SKColor(37, 140, 222), c);
        }

        [Fact]
        public void ChannelMixer_SwapsChannels_AndAddsConstant()
        {
            // Red from blue, green from green plus 20%, blue from red.
            var settings = new AdjustmentSettings(AdjustmentKind.ChannelMixer,
                new double[] { 0, 0, 100, 0, 0, 100, 0, 20, 100, 0, 0, 0, 0 });
            var c = Apply(settings, new SKColor(200, 50, 10));
            Assert.InRange(c.Red, 8, 12);
            Assert.InRange(c.Green, 99, 103);   // 50 + 0.2 * 255
            Assert.InRange(c.Blue, 198, 202);
        }

        [Fact]
        public void ChannelMixer_Monochrome_IsGray()
        {
            var settings = new AdjustmentSettings(AdjustmentKind.ChannelMixer,
                new double[] { 50, 50, 0, 0, 0, 100, 0, 0, 0, 0, 100, 0, 1 });
            var c = Apply(settings, new SKColor(200, 100, 10));
            Assert.Equal(c.Red, c.Green);
            Assert.Equal(c.Red, c.Blue);
            Assert.InRange(c.Red, 148, 152);
        }

        private static AdjustmentSettings Selective(string range, double cyan, double magenta, double yellow, double black, bool absolute = false)
        {
            var values = AdjustmentSettings.Default(AdjustmentKind.SelectiveColor).Values.ToArray();
            var at = System.Array.IndexOf(AdjustmentSettings.SelectiveColorRanges, range) * 4;
            values[at] = cyan; values[at + 1] = magenta; values[at + 2] = yellow; values[at + 3] = black;
            values[^1] = absolute ? 1 : 0;
            return new AdjustmentSettings(AdjustmentKind.SelectiveColor, values);
        }

        [Fact]
        public void SelectiveColor_Default_ChangesNothing()
        {
            var c = Apply(AdjustmentSettings.Default(AdjustmentKind.SelectiveColor), new SKColor(37, 140, 222));
            Assert.InRange(c.Red, 36, 38);
            Assert.InRange(c.Green, 139, 141);
            Assert.InRange(c.Blue, 221, 223);
        }

        [Fact]
        public void SelectiveColor_Reds_ChangesRedsButNotBlues()
        {
            var settings = Selective("Reds", 0, 0, 100, 0, absolute: true); // more yellow ink = less blue
            var red = Apply(settings, new SKColor(220, 40, 120));
            var blue = Apply(settings, new SKColor(30, 60, 220));
            Assert.True(red.Blue < 60, $"red pixel's blue {red.Blue}");
            Assert.Equal(220, blue.Blue);
        }

        [Fact]
        public void SelectiveColor_Relative_LeavesWhiteAlone()
        {
            var c = Apply(Selective("Whites", 100, 0, 0, 0), SKColors.White);
            Assert.Equal(255, c.Red);
            var absolute = Apply(Selective("Whites", 100, 0, 0, 0, absolute: true), SKColors.White);
            Assert.True(absolute.Red < 30);
        }

        [Fact]
        public void SelectiveColor_HalfTransparentPixel_KeepsItsColor()
        {
            using var source = TestHelpers.Solid(1, 1, new SKColor(37, 140, 222, 128));
            using var result = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(result))
            using (var filter = AdjustmentSettings.Default(AdjustmentKind.SelectiveColor).CreateColorFilter())
            using (var paint = new SKPaint { ColorFilter = filter })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, 0, 0, paint);
            }
            var c = result.GetPixel(0, 0);
            Assert.InRange(c.Alpha, 127, 129);
            Assert.InRange(c.Green, 136, 144);
        }

        [Fact]
        public void ColorLookup_LooksChangeColor_AndZeroIntensityChangesNothing()
        {
            var gray = new SKColor(128, 128, 128);
            var warm = Apply(new AdjustmentSettings(AdjustmentKind.ColorLookup, new[] { 0.0, 100 }), gray);
            Assert.True(warm.Red > warm.Blue + 10, $"warm {warm}");

            var noir = Apply(new AdjustmentSettings(AdjustmentKind.ColorLookup, new[] { 5.0, 100 }), new SKColor(200, 60, 40));
            Assert.InRange(noir.Red - noir.Blue, -2, 2);

            var none = Apply(new AdjustmentSettings(AdjustmentKind.ColorLookup, new[] { 2.0, 0 }), new SKColor(37, 140, 222));
            Assert.InRange(none.Red, 36, 38);
            Assert.InRange(none.Blue, 221, 223);
        }

        [Fact]
        public void Equalize_StretchesANarrowRange()
        {
            using var source = new SKBitmap(4, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.SetPixel(0, 0, new SKColor(100, 100, 100));
            source.SetPixel(1, 0, new SKColor(110, 110, 110));
            source.SetPixel(2, 0, new SKColor(120, 120, 120));
            source.SetPixel(3, 0, new SKColor(130, 130, 130));
            using var result = ToneFilters.Equalize(source);
            Assert.Equal(0, result.GetPixel(0, 0).Red);
            Assert.Equal(255, result.GetPixel(3, 0).Red);
            Assert.InRange(result.GetPixel(1, 0).Red, 80, 90);
        }

        [Fact]
        public void ShadowsHighlights_LiftsDarkAreas_AndKeepsBrightOnes()
        {
            using var source = new SKBitmap(40, 20, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(source))
            {
                canvas.Clear(new SKColor(30, 30, 30));
                using var light = new SKPaint { Color = new SKColor(220, 220, 220) };
                canvas.DrawRect(20, 0, 20, 20, light);
            }
            using var result = ToneFilters.ShadowsHighlights(source, 50, 0, 50, 4);
            Assert.True(result.GetPixel(5, 10).Red > 60, $"shadow {result.GetPixel(5, 10).Red}");
            Assert.InRange(result.GetPixel(35, 10).Red, 218, 222);
        }

        [Fact]
        public void ReplaceColor_ChangesOnlyTheChosenColor()
        {
            using var source = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.SetPixel(0, 0, new SKColor(200, 30, 30));
            source.SetPixel(1, 0, new SKColor(30, 30, 200));
            using var result = ToneFilters.ReplaceColor(source, new SKColor(200, 30, 30), 40, 120, 0, 0);
            var was = result.GetPixel(0, 0);
            Assert.True(was.Green > was.Red, $"shifted {was}");
            Assert.Equal(new SKColor(30, 30, 200), result.GetPixel(1, 0));
        }
    }
}
