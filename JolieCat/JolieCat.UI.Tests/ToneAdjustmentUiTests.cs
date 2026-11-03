using JolieCat.Core.Actions;
using JolieCat.Core.Adjustments;
using JolieCat.UI.ViewModels;
using SkiaSharp;

namespace JolieCat.UI.Tests
{
    public class ToneAdjustmentUiTests
    {
        [Fact]
        public void SelectiveColorDialog_KeepsEachColorRangesValues()
        {
            var vm = new GenericAdjustmentViewModel(AdjustmentSettings.Default(AdjustmentKind.SelectiveColor));
            vm.ShowPage(0);                 // Reds
            vm.Sliders[2].Value = 40;       // yellow
            vm.ShowPage(4);                 // Blues
            Assert.Equal(0, vm.Sliders[2].Value);
            vm.Sliders[0].Value = -25;      // cyan
            vm.ShowPage(0);
            Assert.Equal(40, vm.Sliders[2].Value);

            var settings = vm.ToSettings();
            Assert.Equal(AdjustmentSettings.SelectiveColorRanges.Length * 4 + 1, settings.Values.Count);
            Assert.Equal(40, settings.Values[2]);
            Assert.Equal(-25, settings.Values[16]);
        }

        [Fact]
        public void ChannelMixerDialog_StartsAsIdentity_AndMonochromeIsStored()
        {
            var vm = new GenericAdjustmentViewModel(AdjustmentSettings.Default(AdjustmentKind.ChannelMixer));
            vm.ShowPage(1);                 // green output
            Assert.Equal(100, vm.Sliders[1].Value);
            vm.Fields.OfType<AdjustmentCheckField>().Single().IsChecked = true;
            var settings = vm.ToSettings();
            Assert.Equal(13, settings.Values.Count);
            Assert.Equal(1, settings.Values[12]);
        }

        [Fact]
        public void RecordedSteps_ReplayTheSameAdjustment()
        {
            using var source = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.SetPixel(0, 0, new SKColor(200, 50, 10));
            source.SetPixel(1, 0, new SKColor(30, 30, 200));

            var mixer = new AdjustmentSettings(AdjustmentKind.ChannelMixer, new double[] { 0, 0, 100, 0, 0, 100, 0, 0, 100, 0, 0, 0, 0 });
            var step = MainViewModel.StepForSettings(mixer);
            var filter = MainViewModel.FilterFor(step);
            Assert.NotNull(filter);
            using (var swapped = filter!(source))
                Assert.InRange(swapped.GetPixel(0, 0).Red, 8, 12);

            var replace = new ActionStep { Kind = "Tone", Command = "ReplaceColor" }
                .With("Color", "#C8320A").With("Fuzziness", 30).With("Hue", 180).With("Saturation", 0).With("Lightness", 0);
            using var replaced = MainViewModel.FilterFor(replace)!(source);
            Assert.NotEqual(new SKColor(200, 50, 10), replaced.GetPixel(0, 0));
            Assert.Equal(new SKColor(30, 30, 200), replaced.GetPixel(1, 0));

            var shadows = new ActionStep { Kind = "Tone", Command = "ShadowsHighlights" }.With("Shadows", 80).With("Radius", 2);
            Assert.NotNull(MainViewModel.FilterFor(shadows));
        }

        [Fact]
        public void LensAndRenderSteps_Replay()
        {
            using var source = new SKBitmap(50, 50, SKColorType.Rgba8888, SKAlphaType.Premul);
            source.Erase(new SKColor(100, 100, 100));

            var vignette = new ActionStep { Kind = "Tone", Command = "LensCorrection" }.With("Vignette", -100).With("Midpoint", 50);
            using (var dark = MainViewModel.FilterFor(vignette)!(source))
                Assert.True(dark.GetPixel(0, 0).Red < 50);

            var flare = new ActionStep { Kind = "Tone", Command = "LensFlare" }.With("Brightness", 150).With("X", 50).With("Y", 50).With("Lens", 1);
            using (var bright = MainViewModel.FilterFor(flare)!(source))
                Assert.True(bright.GetPixel(25, 25).Red > 200);

            var clouds = new FilterOptionsViewModel(FilterKind.Clouds) { Amount = 0xFF0000, SecondAmount = 0x0000FF };
            using var sky = clouds.Apply(source);
            var p = sky.GetPixel(10, 10);
            Assert.Equal(0, p.Green);
            Assert.InRange(p.Red + p.Blue, 253, 257);
        }
    }
}
